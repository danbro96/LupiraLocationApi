namespace LupiraLocationApi.Core.Dtos.Location;

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
