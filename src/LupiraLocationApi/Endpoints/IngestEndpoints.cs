using LupiraLocationApi.Dtos.Location;
using LupiraLocationApi.Handlers;

namespace LupiraLocationApi.Endpoints;

/// <summary>The location telemetry ingest surface — DeviceKey-authed (the mobile uploader). NDJSON bodies; principal/device
/// are stamped from the key.</summary>
public static class IngestEndpoints
{
    public static IEndpointRouteBuilder MapIngest(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/ingest").RequireAuthorization("IngestPolicy").WithTags("Ingest");

        g.MapPost("/location", (LocationIngestHandler h, CancellationToken ct) => h.IngestAsync(ct))
            .WithSummary("Ingest a batch of GPS fixes (NDJSON, one fix per line).")
            .Accepts<string>("application/x-ndjson")
            .Produces<LocationIngestReceipt>(StatusCodes.Status202Accepted);
        g.MapGet("/location/cursor", (LocationIngestHandler h, CancellationToken ct) => h.CursorAsync(ct))
            .WithSummary("The device's resume cursor (last accepted seq + ts).")
            .Produces<LocationCursor>(StatusCodes.Status200OK);
        g.MapGet("/location/state", (LocationIngestHandler h, CancellationToken ct) => h.StateAsync(ct))
            .WithSummary("Whether tracking is paused for this device (the uploader should stop collecting if so).")
            .Produces<TrackingStateDto>(StatusCodes.Status200OK);
        return app;
    }
}
