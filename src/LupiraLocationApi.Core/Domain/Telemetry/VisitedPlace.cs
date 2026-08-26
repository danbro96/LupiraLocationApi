namespace LupiraLocationApi.Core.Domain.Telemetry;

/// <summary>One place the principal visited on a day, with dwell minutes.</summary>
public sealed record VisitedPlace(string? Label, double Lat, double Lon, double Minutes);
