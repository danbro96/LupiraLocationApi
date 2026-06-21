namespace LupiraLocationApi.Dtos.Devices;

/// <summary>Result of registering a device. <see cref="ApiKey"/> is the one-time plaintext ingest credential
/// (<c>{keyId:N}.{secret}</c>) — it is shown only here and never retrievable again; only its hash is stored.</summary>
public sealed class RegisterDeviceResponse
{
    public required DeviceDto Device { get; set; }
    public required Guid KeyId { get; set; }
    public required string ApiKey { get; set; }
}
