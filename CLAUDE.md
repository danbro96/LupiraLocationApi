# LupiraLocationApi — repo rules

Docs: `docs/architecture.md`.

## Split from LupiraHealthApi
- Here: GPS telemetry, visits, trips, daily summaries, coarse place labels. Not here: ring vitals and health records (LupiraHealthApi).
- The only shared contract between the services is the Authentik `sub`. No shared database, schema or foreign keys.
- The ingest engine (NDJSON merge, ingest service) is duplicated in LupiraHealthApi on purpose; fix an engine bug in both. Partitions and device keys come from `Lupira.Postgres.Partitions` and `Lupira.Auth.DeviceKeys`.
