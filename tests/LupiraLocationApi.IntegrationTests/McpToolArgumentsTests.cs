using Lupira.Testing.Mcp;
using Lupira.Testing.Postgres;
using Xunit;

namespace LupiraLocationApi.IntegrationTests;

[Collection("integration")]
public sealed class McpToolArgumentsTests(LocationApiTestFactory factory) : McpStrictArgumentsTests
{
    protected override HttpClient CreateAuthenticatedClient() => factory.ApiClient("alice@x.test");

    protected override string DeclaredToolName => "whoami";

    public override Task InitializeAsync() => factory.ResetAsync();
}
