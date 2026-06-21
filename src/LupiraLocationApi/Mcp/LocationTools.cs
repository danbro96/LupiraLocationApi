using LupiraLocationApi.Application;
using LupiraLocationApi.Application.Telemetry;
using LupiraLocationApi.Auth;
using LupiraLocationApi.Dtos.Devices;
using LupiraLocationApi.Dtos.Location;
using LupiraLocationApi.Dtos.Me;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace LupiraLocationApi.Mcp;

/// <summary>
/// The agent's MCP surface. Read-only and derived/coarse by design: it exposes the caller's
/// materialized intelligence (visits, trips, daily rollups, coarse place-at, movement stats) but
/// never the raw lat·lon breadcrumb track. Tools call the SAME Core services as the REST handlers,
/// so there is no second source of truth. Identity comes from the bearer principal on the MCP
/// transport (<see cref="CurrentUser"/>); every call is scoped to that principal's own data via the
/// services' <c>principalId</c> filter.
/// </summary>
[McpServerToolType]
public sealed class LocationTools(CurrentUser user, DeviceService devices, LocationQueryService query, TripVisitService trips)
{
    [McpServerTool(Name = "me")]
    [Description("Get the caller's resolved identity (local id, email, display name).")]
    public async Task<MeDto> Me(CancellationToken ct = default)
    {
        var u = await user.GetAsync(ct);
        return new MeDto { Id = u.Id, Email = u.Email, DisplayName = u.DisplayName };
    }

    [McpServerTool(Name = "list_devices")]
    [Description("List the caller's registered devices (id, label, kind). Use a device id to scope movement_stats.")]
    public async Task<List<DeviceDto>> ListDevices(CancellationToken ct = default)
    {
        var pid = (await user.GetAsync(ct)).Id;
        return Require(await devices.ListAsync(pid, ct));
    }

    [McpServerTool(Name = "list_visits")]
    [Description("List the caller's materialized location visits (stay-points) in a time window, with coarse centroid and place label.")]
    public async Task<List<LocationVisitDto>> ListVisits(
        [Description("Window start, ISO-8601 (default: 24h before 'to').")] DateTimeOffset? from = null,
        [Description("Window end, ISO-8601 (default: now).")] DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        var pid = (await user.GetAsync(ct)).Id;
        var (f, t) = Range(from, to);
        return Require(await trips.VisitsAsync(pid, f, t, ct));
    }

    [McpServerTool(Name = "list_trips")]
    [Description("List the caller's materialized trips (movement between stays) in a time window: distance, duration, dominant activity, speeds.")]
    public async Task<List<LocationTripDto>> ListTrips(
        [Description("Window start, ISO-8601 (default: 24h before 'to').")] DateTimeOffset? from = null,
        [Description("Window end, ISO-8601 (default: now).")] DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        var pid = (await user.GetAsync(ct)).Id;
        var (f, t) = Range(from, to);
        return Require(await trips.TripsAsync(pid, f, t, ct));
    }

    [McpServerTool(Name = "daily_summary")]
    [Description("Get the caller's per-day location rollup: total distance, time in motion vs stationary, visit count, and places visited.")]
    public async Task<DailyLocationSummaryDto> DailySummary(
        [Description("The day to summarize (date only, e.g. 2026-06-21).")] DateOnly date,
        CancellationToken ct = default)
    {
        var pid = (await user.GetAsync(ct)).Id;
        return Require(await trips.SummaryAsync(pid, date, ct));
    }

    [McpServerTool(Name = "place_at")]
    [Description("Coarse 'where was I at this time' answer: a place label and coarsened coordinate (visit centroid or ~100 m place). Never the raw fix.")]
    public async Task<PlaceLabelAtDto> PlaceAt(
        [Description("The timestamp to resolve, ISO-8601.")] DateTimeOffset ts,
        CancellationToken ct = default)
    {
        var pid = (await user.GetAsync(ct)).Id;
        return Require(await query.PlaceLabelAtAsync(pid, ts, ct));
    }

    [McpServerTool(Name = "movement_stats")]
    [Description("Distance and speed statistics over a time window (no coordinates): total distance, avg/max speed, sample count.")]
    public async Task<TrackStatsDto> MovementStats(
        [Description("Restrict to a single device id (optional; aggregates all devices when omitted).")] Guid? deviceId = null,
        [Description("Window start, ISO-8601 (default: 24h before 'to').")] DateTimeOffset? from = null,
        [Description("Window end, ISO-8601 (default: now).")] DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        var pid = (await user.GetAsync(ct)).Id;
        var (f, t) = Range(from, to);
        return Require(await query.StatsAsync(pid, deviceId, f, t, ct));
    }

    /// <summary>Default window mirrors <c>LocationQueryHandler.Range()</c>: to = now, from = 24h before.</summary>
    private static (DateTimeOffset From, DateTimeOffset To) Range(DateTimeOffset? from, DateTimeOffset? to)
    {
        var t = to ?? DateTimeOffset.UtcNow;
        return (from ?? t.AddDays(-1), t);
    }

    /// <summary>Unwrap a successful result or surface the failure to the agent as a tool error.</summary>
    private static T Require<T>(OpResult<T> r) => r.Status switch
    {
        OpStatus.Ok => r.Value!,
        OpStatus.NotFound => throw new McpException("Not found, or you don't have access to it."),
        OpStatus.Invalid => throw new McpException(r.Error ?? "The request was invalid."),
        OpStatus.Forbidden => throw new McpException(r.Error ?? "You don't have permission to do that."),
        _ => throw new McpException("Unexpected error."),
    };
}
