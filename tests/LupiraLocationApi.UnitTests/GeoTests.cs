using LupiraLocationApi.Core.Application.Telemetry;
using Xunit;

namespace LupiraLocationApi.UnitTests;

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
