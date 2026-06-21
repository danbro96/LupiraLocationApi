namespace LupiraLocationApi.Domain.Telemetry;

/// <summary>Watermark for the location rollup so the maintenance service is resumable across restarts (one row).</summary>
public sealed class LocationRollupCheckpoint
{
    public const string SingletonId = "location-rollup";
    public string Id { get; set; } = SingletonId;
    public DateTimeOffset RolledUpThrough { get; set; }
}
