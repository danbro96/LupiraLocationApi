using System.Net;
using System.Net.Http.Json;
using LupiraLocationApi.Dtos.Location;
using Xunit;

namespace LupiraLocationApi.IntegrationTests;

public sealed class LocationTrackingTests(LocationApiTestFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Pause_then_state_reports_paused_with_reason()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, _, deviceId) = await SetupDeviceAsync(api);

        Assert.Equal(HttpStatusCode.NoContent, (await api.PostAsJsonAsync($"/location/tracking/{deviceId}/pause", new PauseTrackingRequest { Reason = "battery saver" })).StatusCode);
        var state = await api.GetFromJsonAsync<TrackingStateDto>($"/location/tracking/{deviceId}/state");
        Assert.True(state!.Paused);
        Assert.Equal("battery saver", state.Reason);
        Assert.NotNull(state.PausedAt);
    }

    [Fact]
    public async Task Resume_clears_paused_and_reason()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, _, deviceId) = await SetupDeviceAsync(api);
        await api.PostAsJsonAsync($"/location/tracking/{deviceId}/pause", new PauseTrackingRequest { Reason = "x" });
        await api.PostAsync($"/location/tracking/{deviceId}/resume", null);

        var state = await api.GetFromJsonAsync<TrackingStateDto>($"/location/tracking/{deviceId}/state");
        Assert.False(state!.Paused);
        Assert.Null(state.Reason);
        Assert.Null(state.PausedAt);
    }

    [Fact]
    public async Task Pause_is_idempotent()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, _, deviceId) = await SetupDeviceAsync(api);
        await api.PostAsync($"/location/tracking/{deviceId}/pause", null);
        Assert.Equal(HttpStatusCode.NoContent, (await api.PostAsync($"/location/tracking/{deviceId}/pause", null)).StatusCode);
        Assert.True((await api.GetFromJsonAsync<TrackingStateDto>($"/location/tracking/{deviceId}/state"))!.Paused);
    }

    [Fact]
    public async Task Resume_without_prior_pause_is_ok()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, _, deviceId) = await SetupDeviceAsync(api);
        Assert.Equal(HttpStatusCode.NoContent, (await api.PostAsync($"/location/tracking/{deviceId}/resume", null)).StatusCode);
    }

    [Fact]
    public async Task State_for_unknown_device_is_not_paused()
    {
        var api = Factory.ApiClient("alice@x.test");
        await GetMeAsync(api);
        var state = await api.GetFromJsonAsync<TrackingStateDto>($"/location/tracking/{Guid.NewGuid()}/state");
        Assert.False(state!.Paused);
    }

    [Fact]
    public async Task Ingest_state_endpoint_reflects_pause()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, deviceId) = await SetupDeviceAsync(api);
        await api.PostAsync($"/location/tracking/{deviceId}/pause", null);
        var state = await key.GetFromJsonAsync<TrackingStateDto>("/ingest/location/state");
        Assert.True(state!.Paused);
    }
}
