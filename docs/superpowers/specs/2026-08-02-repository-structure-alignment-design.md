# Repository Structure Alignment Design

## Goal

Strictly align the bootstrap repository with `01-Industrial Platform开发启动实施方案`, integrate the resulting structure into the normal `develop` branch, and prepare it for publication to GitHub.

## Scope

This change restructures and completes the repository skeleton only. It does not implement frontend features, infrastructure integrations, deployment behavior, or business capabilities.

## Repository Layout

The repository will use the following top-level structure:

```text
IndustrialPlatform/
├── docs/
├── src/
│   ├── backend/
│   └── frontend/
├── tests/
├── docker/
├── deploy/
├── .github/
└── .codex/
```

Existing backend projects will move beneath `src/backend`. The backend solution will be located at `src/backend/IndustrialPlatform.slnx`, with project groups under:

```text
src/backend/
├── src/
│   ├── BuildingBlocks/
│   ├── Services/
│   ├── Gateway/
│   └── Tools/
└── IndustrialPlatform.slnx
```

Existing BuildingBlocks, Identity, and ReferenceData projects retain their current namespaces and project boundaries. Only their filesystem locations, solution entries, project-reference paths, documentation, and CI paths change.

## Missing Skeletons

The following structure will be added:

- `src/frontend` with the directories required by the implementation guide. It remains a documented placeholder rather than a generated Vue application.
- `tests/UnitTests`, `tests/IntegrationTests`, `tests/ApiTests`, `tests/PerformanceTests`, and `tests/E2ETests`.
- `docker/postgres`, `docker/redis`, `docker/rabbitmq`, `docker/nginx`, and `docker/seq`.
- `deploy/docker-compose`, `deploy/kubernetes`, `deploy/nginx`, `deploy/scripts`, and `deploy/environment`.
- `src/backend/src/Gateway` and `src/backend/src/Tools`.
- `.codex/project-context.md`, `architecture.md`, `coding-rule.md`, `database-rule.md`, `api-rule.md`, `task-template.md`, and `commit-rule.md`.
- GitHub issue and pull-request templates plus the planned backend, frontend, and Docker workflow filenames.

Empty structural directories will contain concise `README.md` files or `.gitkeep` markers so Git tracks them without implying that their runtime implementation exists.

## CI and Documentation

The existing backend build workflow will be moved or renamed to `.github/workflows/backend-ci.yml` and updated to build the relocated solution. Placeholder frontend and Docker workflows will be safe to keep inactive until their corresponding runnable assets exist; they must not report false successful builds.

The root README and relevant bootstrap documentation will use the new paths. Historical design documents will not be broadly rewritten unless a referenced command becomes incorrect because of this migration.

## Branch and Publication Strategy

The existing `feature/bootstrap` worktree is already isolated and will be used for the restructuring. After validation, its commits will be integrated into a local `develop` branch. No history-destructive operation will be used.

Publishing requires a configured GitHub `origin`. The current repository has no remote, and GitHub CLI is not installed. Local restructuring and integration can finish independently; publication will pause until a repository URL is supplied and an authenticated GitHub mechanism is available.

## Validation

Because the repository is still a skeleton, validation is intentionally lightweight:

1. Verify all required tracked paths exist.
2. Verify the relocated solution references every existing project.
3. Run `dotnet build src/backend/IndustrialPlatform.slnx`.
4. Inspect Git status and the final diff before committing or publishing.

No Docker, frontend, deployment, integration, performance, or end-to-end runtime validation is required in this step.
