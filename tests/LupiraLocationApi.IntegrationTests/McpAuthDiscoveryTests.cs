using Lupira.Testing.Mcp;
using Lupira.Testing.Postgres;
using Xunit;

namespace LupiraLocationApi.IntegrationTests;

[Collection("integration")]
public sealed class McpAuthDiscoveryTests(LocationApiTestFactory factory) : McpResourceMetadataTests
{
    protected override HttpClient CreateAnonymousClient() => factory.AnonymousClient();

    protected override string Issuer => factory.Authority!;

    public override Task InitializeAsync() => factory.ResetAsync();
}
