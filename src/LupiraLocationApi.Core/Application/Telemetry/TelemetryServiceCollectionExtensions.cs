using Lupira.Postgres.Partitions;
using LupiraLocationApi.Core.Application.Telemetry;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the location telemetry subsystem (raw-Npgsql ingest/query + derived intelligence) into DI.</summary>
public static class TelemetryServiceCollectionExtensions
{
    public static IServiceCollection AddLocationTelemetry(this IServiceCollection services)
    {
        services.AddSingleton(new PartitionManager("telemetry"));
        services.AddScoped<TrackingStateService>();
        services.AddScoped<PlaceLabelService>();
        services.AddScoped<LocationIngestService>();
        services.AddScoped<LocationQueryService>();
        services.AddScoped<TripVisitService>();
        return services;
    }
}
