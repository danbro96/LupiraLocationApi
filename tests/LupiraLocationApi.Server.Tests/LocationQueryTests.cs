using System.Net.Http.Json;
using LupiraLocationApi.Dtos.Location;
using Xunit;

namespace LupiraLocationApi.Server.Tests;

public sealed class LocationQueryTests(LocationApiTestFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Current_filters_by_device_and_lists_all_without_filter()
    {
        var api = Factory.ApiClient("alice@x.test");
        var d1 = await RegisterDeviceAsync(api, "Phone", "Phone");
        var d2 = await RegisterDeviceAsync(api, "Watch", "Watch");
        var now = DateTimeOffset.UtcNow;
        await IngestLocationAsync(Factory.DeviceKeyClient(d1.ApiKey), [Fix(1, now.AddMinutes(-2), 59.30, 18.00)]);
        await IngestLocationAsync(Factory.DeviceKeyClient(d2.ApiKey), [Fix(1, now.AddMinutes(-1), 60.00, 19.00)]);

        Assert.Equal(2, (await api.GetFromJsonAsync<List<CurrentFixDto>>("/api/location/current"))!.Count);
        var one = await api.GetFromJsonAsync<List<CurrentFixDto>>($"/api/location/current?deviceId={d1.Device.Id}");
        Assert.Single(one!);
        Assert.Equal(d1.Device.Id, one![0].DeviceId);
    }

    [Fact]
    public async Task Track_defaults_to_last_day_when_no_range()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, _) = await SetupDeviceAsync(api);
        await IngestLocationAsync(key, [Fix(1, DateTimeOffset.UtcNow.AddMinutes(-5), 59.3, 18.0)]);
        Assert.Single((await api.GetFromJsonAsync<List<TrackPointDto>>("/api/location/track"))!);
    }

    [Fact]
    public async Task Thinned_track_collapses_density()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, _) = await SetupDeviceAsync(api);
        var (_, baseTs) = SafeDay();
        await IngestLocationAsync(key, Enumerable.Range(0, 12).Select(i => Fix(i + 1, baseTs.AddSeconds(i * 5), 59.300 + i * 0.0001, 18.000)));

        var raw = await api.GetFromJsonAsync<List<TrackPointDto>>($"/api/location/track?from={Q(baseTs.AddMinutes(-1))}&to={Q(baseTs.AddMinutes(10))}");
        var thinned = await api.GetFromJsonAsync<List<TrackPointDto>>($"/api/location/track/thinned?bucketSeconds=30&from={Q(baseTs.AddMinutes(-1))}&to={Q(baseTs.AddMinutes(10))}");
        Assert.Equal(12, raw!.Count);
        Assert.True(thinned!.Count < raw.Count);
        Assert.NotEmpty(thinned);
    }

    [Fact]
    public async Task Bbox_returns_only_fixes_in_the_rectangle()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, _) = await SetupDeviceAsync(api);
        var (_, baseTs) = SafeDay();
        await IngestLocationAsync(key, [Fix(1, baseTs, 59.30, 18.00), Fix(2, baseTs.AddMinutes(1), 60.50, 19.50)]);

        var from = Q(baseTs.AddMinutes(-1));
        var to = Q(baseTs.AddMinutes(10));
        var inBox = await api.GetFromJsonAsync<List<TrackPointDto>>($"/api/location/bbox?minLat=59.2&maxLat=59.4&minLon=17.9&maxLon=18.1&from={from}&to={to}");
        Assert.Single(inBox!);
        Assert.Equal(59.30, inBox![0].Lat, 2);

        var empty = await api.GetFromJsonAsync<List<TrackPointDto>>($"/api/location/bbox?minLat=10&maxLat=11&minLon=10&maxLon=11&from={from}&to={to}");
        Assert.Empty(empty!);
    }

    [Fact]
    public async Task Stats_reports_a_plausible_distance()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, _) = await SetupDeviceAsync(api);
        var (_, baseTs) = SafeDay();
        await IngestLocationAsync(key, Enumerable.Range(0, 5).Select(i => Fix(i + 1, baseTs.AddMinutes(i), 59.300 + i * 0.0025, 18.000, speed: 3.0)));

        var stats = await api.GetFromJsonAsync<TrackStatsDto>($"/api/location/stats?from={Q(baseTs.AddMinutes(-1))}&to={Q(baseTs.AddMinutes(10))}");
        Assert.True(stats!.DistanceM > 500, $"distance was {stats.DistanceM}");
        Assert.Equal(5, stats.SampleCount);
    }

    [Fact]
    public async Task Stats_excludes_mock_and_low_accuracy_fixes()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, _) = await SetupDeviceAsync(api);
        var (_, baseTs) = SafeDay();
        // All fixes are either mock or low-accuracy → excluded from stats.
        await IngestLocationAsync(key,
        [
            Fix(1, baseTs, 59.300, 18.000, accuracy: 200),
            Fix(2, baseTs.AddMinutes(1), 59.310, 18.010, isMock: true),
            Fix(3, baseTs.AddMinutes(2), 59.320, 18.020, accuracy: 500),
        ]);
        var stats = await api.GetFromJsonAsync<TrackStatsDto>($"/api/location/stats?from={Q(baseTs.AddMinutes(-1))}&to={Q(baseTs.AddMinutes(10))}");
        Assert.Equal(0, stats!.DistanceM);
        Assert.Equal(0, stats.SampleCount);
    }

    [Fact]
    public async Task Stats_empty_range_returns_zeroes()
    {
        var api = Factory.ApiClient("alice@x.test");
        await SetupDeviceAsync(api);
        var now = DateTimeOffset.UtcNow;
        var stats = await api.GetFromJsonAsync<TrackStatsDto>($"/api/location/stats?from={Q(now.AddHours(-1))}&to={Q(now)}");
        Assert.Equal(0, stats!.DistanceM);
        Assert.Equal(0, stats.SampleCount);
        Assert.Null(stats.AvgSpeedMps);
    }

    [Fact]
    public async Task Visits_trips_summary_empty_when_no_data()
    {
        var api = Factory.ApiClient("alice@x.test");
        await SetupDeviceAsync(api);
        var (day, baseTs) = SafeDay();
        var from = Q(baseTs.AddHours(-1));
        var to = Q(baseTs.AddHours(2));
        Assert.Empty((await api.GetFromJsonAsync<List<LocationVisitDto>>($"/api/location/visits?from={from}&to={to}"))!);
        Assert.Empty((await api.GetFromJsonAsync<List<LocationTripDto>>($"/api/location/trips?from={from}&to={to}"))!);
        var summary = await api.GetFromJsonAsync<DailyLocationSummaryDto>($"/api/location/summary?date={day:yyyy-MM-dd}");
        Assert.Equal(0, summary!.VisitCount);
        Assert.Equal(0, summary.DistanceM);
    }

    [Fact]
    public async Task Rollup_produces_visits_trips_and_summary()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (pid, key, deviceId) = await SetupDeviceAsync(api);
        var (day, baseTs) = SafeDay();

        await IngestLocationAsync(key, BuildTwoVisitTrack(baseTs));
        await RollupAsync(pid, deviceId, day);

        var from = Q(baseTs.AddHours(-1));
        var to = Q(baseTs.AddHours(2));
        Assert.Equal(2, (await api.GetFromJsonAsync<List<LocationVisitDto>>($"/api/location/visits?from={from}&to={to}"))!.Count);
        Assert.True((await api.GetFromJsonAsync<List<LocationTripDto>>($"/api/location/trips?from={from}&to={to}"))!.Count >= 1);
        var summary = await api.GetFromJsonAsync<DailyLocationSummaryDto>($"/api/location/summary?date={day:yyyy-MM-dd}");
        Assert.Equal(2, summary!.VisitCount);
        Assert.True(summary.DistanceM > 0);
    }

    [Fact]
    public async Task At_returns_visit_when_inside_a_visit_window()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (pid, key, deviceId) = await SetupDeviceAsync(api);
        var (day, baseTs) = SafeDay();
        await IngestLocationAsync(key, BuildTwoVisitTrack(baseTs));
        await RollupAsync(pid, deviceId, day);

        var at = await api.GetFromJsonAsync<PlaceLabelAtDto>($"/api/location/at?ts={Q(baseTs.AddMinutes(8))}");
        Assert.Equal("visit", at!.Source);
        Assert.True(at.Lat != 0);   // coarse (quantized) coordinate, never the raw fix
    }

    [Fact]
    public async Task At_returns_nearest_fix_when_no_visit()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, _) = await SetupDeviceAsync(api);
        var (_, baseTs) = SafeDay();
        await IngestLocationAsync(key, [Fix(1, baseTs, 59.300, 18.000)]);  // single fix → no visit
        var at = await api.GetFromJsonAsync<PlaceLabelAtDto>($"/api/location/at?ts={Q(baseTs)}");
        Assert.Equal("fix", at!.Source);
    }

    [Fact]
    public async Task At_returns_none_when_no_data()
    {
        var api = Factory.ApiClient("alice@x.test");
        await SetupDeviceAsync(api);
        var at = await api.GetFromJsonAsync<PlaceLabelAtDto>($"/api/location/at?ts={Q(DateTimeOffset.UtcNow)}");
        Assert.Equal("none", at!.Source);
        Assert.Null(at.Label);
    }

    [Fact]
    public async Task Purge_removes_raw_derived_and_current_in_range()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (pid, key, deviceId) = await SetupDeviceAsync(api);
        var (day, baseTs) = SafeDay();
        await IngestLocationAsync(key, BuildTwoVisitTrack(baseTs));
        await RollupAsync(pid, deviceId, day);

        var from = Q(baseTs.AddHours(-1));
        var to = Q(baseTs.AddHours(2));
        var purge = await api.DeleteAsync($"/api/location?from={from}&to={to}");
        Assert.Equal(System.Net.HttpStatusCode.NoContent, purge.StatusCode);

        Assert.Empty((await api.GetFromJsonAsync<List<TrackPointDto>>($"/api/location/track?from={from}&to={to}"))!);
        Assert.Empty((await api.GetFromJsonAsync<List<LocationVisitDto>>($"/api/location/visits?from={from}&to={to}"))!);
        Assert.Empty((await api.GetFromJsonAsync<List<CurrentFixDto>>("/api/location/current"))!);
    }

    /// <summary>Two dwell clusters (Visit A, Visit B) with a short moving segment between → 2 visits + 1 trip.</summary>
    private static List<string> BuildTwoVisitTrack(DateTimeOffset baseTs)
    {
        var lines = new List<string>();
        long seq = 1;
        for (var k = 0; k < 9; k++) lines.Add(Fix(seq++, baseTs.AddMinutes(k * 2), 59.300, 18.000));
        lines.Add(Fix(seq++, baseTs.AddMinutes(20), 59.305, 18.010, speed: 8));
        lines.Add(Fix(seq++, baseTs.AddMinutes(22), 59.312, 18.030, speed: 8));
        for (var k = 0; k < 9; k++) lines.Add(Fix(seq++, baseTs.AddMinutes(26 + k * 2), 59.320, 18.050));
        return lines;
    }
}
