# Configurable Database Topology Documentation Alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Update the SystemData implementation plan and all authoritative blueprint/implementation contracts so Development can use one configured physical database while Test, Staging, and Production remain per-service.

**Architecture:** Treat each service database name as a stable logical identity and resolve it through one `DatabaseTopology` contract to either a shared Development target or a per-service physical database. SystemData remains the orchestration authority and highest-priority PF-02 vertical slice; documentation must describe registration, plan/provision/migrate, locking, drift, and readiness with the same topology semantics.

**Tech Stack:** Markdown architecture documents, .NET Configuration JSON contracts, PostgreSQL 18, SQLite, SqlSugar, SystemData DatabaseOrchestration.

## Global Constraints

- `Development` defaults to `Shared` and may explicitly switch to `PerService`.
- `Test`, `Staging`, and `Production` only allow `PerService`; `Shared` must fail validation.
- PostgreSQL and SQLite use the same topology semantics.
- Shared physical storage never weakens service ownership: table prefixes, migration artifacts, migration ledgers, repositories, APIs, and events remain service-specific.
- SystemData DatabaseOrchestration is the highest-priority implementation scope and the only authority that resolves and orchestrates remote PostgreSQL targets.
- SystemData itself remains the only infrastructure bootstrap exception; no business API receives administrator credentials or creates databases.
- No `EnsureCreated`, unversioned Code First DDL, arbitrary SQL/path/credential API, or silent remote-to-SQLite fallback.
- Preserve all unrelated working-tree changes. In particular, `docs/implementation/05-Industrial Platform SystemData开发实施方案.md` is currently untracked user work; edit it in place and never replace, delete, reset, or regenerate the whole file.
- Do not rewrite historical test evidence as if it were newly verified.

---

### Task 1: Make blueprints 07 and 33 the authoritative topology contract

**Files:**
- Modify: `docs/blueprint/33-Industrial Platform SystemData数据库编排与环境引导.md:26`
- Modify: `docs/blueprint/33-Industrial Platform SystemData数据库编排与环境引导.md:81`
- Modify: `docs/blueprint/33-Industrial Platform SystemData数据库编排与环境引导.md:99`
- Modify: `docs/blueprint/07-PostgreSQL数据库规范及分库设计.md:66`
- Modify: `docs/blueprint/07-PostgreSQL数据库规范及分库设计.md:187`
- Modify: `docs/blueprint/07-PostgreSQL数据库规范及分库设计.md:1067`

**Interfaces:**
- Consumes: approved design `docs/superpowers/specs/2026-08-11-configurable-database-topology-design.md`.
- Produces: canonical `DatabaseTopology` vocabulary and SystemData logical-to-physical resolution rules consumed by every later task.

- [ ] **Step 1: Record the current contradictory phrases**

Run:

```powershell
rg -n "各服务可以继续使用自己的 SQLite|Development/测试可以配置自动 provision|DatabaseName.*目标数据库|identity_db|masterdata_db|operationaldata_db" "docs/blueprint/33-Industrial Platform SystemData数据库编排与环境引导.md" "docs/blueprint/07-PostgreSQL数据库规范及分库设计.md"
```

Expected: matches show that `DatabaseName` is treated as a physical target and that local SQLite is always per-service.

- [ ] **Step 2: Extend blueprint 33 registration and policy contracts**

Add the following normative concepts to sections 3, 4, and 7:

```markdown
`DatabaseName` is the stable logical database identity declared by the service manifest.
SystemData resolves the physical target from the trusted environment `DatabaseTopology`; callers cannot submit a physical database name or connection string.

DatabaseTopology:
- Mode: Shared | PerService
- SharedDatabaseName / SharedSqliteFile
- ServiceDatabases:{ServiceKey}

The normalized orchestration target contains EnvironmentName, Mode, ServiceKey,
Provider, LogicalDatabaseName, PhysicalDatabaseName and IsSharedPhysicalDatabase.
```

Change the environment matrix to state exactly:

```markdown
| Development | Default `Shared`; may explicitly use `PerService`; automatic plan/apply remains policy-controlled |
| Test | `PerService` only; may allow trusted automatic plan/apply |
| Staging | `PerService` only; approval and backup follow environment policy |
| Production | `PerService` only; mandatory plan, approval, verified backup, apply and verify |
```

- [ ] **Step 3: Define shared-target orchestration in blueprint 33**

Add rules to sections 8–10:

```markdown
In Shared mode, SystemData provisions the physical database once, but executes and records each service migration independently. Migration ledgers remain uniquely named per service. DDL operations targeting the same physical database use a physical-database advisory lock and execute serially. Readiness is evaluated per ServiceKey, logical identity, physical target, artifact checksum, and desired version.
```

Replace the local fallback rule with shared SQLite as the Development default and explicit `PerService` as the opt-in validation mode. State that changing topology does not copy data; populated targets report drift and require an explicit migration/import procedure.

- [ ] **Step 4: Reframe blueprint 07 database lists as logical/production topology**

Before the cluster diagram and database list, add:

```markdown
The following names are stable logical database identities and the Test/Staging/Production physical topology. Development defaults to one physical database named `industrial_platform_dev`; services retain the listed logical identity, table prefix, migration artifact, and migration ledger inside that shared target.
```

Update section 18 so SystemData resolves topology, provisions a shared Development database once, serializes same-target DDL, and performs per-service provisioning in `PerService` environments.

- [ ] **Step 5: Verify the two authoritative blueprints**

Run:

```powershell
rg -n "DatabaseTopology|LogicalDatabaseName|PhysicalDatabaseName|Shared|PerService|industrial_platform_dev|advisory lock|NotReady" "docs/blueprint/33-Industrial Platform SystemData数据库编排与环境引导.md" "docs/blueprint/07-PostgreSQL数据库规范及分库设计.md"
git diff --check -- "docs/blueprint/33-Industrial Platform SystemData数据库编排与环境引导.md" "docs/blueprint/07-PostgreSQL数据库规范及分库设计.md"
```

Expected: both files contain all canonical terms and `git diff --check` exits 0.

- [ ] **Step 6: Commit the authoritative blueprint update**

```powershell
git add -- "docs/blueprint/33-Industrial Platform SystemData数据库编排与环境引导.md" "docs/blueprint/07-PostgreSQL数据库规范及分库设计.md"
git commit -m "docs: define configurable database topology"
```

---

### Task 2: Update implementation 05 as the highest-priority SystemData vertical slice

**Files:**
- Modify: `docs/implementation/05-Industrial Platform SystemData开发实施方案.md:154`
- Modify: `docs/implementation/05-Industrial Platform SystemData开发实施方案.md:389`
- Modify: `docs/implementation/05-Industrial Platform SystemData开发实施方案.md:819`
- Modify: `docs/implementation/05-Industrial Platform SystemData开发实施方案.md:878`
- Modify: `docs/implementation/05-Industrial Platform SystemData开发实施方案.md:1073`
- Modify: `docs/implementation/05-Industrial Platform SystemData开发实施方案.md:1306`
- Modify: `docs/implementation/05-Industrial Platform SystemData开发实施方案.md:1443`
- Modify: `docs/implementation/05-Industrial Platform SystemData开发实施方案.md:1491`
- Modify: `docs/implementation/05-Industrial Platform SystemData开发实施方案.md:1777`

**Interfaces:**
- Consumes: Task 1 topology terminology and environment matrix.
- Produces: dispatch-ready PF-02 task cards that implement topology before administrative SystemData features.

- [ ] **Step 1: Preserve and inspect the user-owned implementation document**

Run:

```powershell
git status --short -- "docs/implementation/05-Industrial Platform SystemData开发实施方案.md"
rg -n "^## 7\.1|^## 8\.1|^## 9\.2|^## 12\.|^## TASK-SD-00[1-4]|^# 15|各自 SQLite|DatabaseName|advisory lock" "docs/implementation/05-Industrial Platform SystemData开发实施方案.md"
```

Expected: the file remains present; its current untracked/modified status is recorded before edits.

- [ ] **Step 2: Add the explicit topology decision and normalized target model**

In sections 2.3 and 7.1 define:

```markdown
DatabaseTopologyOptions
- Mode: Shared | PerService
- SharedDatabaseName
- SharedSqliteFile
- ServiceDatabases: IReadOnlyDictionary<string, string>

ResolvedDatabaseTarget
- EnvironmentName
- Mode
- ServiceKey
- Provider
- LogicalDatabaseName
- PhysicalDatabaseName
- IsSharedPhysicalDatabase
```

State that SystemData reads the trusted environment topology, while manifests keep `DatabaseName` as the logical identity. Reject caller-supplied physical targets, unknown modes, invalid names, missing mappings, and `Shared` outside Development.

- [ ] **Step 3: Rewrite sections 7.1.1–7.1.6 around topology-aware orchestration**

Make these behaviors explicit:

```markdown
- Shared Development bootstrap creates `industrial_platform_dev` once; SystemData runs `system_data_schema_migrations` there.
- PerService bootstrap creates `systemdata_db` for SystemData as the sole infrastructure exception.
- Registration stores logical identity, resolved physical identity, topology mode, manifest checksum, artifact checksum, and desired version.
- Shared provision deduplicates by physical target; migration and readiness remain per service.
- The advisory-lock identity is derived from EnvironmentNId + Provider + PhysicalDatabaseName.
- Topology changes with existing data produce drift and never trigger implicit copy, rename, merge, or split.
```

Replace the current “各服务使用各自 SQLite 文件” rule with shared Development SQLite by default and explicit `PerService` validation mode.

- [ ] **Step 4: Align persistence, APIs, errors, UI, observability, and security**

Update sections 8–11 so:

- `system_data_database_registration` distinguishes logical and physical identity plus topology mode/revision.
- environment policy stores allowed/default topology semantics without accepting credentials.
- plan/operation/readiness DTOs expose safe logical/physical identity metadata but never a connection string.
- stable validation errors include unsupported topology, missing shared target, missing service mapping, forbidden shared environment, and topology drift.
- the database orchestration page displays topology mode and logical-to-physical mapping.
- metrics keep low-cardinality labels; database names remain excluded from metric labels.

- [ ] **Step 5: Expand the test matrix and E2E gates**

Add exact acceptance cases:

```markdown
1. Development + Shared + SQLite: services use one file and unique ledgers.
2. Development + Shared + PostgreSQL: one provision, independent migrations and readiness.
3. Development + PerService: independent targets.
4. Test/Staging/Production + Shared: validation failure.
5. Missing service mapping or invalid target: startup/registration failure.
6. Concurrent migrations in one shared target: serialized DDL and independent results.
7. One migration failure: only that service remains NotReady.
8. Logical/physical/artifact/version mismatch: drift and apply rejection.
9. No credential or full connection-string leakage.
10. No EnsureCreated or business-API database creation path.
```

- [ ] **Step 6: Make TASK-SD-001–004 the blocking priority chain**

Update the dependency graph and task cards without renumbering historical task IDs:

- `TASK-SD-001`: service skeleton, topology options/resolver contract, SystemData shared/per-service bootstrap.
- `TASK-SD-002`: registration, logical/physical persistence, topology-aware plan and validation.
- `TASK-SD-003`: provision deduplication, physical-target locking, per-service migration execution and drift.
- `TASK-SD-004`: reusable service handshake/readiness plus shared/per-service acceptance fixture.

State that `TASK-SD-005` and all administrative-domain work cannot start until `TASK-SD-001–004` pass their database orchestration gates.

- [ ] **Step 7: Verify implementation 05 consistency**

Run:

```powershell
rg -n "DatabaseTopologyOptions|ResolvedDatabaseTarget|LogicalDatabaseName|PhysicalDatabaseName|industrial_platform_dev|TASK-SD-001.*TASK-SD-004|Shared.*Development|PerService" "docs/implementation/05-Industrial Platform SystemData开发实施方案.md"
rg -n "各服务使用各自 SQLite|Development/Test.*创建.*非 SystemData.*数据库" "docs/implementation/05-Industrial Platform SystemData开发实施方案.md"
git diff --check -- "docs/implementation/05-Industrial Platform SystemData开发实施方案.md"
```

Expected: the first command shows every contract; the second returns no contradictory matches; diff check exits 0.

- [ ] **Step 8: Commit only the SystemData implementation document**

```powershell
git add -- "docs/implementation/05-Industrial Platform SystemData开发实施方案.md"
git commit -m "docs(systemdata): prioritize database topology orchestration"
```

---

### Task 3: Update governance, service template, and PF-02 priority

**Files:**
- Modify: `docs/implementation/TEMPLATE-开发实施方案.md:23`
- Modify: `docs/implementation/TEMPLATE-开发实施方案.md:119`
- Modify: `docs/implementation/README.md:9`
- Modify: `docs/blueprint/12-.NET10 Clean Architecture模板设计.md:759`
- Modify: `docs/blueprint/12-.NET10 Clean Architecture模板设计.md:974`
- Modify: `docs/blueprint/09-Industrial Platform开发总TodoList.md:214`
- Modify: `docs/blueprint/09-Industrial Platform开发总TodoList.md:457`

**Interfaces:**
- Consumes: Tasks 1–2 canonical fields and PF-02 blocking chain.
- Produces: mandatory topology checklist for every future service design.

- [ ] **Step 1: Extend the implementation template database checklist**

Require every service plan to list:

```markdown
ServiceKey / Provider / LogicalDatabaseName / table prefix / migration ledger
DatabaseTopology Mode / shared Development target / per-service target
resolved physical target validation / SystemData OperationId / readiness
same-physical-target migration lock / topology drift behavior
```

The template must say that the service never chooses its physical target independently.

- [ ] **Step 2: Update the clean-architecture service template**

Replace the single `DatabaseProvisioning.DatabaseName` concept with a manifest logical database name plus `DatabaseTopology`-resolved physical target. Add startup validation for forbidden shared environments and readiness identity checks.

- [ ] **Step 3: Raise the PF-02 orchestration priority in the total TodoList**

In PF-02, put the database orchestration vertical slice before administrative organization/navigation work. Change the completion gate so it proves both:

```markdown
- Development Shared: one PostgreSQL database, independent service migrations/readiness.
- PerService: an isolated test service database plus the Production approval/backup gate.
```

Update the tracking row only to reflect the real document state; do not claim implementation or test completion.

- [ ] **Step 4: Update the implementation index**

Describe blueprints 07 and 33 as the authoritative topology sources and implementation 05 as the PF-02 control-plane plan. Preserve existing ownership and collaboration boundaries.

- [ ] **Step 5: Verify and commit governance documents**

Run:

```powershell
rg -n "LogicalDatabaseName|DatabaseTopology|Shared|PerService|PhysicalDatabaseName|最高优先级|优先" "docs/implementation/TEMPLATE-开发实施方案.md" "docs/implementation/README.md" "docs/blueprint/12-.NET10 Clean Architecture模板设计.md" "docs/blueprint/09-Industrial Platform开发总TodoList.md"
git diff --check -- "docs/implementation/TEMPLATE-开发实施方案.md" "docs/implementation/README.md" "docs/blueprint/12-.NET10 Clean Architecture模板设计.md" "docs/blueprint/09-Industrial Platform开发总TodoList.md"
git add -- "docs/implementation/TEMPLATE-开发实施方案.md" "docs/implementation/README.md" "docs/blueprint/12-.NET10 Clean Architecture模板设计.md" "docs/blueprint/09-Industrial Platform开发总TodoList.md"
git commit -m "docs: require topology-aware service plans"
```

Expected: canonical terms appear in all four documents and the commit contains no unrelated files.

---

### Task 4: Align existing service plans and architecture summaries

**Files:**
- Modify: `docs/implementation/03-Industrial Platform Identity Service开发实施方案.md:71`
- Modify: `docs/implementation/03-Industrial Platform Identity Service开发实施方案.md:254`
- Modify: `docs/implementation/06-Industrial Platform ReferenceData Service开发实施方案.md:262`
- Modify: `docs/implementation/15-Industrial Platform MasterData Service开发实施方案.md:240`
- Modify: `docs/implementation/16-Industrial Platform OperationalData Service开发实施方案.md:22`
- Modify: `docs/blueprint/01-Industrial Platform 总体架构设计 V1.0.md:410`
- Modify: `docs/blueprint/20-Industrial Platform部署架构设计.md:541`
- Modify: `docs/blueprint/25-Industrial Platform完整技术白皮书.md:556`
- Modify: `docs/blueprint/26-Industrial Platform数据库最终模型.md:60`

**Interfaces:**
- Consumes: authoritative topology contract from Task 1 and template rules from Task 3.
- Produces: service-specific logical identities that no longer contradict shared Development storage.

- [ ] **Step 1: Update each current service implementation plan**

For Identity, ReferenceData, MasterData, and OperationalData, preserve the existing stable database name but label it `LogicalDatabaseName`. Add:

```markdown
Development defaults to the configured shared physical target `industrial_platform_dev` (or the shared SQLite file). Test, Staging, and Production resolve this logical identity to the service-specific physical database. Shared Development does not permit cross-service table access and does not merge migration ledgers.
```

Do not alter historical task evidence or completed migration names.

- [ ] **Step 2: Add topology qualifiers to architecture diagrams and summaries**

In blueprints 01, 20, 25, and 26, label per-service database diagrams as logical and Test/Staging/Production topology. Add the one-database Development topology once per document, referring detailed rules to blueprints 07 and 33 rather than duplicating the full contract.

- [ ] **Step 3: Verify no fixed-name statement is misread as all-environment physical topology**

Run:

```powershell
rg -n "数据库名固定|独立数据库|identity_db|referencedata_db|masterdata_db|operationaldata_db" "docs/implementation/03-Industrial Platform Identity Service开发实施方案.md" "docs/implementation/06-Industrial Platform ReferenceData Service开发实施方案.md" "docs/implementation/15-Industrial Platform MasterData Service开发实施方案.md" "docs/implementation/16-Industrial Platform OperationalData Service开发实施方案.md"
rg -n "logical|逻辑|Development|Shared|industrial_platform_dev|蓝图 07|蓝图 33" "docs/blueprint/01-Industrial Platform 总体架构设计 V1.0.md" "docs/blueprint/20-Industrial Platform部署架构设计.md" "docs/blueprint/25-Industrial Platform完整技术白皮书.md" "docs/blueprint/26-Industrial Platform数据库最终模型.md"
git diff --check -- docs/implementation docs/blueprint
```

Expected: fixed database names are paired with logical-identity language; architecture summaries point to the authoritative blueprints; diff check exits 0.

- [ ] **Step 4: Commit the service and architecture alignment**

```powershell
git add -- "docs/implementation/03-Industrial Platform Identity Service开发实施方案.md" "docs/implementation/06-Industrial Platform ReferenceData Service开发实施方案.md" "docs/implementation/15-Industrial Platform MasterData Service开发实施方案.md" "docs/implementation/16-Industrial Platform OperationalData Service开发实施方案.md" "docs/blueprint/01-Industrial Platform 总体架构设计 V1.0.md" "docs/blueprint/20-Industrial Platform部署架构设计.md" "docs/blueprint/25-Industrial Platform完整技术白皮书.md" "docs/blueprint/26-Industrial Platform数据库最终模型.md"
git commit -m "docs: align service database topology contracts"
```

---

### Task 5: Run the cross-document contract audit

**Files:**
- Verify: `docs/blueprint/**/*.md`
- Verify: `docs/implementation/**/*.md`
- Modify only if required: files changed in Tasks 1–4.

**Interfaces:**
- Consumes: all documentation changes from Tasks 1–4.
- Produces: evidence that the documentation set has one unambiguous database topology contract.

- [ ] **Step 1: Scan for placeholders and obsolete topology claims**

Run:

```powershell
rg -n "T[B]D|T[O]DO|适当处[理]|各服务可以继续使用自己的 SQLite|Development/测试可以配置自动 provision \+ migrate" docs/blueprint docs/implementation
```

Expected: no new placeholders; unrelated historical placeholder markers are reported but not edited. Obsolete topology sentences return no matches.

- [ ] **Step 2: Prove required contract coverage**

Run:

```powershell
rg -l "DatabaseTopology" docs/blueprint docs/implementation
rg -l "LogicalDatabaseName" docs/blueprint docs/implementation
rg -l "PhysicalDatabaseName" docs/blueprint docs/implementation
rg -n "Test.*PerService|Staging.*PerService|Production.*PerService|Shared.*启动失败|Shared.*validation" docs/blueprint docs/implementation
rg -n "migration ledger|迁移账本|advisory lock|NotReady|drift" "docs/blueprint/33-Industrial Platform SystemData数据库编排与环境引导.md" "docs/implementation/05-Industrial Platform SystemData开发实施方案.md"
```

Expected: authoritative and template documents contain all required terms; SystemData documents cover independent ledgers, locking, readiness, and drift.

- [ ] **Step 3: Check formatting and review the scoped diff**

Run:

```powershell
git diff --check
git status --short
git log -5 --oneline
```

Expected: `git diff --check` exits 0; status still preserves unrelated user changes; the recent commits correspond to Tasks 1–4.

- [ ] **Step 4: Commit audit-only corrections if needed**

If the audit required corrections, stage only the exact corrected documentation files and run:

```powershell
git commit -m "docs: reconcile database topology terminology"
```

If no corrections were needed, do not create an empty commit.

- [ ] **Step 5: Record handoff evidence**

Report the changed document list, commit IDs, audit commands and exit codes. Explicitly state that this documentation phase did not build, test, or implement runtime database behavior and that code execution must start with `TASK-SD-001` through `TASK-SD-004`.
