using LupiraLocationApi.Dtos.Devices;
using LupiraLocationApi.Handlers;

namespace LupiraLocationApi.Endpoints;

public static class DevicesEndpoints
{
    public static IEndpointRouteBuilder MapDevices(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/devices").RequireAuthorization("ApiPolicy").WithTags("Devices");

        g.MapGet("/", (DevicesHandler h, CancellationToken ct) => h.ListAsync(ct))
            .WithSummary("List the caller's registered location-tracking devices.")
            .Produces<List<DeviceDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        g.MapPost("/", (RegisterDeviceRequest body, DevicesHandler h, CancellationToken ct) => h.RegisterAsync(body, ct))
            .WithSummary("Register a device; returns the one-time ingest API key.")
            .Produces<RegisterDeviceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        g.MapPut("/{id:guid}", (Guid id, RenameDeviceRequest body, DevicesHandler h, CancellationToken ct) => h.RenameAsync(id, body, ct))
            .WithSummary("Rename a device.")
            .Produces<DeviceDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status401Unauthorized);

        g.MapDelete("/{id:guid}", (Guid id, DevicesHandler h, CancellationToken ct) => h.RetireAsync(id, ct))
            .WithSummary("Retire a device (revokes its ingest keys).")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }
}
