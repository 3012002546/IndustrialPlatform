# Document Governance and Task Dispatch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `IndustrialPlatform/docs` the single maintained documentation source, standardize all designed time values on `DateTimeOffset`, and define the current task as a blueprint/TODO dispatch coordinator rather than a coding task.

**Architecture:** Keep blueprint decisions in `docs/blueprint` and dispatch-ready work in `docs/implementation`. Apply global rules in both indexes, then align concrete examples and the two collaboration/TODO documents. Delete only the two explicitly retired outer source directories after confirming their repository copies exist.

**Tech Stack:** Markdown, Git read-only verification, ripgrep, PowerShell.

## Global Constraints

- Do not modify `CLAUDE.md`.
- Preserve all unrelated user and collaborator changes in the dirty working tree.
- Use `DateTimeOffset` / `DateTimeOffset?` for .NET time values and `DateTimeOffset.UtcNow` for current UTC time.
- Use PostgreSQL `timestamp with time zone` (`timestamptz`) for persisted instants.
- This task maintains design and dispatches TODOs; actual code implementation occurs in other tasks unless the user explicitly changes scope.

---

### Task 1: Establish documentation governance

**Files:**
- Modify: `docs/blueprint/README.md`
- Modify: `docs/implementation/README.md`

- [x] State that `IndustrialPlatform/docs` is the single maintained source.
- [x] Add the shared time-type rules to both indexes.
- [x] Add the design/TODO/dispatch-only collaboration boundary and result feedback loop.

### Task 2: Align blueprint time examples

**Files:**
- Modify: `docs/blueprint/08-RabbitMQ事件总线设计规范.md`
- Modify: `docs/blueprint/07-PostgreSQL数据库规范及分库设计.md`
- Modify: `docs/blueprint/13-Identity Service详细设计.md`
- Modify: `docs/blueprint/15-WorkOrder Service详细设计.md`
- Modify: `docs/blueprint/16-Weighting Service详细设计.md`
- Modify: `docs/blueprint/17-IoT Collector Service详细设计.md`
- Modify: `docs/blueprint/18-Trace Service详细设计.md`
- Modify: `docs/blueprint/19-Batch Record Service详细设计.md`
- Modify: `docs/blueprint/21-低代码配置平台设计.md`
- Modify: `docs/blueprint/22-工业数据分析平台设计.md`
- Modify: `docs/blueprint/23-多租户SaaS架构设计.md`
- Modify: `docs/blueprint/24-工业AI助手设计.md`
- Modify: `docs/blueprint/26-Industrial Platform数据库最终模型.md`
- Modify: `docs/implementation/02-Industrial Platform BuildingBlocks基础组件开发实施方案.md`

- [x] Replace concrete `DateTime` and nullable `DateTime?` declarations with `DateTimeOffset` and `DateTimeOffset?`.
- [x] Replace SQL `timestamp`, `timestamp without time zone`, and SQL Server-specific `datetimeoffset` database wording with PostgreSQL `timestamp with time zone` (`timestamptz`).

### Task 3: Define dispatch workflow

**Files:**
- Modify: `docs/blueprint/09-MES MVP第一阶段开发TodoList.md`
- Modify: `docs/blueprint/10-Codex协作开发规范.md`
- Modify: `docs/implementation/01-Industrial Platform开发启动实施方案.md`

- [x] Add the current task boundary: blueprint maintenance, TODO decomposition, dispatch, and result reconciliation only.
- [x] Define the shared TODO lifecycle and dispatch-ready fields.
- [x] State that coding and testing execution belongs to separate development tasks unless explicitly authorized here.

### Task 4: Retire duplicate outer sources

**Directories:**
- Delete: `D:\Code\Industrial Platform\个人MES平台开发设计`
- Delete: `D:\Code\Industrial Platform\开发实施`

- [x] Confirm `docs/blueprint` and `docs/implementation` contain the corresponding maintained documents.
- [x] Delete the two exact outer directories authorized by the user.

### Task 5: Verify consistency

**Checks:**

- [x] Search maintained Markdown for concrete `DateTime` declarations; expected result is none.
- [x] Confirm all intended `DateTimeOffset` rules and PostgreSQL mapping are present.
- [x] Confirm `CLAUDE.md` has no diff caused by this task.
- [x] Confirm the two outer directories no longer exist.
- [x] Review Git diff only for files within this plan and report pre-existing unrelated changes separately.
