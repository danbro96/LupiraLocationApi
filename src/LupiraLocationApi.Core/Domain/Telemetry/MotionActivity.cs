using System.Text.Json.Serialization;

namespace LupiraLocationApi.Core.Domain.Telemetry;

/// <summary>OS-reported motion classification. Stored as the <c>activity</c> smallint; the primary trip/visit segmentation signal.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MotionActivity>))]
public enum MotionActivity : short
{
    Unknown = 0,
    Still = 1,
    Walk = 2,
    Run = 3,
    Cycle = 4,
    Vehicle = 5,
}
