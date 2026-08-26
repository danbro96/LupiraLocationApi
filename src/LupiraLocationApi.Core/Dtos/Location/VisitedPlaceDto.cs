namespace LupiraLocationApi.Core.Dtos.Location;

/// <summary>A place visited on a day, with dwell minutes.</summary>
public sealed class VisitedPlaceDto
{
    public string? Label { get; set; }

    public required double Lat { get; set; }

    public required double Lon { get; set; }

    public required double Minutes { get; set; }
}
