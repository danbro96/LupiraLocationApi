using System.Security.Claims;

namespace LupiraLocationApi.Auth;

/// <summary>Claim types carried by a <see cref="DeviceKeyAuthHandler"/>-authenticated request.</summary>
public static class DeviceKeyClaims
{
    public const string PrincipalId = "principal_id";
    public const string DeviceId = "device_id";

    /// <summary>The resolved (principal, device) the device key acts as. Throws if the request was not device-key authed.</summary>
    public static (Guid PrincipalId, Guid DeviceId) Get(ClaimsPrincipal p) =>
        (Guid.Parse(p.FindFirstValue(PrincipalId) ?? throw new InvalidOperationException("Not a device-key principal.")),
         Guid.Parse(p.FindFirstValue(DeviceId) ?? throw new InvalidOperationException("Not a device-key principal.")));
}
