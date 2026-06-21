-- lupira-location-api: provision the `lupira_location` database on the shared medelynas-db.
-- One role, one logical database, isolated from the other Lupira apps (no cross-grants). The app owns the `location`
-- schema (Marten, via `--apply-schema`) AND the `telemetry` schema (raw partitioned tables, via TelemetrySchema applied
-- by the same `--apply-schema` step) — none of those tables are created here.
--
-- Apply (TrueNAS Shell), substituting a freshly generated password:
--   LUPIRA_LOCATION_DB_PW="$(openssl rand -hex 32)"; echo "$LUPIRA_LOCATION_DB_PW"   # save to your password manager
--   docker exec -i medelynas-db psql -U medelynas_admin -v app_password="'$LUPIRA_LOCATION_DB_PW'" postgres < grants.sql

CREATE ROLE lupira_location_user WITH LOGIN PASSWORD :'app_password';
CREATE DATABASE lupira_location OWNER lupira_location_user;
REVOKE ALL ON DATABASE lupira_location FROM PUBLIC;
GRANT CONNECT ON DATABASE lupira_location TO lupira_location_user;
