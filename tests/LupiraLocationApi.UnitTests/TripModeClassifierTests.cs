using LupiraLocationApi.Core.Application.Telemetry;
using LupiraLocationApi.Core.Domain.Telemetry;
using Xunit;

namespace LupiraLocationApi.UnitTests;

public class TripModeClassifierTests
{
    private const double Lat0 = 59.3827;
    private const double Lon0 = 18.0253;
    private static readonly DateTimeOffset T0 = new(2026, 11, 5, 7, 56, 0, TimeSpan.Zero);

    [Fact]
    public void Sparse_fixes_around_an_underground_ride_are_a_vehicle_trip()
    {
        RollupPoint[] seg =
        [
            At(0, 0, 0),
            At(300, 700, 1.4),
            At(360, 750, 0),
            At(1200, 5700, 0.5),
            At(1500, 6100, 1.3),
        ];

        Assert.Equal(MotionActivity.Vehicle, TripModeClassifier.Classify(seg));
    }

    [Fact]
    public void Slow_steady_progress_is_a_walk()
    {
        var seg = Enumerable.Range(0, 13).Select(k => At(k * 240, k * 150, 1.2)).ToArray();

        Assert.Equal(MotionActivity.Walk, TripModeClassifier.Classify(seg));
    }

    [Fact]
    public void Dense_fixes_at_cycling_speed_are_a_cycle_trip()
    {
        var seg = Enumerable.Range(0, 20).Select(k => At(k * 30, k * 165, k % 2 == 0 ? 5.0 : 6.0)).ToArray();

        Assert.Equal(MotionActivity.Cycle, TripModeClassifier.Classify(seg));
    }

    [Fact]
    public void Road_speed_is_a_vehicle_trip()
    {
        var seg = Enumerable.Range(0, 10).Select(k => At(k * 30, k * 450, 15)).ToArray();

        Assert.Equal(MotionActivity.Vehicle, TripModeClassifier.Classify(seg));
    }

    [Fact]
    public void A_cycle_paced_trip_that_hits_road_speed_is_a_vehicle_trip()
    {
        var seg = Enumerable.Range(0, 20).Select(k => At(k * 30, k * 165, k == 10 ? 14 : 5.5)).ToArray();

        Assert.Equal(MotionActivity.Vehicle, TripModeClassifier.Classify(seg));
    }

    [Fact]
    public void Jitter_alone_is_unknown()
    {
        var seg = Enumerable.Range(0, 6).Select(k => At(k * 60, k % 2 * 20, null)).ToArray();

        Assert.Equal(MotionActivity.Unknown, TripModeClassifier.Classify(seg));
    }

    [Fact]
    public void An_unreliable_gnss_speed_is_ignored()
    {
        var seg = Enumerable.Range(0, 10)
            .Select(k => At(k * 60, k * 60, 9) with { SpeedAccMps = 5 })
            .ToArray();

        Assert.Equal(MotionActivity.Walk, TripModeClassifier.Classify(seg));
    }

    private static RollupPoint At(int second, double north, double? speedMps) =>
        new(T0.AddSeconds(second), Lat0 + (north / 111_320.0), Lon0, speedMps, null);
}
