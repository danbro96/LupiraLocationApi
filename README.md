# LupiraLocationApi

A self-hosted **location/presence API** for a single owner. It registers the devices that produce GPS
telemetry, ingests their fixes at high volume, and serves the owner's track, movement stats, and derived
**Visits / Trips / daily summaries** back over a clean REST API.

It is built for a *store-and-forward tracking client* (e.g. a phone app, built separately) that uploads
batched fixes whenever it has connectivity, and for an owner who wants to query their own history — never
a multi-tenant or social product. All data belongs to one principal and is isolated in its own database.

## What it does

- **Identity** — a local `Principal` is provisioned just-in-time from the caller's OIDC token. The OIDC
  `sub` is the durable anchor; email is a mutable lookup key. There is no shared user table.
- **Devices** — register / list / rename / retire the devices that feed telemetry. Registration mints a
  one-time, per-device **ingest API key**. A device is owned directly by the principal that registered it.
- **Ingest** — batched NDJSON upload of GPS fixes. Idempotent (each fix carries a device-assigned `seq`),
  resumable (a cursor reports the high-water `seq`), and pausable (a per-device kill-switch). Raw fixes
  land in time-partitioned tables (native weekly range partitions — no PostGIS or TimescaleDB).
- **Query** — latest position per device, raw or server-thinned track, distance/speed stats, bounding-box
  search, derived **Visits / Trips / DailyLocationSummary** (materialized by a nightly rollup), and a
  coarse "where was I at *T*" place label.
- **Maintenance** — a background service provisions upcoming partitions, runs the rollup, and drops raw
  partitions past the retention window.

See [docs/architecture.md](docs/architecture.md) for the full design and the domain model.

## Surfaces

| Surface | Base path | Auth | Notes |
|---|---|---|---|
| REST (owner) | `/` (root) | OIDC JWT (`ApiPolicy`) | Device management + location query/control. |
| Ingest (uploader) | `/ingest` | Per-device key (`IngestPolicy`) | `Authorization: DeviceKey {keyId}.{secret}`. |
| MCP (agent) | `/mcp` | OIDC JWT (`ApiPolicy`) | Streamable HTTP. Read-only, derived/coarse tools — no raw track, no mutations. |
| Health | `/livez`, `/readyz` | none | Liveness / readiness (Postgres reachable). |
| OpenAPI | `/openapi/v1.json` | none | Generated OpenAPI document. |
| API reference | `/scalar/v1` | none | [Scalar](https://github.com/scalar/scalar) interactive UI. |

The **MCP surface is intended to be LAN/WireGuard-only**: it shares the process, DB, and OIDC bearer with
REST, and a defence-in-depth backstop 404s any `/mcp` request that arrives bearing reverse-proxy edge
headers (`CF-Ray` / `CF-Connecting-IP`). Keep it off the public internet at your ingress.

### Route map

REST — `ApiPolicy` (OIDC JWT):

| Method | Route | Purpose |
|---|---|---|
| GET | `/me` | The caller's resolved local identity (JIT-provisioned). |
| GET | `/devices` | List the caller's devices. |
| POST | `/devices` | Register a device; returns the one-time ingest API key. |
| PUT | `/devices/{id}` | Rename a device. |
| DELETE | `/devices/{id}` | Retire a device (revokes its ingest keys). |
| GET | `/location/current` | Latest known location per device. |
| GET | `/location/track` | Raw track over a time range (capped). |
| GET | `/location/track/thinned` | Server-downsampled track (one best-accuracy fix per bucket). |
| GET | `/location/stats` | Distance + speed stats over a range. |
| GET | `/location/bbox` | Fixes within a lat/lon rectangle over a range. |
| GET | `/location/at` | Coarse place label at a timestamp (never the raw fix). |
| GET | `/location/visits` | Materialized stay-points over a range. |
| GET | `/location/trips` | Materialized trips over a range. |
| GET | `/location/summary` | Per-day location rollup. |
| DELETE | `/location` | Purge raw fixes + derived docs in a range (owner erase). |
| POST | `/location/tracking/{deviceId}/pause` | Pause tracking (ingest discarded while paused). |
| POST | `/location/tracking/{deviceId}/resume` | Resume tracking. |
| GET | `/location/tracking/{deviceId}/state` | Tracking state for a device. |

Ingest — `IngestPolicy` (per-device key):

| Method | Route | Purpose |
|---|---|---|
| POST | `/ingest/location` | Ingest a batch of GPS fixes (NDJSON, one fix per line). |
| GET | `/ingest/location/cursor` | The device's resume cursor (last accepted `seq` + `ts`). |
| GET | `/ingest/location/state` | Whether tracking is paused (the uploader should stop collecting if so). |

MCP — `ApiPolicy` (OIDC JWT), Streamable HTTP at `/mcp`. Read-only and derived/coarse by design — no
raw lat·lon track tools, no mutations. Tools call the same Core services as REST, scoped to the caller:

| Tool | Maps to | Returns |
|---|---|---|
| `me` | identity | The caller's resolved local identity. |
| `list_devices` | `/devices` (list) | The caller's devices (to scope `movement_stats`). |
| `list_visits` | `/location/visits` | Materialized stay-points over a range. |
| `list_trips` | `/location/trips` | Materialized trips over a range. |
| `daily_summary` | `/location/summary` | Per-day rollup. |
| `place_at` | `/location/at` | Coarse place label at a timestamp (never the raw fix). |
| `movement_stats` | `/location/stats` | Distance + speed stats over a range (no coordinates). |

## Tech stack

| Area | Choice | Version |
|---|---|---|
| Runtime | .NET | 10 (`net10.0`) |
| Web | ASP.NET Core Minimal APIs | 10 |
| Document store | [Marten](https://martendb.io) on PostgreSQL (plain documents — not event-sourced) | 9.6.0 |
| Time-series | Raw Npgsql over native range-partitioned tables | (Npgsql via Marten) |
| OpenAPI | `Microsoft.AspNetCore.OpenApi` | 10.0.9 |
| Auth | `Microsoft.AspNetCore.Authentication.JwtBearer` | 10.0.9 |
| Agent surface | `ModelContextProtocol.AspNetCore` (MCP, Streamable HTTP) | 1.4.0 |
| API reference UI | `Scalar.AspNetCore` | 2.16.4 |
| Telemetry | OpenTelemetry (traces/metrics/logs, OTLP exporter) | 1.16.x / 1.15.x |
| Tests | xUnit + Testcontainers for PostgreSQL | 2.9.3 / 4.x |

Requires PostgreSQL (any reasonably current version; only stock SQL + native partitioning is used).

## Run locally

**Prerequisites:** .NET 10 SDK, and Docker (for a local Postgres and for the integration tests).

```bash
# A throwaway Postgres for local dev:
docker run --rm -d --name loc-pg -e POSTGRES_PASSWORD=devpassword \
  -e POSTGRES_USER=lupira_location_user -e POSTGRES_DB=lupira_location -p 5432:5432 postgres:17

# Build, test, run:
dotnet build LupiraLocationApi.slnx -c Release
dotnet test  LupiraLocationApi.slnx -c Release        # Server.Tests spin up Postgres via Testcontainers
dotnet run --project src/LupiraLocationApi             # listens on http://localhost:5260 (Development)
```

The default local connection string (`Host=localhost;Port=5432;Database=lupira_location;Username=lupira_location_user;Password=devpassword`)
matches the container above; override it with `ConnectionStrings__Postgres`.

### Apply the schema

Schema is applied deliberately (not on boot). Run the one-shot apply against a fresh database — it creates
both the Marten `location` schema and the raw `telemetry` schema (tables + initial partitions):

```bash
dotnet run --project src/LupiraLocationApi -- --apply-schema
```

### Dev-auth on-ramp

In the **Development** environment the API accepts a dev identity header so you can exercise `/api` without
an OIDC provider:

```bash
# Acts as you@example.com (JIT-provisions a Principal on first call):
curl -H "X-Dev-User: you@example.com" http://localhost:5260/me

# Register a device — the response includes a one-time apiKey ("{keyId}.{secret}"):
curl -X POST -H "X-Dev-User: you@example.com" -H "Content-Type: application/json" \
  -d '{"kind":"Phone","label":"My phone"}' http://localhost:5260/devices

# Ingest a fix with that key (NDJSON, one JSON object per line):
curl -X POST -H "Authorization: DeviceKey <keyId>.<secret>" \
  -H "Content-Type: application/x-ndjson" \
  --data-binary $'{"seq":1,"ts":"2026-01-01T12:00:00Z","lat":59.33,"lon":18.07,"accuracy_m":8,"activity":"Walk"}\n' \
  http://localhost:5260/ingest/location
```

The dev header scheme is registered **only** when the environment is Development; in any other environment
`/api` requires a valid OIDC bearer token.

## Configuration

All configuration is environment-driven (double-underscore maps to nested keys, e.g. `Auth__Authority`).

| Variable | Required | Default | Purpose |
|---|---|---|---|
| `ConnectionStrings__Postgres` | yes (prod) | local dev string | Postgres connection (both schemas live in one DB). |
| `Auth__Authority` | yes (prod) | — | OIDC issuer URL. The API only *validates* JWTs (resource server); any OIDC provider works. |
| `Auth__Audience` | yes (prod) | — | Expected token audience. |
| `Nominatim__BaseUrl` | no | empty | Base URL of a [Nominatim](https://nominatim.org) instance for reverse-geocoded place labels. Empty disables labelling (cache-only / null). |
| `Telemetry__LocationRetentionDays` | no | `90` | Raw-fix retention; older partitions are dropped. |
| `Telemetry__MaintenanceEnabled` | no | `true` | Toggles the background partition/rollup/retention service. |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | no | empty | OTLP collector endpoint. Telemetry export is **enabled only when this is set**; otherwise it is a no-op. |
| `OTEL_EXPORTER_OTLP_PROTOCOL` / `OTEL_EXPORTER_OTLP_HEADERS` / `OTEL_RESOURCE_ATTRIBUTES` | no | — | Standard OpenTelemetry SDK knobs, read by the exporter directly. |

Ingest authentication needs no configuration — keys are minted per device at registration and only their
hash is stored.

## Deploy (Docker / Compose)

The image is a standard multi-stage .NET build ([Dockerfile](Dockerfile)); it listens on `8080`.

```bash
docker build -t lupira-location-api .
docker run --rm -p 8080:8080 \
  -e ConnectionStrings__Postgres="Host=db;Port=5432;Database=lupira_location;Username=lupira_location_user;Password=..." \
  -e Auth__Authority="https://your-oidc-issuer/" \
  -e Auth__Audience="lupira-location" \
  lupira-location-api
```

[deploy/compose.yaml](deploy/compose.yaml) is a sample Compose service definition. Its default hostnames,
networks, image name, and port are the author's own and are meant as **overridable samples** — set the env
to your own values before deploying. [deploy/db/grants.sql](deploy/db/grants.sql) provisions an isolated
role + database. Apply the schema once with `--apply-schema` (above) before first traffic.

### Health probes

- `GET /livez` — liveness; always 200 if the process is up (no dependency checks).
- `GET /readyz` — readiness; 200 only when Postgres is reachable.

## CI

[GitHub Actions](.github/workflows): `ci.yml` restores, builds, and runs the full unit + Testcontainers
integration suite on every PR and branch. `release.yml` re-runs that on merge to `main` (and on `v*` tags)
and builds + pushes the container image.

## Project layout

```
src/
  LupiraLocationApi.Core/      class library — no ASP.NET dependency
    Domain/                    Principal, Device, DeviceApiKey, Telemetry/* (Visits/Trips/etc.), enums
    Application/               transport-neutral services + OpResult; Telemetry/ ingest/query/rollup
    Dtos/  Mappers/            request/response shapes + mapping
  LupiraLocationApi/           ASP.NET host (thin transport/composition layer)
    Endpoints/                 Minimal-API route groups (+ McpExposure LAN-only backstop)
    Handlers/                  endpoint handlers (call Core services)
    Mcp/                       MCP agent tools (read-only; call Core services directly)
    Auth/                      OIDC + device-key + dev-header schemes, CurrentUser
    Http/                      OpResult -> RFC 7807 ProblemDetails mapping
    Health/  Background/       readiness check; partition/rollup/retention service
tests/
  LupiraLocationApi.Core.Tests/      unit tests (pure helpers, value objects)
  LupiraLocationApi.Server.Tests/    integration tests (Testcontainers Postgres)
deploy/                        sample compose.yaml + db/grants.sql
docs/                          architecture.md
```

## License

[MIT](LICENSE) © 2026 Daniel Broström.
