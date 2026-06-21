using System.Net;
using System.Net.Http.Json;
using LupiraLocationApi.Domain;
using LupiraLocationApi.Dtos.Devices;
using Xunit;

namespace LupiraLocationApi.Server.Tests;

public sealed class DevicesTests(LocationApiTestFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Register_then_list_rename_retire_lifecycle()
    {
        var api = Factory.ApiClient("alice@x.test");

        var reg = await RegisterDeviceAsync(api, "Tracker", "GPS Tracker");
        Assert.False(string.IsNullOrWhiteSpace(reg.ApiKey));
        Assert.Contains('.', reg.ApiKey);
        Assert.Equal(DeviceKind.Tracker, reg.Device.Kind);

        Assert.Single((await api.GetFromJsonAsync<List<DeviceDto>>("/api/devices"))!);

        var rename = await api.PutAsJsonAsync($"/api/devices/{reg.Device.Id}", new RenameDeviceRequest { Label = "Trip Tracker" });
        rename.EnsureSuccessStatusCode();
        Assert.Equal("Trip Tracker", (await rename.Content.ReadFromJsonAsync<DeviceDto>())!.Label);

        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"/api/devices/{reg.Device.Id}")).StatusCode);
    }

    [Fact]
    public async Task Register_with_empty_label_is_400()
    {
        var api = Factory.ApiClient("alice@x.test");
        var resp = await api.PostAsJsonAsync("/api/devices", new RegisterDeviceRequest { Kind = DeviceKind.Phone, Label = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Register_with_unknown_kind_is_400()
    {
        var api = Factory.ApiClient("alice@x.test");
        // Raw JSON: an out-of-range DeviceKind name fails enum deserialization (no longer a service-layer branch).
        var resp = await api.PostAsJsonAsync("/api/devices", new { kind = "Toaster", label = "Kitchen" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Devices_are_isolated_per_user()
    {
        var a = Factory.ApiClient("a@x.test");
        var regA = await RegisterDeviceAsync(a);

        var b = Factory.ApiClient("b@x.test");
        var bList = (await b.GetFromJsonAsync<List<DeviceDto>>("/api/devices"))!;
        Assert.DoesNotContain(bList, d => d.Id == regA.Device.Id);
        Assert.Empty(bList);
    }

    [Fact]
    public async Task Another_user_cannot_rename_or_retire_my_device()
    {
        var a = Factory.ApiClient("a@x.test");
        var regA = await RegisterDeviceAsync(a);

        var b = Factory.ApiClient("b@x.test");
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsJsonAsync($"/api/devices/{regA.Device.Id}", new RenameDeviceRequest { Label = "Hijack" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/devices/{regA.Device.Id}")).StatusCode);
    }

    [Fact]
    public async Task Rename_missing_device_is_404()
    {
        var api = Factory.ApiClient("alice@x.test");
        var resp = await api.PutAsJsonAsync($"/api/devices/{Guid.NewGuid()}", new RenameDeviceRequest { Label = "X" });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Rename_with_empty_label_is_400()
    {
        var api = Factory.ApiClient("alice@x.test");
        var reg = await RegisterDeviceAsync(api);
        var resp = await api.PutAsJsonAsync($"/api/devices/{reg.Device.Id}", new RenameDeviceRequest { Label = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Retire_missing_device_is_404()
    {
        var api = Factory.ApiClient("alice@x.test");
        Assert.Equal(HttpStatusCode.NotFound, (await api.DeleteAsync($"/api/devices/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Retire_twice_is_404_on_second()
    {
        var api = Factory.ApiClient("alice@x.test");
        var reg = await RegisterDeviceAsync(api);
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"/api/devices/{reg.Device.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.DeleteAsync($"/api/devices/{reg.Device.Id}")).StatusCode);
    }

    [Fact]
    public async Task Retired_device_key_can_no_longer_ingest()
    {
        var api = Factory.ApiClient("alice@x.test");
        var reg = await RegisterDeviceAsync(api);
        var key = Factory.DeviceKeyClient(reg.ApiKey);

        Assert.Equal(HttpStatusCode.Accepted, (await PostNdjson(key, "/api/ingest/location", [Fix(1, DateTimeOffset.UtcNow.AddMinutes(-1), 59.3, 18.0)])).StatusCode);
        await api.DeleteAsync($"/api/devices/{reg.Device.Id}");
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostNdjson(key, "/api/ingest/location", [Fix(2, DateTimeOffset.UtcNow, 59.3, 18.0)])).StatusCode);
    }
}
