# LupiraLocationApi — repo rules

Docs: `docs/architecture.md`.

## Split from LupiraHealthApi
- Here: GPS telemetry, visits, trips, daily summaries, coarse place labels. Not here: ring vitals and health records (LupiraHealthApi).
- The only shared contract between the services is the Authentik `sub`. No shared database, schema or foreign keys.
- The ingest engine (NDJSON merge, ingest service) is duplicated in LupiraHealthApi on purpose; fix an engine bug in both. Partitions and device keys come from `Lupira.Postgres.Partitions` and `Lupira.Auth.DeviceKeys`.

## Rollup
- Days are local (`Location:Rollup:TimeZoneId`); a window reprocesses from the arrival of the stay in progress at its start.
- Bump `TripVisitService.AlgorithmVersion` whenever visit/trip derivation changes; the worker then rebuilds the retention window.
- Late fixes are re-rolled from their local day via the `received_at` watermark (`LocationRollupCheckpoint.ReceivedThrough`).
- `place_at` is evidence, not a guess: visit, else a fix within 30 min, else `none`.
- Trip mode comes from displacement + GNSS speed (`TripModeClassifier`), never per-fix activity labels.
