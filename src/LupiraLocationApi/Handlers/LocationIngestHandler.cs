using LupiraLocationApi.Application.Telemetry;
using LupiraLocationApi.Auth;
using LupiraLocationApi.Dtos.Location;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraLocationApi.Handlers;

/// <summary>The telemetry ingest surface (DeviceKey-authed). principal/device come from the key claims, never the body.</summary>
public sealed class LocationIngestHandler(IHttpContextAccessor http, LocationIngestService ingest, TrackingStateService tracking)
{
    public async Task<Results<Accepted<LocationIngestReceipt>, UnauthorizedHttpResult>> IngestAsync(CancellationToken ct)
    {
        var ctx = http.HttpContext!;
        var (pid, did) = DeviceKeyClaims.Get(ctx.User);
        var r = await ingest.IngestNdjsonAsync(pid, did, ctx.Request.Body, ct);
        return TypedResults.Accepted((string?)null, r.Value!);
    }

    public async Task<Results<Ok<LocationCursor>, UnauthorizedHttpResult>> CursorAsync(CancellationToken ct)
    {
        var (pid, did) = DeviceKeyClaims.Get(http.HttpContext!.User);
        var r = await ingest.GetCursorAsync(pid, did, ct);
        return TypedResults.Ok(r.Value!);
    }

    public async Task<Results<Ok<TrackingStateDto>, UnauthorizedHttpResult>> StateAsync(CancellationToken ct)
    {
        var (pid, did) = DeviceKeyClaims.Get(http.HttpContext!.User);
        var r = await tracking.StateAsync(pid, did, ct);
        return TypedResults.Ok(r.Value!);
    }
}
