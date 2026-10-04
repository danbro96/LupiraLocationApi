using Lupira.Auth.DeviceKeys;
using Marten;

namespace LupiraLocationApi.Auth;

public sealed class MartenDeviceKeyStore(IDocumentStore store) : IDeviceKeyStore
{
    public async Task<DeviceApiKey?> FindAsync(Guid keyId, CancellationToken ct)
    {
        await using var session = store.QuerySession();
        return await session.LoadAsync<DeviceApiKey>(keyId, ct);
    }
}
