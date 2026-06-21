using LupiraLocationApi.Application.Telemetry;
using LupiraLocationApi.Telemetry;
using Npgsql;

namespace LupiraLocationApi.Background;

/// <summary>Periodic maintenance for the location telemetry store: provision upcoming partitions, roll up recent days
/// into Visits/Trips/DailyLocationSummary (freezing place labels), and drop expired raw-location partitions for
/// retention. Defensive + gated by <c>Telemetry:MaintenanceEnabled</c> (disabled in tests so it never races the
/// per-test reset).</summary>
public sealed class LocationMaintenanceService(
    IServiceScopeFactory scopes,
    NpgsqlDataSource db,
    PartitionManager partitions,
    IConfiguration config,
    ILogger<LocationMaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!config.GetValue("Telemetry:MaintenanceEnabled", true)) return;

        try { await Task.Delay(TimeSpan.FromMinutes(2), ct); }
        catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try { await RunOnceAsync(ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Location maintenance pass failed."); }
            try { await Task.Delay(TimeSpan.FromHours(1), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await using var conn = await db.OpenConnectionAsync(ct);

        await partitions.EnsureRangeAsync(conn, "location_point", PartitionInterval.Weekly, now.AddDays(-7), now.AddDays(14), ct);

        var pairs = new List<(Guid Pid, Guid Did)>();
        await using (var cmd = new NpgsqlCommand("SELECT principal_id, device_id FROM telemetry.location_current", conn))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) pairs.Add((r.GetGuid(0), r.GetGuid(1)));

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        foreach (var (pid, did) in pairs)
        {
            using var scope = scopes.CreateScope();
            var trips = scope.ServiceProvider.GetRequiredService<TripVisitService>();
            await trips.RollupDayAsync(pid, did, today.AddDays(-1), ct);
            await trips.RollupDayAsync(pid, did, today, ct);
        }

        var retentionDays = config.GetValue("Telemetry:LocationRetentionDays", 90);
        await partitions.DropExpiredAsync(conn, "location_point", PartitionInterval.Weekly, now.AddDays(-retentionDays), ct);
    }
}
