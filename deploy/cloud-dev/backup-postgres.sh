#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

root_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
backup_root="${BACKUP_ROOT:-/var/backups/industrial-platform/postgres}"
retention_days="${BACKUP_RETENTION_DAYS:-7}"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
destination="$backup_root/$timestamp"

cd "$root_dir"
[[ -f .env ]] || { echo "Missing $root_dir/.env" >&2; exit 1; }
mkdir -p "$destination"

compose=(docker compose --env-file .env -f compose.yaml --profile core)

dump_database() {
  database_env_name="$1"
  output_name="$2"
  "${compose[@]}" exec -T postgres sh -ec \
    "exec pg_dump --format=custom --create --username=\"\$POSTGRES_USER\" \"\$$database_env_name\"" \
    > "$destination/$output_name.dump"
}

"${compose[@]}" exec -T postgres sh -ec \
  'exec pg_dumpall --roles-only --username="$POSTGRES_USER"' \
  | gzip -9 > "$destination/roles.sql.gz"

dump_database POSTGRES_DB platform
dump_database IDENTITY_DATABASE identity

if [[ "$("${compose[@]}" exec -T postgres sh -ec 'printf %s "$REFERENCE_DATA_DATABASE"')" != \
      "$("${compose[@]}" exec -T postgres sh -ec 'printf %s "$POSTGRES_DB"')" ]]; then
  dump_database REFERENCE_DATA_DATABASE reference-data
fi

(cd "$destination" && sha256sum ./* > SHA256SUMS)
find "$backup_root" -mindepth 1 -maxdepth 1 -type d \
  -mmin "+$((retention_days * 1440))" -print -exec rm -rf -- {} +
echo "PostgreSQL backup completed: $destination"
