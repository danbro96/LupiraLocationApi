using LupiraLocationApi.Domain.Telemetry;

namespace LupiraLocationApi.Dtos.Location;

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

/// <summary>Distance + speed stats over a time range.</summary>
public sealed class TrackStatsDto
{
    public required double DistanceM { get; set; }
    public double? AvgSpeedMps { get; set; }
    public double? MaxSpeedMps { get; set; }
    public required long SampleCount { get; set; }
}

/// <summary>A coarse "where was I at T" answer — a place label + coarsened coordinate, never the raw fix. Synergy-safe.</summary>
public sealed class PlaceLabelAtDto
{
    public required DateTimeOffset Ts { get; set; }
    public string? Label { get; set; }
    public required double Lat { get; set; }
    public required double Lon { get; set; }
    public required string Source { get; set; }
}

/// <summary>A materialized stay-point.</summary>
public sealed class LocationVisitDto
{
    public required Guid Id { get; set; }
    public required DateTimeOffset ArriveTs { get; set; }
    public required DateTimeOffset DepartTs { get; set; }
    public required double Lat { get; set; }
    public required double Lon { get; set; }
    public required double RadiusM { get; set; }
    public required int SampleCount { get; set; }
    public string? PlaceLabel { get; set; }
}

/// <summary>A materialized trip between stays.</summary>
public sealed class LocationTripDto
{
    public required Guid Id { get; set; }
    public required DateTimeOffset StartTs { get; set; }
    public required DateTimeOffset EndTs { get; set; }
    public required double DistanceM { get; set; }
    public required double DurationS { get; set; }
    public MotionActivity? DominantActivity { get; set; }
    public required double AvgSpeedMps { get; set; }
    public required double MaxSpeedMps { get; set; }
}

/// <summary>A place visited on a day, with dwell minutes.</summary>
public sealed class VisitedPlaceDto
{
    public string? Label { get; set; }
    public required double Lat { get; set; }
    public required double Lon { get; set; }
    public required double Minutes { get; set; }
}

/// <summary>Per-day location rollup.</summary>
public sealed class DailyLocationSummaryDto
{
    public required DateOnly Date { get; set; }
    public required double DistanceM { get; set; }
    public required double TimeInMotionS { get; set; }
    public required double TimeStationaryS { get; set; }
    public required int VisitCount { get; set; }
    public required IReadOnlyList<VisitedPlaceDto> Places { get; set; }
}

/// <summary>Per-device tracking kill-switch state.</summary>
public sealed class TrackingStateDto
{
    public required Guid DeviceId { get; set; }
    public required bool Paused { get; set; }
    public DateTimeOffset? PausedAt { get; set; }
    public string? Reason { get; set; }
}

/// <summary>Pause tracking for a device (optional human-readable reason).</summary>
public sealed class PauseTrackingRequest
{
    public string? Reason { get; set; }
}
