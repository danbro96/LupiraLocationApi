using LupiraLocationApi.Auth;
using LupiraLocationApi.Background;
using LupiraLocationApi.Domain;
using LupiraLocationApi.Endpoints;
using LupiraLocationApi.Handlers;
using LupiraLocationApi.Health;
using LupiraLocationApi.Mcp;
using LupiraLocationApi.Telemetry;
using Marten;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Npgsql;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// --- Bounded context (Marten document store on the `location` schema + the raw-Npgsql `telemetry` path + the
// transport-neutral services). Connection string is read lazily from ConnectionStrings:Postgres inside AddLocationCore. ---
builder.Services.AddLocationCore();

// --- Host-only services: identity (claims -> Core PrincipalDirectory) + the thin REST/ingest handlers. ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<MeHandler>();
builder.Services.AddScoped<DevicesHandler>();
builder.Services.AddScoped<LocationIngestHandler>();
builder.Services.AddScoped<LocationQueryHandler>();

// MCP server for the agent (read-only, derived/coarse tools), mounted at /mcp over Streamable HTTP.
// LAN/WireGuard-only — not published through the tunnel (see UseMcpLanOnly + the MapMcp call below).
builder.Services.AddMcpServer().WithHttpTransport().WithTools<LocationTools>();

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Background maintenance: partition provisioning + nightly rollup + retention drop (gated by config).
builder.Services.AddHostedService<LocationMaintenanceService>();

// --- Auth: OIDC JWT for the REST surface (human reads/writes); per-device API key for /ingest (the mobile uploader).
//           One identity authority (Authentik); the OIDC `sub` is the only cross-service join key. ---
var authBuilder = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Auth:Authority"];
        options.Audience = builder.Configuration["Auth:Audience"];
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    })
    .AddScheme<AuthenticationSchemeOptions, DeviceKeyAuthHandler>(DeviceKeyAuthHandler.SchemeName, _ => { });

// Development-only: allow X-Dev-User header auth so the API can be exercised without Authentik.
if (builder.Environment.IsDevelopment())
    authBuilder.AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevAuthHandler.SchemeName, _ => { });

string[] apiSchemes = builder.Environment.IsDevelopment()
    ? [JwtBearerDefaults.AuthenticationScheme, DevAuthHandler.SchemeName]
    : [JwtBearerDefaults.AuthenticationScheme];

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ApiPolicy", p => p.AddAuthenticationSchemes(apiSchemes).RequireAuthenticatedUser())
    .AddPolicy("IngestPolicy", p => p.AddAuthenticationSchemes(DeviceKeyAuthHandler.SchemeName).RequireAuthenticatedUser());

// --- Observability: OpenTelemetry -> OpenObserve. Env-gated; the OTLP exporter reads OTEL_EXPORTER_OTLP_* itself. ---
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("lupira-location-api"))
    .WithTracing(t =>
    {
        t.AddAspNetCoreInstrumentation();
        t.AddHttpClientInstrumentation();
        t.AddSource(LocationTelemetry.ActivitySourceName);
        if (!string.IsNullOrWhiteSpace(otlpEndpoint)) t.AddOtlpExporter();
    })
    .WithMetrics(m =>
    {
        m.AddAspNetCoreInstrumentation();
        m.AddHttpClientInstrumentation();
        m.AddRuntimeInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint)) m.AddOtlpExporter();
    });

builder.Logging.AddOpenTelemetry(o =>
{
    o.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("lupira-location-api"));
    o.IncludeScopes = true;
    o.IncludeFormattedMessage = true;
    if (!string.IsNullOrWhiteSpace(otlpEndpoint)) o.AddOtlpExporter();
});

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseReadyCheck>("postgres", tags: ["ready"]);

builder.Services.AddOpenApi();

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

app.UseAuthentication();
app.UseAuthorization();

// Defence-in-depth: 404 any /mcp request that arrives bearing Cloudflare edge headers.
app.UseMcpLanOnly();

app.MapOpenApi();   // /openapi/v1.json
app.MapScalarApiReference();   // /scalar/v1

// Health probes: /livez = liveness (no dependency checks); /readyz = readiness (Postgres reachable).
app.MapHealthChecks("/livez", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/readyz", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") })
    .DisableHttpMetrics();

// REST surface.
app.MapMe();
app.MapDevices();
app.MapIngest();
app.MapLocationQuery();

// Agent MCP transport (LAN/WireGuard-only; excluded from the Cloudflare Tunnel at the edge).
app.MapMcp("/mcp").RequireAuthorization("ApiPolicy");

app.Run();

// Exposes the implicit Program entry point to the integration test assembly (WebApplicationFactory<Program>).
public partial class Program;
