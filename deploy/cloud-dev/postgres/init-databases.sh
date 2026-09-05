#!/usr/bin/env sh
set -eu

create_database() {
  database_name="$1"

  psql \
    --set=ON_ERROR_STOP=1 \
    --set=database_name="$database_name" \
    --username "$POSTGRES_USER" \
    --dbname "$POSTGRES_DB" <<'SQL'
SELECT format('CREATE DATABASE %I', :'database_name')
WHERE NOT EXISTS (
  SELECT FROM pg_database WHERE datname = :'database_name'
)\gexec
SQL
}

topology_mode="${DATABASE_TOPOLOGY_MODE:-Shared}"
targets=""

append_target() {
  database_name="$1"
  case "$database_name" in
    ''|*[!A-Za-z0-9_-]*)
      echo "database target must be a simple PostgreSQL database name" >&2
      exit 1
      ;;
  esac
  [ "${#database_name}" -le 63 ] || { echo "database target is too long" >&2; exit 1; }
  case " $targets " in
    *" $database_name "*) return ;;
  esac
  targets="$targets $database_name"
}

case "$topology_mode" in
  Shared)
    shared_database="${SHARED_DATABASE_NAME:-${POSTGRES_DB:?POSTGRES_DB is required}}"
    [ "$POSTGRES_DB" = "$shared_database" ] \
      || { echo "Shared topology requires POSTGRES_DB and SHARED_DATABASE_NAME to match" >&2; exit 1; }
    [ "${IDENTITY_DATABASE:?IDENTITY_DATABASE is required}" = "$shared_database" ] \
      || { echo "Shared topology requires IDENTITY_DATABASE to match the shared database" >&2; exit 1; }
    [ "${SYSTEMDATA_DATABASE:?SYSTEMDATA_DATABASE is required}" = "$shared_database" ] \
      || { echo "Shared topology requires SYSTEMDATA_DATABASE to match the shared database" >&2; exit 1; }
    [ "${REFERENCE_DATA_DATABASE:?REFERENCE_DATA_DATABASE is required}" = "$shared_database" ] \
      || { echo "Shared topology requires REFERENCE_DATA_DATABASE to match the shared database" >&2; exit 1; }
    [ "${UNIFIEDHOST_DATABASE:?UNIFIEDHOST_DATABASE is required}" = "$shared_database" ] \
      || { echo "Shared topology requires UNIFIEDHOST_DATABASE to match the shared database" >&2; exit 1; }
    append_target "$shared_database"
    ;;
  PerService)
    append_target "${POSTGRES_DB:?POSTGRES_DB is required}"
    append_target "${IDENTITY_DATABASE:?IDENTITY_DATABASE is required}"
    append_target "${SYSTEMDATA_DATABASE:?SYSTEMDATA_DATABASE is required}"
    append_target "${REFERENCE_DATA_DATABASE:?REFERENCE_DATA_DATABASE is required}"
    if [ -n "${UNIFIEDHOST_DATABASE:-}" ]; then append_target "$UNIFIEDHOST_DATABASE"; fi
    ;;
  *)
    echo "DATABASE_TOPOLOGY_MODE must be Shared or PerService" >&2
    exit 1
    ;;
esac

for database_name in $targets; do
  create_database "$database_name"
done
