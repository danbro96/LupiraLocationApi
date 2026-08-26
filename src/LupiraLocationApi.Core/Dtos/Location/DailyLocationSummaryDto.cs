namespace LupiraLocationApi.Core.Dtos.Location;

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
