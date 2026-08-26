namespace LupiraLocationApi.Core.Dtos.Location;

/// <summary>Distance + speed stats over a time range.</summary>
public sealed class TrackStatsDto
{
    public required double DistanceM { get; set; }
    public double? AvgSpeedMps { get; set; }
    public double? MaxSpeedMps { get; set; }
    public required long SampleCount { get; set; }
}
