using LupiraLocationApi.Application;
using LupiraLocationApi.Application.Telemetry;
using LupiraLocationApi.Dtos.Location;
using LupiraLocationApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraLocationApi.Handlers;

/// <summary>Service-to-service reads. The caller authenticates as a service (internal:read scope) and names
/// the principal explicitly by OIDC <c>sub</c> — an unknown sub is 404, never a provisioned placeholder.</summary>
public sealed class InternalLocationHandler(PrincipalDirectory principals, LocationQueryService query)
{
    public async Task<Results<Ok<PlaceLabelAtDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> PlaceAtAsync(
        string sub, DateTimeOffset ts, CancellationToken ct)
    {
        var principal = await principals.FindBySubAsync(sub, ct);
        if (principal is null) return TypedResults.NotFound();
        return OpResultMap.OkNotFoundProblem(await query.PlaceLabelAtAsync(principal.Id, ts, ct));
    }
}
