using Lupira.Postgres.Partitions;
using LupiraLocationApi.Core.Application.Telemetry;
using Npgsql;

namespace LupiraLocationApi.Workers;

/// <summary>Periodic maintenance for the location telemetry store: provision upcoming partitions, roll up recent local days
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

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Location maintenance pass failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(1), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await using var conn = await db.OpenConnectionAsync(ct);

        await partitions.EnsureRangeAsync(conn, "location_point", PartitionInterval.Weekly, now.AddDays(-7), now.AddDays(14), ct);

        var retentionDays = config.GetValue("Telemetry:LocationRetentionDays", 90);
        using (var scope = scopes.CreateScope())
            await scope.ServiceProvider.GetRequiredService<LocationRollupPass>().RunAsync(now, retentionDays, ct);

        await partitions.DropExpiredAsync(conn, "location_point", PartitionInterval.Weekly, now.AddDays(-retentionDays), ct);
    }
}
