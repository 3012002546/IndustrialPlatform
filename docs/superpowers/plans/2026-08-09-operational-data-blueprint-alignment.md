# OperationalData Blueprint Alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add OperationalData as one independent microservice, move runtime inventory/lot/document responsibilities into it, retire the redundant two-year roadmap, and align the blueprint plus dispatch TODOs.

**Architecture:** OperationalData is externally one microservice and internally a modular monolith with Inventory, Lots, Documents, WarehouseOperations, and WmsIntegration modules. MasterData owns stable definitions; OperationalData owns inventory facts and warehouse documents; WorkOrder requests operations; Trace builds genealogy projections; BatchRecord preserves production execution history.

**Tech Stack:** Markdown architecture documents, .NET type vocabulary (`DateTimeOffset`), PostgreSQL (`timestamptz`), RabbitMQ integration events, ripgrep and Git diff checks.

## Global Constraints

- Modify documentation only; do not modify application code, project files, tests, or `CLAUDE.md`.
- Preserve all unrelated and pre-existing changes in the shared dirty `develop` working tree.
- Do not commit, merge, push, or create a pull request in this task.
- Keep OperationalData as one deployable microservice; do not split it into Inventory/Lot/Document services.
- A warehouse has exactly one inventory authority: `Internal` or `ExternalWms`.
- Use `DateTimeOffset` for .NET time values and PostgreSQL `timestamptz` for persisted instants.
- Keep existing document numbers stable. Delete document 05, retain the numbering gap, and insert `14A` between MasterData and WorkOrder.

---

### Task 1: Retire the redundant roadmap and update the documentation index

**Files:**
- Delete: `docs/blueprint/05-个人MES平台两年开发路线.md`
- Modify: `docs/blueprint/README.md`

**Produces:** A single index without the duplicate roadmap and with direct prerequisites for documents 06 and 09.

- [x] Remove the 05 index row without renumbering 06–31.
- [x] Change document 06 prerequisite from `01, 05` to `01`.
- [x] Change document 09 prerequisite from `01, 05` to `01, 03, 14, 14A`.
- [x] Add `14A-OperationalData Service详细设计.md` after MasterData and before WorkOrder.

### Task 2: Create the OperationalData detailed blueprint

**Files:**
- Create: `docs/blueprint/14A-OperationalData Service详细设计.md`

**Consumes:** `docs/superpowers/specs/2026-08-09-operational-data-domain-design.md`.

**Produces:** The normative service boundary for all later blueprint updates.

- [x] Define service purpose, dependency order, module structure, and Clean Architecture project layout.
- [x] Define InventoryBalance, InventoryLot, InventoryDocument, StockTransaction, and StockReservation.
- [x] Define Receipt, MaterialIssue, MaterialReturn, ProductionReceipt, Transfer, Stocktake, and Adjustment documents with posting rules.
- [x] Define `Internal` and `ExternalWms` authority modes, API capabilities, events, idempotency, Outbox/Inbox, failure behavior, and validation strategy.
- [x] Define explicit boundaries with MasterData, WorkOrder, Weighting, Trace, and BatchRecord.

### Task 3: Align the core solution and service catalog

**Files:**
- Modify: `docs/blueprint/01-Industrial Platform 总体架构设计 V1.0.md`
- Modify: `docs/blueprint/03-MES领域模型DDD设计.md`
- Modify: `docs/blueprint/04-Vue3 PCPDAMobile 三端统一架构设计.md`
- Modify: `docs/blueprint/06-Industrial Platform 微服务解决方案目录设计.md`
- Modify: `docs/blueprint/11-Industrial Platform代码初始化设计.md`
- Modify: `docs/blueprint/12-.NET10 Clean Architecture模板设计.md`
- Modify: `docs/blueprint/13-Identity Service详细设计.md`
- Modify: `docs/blueprint/21-低代码配置平台设计.md`
- Modify: `docs/blueprint/22-工业数据分析平台设计.md`
- Modify: `docs/blueprint/23-多租户SaaS架构设计.md`
- Modify: `docs/blueprint/24-工业AI助手设计.md`
- Modify: `docs/implementation/01-Industrial Platform开发启动实施方案.md`
- Modify: `docs/implementation/02-Industrial Platform BuildingBlocks基础组件开发实施方案.md`
- Modify: `docs/implementation/03-Industrial Platform Identity Service开发实施方案.md`
- Modify: `docs/implementation/04-Industrial Platform ReferenceData Service开发实施方案.md`

**Produces:** One consistent core solution order:

```text
BuildingBlocks → Identity → ReferenceData → MasterData → OperationalData → WorkOrder → Weighting → IoTCollector → Trace → BatchRecord
```

- [x] Replace Material.Service and MaterialRuntime.Service runtime responsibilities with OperationalData.
- [x] Add OperationalData to solution, service, test, and phase structures immediately after MasterData.
- [x] Keep material and BOM definitions in MasterData.
- [x] Update frontend routing labels that previously targeted Material Service to target OperationalData for runtime inventory operations.

### Task 4: Align bounded-context responsibilities

**Files:**
- Modify: `docs/blueprint/14-MasterData Service详细设计.md`
- Modify: `docs/blueprint/15-WorkOrder Service详细设计.md`
- Modify: `docs/blueprint/16-Weighting Service详细设计.md`
- Modify: `docs/blueprint/18-Trace Service详细设计.md`
- Modify: `docs/blueprint/19-Batch Record Service详细设计.md`

**Produces:** Non-overlapping ownership of definitions, inventory facts, production requests, trace projections, and production batch records.

- [x] State that MasterData owns Warehouse/Location definitions but not inventory instances or documents.
- [x] Make WorkOrder request reservations, issues, returns, and production receipts through OperationalData.
- [x] Make Weighting consume inventory-lot context and publish actual material-use facts without owning inventory.
- [x] Make Trace consume OperationalData events and remain a genealogy projection, not an inventory authority.
- [x] Keep InventoryLot distinct from BatchRecord and add OperationalData to the BatchRecord source/boundary table.

### Task 5: Align persistence, events, API, deployment, and testing

**Files:**
- Modify: `docs/blueprint/07-PostgreSQL数据库规范及分库设计.md`
- Modify: `docs/blueprint/08-RabbitMQ事件总线设计规范.md`
- Modify: `docs/blueprint/20-Industrial Platform部署架构设计.md`
- Modify: `docs/blueprint/25-Industrial Platform完整技术白皮书.md`
- Modify: `docs/blueprint/26-Industrial Platform数据库最终模型.md`
- Modify: `docs/blueprint/27-Industrial Platform API规范.md`
- Modify: `docs/blueprint/29-Industrial Platform自动化测试体系.md`

**Produces:** `operationaldata_db`, stable events, `/api/operational-data` API surface, one deployable service, and explicit test coverage.

- [x] Add OperationalData database ownership and normative inventory/document table groups using `timestamptz`.
- [x] Replace Material Service event ownership with OperationalData and add receipt/issue/return/production-receipt/lot events.
- [x] Add OperationalData to deployment, whitepaper, and platform service maps.
- [x] Add APIs for inventory queries, reservations, documents, posting, reversal, and WMS callbacks.
- [x] Add domain, persistence, contract, concurrency, idempotency, and dual-mode tests.

### Task 6: Rewrite the MVP dispatch TODO for OperationalData

**Files:**
- Modify: `docs/blueprint/09-MES MVP第一阶段开发TodoList.md`

**Produces:** A dispatch-ready OperationalData stage and task sequence that replaces the old Material service sprint.

- [x] Replace `Material.Service` with `OperationalData.Service` in MVP scope.
- [x] Rename Sprint 3 to OperationalData and include inventory lot, balance, reservation, receipt, issue, return, production receipt, transfer, stocktake, adjustment, and WMS authority mode.
- [x] Define ordered task IDs `TASK-OD-001` through `TASK-OD-009`, each with dependencies, allowed scope, output, verification evidence, and result writeback.
- [x] Update later WorkOrder, Weighting, Trace, integration-test, and final-deliverable sections to depend on OperationalData.

### Task 7: Verify the complete migration

**Checks:**

- [x] Confirm the 05 file and index entry are absent while files 06–31 retain their names.
- [x] Confirm `14A` exists and the README places it between 14 and 15.
- [x] Search maintained docs for `MaterialRuntime.Service`, `Material Service`, and `Material.Service`; any remaining reference must explicitly describe retired terminology.
- [x] Search core structure documents for the exact OperationalData placement after MasterData.
- [x] Confirm every inventory authority statement enforces one authority per warehouse.
- [x] Confirm concrete time types remain `DateTimeOffset`/`timestamptz`.
- [x] Run `git diff --check -- docs` and inspect the documentation-only diff.
- [x] Confirm `CLAUDE.md` and all code paths are untouched by this task.
