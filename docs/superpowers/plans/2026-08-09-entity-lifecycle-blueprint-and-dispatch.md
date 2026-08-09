# Entity Lifecycle Blueprint and Dispatch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Align the architecture blueprints and dispatch TODOs with the approved Entity lifecycle, dual concurrency, soft-delete, and PostgreSQL index design without modifying code.

**Architecture:** SharedKernel defines database-agnostic entity semantics; Infrastructure implements SqlSugar query filtering and atomic concurrency; service migrations own business and optional time indexes. The existing BuildingBlocks foundation remains complete except for one newly approved follow-up task that must finish before later runnable-baseline tasks continue.

**Tech Stack:** Markdown, .NET 10 type vocabulary, SqlSugar repository contracts, PostgreSQL B-tree/partial/BRIN index design, xUnit/Testcontainers verification vocabulary.

## Global Constraints

- Modify documentation only; do not modify `src/`, `tests/`, project files, migrations, Docker, deployment, or `CLAUDE.md`.
- Preserve the external edits already present in `docs/implementation/02A-Industrial Platform可运行基线开发实施方案.md` and do not stage that file.
- Use the approved specification at `docs/superpowers/specs/2026-08-09-entity-lifecycle-concurrency-soft-delete-design.md` as the normative source.
- Keep `IsDeleted` as `bool`; do not require `(Id, IsDeleted)` or `IsDeleted` indexes.
- Keep SharedKernel independent from SqlSugar and PostgreSQL.

---

### Task 1: Align Entity and repository architecture

**Files:**
- Modify: `docs/blueprint/12-.NET10 Clean Architecture模板设计.md`

**Interfaces:**
- Consumes: approved entity lifecycle specification.
- Produces: exact property names, state behavior, repository signatures, and concurrency failure semantics.

- [x] Replace the obsolete generic Entity snippet with the approved non-generic Guid entity model.
- [x] Document `EnsureCanModify`, lifecycle methods, dual-version atomic update, soft delete, restore, and dependency boundaries.

### Task 2: Align database and testing blueprints

**Files:**
- Modify: `docs/blueprint/07-PostgreSQL数据库规范及分库设计.md`
- Modify: `docs/blueprint/26-Industrial Platform数据库最终模型.md`
- Modify: `docs/blueprint/29-Industrial Platform自动化测试体系.md`

**Interfaces:**
- Consumes: approved entity fields and PostgreSQL index policy.
- Produces: normative column types, query filters, atomic SQL predicates, partial unique indexes, optional time indexes, and required tests.

- [x] Replace obsolete audit names and generic index rules.
- [x] Add entity, repository, concurrency, soft-delete, and index verification coverage.

### Task 3: Add one dispatch-ready BuildingBlocks follow-up

**Files:**
- Modify: `docs/implementation/02-Industrial Platform BuildingBlocks基础组件开发实施方案.md`
- Modify: `docs/implementation/README.md`
- Modify: `docs/implementation/01-Industrial Platform开发启动实施方案.md`
- Modify: `docs/blueprint/09-MES MVP第一阶段开发TodoList.md`

**Interfaces:**
- Consumes: aligned blueprints from Tasks 1 and 2.
- Produces: `TASK-BB-010`, a bounded documentation-only dispatch definition for a separate code implementation task.

- [x] Mark existing BuildingBlocks tasks complete and add exact follow-up dependencies, allowed code scope, tests, evidence, writeback, and suggested commit.
- [x] Put `TASK-BB-010` before the remaining runnable-baseline tasks without reopening completed `TASK-BASE-001`.

### Task 4: Verify and commit documentation

**Files:**
- Verify: only the documents listed above and this plan.

- [x] Confirm approved field names and old property names are absent from normative Entity/audit sections.
- [x] Confirm `TASK-BB-010` has all dispatch fields and occurs once in the canonical implementation document.
- [x] Confirm no code path changed, run `git diff --check -- docs`, inspect scope, and commit only this task's documentation.
