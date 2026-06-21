using LupiraLocationApi.Domain;
using LupiraLocationApi.Domain.Telemetry;
using Xunit;

namespace LupiraLocationApi.Core.Tests;

public class DeviceKeyHashingTests
{
    [Fact]
    public void Minted_key_verifies_against_its_hash()
    {
        var (keyId, secret, hash) = DeviceKeyHashing.Mint();
        Assert.NotEqual(Guid.Empty, keyId);
        Assert.True(DeviceKeyHashing.Verify(secret, hash));
        Assert.False(DeviceKeyHashing.Verify(secret + "x", hash));
    }

    [Fact]
    public void Format_then_parse_roundtrips()
    {
        var (keyId, secret, _) = DeviceKeyHashing.Mint();
        var cred = DeviceKeyHashing.Format(keyId, secret);
        Assert.True(DeviceKeyHashing.TryParse(cred, out var parsedId, out var parsedSecret));
        Assert.Equal(keyId, parsedId);
        Assert.Equal(secret, parsedSecret);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nodot")]
    [InlineData("not-a-guid.secret")]
    [InlineData(".secret")]
    public void TryParse_rejects_malformed(string cred) => Assert.False(DeviceKeyHashing.TryParse(cred, out _, out _));
}

public class PlaceLabelTests
{
    [Fact]
    public void Quantize_snaps_nearby_coords_to_the_same_cell()
    {
        Assert.Equal(PlaceLabel.MakeId(59.32510, 18.07110), PlaceLabel.MakeId(59.32499, 18.07051));
        Assert.NotEqual(PlaceLabel.MakeId(59.325, 18.071), PlaceLabel.MakeId(59.345, 18.071));
    }
}

public class DeterministicIdTests
{
    [Fact]
    public void DailyLocationSummary_id_is_stable_and_distinct()
    {
        var p = Guid.NewGuid();
        var d = Guid.NewGuid();
        var day = new DateOnly(2026, 6, 18);
        Assert.Equal(DailyLocationSummary.MakeId(p, d, day), DailyLocationSummary.MakeId(p, d, day));
        Assert.NotEqual(DailyLocationSummary.MakeId(p, d, day), DailyLocationSummary.MakeId(p, d, day.AddDays(1)));
    }
}
