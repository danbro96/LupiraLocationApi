using Xunit;

namespace LupiraLocationApi.Server.Tests;

/// <summary>Generic REST surface: identity (/me), JIT-provisioned on first login.</summary>
public sealed class RestEndpointsTests(LocationApiTestFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Me_returns_the_dev_user()
    {
        var api = Factory.ApiClient("alice@x.test");
        var me = await GetMeAsync(api);
        Assert.Equal("alice@x.test", me.Email);
        Assert.NotEqual(Guid.Empty, me.Id);
    }

    [Fact]
    public async Task Me_is_stable_per_user_and_distinct_across_users()
    {
        var a = Factory.ApiClient("alice@x.test");
        var idA1 = await GetMyIdAsync(a);
        var idA2 = await GetMyIdAsync(a);
        Assert.Equal(idA1, idA2);

        var b = Factory.ApiClient("bob@x.test");
        Assert.NotEqual(idA1, await GetMyIdAsync(b));
    }
}
