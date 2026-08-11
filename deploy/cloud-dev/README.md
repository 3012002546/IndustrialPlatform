# Cloud development infrastructure

This directory is the reproducible source for the private Ubuntu development
host. The server runs published container images only; it does not clone or
build the application repository.

## Security boundary

- Copy `.env.example` to `/opt/industrial-platform-dev/.env`, replace all
  sample values, and set mode `0600`. Never commit or print that file.
- Every published port binds to `TAILSCALE_IP`. Do not use `0.0.0.0`, a public
  interface, or a public firewall rule for these services.
- The real local client file is
  `src/backend/appsettings.Development.local.json`; it and `src/backend/.ssh/`
  are ignored by Git.
- Docker uses the host's configured registry mirror. Compose contains no
  registry credentials and performs no image builds.

## Profiles and 4 GB host budget

`core` is the default operational profile and runs PostgreSQL plus Redis
(1 GiB container memory limit in total). `messaging` adds RabbitMQ (768 MiB),
and `observability` adds Seq (768 MiB). Seq is intentionally off by default.

```bash
cd /opt/industrial-platform-dev
sudo ./manage.sh start                 # core
sudo ./manage.sh start messaging       # core + RabbitMQ
sudo ./manage.sh start observability   # core + Seq
sudo ./manage.sh start all             # all four services
sudo ./manage.sh status
sudo ./manage.sh stop
sudo ./manage.sh update core           # pull published images, never build
```

PostgreSQL 18 uses the volume mount `/var/lib/postgresql`, allowing the image
to maintain its versioned cluster directory below it. PostgreSQL is capped at
768 MiB with 50 connections and conservative shared/work memory. Redis is
capped at 256 MiB and 192 MiB of cached data with `allkeys-lru` eviction.

The initialization script runs only when PostgreSQL creates a fresh cluster.
It creates the Identity and ReferenceData databases idempotently and sets
`ON_ERROR_STOP`, so SQL failures fail container initialization instead of
being silently ignored. For an existing volume, create a newly added database
explicitly; never delete the volume merely to rerun initialization.

## Backups and restore

Install the supplied systemd units once:

```bash
sudo install -m 0644 industrial-platform-postgres-backup.service \
  /etc/systemd/system/
sudo install -m 0644 industrial-platform-postgres-backup.timer \
  /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now industrial-platform-postgres-backup.timer
```

The timer runs daily near 03:20 UTC. `backup-postgres.sh` writes private,
checksummed custom-format dumps below
`/var/backups/industrial-platform/postgres/<UTC timestamp>` and removes backup
directories older than seven days. It also stores cluster roles as compressed
SQL. Copy important backups off-host; seven local days are operational
convenience, not disaster recovery.

Restore into a stopped application environment after copying the selected
backup directory to the server:

```bash
cd /opt/industrial-platform-dev
sudo ./manage.sh start core
gunzip -c /path/to/backup/roles.sql.gz | \
  docker compose --env-file .env -f compose.yaml --profile core exec -T \
  postgres sh -ec 'psql --set=ON_ERROR_STOP=1 --username="$POSTGRES_USER" --dbname=postgres'
docker compose --env-file .env -f compose.yaml --profile core exec -T \
  postgres sh -ec 'pg_restore --exit-on-error --clean --if-exists --create --username="$POSTGRES_USER" --dbname=postgres' \
  < /path/to/backup/identity.dump
```

Repeat the `pg_restore` command for `platform.dump` and, when present,
`reference-data.dump`. Verify the backup's `SHA256SUMS` first. A restore with
`--clean --create` replaces the named database; take a fresh backup and stop
application writers before running it.

## Local optional configuration

Copy `src/backend/appsettings.Development.local.example.json` to
`src/backend/appsettings.Development.local.json`. With
`RemoteDevelopment.Enabled=false` or when the file is absent, both services
use their checked-in SQLite databases. With it enabled, PostgreSQL and Redis
use the Tailnet host. RabbitMQ and Seq each require their own `Enabled=true`;
this keeps the default core profile usable without Seq.

## Safe SSH/Tailscale posture

Service ports remain bound only to the Tailnet address. Keep the cloud
provider firewall closed for PostgreSQL, Redis, RabbitMQ, and Seq. The supplied
SSH hardening installer checks for an authorized key, validates the candidate
configuration with `sshd -t`, reloads rather than restarts SSH, and requires a
second key-authenticated session to confirm success; otherwise it rolls back.
It does not change Tailscale routes, exit-node settings, or ACLs.

## Future lightweight application deployment

Keep infrastructure and application deployments separate. Publish immutable
Gateway/API images in CI, then deploy them with a second Compose project on the
same host. Give application containers a combined memory budget of roughly
1.0-1.25 GiB, one replica per service, read-only root filesystems where
supported, and a private Docker network to core services. Bind only the
Gateway to the Tailnet; do not publish individual API ports. Use rolling image
replacement plus health checks, and keep database migrations as an explicit
one-shot release step. Leave Seq disabled unless a short diagnostic window
justifies its memory cost; ship structured console logs to a remote service or
retain bounded Docker logs by default.
