using System.Text.Json;
using LupiraLocationApi.Domain.Telemetry;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LupiraLocationApi.Application.Telemetry;

/// <summary>Reverse-geocodes a coordinate to a place label, resolve-once-and-freeze into a cache keyed by a quantized
/// (~100 m) coordinate. Uses a self-hosted Nominatim if <c>Nominatim:BaseUrl</c> is configured; otherwise (and on any
/// failure) returns null — it never blocks ingest and never calls an external service.</summary>
public sealed class PlaceLabelService(IDocumentSession session, IConfiguration config, ILogger<PlaceLabelService> logger)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public async Task<string?> ResolveAsync(double lat, double lon, CancellationToken ct = default)
    {
        var id = PlaceLabel.MakeId(lat, lon);
        var cached = await session.LoadAsync<PlaceLabel>(id, ct);
        if (cached is not null) return cached.Label;

        var baseUrl = config["Nominatim:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;

        try
        {
            var (qlat, qlon) = PlaceLabel.Quantize(lat, lon);
            var url = $"{baseUrl.TrimEnd('/')}/reverse?format=jsonv2&lat={qlat.ToString(System.Globalization.CultureInfo.InvariantCulture)}&lon={qlon.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("LupiraLocationApi/1.0");
            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("display_name", out var dn) || dn.ValueKind != JsonValueKind.String) return null;
            var label = dn.GetString()!;

            session.Store(new PlaceLabel { Id = id, Lat = qlat, Lon = qlon, Label = label, Source = "nominatim", ResolvedAt = DateTimeOffset.UtcNow });
            await session.SaveChangesAsync(ct);
            return label;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reverse-geocode failed for ({Lat},{Lon}); returning cache-only.", lat, lon);
            return null;
        }
    }
}
