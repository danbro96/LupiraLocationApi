using System.Text.Json.Serialization;

namespace LupiraLocationApi.Domain;

/// <summary>Kind of registered device that feeds location telemetry.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DeviceKind>))]
public enum DeviceKind { Phone, Watch, Tracker, Other }
