using System.Net.Http.Json;
using LupiraLocationApi.Dtos.Location;
using LupiraLocationApi.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace LupiraLocationApi.Server.Tests;

/// <summary>White-box tests for behaviors HTTP can't easily reach: rollup idempotency, on-demand partition creation,
/// and retention partition-drop. Resolves singletons (NpgsqlDataSource, PartitionManager) + scoped services directly.</summary>
public sealed class StoreLevelTests(LocationApiTestFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Rollup_is_idempotent_replace()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (pid, key, deviceId) = await SetupDeviceAsync(api);
        var (day, baseTs) = SafeDay();

        var lines = new List<string>();
        long seq = 1;
        for (var k = 0; k < 9; k++) lines.Add(Fix(seq++, baseTs.AddMinutes(k * 2), 59.300, 18.000));
        await IngestLocationAsync(key, lines);

        await RollupAsync(pid, deviceId, day);
        await RollupAsync(pid, deviceId, day);   // re-run must replace, not duplicate

        var from = Q(baseTs.AddHours(-1));
        var to = Q(baseTs.AddHours(2));
        Assert.Single((await api.GetFromJsonAsync<List<LocationVisitDto>>($"/location/visits?from={from}&to={to}"))!);
    }

    [Fact]
    public async Task Partition_is_created_on_demand_across_weeks()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, _) = await SetupDeviceAsync(api);
        var now = DateTimeOffset.UtcNow;
        // Three fixes in three different ISO weeks — each lands in its own on-demand partition.
        await IngestLocationAsync(key, [Fix(1, now.AddDays(-15), 59.3, 18.0), Fix(2, now.AddDays(-8), 59.3, 18.0), Fix(3, now.AddMinutes(-1), 59.3, 18.0)]);

        var all = await api.GetFromJsonAsync<List<TrackPointDto>>($"/location/track?from={Q(now.AddDays(-30))}&to={Q(now.AddMinutes(1))}");
        Assert.Equal(3, all!.Count);
    }

    [Fact]
    public async Task Retention_drop_removes_old_partition_but_keeps_recent()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, _) = await SetupDeviceAsync(api);
        var now = DateTimeOffset.UtcNow;
        var old = now.AddDays(-40);
        await IngestLocationAsync(key, [Fix(1, old, 59.3, 18.0), Fix(2, now.AddMinutes(-1), 59.3, 18.0)]);

        var db = Factory.Services.GetRequiredService<NpgsqlDataSource>();
        var partitions = Factory.Services.GetRequiredService<PartitionManager>();
        await using (var conn = await db.OpenConnectionAsync())
            await partitions.DropExpiredAsync(conn, "location_point", PartitionInterval.Weekly, now.AddDays(-30));

        // Old partition gone; recent data intact.
        Assert.Empty((await api.GetFromJsonAsync<List<TrackPointDto>>($"/location/track?from={Q(old.AddDays(-1))}&to={Q(old.AddDays(1))}"))!);
        Assert.Single((await api.GetFromJsonAsync<List<TrackPointDto>>($"/location/track?from={Q(now.AddHours(-1))}&to={Q(now.AddMinutes(1))}"))!);
    }
}
