using LupiraLocationApi.Domain;
using Marten;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace LupiraLocationApi.Auth;

/// <summary>Telemetry ingest auth. Validates <c>Authorization: DeviceKey {keyId}.{secret}</c> against the stored
/// <see cref="DeviceApiKey"/> hash (constant-time), checks revocation + the <c>ingest</c> scope, and emits a principal
/// carrying the resolved local principal id + device id as claims. The ingest endpoints stamp those onto every row, so
/// telemetry never trusts ids from the payload. Least privilege: a key acts as one principal, for one device, ingest-only.</summary>
public sealed class DeviceKeyAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "DeviceKey";
    private const string Prefix = "DeviceKey ";
    private readonly IDocumentStore _store;

    public DeviceKeyAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IDocumentStore store)
        : base(options, logger, encoder)
    {
        _store = store;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var header)) return AuthenticateResult.NoResult();
        var value = header.ToString();
        if (!value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();

        if (!DeviceKeyHashing.TryParse(value[Prefix.Length..].Trim(), out var keyId, out var secret))
            return AuthenticateResult.Fail("Malformed device key.");

        await using var session = _store.QuerySession();
        var key = await session.LoadAsync<DeviceApiKey>(keyId);
        if (key is null || key.RevokedAt is not null || !key.Scopes.Contains("ingest") || !DeviceKeyHashing.Verify(secret, key.KeyHash))
            return AuthenticateResult.Fail("Invalid device key.");

        var claims = new[]
        {
            new Claim(DeviceKeyClaims.PrincipalId, key.PrincipalId.ToString()),
            new Claim(DeviceKeyClaims.DeviceId, key.DeviceId.ToString()),
            new Claim("scope", "ingest"),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}

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
