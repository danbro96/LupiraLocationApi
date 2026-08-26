using System.Text.Json.Serialization;

namespace LupiraLocationApi.Core.Domain.Telemetry;

/// <summary>Source that produced a GPS fix. Stored as the <c>provider</c> smallint on <c>telemetry.location_point</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LocationProvider>))]
public enum LocationProvider : short
{
    Unknown = 0,
    Gps = 1,
    Network = 2,
    Fused = 3,
    Passive = 4,
}
