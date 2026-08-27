# 05-Industrial Platform SystemData开发实施方案

# Industrial Platform SystemData开发实施方案

> 当前里程碑范围：保留 `SystemData.Service` 已实现的拓扑、控制面、组织、岗位与任职范围，并按架构收敛规则将 Service Initialization Pipeline 定位为 Topology、Orchestration、Policy 与脱敏 Observation；各服务自己的初始化器负责 Migration、Seed、Bootstrap、Verify、Ledger 和本地 readiness。File、Notification、Audit、Scheduler、PlatformHealth 本阶段不设计或实现。

版本：V1.2

阶段：PF-02 SystemData；前置 Identity `TASK-ID-001～023` 与 PF-01 `TASK-PF01-001～007` 已完成。`TASK-SD-001～013` 已完成，`TASK-SD-011` 保留 runtime 降级前提；不自动进入 PF-03。

阶段管理状态：已完成；前端门禁和默认 UnifiedHost 云依赖真实管理 CRUD 已通过。完整证据见 `docs/evidence/PF-02.md` 与第 16 节；当前状态以 `docs/status/CURRENT.md` 为准。

模块或服务：

```text
SystemData.Service
└── SystemData 模块
```

Service Host 与内部模块：

```text
PF-02 创建 SystemData.Service
本阶段：SystemData
PF-04 后续加入：File / Notification / Audit
PF-07 后续加入：Scheduler / PlatformHealth
```

阶段不等于微服务。后续模块即使共用 `SystemData.Service`，也必须使用独立表前缀、公开契约、权限、迁移账本和测试，禁止跨模块直读 Repository 或数据表。

技术：

```text
.NET 10 WebAPI / Clean Architecture / DDD
SqlSugar / PostgreSQL 18 / Redis / RabbitMQ Outbox
Vue 3 + TypeScript + Pinia + Vue Router + Element Plus
Vitest + Playwright + xUnit
```

服务初始化与环境引导：

```text
SystemData：Topology + Orchestration + Policy + Observation
服务初始化器：Inspect → Plan → Apply(Migration + Seed + Bootstrap) → Verify
所有权：各服务自有 Schema、Migration、Seed、Bootstrap、Verify、Ledger 和本地 readiness
策略：Standard | Advanced；普通功能默认 Standard，高风险环境才启用审批/备份/签名/漂移恢复
禁止：独立 Migrator/Seeder Service、EnsureCreated、任意 SQL/路径/命令/凭据 API、SystemData 直写业务 Repository 或承载其他服务迁移实现
```

Identity 首个消费者案例（PF-00 已确认）：Identity 将既有 `SchemaMigration → SystemSeed → BootstrapAdmin → Verify` 改写为通用协议实例。权限目录和 `SYSTEM_ADMIN` 属于 `SystemBaseline`，租户安全关系属于 `TenantBaseline`，`ADMIN` 属于 `SecretBootstrap`。Identity 自有 `identity_schema_migrations`、`identity_seed_ledger`、初始化产物和 initializer；initializer 自行解析 Identity Secret Provider，只向 SystemData 回报脱敏 version/checksum/status/TraceId。SystemData 不接收、透传或保存 admin 密码、密码哈希、Token 或 Secret 值。具体临时密码交付、首次改密和恢复规则仍以 Identity 实施 03 的 29A.4 与 TASK-ID-019 为准，本方案不覆盖 PF-00 并行细则。

规格与蓝图依据：

- `docs/blueprint/01-Industrial Platform 总体架构设计 V1.0.md`
- `docs/blueprint/05-Industrial Platform平台基础功能与独立模块设计.md`
- `docs/blueprint/09-Industrial Platform开发总TodoList.md`
- `docs/blueprint/13-Identity Service详细设计.md`
- `docs/blueprint/23-多租户SaaS架构设计.md`
- `docs/blueprint/31-Industrial Platform权限体系与安全架构设计.md`
- `docs/blueprint/32-Industrial Platform Service Host与内部模块边界.md`
- `docs/blueprint/33-Industrial Platform SystemData数据库编排与环境引导.md`
- `docs/implementation/03-Industrial Platform Identity Service开发实施方案.md`
- `docs/implementation/04-Industrial Platform视觉主题与平台外壳开发实施方案.md`
- `docs/implementation/TEMPLATE-开发实施方案.md`

设计优先级：蓝图 33 V3.0 与架构收敛设计为当前最高优先级；现有数据库编排 API/Operation 保持兼容，但只能调用服务初始化器并保存脱敏 Observation。服务的 Migration、Seed、Bootstrap、Verify、Ledger 和 runtime readiness 不上收 SystemData。

---

# 1. 文档说明

## 1.1 文档目的

本文是 PF-02 SystemData 的开发详细设计、任务依赖、九字段任务卡和执行结果唯一维护源。本文供阶段管理任务、后续执行任务、Identity/PF-01 联合验收及 PF-03/PF-04 消费。

阶段管理任务只维护设计、派遣、跟踪和验收，不直接开发业务代码。除非用户在本任务后续明确要求，本方案中的任务卡不得自动派遣。

## 1.2 当前输入状态

截至 2026-08-14，实际仓库状态如下：

- 分支为 `develop`，本轮最终核对 HEAD 为 `61753dc`，远端仍为 `961cad4`。`961cad4` 已创建 SystemData 五层项目、Gateway `/systemdata`、拓扑、自迁移和控制面；`61753dc` 进一步提交 migration-only Runner、PG/SQLite adapter 和测试资产。
- `961cad4` 是代码面与测试资产存在的可验证证据；本设计轮没有重新 build/test，因此 TASK-SD-001～002 只能标记“待验收”，不能仅凭提交标记阶段完成。
- `61753dc` 的 Runner 阶段仍为 `Validate → Inspect → ProvisionDatabase → ProvisionRoles → Backup → Migrate → Verify`，尚无 RequiredSeed/SecretBootstrap；该提交是兼容实现基线，不等于通用初始化扩展完成。本轮不修改其源码或测试。
- PF-00 正在并行修改实施 03、蓝图 13/31、总 Todo 和实施索引；其写入本文的 Identity 种子协同结论已吸收为通用协议首个消费者，本任务不覆盖 PF-00 详细规则。
- PF-00 当前实施 03 WIP 仍含“admin 临时密码由 SystemData 内存透传一次”的旧句；该句与本轮已批准的“SystemData 不接收/透传 Secret 值”冲突，不能视为稳定契约，必须由 PF-00 会话改为 Identity 自有一次性领取/交付边界。本任务只登记冲突，不修改实施 03。
- Identity 历史 `TASK-ID-001～016` 与 PF-01 `TASK-PF01-001～007` 已由提交 `48c5374` 完成；Identity 补强 `TASK-ID-017～023` 仅设计确认、尚未开发。
- BuildingBlocks `ICurrentUser.UserNId : string?`、真实 `HttpAuthGateway`、PermissionGate 和 PF-01 平台壳已存在；本文旧有 Guid/Mock/PF-01 未实现阻塞描述不再作为当前事实。
- `ReferenceData.Service` 只有现有四层骨架；SystemData 不接管字典、系统参数、元数据、动态属性或编码规则。
- 蓝图 33 V3.0 已冻结控制面与执行面边界：SystemData 负责编排和 Observation，业务服务初始化器拥有 Migration、Seed、Bootstrap、Verify、Ledger 与本地 readiness。
- `docs/implementation/15-Industrial Platform MasterData Service开发实施方案.md` 当前已由 `a35ff32` 提交；无论其提交状态如何，PF-02 都不得修改、暂存、回退或重写该文件。

本轮只编写设计文档，没有执行构建、测试或环境联调。03、04、CLAUDE.md 中的测试数字均为历史输入，不是本轮证据。

## 1.3 当前依赖分级

| 级别 | 可依赖内容 | PF-02 行为 |
| --- | --- | --- |
| 当前已提交 | `961cad4` 的 SystemData 骨架、拓扑、自迁移、registration/plan/approval/backup/Operation 控制面和测试资产 | 可继续兼容扩展；因本轮未重跑，只能作为已实现/待验收基线 |
| 当前已提交 | `48c5374` 的 Identity/PF-01 稳定实现、字符串 UserNId 上下文、真实前端认证与平台壳 | 可作为现有契约；补强 TASK-ID-017～023 仍不得假定已实现 |
| 当前已提交 | `61753dc` 的 TASK-SD-003 migration-only Runner、PG/SQLite adapter、store/DI/config/tests | 作为兼容子流程；本轮未重跑，RequiredSeed/SecretBootstrap 扩展仍待派遣 |
| 后续稳定契约 | InitializationManifest/SeedSets、initializer adapter、SeedObservation、双账本 readiness 与 Identity bootstrap 细则 | 在 Contracts、端口和契约测试中冻结，真实实现/联合验收等待执行任务 |
| 不可依赖 | `61753dc` 尚未包含的 RequiredSeed/SecretBootstrap/双账本扩展，以及未来 Audit/File/Notification/Scheduler/PlatformHealth 业务实现 | 不得写成已完成或越界直读其 Repository/账本 |

## 1.4 执行前置

```text
PostgreSQL 18 基础设施最小引导（稳定逻辑 `systemdata_db`：Shared Development 物理创建 `industrial_platform_dev` 并运行 `system_data_schema_migrations`；PerService 物理创建 `systemdata_db`，以及最小角色授权）
                         → SystemData 本地 SchemaMigration + 最小 SystemBaseline 建立控制面
                         → Registration/Plan/Approval/Backup/Operation（961cad4 已实现，待验收）
                         → Runner 扩展 SchemaMigration/RequiredSeed/SecretBootstrap/Verify
                         → 其他服务/模块以 InitializationManifest、双账本和 NotReady 接入

BuildingBlocks / 可运行基线 / 统一前端第一批（历史已完成）
                         ↓
PF-02 后端隔离设计与实现任务
                         ↓
PF-00/PF-01 历史稳定契约（已实现）
PF-00 TASK-ID-017～023 补强契约（仅相关联合验收依赖）
                         ↓
PF-02 真实 Identity/PF-01 适配、页面与联合验收
                         ↓
PF-03 / PF-04 消费 SystemData 稳定契约
```

---

# 2. 定位、目标与职责边界

## 2.1 负责

- 通用服务初始化控制面：registration/query、拓扑解析、dry-run/plan、Standard/Advanced 策略、Operation、幂等和脱敏 Observation。
- 通过进程内或受信 HTTP 端口调用目标服务的 Inspect/Plan/Apply/Verify，不加载或解释目标服务的 Migration/Seed/Bootstrap 实现。
- 行政组织树：公司、部门、科室、班组。
- 岗位实例及其行政组织归属。
- Identity 用户与岗位之间的时间化任职关系和主任职。
- UI 资源注册、菜单草稿、发布快照、终端可见性和路由导航。
- 模块权限清单声明，以及与 Identity 权限目录的注册/核对边界。
- 功能定义、租户覆盖和紧急关闭策略。
- 服务目录、所有者、入口和声明式健康地址。
- 租户默认主题、允许配色/模式/密度范围。
- SystemData 自身本地追加审计、Outbox、缓存、迁移、健康与指标。

## 2.2 不负责

| 不属于 PF-02 | 权威归属 |
| --- | --- |
| 工厂、车间、产线、工作中心、生产资源位置 | MasterData |
| 字典、系统参数、元数据、动态属性、编码规则 | ReferenceData |
| 用户账号、密码、角色、权限分配、令牌、会话、SSO | Identity |
| 租户开通、停用、套餐、订阅、配额、计费 | 未来 Tenant/运营能力 |
| 业务服务 Schema、Migration、Seed、Bootstrap、Verify、Ledger、本地 readiness 和回退策略 | 各业务服务或独立持久化初始化单元自身 |
| 独立 Migrator/Seeder Service、任意 SQL 执行平台、Secret 管理产品 | 不创建；分别使用 SystemData 内部 Runner、签名初始化产物和服务自有 Secret Provider |
| 文件隔离扫描、通知收件箱、统一审计事实源 | PF-04 的 File/Notification/Audit 模块 |
| 任务调度和平台健康聚合 | PF-07 的 Scheduler/PlatformHealth 模块 |
| 物料、设备、库存或 MES 业务权限 | 对应业务模块 |
| 主题设计器、任意色板、用户跨设备偏好同步 | 不在 PF-02 首期 |

## 2.3 已确认领域决策

1. 行政组织采用统一的有类型树；首期类型固定为公司、部门、科室、班组。
2. 公司只能作为根；部门可位于公司/部门下，科室可位于部门/科室下，班组可位于部门/科室/班组下；禁止倒置和循环。
3. 同一租户允许多个根公司；前端可显示不落库的只读租户虚拟根。
4. 组织子树可在同一租户内跨根公司移动并保留 NId；跨租户移动禁止。
5. 岗位实例专属于一个组织；同名岗位可在不同组织分别存在，岗位不能直接跨组织移动。
6. 用户可有多条有效任职；存在有效任职时，每个租户同一时点必须且只能有一个主任职；未任职用户允许存在。
7. 任职使用有效期区间并保留历史；同一用户/岗位的有效区间不得重叠，不引入人事审批流。
8. 组织停用不隐式级联；存在有效下级组织、岗位或任职时拒绝停用。
9. 每个可操作菜单、页面或按钮资源最多绑定一个 PermissionNId；分组节点不绑定权限。
10. ResourceNId 归 SystemData，PermissionNId 归 Identity，两者分离并显式关联。
11. 模块声明版本化权限清单，Identity 幂等注册；SystemData 不直接写 Identity 数据库。
12. 菜单使用草稿与不可变发布快照，显式发布、原子切换并保留上一版本回滚。
13. SystemData 返回带 PermissionNId 的候选导航，前端与当前 AuthUser 权限求交集；目标 API 仍独立授权。
14. Service Initialization Pipeline 采用注册、拓扑解析、异步 Operation 和 Inspect/Plan/Apply/Verify 调用模型；业务服务拥有自己的 Schema、Migration、Seed、Bootstrap、Verify、Ledger 和本地 readiness。
15. 初始化策略固定为 Standard 与 Advanced；生产环境可强制 Advanced 的 `plan → 审批 → 备份证据 → apply → verify`，普通功能不无条件承担高级流程。
16. `systemdata_db` 始终是稳定逻辑身份；Shared Development 的基础设施只物理创建 `industrial_platform_dev` 并在其中运行 `system_data_schema_migrations`，PerService 才物理创建 `systemdata_db` 作为唯一引导例外。SystemData 使用自有迁移产物建立控制面；不得循环调用自身编排 API。
17. 不创建独立 Migrator/Seeder Service，不使用 `EnsureCreated`，不允许 API 提交任意 SQL、路径、命令、服务器地址或凭据。
18. `InitializationUnitKey` 标识服务级或独立持久化初始化单元；同宿主逻辑模块保持表前缀和领域边界，但不机械拆分 Migration、Outbox、Inbox 或 Ledger。
19. 四类种子固定为 SystemBaseline、TenantBaseline、EnvironmentSample、SecretBootstrap；普通业务创建用户的初始密码不属于种子。
20. SystemData 只传非敏感 Operation/目标/租户/期望版本上下文；服务 initializer 自行解析 Secret Provider，SystemData 不接收或透传 Secret 值。已初始化服务的 runtime readiness 只读取本地数据库事实，不依赖 SystemData 在线。
18. 数据库拓扑由受信任环境 `DatabaseTopologyOptions` 决定：`Mode`、`SharedDatabaseName`、`SharedSqliteFile`、`ServiceDatabases`；清单只保留稳定逻辑 `DatabaseName`，SystemData 将其解析为 `ResolvedDatabaseTarget(EnvironmentName, Mode, ServiceKey, Provider, LogicalDatabaseName, PhysicalDatabaseName, IsSharedPhysicalDatabase)`。
19. 拒绝调用方提供物理目标、未知 Mode、非法名称、缺少映射以及 Development 之外的 Shared；已有数据的拓扑变化是 drift，绝不隐式 copy、rename、merge 或 split。

---

# 3. 前后端及跨服务协作目标

纵向交付链：

```text
PostgreSQL 18 最小引导
    ↓
SystemData 自有迁移 + 数据库注册/Plan/Operation/Runner/NotReady
    ↓
服务初始化十三项门禁验收
    ↓
SystemData 其他领域与迁移
    ↓
组织/岗位/任职、菜单、开关、目录、主题应用用例
    ↓
版本化 Contracts + /systemdata/api/v1
    ↓
PF-01 外壳适配器与 PC 管理页面
    ↓
Identity 用户/权限契约适配
    ↓
契约测试 + PostgreSQL/Redis 集成 + Playwright E2E
    ↓
PF-02 阶段验收
```

协作原则：

- SystemData 只保存 `UserNId`、`PermissionNId` 等跨服务稳定标识和必要显示快照，不保存 Identity 数据库 Guid，不建跨库外键。
- 菜单是导航，不是授权；前端隐藏、终端可见性和功能开关均不能替代目标 API 的权限校验。
- PF-01 平台壳与真实 Identity 前端契约已由 `48c5374` 实现；PF-02 适配必须消费其公开端口，不复制或重构内部实现。
- Identity 不可用时不得新增或发布无法验证权限绑定的菜单，但现有已发布导航可以按降级规则继续服务。
- PF-04 Audit 稳定前，SystemData 在自身事务内写本地追加审计与 Outbox；后续通过公开事件接入统一 Audit，不直写 Audit 表。

---

# 4. 总体架构与数据流

```text
Vue Unified Frontend
  ├─ PF-01 Platform Shell
  ├─ SystemData Runtime Adapters
  └─ SystemData PC Admin Pages
              |
              | /systemdata/**
              v
YARP Gateway（剥离 /systemdata）
              |
              v
SystemData.Service / SystemData.Api
  ├─ SystemData.Application
  ├─ SystemData.Domain
  ├─ SystemData.Contracts（零项目引用）
  └─ SystemData.Infrastructure
       ├─ PostgreSQL 解析后的 SystemData 物理目标 / system_data_* 表
       ├─ Redis 版本化只读快照
       ├─ RabbitMQ Outbox
       └─ DatabaseOrchestration Runner（宿主内 BackgroundService）

PostgreSQL 18 基础设施最小引导
  → 解析逻辑 `systemdata_db` 的物理目标 + SystemData owner/migrator/runtime 角色
  → SystemData 自有迁移产物
  → DatabaseOrchestration 控制面
       ├─ 注册/查询 + dry-run/plan
       ├─ 审批/备份证据 + async apply
       └─ Operation 状态 + 目标服务 NotReady 门禁

公开协作：
Identity ← 权限清单注册/核对、用户目录查询、用户/权限变化契约
PF-01   ← 导航、功能开关、主题策略前端适配器
PF-03/04/07 ← 资源清单、服务目录、主题/导航与事件契约
```

## 4.1 权威来源

| 数据 | 权威来源 |
| --- | --- |
| 用户、角色、权限目录与分配 | Identity |
| 行政组织、岗位、任职 | SystemData |
| ResourceNId、菜单草稿与发布快照 | SystemData |
| PermissionNId 注册状态 | Identity；SystemData 保留最近验证投影 |
| 功能定义和租户覆盖 | SystemData |
| 服务目录声明 | SystemData；真实健康状态由 PF-07 PlatformHealth |
| 租户主题策略 | SystemData；用户显式偏好由 PF-01 本地状态 |
| 服务数据库注册、计划、执行状态和迁移观察 | SystemData 控制面 |
| 业务 Schema、迁移产物及其兼容性 | 注册该数据库的业务服务 |
| 基础设施地址和高权限 Secret | 部署环境/Secret Provider；SystemData 只保存 Secret 引用或版本指纹 |

## 4.2 事务边界

- 单个聚合写入、SystemData 本地审计和 Outbox 在同一解析后的 SystemData 物理目标事务提交。
- 组织移动在一个事务中锁定移动节点、目标父节点和受影响边界，更新父引用及树修订号；不逐条重写后代的稳定身份。
- 主任职切换在同一用户串行化事务中结束/拆分旧区间并创建新历史区间。
- 菜单发布在一个事务中完成验证、不可变快照写入、当前版本指针切换、审计和 Outbox。
- Identity 注册、用户核对和跨服务事件不参与本地数据库事务；采用幂等、重试、版本和对账。
- 数据库计划只读取目标状态并形成不可变步骤，不与 apply 共用长事务；apply 以 Operation 为恢复边界，按步骤提交状态并在整个关键区持有 PostgreSQL advisory lock。
- 生产 apply 前的计划、审批、备份证据和目标状态指纹必须绑定到同一 checksum；重检发现漂移时终止，不自动重做破坏性动作。

---

# 5. 项目结构与引用关系

目标后端结构：

```text
src/backend/src/Services/SystemData
├── IndustrialPlatform.SystemData.Api
├── IndustrialPlatform.SystemData.Application
├── IndustrialPlatform.SystemData.Contracts
├── IndustrialPlatform.SystemData.Domain
└── IndustrialPlatform.SystemData.Infrastructure

tests/SystemData
└── IndustrialPlatform.SystemData.Tests
    ├── Domain
    ├── Application
    ├── Infrastructure
    ├── Api
    └── Contracts
```

目标前端结构：

```text
src/frontend/src
├── api/systemData/**
├── systemData/runtime/**
├── stores/systemData/**
├── pages/pc/systemData/**
└── router/systemDataRoutes.ts
```

引用方向：

```text
Api → Application → Domain
Api → Contracts
Application → Contracts
Infrastructure → Application + Domain
Contracts → 无项目引用
```

禁止：

- SystemData 引用 Identity Domain/Infrastructure/Repository 或其数据库。
- Domain 引用 Contracts、Infrastructure、Api 或前端类型。
- 后续同宿主模块引用 SystemData Repository；只能调用公开应用契约、API 或事件。
- 前端页面直接解析缓存、拼接后端 URL 或绕过 PF-01/AuthStore 稳定端口。

---

# 6. 全局技术与实施约束

- 稳定逻辑数据库身份固定为 `systemdata_db`；本模块表前缀固定为 `system_data_`；迁移账本为 `system_data_schema_migrations`，并在解析后的 SystemData 物理目标运行。
- SystemData 自身数据库由 PostgreSQL 18 基础设施执行最小引导，随后由 SystemData 自有 migration runner 应用版本化迁移；控制 API 只编排其他服务数据库，禁止自举循环。
- 所有环境禁止 `EnsureCreated`、运行时自动建删 Schema 和未版本化 Code First DDL；SQLite 本地替身也执行显式迁移。
- 不创建独立 Migrator/Seeder Service。初始化 Runner 是 `SystemData.Service` 内部托管组件，可与 API 同进程或使用同一宿主镜像的一次性隔离任务运行，但不形成新的服务所有权边界。
- 后续 File/Notification/Audit/Scheduler/PlatformHealth 必须使用自己的表前缀和迁移账本，不复用 `system_data_`。
- 内部主键使用 `Guid`；领域与跨服务稳定身份使用 `NId`。
- NId 去除首尾空格，长度 1～128，匹配 `^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$`，保存规范化比较值，创建后不可修改。
- 所有时间使用 `DateTimeOffset`；API 使用带偏移 ISO 8601；有效期采用左闭右开 `[EffectiveFrom, EffectiveTo)`。
- 所有租户数据携带 `TenantNId`；租户只来自验证后的身份上下文，Request 不允许覆盖。
- 写接口使用双版本乐观并发；跨租户对象统一返回 404。
- Api 使用 `ApiResult<T>` / `PageResult<T>`；错误判断只依赖 HTTP 与稳定 code。
- Contracts 使用语义化兼容版本；破坏性变更新建 v2，不原位改变 v1。
- 密钥、连接串、服务凭据和客户地址不提交普通 appsettings。
- 普通 SystemData runtime、SystemData 自迁移、数据库 provision admin 三类凭据必须分离；编排数据库、API 响应、日志、Trace、审计和事件均不得持久化明文 Secret。
- API 只接受已注册、带 checksum/签名的不可变迁移产物标识；禁止传入任意磁盘路径、Shell 命令、SQL 文本或自定义角色名。
- 每个任务执行 TDD 并记录命令、退出码、测试数、覆盖率、报告路径和环境限制。

## 6.1 统一数据建模约束

每张实体表应用 BuildingBlocks Entity 公共生命周期：

```text
Id / IsFrozen / IsLocked / IsDeleted / EntityType
CreatedOn / LastUpdatedOn / OptimisticVersion / ConcurrencyVersion
```

字段表只列业务字段，不逐表重复公共列。PostgreSQL 使用 `snake_case`。

- 同库父子关系使用 `(id,is_deleted)` 复合外键和子表父删除快照，父状态改变通过 `ON UPDATE CASCADE` 或同事务等价机制同步。
- 有效子项查询同时过滤自身 `is_deleted=false` 和父引用快照为 false。
- 跨服务只保存 NId 和必要快照，不建外键、不复制对方 Entity 生命周期。
- 稳定 NId 默认历史不复用；公开 API 不提供组织、岗位、任职、资源和发布快照的物理删除。

## 6.2 身份上下文已知阻塞

SystemData 要求：

```text
SubjectUserNId = JWT sub（string）
TenantNId      = JWT tenant_id（string）
RoleNIds       = JWT role[]
```

当前 `ICurrentUser.UserId : Guid?` 不能满足该契约。PF-02 可以先定义应用端口和测试替身，但真实 Api 适配必须等待 PF-00 明确稳定字符串 UserNId 上下文；不得临时把 NId 转 Guid、另发内部 Guid Claim 或复制一套长期安全组件。

状态统一为：

```text
待细化 → 待派遣 → 已派遣 → 开发中 → 待验收 → 已完成
```

发现前置契约冲突时统一改为：

```text
设计待确认
```

---

# 7. 领域模型详细设计

## 7.1 Service Initialization Pipeline 与环境引导

拓扑规则补充（以蓝图 33 为准）：`DatabaseTopologyOptions` 为可信环境配置，包含 `Mode`、`SharedDatabaseName`、`SharedSqliteFile`、`ServiceDatabases`。服务清单始终登记稳定逻辑 `DatabaseName`；SystemData 在运行时解析 `ResolvedDatabaseTarget` 的 `EnvironmentName`、`Mode`、`ServiceKey`、`Provider`、`LogicalDatabaseName`、`PhysicalDatabaseName` 与 `IsSharedPhysicalDatabase`。API 不接受物理目标、路径、地址或凭据；未知模式、非法名称、缺失 shared target/mapping 和非 Development Shared 一律拒绝且不作静默回退。

### 7.1.1 SystemData 自身最小引导

`SystemData.Service` 的控制面不能依赖尚未启动的自身 API。唯一允许的引导顺序为：

```text
PostgreSQL 18 基础设施
  → 解析逻辑 systemdata_db：Shared Development 创建 industrial_platform_dev；PerService 创建 systemdata_db
  → 创建并最小授权 systemdata_owner / systemdata_migrator / systemdata_runtime
  → 将对应 Secret 写入受控 Secret Provider
  → SystemData 自有 migration runner 校验数据库身份并应用签名迁移产物
  → SystemData 本地执行最小 SystemBaseline 并写 system_data_seed_ledger
  → SystemData readiness 成功
  → 开放对其他服务的 ServiceInitialization API/Runner
```

- 基础设施只负责数据库、角色和最小 grant，不创建 `system_data_*` 业务表。
- SystemData 自有本地 runner 只消费随服务发布、checksum 可验证的迁移/SystemBaseline 产物；复用通用 version/checksum/双账本语义，但不得调用控制 API、`EnsureCreated` 或任意 SQL 管理端点。
- SystemData 自身登记为 `ServiceKey=systemdata`、`ModuleKey=systemdata`、`Provider=PostgreSQL`、`DatabaseName=systemdata_db`、`MigrationArtifactId=IndustrialPlatform.SystemData.Infrastructure`，但 `AutoProvision=false`、`AutoMigrate=false`，仅供查询和审计观察。
- Shared Development 时，基础设施只物理创建一次 `industrial_platform_dev`；SystemData 在其中运行 `system_data_schema_migrations`。PerService 时物理创建 `systemdata_db`，这是唯一的基础设施自举例外。
- 当前未跟踪的 `deploy/cloud-dev/**` 只作为环境脚本意图输入；后续执行必须先核对、保留并与其所有者协调，不得由 PF-02 覆盖现有并行改动。

### 7.1.2 注册清单与环境策略

`ServiceInitializationManifestV1` 由服务/模块所有者发布，SystemData 校验并保存版本化注册。现有 `DatabaseRegistrationManifestV1` 作为无 SeedSets 的兼容输入保留：

```text
TenantNId / EnvironmentNId
ServiceKey / ModuleKey / Provider = PostgreSQL | SQLite
DatabaseName
MigrationArtifactId / MigrationVersion / MigrationChecksum / MigrationSignature
OwnerNId / DesiredState
RequiredRoles = Migrator | Runtime
AutoProvision / AutoMigrate
CompatibilityMode = ExpandContract
BackupPolicyNId / RecoveryMetadata
ManifestVersion / ManifestChecksum
SeedSets[] {
  SeedKey / SeedVersion / SeedClass / Scope
  SeedArtifactId / SeedChecksum / SeedSignature
  RequiredForReadiness / AllowedEnvironments
  DependsOnMigrationVersion / DependsOnSeedKeys
  BootstrapPolicy
}
```

- `ServiceKey + ModuleKey`、环境、租户和 Scope 共同确定初始化身份；同版本同 checksum 幂等，不同内容 drift。
- 业务服务/模块拥有 Schema、迁移/种子内容、initializer Bundle、版本兼容性和恢复说明；SystemData 只验证 allowlist、签名/checksum、依赖和环境策略并调度。
- 清单、API 和控制表不得包含种子实际内容、服务器地址、管理员连接串、密码、Token、密码哈希、Secret、任意角色名、SQL、路径或命令。
- 注册保留逻辑身份、解析后物理身份、拓扑 Mode/revision、Manifest/Artifact checksum 与 desired version；逻辑名不随物理部署拓扑改变。
- Shared SQLite 是 Development 默认；PerService SQLite 仅为显式验证模式。远程模式开启后，SystemData/目标 PostgreSQL 不可用时禁止静默回退 SQLite。

`DatabaseTopology` 先定义物理目标策略：

| 环境 | 允许的拓扑 |
| --- | --- |
| Development | 默认 `Shared`，可显式选择 `PerService` |
| Test | 仅 `PerService` |
| Staging | 仅 `PerService` |
| Production | 仅 `PerService` |

`DatabaseEnvironmentPolicy` 独立定义自动 plan/apply 门禁：

| 环境 | 自动 plan/apply 策略 |
| --- | --- |
| Development | 必须先 plan；可配置可信服务自动 approval/backup 豁免，apply 仍异步且受锁/幂等保护 |
| Test | 必须先 plan；可配置可信服务自动 approval/backup 豁免，apply 仍异步且受锁/幂等保护 |
| Staging | plan 后显式 apply；建议要求备份证据 |
| Production | 强制 `plan → 审批 → 备份证据 → apply → verify`，任何环节不可跳过 |

`Local` 只是使用 SQLite Provider 的 Development profile，仍由同一 `DatabaseTopology` 解析，不是第五种环境或另一套拓扑语义。

四类 SeedSet：

| SeedClass | 内容边界 | readiness/环境 |
| --- | --- | --- |
| `SystemBaseline` | 权限码、系统角色、内置状态、驱动类型等平台必需目录 | 默认 RequiredForReadiness |
| `TenantBaseline` | 租户默认配置、分类、策略 | manifest 决定是否 RequiredForReadiness |
| `EnvironmentSample` | Development/Test 样例 | 必须显式启用；Staging/Production 拒绝 |
| `SecretBootstrap` | Identity admin 等一次性敏感引导 | 环境策略显式允许；缺 Secret 时失败并 NotReady |

普通业务运行时创建用户所使用的初始密码不是种子，不得通过初始化管道替代业务用例。

### 7.1.3 Plan、审批与备份证据

Plan 本身是异步操作：`POST /plans` 返回 `202 + OperationId`，Runner 检查目标数据库身份、当前迁移版本、角色/grant、待执行产物和环境策略，成功后生成不可变 `DatabaseProvisionPlan`：

```text
PlanNId / TenantNId / EnvironmentNId / ServiceKey
RequestedMigrationVersion / CurrentMigrationVersion
RequestedSeedVersions[] / CurrentSeedObservations[]
TargetStateFingerprint / PlanChecksum
Steps[] / RiskLevel / DestructiveChangeDetected
RequiredApprovalPolicy / RequiredBackupPolicy
CreatedOn / ExpiresOn（默认 30 分钟）
```

- 每一步包含稳定 StepKind、顺序、输入摘要、前置/后置条件和风险，不返回 Secret、原始连接串或完整 SQL。
- apply 必须重新 inspect；目标状态指纹、清单版本、迁移/种子产物 checksum、依赖图或环境策略变化均视为 drift，返回冲突并要求重新 plan。
- 已有数据的拓扑 Mode、物理映射或 revision 变化同样是 drift；不得自动复制、重命名、合并或拆分数据库。
- `DatabaseApproval` 记录审批人、权限、理由、时间，并绑定 PlanNId、PlanChecksum、EnvironmentNId 和目标指纹。
- `DatabaseBackupEvidence` 记录备份提供者、备份标识、完成时间、验证状态、保留策略和目标指纹，不保存备份凭据。生产 apply 只接受未过期、已验证且与计划完全匹配的证据。

### 7.1.4 异步 Operation 与 Runner

`DatabaseProvisionOperation` 是计划和执行的统一状态载体：

```text
OperationId / OperationNId / Kind = Plan | Apply
TenantNId / EnvironmentNId / ServiceKey / PlanNId
RequestedVersion / IdempotencyKey / RequestHash
Status = Queued | Running | Succeeded | Failed | Cancelled | TimedOut
Phase = Validate | Inspect | ProvisionDatabase | ProvisionRoles | Backup | SchemaMigration | RequiredSeed | SecretBootstrap | Verify
Attempt / LeaseOwner / LeaseExpiresOn / HeartbeatOn
QueuedOn / StartedOn / CompletedOn / TimeoutOn
SanitizedErrorCode / SanitizedErrorSummary / TraceId
```

- API 只入队并返回 `202`；Runner 使用数据库可靠队列与 `FOR UPDATE SKIP LOCKED` 领取操作，不引入独立消息消费者服务。
- `961cad4`/当前 WIP 的 `OperationPhase.Migrate` 在 v1 兼容层映射为 `SchemaMigration`；持久化值不得原位破坏，新增阶段须用显式兼容映射和迁移测试。
- Shared 目标解析按 PhysicalDatabaseName 去重；每个服务的初始化器独立执行并报告，因此一个服务失败只令该服务本地 NotReady。
- 推荐 Runner lease 60 秒、heartbeat 15 秒、poll 2 秒；plan 默认超时 2 分钟，apply 默认 30 分钟，均由环境策略限定范围。
- 同一 `Idempotency-Key + RequestHash` 返回原 Operation；同 Key 不同请求返回 409。每个步骤单独记录 attempt、起止时间、结果和脱敏诊断。
- SystemData 的 Operation 只记录 Inspect/Plan/Apply/Verify 调用；Migration、RequiredSeed 与 Bootstrap 的恢复边界由目标服务初始化器及其本地 Ledger 证明，控制面不得替代该事实。
- 取消只允许 Queued 或安全阶段边界；不得中断正在提交的迁移事务。超时后释放 lease 前先检查数据库会话与实际版本，避免双执行。

### 7.1.5 锁、权限、Secret 与目标角色

- provision 关键区由控制面避免重复编排；migration/seed/bootstrap/verify 的锁与幂等由目标服务初始化器和本地 Ledger 负责。
- `provision admin` 只负责创建数据库/角色/grant；每服务 `migrator` 仅可在自身数据库/Schema 执行所需 DDL；`runtime` 仅获必需 DML。SystemData 普通运行连接不得拥有创建数据库或角色权限。
- provision admin Secret 只从环境变量、容器/Kubernetes Secret 或既有 Secret Provider 临时解析；控制面只保存 SecretRef、版本或 fingerprint，不保存值。
- 新生成的目标 runtime/migrator Secret 直接写入配置的 Secret Sink；API 只返回 credential version/fingerprint。日志、Trace、审计、异常、事件、数据库快照和测试夹具均执行敏感信息扫描。
- Runner 通过进程内或受信 HTTP 端口调用服务初始化器，只传非敏感上下文；Migration、Seed 与 Bootstrap 实现随目标服务发布，不由 SystemData 下载、解释或执行。禁止 API 传任意 SQL、路径、命令或凭据。
- 服务 initializer 自行解析本服务 Secret Provider，只回报脱敏 version/checksum/status/TraceId；SystemData 不接收或透传 Secret 值。

### 7.1.6 双账本、SeedObservation 与数据保护

每个服务或具有独立持久化生命周期的初始化单元拥有 Migration/Seed Ledger。共享宿主内逻辑模块保持表前缀和领域边界，但不因模块数量机械拆分 Ledger、Migration、Outbox、Inbox 或基础设施。

seed ledger 最少记录：

```text
TenantNId / ModuleKey / SeedKey / SeedVersion / Checksum / Scope
Status / AppliedOn / OperationNId / TraceId
```

- 本地账本是权威；SystemData 只保存 `SeedObservation` 和初始化摘要，不跨库直读。
- 相同 key/scope/version/checksum 重跑幂等成功；同版本不同 checksum 为 drift 并拒绝。
- 版本升级新增 ledger 版本；失败只从可验证边界继续，不静默覆盖成功记录。
- 种子不得覆盖管理员维护数据。删除或修正必须使用显式 `DataPatch`，并在 plan 中列出风险、影响范围、备份与恢复说明。

### 7.1.7 服务启动握手与 NotReady

其他业务服务首次初始化或升级时通过控制面：

```text
注册/查询 desired state → Inspect → Plan → Apply → Verify → 脱敏 Observation
```

- 日常启动直接使用自身 runtime Secret 验证 database identity、Migration Ledger、Required Seed Ledger 和 Bootstrap 状态。SystemData 不可达时，已初始化服务仍可按本地事实 Ready；本地事实失败或版本不匹配时 readiness 返回 503 `NotReady`。
- 远程环境不得在失败时启动在 SQLite、旧 Schema 或错误数据库上；写流量在 readiness 成功前不得进入。
- PF-02 提供初始化调用端口、Operation/Observation 契约和测试 fixture；具体业务服务负责自己的初始化器与 readiness，不由 SystemData 代写或承载。
- fixture 必须覆盖 Shared/PerService SQLite 与 PostgreSQL；不得通过业务 API 创建数据库或用 `EnsureCreated` 绕过显式迁移。
- `DatabaseReadinessV1` 只绑定 `ServiceKey`、`LogicalDatabaseName`、脱敏 `PhysicalDatabaseTarget/Fingerprint`、`ArtifactChecksum`、`DesiredVersion`、`ObservedVersion` 和 `TopologyRevision`；不得包含连接串、SQLite 路径或任何凭据。

## 7.2 AdministrativeOrganization 聚合

业务字段：

```text
TenantNId
NId / NormalizedNId
Name
Type = Company | Department | Section | Team
ParentOrganizationNId
ParentOrganization_Id / ParentOrganization_IsDeleted
DisplayOrder
Status = Active | Inactive
```

不变量：

- Company 的 Parent 必须为空；同一租户允许多个 Company 根。
- Department 的父类型只能为 Company/Department；Section 只能为 Department/Section；Team 只能为 Department/Section/Team。
- 父子必须属于同一 TenantNId；禁止自指、祖先循环和跨租户移动。
- 同一父节点下活动组织名称大小写不敏感唯一；NId 在租户内全历史唯一。
- 移动保留当前节点、后代、岗位和任职的稳定 NId；只改变移动根的父引用并推进树修订号。
- 移动前返回影响摘要；提交命令必须携带摘要对应的 OrganizationRevision 与双并发版本，过期则 409。
- 停用前必须不存在活动子组织、活动岗位和当前/未来有效任职；不做隐式级联。
- Inactive 组织不能新增子组织、岗位或任职；恢复时父组织必须 Active。
- 公共 API 不提供删除；历史修复只允许受控运维流程。

核心行为：

```text
CreateRootCompany / CreateChild
Rename / ChangeDisplayOrder
PreviewMove / Move
Deactivate / Activate
```

## 7.3 Position 聚合

业务字段：

```text
TenantNId
NId / NormalizedNId
OrganizationNId
Organization_Id / Organization_IsDeleted
Name
Description
DisplayOrder
Status = Active | Inactive
```

不变量：

- 岗位只属于一个 Active 行政组织，创建后 OrganizationNId 不可修改。
- 同一组织下活动岗位名称大小写不敏感唯一；PositionNId 在租户内全历史唯一。
- 不同组织可以存在同名岗位，它们是不同岗位实例。
- 存在当前或未来有效任职时不能停用岗位；恢复时所属组织必须 Active。
- 岗位调整到其他组织必须新建目标岗位并显式迁移/结束任职，不提供 MovePosition。

## 7.4 UserAssignment 聚合

业务字段：

```text
TenantNId
NId / NormalizedNId
UserNId
UserDisplayNameSnapshot
OrganizationNId
PositionNId
Position_Id / Position_IsDeleted
IsPrimary
EffectiveFrom
EffectiveTo
State = Enabled | Cancelled
CancelledOn
CancelReason
```

`OrganizationNId` 是便于契约与查询的组织业务标识，必须与 Position 当前所属组织一致；数据库关系以 Position 复合外键为准，不建立 Identity User 外键。

投影状态由当前时间派生：

```text
Cancelled：State=Cancelled
Scheduled：Enabled 且 now < EffectiveFrom
Current：Enabled 且 EffectiveFrom <= now < EffectiveTo（或 EffectiveTo 为空）
Ended：Enabled 且 EffectiveTo <= now
```

不变量：

- `EffectiveTo` 可空；非空时必须晚于 `EffectiveFrom`。
- 同一 TenantNId、UserNId、PositionNId 的 Enabled 区间不得重叠。
- 用户在任一时点存在 Enabled 任职时，该时点必须且只能覆盖一条 `IsPrimary=true` 任职。
- 主任职不授予 Identity 权限，不自动形成制造数据权限，只是人员目录和默认行政上下文。
- 新建/调整任职前必须通过 `IIdentityUserDirectory` 验证 UserNId 存在且可任职；该端口不可用时写操作 fail-closed。
- 创建任职要求组织和岗位均 Active，且 OrganizationNId 与岗位归属一致。
- Scheduled 任职可更新区间或取消；Current 任职只能结束，不允许回写用户、岗位或开始时间；Ended/Cancelled 不可修改。
- 主任职切换由专用用例完成。若目标原为兼任，应用服务在切换时原子拆分历史区间，禁止原位改写已发生历史。
- 未任职用户允许存在；结束最后一条任职后不强制创建占位记录。

并发与区间完整性：

- 应用服务按 `TenantNId + UserNId` 获取 PostgreSQL 事务级 advisory lock，再读取受影响区间并校验重叠和主任职覆盖。
- SQLite 测试替身使用进程内按用户锁验证相同语义；PostgreSQL 真库是最终并发验收源。
- 数据库增加 `effective_to IS NULL OR effective_to > effective_from` 检查和覆盖查询索引；不依赖缺少事务锁的“先查后写”。

核心行为：

```text
CreateAssignment
UpdateScheduledAssignment
EndAssignment
CancelScheduledAssignment
SetPrimaryAssignment（原子历史拆分）
```

## 7.5 UiResource 与 ModuleManifest

`UiResource` 业务字段：

```text
TenantNId
NId / NormalizedNId                 # ResourceNId
OwnerModuleNId
ManifestVersion
Type = Page | Action
Name
RouteName                            # Page 必填，Action 为空
RequiredPermissionNId
SupportedTerminals = Pc | Pda | Mobile 的集合
Status = Active | Retired
```

规则：

- ResourceNId 与 PermissionNId 分离，均创建后不可修改。
- 每个资源最多绑定一个 PermissionNId；本阶段所有可操作资源均要求权限，只有 Navigation Group 无权限。
- RouteName 必须来自模块清单，管理员不能输入前端组件路径、任意 URL 或脚本。
- 模块清单包含 `ModuleNId + Version + Checksum + PermissionDeclarations + Resources`，按版本和校验和幂等应用。
- PF-02 只通过迁移/启动注册受信任的 SystemData 内置清单；远程模块注册端点在服务身份契约稳定前保持禁用。
- Retired 资源不能新增菜单引用；已有草稿必须修复后才能发布。

SystemData 声明权限、Identity 拥有目录和分配。注册流程：

```text
SystemData versioned permission manifest
    ↓ 幂等注册/核对
Identity permission registry
    ↓ registration receipt（version + checksum + verifiedOn）
SystemData local verification projection
```

注册回执是发布校验输入，不改变 Identity 权威。Identity 不可用或清单未获确认时，现有发布快照继续服务，但新的相关菜单发布被拒绝。

## 7.6 NavigationSet 聚合

一个租户首期固定一个 `PLATFORM_NAVIGATION` 导航集，业务字段：

```text
TenantNId
NId
DraftRevision
ActiveSnapshotNId
PreviousSnapshotNId
```

`NavigationNode` 草稿字段：

```text
TenantNId
NId / NormalizedNId
NavigationSet_Id / NavigationSet_IsDeleted
ParentNodeNId
ParentNode_Id / ParentNode_IsDeleted
Kind = Group | Link
Label
IconKey
ResourceNId
Resource_Id / Resource_IsDeleted
FeatureNId
Feature_Id / Feature_IsDeleted
DisplayOrder
VisibleTerminals
Status = Active | Inactive
```

规则：

- Group 不引用 ResourceNId/PermissionNId；Link 必须引用 Active Page Resource。
- Group 只有存在最终可见子项时才返回；前端不显示空分组。
- VisibleTerminals 必须是资源 SupportedTerminals 的非空子集。
- FeatureNId 可空；有值时必须引用 Active 功能定义。
- 同父节点 DisplayOrder 可重复但最终稳定排序为 `DisplayOrder + NId`。
- 禁止循环、孤儿节点、重复 NId、无效 IconKey、未知路由、退休资源、未知权限和未知功能引用。

发布流程：

```text
读取 DraftRevision 与全部草稿
  ↓
验证树/资源/路由/终端/功能/Identity 权限注册回执
  ↓
生成不可变 NavigationSnapshot + SnapshotNodes
  ↓
原子切换 ActiveSnapshotNId / PreviousSnapshotNId
  ↓
本地审计 + Outbox + 缓存版本推进
```

发布快照不可修改。回滚只允许切换到当前 PreviousSnapshot，并将该动作生成新的发布修订与审计事实；不回写旧草稿或删除历史快照。

## 7.7 FeatureDefinition 与租户覆盖

功能定义由模块清单注册：

```text
TenantNId
NId / NormalizedNId                 # FeatureNId
OwnerModuleNId
Name
Description
DefaultEnabled
Status = Active | Retired
```

租户覆盖：

```text
FeatureNId
Mode = Inherit | Enabled | Disabled
Reason
```

有效值优先级固定为：

```text
进程/环境紧急 Disabled 清单（只能强制关闭）
    > 租户覆盖 Enabled/Disabled
    > 功能定义 DefaultEnabled
```

- 首期不支持用户、角色、组织、百分比、地区或时间窗定向；权限与终端可见性不属于 FeatureFlag。
- Retired 功能一律 Disabled，不能被租户重新启用。
- 修改覆盖推进 FeatureRevision，发布 `FeatureFlagsChanged.v1`。
- 功能关闭只控制入口和用例启用状态，不替代目标 API 授权。

## 7.8 ServiceCatalogEntry 聚合

业务字段：

```text
TenantNId
NId / NormalizedNId                 # ServiceNId
Kind = Platform | External
Name
Description
OwnerOrganizationNId
OwnerOrganization_Id / OwnerOrganization_IsDeleted
TechnicalOwnerUserNId
OwnerDisplaySnapshot
EntryPoint
GatewayPathPrefix
HealthPath
SupportedTerminals
Status = Active | Inactive | Retired
Source = Manifest | Manual
```

规则：

- Platform 条目由受信任模块清单注册；管理员可以修改展示、所有者和启停，不能修改清单拥有的 GatewayPathPrefix/HealthPath。
- Platform EntryPoint 使用站内相对路径；HealthPath 必须是以 `/health` 开始的相对路径。本阶段只声明，不主动探测。
- External 条目可由管理员创建，只允许 HTTPS 绝对 URL；拒绝凭据、fragment、协议相对地址、loopback、link-local、私网管理地址和云元数据地址。
- PF-02 不请求 External URL，不把目录条目显示为 Healthy。真实健康状态由 PF-07 PlatformHealth 另行拥有。
- OwnerOrganizationNId 必须引用 Active 行政组织；TechnicalOwnerUserNId 可空，非空时通过 Identity 目录核对并保存显示快照。
- Retired NId 不复用；Retired 条目不出现在运行目录。

## 7.9 TenantThemePolicy 聚合

每租户一条策略：

```text
TenantNId
NId = TENANT_THEME_POLICY
AllowedPalettes
AllowedModes
AllowedPcDensities
DefaultPalette
DefaultMode
DefaultPcDensity
PolicyRevision
```

允许值固定来自 PF-01 已批准类型：

```text
ThemePalette = industrial-cyan | technology-blue | neutral-gray
ThemeMode = light | dark | system
PcDensity = comfortable | compact
```

规则：

- 三个 Allowed 集合均不能为空；默认值必须属于相应 Allowed 集合。
- SystemData 只拥有租户默认和可选范围，不保存用户个人选择。
- PF-01 优先级保持“用户显式偏好 > 租户默认 > 产品默认”；但用户值被新策略禁止时，ThemeStore 使用租户默认并提示策略收敛，不把非法值继续写回。
- 不支持任意颜色、Logo、CSS、字体、组件圆角或租户自定义主题包。

---

# 8. 数据与持久化设计

## 8.1 数据库与表

数据库：稳定逻辑身份为 `systemdata_db`；Shared Development 的物理目标为 `industrial_platform_dev` 并运行 `system_data_schema_migrations`，PerService 才物理引导 `systemdata_db`。

| 表 | 主要业务字段 | 关键约束/索引 |
| --- | --- | --- |
| `system_data_database_environment_policy` | TenantNId、EnvironmentNId、EnvironmentKind、ApprovalRequired、BackupRequired、PlanTtlSeconds、Plan/ApplyTimeoutSeconds、MaxPreMigrationRetries、PolicyRevision | Tenant+Environment唯一；生产强制审批与备份检查 |
| `system_data_database_registration` | TenantNId、EnvironmentNId、ServiceKey、ModuleKey、Provider、LogicalDatabaseName、PhysicalDatabaseName、IsSharedPhysicalDatabase、TopologyMode/Revision、MigrationArtifactId/Version/Checksum/Signature、OwnerNId、DesiredState、AutoProvision/AutoMigrate、ManifestVersion/Checksum、Status | Tenant+Environment+Service+Module唯一；保留现有表名兼容数据库控制面 |
| `system_data_initialization_seed_set` | Registration父引用、ModuleKey、SeedKey、SeedVersion、SeedClass、Scope、ArtifactId/Checksum/Signature、RequiredForReadiness、AllowedEnvironments、DependsOnMigrationVersion、DependsOnSeedKeys、BootstrapPolicy | Registration+Module+SeedKey+Scope+Version唯一；只存元数据，不存内容/Secret |
| `system_data_database_plan` | TenantNId、PlanNId、EnvironmentNId、ServiceKey、ModuleKey、Requested/CurrentMigrationVersion、RequestedSeedVersions、CurrentSeedObservation摘要、TargetStateFingerprint、PlanChecksum、RiskLevel、DestructiveChangeDetected、RequiredPolicies、ExpiresOn | PlanNId唯一；checksum唯一；过期索引；成功后不可变 |
| `system_data_database_plan_step` | Plan父引用、Sequence、StepKind、InputSummary、PreconditionSummary、PostconditionSummary、RiskLevel | Plan+Sequence唯一；不可变；不保存 SQL/Secret |
| `system_data_database_approval` | TenantNId、ApprovalNId、PlanNId、PlanChecksum、TargetStateFingerprint、ApprovedByUserNId、Reason、ApprovedOn、ExpiresOn | Plan+审批策略唯一；证据绑定索引；只追加 |
| `system_data_database_backup_evidence` | TenantNId、EvidenceNId、PlanNId、PlanChecksum、TargetStateFingerprint、Provider、BackupReference、CompletedOn、VerifiedOn、RetentionUntil、Status | Plan+证据唯一；不得保存访问 Secret |
| `system_data_database_operation` | TenantNId、OperationNId、Kind、EnvironmentNId、ServiceKey、ModuleKey、PlanNId、RequestedMigration/SeedVersions、IdempotencyKey、RequestHash、Status、Phase、Attempt、LeaseOwner/Expiry、HeartbeatOn、TimeoutOn、SanitizedError、TraceId | OperationNId唯一；Tenant+IdempotencyKey唯一；状态/lease/队列索引；保留现有表名 |
| `system_data_database_operation_step` | Operation父引用、Sequence、Phase、Attempt、Status、StartedOn、CompletedOn、SanitizedError | Operation+Sequence+Attempt唯一；只追加诊断 |
| `system_data_database_migration_observation` | TenantNId、EnvironmentNId、ServiceKey、DatabaseIdentityFingerprint、ObservedVersion、ArtifactChecksum、ObservedOn、OperationNId、VerificationStatus | Tenant+Environment+Service+ObservedOn索引；不替代服务自有迁移账本 |
| `system_data_seed_observation` | TenantNId、EnvironmentNId、ServiceKey、ModuleKey、SeedKey、SeedVersion、SeedClass、Scope、Checksum、Status、ObservedOn、OperationNId、TraceId | 只保存脱敏观察；不替代 `<module>_seed_ledger`，不含内容/Secret |
| `system_data_seed_ledger` | TenantNId、ModuleKey、SeedKey、SeedVersion、Checksum、Scope、Status、AppliedOn、OperationNId、TraceId | SystemData 自身本地权威账本；相同版本同 checksum 幂等，不同 checksum drift |
| `system_data_organization` | TenantNId、NId、NormalizedNId、Name、Type、ParentOrganizationNId、ParentOrganization_Id/IsDeleted、DisplayOrder、Status | Tenant+NormalizedNId全历史唯一；同父活动名称唯一；父复合外键；Tenant+Parent+Status+Order索引 |
| `system_data_position` | TenantNId、NId、NormalizedNId、OrganizationNId、Organization_Id/IsDeleted、Name、Description、DisplayOrder、Status | Tenant+NormalizedNId全历史唯一；同组织活动名称唯一；组织复合外键 |
| `system_data_user_assignment` | TenantNId、NId、NormalizedNId、UserNId、UserDisplayNameSnapshot、OrganizationNId、PositionNId、Position_Id/IsDeleted、IsPrimary、EffectiveFrom/To、State、CancelledOn、CancelReason | Tenant+NormalizedNId唯一；区间检查；User+time、Position+time索引；Position复合外键 |
| `system_data_module_manifest` | TenantNId、ModuleNId、ManifestVersion、Checksum、AppliedOn、PermissionReceiptVersion、PermissionReceiptChecksum、PermissionVerifiedOn | Tenant+Module+Version唯一；当前清单索引 |
| `system_data_ui_resource` | TenantNId、NId、NormalizedNId、OwnerModuleNId、ManifestVersion、Type、Name、RouteName、RequiredPermissionNId、SupportedTerminals、Status | Tenant+NormalizedNId全历史唯一；RouteName活动唯一；OwnerModule+Status索引 |
| `system_data_navigation_set` | TenantNId、NId、DraftRevision、ActiveSnapshotNId、PreviousSnapshotNId | Tenant+NId唯一；每租户一个活动集合 |
| `system_data_navigation_node` | TenantNId、NId、NormalizedNId、NavigationSet父引用、ParentNode父引用、Kind、Label、IconKey、ResourceNId/Resource父引用、FeatureNId/Feature父引用、DisplayOrder、VisibleTerminals、Status | 树父复合外键；Resource/Feature复合外键；Set+Parent+Order索引 |
| `system_data_navigation_snapshot` | TenantNId、NId、Revision、SourceDraftRevision、PublishedByUserNId、PublishedOn、ContentChecksum、RollbackSourceSnapshotNId | Tenant+Revision唯一；ContentChecksum索引；不可变 |
| `system_data_navigation_snapshot_node` | Snapshot父引用、NodeNId、ParentNodeNId、Kind、Label、IconKey、ResourceNId、RouteName、RequiredPermissionNId、FeatureNId、DisplayOrder、VisibleTerminals | Snapshot复合外键；Snapshot+Terminal+Order索引；不可变投影 |
| `system_data_feature_definition` | TenantNId、NId、NormalizedNId、OwnerModuleNId、Name、Description、DefaultEnabled、Status | Tenant+NormalizedNId全历史唯一 |
| `system_data_feature_override` | TenantNId、FeatureNId、Feature_Id/IsDeleted、Mode、Reason | Feature复合外键；每租户每功能唯一活动覆盖 |
| `system_data_service_catalog` | TenantNId、NId、NormalizedNId、Kind、Name、Description、OwnerOrganizationNId/OwnerOrganization父引用、TechnicalOwnerUserNId、OwnerDisplaySnapshot、EntryPoint、GatewayPathPrefix、HealthPath、SupportedTerminals、Status、Source | Tenant+NormalizedNId全历史唯一；OwnerOrganization复合外键；Status+Name索引 |
| `system_data_theme_policy` | TenantNId、NId、AllowedPalettes、AllowedModes、AllowedPcDensities、DefaultPalette、DefaultMode、DefaultPcDensity、PolicyRevision | 每租户唯一；集合非空和默认包含检查 |
| `system_data_projection_revision` | TenantNId、Area、Revision、GeneratedOn | Tenant+Area唯一；事务内单调递增；为组织、导航运行投影、功能、服务目录和主题缓存提供版本 |
| `system_data_operation_audit` | TenantNId、ActorUserNId、Action、ObjectType、ObjectNId、Reason、BeforeSummary、AfterSummary、TraceId | Tenant+CreatedOn、ObjectType+ObjectNId索引；只追加 |
| `system_data_outbox` | EventId、EventType、EventVersion、Payload、EventCreatedTime、PublishedOn、RetryCount、LastError | EventId唯一；PublishedOn+EventCreatedTime索引 |

## 8.2 删除、并发和引用

- 组织、岗位、资源、功能和服务目录通过 Status 退出使用；公共 API 不调用软删除。
- 草稿节点允许软删除；快照及快照节点不可修改或删除。
- 父表 `(id,is_deleted)` 提供可引用唯一键；同库子表使用复合外键与双重过滤。
- `UserNId`、`PermissionNId`、TechnicalOwnerUserNId 不建数据库外键。
- 所有更新命令携带 `expectedOptimisticVersion` 与 `expectedConcurrencyVersion`。
- 编排表携带可信上下文中的 TenantNId 和 EnvironmentNId；SecretRef 只允许存引用/版本指纹，任何连接串和 Secret 值禁止落表。
- Operation 使用短事务领取和续租；advisory lock 不替代 operation 状态机、幂等键或目标状态重检；同物理目标的 provision 去重，服务迁移/readiness 仍独立。

## 8.3 修订与快照

组织树、导航运行投影、功能、服务目录和主题策略通过 `system_data_projection_revision` 分别维护单调递增 revision。revision 与业务写入同事务推进，用于 ETag、缓存键和事件乱序判断，不替代 Entity 双版本并发。NavigationSet.DraftRevision 和 TenantThemePolicy.PolicyRevision 仍承担各自聚合内的编辑并发语义。

---

# 9. API、事件与外部集成契约

## 9.1 路径与通用约定

```text
Gateway：/systemdata/**
内部：   /api/v1/**
```

Gateway 使用 `PathRemovePrefix=/systemdata`。所有运行端点要求已认证租户上下文；管理端点另要求 PermissionNId。GET 支持 `ETag`/`If-None-Match`，敏感用户目录响应使用 `Cache-Control: no-store`。

## 9.2 Service Initialization API

`961cad4` 已提交的 `/api/v1/database-orchestration/**` 作为 migration-only v1 兼容面保留；当 registration 不含 SeedSets/SecretBootstrap 时可继续工作。通用协议使用 `/api/v1/service-initialization/**`，复用同一底层 Plan/Operation 身份，禁止同一请求在两套控制面重复执行。

| Method | 内部路径 | 权限/主体 | 用途 |
| --- | --- | --- | --- |
| PUT | `/api/v1/service-initialization/registrations/{serviceKey}/{moduleKey}` | 可信服务身份或 `systemdata.service-initialization.register` | 幂等注册 InitializationManifest/SeedSets |
| GET | `/api/v1/service-initialization/registrations` | `systemdata.service-initialization.view` | 按环境、服务、模块、状态查询 |
| GET | `/api/v1/service-initialization/registrations/{serviceKey}/{moduleKey}` | 可信服务身份或 view | 查询 desired/current migration+seed 摘要 |
| POST | `/api/v1/service-initialization/plans` | `systemdata.service-initialization.plan` | 返回 202 和 Plan Operation |
| GET | `/api/v1/service-initialization/plans/{planNId}` | view | 查询不可变阶段、依赖与门禁 |
| POST | `/api/v1/service-initialization/plans/{planNId}/approvals` | `systemdata.service-initialization.approve` | 记录绑定计划的审批证据 |
| POST | `/api/v1/service-initialization/plans/{planNId}/backup-evidence` | `systemdata.service-initialization.backup` | 登记并验证备份证据 |
| POST | `/api/v1/service-initialization/operations/apply` | `systemdata.service-initialization.apply` | 返回 202 和 Initialize Operation |
| GET | `/api/v1/service-initialization/operations/{operationNId}` | 可信服务身份或 view | 查询阶段、seed observation 和脱敏错误 |
| GET | `/api/v1/service-initialization/operations` | view | 管理检索 |
| POST | `/api/v1/service-initialization/operations/{operationNId}/cancel` | `systemdata.service-initialization.cancel` | 在允许边界取消 |
| GET | `/api/v1/service-initialization/readiness/{serviceKey}/{moduleKey}` | 可信服务身份 | 返回 migration/required seed/bootstrap readiness |

所有 POST/PUT 携带 `Idempotency-Key`；注册清单和 apply 请求同时携带语义 request hash。响应不返回种子内容、数据库地址、角色密码、SecretRef/值、SQL、命令或原始 initializer 输出。v1→v2 adapter 必须有契约测试，migration-only 请求的 OperationId/idempotency 语义不得改变。

## 9.3 组织、岗位和任职 API

| Method | 内部路径 | 权限 | 用途 |
| --- | --- | --- | --- |
| GET | `/api/v1/organizations/tree` | `systemdata.organization.view` | 读取组织森林，可按状态过滤 |
| GET | `/api/v1/organizations/{organizationNId}` | `systemdata.organization.view` | 读取详情与并发版本 |
| POST | `/api/v1/organizations` | `systemdata.organization.create` | 创建根公司或子组织 |
| PUT | `/api/v1/organizations/{organizationNId}` | `systemdata.organization.update` | 修改名称、顺序 |
| POST | `/api/v1/organizations/{organizationNId}/move-preview` | `systemdata.organization.move` | 返回影响摘要与 OrganizationRevision |
| POST | `/api/v1/organizations/{organizationNId}/move` | `systemdata.organization.move` | 带预览修订执行移动 |
| PUT | `/api/v1/organizations/{organizationNId}/status` | `systemdata.organization.status` | 启用/停用 |
| GET | `/api/v1/positions` | `systemdata.position.view` | 按组织、状态分页 |
| POST | `/api/v1/positions` | `systemdata.position.create` | 创建岗位 |
| PUT | `/api/v1/positions/{positionNId}` | `systemdata.position.update` | 修改名称、描述、顺序 |
| PUT | `/api/v1/positions/{positionNId}/status` | `systemdata.position.status` | 启用/停用 |
| GET | `/api/v1/users/{userNId}/assignments` | `systemdata.assignment.view` | 当前、未来与历史任职 |
| POST | `/api/v1/users/{userNId}/assignments` | `systemdata.assignment.manage` | 创建任职 |
| PUT | `/api/v1/assignments/{assignmentNId}` | `systemdata.assignment.manage` | 仅修改 Scheduled 任职 |
| POST | `/api/v1/assignments/{assignmentNId}/end` | `systemdata.assignment.manage` | 结束 Current/Scheduled 区间 |
| POST | `/api/v1/assignments/{assignmentNId}/cancel` | `systemdata.assignment.manage` | 取消 Scheduled 任职 |
| POST | `/api/v1/users/{userNId}/primary-assignment` | `systemdata.assignment.manage` | 原子切换主任职并保留历史 |

关键命令 DTO：

```text
CreateOrganizationRequest
  nId, name, type, parentOrganizationNId, displayOrder

MoveOrganizationRequest
  targetParentOrganizationNId, previewOrganizationRevision,
  expectedOptimisticVersion, expectedConcurrencyVersion, reason

CreatePositionRequest
  nId, organizationNId, name, description, displayOrder

CreateAssignmentRequest
  nId, positionNId, isPrimary, effectiveFrom, effectiveTo

SetPrimaryAssignmentRequest
  targetAssignmentNId, effectiveOn, reason,
  expectedUserAssignmentRevision
```

## 9.4 资源和导航 API

| Method | 内部路径 | 权限 | 用途 |
| --- | --- | --- | --- |
| GET | `/api/v1/resources` | `systemdata.resource.view` | 查询已注册 Page/Action 资源 |
| GET | `/api/v1/navigation/draft` | `systemdata.navigation.view` | 读取草稿树和校验状态 |
| POST | `/api/v1/navigation/draft/nodes` | `systemdata.navigation.manage` | 新增 Group/Link |
| PUT | `/api/v1/navigation/draft/nodes/{nodeNId}` | `systemdata.navigation.manage` | 修改草稿节点 |
| DELETE | `/api/v1/navigation/draft/nodes/{nodeNId}` | `systemdata.navigation.manage` | 软删除无子节点草稿 |
| POST | `/api/v1/navigation/validate` | `systemdata.navigation.manage` | 返回完整发布校验结果 |
| POST | `/api/v1/navigation/publish` | `systemdata.navigation.publish` | 生成并切换不可变快照 |
| POST | `/api/v1/navigation/rollback` | `systemdata.navigation.rollback` | 回滚 PreviousSnapshot 并产生新修订 |
| GET | `/api/v1/runtime/navigation?terminal={pc|pda|mobile}` | 已认证 | 返回候选导航、revision、degraded |

运行响应节点固定返回：

```text
nodeNId, kind, label, iconKey, resourceNId,
routeName, requiredPermissionNId, featureNId,
displayOrder, children[]
```

前端按 current AuthUser.permissionNIds 与 requiredPermissionNId 求交集，再移除空 Group。运行响应不返回组件路径、外部脚本或后台数据库 Id。

## 9.5 功能、服务目录和主题 API

| Method | 内部路径 | 权限 | 用途 |
| --- | --- | --- | --- |
| GET | `/api/v1/features` | `systemdata.feature.view` | 管理视图 |
| PUT | `/api/v1/features/{featureNId}/override` | `systemdata.feature.manage` | 设置 Inherit/Enabled/Disabled |
| GET | `/api/v1/runtime/features` | 已认证 | 有效开关快照 |
| GET | `/api/v1/service-catalog` | `systemdata.service-catalog.view` | 管理目录 |
| POST | `/api/v1/service-catalog` | `systemdata.service-catalog.manage` | 新建 External 条目 |
| PUT | `/api/v1/service-catalog/{serviceNId}` | `systemdata.service-catalog.manage` | 修改允许字段 |
| PUT | `/api/v1/service-catalog/{serviceNId}/status` | `systemdata.service-catalog.manage` | 启停/退休 |
| GET | `/api/v1/runtime/service-catalog` | `systemdata.service-catalog.view` | 运行目录，不伪造健康状态 |
| GET | `/api/v1/theme-policy` | `systemdata.theme-policy.view` | 管理策略 |
| PUT | `/api/v1/theme-policy` | `systemdata.theme-policy.manage` | 更新允许范围和默认值 |
| GET | `/api/v1/runtime/theme-policy` | 已认证 | PF-01 `TenantUiDefaultsSource` 输入 |

## 9.6 权限清单

SystemData 第一批 PermissionNId：

```text
systemdata.organization.view
systemdata.organization.create
systemdata.organization.update
systemdata.organization.move
systemdata.organization.status
systemdata.position.view
systemdata.position.create
systemdata.position.update
systemdata.position.status
systemdata.assignment.view
systemdata.assignment.manage
systemdata.resource.view
systemdata.navigation.view
systemdata.navigation.manage
systemdata.navigation.publish
systemdata.navigation.rollback
systemdata.feature.view
systemdata.feature.manage
systemdata.service-catalog.view
systemdata.service-catalog.manage
systemdata.theme-policy.view
systemdata.theme-policy.manage
systemdata.database-orchestration.view
systemdata.database-orchestration.register
systemdata.database-orchestration.plan
systemdata.database-orchestration.apply
systemdata.database-orchestration.approve
systemdata.database-orchestration.backup
systemdata.database-orchestration.cancel
systemdata.service-initialization.view
systemdata.service-initialization.register
systemdata.service-initialization.plan
systemdata.service-initialization.apply
systemdata.service-initialization.approve
systemdata.service-initialization.backup
systemdata.service-initialization.cancel
```

`systemdata.service-initialization.*` 是 v2 规范权限；migration-only v1 端点在兼容期继续接受对应 `systemdata.database-orchestration.*`。两组权限一一映射并分别审计，不得用旧权限隐式扩大 Seed/Bootstrap 管理能力；兼容期结束需显式版本迁移。

权限清单契约：

```text
PermissionManifestV1
  moduleNId, manifestVersion, checksum,
  permissions[{ permissionNId, name, resourceType, parentPermissionNId }]

PermissionRegistrationReceiptV1
  moduleNId, manifestVersion, checksum, verifiedOn
```

当前 Identity 没有该稳定 API。本文只冻结 SystemData 需要的契约；真实路径、服务认证和注册适配由 PF-00 恢复后共同确认，未确认前不得臆造 Identity endpoint。

## 9.7 Identity 用户目录端口

PF-02 需要但当前未稳定的最小契约：

```text
IdentityUserDirectoryEntryV1
  userNId, loginName, name, status, authVersion

FindByUserNId(userNId)
Search(query, status, page)
```

SystemData 仅用于核对任职/技术负责人和显示快照，不复制角色、密码、联系方式或完整权限。真实 API 路径等待 PF-00；适配器必须通过双方契约测试。

## 9.8 集成事件

公共字段：

```text
eventId, eventType, eventVersion, createdTime,
tenantNId, subjectNId, revision, traceId
```

事件：

```text
SystemData.OrganizationChanged.v1
SystemData.PositionChanged.v1
SystemData.UserAssignmentsChanged.v1
SystemData.NavigationPublished.v1
SystemData.FeatureFlagsChanged.v1
SystemData.ServiceCatalogChanged.v1
SystemData.ThemePolicyChanged.v1
SystemData.DatabaseRegistrationChanged.v1
SystemData.DatabasePlanCompleted.v1
SystemData.DatabaseOperationStatusChanged.v1
SystemData.ServiceInitializationRegistrationChanged.v1
SystemData.ServiceInitializationPlanCompleted.v1
SystemData.ServiceInitializationOperationStatusChanged.v1
SystemData.OperationAudited.v1
```

- 业务写、本地审计和 Outbox 同事务；发布至少一次。
- 事件不包含数据库 Guid、完整树、完整菜单、用户权限集合、种子内容、连接地址、SecretRef/值、SQL/命令、initializer 原始输出或敏感 Owner 联系信息。初始化事件只携带 ServiceKey、ModuleKey、EnvironmentNId、公开 migration/seed 版本与 checksum、状态、OperationId、PlanChecksum 和 TraceId。数据库 v1 事件在 migration-only 兼容期保留。`SystemData.OperationAudited.v1` 只携带已脱敏摘要，供 PF-04 后续接收。
- 消费者按 eventId 去重，按 tenantNId + subjectNId + revision 丢弃乱序旧事件；收到事件后通过 API 拉取当前快照。
- Outbox 失败可重试并可观测，不能回滚已提交业务事实。

## 9.9 稳定错误码

| HTTP | code | 含义 |
| ---: | --- | --- |
| 400 | `SD_VALIDATION_FAILED` | 输入或组合规则不合法 |
| 400 | `SD_ORG_PARENT_TYPE_INVALID` | 组织父子类型不允许 |
| 409 | `SD_ORG_CYCLE` | 移动产生循环 |
| 409 | `SD_ORG_HAS_ACTIVE_DEPENDENCIES` | 组织仍有活动依赖 |
| 409 | `SD_POSITION_HAS_ACTIVE_ASSIGNMENTS` | 岗位仍有当前/未来任职 |
| 409 | `SD_ASSIGNMENT_INTERVAL_OVERLAP` | 用户岗位区间重叠 |
| 409 | `SD_ASSIGNMENT_PRIMARY_REQUIRED` | 任职区间缺少主任职 |
| 409 | `SD_ASSIGNMENT_PRIMARY_OVERLAP` | 同一时点存在多个主任职 |
| 409 | `SD_CONCURRENCY_CONFLICT` | 双版本或 revision 冲突 |
| 409 | `SD_RESOURCE_CONFLICT` | ResourceNId/RouteName/Manifest 冲突 |
| 409 | `SD_NAVIGATION_INVALID` | 草稿未通过完整校验 |
| 409 | `SD_NAVIGATION_PUBLISH_BLOCKED` | 权限未验证或前置不可用 |
| 400 | `SD_SERVICE_ENTRY_INVALID` | 服务入口或健康路径不安全 |
| 400 | `SD_THEME_POLICY_INVALID` | 默认值与允许范围不一致 |
| 404 | `SD_NOT_FOUND` | 不存在或跨租户不可见 |
| 503 | `SD_IDENTITY_DIRECTORY_UNAVAILABLE` | 用户目录不可验证，写入拒绝 |
| 503 | `SD_PERMISSION_REGISTRY_UNAVAILABLE` | 权限注册/核对不可用 |
| 503 | `SD_RUNTIME_SNAPSHOT_UNAVAILABLE` | 无可安全使用的运行快照 |
| 400 | `SD_DB_MANIFEST_INVALID` | 注册清单或派生名称不合法 |
| 400 | `SD_DB_PROVIDER_UNSUPPORTED` | 环境不支持该 Provider |
| 409 | `SD_DB_ARTIFACT_INVALID` | 迁移产物不在允许列表或 checksum/签名无效 |
| 409 | `SD_DB_PLAN_EXPIRED` | 计划已过期 |
| 409 | `SD_DB_PLAN_DRIFT` | 目标状态或输入在计划后发生变化 |
| 409 | `SD_DB_APPROVAL_REQUIRED` | 缺少匹配的生产审批 |
| 409 | `SD_DB_BACKUP_REQUIRED` | 缺少匹配且已验证的备份证据 |
| 409 | `SD_DB_OPERATION_CONFLICT` | 幂等键冲突、同目标操作冲突或锁超时 |
| 409 | `SD_DB_OPERATION_NOT_CANCELLABLE` | 当前阶段不可安全取消 |
| 409 | `SD_DB_TARGET_MISMATCH` | 数据库身份或当前版本与注册不符 |
| 503 | `SD_DB_SECRET_UNAVAILABLE` | 必需 Secret 无法安全解析 |
| 503 | `SD_DB_MIGRATION_FAILED` | 迁移失败且未证明可安全恢复 |
| 409 | `SD_INIT_SEED_CHECKSUM_DRIFT` | 相同 SeedKey/Scope/Version 出现不同 checksum |
| 409 | `SD_INIT_SEED_DEPENDENCY_UNSATISFIED` | 迁移版本或 SeedKey 依赖未满足 |
| 409 | `SD_INIT_SAMPLE_ENVIRONMENT_FORBIDDEN` | EnvironmentSample 禁止进入当前环境 |
| 409 | `SD_INIT_ADMIN_DATA_CONFLICT` | 种子将覆盖管理员维护数据，必须使用显式 DataPatch |
| 503 | `SD_INIT_BOOTSTRAP_SECRET_MISSING` | SecretBootstrap 缺少服务自有 Secret |
| 503 | `SD_INIT_INITIALIZER_FAILED` | 服务 initializer 失败，仅允许脱敏诊断 |
| 503 | `SD_DB_NOT_READY` | 目标未达到 exact desired state |
| 400 | `SD_DB_TOPOLOGY_UNSUPPORTED` | 不支持或未知的拓扑 Mode |
| 400 | `SD_DB_SHARED_TARGET_MISSING` | 缺少 Shared target |
| 400 | `SD_DB_SERVICE_MAPPING_MISSING` | 缺少 PerService 的服务物理映射 |
| 409 | `SD_DB_SHARED_ENVIRONMENT_FORBIDDEN` | Shared 不允许用于非 Development 环境 |
| 409 | `SD_DB_TOPOLOGY_DRIFT` | 已有数据的拓扑或物理映射发生变化 |

---

# 10. 页面与交互设计

PF-02 管理能力只提供 PC 页面；PDA/Mobile 不复制后台。三端仅消费发布导航、有效功能开关和主题策略。页面复用 `48c5374` 已实现的 PF-01 公开组件契约，不复制平台壳内部实现。

## 10.1 路由

| 路径 | 权限 | 页面 |
| --- | --- | --- |
| `/pc/systemdata/organizations` | `systemdata.organization.view` | 行政组织与岗位 |
| `/pc/systemdata/assignments` | `systemdata.assignment.view` | 用户任职 |
| `/pc/systemdata/navigation` | `systemdata.navigation.view` | 导航与资源发布 |
| `/pc/systemdata/features` | `systemdata.feature.view` | 功能开关 |
| `/pc/systemdata/services` | `systemdata.service-catalog.view` | 服务目录 |
| `/pc/systemdata/themes` | `systemdata.theme-policy.view` | 租户主题策略 |
| `/pc/systemdata/service-initialization` | `systemdata.service-initialization.view` | 服务/模块注册、SeedSets、计划与 Operation；旧路由重定向兼容 |

所有路由使用稳定 route name、PermissionNId 和 `workspace: 'business'`；通过 PF-01 `NavigationGroup` 接入，不直接修改平台外壳内部状态。

## 10.2 服务初始化编排页

- 五个页签：服务/模块注册、SeedSets、计划、Operation、环境策略；默认先展示生产门禁和失败/运行中操作，并显示拓扑 Mode/revision 与逻辑到物理映射（从不显示连接信息）。
- 计划详情以步骤、风险、目标版本、checksum、过期时间和 drift 状态展示；不显示完整 SQL、地址、连接串、SecretRef 或原始迁移输出。
- 生产 apply 操作区按顺序展示审批、备份证据和最终确认；任何证据缺失或不匹配时按钮禁用并显示稳定错误码。
- Operation 展示 Queued/Running/终态以及 SchemaMigration、RequiredSeed、SecretBootstrap、Verify 阶段和各 ModuleKey 的脱敏 observation；只在安全阶段显示取消。
- 页面不得提供任意 SQL、任意产物路径、服务器地址、角色名或 Secret 输入框。

## 10.3 行政组织与岗位页

布局：`AppTreeTableLayout` 左侧组织森林，右侧组织详情和岗位表；租户虚拟根只在 UI 显示，不可编辑或发送给 API。

- 树节点显示类型、名称、状态；支持状态过滤和按权限显示操作。
- 创建子节点时只展示父类型允许的类型。
- 移动使用专用抽屉：先调用 move-preview，展示原父、目标父、子树节点数、岗位数、当前/未来任职数及 OrganizationRevision；提交前二次确认。
- 预览过期返回 409，页面必须重新预览，不能静默重试移动。
- 停用被依赖阻止时显示依赖分类数量和下一步链接，不自动级联。
- 岗位在选中组织下分页展示；岗位所属组织只读，跨组织调整通过“目标岗位创建 + 任职调整”完成。

## 10.4 用户任职页

- 用户搜索由 Identity 用户目录适配器提供；契约不可用时页面显示“用户目录暂不可用”，禁止提交，不降级为手输未知 UserNId。
- 当前、未来、历史、已取消使用明确标签，不以颜色作为唯一状态表达。
- 创建任职选择岗位后只读展示组织；不允许客户端提交不一致 OrganizationNId。
- 主任职切换显示生效时间及将被结束/拆分的区间摘要，提交后刷新完整时间线。
- Current 记录只提供结束；Scheduled 可编辑或取消；Ended/Cancelled 只读。
- 409 区间或主任职冲突展示服务端结构化冲突范围，不根据中文 message 判断。

## 10.5 导航与资源发布页

- 左栏为草稿树，中间为节点属性，右侧提供 PC/PDA/Mobile 预览。
- Link 只能从 Active Page Resource 中选择；RouteName、PermissionNId 和支持终端只读来源于资源注册。
- Group 不显示权限选择；终端选择必须受资源支持范围约束。
- “验证”展示错误定位到 NodeNId；“发布”只有验证无错误且权限回执有效时可用。
- 发布前显示 DraftRevision、当前 Snapshot Revision、变更摘要和权限清单版本。
- 回滚只允许 PreviousSnapshot；明确说明回滚会生成新 revision，不覆盖历史。
- 运行预览使用与真实前端相同的权限求交集纯函数和空分组裁剪规则。

## 10.6 功能开关页

- 列表显示 FeatureNId、模块、默认值、租户覆盖、环境强制关闭和最终有效值。
- 管理员只能修改 Inherit/Enabled/Disabled 与 Reason；环境强制关闭时 Enabled 不会生效，页面明确原因。
- 不提供用户、角色、组织、百分比或时间定向入口。
- 变更需二次确认并显示受影响 Resource/Menu 数量；不伪造业务影响统计。

## 10.7 服务目录页

- Platform 与 External 分组展示名称、所有者、入口、声明健康路径、终端和状态。
- Platform 的 GatewayPathPrefix/HealthPath 只读；External 可创建/编辑 HTTPS 入口。
- 页面明确“未探测”“由 PlatformHealth 提供”而不是显示绿色健康状态。
- 外部入口打开前展示目标域名；使用 `noopener/noreferrer`，禁止 `javascript:`、协议相对和含凭据 URL。
- Owner User 查询不可用时保留已有显示快照但禁止绑定新的未知 UserNId。

## 10.8 主题策略页

- 只展示工业青、科技蓝、中性灰；light、dark、system；comfortable、compact。
- 每组至少选择一项；默认值只能从允许集合选择。
- 页面提供代表性预览，但不复制 PF-01 主题 Token 或实现任意色板。
- 保存后运行适配器重新获取策略；现有用户值被禁止时提示使用租户默认。

## 10.9 页面状态与可访问性

- 所有页面覆盖 Loading、Empty、Error、Permission、Degraded、Concurrency 六类状态。
- 使用 PF-01 `AppQueryPanel`、`AppTreeTableLayout`、`AppFormDrawer` 和状态组件；若实际公开 API 与实施 04 有偏差，先记录契约偏差，不在 PF-02 重构 PF-01。
- 键盘可完成树导航、表格操作、抽屉、发布和回滚；焦点在关闭后返回触发点。
- 1280×720、1440×900 无页面级横向滚动；200% 缩放仍可访问主要操作。
- 只有真实 TraceId 存在时显示；不显示数据库 Id、完整缓存键或敏感 URL。

---

# 11. 错误、安全、审计、缓存与可观测性

## 11.1 认证、授权与租户

- 所有 API 默认认证；运行端点从 JWT 获取 TenantNId，不接受 Request TenantNId。
- 管理 API 使用 PermissionNId Policy，不以角色名、菜单文字或前端按钮状态授权。
- SystemData 不实现“管理员角色全部放行”后门。
- 当前主体上下文不兼容时，真实适配任务阻塞而不是把 UserNId 强制转 Guid。
- 跨租户 NId、内部 Id 和查询统一返回 404；日志可以记录租户不匹配安全事件但不回显对象存在性。

## 11.2 Identity 协作与降级

| 场景 | 行为 |
| --- | --- |
| Identity 用户目录不可用 | 任职和技术负责人新写入 fail-closed；读取使用已有显示快照并标记可能陈旧 |
| 权限注册/核对不可用 | 已发布导航继续读取；引用未验证权限的新发布 fail-closed |
| 当前用户 PermissionNIds 更新 | 前端 AuthStore 更新后立即重新求交集；SystemData 不保存用户权限副本 |
| 权限被撤销但客户端未刷新 | 菜单可能短暂陈旧，但 Router Guard/目标 API 必须独立 403；不得据此扩大后端访问 |
| Identity 事件重复/乱序 | 按 eventId 去重和版本丢弃旧投影；定时对账修复漏消息 |

## 11.3 本地审计

PF-04 前由 `system_data_operation_audit` 追加记录，并与业务写、Outbox 同事务：

- 组织创建、移动、启停。
- 岗位创建、启停。
- 任职创建、修改未来区间、结束、取消和主任职切换。
- 清单应用、导航发布和回滚。
- 功能覆盖修改。
- 服务目录入口、所有者和状态修改。
- 主题策略修改。

审计字段包括 ActorUserNId、Action、ObjectType/ObjectNId、Reason、Before/After 摘要和 TraceId。禁止记录 Token、权限全集、完整用户目录响应、任职页面查询条件中的敏感文本或外部 URL 凭据。

PF-04 Audit 上线后通过 Outbox 消费这些事实；不得跨模块写 Audit Repository。高风险查看行为由 PF-04 另行设计，本阶段不提前实现。

## 11.4 缓存与一致性

Redis Key：

```text
systemdata:organization-tree:{tenantNId}:v{revision}
systemdata:navigation:{tenantNId}:{terminal}:v{revision}
systemdata:features:{tenantNId}:v{revision}
systemdata:service-catalog:{tenantNId}:v{revision}
systemdata:theme-policy:{tenantNId}:v{revision}
systemdata:revision:{tenantNId}:{area}
```

规则：

- 数据库是权威；缓存只保存版本化只读投影，不缓存写模型或用户权限。
- revision 指针 TTL 固定 30 秒；版本化快照 TTL 固定 15 分钟。
- 写事务提交后推进 revision、写 Outbox，再删除旧 revision 指针；删除失败记录告警，版本化键防止新读命中旧内容。
- 缓存未命中或 Redis 不可用时回源 PostgreSQL；使用 BuildingBlocks 分布式锁抑制同一快照击穿，锁不可用时允许有限并发回源。
- PostgreSQL 不可用而 Redis 有缓存时，运行导航/功能/目录/主题只可使用生成时间不超过 5 分钟的最后快照，并返回 `degraded=true`；超过 5 分钟或无快照返回 503。
- 管理读写、发布、移动、任职和权限核对从不使用陈旧缓存完成决策，数据库不可用时 fail-closed。
- 前端在登录完成、终端切换、PermissionNIds 变化、窗口重新获得焦点及每 5 分钟重验证运行快照；ETag 未变化返回 304。
- 环境紧急 Disabled 清单在每个消费进程本地配置求值，优先于缓存，避免中央服务故障阻止紧急关闭。

## 11.5 失败恢复与对账

- Outbox 指数退避重试，上限后进入可查询失败状态并触发指标；不静默删除。
- 每 15 分钟核对 Identity 权限注册回执和用户显示快照版本；这里只定义 SystemData reconciliation handler，调度由本服务 BackgroundService 临时承载，PF-07 Scheduler 上线后迁移注册方式，不把 Scheduler 领域提前放入 PF-02。
- 对账只修复投影和标记异常，不自动创建 Identity 权限、不自动更换任职 UserNId。
- 导航快照 checksum 不匹配立即拒绝服务该版本并回退 PreviousSnapshot；回退动作产生安全告警和审计，不能静默覆盖。

## 11.6 服务目录安全

- External URL 解析后执行 scheme、userinfo、host、DNS/IP 和重定向禁止规则；PF-02 服务端不主动请求该 URL。
- Platform HealthPath 仅保存相对路径；真实目标主机由受信任部署配置组合，管理员无法输入探测主机。
- 返回前端的 Owner 快照限制长度并按纯文本编码；不支持 HTML。

## 11.7 可观测性

结构化日志包含：Service、Module、TenantNId、ActorUserNId（可用时）、Operation、ObjectNId、Revision、TraceId；不把 UserNId、RouteName 或 FeatureNId 作为默认高基数指标标签。

指标：

```text
systemdata_http_requests_total / duration
systemdata_navigation_publish_total / failures
systemdata_runtime_snapshot_cache_hit_ratio
systemdata_runtime_snapshot_degraded_total
systemdata_identity_directory_failures_total
systemdata_permission_registry_failures_total
systemdata_outbox_pending / retry_total / dead_total
systemdata_assignment_conflicts_total
```

健康：

- `/health`：进程静态响应。
- `/health/live`：不检查外部依赖。
- `/health/ready`：PostgreSQL 必需；Redis/RabbitMQ/Identity 分别报告依赖状态和降级，不泄露连接信息。
- PostgreSQL 不可用时 readiness Unhealthy；Redis/RabbitMQ 失败是否降级由检查标签表达，Gateway 聚合沿用现有规则。

## 11.8 服务初始化安全、审计与可观测性

- 注册/SeedSets 变更、plan 完成、审批、备份证据、apply、migration/seed/bootstrap、取消、超时、重试、锁冲突和每次状态转换均写本地追加审计；高权限操作必须记录 Actor、理由、PlanChecksum、OperationId 和 TraceId。
- 审计与 Operation 只保存脱敏摘要；种子内容、Secret 值、连接串、服务器地址、SQL、命令、完整异常和 initializer stdout/stderr 禁止进入日志、Trace、事件或数据库。
- Operation 状态机是权威，不缓存计划/执行写模型；管理查询可使用短 TTL 投影，但 apply、审批、备份与取消必须读数据库并校验 revision/checksum。
- 指标至少包括 `systemdata_db_orchestration_queued/running/failed_total`、`duration_seconds`、`lock_wait_seconds`、`retry_total`、`plan_drift_total`、`not_ready_total`，标签只用 EnvironmentKind、Provider、Phase、Status，不使用数据库名或 ServiceKey 造成高基数泄露。
- 编排 API、事件和审计仅暴露安全的逻辑/物理身份元数据与 topology revision；绝不返回连接串、SQLite 路径、地址或凭据。
- SystemData readiness 在自身数据库身份或迁移版本不符合时失败；对目标业务服务的失败只影响该服务的 readiness/Operation，不应令 SystemData 控制面整体失活。

---

# 12. 迁移、初始化、测试与验收设计

## 12.1 迁移步骤

迁移账本 `system_data_schema_migrations` 在解析后的 SystemData 物理目标运行，步骤按顺序幂等执行：

```text
SDM-001 环境策略、服务/模块注册与 Initialization SeedSet 元数据
SDM-002 不可变计划、计划步骤、审批与备份证据
SDM-003 Operation/Step、迁移/种子观察与 SystemData 自身 seed ledger
SDM-004 组织表及自引用约束
SDM-005 岗位表及组织复合外键
SDM-006 任职表、区间检查和索引
SDM-007 模块清单与 UI Resource
SDM-008 导航集、草稿节点、快照与快照节点
SDM-009 功能定义与租户覆盖
SDM-010 服务目录
SDM-011 租户主题策略
SDM-012 投影修订、本地操作审计与 Outbox
SDM-013 默认租户 SystemData 权限声明、资源和导航草稿种子
SDM-014 默认租户 SystemData 功能定义种子
SDM-015 默认租户 SystemData.Service 目录条目种子
SDM-016 默认租户 PF-01 合法主题策略种子
```

- 每步 `BeginTran → Apply → 记账 → Commit`；失败回滚且不记账。
- PostgreSQL 18 基础设施将稳定逻辑 `systemdata_db` 解析为 Shared Development 的 `industrial_platform_dev`（并在那里运行 `system_data_schema_migrations`）或 PerService 的物理 `systemdata_db`（唯一引导例外），再创建最小角色；SystemData 自有 migration runner 执行 SDM-001～016。不得通过 DatabaseOrchestration API 自编排，不得使用 `EnsureCreated`。
- PostgreSQL 验证 `uuid/timestamptz/boolean/snake_case`、部分唯一索引、复合外键和 `ON UPDATE CASCADE`。
- SQLite 作为快速集成替身，连接启用 Foreign Keys；PostgreSQL advisory lock、时间区间并发和真实 DDL 必须在真库验收。
- 默认租户 NId 来自显式配置；没有配置时不创建固定生产租户。Development 可以使用 `development`，但不创建虚假组织、用户或服务健康数据。
- SDM-013～016 属 SystemData 自身 `SystemBaseline/TenantBaseline`，必须写 `system_data_seed_ledger`；不创建 EnvironmentSample 公司、岗位、任职或伪健康状态。删除/修正使用显式 DataPatch，不覆盖管理员维护数据。

## 12.2 测试层次

| 层次 | 具体范围 |
| --- | --- |
| Domain | InitializationManifest/SeedSets、四类种子、计划/Operation/双账本状态机与门禁，以及既有 SystemData 领域不变量 |
| Application | 初始化幂等/依赖/审批/备份/readiness/observation、租户隔离、权限和既有应用用例 |
| Infrastructure | PostgreSQL 18 自举、显式迁移/种子、可靠队列、lease/heartbeat、受控 initializer、最小角色、产物校验、PG/SQLite、advisory lock、缓存/Outbox |
| API | 所有端点、信封、400/401/403/404/409/503、ETag、no-store、OpenAPI |
| Contract/Event | DTO JSON、Permission/Resource Manifest、事件 v1、敏感字段扫描、兼容夹具 |
| Frontend Component | 七个管理页面、权限、抽屉、树、发布、编排门禁、冲突和降级状态 |
| E2E | 管理员完成数据库 plan/apply、组织/岗位/任职、导航发布、开关、目录、主题以及运行端消费 |

以上适用层次收敛在 `IndustrialPlatform.SystemData.Tests` 服务级项目；真实 PostgreSQL、Redis、RabbitMQ 和跨服务链路进入统一 `IndustrialPlatform.IntegrationTests`。目录用于定位能力，不再为每个生产技术层创建独立测试项目。

## 12.3 核心测试矩阵

服务初始化编排：

- 首次初始化完成 provision、角色、SchemaMigration、RequiredSeed、按需 SecretBootstrap、Verify 和 Healthy。
- 相同 manifest 重复 apply 幂等；多副本通过 advisory lock + ModuleKey/Scope + 双账本只执行一次。
- 迁移/种子版本升级追加执行；相同 key/scope/version 不同 checksum drift 拒绝。
- SchemaMigration、RequiredSeed 或 initializer 部分失败只从可验证账本边界重试。
- 缺必需 Secret 时 SecretBootstrap 失败且消费者 NotReady；SystemData API/表/log/Trace/audit/event 不出现 Secret 值。
- Production 缺审批或备份证据拒绝；完整 `plan→approval→backup→apply→verify` 成功，禁止启动时自动播种。
- EnvironmentSample 仅 Development/Test 显式启用，Staging/Production registration/plan/apply 均拒绝。
- 共享物理库多个 ModuleKey 的表前缀、schema/seed ledger、SeedKey/checksum 和锁范围隔离。
- 重复 SystemBaseline/TenantBaseline 不覆盖管理员维护数据；删除/修正只接受显式 DataPatch、风险和恢复说明。
- SystemData 不可用时，已初始化服务按本地事实继续 Ready；本地 initialization 未达期望时消费者 NotReady，不得回退 SQLite、Mock、默认密码或旧 Schema。
- SystemData 自身仅经 PostgreSQL 18 最小引导、本地迁移和最小 SystemBaseline 建立，无循环 API；全仓无 `EnsureCreated`。
- 简单签名 SQL seed bundle 与服务 initializer bundle 均执行 allowlist/checksum/签名校验，任意 SQL/路径/命令/API 输入拒绝。
- 拓扑验收（十项）：(1) Shared SQLite Development 默认；(2) Shared PostgreSQL 只 provision 一次、服务迁移独立；(3) Development PerService；(4) 非 Development Shared 拒绝；(5) 缺失/非法 target 或 mapping 拒绝；(6) 同物理目标 DDL 串行；(7) 单服务失败隔离并 NotReady；(8) 已有数据拓扑变更 drift 拒绝；(9) API/事件/日志无凭据泄露；(10) 无 `EnsureCreated` 且业务 API 不创建数据库。

组织/岗位：

- 四类型全部允许/禁止父子组合，多根公司和租户虚拟根不落库。
- 同租户移动、跨根公司移动、跨租户拒绝、祖先循环、并发预览过期。
- 有活动依赖时停用拒绝；不存在依赖时自下而上停用/恢复。
- 岗位同组织名称冲突、跨组织同名允许、岗位不可移动。

任职：

- 当前/未来/历史/取消投影；区间边界使用左闭右开。
- 同用户岗位区间重叠、两个主任职、存在任职但无主任职均被拒绝。
- 兼任、未来调岗、主任职原子切换、最后任职结束和用户未任职。
- 两实例并发写由 PostgreSQL advisory lock 串行化。
- Identity 用户不存在、Disabled、目录超时和显示快照陈旧。

菜单/权限：

- Group/Link、资源类型、路由、终端子集、Feature 引用、循环和空分组。
- 清单幂等、版本升级、相同版本不同 checksum 冲突、退休资源。
- Identity 注册回执缺失/过期时发布拒绝；现有快照继续服务。
- 发布原子切换、快照不可变、Previous 回滚、checksum 损坏恢复。
- 前端 PermissionNIds 求交集、权限变化重算、直接路由仍 403。

开关/目录/主题：

- 环境 Disabled > 租户覆盖 > 默认；Retired 强制 Disabled。
- Platform/External URL 白名单和 SSRF 输入拒绝；目录不返回伪健康。
- 三套 palette、三种 mode、两种 density 集合非空和默认包含。
- 用户偏好被策略禁止时收敛到租户默认。

可靠性：

- Redis 不可用回源数据库；DB 不可用时 5 分钟缓存降级和超期 503。
- Outbox 重复、乱序、重试、死信指标和消费者幂等。
- 审计与业务原子提交，敏感字段排除。
- TenantNId 伪造、跨租户 NId、无权限和主体 UserNId 类型偏差。

## 12.4 关键 E2E

1. 从 PostgreSQL 18 空环境最小引导 SystemData，本地执行 SDM-001～016 和最小 SystemBaseline，证明双账本、无循环 API 与无 `EnsureCreated`。
2. 注册含两个 ModuleKey 的测试宿主，在 Development 完成异步 plan/apply、最小角色、迁移、RequiredSeed、可选样例和 exact-version readiness。
3. 在 Production 验证审批/备份、EnvironmentSample 拒绝、SecretBootstrap 缺 Secret、并发单执行、checksum drift、管理员数据保护和消费者 NotReady。
4. 创建两个根公司，建立部门/科室/班组和岗位，跨根公司移动部门并验证 NId、后代、岗位和任职保持。
5. 创建主任职和兼任，安排未来调岗，原子切换主任职并查询历史时间线。
6. 注册 SystemData 内置资源与权限回执，编辑草稿、三终端预览、发布、权限求交集和回滚。
7. 关闭 Feature 后菜单入口消失且直接 API 仍执行授权/功能校验；恢复后按 revision 更新。
8. 管理服务目录和主题范围；运行端不显示伪健康，PF-01 适配器应用租户默认和允许范围。
9. Redis/Identity 降级、并发冲突、403、503 和 TraceId 页面行为正确。

## 12.5 稳定验证命令与证据

后端目标命令：

```powershell
dotnet restore src/backend/IndustrialPlatform.slnx
dotnet build src/backend/IndustrialPlatform.slnx --no-restore
dotnet test tests/SystemData/IndustrialPlatform.SystemData.Tests/IndustrialPlatform.SystemData.Tests.csproj --configuration Release
dotnet test tests/IntegrationTests/IndustrialPlatform.IntegrationTests/IndustrialPlatform.IntegrationTests.csproj --configuration Release --filter "Category=Integration"
```

前端目标命令：

```powershell
Set-Location src/frontend
pnpm format:check
pnpm lint
pnpm typecheck
pnpm test:unit:coverage
pnpm build
pnpm test:e2e
```

每条证据记录命令、退出码、通过/失败/跳过数量、覆盖率、报告/截图路径、依赖提交和外部限制。`961cad4` 的测试资产不是本轮新鲜执行证据；PostgreSQL/Redis/RabbitMQ 或真实环境未验证的项目只能标记“待验收”。

---

# 13. 开发任务依赖

```text
TASK-SD-001 服务骨架、SystemData 自身最小引导与迁移边界（961cad4 已实现，待验收）
    → TASK-SD-002 数据库注册、Plan、审批、备份与 Operation 控制面基线（961cad4 已实现，待验收）
        → TASK-SD-003 通用初始化 Manifest/双账本/Runner、migration/seed/bootstrap（61753dc 已有 migration-only 基线；扩展待派遣）
            → TASK-SD-004 消费服务握手、NotReady 契约与初始化验收夹具

TASK-SD-004
    ├→ TASK-SD-005 组织、岗位、任职领域与持久化
    │      └→ TASK-SD-006 组织、岗位、任职应用与 API
    ├→ TASK-SD-007 资源、权限清单与导航发布
    ├→ TASK-SD-008 功能开关
    └→ TASK-SD-009 服务目录与主题策略

TASK-SD-006 + 007 + 008 + 009
    → TASK-SD-010 缓存、审计、Outbox、对账与真实 Identity 适配

TASK-SD-007 + 008 + 009
    + 已满足基线：PF-01/Identity `48c5374`
    → TASK-SD-011 前端运行适配器

TASK-SD-002 + 003 + 006 + 007 + 008 + 009
    + 已满足基线：PF-01/Identity `48c5374`
    → TASK-SD-012 PC 管理页面（含服务初始化）

TASK-SD-004 + 010 + 011 + 012
    → TASK-SD-013 契约、E2E 与阶段验收
```

并行与冲突规则：

- 001→002→003→004 的最高优先级串行链已完成；005～010 的后端业务纵切与可靠性装配也已完成。
- 011 与 012 已完成本轮前端交付：011 消费 007～009 的 runtime 契约，012 实现 PC 管理页面；两者未反向改写已验收的后端边界。
- 003 是唯一允许使用 provision admin、调度签名 migration/seed/initializer 产物和持有目标 advisory lock 的任务；不得创建独立 Migrator/Seeder Service，SystemData 不接收业务 Secret 值。
- 010 是唯一允许接入真实 Identity、Redis、RabbitMQ 和跨领域一致性装配的任务；前置契约未稳定时保持阻塞。
- 013 只修复验收阻塞缺陷，不扩张业务范围。
- 任何任务不得触碰实施 15、Identity/PF-01 受保护文件或其他阶段模块；跨阶段契约只做双方批准的精确增量。

---

# 14. 开发任务拆分

任务卡设计已经用户批准，并按当前 `develop` 校准：001～013 完成，011 保留 runtime 降级前提。完成状态不构成 PF-03 自动启动授权。

## TASK-SD-001 创建服务骨架与 SystemData 自身最小引导边界

**状态：** 已完成

**目标：** 创建 `SystemData.Service` 五层项目和测试基线，实现受信任 topology options/resolver，冻结 PostgreSQL 18 基础设施最小引导、三类分权连接、SystemData 自有显式迁移、无循环自编排和 readiness 边界。

**输入文档：** 本文第 1～7.1.1、8、9.1、11.7～11.8、12.1 节；蓝图 32、33 V2.0；提交 `961cad4`；现有 Gateway 与受保护的部署/Runner 并行状态。

**依赖：** BuildingBlocks、可运行基线历史已完成；无 PF-02 前置任务。

**允许修改范围：** 新建 `src/backend/src/Services/SystemData/**`、`tests/SystemData/**`；修改解决方案、BuildingBlocks 架构测试、Gateway 服务配置/测试和经协调后的 SystemData/PostgreSQL 引导配置。禁止覆盖当前并行 `deploy/cloud-dev/**`、实现业务领域、修改 Identity/ReferenceData、前端、实施 15 或后续同宿主模块。

**预期输出：** 五层项目与引用测试、Contracts 零引用、Gateway 路由/OpenAPI、稳定逻辑 `systemdata_db` 的引导边界（Shared Development 物理 `industrial_platform_dev` 并运行 `system_data_schema_migrations`；PerService 物理 `systemdata_db`）及 owner/migrator/runtime 最小角色、SystemData 自有 runner、显式迁移、无 `EnsureCreated`、数据库身份/版本 readiness 和空 DatabaseOrchestration DI 扩展点。

**验证与证据：** 先写架构/配置/路由/自举/迁移失败测试；在 PostgreSQL 18 空环境验证最小引导后 SDM-001～016 可执行、普通 runtime 无 DDL 权限、自身不调用编排 API、SQLite/PG 均显式迁移；全仓扫描 `EnsureCreated`，记录退出码、测试数和环境限制。

**结果回写：** 回写真实项目名、端口、数据库/账本、路由、健康检查、测试数、提交和与现有骨架模式的偏差到本文第 16、17 节。

**建议提交：** `feat(systemdata): scaffold service and bootstrap database`

---

## TASK-SD-002 实现数据库注册、Plan 与 Operation 控制面基线

**状态：** 已完成

**目标：** 交付保留 logical/physical identity 与 topology revision 的 migration-only v1 注册/查询、异步 dry-run/plan/validation、审批、备份证据、apply 入队和 Operation 状态机，作为通用初始化协议的兼容基线。

**输入文档：** 本文第 2.3、4、6、7.1.2～7.1.4、8、9.1～9.2、9.6、9.8～9.9、11.1、11.3、11.8、12 节；蓝图 33；TASK-SD-001 输出。

**依赖：** TASK-SD-001。

**允许修改范围：** SystemData DatabaseOrchestration 的 Domain/Application/Contracts/Infrastructure/Api，SDM-001～003 与对应测试。只创建控制面状态、仓储和 API；禁止 provision 连接、实际迁移执行、修改业务服务迁移、创建独立服务或触碰并行部署文件。

**预期输出：** 已由 `961cad4` 提交的 DatabaseRegistrationManifestV1、EnvironmentPolicy、Plan/Step、Approval、BackupEvidence、Operation/Step、registration/plan/apply/status/cancel/readiness API、幂等/drift/生产门禁和无 Secret 事件；不把尚未实现的 SeedSets 写入本任务完成范围。

**验证与证据：** TDD 覆盖清单幂等/冲突、非法输入、异步 plan/apply、状态转换、过期/drift、审批/备份绑定、取消边界、租户/权限、OpenAPI/JSON/event 兼容和敏感字段扫描；不以 Mock Operation 证明真实迁移完成。

**结果回写：** 回写最终 DTO、字段、表/索引、状态机、API、权限、错误码、事件、测试数、提交和待 Runner 验收项。

**建议提交：** `feat(systemdata): add database orchestration control plane`

---

## TASK-SD-003 将内部 Runner 扩展为通用服务初始化 Pipeline

**状态：** 已完成

**目标：** 保留当前 provision/migrate/verify WIP，在 `SystemData.Service` 内扩展 InitializationManifest/SeedSets、双账本 observation、SchemaMigration、RequiredSeed、按需 SecretBootstrap、Verify 与 readiness，形成通用 Service Initialization Pipeline。

**输入文档：** 本文第 4.2、6、7.1.2～7.1.7、8、9.2、9.8～9.9、11.3、11.8、12 节；蓝图 33 V2.0；TASK-SD-002 与提交 `61753dc`。

**依赖：** TASK-SD-002；测试环境需 PostgreSQL 18、受控 Artifact Registry/fixture 和隔离 Secret Provider/Sink。

**允许修改范围：** 未来执行任务可增量修改 SystemData Domain/Application/Contracts/Infrastructure/Api 的 ServiceInitialization/DatabaseOrchestration、Runner、migration/seed/initializer adapters、SeedObservation/控制面迁移、配置与测试。禁止回退 `61753dc`、修改业务服务 Schema/种子内容、直写 Repository、接收/透传 Secret、Shell 拼接、独立 Migrator/Seeder Service 或 `EnsureCreated`。

**预期输出：** ServiceInitializationManifestV1/SeedSets、v1 兼容 adapter、DB-backed queue、lease/heartbeat、advisory lock、受控一次性 initializer、两类 seed bundle adapter、schema/seed observation、四类种子环境门禁、双账本幂等、DataPatch 保护、Secret Provider 隔离、provision/migration/seed/bootstrap/verify 与脱敏错误。

**验证与证据：** 覆盖第 12.3 节全部初始化门禁；记录真实 DDL/grants、测试数和敏感扫描。`61753dc` 只证明 migration-only 代码/测试资产已提交，本轮未重跑且不能替代 seed/bootstrap 验收。

**结果回写：** 回写 Runner 部署形态、时序参数、锁键算法、角色/grant、Secret/Artifact provider、超时重试、指标、测试数、提交和安全证据。

**建议提交：** `feat(systemdata): run secure database provisioning operations`

---

## TASK-SD-004 交付初始化握手、NotReady 契约与验收夹具

**状态：** 已完成

**目标：** 提供 PF-03+ 可复用的 ServiceKey/ModuleKey 注册、Operation 观察、迁移/RequiredSeed/bootstrap 校验和 NotReady 契约，并用多模块 fixture 完成 Development/Test 与 Production 初始化门禁验收。

**输入文档：** 本文第 3、7.1.7、9.2、9.9、10.2、11.8、12.2～12.5 节；蓝图 33 V2.0；TASK-SD-001～003 输出。

**依赖：** TASK-SD-003。

**允许修改范围：** SystemData Contracts、共享测试 fixture/示例宿主、SystemData 集成/E2E 测试和编排说明；仅使用测试服务自有 Schema/迁移产物。禁止改造真实业务服务、创建新生产服务、修改并行部署文件或派遣 PF-03+。

**预期输出：** ServiceInitializationManifest/SeedSets/Operation/Readiness v2 与 migration-only v1 兼容夹具、含两个 ModuleKey 的测试宿主、签名迁移/种子/initializer 产物、PG/SQLite 双账本路径、NotReady 503 和第 12 节验收报告。

**验证与证据：** 2026-08-14 新鲜验证——SystemData 5 项目 **242/242 全绿**（Application 82 / Infrastructure 60 / Domain 59 / Api 27 / Contract 14），SD-003 基线 211 无回归；新增 13 项门禁测试（握手 5、账本 5、环境门禁 4、模块隔离 1）落于 4 个验收文件，另账本执行语义 7 + PostgreSQL E2E 3（env 门控早退）；各项目 build 0 警告 0 错误；全 slnx 构建被协作方 Identity WIP 的 2 处既有错误阻断（非本任务范围）。**PostgreSQL 真实验证（云端 Docker，`SYSTEMDATA_PG_E2E=1`）`SeedLedgerPostgreSqlE2ETests` 3/3 通过**：种子账本幂等重放/版本升级追加/缺 Secret fail-closed 三语义在真实 PG `timestamptz` 上成立；跨运行幂等（多次执行不追加账本行）与自清理（`DropTestTablesAsync` 删迁移账本/种子账本/业务表，云端不留痕）一并实证。余下待验收：全 Runner 生产门禁（plan→审批→备份→apply）、Redis/RabbitMQ 真实环境。

**结果回写：** 契约 v2：`ServiceInitializationManifestV2`/`SeedSetV1`/`OperationV2`/`ReadinessV2`/`NotReadyV2`，v1 线 `SchemaMigration→"Migrate"` 映射兼容。fixture 路径：`tests/SystemData/IndustrialPlatform.SystemData.Testing/`（示例宿主 `TestInitializableService`：ModuleA=module-a 2 条 SQL 种子、ModuleB=module-b 1 条 SecretBootstrap 种子；`TestFixtureScope`/`TestArtifactWriter`/`TestTargetDatabase`/`TestSeedSets`；门禁夹具 `InitializationGateHarness`）。Runner 扩展：`OperationPhase` v2（RequiredSeed=6/SecretBootstrap=7/Verify=8）；RequiredSeed 按 SeedKey 字母序逐种子执行 + 本地账本幂等；SecretBootstrap 缺 Secret fail-closed（`SD_INIT_BOOTSTRAP_SECRET_MISSING`）。双账本 `{moduleKey}_schema_migrations`/`{moduleKey}_seed_ledger`。验证中修复：Gate05 种子命名强制字母序（成功种子先记账、失败种子不记账）；OperationPhase 7→9 阶段数断言 4 处改引 `DatabaseProvisionOperation.AllPhases.Length`；PG E2E 测试表名改用 `SeedLedgerContracts.TableName`/`MigrationLedgerContracts.TableName`（`SanitizeModuleKey` 转安全标识，`module-pg-e2e` 连字符致 PG 语法错误 42601）；Infrastructure 程序集补 `InternalsVisibleTo("IndustrialPlatform.SystemData.Infrastructure.Tests")`；幂等重放时间戳断言改瞬间容差比较（PG `timestamptz` 微秒截断 + 会话时区偏移）。**PostgreSQL 真账本语义已验证（云端 Docker，3/3）**；待验收：全 Runner 生产门禁、Redis/RabbitMQ 真实环境。提交待确认。

**建议提交：** `feat(systemdata): deliver initialization handshake and readiness fixtures (SD-004)`

---

## TASK-SD-005 实现行政组织、岗位与任职领域及持久化

**状态：** 已完成

**目标：** 按已确认规则实现 AdministrativeOrganization、Position、UserAssignment 聚合、时间区间、主任职、仓储和 SDM-004～006 迁移。

**输入文档：** 本文第 2.3、6、7.2～7.4、8、12.1～12.3 节；BuildingBlocks Entity 生命周期。

**依赖：** TASK-SD-004。

**允许修改范围：** SystemData Domain/Application 的 Organizations/Positions/Assignments 端口，Infrastructure 对应表模型、映射、仓储、SDM-004～006 迁移及 Domain/Infrastructure 测试。禁止创建 Controller、前端页面、Identity 调用或其他模块表。

**预期输出：** 四类型组织树与父子矩阵、多根公司、移动/停用规则、组织专属岗位、时间化多任职和主任职历史拆分、双版本并发、复合外键、按 UserNId advisory lock 端口、SQLite 替身和 PostgreSQL DDL。

**验证与证据：** TDD 覆盖第 12.3 节组织/岗位/任职全部矩阵；SQLite 验证外键/过滤/映射，PostgreSQL 验证 advisory lock 和并发区间；记录命令、测试数和真库限制。

**结果回写：** 回写最终类名、字段、状态、允许转换、表/索引/迁移标识、锁算法、测试数、提交和偏差。

**建议提交：** `feat(systemdata): add organization position and assignment domain`

---

## TASK-SD-006 实现组织、岗位与任职应用用例和 API

**状态：** 已完成

**目标：** 纵向交付组织森林、岗位、用户任职、移动预览、主任职切换、权限、审计端口和第 9.2 节 API，不接入未稳定的真实 Identity endpoint。

**输入文档：** 本文第 3、7.2～7.4、9.1、9.3、9.9、10.3～10.4、11.1～11.3、12 节；TASK-SD-005 输出。

**依赖：** TASK-SD-005。

**允许修改范围：** SystemData Application/Api 的 Organizations/Positions/Assignments、Contracts DTO、OpenAPI 和对应 Application/Api/Contract 测试；Infrastructure 只允许补充查询实现。禁止修改 Identity、真实前端、导航/功能/目录/主题实现。

**预期输出：** 第 9.2 节全部端点和 DTO、过滤/分页、移动预览 revision、结构化冲突、`IIdentityUserDirectory` 应用端口与测试替身、PermissionNId Policy 声明、本地审计命令和契约夹具。

**验证与证据：** 覆盖成功、400、401、403、404、409、503、租户伪造、双版本、用户目录失败、响应无数据库 Id；生成 OpenAPI/JSON 契约报告，并以 `48c5374` 的真实 Identity 契约做联合验证。

**结果回写：** 回写最终路由、DTO、权限码、错误码、分页/过滤、Identity 端口、测试数、提交和待验收项。

**建议提交：** `feat(systemdata): expose organization and assignment APIs`

---

## TASK-SD-007 实现资源清单、权限协作与导航发布

**状态：** 已完成（实现提交 `1427d18`；验收证据见第 16 节）

**目标：** 实现 ModuleManifest、UiResource、NavigationSet 草稿/验证/不可变快照/回滚和候选导航契约，冻结与 Identity 权限注册的明确边界。

**输入文档：** 本文第 2.3、7.5～7.6、8、9.4、9.6、10.5、11.2、12 节；PF-01 Navigation/Route Meta 已批准契约；Identity PermissionCatalog 现有实现。

**依赖：** TASK-SD-004。

**允许修改范围：** SystemData Domain/Application/Contracts/Infrastructure/Api 的 Resources/Manifests/Navigation，SDM-007～008 和 SDM-013 中对应种子、测试。只允许增加跨服务契约夹具，不修改 Identity 或前端实现。

**预期输出：** 版本化清单和 checksum 幂等、ResourceNId/PermissionNId 分离、SystemData 29 项权限声明（含 v1/v2 初始化兼容权限）、草稿树、三终端约束、完整发布校验、不可变快照、Previous 回滚、runtime navigation/ETag 和前端权限过滤纯契约。

**验证与证据：** 覆盖清单冲突、未知/退休资源、父子循环、路由/终端/功能/权限回执、原子发布、快照不可变、回滚、checksum 损坏、候选导航 JSON 和敏感字段扫描。真实 Identity 注册保持待验收。

**结果回写：** 回写清单/资源/快照类型、表、路由、权限、事件输入、版本/checksum、测试数、提交和 Identity 前置。

**建议提交：** `feat(systemdata): add resource registry and navigation publishing`

---

## TASK-SD-008 实现功能开关定义与租户覆盖

**状态：** 已完成（实现提交 `1427d18`；验收证据见第 16 节）

**目标：** 实现模块声明功能、Inherit/Enabled/Disabled 租户覆盖、环境强制关闭、有效快照和版本化契约，不引入用户/角色定向。

**输入文档：** 本文第 7.7、8、9.5、10.6、11.4、12 节。

**依赖：** TASK-SD-004。

**允许修改范围：** SystemData Features 的 Domain/Application/Contracts/Infrastructure/Api、SDM-009、SDM-014 和对应测试。禁止实现 Scheduler、实验分流、角色/用户目标或修改业务模块。

**预期输出：** FeatureDefinition/Override、固定优先级、Retired 语义、FeatureRevision、管理/runtime API、ETag、环境 Disabled 配置解析、结构化审计输入和事件数据。

**验证与证据：** 覆盖全部优先级组合、非法清单、退休、租户隔离、并发、权限、环境强制关闭、runtime JSON、ETag 和 Redis 未接入前的数据库读取。

**结果回写：** 回写字段、配置键、优先级、API、权限、revision、测试数、提交和不支持范围。

**建议提交：** `feat(systemdata): add tenant feature controls`

---

## TASK-SD-009 实现服务目录与租户主题策略

**状态：** 已完成（实现提交 `1427d18`；验收证据见第 16 节）

**目标：** 实现安全的 Platform/External 服务目录和 PF-01 可消费的租户主题默认/允许范围，明确目录声明与真实健康状态、租户策略与用户偏好的边界。

**输入文档：** 本文第 7.8～7.9、8、9.5、10.7～10.8、11.6、12 节；实施 04 `TenantUiDefaultsSource` 和主题类型。

**依赖：** TASK-SD-004。

**允许修改范围：** SystemData ServiceCatalog/ThemePolicy 的 Domain/Application/Contracts/Infrastructure/Api、SDM-010～011、SDM-015～016 和对应测试。禁止实现 PlatformHealth 探测、任意主题设计器或前端页面。

**预期输出：** ServiceCatalogEntry、Platform 清单字段保护、External HTTPS/SSRF 校验、Owner NId/快照、无伪健康的管理/runtime API；TenantThemePolicy、固定枚举、集合/default 不变量、PolicyRevision 和 PF-01 source DTO。

**验证与证据：** 覆盖 Platform/External 字段权限、恶意 URL、Owner 目录失败、状态、三端、主题全部合法/非法组合、租户隔离、并发、ETag 和 Contracts JSON。

**结果回写：** 回写服务/主题字段、URL 规则、API、权限、revision、PF-01 DTO 映射、测试数、提交和 PF-07 边界。

**建议提交：** `feat(systemdata): add service catalog and theme policy`

---

## TASK-SD-010 集成缓存、审计、Outbox、对账与 Identity 稳定契约

**状态：** 已完成（实现提交 `1427d18`；验收证据见第 16 节）

**目标：** 将前述领域能力接入版本化 Redis 快照、本地追加审计、Outbox、重试/对账和真实 Identity 用户/权限契约，形成可降级但不越权的后端闭环。

**输入文档：** 本文第 4、8、9.6～9.8、11、12 节；TASK-SD-002～009 输出；PF-00 恢复后批准的主体、用户目录、权限注册和事件契约。

**依赖：** TASK-SD-006、007、008、009；Identity/PF-01 历史稳定基线由 `48c5374` 满足，Identity 补强仅在涉及 admin 初始化联合验收时作为外部依赖。

**允许修改范围：** SystemData Application/Infrastructure/Api 的 Caching/Auditing/Outbox/Reconciliation/IdentityAdapters、SDM-012、配置、健康/指标和测试；只允许在 Identity Contracts 增加双方已批准的契约夹具。禁止修改 Identity Domain/Infrastructure、PF-04 Audit 实现或 PF-07 Scheduler/PlatformHealth。

**预期输出：** 第 11.4 节缓存键/TTL/5 分钟降级、全部 v1 事件、Outbox 发布与幂等、审计原子性、15 分钟对账 BackgroundService、真实字符串 UserNId 主体适配、Identity 用户目录和权限回执适配、健康与指标。

**验证与证据：** PostgreSQL/Redis/RabbitMQ 集成覆盖缓存命中/击穿/失效、DB/Redis 故障、Outbox 重复/乱序/重试、审计事务、Identity 超时/恢复/对账、主体类型和敏感扫描；记录真实依赖版本、命令、测试数和外部待验收项。

**结果回写：** 回写实际 Identity 契约路径/版本、缓存 TTL、事件路由键、对账周期、健康标签、指标、测试数、提交和降级证据。

**建议提交：** `feat(systemdata): add reliable cache events and identity integration`

---

## TASK-SD-011 接入前端运行导航、功能与主题适配器

**状态：** 已完成（代码与本地门禁；浏览器 E2E/build 待环境复验）

**目标：** 在 PF-01 和真实 AuthUser 稳定后，实现候选导航权限求交集、功能快照、TenantUiDefaultsSource、ETag 重验证和降级状态，使三端外壳消费真实 SystemData。

**输入文档：** 本文第 3、9.4～9.5、10、11.2、11.4、12 节；TASK-SD-007～009；`48c5374` 的 PF-01/Identity 公开契约。

**依赖：** TASK-SD-007、008、009；PF-01/Identity 基线已满足。

**允许修改范围：** 创建 `src/frontend/src/api/systemData/**`、`systemData/runtime/**`、`stores/systemData/**` 和对应 unit/contract/E2E；只通过 PF-01 公开端口最小修改 app 装配。禁止修改 PF-01 shell 内部、Identity auth/API/page、后端或管理页面。

**预期输出：** SystemData HTTP DTO/mapper、NavigationGroup 适配、PermissionNIds 交集和空组裁剪、Feature evaluator、TenantUiDefaultsSource、登录/权限变化/终端切换/focus/5 分钟重验证、ETag 304、5 分钟 degraded UI 和 503 回退。

**验证与证据：** 共享契约测试、权限增删响应、三终端导航、空分组、开关关闭、主题允许范围、缓存重验证、Identity/SystemData 降级、直接路由 403 和控制台敏感扫描；运行 format/lint/typecheck/unit/build 与定向 E2E。

**结果回写：** 回写最终前端类型、适配点、重验证触发、缓存/降级行为、路由映射、测试/覆盖率、截图、提交和 PF-01 偏差。

**建议提交：** `feat(frontend): connect systemdata runtime policies`

---

## TASK-SD-012 实现 SystemData PC 管理页面

**状态：** 已完成

**目标：** 使用 PF-01 平台壳与通用管理组件交付服务初始化、组织岗位、任职、导航、功能、服务和主题七个 PC 管理页面，连接真实 API 并覆盖权限、门禁、并发和降级。

**输入文档：** 本文第 9～12 节；TASK-SD-002～003、006～009；`48c5374` 的 PF-01/Identity 公开契约。

**依赖：** TASK-SD-002、003、006、007、008、009；PF-01/Identity 基线已满足。

**允许修改范围：** `src/frontend/src/pages/pc/systemData/**`、SystemData 管理 API/types/stores/components、`router/systemDataRoutes.ts`、授权导航注册和对应 unit/component/E2E。禁止修改 PDA/Mobile 管理页、Identity 页面、PF-01 shell 内部或后端。

**预期输出：** 第 10 节七页面、稳定路由/PermissionNId/workspace meta、ServiceKey/ModuleKey/SeedSets/计划/审批/备份/Operation 视图、树表/抽屉、移动预览、任职时间线、发布/回滚、功能/目录/主题表单、完整状态和关键视口截图。

**验证与证据：** 组件测试覆盖字段、权限、键盘、焦点、busy、防重、409/503；Playwright 覆盖第 12.4 节管理路径、1280×720/1440×900、200% 缩放、无横向滚动和控制台。不得使用 Mock 证明真实联合完成。

**结果回写：** 回写最终页面路由、字段、组件、权限、错误映射、截图/报告、测试数、覆盖率、提交和外部待验收项。

**建议提交：** `feat(frontend): add systemdata administration pages`

---

## TASK-SD-013 完成 PF-02 契约、E2E 与阶段验收

**状态：** 已完成

**目标：** 从 PostgreSQL 18 新环境验证 SystemData 自举、Service Initialization Pipeline、真实 Identity/PF-01 集成、三端运行策略、PC 管理路径、缓存、事件和故障降级，并形成 PF-03+ 稳定输入契约。

**输入文档：** 本文全部章节；TASK-SD-001～012 输出；Identity/PF-01 稳定提交；总 TodoList PF-02 与蓝图 33 门禁。

**依赖：** TASK-SD-004、010、011、012。

**允许修改范围：** SystemData/前端联合测试、验收脚本、README、本文执行记录、实施索引和总 TodoList PF-02 行；只修复 PF-02 验收阻塞缺陷。禁止扩张到 PF-03/04/07、修改实施 15 或重构 Identity/PF-01 所有代码。

**预期输出：** 后端全量报告、PostgreSQL 18 自举与服务初始化十三项门禁、PostgreSQL/Redis/RabbitMQ 真实联调、OpenAPI/事件/manifest 契约报告、七项管理 E2E、三端运行策略、故障矩阵、权限/审计/敏感扫描、下一阶段契约和完整执行记录。

**验证与证据：** 执行第 12.5 节全部门禁及关键 E2E；记录命令、退出码、测试数、覆盖率、耗时、报告/截图、依赖提交和外部限制。缺少真实依赖、Identity 或 PF-01 时相关项保持待验收，阶段不得标记完成。

**结果回写：** 更新本文第 16、17、18 节、实施索引和总 TodoList；记录所有任务状态、提交、最终契约、测试证据、已知偏差和 PF-03/PF-04 输入。

**建议提交：** `feat(systemdata): complete pf02 administration flow`

---

# 15. 完成标准

## 15.1 领域与数据

- PostgreSQL 18 最小引导仅创建 SystemData 数据库/角色/grant；SystemData 自有显式迁移建立控制面，无循环自编排和 `EnsureCreated`。
- Registration/InitializationManifest/SeedSets、不可变 plan、审批/备份、异步 Operation、Runner、advisory lock、幂等和 migration/seed observation 通过真库验证。
- 四类种子、ModuleKey 隔离、双账本、DataPatch、部分失败恢复和 checksum drift 规则通过验证。
- 业务服务/模块保有自身 Schema、迁移/种子/initializer 产物和双账本，生产 `plan→approval→backup→apply→verify` 门禁不可绕过。
- 行政组织四类型、父子矩阵、多根公司、跨根公司移动和无隐式级联符合已确认规则。
- 岗位组织专属；多任职、主任职、有效期和历史不可篡改规则通过并发测试。
- 所有实体 NId、TenantNId、双版本、复合外键、软删除过滤和 `timestamptz` 落地。
- 稳定逻辑 `systemdata_db`、`system_data_*` 与迁移账本独立，未创建后续模块表；Shared Development 物理使用 `industrial_platform_dev`，PerService 才物理使用 `systemdata_db`。

## 15.2 API、资源与协作

- 第 9 节 API、DTO、权限和错误码与实现/OpenAPI 一致。
- 服务/模块 registration/query、异步 plan/apply、Operation/readiness 契约稳定；API 不返回种子内容、Secret、连接地址、SQL、命令或 initializer 原始输出。
- ResourceNId/PermissionNId 分离，权限清单由 Identity 注册，SystemData 不直写 Identity。
- 菜单草稿、验证、不可变发布、回滚、三终端候选和前端权限求交集闭环通过。
- 功能、服务目录、主题策略边界明确，未伪造健康或用户偏好同步。

## 15.3 安全、审计与可靠性

- provision admin、每服务 migrator/runtime 最小权限隔离；initializer 只解析本服务 Secret Provider，SystemData 不接收/透传 Secret 并通过全链路扫描。
- 多副本同目标只执行一次；幂等、lease/heartbeat、超时、安全取消、drift 和失败恢复证据完整。
- TenantNId 只来自可信上下文，UserNId 字符串契约不再受 Guid 偏差影响。
- 管理 API 后端授权、跨租户 404、双版本并发和 SSRF 防护通过。
- 本地审计、Outbox、缓存版本、故障降级、对账、健康和指标通过。
- Redis/Identity 故障不扩大权限；无安全快照时 fail-closed。

## 15.4 前端与用户路径

- 管理员可完成服务/模块初始化 plan/审批/备份/apply/状态查询，以及组织、岗位、任职、导航发布、功能、服务和主题管理。
- PC 页面消费 PF-01 稳定组件；PDA/Mobile 不出现后台管理页面。
- 三端运行导航、功能和主题策略连接真实 SystemData；无假菜单、假健康或假 KPI。
- 401、403、404、409、503、Loading、Empty、Permission、Degraded 状态可诊断。

## 15.5 验证与范围

- 后端 restore/build/test、前端变更范围 format/lint/typecheck/unit/build/Mock E2E 退出码 0；全量 `format:check` 仍有 22 个本任务未修改的既有 Identity/页面文件告警。
- PostgreSQL/Redis/RabbitMQ、真实 Identity/PF-01 有新鲜证据；缺失项只标待验收。
- 测试服务在数据库未达 exact desired state 时保持 NotReady；远程失败不回退 SQLite。
- 未实现 File、Notification、Audit、Scheduler、PlatformHealth、制造组织、ReferenceData、租户运营或 Identity 所有能力。

---

# 16. 执行记录

| 任务 | 状态 | 执行者/任务 | 提交 | 验证证据 | 结果回写 |
| --- | --- | --- | --- | --- | --- |
| TASK-SD-001 | 已完成 | Claude Code / PF-02 | `961cad4` | 五层骨架、Gateway、拓扑、自迁移与测试资产已纳入整体构建测试 | 已合入 `develop` |
| TASK-SD-002 | 已完成 | Claude Code / PF-02 | `961cad4` | registration/plan/approval/backup/Operation 控制面已纳入整体构建测试 | 已合入 `develop` |
| TASK-SD-003 | 已完成 | Claude Code / PF-02 | `61753dc` | Runner、PG/SQLite adapter、RequiredSeed/SecretBootstrap 与双账本扩展已完成 | 已合入 `develop` |
| TASK-SD-004 | 已完成 | Claude Code / PF-02 | `05fe591` | 13 项门禁测试；PostgreSQL 种子账本云端 Docker 3/3 | 已合入 `develop` |
| TASK-SD-005 | 已完成 | Claude Code / PF-02 | `69c49b7` | 组织、岗位、时间化任职领域、持久化及测试已完成 | 已合入 `develop` |
| TASK-SD-006 | 已完成 | Claude Code / PF-02 | `1b72c6b` | Debug/Release build 0/0；SystemData 五层 492/492；OpenAPI 17 端点 | 见 `docs/evidence/TASK-SD-006.md` |
| TASK-SD-007 | 已完成 | Codex / `SystemData TASK-SD-001 至 010 实施` | `1427d18` | 清单/资源、权限回执、导航草稿/发布/回滚、三终端 runtime 与持久化测试通过 | 前端消费明确归 TASK-SD-011，管理页面归 TASK-SD-012 |
| TASK-SD-008 | 已完成 | Codex / `SystemData TASK-SD-001 至 010 实施` | `1427d18` | Feature 定义/覆盖/revision、环境强制关闭、runtime/ETag 与审计事件测试通过 | 不含角色/用户定向和前端页面 |
| TASK-SD-009 | 已完成 | Codex / `SystemData TASK-SD-001 至 010 实施` | `1427d18` | 服务目录 Owner 校验/快照、HTTPS/SSRF、平台字段保护、主题策略与持久化测试通过 | 不含 PlatformHealth 探测和前端页面 |
| TASK-SD-010 | 已完成 | Codex / `SystemData TASK-SD-001 至 010 实施` | `1427d18` | Release build 0/0；后端常规测试全绿；云 PostgreSQL/Redis/RabbitMQ 3/3；UnifiedHost 云配置启动通过 | SQL 原子审计/Outbox、Redis 降级、Identity 适配/对账、健康与指标已接入 |
| TASK-SD-011 | 已完成 | Codex / 当前任务 | 未提交 | Vitest 全量 59 文件 473/473；变更范围 Prettier、ESLint、vue-tsc、Vite build 通过；Mock Playwright 2/2 与 admin 真实运行页面通过 | `docs/evidence/PF-02.md`；未进入 PF-03 |
| TASK-SD-012 | 已完成 | Codex / 当前任务 | 未提交 | 统一表单、组织/岗位真实纵向样板与七页业务组件边界完成；默认 UnifiedHost 云依赖真实组织/岗位 CRUD 1/1 通过 | `docs/evidence/PF-02.md`；未新增 PDA/Mobile 管理页 |
| TASK-SD-013 | 已完成 | Codex / 当前任务 | 未提交 | fresh Release 0/0；后端 1298 通过、3 跳过（总计 1301）；前端门禁通过；UnifiedHost 显式模块目录与真实 CRUD 已验收 | `docs/evidence/PF-02.md`；不自动启动 PF-03 |

本表只记录实际派遣、提交和新鲜验证；设计完成不等于开发完成。

本轮 TASK-SD-011～013 与阶段联合验收均已完成，不再重复派遣。

---

# 17. 下一阶段输入契约

PF-03、PF-04、PF-07 和后续模块在 PF-02 完成后可以依赖：

```text
身份与租户
  TenantNId / UserNId（字符串 NId）

行政组织
  OrganizationNId / OrganizationType / Status / OrganizationRevision
  PositionNId / OrganizationNId / Status
  UserAssignmentNId / UserNId / PositionNId / effective interval / primary

UI 与权限
  ModuleManifestV1 / PermissionManifestV1 / PermissionRegistrationReceiptV1
  ResourceNId / ResourceType / RouteName / RequiredPermissionNId / Terminals
  RuntimeNavigationV1 / NavigationRevision / ETag

平台策略
  RuntimeFeatureSnapshotV1 / FeatureRevision
  ServiceCatalogEntryV1 / ServiceCatalogRevision
  TenantThemePolicyV1 / PolicyRevision

服务初始化与环境引导
  ServiceInitializationManifestV1 / ServiceKey / ModuleKey / SeedSets
  MigrationArtifactId/Version/Checksum + SeedKey/Version/Class/Scope/Artifact/Checksum/Signature
  RequiredForReadiness / AllowedEnvironments / DependsOnMigrationVersion / DependsOnSeedKeys / BootstrapPolicy
  EnvironmentPolicyV1 / DatabaseProvisionPlanV1 / PlanChecksum / TargetStateFingerprint
  DatabaseApprovalV1 / DatabaseBackupEvidenceV1
  ServiceInitializationOperationV1 / OperationId / Status / SchemaMigration / RequiredSeed / SecretBootstrap / Verify / TraceId
  ServiceInitializationReadinessV1 / desired+observed migration/seed/bootstrap / NotReady
  业务服务/模块自有 Schema、migration/seed/initializer 产物、<module>_schema_migrations 与 <module>_seed_ledger
  SystemData 只保存 MigrationObservation/SeedObservation，不含内容或 Secret
  migration-only DatabaseRegistration/Operation v1 兼容边界
  SystemData PostgreSQL 18 最小引导、本地 migration+SystemBaseline 和无循环 API 边界

事件
  SystemData.OrganizationChanged.v1
  SystemData.PositionChanged.v1
  SystemData.UserAssignmentsChanged.v1
  SystemData.NavigationPublished.v1
  SystemData.FeatureFlagsChanged.v1
  SystemData.ServiceCatalogChanged.v1
  SystemData.ThemePolicyChanged.v1
  SystemData.DatabaseRegistrationChanged.v1
  SystemData.DatabasePlanCompleted.v1
  SystemData.DatabaseOperationStatusChanged.v1
  SystemData.ServiceInitializationRegistrationChanged.v1
  SystemData.ServiceInitializationPlanCompleted.v1
  SystemData.ServiceInitializationOperationStatusChanged.v1
  SystemData.OperationAudited.v1
```

本轮前端消费契约补充：运行端通过统一外部前缀 `/systemdata/runtime` 读取 navigation、features、theme-policy，并以 ETag/304、权限求交集、5 分钟降级窗口和公开导航端口接入 PF-01；PC 管理端通过 `/systemdata/api/v1` 访问组织、岗位、任职、资源/导航、Feature、服务目录、主题策略和服务初始化查询/写入契约。默认 UnifiedHost 直接映射模块并剥离前缀；Gateway 仅在分布式模式承担 YARP/CORS/路由/下游健康，不加载业务模块或充当前端组合宿主。七页入口及真实组织/岗位 CRUD 已验收；PF-03 仍需用户明确启动。

后续阶段仍必须自行设计，不能从 PF-02 推断：

- ReferenceData 字典、参数、元数据、动态属性和编码规则。
- Audit 全局存储、合规查询、保留和高风险查看再审计。
- File/Notification 的数据、API、投递或扫描。
- Scheduler 任务领域和 PlatformHealth 真实探测。
- 制造组织与行政组织映射对象、MES 数据权限。
- Tenant 生命周期、套餐、配额或计费。
- 独立 Migrator/Seeder Service、任意 SQL/命令执行平台，或由 SystemData 接管业务服务 Schema、种子内容、initializer、Repository/本地账本。

同宿主后续模块只能消费公开契约/API/事件，不得引用 SystemData Repository、表或迁移账本。

---

# 18. 文档自审清单

- [x] 指定蓝图、实施 03/04、模板、CLAUDE.md、Git、代码结构和近期提交已核对。
- [x] 当前可依赖提交与后续 Identity/PF-01 稳定契约严格区分。
- [x] PF-02 只设计 SystemData，未越界到 PF-04/PF-07 或其他并行模块。
- [x] 已确认的组织、岗位、任职和菜单权限决策均已写入不变量。
- [x] 表字段未逐表重复 Entity 生命周期；NId、复合外键和跨服务引用一致。
- [x] 蓝图 33 V2.0 已将数据库编排兼容升级为 Service Initialization Pipeline，并同步总 Todo、模板、实施索引和微服务母版。
- [x] ModuleKey、InitializationManifest/SeedSets、四类种子、双账本、受控 initializer、DataPatch、Secret/锁/幂等/NotReady 与生产门禁均有稳定边界。
- [x] API、权限、事件、页面、错误、缓存、审计、迁移和测试均有稳定边界。
- [x] 十三张任务卡均且只包含统一九字段。
- [x] 任务依赖、任务卡和执行记录编号一致。
- [x] 文档明确“设计完成不等于开发完成”；本轮 011～013 已实际执行，后续真实联合验收仍单独保留。
- [x] 实施 15、Identity、PF-01 和其他并行改动未触碰。
- [x] 用户已批准本轮通用初始化调整；001～013 已完成，011 保留 runtime 降级前提。
- [x] 已明确未经用户主动要求不得进入 PF-03 或扩展其他阶段。
- [x] 本轮整改范围、测试结果、外部限制和未提交状态已回写至第 16 节及 `docs/evidence/PF-02.md`。
- [x] 真实云依赖、fresh Release、默认 UnifiedHost 新二进制与 admin 真实 CRUD 证据已补齐。
- [x] 真实 Identity/SystemData 组织与岗位 CRUD 管理纵向样板已完成复验；其余页面由契约、组件及入口矩阵覆盖。
- [ ] 提交前运行引用、占位、契约一致性、九字段和 `git diff --check` 自审。
