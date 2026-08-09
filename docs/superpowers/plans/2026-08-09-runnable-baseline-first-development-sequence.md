# Runnable Baseline First Development Sequence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reorder Industrial Platform development so the already-complete BuildingBlocks foundation is followed by a runnable local platform, the first unified frontend shell, Identity login integration, and then vertically delivered business services with matching pages.

**Architecture:** `docs/implementation` remains the single source of dispatch state. Two new bounded plans define the runnable backend/infrastructure baseline and the first frontend shell; existing service plans consume those outputs and require corresponding pages in the same delivery phase.

**Tech Stack:** Markdown, .NET 10, Docker Compose, PostgreSQL, Redis, RabbitMQ, Seq, Vue 3, TypeScript, Vite, Pinia, Vue Router, Element Plus, Vitest, Playwright.

## Global Constraints

- Modify documentation only; do not implement backend, frontend, Docker, tests, or deployment assets in this task.
- Treat `CLAUDE.md` as the current code-progress source and do not modify it.
- Mark BuildingBlocks complete and do not redispatch its finished tasks.
- First frontend scope is login, PC shell, dashboard, 403/404, PDA shell, Mobile shell, and Mock auth boundary only.
- Deliver each later service with its corresponding pages, contract tests, and critical-path E2E coverage.
- Preserve unrelated untracked files and user changes.

---

### Task 1: Define the runnable platform baseline TODO

**Files:**
- Create: `docs/implementation/02A-Industrial Platform可运行基线开发实施方案.md`

**Interfaces:**
- Consumes: completed BuildingBlocks, Identity/ReferenceData skeletons, repository placeholders.
- Produces: dispatch tasks `TASK-BASE-001` through `TASK-BASE-006` for startup, infrastructure, gateway, configuration, verification, and onboarding.

- [x] Write exact scope, dependencies, outputs, verification evidence, and completion criteria.
- [x] Keep Identity business functionality and frontend implementation out of this document.

### Task 2: Define the first unified frontend TODO

**Files:**
- Create: `docs/implementation/02B-Industrial Platform统一前端第一批开发实施方案.md`

**Interfaces:**
- Consumes: the Phase 1 runtime entry points and future Identity contract shape.
- Produces: dispatch tasks `TASK-FE-001` through `TASK-FE-008` for the Vue foundation, API/error layer, auth boundary, layouts, pages, tests, and runtime integration.

- [x] Define exact first-batch screens and explicitly deferred business features.
- [x] Define MockAuthGateway/HttpAuthGateway replacement boundary and verification evidence.

### Task 3: Align global implementation order

**Files:**
- Modify: `docs/implementation/README.md`
- Modify: `docs/implementation/01-Industrial Platform开发启动实施方案.md`
- Modify: `docs/blueprint/09-MES MVP第一阶段开发TodoList.md`

**Interfaces:**
- Consumes: tasks BASE and FE plus `CLAUDE.md` progress.
- Produces: one current sequence from completed BuildingBlocks to vertical business delivery.

- [x] Register 02A/02B, mark BuildingBlocks complete, and publish the new active order.
- [x] Replace the obsolete phase order and Sprint 0/1 descriptions with current baseline stages and canonical links.

### Task 4: Align service prerequisites and vertical page delivery

**Files:**
- Modify: `docs/implementation/03-Industrial Platform Identity Service开发实施方案.md`
- Modify: `docs/implementation/04-Industrial Platform ReferenceData Service开发实施方案.md`
- Modify: `docs/implementation/05-Industrial Platform MasterData Service开发实施方案.md`
- Modify: `docs/implementation/06-Industrial Platform OperationalData Service开发实施方案.md`

**Interfaces:**
- Consumes: completed frontend shell, Identity login, and preceding service contracts.
- Produces: explicit frontend page and E2E obligations for MasterData and OperationalData.

- [x] Add phase prerequisites and page-delivery constraints without changing domain ownership.
- [x] Add completion criteria for corresponding pages and critical workflow integration.

### Task 5: Verify and commit

**Files:**
- Verify: all documentation modified by this plan.

- [x] Confirm BASE-001..006 and FE-001..008 are complete, unique task headings.
- [x] Confirm old `BuildingBlocks → Identity` direct active sequence is replaced in implementation governance.
- [x] Scan for placeholders, run `git diff --check -- docs`, inspect scope, and commit only task documents.
