using LupiraLocationApi.Handlers;

namespace LupiraLocationApi.Endpoints;

/// <summary>Service-to-service seams (LAN-only: the CF-header backstop 404s tunnelled hits). Excluded from
/// the public OpenAPI document. Consumer today: lupira-photo-api's no-EXIF-GPS geotag fallback.</summary>
public static class InternalEndpoints
{
    public static IEndpointRouteBuilder MapInternal(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/internal/location/place-at",
                (string sub, DateTimeOffset ts, InternalLocationHandler h, CancellationToken ct) => h.PlaceAtAsync(sub, ts, ct))
            .RequireAuthorization("InternalPolicy")
            .ExcludeFromDescription()
            .WithName("GetPlaceAt");
        return app;
    }
}
