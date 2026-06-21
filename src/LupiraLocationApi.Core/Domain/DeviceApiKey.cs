namespace LupiraLocationApi.Domain;

/// <summary>A long-lived ingest credential bound to a single <c>(PrincipalId, DeviceId)</c> and the <c>ingest</c> scope.
/// The plaintext secret is shown once at registration; only its hash is stored. Validated by the host's
/// <c>DeviceKeyAuthHandler</c>, which stamps the resolved principal/device onto the request — telemetry never trusts
/// ids from the payload. <see cref="Id"/> is the public key id (the part before the dot in <c>keyId.secret</c>).</summary>
public sealed class DeviceApiKey
{
    public Guid Id { get; set; }
    public Guid PrincipalId { get; set; }
    public Guid DeviceId { get; set; }
    public string KeyHash { get; set; } = "";
    public string[] Scopes { get; set; } = ["ingest"];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}
