namespace LupiraLocationApi.Core.Domain.Telemetry;

/// <summary>Which rollup algorithm the stored visits/trips were built with (one row); a lower version triggers a rebuild.</summary>
public sealed class LocationRollupCheckpoint
{
    public const string SingletonId = "location-rollup";

    public string Id { get; set; } = SingletonId;

    public int AlgorithmVersion { get; set; }
}
