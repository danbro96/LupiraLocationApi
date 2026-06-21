using System.Net;
using System.Net.Http.Json;
using LupiraLocationApi.Dtos.Devices;
using LupiraLocationApi.Dtos.Location;
using Xunit;

namespace LupiraLocationApi.IntegrationTests;

/// <summary>Principal isolation: a user can only see their own telemetry and only manage their own devices. Telemetry
/// queries hard-filter principal_id = caller; device ops are ownership-checked.</summary>
public sealed class SecurityRegressionTests(LocationApiTestFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task UserB_cannot_see_user_As_location()
    {
        var a = Factory.ApiClient("a@x.test");
        var regA = await RegisterDeviceAsync(a);
        var keyA = Factory.DeviceKeyClient(regA.ApiKey);

        var now = DateTimeOffset.UtcNow;
        await IngestLocationAsync(keyA, [Fix(1, now.AddMinutes(-2), 59.30, 18.00), Fix(2, now.AddMinutes(-1), 59.31, 18.01)]);

        Assert.Single((await a.GetFromJsonAsync<List<CurrentFixDto>>("/location/current"))!);

        var b = Factory.ApiClient("b@x.test");
        Assert.Empty((await b.GetFromJsonAsync<List<CurrentFixDto>>("/location/current"))!);
        Assert.Empty((await b.GetFromJsonAsync<List<TrackPointDto>>($"/location/track?from={Q(now.AddHours(-1))}&to={Q(now.AddMinutes(1))}"))!);
    }

    [Fact]
    public async Task Foreign_device_id_in_query_returns_empty_not_others_data()
    {
        var a = Factory.ApiClient("a@x.test");
        var regA = await RegisterDeviceAsync(a);
        var now = DateTimeOffset.UtcNow;
        await IngestLocationAsync(Factory.DeviceKeyClient(regA.ApiKey), [Fix(1, now.AddMinutes(-1), 59.30, 18.00)]);

        // B passes A's real device id — the principal_id hard-filter means it still matches nothing.
        var b = Factory.ApiClient("b@x.test");
        Assert.Empty((await b.GetFromJsonAsync<List<CurrentFixDto>>($"/location/current?deviceId={regA.Device.Id}"))!);
    }

    [Fact]
    public async Task UserB_cannot_see_or_manage_user_As_devices()
    {
        var a = Factory.ApiClient("a@x.test");
        var regA = await RegisterDeviceAsync(a);

        var b = Factory.ApiClient("b@x.test");
        Assert.DoesNotContain((await b.GetFromJsonAsync<List<DeviceDto>>("/devices"))!, d => d.Id == regA.Device.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/devices/{regA.Device.Id}")).StatusCode);
    }
}
