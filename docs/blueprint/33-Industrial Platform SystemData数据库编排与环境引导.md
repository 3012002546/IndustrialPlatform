# Industrial Platform SystemData 服务初始化编排与环境引导

版本：V3.1
状态：已确认，平台服务初始化权威母版
生效日期：2026-09-02

---

# 1. 决策与定位

PF-03 及后续服务的初始化由 `SystemData.Service` 控制面编排、由目标服务自己的初始化器执行。该能力由既有数据库编排控制面兼容升级而来，属于 PF-02 SystemData，不新增核心 Service Host，也不创建独立 Migrator/Seeder Service。

冻结所有权公式：

```text
SystemData = Topology + Orchestration + Policy + Observation
Service = Migration + Seed + Bootstrap + Verify + Ledger
Runtime readiness = local database fact
```

SystemData 负责 `Where、When、Policy、Observation`，提供 registration、拓扑解析、plan、环境策略、Operation 和脱敏观察；目标服务负责 `What、How、Fact`，拥有自己的 Schema、Migration、Seed、Bootstrap、Verify、Ledger、回滚/恢复和 runtime readiness。SystemData 不理解业务表、不直写业务 Repository、不承载其他服务迁移/种子实现，也不保存业务 Secret 值。

`Service Host`、`Domain Module`、`Initialization Unit`、`Deployment Unit` 是四个不同概念。逻辑模块不会自动成为初始化单元；只有具备独立持久化生命周期时才独立初始化。ReferenceData 当前是一个宿主、五个逻辑模块，并共享一个服务级 Migration/Ledger、一个带 `ModuleKey` 的服务级 Outbox 和基础设施；没有真实入站事件消费者时不预建 Inbox/Checkpoint。

# 2. API 与操作模型

SystemData 至少提供以下受控能力：

- `dry-run/plan`：请求目标服务初始化器检查本地事实并返回脱敏计划，不改变数据库；
- `apply/initialize`：按已授权策略调用目标服务初始化器的 `Apply`，再调用 `Verify` 获取脱敏结果；
- `operation status`：按 `OperationId` 查询排队、执行、成功、失败、取消/超时及脱敏错误；
- `service registration/query`：登记和查询服务期望状态、迁移版本、所有者与环境策略。

拓扑 provision 与服务初始化可能耗时。API 必须快速返回可追踪 `OperationId`，由内部 Runner 通过进程内或受信 HTTP 适配器调用服务初始化器；禁止用长时间同步 HTTP 持有连接等待完成。写操作必须带幂等键，同一服务、环境、目标版本和请求语义不得重复执行。

通用阶段固定为：

```text
Registration → ResolveTopology → Inspect → Plan → Apply → Verify → Observation
```

`Apply` 内部的 Migration、RequiredSeed 和 Bootstrap 顺序由目标服务实现并写入自己的 Ledger。SystemData 只记录步骤摘要和脱敏 Observation，不能以控制面记录替代本地 readiness 事实。

# 3. 声明式注册清单

新服务通过 registration/`InitializationManifest` 声明下列最小信息：

| 字段 | 含义 |
| --- | --- |
| `ServiceKey` | 稳定服务标识，不使用显示名称代替 |
| `ModuleKey` | 初始化单元标识；服务级初始化可等于 ServiceKey，只有独立持久化生命周期的模块才使用独立值 |
| `Provider` | 数据库提供程序；云端首期为 PostgreSQL，本地回退可为 SQLite |
| `DatabaseName` | 稳定的逻辑数据库身份，不包含服务器地址或凭据 |
| `DesiredVersion` | 目标服务初始化器需要达到的期望版本 |
| `Owner` | 服务/模块责任人或责任团队 |
| `DesiredState` | 期望的数据库、角色、授权和迁移状态 |
| `AutoProvision` | 是否允许自动创建；必须受环境策略约束 |
| `AutoMigrate` | 是否允许请求服务初始化器自动 Apply；必须受环境策略约束 |
| `Policy` | `Standard` 或 `Advanced` |

服务内部 SeedSet 至少声明：

```text
SeedKey / SeedVersion / SeedClass / Scope
SeedArtifactId / SeedChecksum / SeedSignature
RequiredForReadiness / AllowedEnvironments
DependsOnMigrationVersion / DependsOnSeedKeys
BootstrapPolicy
```

清单及 API/数据库不得包含种子实际内容、密码、Token、密码哈希、Secret 值、任意 SQL、任意路径/命令、真实服务器地址或数据库凭据。`SecretBootstrap` 只声明非敏感需求、环境允许条件和交付策略，不携带 Secret。

本文是可配置数据库拓扑的权威契约：共享物理存储绝不削弱服务的表前缀、迁移、Repository、API、事件或数据所有权边界。`DatabaseName` 是稳定的逻辑数据库身份（logical database identity），不因物理拓扑变更而改名。受信服务的环境配置还必须声明 `DatabaseTopology`：`Mode: Shared | PerService`、`SharedDatabaseName`、`SharedSqliteFile` 和 `ServiceDatabases:{ServiceKey}`。SystemData 将声明规范化为 `EnvironmentName`、`Mode`、`ServiceKey`、`Provider`、`LogicalDatabaseName`、`PhysicalDatabaseName`、`IsSharedPhysicalDatabase` 后再编排服务初始化器；例如 Development 的 Shared PostgreSQL 可使用 `SharedDatabaseName: industrial_platform_dev`。

# 4. 服务启动握手与就绪门禁

服务首次初始化或升级的控制面流程为：

```text
SystemData 解析拓扑与策略
→ 调用服务初始化器 Inspect
→ 调用服务初始化器 Plan
→ 按策略调用 Apply
→ 调用服务初始化器 Verify
→ 保存脱敏 Observation
```

服务日常启动与 readiness 固定读取本地数据库身份、Migration Ledger、Required Seed Ledger 和 Bootstrap 状态。SystemData 不在线时，已经完成初始化且本地事实有效的服务仍可 `Ready`；本地迁移/RequiredSeed 未达期望版本、Bootstrap 缺少必需 Secret、目标错误或 ledger 不一致时必须 `NotReady`。不得回退默认密码、Mock、错误数据库或旧 Schema，也不得把 liveness 与 readiness 混为一体。

readiness 只表达宿主提供核心能力的最低条件。Redis、消息代理、日志聚合或追踪后端默认进入独立 capability health：可安全回源、积压或降级时报告 `Degraded`，不阻断宿主 Ready；只有某项公开能力无法在依赖缺失时保持正确语义，且其契约明确声明为必需依赖时，才将该依赖纳入相应 readiness 门禁。

拓扑变更绝不隐式复制数据；已填充的目标必须报告 drift，并要求显式迁移/import。SystemData 对 Shared 目标按服务保存脱敏 Observation；readiness 结论仍由服务本地事实产生。

# 5. 职责与数据所有权

| SystemData 服务初始化控制面 | 各业务服务/初始化单元 |
| --- | --- |
| 服务登记、计划、操作状态 | 定义领域 Schema 和表 |
| 拓扑、数据库目标和执行策略编排 | 实现并执行 Migration、Seed、Bootstrap 与 Verify |
| 调用调度、Operation、观察与审计 | 维护本服务 migration/seed/bootstrap ledger 和本地 readiness |
| 环境策略、审批门禁和备份登记 | 提供 expand/contract、DataPatch、回滚或恢复说明 |
| 传递非敏感 Operation/目标/租户/期望版本上下文 | 从本服务 Secret Provider 解析 Secret 并只回报脱敏状态 |

SystemData 不直接编写、推断或长期维护业务表/种子定义，不跨服务读取业务 Repository 或本地账本，不成为业务 Schema/种子内容的权威来源。业务服务运行连接只获得其所需的最小权限。

## 5.1 四类种子

| SeedClass | 用途 | readiness 与环境规则 |
| --- | --- | --- |
| `SystemBaseline` | 权限码、系统角色、内置状态、驱动类型等平台必需目录 | 默认 `RequiredForReadiness=true` |
| `TenantBaseline` | 租户默认配置、分类和策略 | 由 manifest 显式声明是否影响 readiness |
| `EnvironmentSample` | Development/Test 演示或测试样例 | 仅显式启用；Staging/Production 永久拒绝，不影响生产 readiness |
| `SecretBootstrap` | Identity admin 等一次性敏感引导 | 按环境策略显式启用；缺必需 Secret 时失败并 NotReady |

普通业务运行时创建用户所使用的默认初始密码不是种子，不得借 `SecretBootstrap` 绕过业务用例和密码安全策略。

## 5.2 初始化产物与受控执行

- 每个服务提供版本化初始化器并拥有其 migration/seed/bootstrap 实现；SystemData 通过进程内或受信 HTTP 适配器调用，不加载或解释该实现。
- SystemData 只传 `OperationId`、目标身份指纹、TenantNId、ServiceKey、ModuleKey、期望迁移/种子版本和 TraceId 等非敏感上下文。
- 服务初始化器自行解析本服务 Secret Provider，只回报脱敏的 version/checksum/status/TraceId；不得向 SystemData 回传密码、Token、哈希或 Secret 值。
- 控制面契约和内部 HTTP 均禁止提交任意 SQL、路径、命令、凭据或可执行内容。

## 5.3 双账本与幂等

每个服务或具有独立持久化生命周期的初始化单元拥有 migration/seed ledger；逻辑模块不因领域拆分机械创建独立账本。ReferenceData 骨架当前遗留 `reference_data_schema_migrations` 与 `reference_data_seed_ledger` CodeFirst 占位表；PF-03 正式目标统一为 `reference_data.schema_migrations` 与 `reference_data.seed_ledger`，仍是一套服务级双账本。seed ledger 至少记录：

```text
TenantNId / ModuleKey / SeedKey / SeedVersion / Checksum / Scope
Status / AppliedOn / OperationNId / TraceId
```

- 本地账本是权威，SystemData 只保存 `SeedObservation`/初始化观察结果，不跨库直读。
- 相同 key/scope/version/checksum 重跑幂等成功；同版本不同 checksum 为 drift 并拒绝。
- 失败只从账本可验证边界重试；升级新增版本，不原位覆盖历史。
- 种子不得静默覆盖管理员维护数据；删除或修正必须用显式 `DataPatch`，并声明风险、影响范围与恢复说明。
- 多副本通过目标服务自己的锁和 ledger 保证一次执行；SystemData 的 Operation 锁只避免重复编排，不替代服务本地幂等。

# 6. 权限与密钥安全

- SystemData 普通运行连接与 provisioning 管理凭据必须分离。
- provisioning 管理凭据只能来自 Secret Provider、容器/Kubernetes Secret 或受控环境注入；不写入业务数据库，不经 API 返回，不记录到日志、Trace、审计前后值或 Operation 错误详情。
- API 只允许平台管理员或具有服务身份的受信调用方访问；人工 `apply` 必须记录操作者、审批依据、计划摘要和环境。
- 全链路执行认证、授权、审计、脱敏、限流、Trace 和幂等校验。
- SystemData 为目标服务创建最小业务角色；业务服务不得获得创建数据库、创建角色或跨库授权能力。
- SecretBootstrap Secret 只由目标服务 initializer 从本服务 Secret Provider 解析。SystemData 的 API、Operation、数据库、日志、Trace、审计、事件和重试载荷不得接收或透传 Secret 值。

# 7. 环境策略

`DatabaseTopology` 与自动 plan/apply 是独立策略。拓扑矩阵如下：

| 环境 | 允许的拓扑 |
| --- | --- |
| Development | 默认 `Shared`，可显式选择 `PerService` |
| Test | 仅 `PerService` |
| Staging | 仅 `PerService` |
| Production | 仅 `PerService` |

自动 plan/apply 矩阵如下：

| 环境 | 默认策略 |
| --- | --- |
| Development | 可配置自动执行必要迁移和 RequiredSeed；EnvironmentSample 仅在显式启用时执行 |
| Test | 可配置自动执行必要迁移和 RequiredSeed；EnvironmentSample 仅在显式启用时执行 |
| Staging | 默认先 plan，可按发布策略批准自动 apply |
| Production | 禁止启动时自动迁移/播种；默认强制 Advanced，执行 `plan → 审批 → 备份 → apply → 验证` |

所有环境都使用版本化迁移；禁止使用 `EnsureCreated`、Code First 自动建表或删除重建代替迁移。破坏性变更采用 expand/contract，并在计划中提供兼容窗口、备份、回滚或恢复说明。

初始化分为两档：`Standard` 覆盖普通 Migration、RequiredSeed、Bootstrap 与 Verify；`Advanced` 才启用审批、备份证据、签名校验和漂移恢复等高风险门禁。环境策略可强制升级到 Advanced，但普通功能不得无条件承担高级流程。

`EnvironmentSample` 在 Staging/Production 必须于 registration、plan 和 apply 三层拒绝。`SecretBootstrap` 只有环境策略与 manifest 同时允许才执行；缺 Secret 时失败并保持 NotReady，不得回退固定默认密码、Mock 或空凭据。

`Local` 只是使用 SQLite Provider 的 Development profile，仍按该 `DatabaseTopology` 矩阵解析；它不是第五种环境，也没有不同的拓扑语义。

# 8. 并发、失败与恢复

- 同一目标数据库同一时间只允许一个迁移操作；由服务初始化器使用 PostgreSQL advisory lock 或等效分布式锁。
- 操作必须幂等，明确可重试与不可重试错误、退避、超时和最大尝试次数。
- SystemData 持久化计划摘要、目标版本、开始/结束时间、状态、脱敏错误和关联审计；迁移历史只保存在服务本地 Ledger，控制面保存 Observation。
- 多副本同时启动时，只有持锁执行者可以迁移；其他副本等待状态结果并保持 NotReady，不得并发执行。
- 失败不得伪装成功；人工恢复后必须从可验证的迁移历史继续，而不是跳过未知步骤。
- SchemaMigration、RequiredSeed 和 SecretBootstrap 各自具有可验证恢复边界；迁移或种子失败不得盲目从头重跑已成功步骤。

Shared 模式下，SystemData 只解析并编排一次物理数据库目标；每个服务/初始化单元通过自己的初始化器执行并记录事实。同一物理目标数据库的 DDL 必须由服务初始化器通过 PostgreSQL advisory lock 或等效分布式锁串行化。

# 9. SystemData 自身引导例外

SystemData 存在 bootstrap paradox：其自身数据库不能依赖尚未运行的 SystemData API 创建。`SystemData.Service` 自身所需数据库、最小角色和基础授权由基础设施最小引导负责，当前使用 PostgreSQL 18，并保留 `deploy/cloud-dev` Compose/init 或等价部署步骤作为最小引导入口。

SystemData 本地先通过自己的初始化器执行 Schema migration 与最小 `SystemBaseline`，并写入自己的 schema/seed ledger，之后才开放 API/Runner。该路径复用相同 version/checksum/幂等语义，但不得调用自身 API。基础设施引导不得扩张为维护 SystemData 业务表或种子内容。

# 10. 本地回退

- `RemoteDevelopment.Enabled=false` 时，SQLite 物理目标仍按受信 `DatabaseTopology` 解析。
- 启用远程开发/云端环境时，各服务通过受控配置和 SystemData 使用 PostgreSQL 18。
- 本地 SQLite 与云端 PostgreSQL 必须使用等价的版本语义和显式迁移，不得用 `EnsureCreated` 掩盖差异。
- SystemData 不可用会阻止新的拓扑编排或升级 Operation，但不会改变已初始化服务基于本地数据库事实计算的 readiness；不得回退到另一个数据库或管理员自建库。

Shared SQLite（`SharedSqliteFile`）是 Development 默认；`PerService` SQLite 仅作为显式验证模式，且各服务继续使用自己的本地迁移路径。

SQLite 与 PostgreSQL 均适用同一规范化目标、Shared 一次物理 provision、按服务的迁移 ledger/readiness、物理目标 DDL 串行化及 drift/显式迁移/import 规则。

# 11. 新服务模板强制项

后续微服务/Service Host 模板必须包含：

- `ServiceKey + InitializationUnitKey`、期望版本和 `Standard | Advanced` 策略；
- 服务拥有的初始化器、Migration、Seed、Bootstrap、Verify 和 Ledger；
- 只有独立持久化生命周期才拆分初始化单元；
- 初始化 Operation 与日常本地 readiness 明确分离；
- 最小业务数据库角色及权限需求声明；
- 备份策略/备份登记；
- 日志、Trace、指标和错误中可关联的 `OperationId`；
- SystemData 不可用时已初始化服务仍 Ready，以及本地 Migration/Seed/Bootstrap 失败、并发启动、checksum drift、错误目标和环境拒绝的自动化测试。

# 12. PF-02 与后续阶段门禁

PF-02 必须把 Service Initialization Pipeline 纳入 SystemData 详细设计、任务拆分和验收，至少证明：

1. 受信服务可登记声明并获得 plan；
2. 首次初始化可完成 provision、角色、迁移、RequiredSeed、按需 SecretBootstrap 和 Healthy；
3. 相同 manifest 重复 apply 幂等成功，多副本并发只执行一次；
4. 迁移/种子版本升级可追加执行，同版本 checksum drift 被拒绝；
5. 部分失败只从可验证边界重试，已成功步骤不重复破坏数据；
6. 缺必需 Secret 时 bootstrap 失败并 NotReady，SystemData 不接触 Secret 值；
7. Advanced（Production 默认）策略在未审批或未备份时拒绝 apply；
8. EnvironmentSample 在 Staging/Production 被拒绝；
9. 共享物理库中多个服务/初始化单元的表前缀、Ledger、checksum 和锁范围隔离；逻辑模块不机械拆分初始化单元；
10. 重复种子不覆盖管理员维护数据，删除/修正只通过显式 DataPatch；
11. SystemData 失败不影响已初始化服务的本地 Ready；初始化事实失败时服务保持 NotReady；
12. SystemData 自身由基础设施最小引导、本地 migration+SystemBaseline 完成，无循环依赖；
13. SQLite 本地路径与 PostgreSQL 远程路径使用一致版本/双账本语义。

PF-03 及之后所有新服务在阶段详细设计中必须读取本文，并将初始化单元、服务自有初始化器、Migration/Seed/Bootstrap/Verify/Ledger、本地 readiness、环境策略和 `OperationId` 纳入完成标准。审批、备份、签名和漂移恢复只在 Advanced 策略适用时成为门禁。

# 13. Identity 首个消费者案例

Identity 是通用协议的首个正式消费者，但其业务规则仍由 PF-00 实施 03 管理：

```text
SchemaMigration → SystemBaseline → TenantBaseline → SecretBootstrap(ADMIN) → Verify
```

- 权限目录和 `SYSTEM_ADMIN` 属于 `SystemBaseline`。
- 租户安全关系属于 `TenantBaseline`。
- `ADMIN` 属于 `SecretBootstrap`，由 Identity initializer 自行解析 Secret Provider 或执行其已批准的一次性生成策略。
- SystemData 只接收非敏感 manifest/observation，不接收 admin 密码、密码哈希、Token 或 Secret 值。
- Identity 的具体账本、临时密码交付、首次改密和恢复规则以实施 03 的当前并行设计为准；本文不覆盖 PF-00 细则。
