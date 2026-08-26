namespace LupiraLocationApi.Core.Dtos.Location;

/// <summary>Per-device tracking kill-switch state.</summary>
public sealed class TrackingStateDto
{
    public required Guid DeviceId { get; set; }
    public required bool Paused { get; set; }
    public DateTimeOffset? PausedAt { get; set; }
    public string? Reason { get; set; }
}
