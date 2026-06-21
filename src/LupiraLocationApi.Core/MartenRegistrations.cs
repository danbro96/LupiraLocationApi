using LupiraLocationApi.Domain.Telemetry;
using Marten;
using Weasel.Core;

namespace LupiraLocationApi.Domain;

/// <summary>Configures the Marten store for the Location API in the <c>location</c> schema: plain documents only
/// (identity, the devices that feed location telemetry, and the derived location intelligence). The high-frequency
/// time-series lives in a separate <c>telemetry</c> schema owned by raw Npgsql
/// (<see cref="LupiraLocationApi.Telemetry.TelemetrySchema"/>), which Marten's schema-diff never touches. Enums
/// serialize as strings.</summary>
public static class MartenRegistrations
{
    public static StoreOptions UseLupiraLocation(this StoreOptions opts)
    {
        opts.DatabaseSchemaName = "location";
        opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);

        // Identity + the devices that feed location telemetry.
        opts.Schema.For<Principal>().Index(x => x.AuthentikSub).Index(x => x.Email);
        opts.Schema.For<Device>().Index(x => x.PrincipalId);
        opts.Schema.For<DeviceApiKey>().Index(x => x.PrincipalId).Index(x => x.DeviceId);

        // Derived location intelligence + caches (materialized by the rollup; survive raw telemetry drop).
        opts.Schema.For<LocationVisit>().Index(x => x.PrincipalId);
        opts.Schema.For<LocationTrip>().Index(x => x.PrincipalId);
        opts.Schema.For<DailyLocationSummary>().Index(x => x.PrincipalId);
        opts.Schema.For<PlaceLabel>();
        opts.Schema.For<TrackingState>().Index(x => x.PrincipalId);
        opts.Schema.For<LocationRollupCheckpoint>();

        return opts;
    }
}
