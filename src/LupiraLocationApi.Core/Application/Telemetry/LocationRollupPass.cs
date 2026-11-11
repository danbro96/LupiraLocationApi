using LupiraLocationApi.Core.Domain.Telemetry;
using Marten;
using Npgsql;
using NpgsqlTypes;

namespace LupiraLocationApi.Core.Application.Telemetry;

/// <summary>One maintenance rollup over every device: local yesterday through now, reaching back to the day of any fix
/// that arrived since the last pass, or the whole retention window when the stored docs were built by an older
/// <see cref="TripVisitService.AlgorithmVersion"/>.</summary>
public sealed class LocationRollupPass(NpgsqlDataSource db, IDocumentSession session, TripVisitService rollup)
{
    // An ingest transaction that began before the previous mark can commit after it; re-rolling is idempotent.
    private static readonly TimeSpan LateOverlap = TimeSpan.FromMinutes(10);

    public async Task RunAsync(DateTimeOffset now, int retentionDays, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var mark = await DbNowAsync(conn, ct);
        var checkpoint = await session.LoadAsync<LocationRollupCheckpoint>(LocationRollupCheckpoint.SingletonId, ct);
        var rebuild = (checkpoint?.AlgorithmVersion ?? 0) < TripVisitService.AlgorithmVersion;
        var tz = rollup.TimeZone;
        var from = rebuild
            ? LocalDay.StartUtc(LocalDay.Of(now.AddDays(-retentionDays), tz), tz)
            : LocalDay.StartUtc(LocalDay.Of(now, tz).AddDays(-1), tz);
        var late = rebuild || checkpoint is null
            ? []
            : await LateFixesAsync(conn, checkpoint.ReceivedThrough - LateOverlap, from, ct);

        foreach (var (pid, did) in await DevicesAsync(conn, ct))
        {
            var start = late.TryGetValue((pid, did), out var oldest) ? LocalDay.StartUtc(LocalDay.Of(oldest, tz), tz) : from;
            await rollup.RollupAsync(pid, did, start, now, ct);
        }

        session.Store(new LocationRollupCheckpoint { AlgorithmVersion = TripVisitService.AlgorithmVersion, ReceivedThrough = mark });
        await session.SaveChangesAsync(ct);
    }

    private static async Task<DateTimeOffset> DbNowAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT now()", conn);
        return new DateTimeOffset((DateTime) (await cmd.ExecuteScalarAsync(ct))!, TimeSpan.Zero);
    }

    /// <summary>Oldest fix per device received after <paramref name="since"/> but timestamped before the normal window.</summary>
    private static async Task<Dictionary<(Guid Pid, Guid Did), DateTimeOffset>> LateFixesAsync(
        NpgsqlConnection conn, DateTimeOffset since, DateTimeOffset before, CancellationToken ct)
    {
        const string sql = """
            SELECT principal_id, device_id, min(ts)
            FROM telemetry.location_point
            WHERE received_at > @since AND ts < @before
            GROUP BY principal_id, device_id
            """;
        var late = new Dictionary<(Guid Pid, Guid Did), DateTimeOffset>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("since", NpgsqlDbType.TimestampTz, since.UtcDateTime);
        cmd.Parameters.AddWithValue("before", NpgsqlDbType.TimestampTz, before.UtcDateTime);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) late[(r.GetGuid(0), r.GetGuid(1))] = new DateTimeOffset(r.GetFieldValue<DateTime>(2), TimeSpan.Zero);
        return late;
    }

    private static async Task<List<(Guid Pid, Guid Did)>> DevicesAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        var pairs = new List<(Guid Pid, Guid Did)>();
        await using var cmd = new NpgsqlCommand("SELECT principal_id, device_id FROM telemetry.location_current", conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) pairs.Add((r.GetGuid(0), r.GetGuid(1)));
        return pairs;
    }
}
