using System.Text.Json.Serialization;

namespace LupiraLocationApi.Domain.Telemetry;

/// <summary>Source that produced a GPS fix. Stored as the <c>provider</c> smallint on <c>telemetry.location_point</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LocationProvider>))]
public enum LocationProvider : short { Unknown = 0, Gps = 1, Network = 2, Fused = 3, Passive = 4 }

/// <summary>OS-reported motion classification. Stored as the <c>activity</c> smallint; the primary trip/visit segmentation signal.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MotionActivity>))]
public enum MotionActivity : short { Unknown = 0, Still = 1, Walk = 2, Run = 3, Cycle = 4, Vehicle = 5 }
