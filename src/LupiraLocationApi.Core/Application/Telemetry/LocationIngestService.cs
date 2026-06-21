using System.Globalization;
using System.Text.Json;
using LupiraLocationApi.Domain.Telemetry;
using LupiraLocationApi.Dtos.Location;
using LupiraLocationApi.Telemetry;
using Npgsql;
using NpgsqlTypes;

namespace LupiraLocationApi.Application.Telemetry;

/// <summary>Ingests batched GPS fixes (NDJSON, one fix per line). Idempotent and resumable: rows are merged with
/// <c>ON CONFLICT DO NOTHING</c> keyed on the device-assigned <c>seq</c>, partitions are pre-created on demand for any
/// (possibly late) timestamp, and the latest-snapshot row only advances monotonically. <c>principalId</c>/<c>deviceId</c>
/// are stamped server-side from the authenticated device key — a body carrying ids is rejected.</summary>
public sealed class LocationIngestService(NpgsqlDataSource db, PartitionManager partitions, TrackingStateService tracking)
{
    private const int MaxRows = 10_000;
    private const string Parent = "location_point";

    public int RetentionDays { get; init; } = 90;

    public async Task<OpResult<LocationIngestReceipt>> IngestNdjsonAsync(Guid principalId, Guid deviceId, Stream body, CancellationToken ct = default)
    {
        if (await tracking.IsPausedAsync(principalId, deviceId, ct))
            return OpResult<LocationIngestReceipt>.Ok(LocationIngestReceipt.PausedReceipt);

        var now = DateTimeOffset.UtcNow;
        var maxFuture = now.AddMinutes(5);
        var minPast = now.AddDays(-RetentionDays);

        var accepted = new List<LocationFix>();
        var rejects = new List<IngestReject>();
        var submitted = 0;

        using var reader = new StreamReader(body);
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            submitted++;
            if (submitted > MaxRows) { rejects.Add(new IngestReject { Seq = null, Reason = "batch_too_large" }); break; }
            var (fix, reason, seq) = ParseFix(line, maxFuture, minPast);
            if (fix is not null) accepted.Add(fix);
            else rejects.Add(new IngestReject { Seq = seq, Reason = reason! });
        }

        var inserted = 0;
        if (accepted.Count > 0)
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            foreach (var week in accepted.Select(f => f.Ts).Distinct())
                await partitions.EnsureAsync(conn, tx, Parent, PartitionInterval.Weekly, week, ct);

            inserted = await InsertAsync(conn, tx, principalId, deviceId, accepted, ct);
            await UpsertCurrentAsync(conn, tx, principalId, deviceId, accepted, ct);
            await tx.CommitAsync(ct);
        }

        var highWater = await HighWaterSeqAsync(principalId, deviceId, ct);
        return OpResult<LocationIngestReceipt>.Ok(new LocationIngestReceipt
        {
            Submitted = submitted,
            Inserted = inserted,
            Duplicates = accepted.Count - inserted,
            Rejected = rejects.Count,
            HighWaterSeq = highWater,
            Rejects = rejects,
        });
    }

    public async Task<OpResult<LocationCursor>> GetCursorAsync(Guid principalId, Guid deviceId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT seq, ts FROM telemetry.location_current WHERE principal_id = @pid AND device_id = @did", conn);
        cmd.Parameters.AddWithValue("pid", principalId);
        cmd.Parameters.AddWithValue("did", deviceId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (await r.ReadAsync(ct))
            return OpResult<LocationCursor>.Ok(new LocationCursor { DeviceId = deviceId, LastSeq = r.GetInt64(0), LastTs = new DateTimeOffset(r.GetFieldValue<DateTime>(1), TimeSpan.Zero) });
        return OpResult<LocationCursor>.Ok(new LocationCursor { DeviceId = deviceId, LastSeq = null, LastTs = null });
    }

    private async Task<long?> HighWaterSeqAsync(Guid principalId, Guid deviceId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT seq FROM telemetry.location_current WHERE principal_id = @pid AND device_id = @did", conn);
        cmd.Parameters.AddWithValue("pid", principalId);
        cmd.Parameters.AddWithValue("did", deviceId);
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is long l ? l : null;
    }

    private static async Task<int> InsertAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid pid, Guid did, List<LocationFix> rows, CancellationToken ct)
    {
        var n = rows.Count;
        var ts = new DateTime[n]; var seq = new long[n]; var lat = new double[n]; var lon = new double[n];
        var acc = new double?[n]; var alt = new double?[n]; var vacc = new double?[n]; var hdg = new double?[n]; var hacc = new double?[n];
        var spd = new double?[n]; var sacc = new double?[n]; var prov = new short[n]; var act = new short[n];
        var aconf = new short?[n]; var bat = new short?[n]; var moving = new bool?[n]; var mock = new bool[n];
        for (var i = 0; i < n; i++)
        {
            var f = rows[i];
            ts[i] = f.Ts.UtcDateTime; seq[i] = f.Seq; lat[i] = f.Lat; lon[i] = f.Lon;
            acc[i] = f.AccuracyM; alt[i] = f.AltitudeM; vacc[i] = f.VerticalAccM; hdg[i] = f.HeadingDeg; hacc[i] = f.HeadingAccDeg;
            spd[i] = f.SpeedMps; sacc[i] = f.SpeedAccMps; prov[i] = (short)f.Provider; act[i] = (short)f.Activity;
            aconf[i] = f.ActivityConf; bat[i] = f.BatteryPct; moving[i] = f.IsMoving; mock[i] = f.IsMock;
        }

        const string sql = """
            INSERT INTO telemetry.location_point
              (principal_id, device_id, ts, seq, lat, lon, accuracy_m, altitude_m, vertical_acc_m,
               heading_deg, heading_acc_deg, speed_mps, speed_acc_mps, provider, activity, activity_conf, battery_pct, is_moving, is_mock)
            SELECT @pid, @did, t.ts, t.seq, t.lat, t.lon, t.accuracy_m, t.altitude_m, t.vertical_acc_m,
                   t.heading_deg, t.heading_acc_deg, t.speed_mps, t.speed_acc_mps, t.provider, t.activity, t.activity_conf, t.battery_pct, t.is_moving, t.is_mock
            FROM unnest(@ts::timestamptz[], @seq::bigint[], @lat::float8[], @lon::float8[], @accuracy_m::float8[], @altitude_m::float8[], @vertical_acc_m::float8[],
                        @heading_deg::float8[], @heading_acc_deg::float8[], @speed_mps::float8[], @speed_acc_mps::float8[],
                        @provider::smallint[], @activity::smallint[], @activity_conf::smallint[], @battery_pct::smallint[], @is_moving::bool[], @is_mock::bool[])
              AS t(ts, seq, lat, lon, accuracy_m, altitude_m, vertical_acc_m, heading_deg, heading_acc_deg, speed_mps, speed_acc_mps, provider, activity, activity_conf, battery_pct, is_moving, is_mock)
            ON CONFLICT DO NOTHING
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("pid", pid);
        cmd.Parameters.AddWithValue("did", did);
        Arr(cmd, "ts", NpgsqlDbType.TimestampTz, ts);
        Arr(cmd, "seq", NpgsqlDbType.Bigint, seq);
        Arr(cmd, "lat", NpgsqlDbType.Double, lat);
        Arr(cmd, "lon", NpgsqlDbType.Double, lon);
        Arr(cmd, "accuracy_m", NpgsqlDbType.Double, acc);
        Arr(cmd, "altitude_m", NpgsqlDbType.Double, alt);
        Arr(cmd, "vertical_acc_m", NpgsqlDbType.Double, vacc);
        Arr(cmd, "heading_deg", NpgsqlDbType.Double, hdg);
        Arr(cmd, "heading_acc_deg", NpgsqlDbType.Double, hacc);
        Arr(cmd, "speed_mps", NpgsqlDbType.Double, spd);
        Arr(cmd, "speed_acc_mps", NpgsqlDbType.Double, sacc);
        Arr(cmd, "provider", NpgsqlDbType.Smallint, prov);
        Arr(cmd, "activity", NpgsqlDbType.Smallint, act);
        Arr(cmd, "activity_conf", NpgsqlDbType.Smallint, aconf);
        Arr(cmd, "battery_pct", NpgsqlDbType.Smallint, bat);
        Arr(cmd, "is_moving", NpgsqlDbType.Boolean, moving);
        Arr(cmd, "is_mock", NpgsqlDbType.Boolean, mock);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task UpsertCurrentAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid pid, Guid did, List<LocationFix> rows, CancellationToken ct)
    {
        var top = rows.MaxBy(f => f.Seq)!;
        const string sql = """
            INSERT INTO telemetry.location_current (principal_id, device_id, ts, seq, lat, lon, accuracy_m, speed_mps, activity, battery_pct, received_at)
            VALUES (@pid, @did, @ts, @seq, @lat, @lon, @acc, @spd, @act, @bat, now())
            ON CONFLICT (principal_id, device_id) DO UPDATE SET
                ts = EXCLUDED.ts, seq = EXCLUDED.seq, lat = EXCLUDED.lat, lon = EXCLUDED.lon,
                accuracy_m = EXCLUDED.accuracy_m, speed_mps = EXCLUDED.speed_mps, activity = EXCLUDED.activity,
                battery_pct = EXCLUDED.battery_pct, received_at = now()
            WHERE EXCLUDED.seq > telemetry.location_current.seq
            """;
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("pid", pid);
        cmd.Parameters.AddWithValue("did", did);
        cmd.Parameters.AddWithValue("ts", NpgsqlDbType.TimestampTz, top.Ts.UtcDateTime);
        cmd.Parameters.AddWithValue("seq", top.Seq);
        cmd.Parameters.AddWithValue("lat", top.Lat);
        cmd.Parameters.AddWithValue("lon", top.Lon);
        cmd.Parameters.AddWithValue("acc", (object?)top.AccuracyM ?? DBNull.Value);
        cmd.Parameters.AddWithValue("spd", (object?)top.SpeedMps ?? DBNull.Value);
        cmd.Parameters.AddWithValue("act", (short)top.Activity);
        cmd.Parameters.AddWithValue("bat", (object?)top.BatteryPct ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void Arr(NpgsqlCommand cmd, string name, NpgsqlDbType elem, Array value) =>
        cmd.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Array | elem) { Value = value });

    // ---- parsing ----

    private static (LocationFix? Fix, string? Reason, long? Seq) ParseFix(string line, DateTimeOffset maxFuture, DateTimeOffset minPast)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(line); }
        catch { return (null, "invalid_json", null); }
        using (doc)
        {
            var o = doc.RootElement;
            if (o.ValueKind != JsonValueKind.Object) return (null, "invalid_json", null);
            if (o.TryGetProperty("principal_id", out _) || o.TryGetProperty("principalId", out _)
                || o.TryGetProperty("device_id", out _) || o.TryGetProperty("deviceId", out _))
                return (null, "body_ids_forbidden", ReadLong(o, "seq"));

            var seq = ReadLong(o, "seq");
            if (seq is null) return (null, "missing_seq", null);

            var tsStr = ReadString(o, "ts");
            if (tsStr is null || !DateTimeOffset.TryParse(tsStr, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var ts))
                return (null, "invalid_ts", seq);
            if (ts > maxFuture || ts < minPast) return (null, "ts_out_of_range", seq);

            var lat = ReadDouble(o, "lat");
            var lon = ReadDouble(o, "lon");
            if (lat is null || lon is null) return (null, "missing_latlon", seq);
            if (lat is < -90 or > 90 || lon is < -180 or > 180) return (null, "invalid_latlon", seq);

            var fix = new LocationFix(
                seq.Value, ts, lat.Value, lon.Value,
                ReadDouble(o, "accuracy_m"), ReadDouble(o, "altitude_m"), ReadDouble(o, "vertical_acc_m"),
                ReadDouble(o, "heading_deg"), ReadDouble(o, "heading_acc_deg"),
                ReadDouble(o, "speed_mps"), ReadDouble(o, "speed_acc_mps"),
                ParseProvider(o), ParseActivity(o), ReadShort(o, "activity_conf"), ReadShort(o, "battery_pct"),
                ReadBool(o, "is_moving"), ReadBool(o, "is_mock") ?? false);
            return (fix, null, seq);
        }
    }

    private static LocationProvider ParseProvider(JsonElement o)
    {
        if (o.TryGetProperty("provider", out var e))
        {
            if (e.ValueKind == JsonValueKind.String && Enum.TryParse<LocationProvider>(e.GetString(), true, out var v)) return v;
            if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) && Enum.IsDefined(typeof(LocationProvider), (short)n)) return (LocationProvider)(short)n;
        }
        return LocationProvider.Unknown;
    }

    private static MotionActivity ParseActivity(JsonElement o)
    {
        if (o.TryGetProperty("activity", out var e))
        {
            if (e.ValueKind == JsonValueKind.String && Enum.TryParse<MotionActivity>(e.GetString(), true, out var v)) return v;
            if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) && Enum.IsDefined(typeof(MotionActivity), (short)n)) return (MotionActivity)(short)n;
        }
        return MotionActivity.Unknown;
    }

    private static double? ReadDouble(JsonElement o, string name) =>
        o.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var d) ? d : null;

    private static long? ReadLong(JsonElement o, string name) =>
        o.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var v) ? v : null;

    private static short? ReadShort(JsonElement o, string name) =>
        o.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var v) ? (short)v : null;

    private static bool? ReadBool(JsonElement o, string name) =>
        o.TryGetProperty(name, out var e) && e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : null;

    private static string? ReadString(JsonElement o, string name) =>
        o.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}
