using LupiraLocationApi.Core.Domain;
using Xunit;

namespace LupiraLocationApi.UnitTests;

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
