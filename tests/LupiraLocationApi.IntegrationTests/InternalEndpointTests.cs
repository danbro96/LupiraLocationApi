using System.Net;
using System.Net.Http.Json;
using LupiraLocationApi.Dtos.Location;
using Xunit;

namespace LupiraLocationApi.IntegrationTests;

/// <summary>The /internal/location/place-at seam (consumer: lupira-photo-api's geotag fallback).</summary>
public class InternalEndpointTests(LocationApiTestFactory factory) : IntegrationTest(factory)
{
    private HttpClient ServiceClient()
    {
        var client = Factory.ApiClient("photo-svc@internal.test");
        client.DefaultRequestHeaders.Add("X-Dev-Scopes", "internal:read");
        return client;
    }

    [Fact]
    public async Task PlaceAt_ResolvesSubAndReturnsQuantizedFixMatch()
    {
        var api = Factory.ApiClient("alice@x.test");
        var (_, key, _) = await SetupDeviceAsync(api);
        var ts = DateTimeOffset.UtcNow.AddHours(-1);
        await IngestLocationAsync(key, [Fix(1, ts, 59.3300, 18.0700)]);

        var svc = ServiceClient();
        var resp = await svc.GetAsync($"/internal/location/place-at?sub={Uri.EscapeDataString("dev|alice@x.test")}&ts={Uri.EscapeDataString(ts.UtcDateTime.ToString("O"))}");
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<PlaceLabelAtDto>())!;

        Assert.Equal("fix", dto.Source);
        // Quantized (~100 m), never the raw fix.
        Assert.Equal(59.33, dto.Lat, 1);
        Assert.Equal(18.07, dto.Lon, 1);
    }

    [Fact]
    public async Task PlaceAt_UnknownSub_Is404_AndProvisionsNothing()
    {
        var resp = await ServiceClient().GetAsync($"/internal/location/place-at?sub=unknown-sub&ts={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);

        await using var session = Store.LightweightSession();
        Assert.Empty(await Marten.QueryableExtensions.ToListAsync(
            session.Query<Domain.Identity.Principal>().Where(p => p.AuthentikSub == "unknown-sub")));
    }

    [Fact]
    public async Task PlaceAt_WithoutInternalScope_IsForbidden()
    {
        var user = Factory.ApiClient("alice@x.test");
        var resp = await user.GetAsync($"/internal/location/place-at?sub=x&ts={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task PlaceAt_NoData_ReturnsSourceNone()
    {
        // Provision the principal via /me first.
        var api = Factory.ApiClient("bob@x.test");
        await GetMyIdAsync(api);

        var resp = await ServiceClient().GetAsync($"/internal/location/place-at?sub={Uri.EscapeDataString("dev|bob@x.test")}&ts={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}");
        resp.EnsureSuccessStatusCode();
        Assert.Equal("none", (await resp.Content.ReadFromJsonAsync<PlaceLabelAtDto>())!.Source);
    }
}
