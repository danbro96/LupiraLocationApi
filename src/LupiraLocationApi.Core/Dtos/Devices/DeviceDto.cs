using LupiraLocationApi.Core.Domain;

namespace LupiraLocationApi.Core.Dtos.Devices;

/// <summary>A registered device.</summary>
public sealed class DeviceDto
{
    public required Guid Id { get; set; }
    public required DeviceKind Kind { get; set; }
    public required string Label { get; set; }
    public string? ExternalId { get; set; }
    public required DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
}
