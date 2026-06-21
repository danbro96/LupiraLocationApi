using System.Net;
using System.Net.Http.Json;
using LupiraLocationApi.Dtos.Location;
using Xunit;

namespace LupiraLocationApi.Server.Tests;

public sealed class LocationIngestTests(LocationApiTestFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Ingest_then_current_and_track()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, _) = await SetupDeviceAsync(api);
        var now = DateTimeOffset.UtcNow;
        var receipt = await IngestLocationAsync(key,
        [
            Fix(1, now.AddMinutes(-3), 59.300, 18.000),
            Fix(2, now.AddMinutes(-2), 59.301, 18.001),
            Fix(3, now.AddMinutes(-1), 59.302, 18.002),
        ]);
        Assert.Equal(3, receipt.Inserted);
        Assert.Equal(0, receipt.Duplicates);
        Assert.Equal(3, receipt.HighWaterSeq);

        var current = await api.GetFromJsonAsync<List<CurrentFixDto>>("/api/location/current");
        Assert.Single(current!);
        Assert.Equal(59.302, current![0].Lat, 3);

        var track = await api.GetFromJsonAsync<List<TrackPointDto>>($"/api/location/track?from={Q(now.AddHours(-1))}&to={Q(now.AddMinutes(1))}");
        Assert.Equal(3, track!.Count);
        Assert.True(track[0].Ts <= track[1].Ts && track[1].Ts <= track[2].Ts);
    }

    [Fact]
    public async Task Re_ingest_is_idempotent()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, _) = await SetupDeviceAsync(api);
        var now = DateTimeOffset.UtcNow;
        var batch = new[] { Fix(1, now.AddMinutes(-3), 59.3, 18.0), Fix(2, now.AddMinutes(-2), 59.3, 18.0), Fix(3, now.AddMinutes(-1), 59.3, 18.0) };

        Assert.Equal(3, (await IngestLocationAsync(key, batch)).Inserted);
        var second = await IngestLocationAsync(key, batch);
        Assert.Equal(0, second.Inserted);
        Assert.Equal(3, second.Duplicates);
    }

    [Fact]
    public async Task Cursor_reports_high_water()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, deviceId) = await SetupDeviceAsync(api);
        var now = DateTimeOffset.UtcNow;
        await IngestLocationAsync(key, Enumerable.Range(1, 5).Select(i => Fix(i, now.AddMinutes(-10 + i), 59.3, 18.0)));

        var cursor = await key.GetFromJsonAsync<LocationCursor>($"/api/ingest/location/cursor?deviceId={deviceId}");
        Assert.Equal(5, cursor!.LastSeq);
    }

    [Fact]
    public async Task Cursor_for_fresh_device_is_null()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, deviceId) = await SetupDeviceAsync(api);
        var cursor = await key.GetFromJsonAsync<LocationCursor>($"/api/ingest/location/cursor?deviceId={deviceId}");
        Assert.Null(cursor!.LastSeq);
        Assert.Null(cursor.LastTs);
    }

    [Fact]
    public async Task State_for_fresh_device_is_not_paused()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, _) = await SetupDeviceAsync(api);
        var state = await key.GetFromJsonAsync<TrackingStateDto>("/api/ingest/location/state");
        Assert.False(state!.Paused);
    }

    [Fact]
    public async Task Out_of_order_batch_does_not_rewind_current_and_bad_line_rejected()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, deviceId) = await SetupDeviceAsync(api);
        var now = DateTimeOffset.UtcNow;

        await IngestLocationAsync(key, [Fix(100, now.AddMinutes(-3), 59.3, 18.0), Fix(101, now.AddMinutes(-2), 59.3, 18.0), Fix(102, now.AddMinutes(-1), 59.3, 18.0)]);

        var receipt = await IngestLocationAsync(key, [Fix(1, now.AddMinutes(-30), 59.3, 18.0), "{not valid json"]);
        Assert.Equal(1, receipt.Inserted);
        Assert.Equal(1, receipt.Rejected);

        var cursor = await key.GetFromJsonAsync<LocationCursor>($"/api/ingest/location/cursor?deviceId={deviceId}");
        Assert.Equal(102, cursor!.LastSeq);
    }

    [Fact]
    public async Task Invalid_json_line_is_rejected()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, _) = await SetupDeviceAsync(api);
        var receipt = await IngestLocationAsync(key, ["{bad"]);
        Assert.Equal(0, receipt.Inserted);
        Assert.Equal("invalid_json", receipt.Rejects[0].Reason);
    }

    [Fact]
    public async Task Missing_seq_is_rejected()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, _) = await SetupDeviceAsync(api);
        var line = $"{{\"ts\":\"{DateTimeOffset.UtcNow.AddMinutes(-1):O}\",\"lat\":59.3,\"lon\":18.0}}";
        Assert.Equal("missing_seq", (await IngestLocationAsync(key, [line])).Rejects[0].Reason);
    }

    [Fact]
    public async Task Missing_latlon_is_rejected()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, _) = await SetupDeviceAsync(api);
        var line = $"{{\"seq\":1,\"ts\":\"{DateTimeOffset.UtcNow.AddMinutes(-1):O}\"}}";
        Assert.Equal("missing_latlon", (await IngestLocationAsync(key, [line])).Rejects[0].Reason);
    }

    [Theory]
    [InlineData(91.0, 18.0)]
    [InlineData(-91.0, 18.0)]
    [InlineData(59.3, 181.0)]
    [InlineData(59.3, -181.0)]
    public async Task Out_of_range_latlon_is_rejected(double lat, double lon)
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, _) = await SetupDeviceAsync(api);
        var receipt = await IngestLocationAsync(key, [Fix(1, DateTimeOffset.UtcNow.AddMinutes(-1), lat, lon)]);
        Assert.Equal(0, receipt.Inserted);
        Assert.Equal("invalid_latlon", receipt.Rejects[0].Reason);
    }

    [Fact]
    public async Task Future_timestamp_is_rejected()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, _) = await SetupDeviceAsync(api);
        var receipt = await IngestLocationAsync(key, [Fix(1, DateTimeOffset.UtcNow.AddHours(1), 59.3, 18.0)]);
        Assert.Equal("ts_out_of_range", receipt.Rejects[0].Reason);
    }

    [Fact]
    public async Task Too_old_timestamp_is_rejected()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, _) = await SetupDeviceAsync(api);
        var receipt = await IngestLocationAsync(key, [Fix(1, DateTimeOffset.UtcNow.AddDays(-120), 59.3, 18.0)]);
        Assert.Equal("ts_out_of_range", receipt.Rejects[0].Reason);
    }

    [Fact]
    public async Task Body_with_device_id_is_rejected()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, _) = await SetupDeviceAsync(api);
        var line = $"{{\"seq\":1,\"ts\":\"{DateTimeOffset.UtcNow.AddMinutes(-1):O}\",\"lat\":59.3,\"lon\":18.0,\"device_id\":\"{Guid.NewGuid()}\"}}";
        Assert.Equal("body_ids_forbidden", (await IngestLocationAsync(key, [line])).Rejects[0].Reason);
    }

    [Fact]
    public async Task Empty_lines_are_skipped()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, _) = await SetupDeviceAsync(api);
        var now = DateTimeOffset.UtcNow;
        var receipt = await IngestLocationAsync(key, ["", Fix(1, now.AddMinutes(-1), 59.3, 18.0), "   ", Fix(2, now, 59.3, 18.0)]);
        Assert.Equal(2, receipt.Submitted);
        Assert.Equal(2, receipt.Inserted);
        Assert.Equal(0, receipt.Rejected);
    }

    [Fact]
    public async Task Late_fix_in_an_old_partition_still_inserts()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, _) = await SetupDeviceAsync(api);
        var now = DateTimeOffset.UtcNow;
        // A few weeks back (well within 90d retention) — lands in an older weekly partition created on demand.
        var receipt = await IngestLocationAsync(key, [Fix(1, now.AddDays(-21), 59.3, 18.0)]);
        Assert.Equal(1, receipt.Inserted);
    }

    [Fact]
    public async Task Paused_tracking_discards_ingest()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, deviceId) = await SetupDeviceAsync(api);
        Assert.Equal(HttpStatusCode.NoContent, (await api.PostAsync($"/api/location/tracking/{deviceId}/pause", null)).StatusCode);

        var receipt = await IngestLocationAsync(key, [Fix(1, DateTimeOffset.UtcNow.AddMinutes(-1), 59.3, 18.0)]);
        Assert.True(receipt.Paused);
        Assert.Empty((await api.GetFromJsonAsync<List<CurrentFixDto>>("/api/location/current"))!);

        await api.PostAsync($"/api/location/tracking/{deviceId}/resume", null);
        Assert.Equal(1, (await IngestLocationAsync(key, [Fix(2, DateTimeOffset.UtcNow.AddMinutes(-1), 59.3, 18.0)])).Inserted);
    }
}
