using LupiraLocationApi.Core.Domain.Telemetry;
using Xunit;

namespace LupiraLocationApi.UnitTests;

public class PlaceLabelTests
{
    [Fact]
    public void Quantize_snaps_nearby_coords_to_the_same_cell()
    {
        Assert.Equal(PlaceLabel.MakeId(59.32510, 18.07110), PlaceLabel.MakeId(59.32499, 18.07051));
        Assert.NotEqual(PlaceLabel.MakeId(59.325, 18.071), PlaceLabel.MakeId(59.345, 18.071));
    }
}
