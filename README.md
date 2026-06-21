# LupiraLocationApi

A personal location/presence-tracking API — sibling service to the other Lupira APIs, sharing the same identity
(Authentik OIDC) and operational conventions (.NET 10, Marten on Postgres, Minimal APIs, `OpResult<T>`, OpenTelemetry →
OpenObserve, Docker + GitHub Actions → Docker Hub) but owning its data in its **own database** (`lupira_location`),
isolated from the other services. Extracted from `LupiraHealthApi` — GPS/presence is a distinct product from health
vitals, with its own sharing model and high-volume always-on ingest.

## Scope

- **Identity** — JIT-provisioned `Principal` (the OIDC `sub` is the only cross-service join key; no shared user table).
- **Devices** — register/list/rename/retire the devices that feed location telemetry; registration mints a one-time
  per-device **ingest API key**. A device is owned directly by the principal that registered it (single-owner).
- **Location tracking** — batched NDJSON ingest (idempotent via device `seq`, resumable via a cursor, pausable),
  raw/thinned track + distance/speed stats + bounding-box queries, on-read downsampling, and derived
  **Visits / Trips / DailyLocationSummary** (materialized by a rollup), plus a coarse "where was I at T" place label.
  Raw fixes live in time-partitioned `telemetry` tables (native weekly partitioning, no PostGIS/TimescaleDB).

## Architecture

- `src/LupiraLocationApi.Core` — domain + application services + DTOs (zero ASP.NET). Marten owns the `location` schema;
  the high-frequency time-series lives in a separate `telemetry` schema written by raw Npgsql (binary-array idempotent
  merge), which Marten's schema-diff never touches.
- `src/LupiraLocationApi` — thin ASP.NET host: Minimal-API endpoint groups → handlers → services. Two auth policies:
  `ApiPolicy` (OIDC JWT for humans) and `IngestPolicy` (per-device API key for the uploader).

## Develop

```bash
dotnet build LupiraLocationApi.slnx -c Release
dotnet test  LupiraLocationApi.slnx -c Release      # Server.Tests use Testcontainers (Docker required)
# Apply schema (location + telemetry) to a local/prod DB:
dotnet run --project src/LupiraLocationApi -- --apply-schema
```

In Development, authenticate REST calls with `X-Dev-User: you@example.com`; ingest calls use
`Authorization: DeviceKey {keyId}.{secret}` (from `POST /api/devices`).

A custom mobile app (built separately) pushes GPS telemetry to the ingest endpoints.
