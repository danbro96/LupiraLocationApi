using LupiraLocationApi.Core.Domain.Telemetry;
using Marten;
using Npgsql;

namespace LupiraLocationApi.Core.Application.Telemetry;

/// <summary>One maintenance rollup over every device: local yesterday through now, or the whole retention window when the
/// stored docs were built by an older <see cref="TripVisitService.AlgorithmVersion"/>.</summary>
public sealed class LocationRollupPass(NpgsqlDataSource db, IDocumentSession session, TripVisitService rollup)
{
    public async Task RunAsync(DateTimeOffset now, int retentionDays, CancellationToken ct = default)
    {
        var checkpoint = await session.LoadAsync<LocationRollupCheckpoint>(LocationRollupCheckpoint.SingletonId, ct);
        var rebuild = (checkpoint?.AlgorithmVersion ?? 0) < TripVisitService.AlgorithmVersion;
        var tz = rollup.TimeZone;
        var firstDay = LocalDay.Of(rebuild ? now.AddDays(-retentionDays) : now, tz);
        var from = LocalDay.StartUtc(rebuild ? firstDay : firstDay.AddDays(-1), tz);

        foreach (var (pid, did) in await DevicesAsync(ct))
            await rollup.RollupAsync(pid, did, from, now, ct);

        if (rebuild)
        {
            session.Store(new LocationRollupCheckpoint { AlgorithmVersion = TripVisitService.AlgorithmVersion });
            await session.SaveChangesAsync(ct);
        }
    }

    private async Task<List<(Guid Pid, Guid Did)>> DevicesAsync(CancellationToken ct)
    {
        var pairs = new List<(Guid Pid, Guid Did)>();
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT principal_id, device_id FROM telemetry.location_current", conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) pairs.Add((r.GetGuid(0), r.GetGuid(1)));
        return pairs;
    }
}
