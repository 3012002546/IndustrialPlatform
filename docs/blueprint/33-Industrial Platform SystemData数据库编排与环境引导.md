# Industrial Platform SystemData 服务初始化编排与环境引导

版本：V2.0
状态：已确认，平台服务初始化权威母版
生效日期：2026-08-14

---

# 1. 决策与定位

PF-03 及后续微服务、共享 Service Host 内部模块的初始化统一由 `SystemData.Service` 内部的 `Service Initialization Pipeline` 控制。该能力由既有数据库编排控制面兼容升级而来，属于 PF-02 SystemData，不新增核心 Service Host，不创建独立 Migrator/Seeder Service，也不允许各业务 API 持有数据库管理员凭据自行建库、迁移或播种。

SystemData 提供 registration、plan、审批/备份、provision、迁移、种子、一次性引导、Operation 和 readiness 控制面；内部 Worker/Runner 仅以受控一次性隔离任务或执行适配器调度服务自有初始化产物。各业务服务/模块仍拥有自己的 Schema、迁移产物、种子内容、初始化器、本地账本及回滚/恢复说明。SystemData 不理解业务表、不直写业务 Repository，也不保存业务 Secret 值。

# 2. API 与操作模型

SystemData 至少提供以下受控能力：

- `dry-run/plan`：验证声明、目标环境、当前状态、迁移差异和风险，不改变数据库；
- `apply/initialize`：按已授权计划创建数据库、角色和授权，执行版本化迁移、必要种子、按需 SecretBootstrap，并验证最终状态；
- `operation status`：按 `OperationId` 查询排队、执行、成功、失败、取消/超时及脱敏错误；
- `service registration/query`：登记和查询服务期望状态、迁移版本、所有者与环境策略。

创建数据库和执行迁移属于耗时操作。API 必须快速返回可追踪 `OperationId`，由内部 Runner 执行；禁止用长时间同步 HTTP 持有连接等待完成。写操作必须带幂等键，同一服务、环境、目标版本和请求语义不得重复执行。

通用阶段固定为：

```text
Registration → Plan → ProvisionDatabase → ProvisionRoles → Backup
→ SchemaMigration → RequiredSeed → SecretBootstrap（按需）
→ Verify → Readiness Healthy
```

已有仅执行 `Migrate → Verify` 的实现视为兼容子流程：当 manifest 未声明 SeedSets 且无需 SecretBootstrap 时保持有效；一旦声明 required seed 或 bootstrap，Operation 必须补齐对应阶段后才能 Healthy。

# 3. 声明式注册清单

新服务通过 registration/`InitializationManifest` 声明下列最小信息：

| 字段 | 含义 |
| --- | --- |
| `ServiceKey` | 稳定服务标识，不使用显示名称代替 |
| `ModuleKey` | 强制模块标识；独立服务也必须声明，禁止共享宿主使用宿主级模糊大包 |
| `Provider` | 数据库提供程序；云端首期为 PostgreSQL，本地回退可为 SQLite |
| `DatabaseName` | 稳定的逻辑数据库身份，不包含服务器地址或凭据 |
| `MigrationAssembly` / `MigrationBundle` | 服务拥有的迁移程序集、Bundle 或等价产物 |
| `MigrationArtifactId` / `MigrationVersion` / `MigrationChecksum` | 不可变迁移产物身份、版本和 checksum |
| `MigrationVersion` | 期望迁移版本或不可变产物版本 |
| `Owner` | 服务/模块责任人或责任团队 |
| `DesiredState` | 期望的数据库、角色、授权和迁移状态 |
| `AutoProvision` | 是否允许自动创建；必须受环境策略约束 |
| `AutoMigrate` | 是否允许自动迁移；必须受环境策略约束 |
| `SeedSets[]` | 本模块拥有的版本化种子声明集合 |

每个 SeedSet 至少声明：

```text
SeedKey / SeedVersion / SeedClass / Scope
SeedArtifactId / SeedChecksum / SeedSignature
RequiredForReadiness / AllowedEnvironments
DependsOnMigrationVersion / DependsOnSeedKeys
BootstrapPolicy
```

清单及 API/数据库不得包含种子实际内容、密码、Token、密码哈希、Secret 值、任意 SQL、任意路径/命令、真实服务器地址或数据库凭据。`SecretBootstrap` 只声明非敏感需求、环境允许条件和交付策略，不携带 Secret。

本文是可配置数据库拓扑的权威契约：共享物理存储绝不削弱服务的表前缀、迁移、Repository、API、事件或数据所有权边界。`DatabaseName` 是稳定的逻辑数据库身份（logical database identity），不因物理拓扑变更而改名。受信服务的环境配置还必须声明 `DatabaseTopology`：`Mode: Shared | PerService`、`SharedDatabaseName`、`SharedSqliteFile` 和 `ServiceDatabases:{ServiceKey}`。SystemData 将声明规范化为 `EnvironmentName`、`Mode`、`ServiceKey`、`Provider`、`LogicalDatabaseName`、`PhysicalDatabaseName`、`IsSharedPhysicalDatabase` 后再计划和执行；例如 Development 的 Shared PostgreSQL 可使用 `SharedDatabaseName: industrial_platform_dev`。

# 4. 服务启动握手与就绪门禁

新服务启动顺序固定为：

```text
读取本服务声明与环境策略
→ 调用 SystemData 查询/登记期望状态
→ SystemData 幂等检查数据库、最小业务角色与授权
→ 缺失时按策略创建
→ 获取 PostgreSQL advisory lock 或等效分布式锁
→ 使用该服务提供的版本化迁移产物执行迁移
→ 按依赖图执行 RequiredSeed
→ 按环境策略执行可选 SecretBootstrap
→ 服务初始化器复核数据库身份、迁移版本、种子账本和 bootstrap 状态
→ 回报脱敏 observation、Operation、审计与指标
→ readiness 转为 Healthy
```

SystemData 不可用、操作失败、迁移/RequiredForReadiness 种子未达期望版本、SecretBootstrap 缺少必需 Secret、目标版本不一致或连接指向错误数据库时，服务必须保持 `NotReady` 并给出明确且脱敏的错误、`OperationId` 和 TraceId。不得回退默认密码、Mock、错误数据库或旧 Schema，也不得把 liveness 与 readiness 混为一体。

拓扑变更绝不隐式复制数据；已填充的目标必须报告 drift，并要求显式迁移/import。SystemData 对 Shared 目标按服务、迁移产物和版本分别评估 readiness。

# 5. 职责与数据所有权

| SystemData 服务初始化控制面 | 各业务服务/模块 |
| --- | --- |
| 服务登记、计划、操作状态 | 定义领域 Schema 和表 |
| 数据库、最小业务角色和授权编排 | 生成并版本化迁移、种子和 initializer 产物 |
| 并发锁、执行调度、Operation、观察与审计 | 维护本服务 schema migration/seed ledger |
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

- 每个服务提供签名、不可变、版本化的 migration/seed/initializer bundle；SystemData 校验 allowlist、checksum/签名后，以受控一次性隔离任务或适配器执行。
- SystemData 只传 `OperationId`、目标身份指纹、TenantNId、ServiceKey、ModuleKey、期望迁移/种子版本和 TraceId 等非敏感上下文。
- 服务初始化器自行解析本服务 Secret Provider，只回报脱敏的 version/checksum/status/TraceId；不得向 SystemData 回传密码、Token、哈希或 Secret 值。
- 实现层可支持简单签名 SQL seed bundle 与服务自有 initializer bundle 两种适配器，但均禁止 API 提交任意 SQL、路径、命令或可执行内容。

## 5.3 双账本与幂等

每个服务/模块分别拥有 `<module>_schema_migrations` 与 `<module>_seed_ledger`；共享宿主不得共用模糊账本范围。seed ledger 至少记录：

```text
TenantNId / ModuleKey / SeedKey / SeedVersion / Checksum / Scope
Status / AppliedOn / OperationNId / TraceId
```

- 本地账本是权威，SystemData 只保存 `SeedObservation`/初始化观察结果，不跨库直读。
- 相同 key/scope/version/checksum 重跑幂等成功；同版本不同 checksum 为 drift 并拒绝。
- 失败只从账本可验证边界重试；升级新增版本，不原位覆盖历史。
- 种子不得静默覆盖管理员维护数据；删除或修正必须用显式 `DataPatch`，并声明风险、影响范围与恢复说明。
- 多副本通过 physical-target advisory lock、ModuleKey 范围锁和 seed ledger 共同保证一次执行。

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
| Production | 禁止启动时自动迁移/播种；执行 `plan → 审批 → 备份 → apply → 验证` |

所有环境都使用版本化迁移；禁止使用 `EnsureCreated`、Code First 自动建表或删除重建代替迁移。破坏性变更采用 expand/contract，并在计划中提供兼容窗口、备份、回滚或恢复说明。

`EnvironmentSample` 在 Staging/Production 必须于 registration、plan 和 apply 三层拒绝。`SecretBootstrap` 只有环境策略与 manifest 同时允许才执行；缺 Secret 时失败并保持 NotReady，不得回退固定默认密码、Mock 或空凭据。

`Local` 只是使用 SQLite Provider 的 Development profile，仍按该 `DatabaseTopology` 矩阵解析；它不是第五种环境，也没有不同的拓扑语义。

# 8. 并发、失败与恢复

- 同一目标数据库同一时间只允许一个迁移操作；使用 PostgreSQL advisory lock 或等效分布式锁。
- 操作必须幂等，明确可重试与不可重试错误、退避、超时和最大尝试次数。
- 持久化计划摘要、目标版本、迁移历史、开始/结束时间、状态、脱敏错误和关联审计。
- 多副本同时启动时，只有持锁执行者可以迁移；其他副本等待状态结果并保持 NotReady，不得并发执行。
- 失败不得伪装成功；人工恢复后必须从可验证的迁移历史继续，而不是跳过未知步骤。
- SchemaMigration、RequiredSeed 和 SecretBootstrap 各自具有可验证恢复边界；迁移或种子失败不得盲目从头重跑已成功步骤。

Shared 模式下，SystemData 只 provision 一次物理数据库；每个 ServiceKey/ModuleKey 独立执行自己的迁移/种子产物和双账本。同一物理目标数据库的 DDL 必须通过 physical-target PostgreSQL advisory lock 或等效分布式锁串行化，种子还须按 ModuleKey/Scope 与本地 ledger 隔离。

# 9. SystemData 自身引导例外

SystemData 存在 bootstrap paradox：其自身数据库不能依赖尚未运行的 SystemData API 创建。`SystemData.Service` 自身所需数据库、最小角色和基础授权由基础设施最小引导负责，当前使用 PostgreSQL 18，并保留 `deploy/cloud-dev` Compose/init 或等价部署步骤作为最小引导入口。

SystemData 本地先执行自身 Schema migration 与最小 `SystemBaseline`，并写入自己的 schema/seed ledger，之后才开放 API/Runner。该路径复用相同 version/checksum/幂等语义，但不得调用自身 API。基础设施引导不得扩张为维护 SystemData 业务表或种子内容；其他服务在 SystemData 可用后转由本母版定义的初始化 API 管理。

# 10. 本地回退

- `RemoteDevelopment.Enabled=false` 时，SQLite 物理目标仍按受信 `DatabaseTopology` 解析。
- 启用远程开发/云端环境时，各服务通过受控配置和 SystemData 使用 PostgreSQL 18。
- 本地 SQLite 与云端 PostgreSQL 必须使用等价的版本语义和显式迁移，不得用 `EnsureCreated` 掩盖差异。
- SystemData 不可用时，启用远程 PostgreSQL 的新服务保持 NotReady；不得回退到另一个数据库或管理员自建库。

Shared SQLite（`SharedSqliteFile`）是 Development 默认；`PerService` SQLite 仅作为显式验证模式，且各服务继续使用自己的本地迁移路径。

SQLite 与 PostgreSQL 均适用同一规范化目标、Shared 一次物理 provision、按服务的迁移 ledger/readiness、物理目标 DDL 串行化及 drift/显式迁移/import 规则。

# 11. 新服务模板强制项

后续微服务/Service Host 模板必须包含：

- `ServiceKey + ModuleKey` 和 `InitializationManifest/SeedSets` 配置与校验；
- 服务拥有的版本化迁移、种子、initializer Bundle 或等价不可变产物；
- 每模块独立 `<module>_schema_migrations`、`<module>_seed_ledger`、表前缀和 checksum 范围；
- 启动登记、plan/apply 状态握手和 readiness 门禁；
- 最小业务数据库角色及权限需求声明；
- 备份策略/备份登记；
- 日志、Trace、指标和错误中可关联的 `OperationId`；
- SystemData 不可用、迁移/种子/bootstrap 失败、并发启动、checksum drift、错误目标和环境拒绝的自动化测试。

# 12. PF-02 与后续阶段门禁

PF-02 必须把 Service Initialization Pipeline 纳入 SystemData 详细设计、任务拆分和验收，至少证明：

1. 受信服务可登记声明并获得 plan；
2. 首次初始化可完成 provision、角色、迁移、RequiredSeed、按需 SecretBootstrap 和 Healthy；
3. 相同 manifest 重复 apply 幂等成功，多副本并发只执行一次；
4. 迁移/种子版本升级可追加执行，同版本 checksum drift 被拒绝；
5. 部分失败只从可验证边界重试，已成功步骤不重复破坏数据；
6. 缺必需 Secret 时 bootstrap 失败并 NotReady，SystemData 不接触 Secret 值；
7. 生产策略在未审批或未备份时拒绝 apply；
8. EnvironmentSample 在 Staging/Production 被拒绝；
9. 共享物理库中多个 ModuleKey 的表前缀、双账本、checksum 和锁范围隔离；
10. 重复种子不覆盖管理员维护数据，删除/修正只通过显式 DataPatch；
11. SystemData 失败或初始化失败时消费者保持 NotReady；
12. SystemData 自身由基础设施最小引导、本地 migration+SystemBaseline 完成，无循环依赖；
13. SQLite 本地路径与 PostgreSQL 远程路径使用一致版本/双账本语义。

PF-03 及之后所有新服务/模块在阶段详细设计中必须读取本文，并将 ModuleKey、InitializationManifest/SeedSets、迁移/种子/initializer 产物、双账本、启动握手、readiness、最小角色、备份登记和 `OperationId` 纳入完成标准。

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
