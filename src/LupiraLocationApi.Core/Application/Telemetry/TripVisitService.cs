using LupiraLocationApi.Domain.Telemetry;
using LupiraLocationApi.Dtos.Location;
using Marten;
using Npgsql;
using NpgsqlTypes;

namespace LupiraLocationApi.Application.Telemetry;

/// <summary>Derives Visits (stay-points), Trips (movement between stays), and a DailyLocationSummary from a day's raw
/// fixes, and materializes them as Marten docs in the <c>health</c> schema (so they survive raw-GPS retention drop).
/// OS-activity-primed stay-point detection: a Visit opens when consecutive points stay within <c>roamRadius</c> for at
/// least <c>minDwell</c>. The rollup is idempotent — re-running a day replaces that day+device's docs.</summary>
public sealed class TripVisitService(NpgsqlDataSource db, IDocumentSession session, PlaceLabelService labels)
{
    private const double RoamRadiusM = 80.0;
    private static readonly TimeSpan MinDwell = TimeSpan.FromMinutes(8);

    private readonly record struct Pt(DateTimeOffset Ts, double Lat, double Lon, double? Speed, short Activity);

    public async Task<OpResult> RollupDayAsync(Guid principalId, Guid deviceId, DateOnly day, CancellationToken ct = default)
    {
        var dayStart = new DateTimeOffset(day.Year, day.Month, day.Day, 0, 0, 0, TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);
        var points = await ReadPointsAsync(principalId, deviceId, dayStart, dayEnd, ct);

        // Clear any prior rollup for this day+device (idempotent replace).
        session.DeleteWhere<LocationVisit>(v => v.PrincipalId == principalId && v.DeviceId == deviceId && v.ArriveTs >= dayStart && v.ArriveTs < dayEnd);
        session.DeleteWhere<LocationTrip>(t => t.PrincipalId == principalId && t.DeviceId == deviceId && t.StartTs >= dayStart && t.StartTs < dayEnd);

        var visits = DetectVisits(points);
        var visitDocs = new List<LocationVisit>();
        foreach (var v in visits)
        {
            visitDocs.Add(new LocationVisit
            {
                Id = Guid.NewGuid(),
                PrincipalId = principalId,
                DeviceId = deviceId,
                ArriveTs = v.Arrive,
                DepartTs = v.Depart,
                CentroidLat = v.Lat,
                CentroidLon = v.Lon,
                RadiusM = v.Radius,
                SampleCount = v.Count,
                PlaceLabel = await labels.ResolveAsync(v.Lat, v.Lon, ct),
            });
        }

        var trips = BuildTrips(principalId, deviceId, visitDocs, points);

        foreach (var v in visitDocs) session.Store(v);
        foreach (var t in trips) session.Store(t);
        session.Store(BuildSummary(principalId, deviceId, day, points, visitDocs, trips));
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    public async Task<OpResult<List<LocationVisitDto>>> VisitsAsync(Guid pid, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var visits = await session.Query<LocationVisit>()
            .Where(v => v.PrincipalId == pid && v.ArriveTs >= from && v.ArriveTs < to)
            .OrderBy(v => v.ArriveTs).ToListAsync(ct);
        return OpResult<List<LocationVisitDto>>.Ok(visits
            .Select(v => new LocationVisitDto
            {
                Id = v.Id, ArriveTs = v.ArriveTs, DepartTs = v.DepartTs, Lat = v.CentroidLat, Lon = v.CentroidLon,
                RadiusM = v.RadiusM, SampleCount = v.SampleCount, PlaceLabel = v.PlaceLabel,
            })
            .ToList());
    }

    public async Task<OpResult<List<LocationTripDto>>> TripsAsync(Guid pid, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var trips = await session.Query<LocationTrip>()
            .Where(t => t.PrincipalId == pid && t.StartTs >= from && t.StartTs < to)
            .OrderBy(t => t.StartTs).ToListAsync(ct);
        return OpResult<List<LocationTripDto>>.Ok(trips
            .Select(t => new LocationTripDto
            {
                Id = t.Id, StartTs = t.StartTs, EndTs = t.EndTs, DistanceM = t.DistanceM, DurationS = t.DurationS,
                DominantActivity = t.DominantActivity, AvgSpeedMps = t.AvgSpeedMps, MaxSpeedMps = t.MaxSpeedMps,
            })
            .ToList());
    }

    public async Task<OpResult<DailyLocationSummaryDto>> SummaryAsync(Guid pid, DateOnly date, CancellationToken ct = default)
    {
        var summaries = await session.Query<DailyLocationSummary>()
            .Where(s => s.PrincipalId == pid && s.Date == date).ToListAsync(ct);
        if (summaries.Count == 0)
            return OpResult<DailyLocationSummaryDto>.Ok(new DailyLocationSummaryDto
            {
                Date = date, DistanceM = 0, TimeInMotionS = 0, TimeStationaryS = 0, VisitCount = 0, Places = [],
            });

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

    // ---- detection ----

    private readonly record struct VisitAccum(DateTimeOffset Arrive, DateTimeOffset Depart, double Lat, double Lon, double Radius, int Count);

    private static List<VisitAccum> DetectVisits(IReadOnlyList<Pt> p)
    {
        var visits = new List<VisitAccum>();
        var n = p.Count;
        var i = 0;
        while (i < n)
        {
            var j = i + 1;
            while (j < n && Geo.HaversineMeters(p[i].Lat, p[i].Lon, p[j].Lat, p[j].Lon) <= RoamRadiusM) j++;
            var dwell = p[j - 1].Ts - p[i].Ts;
            if (j - 1 > i && dwell >= MinDwell)
            {
                double sumLat = 0, sumLon = 0;
                for (var k = i; k < j; k++) { sumLat += p[k].Lat; sumLon += p[k].Lon; }
                var cLat = sumLat / (j - i);
                var cLon = sumLon / (j - i);
                double radius = 0;
                for (var k = i; k < j; k++) radius = Math.Max(radius, Geo.HaversineMeters(cLat, cLon, p[k].Lat, p[k].Lon));
                visits.Add(new VisitAccum(p[i].Ts, p[j - 1].Ts, cLat, cLon, radius, j - i));
                i = j;
            }
            else i++;
        }
        return visits;
    }

    private static List<LocationTrip> BuildTrips(Guid pid, Guid did, List<LocationVisit> visits, IReadOnlyList<Pt> points)
    {
        var trips = new List<LocationTrip>();
        for (var k = 0; k + 1 < visits.Count; k++)
        {
            var startTs = visits[k].DepartTs;
            var endTs = visits[k + 1].ArriveTs;
            var seg = points.Where(p => p.Ts >= startTs && p.Ts <= endTs).ToList();
            if (seg.Count < 2) continue;

            double dist = 0;
            for (var m = 1; m < seg.Count; m++) dist += Geo.HaversineMeters(seg[m - 1].Lat, seg[m - 1].Lon, seg[m].Lat, seg[m].Lon);
            var speeds = seg.Where(s => s.Speed is not null).Select(s => s.Speed!.Value).ToList();

            trips.Add(new LocationTrip
            {
                Id = Guid.NewGuid(),
                PrincipalId = pid,
                DeviceId = did,
                StartTs = startTs,
                EndTs = endTs,
                FromVisitId = visits[k].Id,
                ToVisitId = visits[k + 1].Id,
                DistanceM = dist,
                DurationS = (endTs - startTs).TotalSeconds,
                DominantActivity = DominantActivity(seg),
                AvgSpeedMps = speeds.Count > 0 ? speeds.Average() : 0,
                MaxSpeedMps = speeds.Count > 0 ? speeds.Max() : 0,
            });
        }
        return trips;
    }

    private static MotionActivity DominantActivity(IReadOnlyList<Pt> seg)
    {
        var grouped = seg.Select(s => (MotionActivity)s.Activity)
            .Where(a => a != MotionActivity.Unknown)
            .GroupBy(a => a).OrderByDescending(g => g.Count()).FirstOrDefault();
        return grouped?.Key ?? MotionActivity.Unknown;
    }

    private static DailyLocationSummary BuildSummary(Guid pid, Guid did, DateOnly day, IReadOnlyList<Pt> points, List<LocationVisit> visits, List<LocationTrip> trips)
    {
        double pathM = 0;
        for (var m = 1; m < points.Count; m++) pathM += Geo.HaversineMeters(points[m - 1].Lat, points[m - 1].Lon, points[m].Lat, points[m].Lon);
        return new DailyLocationSummary
        {
            Id = DailyLocationSummary.MakeId(pid, did, day),
            PrincipalId = pid,
            DeviceId = did,
            Date = day,
            DistanceM = pathM,
            TimeInMotionS = trips.Sum(t => t.DurationS),
            TimeStationaryS = visits.Sum(v => (v.DepartTs - v.ArriveTs).TotalSeconds),
            VisitCount = visits.Count,
            PlacesVisited = visits.Select(v => new VisitedPlace(v.PlaceLabel, v.CentroidLat, v.CentroidLon, (v.DepartTs - v.ArriveTs).TotalMinutes)).ToList(),
        };
    }

    private async Task<List<Pt>> ReadPointsAsync(Guid pid, Guid did, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        const string sql = """
            SELECT ts, lat, lon, speed_mps, activity
            FROM telemetry.location_point
            WHERE principal_id = @pid AND device_id = @did AND ts >= @from AND ts < @to
              AND (accuracy_m IS NULL OR accuracy_m <= 50) AND is_mock = false
            ORDER BY ts, seq
            """;
        var pts = new List<Pt>();
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("pid", pid);
        cmd.Parameters.AddWithValue("did", did);
        cmd.Parameters.AddWithValue("from", NpgsqlDbType.TimestampTz, from.UtcDateTime);
        cmd.Parameters.AddWithValue("to", NpgsqlDbType.TimestampTz, to.UtcDateTime);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            pts.Add(new Pt(new DateTimeOffset(r.GetFieldValue<DateTime>(0), TimeSpan.Zero), r.GetDouble(1), r.GetDouble(2), Db.NDouble(r, 3), Db.NShort(r, 4) ?? 0));
        return pts;
    }
}
