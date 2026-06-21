using Npgsql;

namespace LupiraLocationApi.Telemetry;

/// <summary>Owns the raw <c>telemetry</c> schema (location tables + indexes), applied via the app's <c>--apply-schema</c>
/// one-shot after Marten's own apply. Marten's schema-diff only inspects the <c>location</c> schema, so it never touches
/// these tables. Ongoing partition create/drop is handled at runtime (ingest pre-creates on demand; the maintenance
/// service drops expired ones). All DDL is idempotent.</summary>
public static class TelemetrySchema
{
    public static async Task ApplyAsync(NpgsqlDataSource db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(Ddl, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Wipes all telemetry rows (test isolation). TRUNCATE on a partitioned parent cascades to its partitions.</summary>
    public static async Task TruncateAllAsync(NpgsqlDataSource db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "TRUNCATE telemetry.location_point, telemetry.location_current", conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private const string Ddl = """
        CREATE SCHEMA IF NOT EXISTS telemetry;

        CREATE TABLE IF NOT EXISTS telemetry.location_point (
            principal_id    uuid              NOT NULL,
            device_id       uuid              NOT NULL,
            ts              timestamptz       NOT NULL,
            seq             bigint            NOT NULL,
            lat             double precision  NOT NULL,
            lon             double precision  NOT NULL,
            accuracy_m      real,
            altitude_m      real,
            vertical_acc_m  real,
            heading_deg     real,
            heading_acc_deg real,
            speed_mps       real,
            speed_acc_mps   real,
            provider        smallint,
            activity        smallint,
            activity_conf   smallint,
            battery_pct     smallint,
            is_moving       boolean,
            is_mock         boolean           NOT NULL DEFAULT false,
            received_at     timestamptz       NOT NULL DEFAULT now(),
            PRIMARY KEY (principal_id, device_id, ts, seq)
        ) PARTITION BY RANGE (ts);

        CREATE INDEX IF NOT EXISTS ix_location_point_pid_ts     ON telemetry.location_point (principal_id, ts);
        CREATE INDEX IF NOT EXISTS ix_location_point_pid_latlon ON telemetry.location_point (principal_id, lat, lon);

        CREATE TABLE IF NOT EXISTS telemetry.location_current (
            principal_id uuid             NOT NULL,
            device_id    uuid             NOT NULL,
            ts           timestamptz      NOT NULL,
            seq          bigint           NOT NULL,
            lat          double precision NOT NULL,
            lon          double precision NOT NULL,
            accuracy_m   real,
            speed_mps    real,
            activity     smallint,
            battery_pct  smallint,
            received_at  timestamptz      NOT NULL DEFAULT now(),
            PRIMARY KEY (principal_id, device_id)
        );
        """;
}
