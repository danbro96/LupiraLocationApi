using System.Net;
using System.Net.Http.Json;
using LupiraLocationApi.Domain;
using LupiraLocationApi.Dtos.Devices;
using Xunit;

namespace LupiraLocationApi.Server.Tests;

/// <summary>Cross-cutting authentication: unauthenticated requests are rejected, and each surface only accepts its own
/// auth scheme (OIDC for the REST surface, device key for /ingest).</summary>
public sealed class AccessTests(LocationApiTestFactory factory) : IntegrationTest(factory)
{
    [Theory]
    [InlineData("/me")]
    [InlineData("/devices")]
    [InlineData("/location/current")]
    public async Task Unauthenticated_reads_are_rejected(string url)
    {
        var anon = Factory.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_writes_are_rejected()
    {
        var anon = Factory.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/devices", new RegisterDeviceRequest { Kind = DeviceKind.Phone, Label = "x" })).StatusCode);
    }

    [Fact]
    public async Task Ingest_requires_a_device_key_not_an_api_token()
    {
        // An OIDC/dev-authed client (ApiPolicy) must not be able to hit the device-key-only ingest surface.
        var api = Factory.ApiClient("alice@x.test");
        await GetMeAsync(api);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostNdjson(api, "/ingest/location", [Fix(1, DateTimeOffset.UtcNow.AddMinutes(-1), 59.3, 18.0)])).StatusCode);
    }

    [Fact]
    public async Task Ingest_with_malformed_or_unknown_key_is_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostNdjson(Factory.DeviceKeyClient("garbage"), "/ingest/location", [Fix(1, DateTimeOffset.UtcNow, 59.3, 18.0)])).StatusCode);
        var wellFormedUnknown = $"{Guid.NewGuid():N}.{new string('a', 64)}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostNdjson(Factory.DeviceKeyClient(wellFormedUnknown), "/ingest/location", [Fix(1, DateTimeOffset.UtcNow, 59.3, 18.0)])).StatusCode);
    }

    [Fact]
    public async Task Api_endpoints_reject_a_device_key()
    {
        var api = Factory.ApiClient("alice@x.test");
        var reg = await RegisterDeviceAsync(api);
        var key = Factory.DeviceKeyClient(reg.ApiKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await key.GetAsync("/location/current")).StatusCode);
    }
}
