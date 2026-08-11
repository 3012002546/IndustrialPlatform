# Cloud development infrastructure handoff (2026-08-11)

## Delivered in the repository

- `deploy/cloud-dev/compose.yaml` defines immutable-image-only infrastructure
  profiles: default operational profile `core` (PostgreSQL + Redis), optional
  `messaging` (RabbitMQ), and optional `observability` (Seq).
- PostgreSQL 18 persists `/var/lib/postgresql`; its initialization script uses
  `ON_ERROR_STOP` and idempotently creates the platform, Identity, and
  ReferenceData databases.
- Core fits the 4 GB host with conservative PostgreSQL and Redis settings.
  RabbitMQ and Seq have explicit limits and are not part of default startup.
- `manage.sh` provides non-sensitive start, stop, status, and pull/update
  operations. It never builds an application or infrastructure image.
- `backup-postgres.sh` creates private checksummed database/role backups, and
  the supplied systemd timer runs it daily with seven-day on-host retention.
  Restore commands and cautions are in `deploy/cloud-dev/README.md`.
- Local APIs retain SQLite defaults. The ignored local configuration switches
  PostgreSQL/Redis to the Tailnet only when `RemoteDevelopment.Enabled=true`.
  RabbitMQ and Seq are separately opt-in, and Seq remains disabled by default.
- A guarded SSH installer validates the candidate configuration, reloads SSH,
  requires a second key-authenticated session, and automatically rolls back
  when confirmation is absent.
- The operations guide includes a future lightweight application deployment
  shape based on CI-published immutable images, a private container network,
  Tailnet-only Gateway exposure, and Seq disabled by default.

## Applied to the development server

- Replaced the ad-hoc Compose and empty PostgreSQL initializer with the
  version-controlled files under `/opt/industrial-platform-dev` while
  preserving the private `.env` and all named data volumes.
- Restricted the server `.env` to root-only access.
- Pulled published PostgreSQL and Redis images and recreated/retained core
  services with the new resource settings. No server-side image build ran.
- Stopped RabbitMQ and Seq under their optional profiles without deleting
  containers' named volumes.
- Installed and enabled the daily PostgreSQL backup timer.
- Applied SSH public-key-only hardening after `sshd -t` validation and a
  successful second-session confirmation. No Tailscale route, ACL, exit-node,
  or SSH port setting changed.

## Explicitly deferred to the independent test task

No unit, integration, API, E2E, smoke, or full test suite was run in this task.
The independent test task should:

1. Run the existing configuration-binding tests and update only test code that
   must express the new explicit `RabbitMq.Enabled` / `Seq.Enabled` behavior.
2. Cover missing local file, `RemoteDevelopment.Enabled=false`, core-only
   remote configuration, and each optional profile override.
3. Exercise a disposable fresh PostgreSQL volume to prove all databases are
   created and SQL errors fail initialization; do not use the server's live
   development volume for this.
4. Exercise backup/restore against disposable data and validate checksums and
   retention behavior.
5. Run any integration, E2E, smoke, and full suites only from that separate
   authorized test task.

## User follow-up

- Add `RabbitMq.Enabled=true` or `Seq.Enabled=true` to the ignored local file
  only when the matching server profile is intentionally started.
- Arrange off-host backup replication if the server becomes important; seven
  local days do not protect against host loss.
- Define Tailnet ACL ownership outside this repository. Do not widen cloud
  firewall rules for infrastructure ports.
- Choose an image registry and CI release policy before deploying application
  containers to this 4 GB host.
