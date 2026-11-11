namespace LupiraLocationApi.Core.Application.Telemetry;

/// <summary>Rollup settings (<c>Location:Rollup</c>). Day boundaries for visits, trips and daily summaries.</summary>
public sealed class LocationRollupOptions
{
    /// <summary>IANA zone whose local midnight splits rollup days.</summary>
    public string TimeZoneId { get; set; } = "Europe/Stockholm";
}
