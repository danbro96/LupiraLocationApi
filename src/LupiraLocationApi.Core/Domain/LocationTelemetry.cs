using System.Diagnostics;

namespace LupiraLocationApi.Domain;

/// <summary>Domain-specific tracing source, registered with OpenTelemetry in Program.cs. (Named to avoid colliding with
/// the <c>LupiraLocationApi.Domain.Telemetry</c> namespace that holds the time-series domain types.)</summary>
public static class LocationTelemetry
{
    public const string ActivitySourceName = "LupiraLocationApi.Location";
    public static readonly ActivitySource Source = new(ActivitySourceName);
}
