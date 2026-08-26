using LupiraLocationApi.Core.Domain.Telemetry;

namespace LupiraLocationApi.Core.Dtos.Location;

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

    /// <summary>Endpoint visits (when the trip started/ended at a detected stay-point) — lets a client join trips to
    /// visit markers without time-window heuristics. The trip carries no geometry; fetch the polyline via
    /// GET /location/track/thinned for [StartTs, EndTs].</summary>
    public Guid? FromVisitId { get; set; }

    public Guid? ToVisitId { get; set; }
}
