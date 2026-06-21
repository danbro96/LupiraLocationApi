using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using LupiraLocationApi.Application.Telemetry;
using LupiraLocationApi.Domain;
using LupiraLocationApi.Dtos.Devices;
using LupiraLocationApi.Dtos.Location;
using LupiraLocationApi.Dtos.Me;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LupiraLocationApi.IntegrationTests;

/// <summary>Base for integration tests: shares the container fixture, resets all state before each test, and provides
/// REST + NDJSON-ingest helpers. Serial within the "integration" collection.</summary>
[Collection("integration")]
public abstract class IntegrationTest(LocationApiTestFactory factory) : IAsyncLifetime
{
    protected readonly LocationApiTestFactory Factory = factory;

    public async Task InitializeAsync() => await Factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ---- REST fixture helpers ----

    protected static async Task<MeDto> GetMeAsync(HttpClient api) => (await api.GetFromJsonAsync<MeDto>("/me"))!;
    protected static async Task<Guid> GetMyIdAsync(HttpClient api) => (await GetMeAsync(api)).Id;

    protected static async Task<RegisterDeviceResponse> RegisterDeviceAsync(HttpClient api, string kind = "Phone", string label = "My Phone")
    {
        var resp = await api.PostAsJsonAsync("/devices", new RegisterDeviceRequest { Kind = Enum.Parse<DeviceKind>(kind), Label = label });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<RegisterDeviceResponse>())!;
    }

    /// <summary>JIT-provisions the caller (via /me), registers a device, and returns its principal id + ingest-key client.</summary>
    protected async Task<(Guid Pid, HttpClient Key, Guid DeviceId)> SetupDeviceAsync(HttpClient api, string kind = "Phone")
    {
        var pid = await GetMyIdAsync(api);
        var reg = await RegisterDeviceAsync(api, kind);
        return (pid, Factory.DeviceKeyClient(reg.ApiKey), reg.Device.Id);
    }

    // ---- NDJSON ingest helpers ----

    protected static Task<HttpResponseMessage> PostNdjson(HttpClient client, string url, IEnumerable<string> lines)
    {
        var content = new StringContent(string.Join('\n', lines), Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");
        return client.PostAsync(url, content);
    }

    protected static async Task<LocationIngestReceipt> IngestLocationAsync(HttpClient key, IEnumerable<string> lines) =>
        (await (await PostNdjson(key, "/ingest/location", lines)).Content.ReadFromJsonAsync<LocationIngestReceipt>())!;

    /// <summary>Triggers the rollup directly (the maintenance BackgroundService is disabled in tests).</summary>
    protected async Task RollupAsync(Guid pid, Guid deviceId, DateOnly day)
    {
        using var scope = Factory.Services.CreateScope();
        var trips = scope.ServiceProvider.GetRequiredService<TripVisitService>();
        await trips.RollupDayAsync(pid, deviceId, day);
    }

    // ---- payload builders ----

    /// <summary>Builds one NDJSON location-fix line (doubles formatted invariant).</summary>
    protected static string Fix(long seq, DateTimeOffset ts, double lat, double lon, double accuracy = 5, double? speed = null,
        string provider = "gps", string activity = "walk", bool isMock = false)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{{\"seq\":{seq},\"ts\":\"{ts:O}\",\"lat\":{lat.ToString(CultureInfo.InvariantCulture)},\"lon\":{lon.ToString(CultureInfo.InvariantCulture)}");
        sb.Append(CultureInfo.InvariantCulture, $",\"accuracy_m\":{accuracy.ToString(CultureInfo.InvariantCulture)}");
        if (speed is not null) sb.Append(CultureInfo.InvariantCulture, $",\"speed_mps\":{speed.Value.ToString(CultureInfo.InvariantCulture)}");
        sb.Append(CultureInfo.InvariantCulture, $",\"provider\":\"{provider}\",\"activity\":\"{activity}\"");
        if (isMock) sb.Append(",\"is_mock\":true");
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>ISO-8601 query-string escaper for from/to params.</summary>
    protected static string Q(DateTimeOffset ts) => Uri.EscapeDataString(ts.ToString("O"));

    /// <summary>A safe in-the-past day anchor that never crosses midnight or lands in the future regardless of run time.</summary>
    protected static (DateOnly Day, DateTimeOffset Base) SafeDay()
    {
        var now = DateTimeOffset.UtcNow;
        var day = now.Hour >= 2 ? DateOnly.FromDateTime(now.UtcDateTime) : DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1);
        return (day, new DateTimeOffset(day.Year, day.Month, day.Day, 0, 30, 0, TimeSpan.Zero));
    }
}
