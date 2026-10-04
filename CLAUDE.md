# LupiraLocationApi — repo rules

Docs: `docs/architecture.md`.

## Split from LupiraHealthApi
- Here: GPS telemetry, visits, trips, daily summaries, coarse place labels. Not here: ring vitals and health records (LupiraHealthApi).
- The only shared contract between the services is the Authentik `sub`. No shared database, schema or foreign keys.
- The ingest engine (`PartitionManager`, NDJSON merge, ingest service) is duplicated in both repos on purpose. Fix an engine bug in both. Do not extract a shared NuGet package.
