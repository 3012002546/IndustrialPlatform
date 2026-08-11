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

create_database "${IDENTITY_DATABASE:?IDENTITY_DATABASE is required}"
create_database "${REFERENCE_DATA_DATABASE:?REFERENCE_DATA_DATABASE is required}"
