using System.Net.Http.Json;
using Lupira.Testing.Postgres;
using LupiraLocationApi.Core.Application.Telemetry;
using LupiraLocationApi.Core.Domain.Telemetry;
using LupiraLocationApi.Core.Dtos.Location;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LupiraLocationApi.IntegrationTests;

/// <summary>Rollup behaviour across local midnight, trip mode/speed, and the version-triggered rebuild.</summary>
public sealed class RollupTests(LocationApiTestFactory factory) : IntegrationTest(factory)
{
    private static readonly TimeZoneInfo Tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    [Fact]
    public async Task A_stay_across_local_midnight_is_one_visit()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (pid, key, deviceId) = await SetupDeviceAsync(api);
        var (day, midnight) = PastMidnight();

        var lines = new List<string>();
        long seq = 1;
        for (var t = midnight.AddHours(-4); t <= midnight.AddHours(2); t = t.AddMinutes(10)) lines.Add(Fix(seq++, t, 59.300, 18.000));
        await IngestLocationAsync(key, lines);
        await RollupAsync(pid, deviceId, day.AddDays(-1));
        await RollupAsync(pid, deviceId, day);

        var visit = Assert.Single((await api.GetFromJsonAsync<List<LocationVisitDto>>($"/location/visits?from={Q(midnight)}&to={Q(midnight.AddHours(1))}"))!);
        Assert.Equal(midnight.AddHours(-4), visit.ArriveTs);
        Assert.Equal(midnight.AddHours(2), visit.DepartTs);
        var summary = await api.GetFromJsonAsync<DailyLocationSummaryDto>($"/location/summary?date={day:yyyy-MM-dd}");
        Assert.Equal(120, Assert.Single(summary!.Places).Minutes, 3);
    }

    [Fact]
    public async Task A_trip_across_local_midnight_is_kept()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (pid, key, deviceId) = await SetupDeviceAsync(api);
        var (day, midnight) = PastMidnight();

        var lines = new List<string>();
        long seq = 1;
        for (var k = 0; k < 10; k++) lines.Add(Fix(seq++, midnight.AddMinutes(-60 + (k * 5)), 59.300, 18.000));
        lines.Add(Fix(seq++, midnight.AddMinutes(-5), 59.310, 18.000));
        lines.Add(Fix(seq++, midnight.AddMinutes(5), 59.330, 18.000));
        for (var k = 0; k < 10; k++) lines.Add(Fix(seq++, midnight.AddMinutes(15 + (k * 5)), 59.340, 18.000));
        await IngestLocationAsync(key, lines);
        await RollupAsync(pid, deviceId, day.AddDays(-1));
        await RollupAsync(pid, deviceId, day);

        var trip = Assert.Single((await api.GetFromJsonAsync<List<LocationTripDto>>($"/location/trips?from={Q(midnight.AddHours(-2))}&to={Q(midnight.AddHours(2))}"))!);
        Assert.True(trip.StartTs < midnight && trip.EndTs > midnight);
        Assert.Equal(2, (await api.GetFromJsonAsync<List<LocationVisitDto>>($"/location/visits?from={Q(midnight.AddHours(-2))}&to={Q(midnight.AddHours(2))}"))!.Count);
    }

    [Fact]
    public async Task Trip_mode_and_speed_come_from_displacement()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (pid, key, deviceId) = await SetupDeviceAsync(api);
        var (day, baseTs) = SafeDay();

        var lines = new List<string>();
        long seq = 1;
        for (var k = 0; k < 9; k++) lines.Add(Fix(seq++, baseTs.AddMinutes(k * 2), 59.300, 18.000));
        lines.Add(Fix(seq++, baseTs.AddMinutes(20), 59.330, 18.000));
        lines.Add(Fix(seq++, baseTs.AddMinutes(28), 59.360, 18.000));
        for (var k = 0; k < 9; k++) lines.Add(Fix(seq++, baseTs.AddMinutes(34 + (k * 2)), 59.380, 18.000));
        await IngestLocationAsync(key, lines);
        await RollupAsync(pid, deviceId, day);

        var trip = Assert.Single((await api.GetFromJsonAsync<List<LocationTripDto>>($"/location/trips?from={Q(baseTs)}&to={Q(baseTs.AddHours(2))}"))!);
        Assert.Equal(MotionActivity.Vehicle, trip.DominantActivity);
        Assert.Equal(trip.DistanceM / trip.DurationS, trip.AvgSpeedMps, 6);
    }

    [Fact]
    public async Task Rollup_pass_rebuilds_the_retention_window_on_a_version_bump()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, _) = await SetupDeviceAsync(api);
        var old = DateTimeOffset.UtcNow.AddDays(-10);

        var lines = new List<string>();
        for (var k = 0; k < 9; k++) lines.Add(Fix(k + 1, old.AddMinutes(k * 2), 59.300, 18.000));
        await IngestLocationAsync(key, lines);

        using (var scope = Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<LocationRollupPass>().RunAsync(DateTimeOffset.UtcNow, 90);

        Assert.Single((await api.GetFromJsonAsync<List<LocationVisitDto>>($"/location/visits?from={Q(old.AddHours(-1))}&to={Q(old.AddHours(1))}"))!);
        await using var session = Store.QuerySession();
        var checkpoint = await session.LoadAsync<LocationRollupCheckpoint>(LocationRollupCheckpoint.SingletonId);
        Assert.Equal(TripVisitService.AlgorithmVersion, checkpoint!.AlgorithmVersion);
    }

    /// <summary>A local midnight three days back, and the local day it starts.</summary>
    private static (DateOnly Day, DateTimeOffset Midnight) PastMidnight()
    {
        var day = LocalDay.Of(DateTimeOffset.UtcNow, Tz).AddDays(-3);
        return (day, LocalDay.StartUtc(day, Tz));
    }
}
