namespace LupiraLocationApi.Core.Dtos.Location;

/// <summary>Pause tracking for a device (optional human-readable reason).</summary>
public sealed class PauseTrackingRequest
{
    public string? Reason { get; set; }
}
