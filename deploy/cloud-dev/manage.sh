#!/usr/bin/env bash
set -Eeuo pipefail

root_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cd "$root_dir"

compose=(docker compose --env-file .env -f compose.yaml)
all_profiles=(--profile core --profile messaging --profile observability)

usage() {
  echo "Usage: $0 {start|stop|status|update} [core|messaging|observability|all]" >&2
  exit 2
}

selected_profiles() {
  case "${1:-core}" in
    core) printf '%s\n' --profile core ;;
    messaging) printf '%s\n' --profile core --profile messaging ;;
    observability) printf '%s\n' --profile core --profile observability ;;
    all) printf '%s\n' --profile core --profile messaging --profile observability ;;
    *) usage ;;
  esac
}

[[ -f .env ]] || { echo "Missing $root_dir/.env" >&2; exit 1; }
command -v docker >/dev/null || { echo "docker is required" >&2; exit 1; }

action="${1:-}"
mapfile -t profiles < <(selected_profiles "${2:-core}")

case "$action" in
  start)
    "${compose[@]}" "${profiles[@]}" up -d --remove-orphans
    ;;
  stop)
    "${compose[@]}" "${all_profiles[@]}" stop
    ;;
  status)
    "${compose[@]}" "${all_profiles[@]}" ps --all
    ;;
  update)
    "${compose[@]}" "${profiles[@]}" pull
    "${compose[@]}" "${profiles[@]}" up -d --remove-orphans
    docker image prune -f --filter "until=168h"
    ;;
  *) usage ;;
esac
