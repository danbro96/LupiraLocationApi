namespace LupiraLocationApi.Core.Dtos.Location;

/// <summary>A coarse "where was I at T" answer — a place label + coarsened coordinate, never the raw fix. Synergy-safe.</summary>
public sealed class PlaceLabelAtDto
{
    public required DateTimeOffset Ts { get; set; }

    public string? Label { get; set; }

    public required double Lat { get; set; }

    public required double Lon { get; set; }

    public required string Source { get; set; }
}
