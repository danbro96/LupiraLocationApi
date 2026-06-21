using LupiraLocationApi.Application;
using LupiraLocationApi.Auth;
using LupiraLocationApi.Dtos.Devices;
using LupiraLocationApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraLocationApi.Handlers;

public sealed class DevicesHandler(CurrentUser user, DeviceService devices)
{
    public async Task<Results<Ok<List<DeviceDto>>, ProblemHttpResult, UnauthorizedHttpResult>> ListAsync(CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await devices.ListAsync(u.Id, ct));
    }

    public async Task<Results<Ok<RegisterDeviceResponse>, ProblemHttpResult, UnauthorizedHttpResult>> RegisterAsync(RegisterDeviceRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await devices.RegisterAsync(u.Id, body, ct));
    }

    public async Task<Results<Ok<DeviceDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> RenameAsync(Guid id, RenameDeviceRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await devices.RenameAsync(u.Id, id, body, ct));
    }

    public async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> RetireAsync(Guid id, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.NoContentNotFoundProblem(await devices.RetireAsync(u.Id, id, ct));
    }
}
