# MasterData and OperationalData Development TODOs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create canonical, dispatch-ready development TODO documents for MasterData and OperationalData and remove duplicated task bodies from the MVP blueprint.

**Architecture:** Blueprint documents remain the normative domain design, while `docs/implementation` becomes the single source for task state, dependencies, verification evidence, and result writeback. MasterData owns stable manufacturing definitions; OperationalData consumes those definitions and owns runtime inventory facts and warehouse operations.

**Tech Stack:** Markdown, .NET 10 project conventions, PostgreSQL, RabbitMQ, xUnit, `dotnet` CLI, ripgrep, Git.

## Global Constraints

- Modify documentation only.
- Preserve unrelated untracked files and existing user changes.
- Use `DateTimeOffset` for .NET instants and `timestamptz` for PostgreSQL instants.
- Keep MasterData and OperationalData ownership non-overlapping.
- A warehouse has exactly one inventory authority: `Internal` or `ExternalWms`.

---

### Task 1: Publish canonical MasterData TODOs

**Files:**
- Create: `docs/implementation/05-Industrial Platform MasterData Service开发实施方案.md`

**Interfaces:**
- Consumes: MasterData blueprint and completed BuildingBlocks/Identity/ReferenceData foundations.
- Produces: `TASK-MD-001` through `TASK-MD-010` with explicit dependencies and verification evidence.

- [x] Write service-wide constraints, dependency graph, state tracking, and ten bounded development tasks.
- [x] Verify every task contains scope, output, verification, writeback, and commit guidance.

### Task 2: Publish canonical OperationalData TODOs

**Files:**
- Create: `docs/implementation/06-Industrial Platform OperationalData Service开发实施方案.md`

**Interfaces:**
- Consumes: OperationalData blueprint and stable outputs from MasterData tasks.
- Produces: `TASK-OD-001` through `TASK-OD-009` with exact MasterData prerequisites.

- [x] Move the approved OD task chain into its canonical implementation document.
- [x] Add global constraints, verification commands, result tracking, and completion criteria.

### Task 3: Merge both task sets into documentation governance

**Files:**
- Modify: `docs/implementation/README.md`
- Modify: `docs/blueprint/09-MES MVP第一阶段开发TodoList.md`

**Interfaces:**
- Consumes: Both canonical implementation documents.
- Produces: One index and blueprint summaries without duplicated task bodies.

- [x] Register documents 05 and 06 in dependency order.
- [x] Replace Sprint 2 and Sprint 3 task bodies with task-number summaries and canonical links.

### Task 4: Verify and commit

**Files:**
- Verify: all modified documentation.

- [x] Confirm MD-001..010 and OD-001..009 each occur once in canonical implementation files.
- [x] Scan for placeholders and broken task dependencies.
- [x] Run `git diff --check -- docs` and inspect the docs-only diff.
- [ ] Commit only the documentation files created or modified by this task.
