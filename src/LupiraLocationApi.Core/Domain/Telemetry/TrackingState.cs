namespace LupiraLocationApi.Domain.Telemetry;

/// <summary>Per-device tracking kill-switch. While paused, location ingest is accepted (202) but discarded, and the app
/// learns to stop collecting via the ingest state endpoint. Composite id (record-agnostic: principal + device).</summary>
public sealed class TrackingState
{
    public string Id { get; set; } = "";
    public Guid PrincipalId { get; set; }
    public Guid DeviceId { get; set; }
    public bool Paused { get; set; }
    public DateTimeOffset? PausedAt { get; set; }
    public string? Reason { get; set; }

    public static string MakeId(Guid principalId, Guid deviceId) => $"{principalId:N}:{deviceId:N}";
}
