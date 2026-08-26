using LupiraLocationApi.Core.Domain.Telemetry;

namespace LupiraLocationApi.Core.Dtos.Location;

/// <summary>A point on a (raw or thinned) track.</summary>
public sealed class TrackPointDto
{
    public required Guid DeviceId { get; set; }

    public required DateTimeOffset Ts { get; set; }

    public required double Lat { get; set; }

    public required double Lon { get; set; }

    public double? AccuracyM { get; set; }

    public double? AltitudeM { get; set; }

    public double? HeadingDeg { get; set; }

    public double? SpeedMps { get; set; }

    public MotionActivity? Activity { get; set; }

    public LocationProvider? Provider { get; set; }
}
