namespace LupiraLocationApi.Domain.Telemetry;

/// <summary>A stay-point: a cluster of fixes where the principal dwelled. Materialized by the rollup job (so it survives
/// raw-GPS retention drop) into the <c>health</c> schema. <see cref="PlaceLabel"/> is the frozen reverse-geocoded name.</summary>
public sealed class LocationVisit
{
    public Guid Id { get; set; }
    public Guid PrincipalId { get; set; }
    public Guid DeviceId { get; set; }
    public DateTimeOffset ArriveTs { get; set; }
    public DateTimeOffset DepartTs { get; set; }
    public double CentroidLat { get; set; }
    public double CentroidLon { get; set; }
    public double RadiusM { get; set; }
    public int SampleCount { get; set; }
    public string? PlaceLabel { get; set; }
}
