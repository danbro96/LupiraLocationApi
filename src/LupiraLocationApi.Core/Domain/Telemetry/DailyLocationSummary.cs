namespace LupiraLocationApi.Domain.Telemetry;

/// <summary>One place the principal visited on a day, with dwell minutes.</summary>
public sealed record VisitedPlace(string? Label, double Lat, double Lon, double Minutes);

/// <summary>Per-day rollup of a principal's location. Deterministic id from (principal, date, device) so re-running a
/// day's rollup overwrites it. Marten document in the <c>location</c> schema; survives raw-fix retention drop.</summary>
public sealed class DailyLocationSummary
{
    public Guid Id { get; set; }
    public Guid PrincipalId { get; set; }
    public Guid DeviceId { get; set; }
    public DateOnly Date { get; set; }
    public double DistanceM { get; set; }
    public double TimeInMotionS { get; set; }
    public double TimeStationaryS { get; set; }
    public int VisitCount { get; set; }
    public List<VisitedPlace> PlacesVisited { get; set; } = new();

    public static Guid MakeId(Guid principalId, Guid deviceId, DateOnly date) =>
        DeterministicGuid.From($"{principalId:N}:{deviceId:N}:{date:yyyyMMdd}:locsummary");
}
