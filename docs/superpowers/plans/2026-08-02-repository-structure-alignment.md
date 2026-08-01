# Repository Structure Alignment Plan

**Goal:** Strictly align the empty repository skeleton with the startup implementation guide and integrate it into `develop`.

1. Move the solution and existing backend projects under `src/backend` and repair paths.
2. Add the required frontend, Gateway, Tools, test, Docker, deployment, `.codex`, and GitHub template directories as tracked placeholders.
3. Update only README and CI paths affected by the move.
4. Run a required-path check and one `dotnet build`.
5. Commit the intended changes, create/fast-forward `develop`, and push when a GitHub remote and authentication are available.
