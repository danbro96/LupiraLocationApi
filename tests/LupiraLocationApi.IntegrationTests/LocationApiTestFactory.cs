using Lupira.Testing.Postgres;
using LupiraLocationApi.Core.Telemetry;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LupiraLocationApi.IntegrationTests;

/// <summary>
/// Telemetry ingest uses a real per-device key minted via the API. Both the Marten <c>location</c> schema and the raw
/// <c>telemetry</c> schema are applied once; data is reset per test. The background maintenance service is disabled so
/// it never races the reset.
/// </summary>
public sealed class LocationApiTestFactory : LupiraApiFactory<Program>
{
    public IDocumentStore Store => Services.GetRequiredService<IDocumentStore>();
    public NpgsqlDataSource DataSource => Services.GetRequiredService<NpgsqlDataSource>();

    protected override string AuthentikSlug => "lupira-location";

    protected override async Task ApplySchemaAsync()
    {
        await Store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await TelemetrySchema.ApplyAsync(DataSource);
    }

    protected override async Task ResetDataAsync()
    {
        await Store.Advanced.ResetAllData();
        await TelemetrySchema.TruncateAllAsync(DataSource);
    }

    protected override void AddSettings(IDictionary<string, string?> settings) =>
        settings["Telemetry:MaintenanceEnabled"] = "false";
}
