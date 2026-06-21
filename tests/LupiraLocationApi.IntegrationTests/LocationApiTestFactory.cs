using LupiraLocationApi.Telemetry;
using Marten;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace LupiraLocationApi.IntegrationTests;

/// <summary>
/// Hosts the real app against an ephemeral Postgres (Testcontainers). Runs in <c>Development</c> so the dev auth handler
/// is wired (<c>X-Dev-User</c> for <c>/api</c>); telemetry ingest uses a real per-device key minted via the API. Both the
/// Marten <c>location</c> schema and the raw <c>telemetry</c> schema are applied once; data is reset per test. The
/// background maintenance service is disabled so it never races the reset.
/// </summary>
public sealed class LocationApiTestFactory : WebApplicationFactory<Program>
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private bool _schemaApplied;

    public LocationApiTestFactory() => _postgres.StartAsync().GetAwaiter().GetResult();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration(cfg =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
                ["Telemetry:MaintenanceEnabled"] = "false",
            }));
    }

    public IDocumentStore Store => Services.GetRequiredService<IDocumentStore>();
    public NpgsqlDataSource DataSource => Services.GetRequiredService<NpgsqlDataSource>();

    /// <summary>Ensure both schemas exist (once), then wipe all Marten documents/events and all telemetry rows.</summary>
    public async Task ResetAsync()
    {
        if (!_schemaApplied)
        {
            await Store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
            await TelemetrySchema.ApplyAsync(DataSource);
            _schemaApplied = true;
        }
        await Store.Advanced.ResetAllData();
        await TelemetrySchema.TruncateAllAsync(DataSource);
    }

    public HttpClient ApiClient(string email)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", email);
        return client;
    }

    public HttpClient DeviceKeyClient(string apiKey)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"DeviceKey {apiKey}");
        return client;
    }

    /// <summary>A client with no auth header — for asserting unauthenticated requests are rejected.</summary>
    public HttpClient AnonymousClient() => CreateClient();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _postgres.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
