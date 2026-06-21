using LupiraLocationApi.Domain.Telemetry;
using LupiraLocationApi.Dtos.Location;
using Marten;
using Npgsql;
using NpgsqlTypes;

namespace LupiraLocationApi.Application.Telemetry;

/// <summary>Read API over a principal's own location data. Every query hard-filters <c>principal_id = caller</c>, which
/// IS the authorization boundary — a foreign device id simply matches nothing. Raw track is owner-only; the coarse
/// "where was I" answer is the only thing safe to surface for cross-service synergy.</summary>
public sealed class LocationQueryService(NpgsqlDataSource db, IDocumentSession session, PlaceLabelService labels)
{
    private const int Cap = 50_000;

    public async Task<OpResult<List<CurrentFixDto>>> CurrentAsync(Guid pid, Guid? deviceId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT device_id, ts, lat, lon, accuracy_m, speed_mps, activity, battery_pct
            FROM telemetry.location_current
            WHERE principal_id = @pid AND (@did::uuid IS NULL OR device_id = @did)
            ORDER BY ts DESC
            """;
        var result = new List<CurrentFixDto>();
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = Cmd(conn, sql, pid, deviceId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            result.Add(new CurrentFixDto
            {
                DeviceId = r.GetGuid(0), Ts = Utc(r, 1), Lat = r.GetDouble(2), Lon = r.GetDouble(3),
                AccuracyM = Db.NDouble(r, 4), SpeedMps = Db.NDouble(r, 5), Activity = Db.Activity(Db.NShort(r, 6)), BatteryPct = Db.NInt(r, 7),
            });
        return OpResult<List<CurrentFixDto>>.Ok(result);
    }

    public Task<OpResult<List<TrackPointDto>>> TrackAsync(Guid pid, Guid? deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        const string sql = """
            SELECT device_id, ts, lat, lon, accuracy_m, altitude_m, heading_deg, speed_mps, activity, provider
            FROM telemetry.location_point
            WHERE principal_id = @pid AND ts >= @from AND ts < @to AND (@did::uuid IS NULL OR device_id = @did)
            ORDER BY device_id, ts, seq
            LIMIT @cap
            """;
        return ReadTrackAsync(sql, pid, deviceId, from, to, addBucket: false, bucket: default, box: null, ct);
    }

    public Task<OpResult<List<TrackPointDto>>> ThinnedTrackAsync(Guid pid, Guid? deviceId, DateTimeOffset from, DateTimeOffset to, TimeSpan bucket, CancellationToken ct = default)
    {
        const string sql = """
            SELECT DISTINCT ON (date_bin(@bucket, ts, @from), device_id)
                   device_id, ts, lat, lon, accuracy_m, altitude_m, heading_deg, speed_mps, activity, provider
            FROM telemetry.location_point
            WHERE principal_id = @pid AND ts >= @from AND ts < @to AND (@did::uuid IS NULL OR device_id = @did)
            ORDER BY date_bin(@bucket, ts, @from), device_id, accuracy_m NULLS LAST, ts
            """;
        return ReadTrackAsync(sql, pid, deviceId, from, to, addBucket: true, bucket: bucket, box: null, ct);
    }

    public Task<OpResult<List<TrackPointDto>>> BoundingBoxAsync(Guid pid, Guid? deviceId, DateTimeOffset from, DateTimeOffset to, (double MinLat, double MaxLat, double MinLon, double MaxLon) box, CancellationToken ct = default)
    {
        const string sql = """
            SELECT device_id, ts, lat, lon, accuracy_m, altitude_m, heading_deg, speed_mps, activity, provider
            FROM telemetry.location_point
            WHERE principal_id = @pid AND ts >= @from AND ts < @to
              AND lat BETWEEN @minLat AND @maxLat AND lon BETWEEN @minLon AND @maxLon
              AND (@did::uuid IS NULL OR device_id = @did)
            ORDER BY device_id, ts, seq
            LIMIT @cap
            """;
        return ReadTrackAsync(sql, pid, deviceId, from, to, addBucket: false, bucket: default, box: box, ct);
    }

    private async Task<OpResult<List<TrackPointDto>>> ReadTrackAsync(string sql, Guid pid, Guid? deviceId, DateTimeOffset from, DateTimeOffset to, bool addBucket, TimeSpan bucket, (double MinLat, double MaxLat, double MinLon, double MaxLon)? box, CancellationToken ct)
    {
        var result = new List<TrackPointDto>();
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = Cmd(conn, sql, pid, deviceId);
        cmd.Parameters.AddWithValue("from", NpgsqlDbType.TimestampTz, from.UtcDateTime);
        cmd.Parameters.AddWithValue("to", NpgsqlDbType.TimestampTz, to.UtcDateTime);
        cmd.Parameters.AddWithValue("cap", Cap);
        if (addBucket) cmd.Parameters.AddWithValue("bucket", bucket);
        if (box is { } b)
        {
            cmd.Parameters.AddWithValue("minLat", b.MinLat);
            cmd.Parameters.AddWithValue("maxLat", b.MaxLat);
            cmd.Parameters.AddWithValue("minLon", b.MinLon);
            cmd.Parameters.AddWithValue("maxLon", b.MaxLon);
        }
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            result.Add(new TrackPointDto
            {
                DeviceId = r.GetGuid(0), Ts = Utc(r, 1), Lat = r.GetDouble(2), Lon = r.GetDouble(3),
                AccuracyM = Db.NDouble(r, 4), AltitudeM = Db.NDouble(r, 5), HeadingDeg = Db.NDouble(r, 6), SpeedMps = Db.NDouble(r, 7),
                Activity = Db.Activity(Db.NShort(r, 8)), Provider = Db.Provider(Db.NShort(r, 9)),
            });
        return OpResult<List<TrackPointDto>>.Ok(result);
    }

    public async Task<OpResult<TrackStatsDto>> StatsAsync(Guid pid, Guid? deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        const string sql = """
            WITH ordered AS (
                SELECT ts, lat, lon, speed_mps, speed_acc_mps,
                       LAG(lat) OVER w AS plat, LAG(lon) OVER w AS plon
                FROM telemetry.location_point
                WHERE principal_id = @pid AND ts >= @from AND ts < @to AND (@did::uuid IS NULL OR device_id = @did)
                  AND (accuracy_m IS NULL OR accuracy_m <= 50) AND is_mock = false
                WINDOW w AS (PARTITION BY device_id ORDER BY ts, seq)
            ),
            steps AS (
                SELECT speed_mps, speed_acc_mps,
                       CASE WHEN plat IS NULL THEN 0 ELSE
                         6371000 * 2 * asin(sqrt(power(sin(radians(lat - plat) / 2), 2)
                           + cos(radians(plat)) * cos(radians(lat)) * power(sin(radians(lon - plon) / 2), 2))) END AS step_m
                FROM ordered
            )
            SELECT COALESCE(SUM(step_m), 0) AS distance_m,
                   AVG(speed_mps) FILTER (WHERE speed_mps IS NOT NULL) AS avg_speed,
                   MAX(speed_mps) FILTER (WHERE speed_acc_mps IS NULL OR speed_acc_mps <= 2) AS max_speed,
                   COUNT(*) AS cnt
            FROM steps
            """;
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = Cmd(conn, sql, pid, deviceId);
        cmd.Parameters.AddWithValue("from", NpgsqlDbType.TimestampTz, from.UtcDateTime);
        cmd.Parameters.AddWithValue("to", NpgsqlDbType.TimestampTz, to.UtcDateTime);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return OpResult<TrackStatsDto>.Ok(new TrackStatsDto { DistanceM = 0, AvgSpeedMps = null, MaxSpeedMps = null, SampleCount = 0 });
        return OpResult<TrackStatsDto>.Ok(new TrackStatsDto { DistanceM = Db.Double0(r, 0), AvgSpeedMps = Db.NDouble(r, 1), MaxSpeedMps = Db.NDouble(r, 2), SampleCount = r.GetInt64(3) });
    }

    public async Task<OpResult<PlaceLabelAtDto>> PlaceLabelAtAsync(Guid pid, DateTimeOffset ts, CancellationToken ct = default)
    {
        var visit = await session.Query<LocationVisit>()
            .Where(v => v.PrincipalId == pid && v.ArriveTs <= ts && v.DepartTs >= ts)
            .FirstOrDefaultAsync(ct);
        if (visit is not null)
        {
            var (qlat, qlon) = PlaceLabel.Quantize(visit.CentroidLat, visit.CentroidLon);
            var label = visit.PlaceLabel ?? await labels.ResolveAsync(visit.CentroidLat, visit.CentroidLon, ct);
            return OpResult<PlaceLabelAtDto>.Ok(new PlaceLabelAtDto { Ts = ts, Label = label, Lat = qlat, Lon = qlon, Source = "visit" });
        }

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT lat, lon FROM telemetry.location_point WHERE principal_id = @pid ORDER BY abs(extract(epoch FROM (ts - @ts))) LIMIT 1", conn);
        cmd.Parameters.AddWithValue("pid", pid);
        cmd.Parameters.AddWithValue("ts", NpgsqlDbType.TimestampTz, ts.UtcDateTime);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (await r.ReadAsync(ct))
        {
            var (qlat, qlon) = PlaceLabel.Quantize(r.GetDouble(0), r.GetDouble(1));
            var label = await labels.ResolveAsync(r.GetDouble(0), r.GetDouble(1), ct);
            return OpResult<PlaceLabelAtDto>.Ok(new PlaceLabelAtDto { Ts = ts, Label = label, Lat = qlat, Lon = qlon, Source = "fix" });
        }
        return OpResult<PlaceLabelAtDto>.Ok(new PlaceLabelAtDto { Ts = ts, Label = null, Lat = 0, Lon = 0, Source = "none" });
    }

    public async Task<OpResult> PurgeRangeAsync(Guid pid, Guid? deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        await using (var conn = await db.OpenConnectionAsync(ct))
        {
            // Raw fixes in range, plus the latest-snapshot row when its fix falls in the purged range (so a full
            // erase doesn't leave /current pointing at a purged location).
            foreach (var table in new[] { "telemetry.location_point", "telemetry.location_current" })
            {
                await using var cmd = new NpgsqlCommand(
                    $"DELETE FROM {table} WHERE principal_id = @pid AND ts >= @from AND ts < @to AND (@did::uuid IS NULL OR device_id = @did)", conn);
                cmd.Parameters.AddWithValue("pid", pid);
                cmd.Parameters.AddWithValue("did", (object?)deviceId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("from", NpgsqlDbType.TimestampTz, from.UtcDateTime);
                cmd.Parameters.AddWithValue("to", NpgsqlDbType.TimestampTz, to.UtcDateTime);
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }

        // Derived docs in range (owned by this principal).
        session.DeleteWhere<LocationVisit>(v => v.PrincipalId == pid && v.ArriveTs >= from && v.ArriveTs < to);
        session.DeleteWhere<LocationTrip>(t => t.PrincipalId == pid && t.StartTs >= from && t.StartTs < to);
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    private static NpgsqlCommand Cmd(NpgsqlConnection conn, string sql, Guid pid, Guid? deviceId)
    {
        var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("pid", pid);
        cmd.Parameters.AddWithValue("did", (object?)deviceId ?? DBNull.Value);
        return cmd;
    }

    private static DateTimeOffset Utc(NpgsqlDataReader r, int i) => new(r.GetFieldValue<DateTime>(i), TimeSpan.Zero);
}
