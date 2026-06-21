using LupiraLocationApi.Domain;

namespace LupiraLocationApi.Dtos.Devices;

/// <summary>Register a device that will feed location telemetry.</summary>
public sealed class RegisterDeviceRequest
{
    public required DeviceKind Kind { get; set; }
    public required string Label { get; set; }
    public string? ExternalId { get; set; }
}
