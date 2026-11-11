using LupiraLocationApi.Core.Application.Telemetry;
using Xunit;

namespace LupiraLocationApi.UnitTests;

public class StayPointDetectorTests
{
    private const double Lat0 = 59.3827;
    private const double Lon0 = 18.0253;
    private static readonly DateTimeOffset T0 = new(2026, 11, 5, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_single_stray_fix_does_not_split_a_stay()
    {
        var pts = new List<RollupPoint>();
        for (var m = 0; m < 10; m++) pts.Add(At(m, north: m % 2 * 10));
        pts.Add(At(10, north: 150));
        for (var m = 11; m < 21; m++) pts.Add(At(m, north: m % 2 * 10));

        var stay = Assert.Single(StayPointDetector.Detect(pts));
        Assert.Equal(T0, stay.Arrive);
        Assert.Equal(T0.AddMinutes(20), stay.Depart);
        Assert.Equal(20, stay.Count);
    }

    [Fact]
    public void Jittery_fragments_within_110m_become_one_stay()
    {
        var pts = Fragments();

        var stay = Assert.Single(StayPointDetector.Detect(pts));
        Assert.Equal(pts[0].Ts, stay.Arrive);
        Assert.Equal(pts[^1].Ts, stay.Depart);
    }

    [Fact]
    public void Merged_radius_bounds_every_member_fix()
    {
        var pts = Fragments();

        var stay = Assert.Single(StayPointDetector.Detect(pts));
        Assert.All(pts, p => Assert.True(Geo.HaversineMeters(stay.Lat, stay.Lon, p.Lat, p.Lon) <= stay.RadiusM + 0.01));
    }

    [Fact]
    public void Stays_300m_apart_stay_separate()
    {
        var pts = new List<RollupPoint>();
        for (var m = 0; m < 10; m++) pts.Add(At(m, north: 0));
        for (var m = 15; m < 25; m++) pts.Add(At(m, north: 300));

        Assert.Equal(2, StayPointDetector.Detect(pts).Count);
    }

    [Fact]
    public void A_far_excursion_between_two_stays_at_one_place_is_kept()
    {
        var pts = new List<RollupPoint>();
        for (var m = 0; m < 10; m++) pts.Add(At(m, north: 0));
        for (var m = 1; m <= 10; m++) pts.Add(At(9 + m, north: m * 60));
        for (var m = 1; m <= 9; m++) pts.Add(At(19 + m, north: 600 - (m * 60)));
        for (var m = 29; m < 39; m++) pts.Add(At(m, north: 0));

        Assert.Equal(2, StayPointDetector.Detect(pts).Count);
    }

    private static List<RollupPoint> Fragments()
    {
        double[] centersNorth = [0, 60, -50, 55, -45];
        int[] gapsMin = [0, 4, 20, 4, 4];
        var pts = new List<RollupPoint>();
        var minute = 0;
        for (var c = 0; c < centersNorth.Length; c++)
        {
            minute += gapsMin[c];
            for (var k = 0; k < 3; k++) pts.Add(At(minute + (k * 5), north: centersNorth[c] + (k * 3)));
            minute += 10;
        }

        return pts;
    }

    private static RollupPoint At(int minute, double north) =>
        new(T0.AddMinutes(minute), Lat0 + (north / 111_320.0), Lon0, null, null);
}
