namespace LupiraLocationApi.Domain;

/// <summary>A registered device that feeds a principal's location telemetry (plain document — pure registration
/// metadata). Telemetry rows carry the <see cref="Id"/> by value; per-device ingest credentials live in
/// <see cref="DeviceApiKey"/>. Phase 1 has no sharing: a device is owned directly by the principal that registered it.</summary>
public sealed class Device
{
    public Guid Id { get; set; }
    public Guid PrincipalId { get; set; }
    public DeviceKind Kind { get; set; }
    public string Label { get; set; } = "";
    public string? ExternalId { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
}
