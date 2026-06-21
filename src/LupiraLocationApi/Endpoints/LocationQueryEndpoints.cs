using LupiraLocationApi.Dtos.Location;
using LupiraLocationApi.Handlers;

namespace LupiraLocationApi.Endpoints;

/// <summary>The owner-facing location read + tracking-control surface (OIDC-authed). Raw track is owner-only; only the
/// coarse <c>/at</c> place label is synergy-safe.</summary>
public static class LocationQueryEndpoints
{
    public static IEndpointRouteBuilder MapLocationQuery(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/location").RequireAuthorization("ApiPolicy").WithTags("Location");

        g.MapGet("/current", (Guid? deviceId, LocationQueryHandler h, CancellationToken ct) => h.CurrentAsync(deviceId, ct))
            .WithSummary("Latest known location per device.").Produces<List<CurrentFixDto>>(StatusCodes.Status200OK);
        g.MapGet("/track", (DateTimeOffset? from, DateTimeOffset? to, Guid? deviceId, LocationQueryHandler h, CancellationToken ct) => h.TrackAsync(from, to, deviceId, ct))
            .WithSummary("Raw track over a time range (capped).").Produces<List<TrackPointDto>>(StatusCodes.Status200OK);
        g.MapGet("/track/thinned", (DateTimeOffset? from, DateTimeOffset? to, int? bucketSeconds, Guid? deviceId, LocationQueryHandler h, CancellationToken ct) => h.ThinnedAsync(from, to, bucketSeconds, deviceId, ct))
            .WithSummary("Server-downsampled track (one best-accuracy fix per time bucket).").Produces<List<TrackPointDto>>(StatusCodes.Status200OK);
        g.MapGet("/stats", (DateTimeOffset? from, DateTimeOffset? to, Guid? deviceId, LocationQueryHandler h, CancellationToken ct) => h.StatsAsync(from, to, deviceId, ct))
            .WithSummary("Distance + speed stats over a time range.").Produces<TrackStatsDto>(StatusCodes.Status200OK);
        g.MapGet("/bbox", (double minLat, double maxLat, double minLon, double maxLon, DateTimeOffset? from, DateTimeOffset? to, Guid? deviceId, LocationQueryHandler h, CancellationToken ct) => h.BboxAsync(minLat, maxLat, minLon, maxLon, from, to, deviceId, ct))
            .WithSummary("Fixes within a lat/lon rectangle over a time range.").Produces<List<TrackPointDto>>(StatusCodes.Status200OK);
        g.MapGet("/at", (DateTimeOffset ts, LocationQueryHandler h, CancellationToken ct) => h.AtAsync(ts, ct))
            .WithSummary("Coarse place label at a time (synergy-safe — never the raw fix).").Produces<PlaceLabelAtDto>(StatusCodes.Status200OK);
        g.MapGet("/visits", (DateTimeOffset? from, DateTimeOffset? to, LocationQueryHandler h, CancellationToken ct) => h.VisitsAsync(from, to, ct))
            .WithSummary("Materialized stay-points over a time range.").Produces<List<LocationVisitDto>>(StatusCodes.Status200OK);
        g.MapGet("/trips", (DateTimeOffset? from, DateTimeOffset? to, LocationQueryHandler h, CancellationToken ct) => h.TripsAsync(from, to, ct))
            .WithSummary("Materialized trips over a time range.").Produces<List<LocationTripDto>>(StatusCodes.Status200OK);
        g.MapGet("/summary", (DateOnly date, LocationQueryHandler h, CancellationToken ct) => h.SummaryAsync(date, ct))
            .WithSummary("Per-day location rollup.").Produces<DailyLocationSummaryDto>(StatusCodes.Status200OK);
        g.MapDelete("/", (DateTimeOffset? from, DateTimeOffset? to, Guid? deviceId, LocationQueryHandler h, CancellationToken ct) => h.PurgeAsync(from, to, deviceId, ct))
            .WithSummary("Purge raw fixes + derived docs in a time range (owner erase).").Produces(StatusCodes.Status204NoContent);

        g.MapPost("/tracking/{deviceId:guid}/pause", (Guid deviceId, PauseTrackingRequest? body, LocationQueryHandler h, CancellationToken ct) => h.PauseAsync(deviceId, body, ct))
            .WithSummary("Pause tracking for a device (ingest is discarded while paused).").Produces(StatusCodes.Status204NoContent);
        g.MapPost("/tracking/{deviceId:guid}/resume", (Guid deviceId, LocationQueryHandler h, CancellationToken ct) => h.ResumeAsync(deviceId, ct))
            .WithSummary("Resume tracking for a device.").Produces(StatusCodes.Status204NoContent);
        g.MapGet("/tracking/{deviceId:guid}/state", (Guid deviceId, LocationQueryHandler h, CancellationToken ct) => h.TrackingStateAsync(deviceId, ct))
            .WithSummary("Tracking state for a device.").Produces<TrackingStateDto>(StatusCodes.Status200OK);
        return app;
    }
}
