namespace LupiraLocationApi.Core.Application.Telemetry;

/// <summary>Calendar days in the rollup's time zone, so a day boundary falls at local midnight.</summary>
internal static class LocalDay
{
    public static DateTimeOffset StartUtc(DateOnly day, TimeZoneInfo tz) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), tz), TimeSpan.Zero);

    public static DateOnly Of(DateTimeOffset ts, TimeZoneInfo tz) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ts, tz).DateTime);
}
