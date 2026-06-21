using LupiraLocationApi.Auth;
using LupiraLocationApi.Dtos.Me;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraLocationApi.Handlers;

public sealed class MeHandler(CurrentUser user)
{
    public async Task<Results<Ok<MeDto>, UnauthorizedHttpResult>> GetAsync(CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return TypedResults.Ok(new MeDto { Id = u.Id, Email = u.Email, DisplayName = u.DisplayName });
    }
}
