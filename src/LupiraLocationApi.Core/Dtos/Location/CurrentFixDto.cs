using LupiraLocationApi.Core.Domain.Telemetry;

namespace LupiraLocationApi.Core.Dtos.Location;

/// <summary>Latest-known location for a device.</summary>
public sealed class CurrentFixDto
{
    public required Guid DeviceId { get; set; }

    public required DateTimeOffset Ts { get; set; }

    public required double Lat { get; set; }

    public required double Lon { get; set; }

    public double? AccuracyM { get; set; }

    public double? SpeedMps { get; set; }

    public MotionActivity? Activity { get; set; }

    public int? BatteryPct { get; set; }
}
