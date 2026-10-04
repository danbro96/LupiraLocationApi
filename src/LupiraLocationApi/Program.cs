using Lupira.Auth.DeviceKeys.AspNetCore;
using Lupira.Auth.Jwt;
using Lupira.Hosting.Defaults;
using Lupira.Hosting.Health;
using Lupira.Hosting.LanEdge;
using Lupira.Hosting.Observability;
using Lupira.Hosting.OpenApi;
using Lupira.Hosting.Problems;
using Lupira.Identity.Marten.AspNetCore;
using Lupira.Mcp;
using Lupira.Postgres.Health;
using LupiraLocationApi.Auth;
using LupiraLocationApi.Core.Telemetry;
using LupiraLocationApi.Endpoints;
using LupiraLocationApi.Handlers;
using LupiraLocationApi.Mcp;
using LupiraLocationApi.Workers;
using Marten;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// --- Bounded context (Marten document store on the `location` schema + the raw-Npgsql `telemetry` path + the
// transport-neutral services). Connection string is read lazily from ConnectionStrings:Postgres inside AddLocationCore. ---
builder.Services.AddLocationCore();

// --- Host-only services: identity (claims -> PrincipalDirectory) + the thin REST/ingest handlers. ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddLupiraCurrentUser();
builder.Services.AddScoped<MeHandler>();
builder.Services.AddScoped<DevicesHandler>();
builder.Services.AddScoped<LocationIngestHandler>();
builder.Services.AddScoped<LocationQueryHandler>();
builder.Services.AddScoped<InternalLocationHandler>();

// MCP server for the agent (read-only, derived/coarse tools), mounted at /mcp over Streamable HTTP.
// LAN/WireGuard-only — not published through the tunnel (see UseLanOnlySurfaces + the MapLupiraMcp call below).
builder.Services.AddLupiraMcp().WithTools<LocationTools>();

builder.AddLupiraDefaults(o =>
{
    o.CaseInsensitiveProperties = true;
    o.ForwardedHeaders = ForwardedHeaders.None;
});

// Background maintenance: partition provisioning + nightly rollup + retention drop (gated by config).
builder.Services.AddHostedService<LocationMaintenanceService>();

// --- Auth: OIDC JWT for the REST surface (human reads/writes); per-device API key for /ingest (the mobile uploader).
//           One identity authority (Authentik); the OIDC `sub` is the only cross-service join key. ---
builder.AddLupiraJwt().AddLupiraDeviceKeys<MartenDeviceKeyStore>();

var apiSchemes = LupiraJwtSchemes.Api(builder.Environment);
builder.Services.AddAuthorizationBuilder()
    .AddLupiraApiPolicy(apiSchemes)
    .AddLupiraApiPolicy([DeviceKeyAuthHandler.SchemeName], "IngestPolicy")
    .AddLupiraInternalScopePolicy(apiSchemes);

builder.AddLupiraTelemetry("lupira-location-api");

builder.Services.AddLupiraHealth().AddReadyCheck<DatabaseReadyCheck>("postgres");

builder.Services.AddLupiraProblems();

builder.Services.AddOpenApi("v1", options => options.AddLupiraConventions(o =>
{
    o.Title = "Lupira Location API";
    o.Description =
        "Location and presence backend for Lupira. " +
        "Authenticate with a Bearer token issued by the OIDC provider (Authentik).";
    o.DropNullEnumMembers = true;
}));

var app = builder.Build();

// One-shot schema apply (deploy step: `dotnet LupiraLocationApi.dll --apply-schema`). Applies the Marten `location`
// schema AND the raw `telemetry` schema (tables + initial partitions), which Marten's diff never touches.
if (args.Contains("--apply-schema"))
{
    var store = app.Services.GetRequiredService<IDocumentStore>();
    await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    await TelemetrySchema.ApplyAsync(app.Services.GetRequiredService<NpgsqlDataSource>());
    Console.WriteLine("Schema applied.");
    return;
}

// LAN-only surfaces (/mcp + its discovery metadata): 404 anything arriving through the tunnel,
// before auth so a tunnelled probe never even receives a challenge.
app.UseLanOnlySurfaces("/mcp", "/internal", "/.well-known/oauth-protected-resource");

app.UseLupiraDefaults();
app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

app.MapLupiraOpenApi(o => o.Title = "Lupira Location API");

app.MapLupiraHealth();

// REST surface.
app.MapMe();
app.MapDevices();
app.MapIngest();
app.MapLocationQuery();
app.MapInternal();

// Agent MCP transport (LAN/WireGuard-only; excluded from the Cloudflare Tunnel at the edge).
// RFC 9728 metadata lets MCP clients discover the Authentik issuer from the 401 challenge.
app.MapMcpResourceMetadata(app.Configuration["Auth:Oidc:Authority"]);
app.MapLupiraMcp();

app.Run();

// Exposes the implicit Program entry point to the integration test assembly (WebApplicationFactory<Program>).
public partial class Program;
