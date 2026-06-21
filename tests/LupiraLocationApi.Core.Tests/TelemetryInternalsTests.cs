using LupiraLocationApi.Application.Telemetry;
using LupiraLocationApi.Telemetry;
using Xunit;

namespace LupiraLocationApi.Core.Tests;

public class GeoTests
{
    [Fact]
    public void Haversine_one_degree_longitude_at_equator_is_about_111km()
    {
        var m = Geo.HaversineMeters(0, 0, 0, 1);
        Assert.InRange(m, 111_000, 111_400);
    }

    [Fact]
    public void Haversine_same_point_is_zero() => Assert.Equal(0, Geo.HaversineMeters(59.3, 18.0, 59.3, 18.0), 3);
}

public class PartitionBoundsTests
{
    [Fact]
    public void Weekly_bounds_align_to_a_monday_and_span_seven_days()
    {
        // 2026-06-18 is a Thursday.
        var (name, lower, upper) = PartitionManager.Bounds("location_point", PartitionInterval.Weekly, new DateTimeOffset(2026, 6, 18, 13, 0, 0, TimeSpan.Zero));
        Assert.Equal(DayOfWeek.Monday, lower.UtcDateTime.DayOfWeek);
        Assert.Equal(new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero), lower);
        Assert.Equal(lower.AddDays(7), upper);
        Assert.Equal("location_point_w20260615", name);
    }

    [Fact]
    public void Monthly_bounds_span_the_calendar_month()
    {
        var (name, lower, upper) = PartitionManager.Bounds("location_point", PartitionInterval.Monthly, new DateTimeOffset(2026, 6, 18, 13, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), lower);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), upper);
        Assert.Equal("location_point_m202606", name);
    }
}
