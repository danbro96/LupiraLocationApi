using LupiraLocationApi.Core.Application.Telemetry;
using Xunit;

namespace LupiraLocationApi.UnitTests;

public class LocalDayTests
{
    private static readonly TimeZoneInfo Stockholm = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    [Theory]
    [InlineData("2026-11-11", "2026-11-10T23:00:00Z")]
    [InlineData("2026-07-01", "2026-06-30T22:00:00Z")]
    [InlineData("2026-03-29", "2026-03-28T23:00:00Z")]
    [InlineData("2026-03-30", "2026-03-29T22:00:00Z")]
    [InlineData("2026-10-25", "2026-10-24T22:00:00Z")]
    [InlineData("2026-10-26", "2026-10-25T23:00:00Z")]
    public void Start_is_local_midnight_in_utc(string day, string expectedUtc) =>
        Assert.Equal(DateTimeOffset.Parse(expectedUtc), LocalDay.StartUtc(DateOnly.Parse(day), Stockholm));

    [Theory]
    [InlineData("2026-11-10T22:59:00Z", "2026-11-10")]
    [InlineData("2026-11-10T23:00:00Z", "2026-11-11")]
    [InlineData("2026-06-30T22:30:00Z", "2026-07-01")]
    public void Of_maps_an_instant_to_its_local_date(string ts, string expectedDay) =>
        Assert.Equal(DateOnly.Parse(expectedDay), LocalDay.Of(DateTimeOffset.Parse(ts), Stockholm));
}
