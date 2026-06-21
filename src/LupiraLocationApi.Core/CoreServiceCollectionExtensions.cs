using LupiraLocationApi.Application;
using LupiraLocationApi.Domain;
using Marten;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the LupiraLocationApi bounded context into the host's DI container: the Marten store (document
/// store on the <c>location</c> schema), a shared <see cref="NpgsqlDataSource"/> for the raw telemetry path (same
/// Postgres, <c>telemetry</c> schema), and the transport-neutral services.</summary>
public static class CoreServiceCollectionExtensions
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=lupira_location;Username=lupira_location_user;Password=devpassword";

    public static IServiceCollection AddLocationCore(this IServiceCollection services)
    {
        // Resolve the connection string lazily from IConfiguration so test hosts (WebApplicationFactory) can override
        // ConnectionStrings:Postgres before the store / data source is built.
        services.AddMarten(sp =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres") ?? DefaultConnectionString;
            var opts = new StoreOptions();
            opts.Connection(connectionString);
            opts.UseLupiraLocation();
            return opts;
        }).UseLightweightSessions();

        // The raw time-series path borrows connections from this pool. Marten gets its own sessions; both point at the
        // same Postgres but never fight (different schemas).
        services.AddSingleton(sp =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres") ?? DefaultConnectionString;
            return NpgsqlDataSource.Create(connectionString);
        });

        services.AddScoped<PrincipalDirectory>();
        services.AddScoped<DeviceService>();

        services.AddLocationTelemetry();
        return services;
    }
}
