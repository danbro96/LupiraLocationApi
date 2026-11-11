using Lupira.Results;
using LupiraLocationApi.Core.Domain.Telemetry;
using LupiraLocationApi.Core.Dtos.Location;
using Marten;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace LupiraLocationApi.Core.Application.Telemetry;

/// <summary>Derives Visits (stay-points), Trips (movement between stays), and DailyLocationSummaries (per local day) from
/// raw fixes, and materializes them as Marten docs in the <c>location</c> schema (so they survive raw-GPS retention drop).
/// A rollup window reprocesses from the arrival of the stay in progress at its start, so stays and trips crossing midnight
/// stay whole. Idempotent — re-running a window replaces that device's docs in it.</summary>
public sealed class TripVisitService(NpgsqlDataSource db, IDocumentSession session, PlaceLabelService labels, IOptions<LocationRollupOptions> options)
{
    /// <summary>Bump when visit/trip derivation changes; the maintenance pass then rebuilds the retention window.</summary>
    public const int AlgorithmVersion = 3;

    private static readonly TimeSpan AnchorReach = TimeSpan.FromHours(12);

    private readonly TimeZoneInfo _tz = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZoneId);

    public TimeZoneInfo TimeZone => _tz;

    public async Task<OpResult> RollupAsync(Guid principalId, Guid deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var reach = from - AnchorReach;
        var anchor = await session.Query<LocationVisit>()
            .Where(v => v.PrincipalId == principalId && v.DeviceId == deviceId && v.ArriveTs < from && v.DepartTs >= reach)
            .OrderByDescending(v => v.ArriveTs)
            .FirstOrDefaultAsync(ct);
        var start = anchor?.ArriveTs ?? from;
        var points = await ReadPointsAsync(principalId, deviceId, start, to, ct);

        session.DeleteWhere<LocationVisit>(v => v.PrincipalId == principalId && v.DeviceId == deviceId && v.ArriveTs >= start && v.ArriveTs < to);
        session.DeleteWhere<LocationTrip>(t => t.PrincipalId == principalId && t.DeviceId == deviceId && t.StartTs >= start && t.StartTs < to);

        var stays = StayPointDetector.Detect(points);
        var visits = new List<LocationVisit>(stays.Count);
        foreach (var s in stays)
        {
            visits.Add(new LocationVisit
            {
                Id = Guid.NewGuid(),
                PrincipalId = principalId,
                DeviceId = deviceId,
                ArriveTs = s.Arrive,
                DepartTs = s.Depart,
                CentroidLat = s.Lat,
                CentroidLon = s.Lon,
                RadiusM = s.RadiusM,
                SampleCount = s.Count,
                MaxGapS = s.MaxGapS,
                PlaceLabel = await labels.ResolveAsync(s.Lat, s.Lon, ct),
            });
        }

        var trips = BuildTrips(principalId, deviceId, stays, visits, points);

        foreach (var v in visits) session.Store(v);
        foreach (var t in trips) session.Store(t);
        for (var day = LocalDay.Of(from, _tz); LocalDay.StartUtc(day, _tz) < to; day = day.AddDays(1))
            session.Store(BuildSummary(principalId, deviceId, day, points, visits, trips));
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    public async Task<OpResult<List<LocationVisitDto>>> VisitsAsync(Guid pid, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var visits = await session.Query<LocationVisit>()
            .Where(v => v.PrincipalId == pid && v.ArriveTs < to && v.DepartTs > from)
            .OrderBy(v => v.ArriveTs).ToListAsync(ct);
        return OpResult<List<LocationVisitDto>>.Ok(visits
            .Select(v => new LocationVisitDto
            {
                Id = v.Id,
                ArriveTs = v.ArriveTs,
                DepartTs = v.DepartTs,
                Lat = v.CentroidLat,
                Lon = v.CentroidLon,
                RadiusM = v.RadiusM,
                SampleCount = v.SampleCount,
                MaxGapS = v.MaxGapS,
                PlaceLabel = v.PlaceLabel,
            })
            .ToList());
    }

    public async Task<OpResult<List<LocationTripDto>>> TripsAsync(Guid pid, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var trips = await session.Query<LocationTrip>()
            .Where(t => t.PrincipalId == pid && t.StartTs < to && t.EndTs > from)
            .OrderBy(t => t.StartTs).ToListAsync(ct);
        return OpResult<List<LocationTripDto>>.Ok(trips
            .Select(t => new LocationTripDto
            {
                Id = t.Id,
                StartTs = t.StartTs,
                EndTs = t.EndTs,
                DistanceM = t.DistanceM,
                DurationS = t.DurationS,
                DominantActivity = t.DominantActivity,
                AvgSpeedMps = t.AvgSpeedMps,
                MaxSpeedMps = t.MaxSpeedMps,
                FromVisitId = t.FromVisitId,
                ToVisitId = t.ToVisitId,
            })
            .ToList());
    }

    public async Task<OpResult<DailyLocationSummaryDto>> SummaryAsync(Guid pid, DateOnly date, CancellationToken ct = default)
    {
        var summaries = await session.Query<DailyLocationSummary>()
            .Where(s => s.PrincipalId == pid && s.Date == date).ToListAsync(ct);
        if (summaries.Count == 0)
        {
            return OpResult<DailyLocationSummaryDto>.Ok(new DailyLocationSummaryDto
            {
                Date = date,
                DistanceM = 0,
                TimeInMotionS = 0,
                TimeStationaryS = 0,
                VisitCount = 0,
                Places = [],
            });
        }

        var places = summaries.SelectMany(s => s.PlacesVisited)
            .Select(p => new VisitedPlaceDto { Label = p.Label, Lat = p.Lat, Lon = p.Lon, Minutes = p.Minutes }).ToList();
        return OpResult<DailyLocationSummaryDto>.Ok(new DailyLocationSummaryDto
        {
            Date = date,
            DistanceM = summaries.Sum(s => s.DistanceM),
            TimeInMotionS = summaries.Sum(s => s.TimeInMotionS),
            TimeStationaryS = summaries.Sum(s => s.TimeStationaryS),
            VisitCount = summaries.Sum(s => s.VisitCount),
            Places = places,
        });
    }

    private static List<LocationTrip> BuildTrips(Guid pid, Guid did, List<StayPoint> stays, List<LocationVisit> visits, List<RollupPoint> points)
    {
        var trips = new List<LocationTrip>();
        for (var k = 0; k + 1 < stays.Count; k++)
        {
            var seg = points.GetRange(stays[k].LastIndex, stays[k + 1].FirstIndex - stays[k].LastIndex + 1);
            double dist = 0;
            for (var m = 1; m < seg.Count; m++) dist += Geo.HaversineMeters(seg[m - 1].Lat, seg[m - 1].Lon, seg[m].Lat, seg[m].Lon);
            var durationS = (stays[k + 1].Arrive - stays[k].Depart).TotalSeconds;

            trips.Add(new LocationTrip
            {
                Id = Guid.NewGuid(),
                PrincipalId = pid,
                DeviceId = did,
                StartTs = stays[k].Depart,
                EndTs = stays[k + 1].Arrive,
                FromVisitId = visits[k].Id,
                ToVisitId = visits[k + 1].Id,
                DistanceM = dist,
                DurationS = durationS,
                DominantActivity = TripModeClassifier.Classify(seg),
                AvgSpeedMps = durationS > 0 ? dist / durationS : 0,
                MaxSpeedMps = seg.Max(s => s.TrustedSpeedMps) ?? 0,
            });
        }

        return trips;
    }

    private static double OverlapS(DateTimeOffset start, DateTimeOffset end, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        var s = start > windowStart ? start : windowStart;
        var e = end < windowEnd ? end : windowEnd;
        return e > s ? (e - s).TotalSeconds : 0;
    }

    private DailyLocationSummary BuildSummary(Guid pid, Guid did, DateOnly day, List<RollupPoint> points, List<LocationVisit> visits, List<LocationTrip> trips)
    {
        var dayStart = LocalDay.StartUtc(day, _tz);
        var dayEnd = LocalDay.StartUtc(day.AddDays(1), _tz);
        var dayPoints = points.Where(p => p.Ts >= dayStart && p.Ts < dayEnd).ToList();
        double pathM = 0;
        for (var m = 1; m < dayPoints.Count; m++) pathM += Geo.HaversineMeters(dayPoints[m - 1].Lat, dayPoints[m - 1].Lon, dayPoints[m].Lat, dayPoints[m].Lon);
        var dayVisits = visits.Where(v => v.ArriveTs < dayEnd && v.DepartTs > dayStart).ToList();

        return new DailyLocationSummary
        {
            Id = DailyLocationSummary.MakeId(pid, did, day),
            PrincipalId = pid,
            DeviceId = did,
            Date = day,
            DistanceM = pathM,
            TimeInMotionS = trips.Sum(t => OverlapS(t.StartTs, t.EndTs, dayStart, dayEnd)),
            TimeStationaryS = dayVisits.Sum(v => OverlapS(v.ArriveTs, v.DepartTs, dayStart, dayEnd)),
            VisitCount = dayVisits.Count,
            PlacesVisited = dayVisits
                .Select(v => new VisitedPlace(v.PlaceLabel, v.CentroidLat, v.CentroidLon, OverlapS(v.ArriveTs, v.DepartTs, dayStart, dayEnd) / 60))
                .ToList(),
        };
    }

    private async Task<List<RollupPoint>> ReadPointsAsync(Guid pid, Guid did, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        const string sql = """
            SELECT ts, lat, lon, speed_mps, speed_acc_mps
            FROM telemetry.location_point
            WHERE principal_id = @pid AND device_id = @did AND ts >= @from AND ts < @to
              AND (accuracy_m IS NULL OR accuracy_m <= 50) AND is_mock = false
            ORDER BY ts, seq
            """;
        var pts = new List<RollupPoint>();
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("pid", pid);
        cmd.Parameters.AddWithValue("did", did);
        cmd.Parameters.AddWithValue("from", NpgsqlDbType.TimestampTz, from.UtcDateTime);
        cmd.Parameters.AddWithValue("to", NpgsqlDbType.TimestampTz, to.UtcDateTime);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            pts.Add(new RollupPoint(new DateTimeOffset(r.GetFieldValue<DateTime>(0), TimeSpan.Zero), r.GetDouble(1), r.GetDouble(2), Db.NDouble(r, 3), Db.NDouble(r, 4)));
        return pts;
    }
}
