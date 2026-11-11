namespace LupiraLocationApi.Core.Domain.Telemetry;

/// <summary>Rollup progress (one row): the algorithm the stored visits/trips were built with (a lower version triggers a
/// rebuild) and the <c>received_at</c> watermark below which late fixes have been rolled up.</summary>
public sealed class LocationRollupCheckpoint
{
    public const string SingletonId = "location-rollup";

    public string Id { get; set; } = SingletonId;

    public int AlgorithmVersion { get; set; }

    public DateTimeOffset ReceivedThrough { get; set; }
}
