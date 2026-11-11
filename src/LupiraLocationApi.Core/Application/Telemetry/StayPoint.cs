namespace LupiraLocationApi.Core.Application.Telemetry;

/// <summary>A detected stay over the fixes <c>[FirstIndex, LastIndex]</c> of the rollup window.</summary>
internal readonly record struct StayPoint(
    int FirstIndex,
    int LastIndex,
    DateTimeOffset Arrive,
    DateTimeOffset Depart,
    double Lat,
    double Lon,
    double RadiusM,
    int Count);
