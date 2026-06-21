using LupiraLocationApi.Application.Telemetry;
using LupiraLocationApi.Auth;
using LupiraLocationApi.Dtos.Location;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraLocationApi.Handlers;

/// <summary>The owner-facing location read + tracking-control surface (OIDC-authed). principal = the caller; every query
/// is scoped to the caller's own data.</summary>
public sealed class LocationQueryHandler(CurrentUser user, LocationQueryService q, TripVisitService trips, TrackingStateService tracking)
{
    public async Task<Results<Ok<List<CurrentFixDto>>, UnauthorizedHttpResult>> CurrentAsync(Guid? deviceId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return TypedResults.Ok((await q.CurrentAsync(u.Id, deviceId, ct)).Value!);
    }

    public async Task<Results<Ok<List<TrackPointDto>>, UnauthorizedHttpResult>> TrackAsync(DateTimeOffset? from, DateTimeOffset? to, Guid? deviceId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        var (f, t) = Range(from, to);
        return TypedResults.Ok((await q.TrackAsync(u.Id, deviceId, f, t, ct)).Value!);
    }

    public async Task<Results<Ok<List<TrackPointDto>>, UnauthorizedHttpResult>> ThinnedAsync(DateTimeOffset? from, DateTimeOffset? to, int? bucketSeconds, Guid? deviceId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        var (f, t) = Range(from, to);
        var bucket = TimeSpan.FromSeconds(bucketSeconds is > 0 ? bucketSeconds.Value : 30);
        return TypedResults.Ok((await q.ThinnedTrackAsync(u.Id, deviceId, f, t, bucket, ct)).Value!);
    }

    public async Task<Results<Ok<TrackStatsDto>, UnauthorizedHttpResult>> StatsAsync(DateTimeOffset? from, DateTimeOffset? to, Guid? deviceId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        var (f, t) = Range(from, to);
        return TypedResults.Ok((await q.StatsAsync(u.Id, deviceId, f, t, ct)).Value!);
    }

    public async Task<Results<Ok<List<TrackPointDto>>, UnauthorizedHttpResult>> BboxAsync(double minLat, double maxLat, double minLon, double maxLon, DateTimeOffset? from, DateTimeOffset? to, Guid? deviceId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        var (f, t) = Range(from, to);
        return TypedResults.Ok((await q.BoundingBoxAsync(u.Id, deviceId, f, t, (minLat, maxLat, minLon, maxLon), ct)).Value!);
    }

    public async Task<Results<Ok<PlaceLabelAtDto>, UnauthorizedHttpResult>> AtAsync(DateTimeOffset ts, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return TypedResults.Ok((await q.PlaceLabelAtAsync(u.Id, ts, ct)).Value!);
    }

    public async Task<Results<Ok<List<LocationVisitDto>>, UnauthorizedHttpResult>> VisitsAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        var (f, t) = Range(from, to);
        return TypedResults.Ok((await trips.VisitsAsync(u.Id, f, t, ct)).Value!);
    }

    public async Task<Results<Ok<List<LocationTripDto>>, UnauthorizedHttpResult>> TripsAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        var (f, t) = Range(from, to);
        return TypedResults.Ok((await trips.TripsAsync(u.Id, f, t, ct)).Value!);
    }

    public async Task<Results<Ok<DailyLocationSummaryDto>, UnauthorizedHttpResult>> SummaryAsync(DateOnly date, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return TypedResults.Ok((await trips.SummaryAsync(u.Id, date, ct)).Value!);
    }

    public async Task<Results<NoContent, UnauthorizedHttpResult>> PurgeAsync(DateTimeOffset? from, DateTimeOffset? to, Guid? deviceId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        var (f, t) = Range(from, to);
        await q.PurgeRangeAsync(u.Id, deviceId, f, t, ct);
        return TypedResults.NoContent();
    }

    public async Task<Results<NoContent, UnauthorizedHttpResult>> PauseAsync(Guid deviceId, PauseTrackingRequest? body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        await tracking.PauseAsync(u.Id, deviceId, body?.Reason, ct);
        return TypedResults.NoContent();
    }

    public async Task<Results<NoContent, UnauthorizedHttpResult>> ResumeAsync(Guid deviceId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        await tracking.ResumeAsync(u.Id, deviceId, ct);
        return TypedResults.NoContent();
    }

    public async Task<Results<Ok<TrackingStateDto>, UnauthorizedHttpResult>> TrackingStateAsync(Guid deviceId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return TypedResults.Ok((await tracking.StateAsync(u.Id, deviceId, ct)).Value!);
    }

    private static (DateTimeOffset From, DateTimeOffset To) Range(DateTimeOffset? from, DateTimeOffset? to)
    {
        var t = to ?? DateTimeOffset.UtcNow;
        var f = from ?? t.AddDays(-1);
        return (f, t);
    }
}
