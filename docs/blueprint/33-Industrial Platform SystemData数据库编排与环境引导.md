# Industrial Platform SystemData 数据库编排与环境引导

版本：V1.0
状态：已确认，平台数据库初始化权威母版
生效日期：2026-08-11

---

# 1. 决策与定位

后续微服务数据库初始化与环境搭建统一由 `SystemData.Service` 内部的数据库编排/环境引导能力管理。该能力属于 PF-02 SystemData，不新增核心 Service Host，不以独立 Database Migrator Service 作为首选架构，也不允许各业务 API 持有数据库管理员凭据自行建库。

SystemData 提供控制面 API；内部 Worker/Runner 可以作为异步执行细节，但不拥有业务领域表定义。各业务服务仍拥有自己的领域 Schema、版本化迁移产物和回滚/恢复说明。

# 2. API 与操作模型

SystemData 至少提供以下受控能力：

- `dry-run/plan`：验证声明、目标环境、当前状态、迁移差异和风险，不改变数据库；
- `apply/provision`：按已授权计划创建数据库、角色和授权，并执行版本化迁移；
- `operation status`：按 `OperationId` 查询排队、执行、成功、失败、取消/超时及脱敏错误；
- `service registration/query`：登记和查询服务期望状态、迁移版本、所有者与环境策略。

创建数据库和执行迁移属于耗时操作。API 必须快速返回可追踪 `OperationId`，由内部 Runner 执行；禁止用长时间同步 HTTP 持有连接等待完成。写操作必须带幂等键，同一服务、环境、目标版本和请求语义不得重复执行。

# 3. 声明式注册清单

新服务通过 registration/manifest 声明下列最小信息：

| 字段 | 含义 |
| --- | --- |
| `ServiceKey` | 稳定服务标识，不使用显示名称代替 |
| `Provider` | 数据库提供程序；云端首期为 PostgreSQL，本地回退可为 SQLite |
| `DatabaseName` | 稳定的逻辑数据库身份，不包含服务器地址或凭据 |
| `MigrationAssembly` / `MigrationBundle` | 服务拥有的迁移程序集、Bundle 或等价产物 |
| `MigrationVersion` | 期望迁移版本或不可变产物版本 |
| `Owner` | 服务/模块责任人或责任团队 |
| `DesiredState` | 期望的数据库、角色、授权和迁移状态 |
| `AutoProvision` | 是否允许自动创建；必须受环境策略约束 |
| `AutoMigrate` | 是否允许自动迁移；必须受环境策略约束 |

清单不得包含真实服务器地址、密码、私钥、管理员连接串或可还原的密钥材料。

本文是可配置数据库拓扑的权威契约：共享物理存储绝不削弱服务的表前缀、迁移、Repository、API、事件或数据所有权边界。`DatabaseName` 是稳定的逻辑数据库身份（logical database identity），不因物理拓扑变更而改名。受信服务的环境配置还必须声明 `DatabaseTopology`：`Mode: Shared | PerService`、`SharedDatabaseName`、`SharedSqliteFile` 和 `ServiceDatabases:{ServiceKey}`。SystemData 将声明规范化为 `EnvironmentName`、`Mode`、`ServiceKey`、`Provider`、`LogicalDatabaseName`、`PhysicalDatabaseName`、`IsSharedPhysicalDatabase` 后再计划和执行；例如 Development 的 Shared PostgreSQL 可使用 `SharedDatabaseName: industrial_platform_dev`。

# 4. 服务启动握手与就绪门禁

启用远程 PostgreSQL 时，新服务启动顺序固定为：

```text
读取本服务声明与环境策略
→ 调用 SystemData 查询/登记期望状态
→ SystemData 幂等检查数据库、最小业务角色与授权
→ 缺失时按策略创建
→ 获取 PostgreSQL advisory lock 或等效分布式锁
→ 使用该服务提供的版本化迁移产物执行迁移
→ 记录迁移历史、Operation、审计与指标
→ 服务复核期望版本
→ readiness 转为 Healthy
```

SystemData 不可用、操作失败、目标版本不一致或连接指向错误数据库时，服务必须保持 `NotReady` 并给出明确且脱敏的错误、`OperationId` 和 TraceId。不得静默降级到错误数据库，不得临时使用管理员权限自行建库，也不得把 liveness 与 readiness 混为一体。

拓扑变更绝不隐式复制数据；已填充的目标必须报告 drift，并要求显式迁移/import。SystemData 对 Shared 目标按服务、迁移产物和版本分别评估 readiness。

# 5. 职责与数据所有权

| SystemData 数据库编排能力 | 各业务服务 |
| --- | --- |
| 服务登记、计划、操作状态 | 定义领域 Schema 和表 |
| 数据库、最小业务角色和授权编排 | 生成并版本化迁移产物 |
| 并发锁、执行调度、历史与审计 | 提供 expand/contract、回滚或恢复说明 |
| 环境策略、审批门禁和备份登记 | 在 readiness 前验证目标版本 |

SystemData 不直接编写、推断或长期维护业务表定义，不跨服务读取业务 Repository，不成为业务 Schema 的权威来源。业务服务运行连接只获得其所需的最小权限。

# 6. 权限与密钥安全

- SystemData 普通运行连接与 provisioning 管理凭据必须分离。
- provisioning 管理凭据只能来自 Secret Provider、容器/Kubernetes Secret 或受控环境注入；不写入业务数据库，不经 API 返回，不记录到日志、Trace、审计前后值或 Operation 错误详情。
- API 只允许平台管理员或具有服务身份的受信调用方访问；人工 `apply` 必须记录操作者、审批依据、计划摘要和环境。
- 全链路执行认证、授权、审计、脱敏、限流、Trace 和幂等校验。
- SystemData 为目标服务创建最小业务角色；业务服务不得获得创建数据库、创建角色或跨库授权能力。

# 7. 环境策略

| 环境 | 默认策略 |
| --- | --- |
| Development / 自动化测试 | 可配置 `AutoProvision=true` 与 `AutoMigrate=true` |
| Staging | 默认先 plan，可按发布策略批准自动 apply |
| Production | 默认禁止自动创建和自动迁移；执行 `plan → 审批 → 备份 → apply → 验证` |

所有环境都使用版本化迁移；禁止使用 `EnsureCreated`、Code First 自动建表或删除重建代替迁移。破坏性变更采用 expand/contract，并在计划中提供兼容窗口、备份、回滚或恢复说明。

Development 默认 `Shared`，也可显式使用 `PerService`；Test、Staging 和 Production 只允许 `PerService`，Shared 无效。

# 8. 并发、失败与恢复

- 同一目标数据库同一时间只允许一个迁移操作；使用 PostgreSQL advisory lock 或等效分布式锁。
- 操作必须幂等，明确可重试与不可重试错误、退避、超时和最大尝试次数。
- 持久化计划摘要、目标版本、迁移历史、开始/结束时间、状态、脱敏错误和关联审计。
- 多副本同时启动时，只有持锁执行者可以迁移；其他副本等待状态结果并保持 NotReady，不得并发执行。
- 失败不得伪装成功；人工恢复后必须从可验证的迁移历史继续，而不是跳过未知步骤。

Shared 模式下，SystemData 只 provision 一次物理数据库；每个服务独立执行自己的迁移产物和唯一 migration ledger。同一物理目标数据库的 DDL 必须通过 physical-target PostgreSQL advisory lock 或等效分布式锁串行化。

# 9. SystemData 自身引导例外

SystemData 存在 bootstrap paradox：其自身数据库不能依赖尚未运行的 SystemData API 创建。`SystemData.Service` 自身所需数据库、最小角色和基础授权由基础设施最小引导负责，当前使用 PostgreSQL 18，并保留 `deploy/cloud-dev` Compose/init 或等价部署步骤作为最小引导入口。

SystemData API 只管理其他服务数据库。基础设施引导不得扩张为维护后续业务表定义；后续业务库在 SystemData 可用后转由本母版定义的编排 API 管理。

# 10. 本地回退

- `RemoteDevelopment.Enabled=false` 时，SQLite 物理目标仍按受信 `DatabaseTopology` 解析。
- 启用远程开发/云端环境时，各服务通过受控配置和 SystemData 使用 PostgreSQL 18。
- 本地 SQLite 与云端 PostgreSQL 必须使用等价的版本语义和显式迁移，不得用 `EnsureCreated` 掩盖差异。
- SystemData 不可用时，启用远程 PostgreSQL 的新服务保持 NotReady；不得回退到另一个数据库或管理员自建库。

Shared SQLite（`SharedSqliteFile`）是 Development 默认；`PerService` SQLite 仅作为显式验证模式，且各服务继续使用自己的本地迁移路径。

SQLite 与 PostgreSQL 均适用同一规范化目标、Shared 一次物理 provision、按服务的迁移 ledger/readiness、物理目标 DDL 串行化及 drift/显式迁移/import 规则。

# 11. 新服务模板强制项

后续微服务/Service Host 模板必须包含：

- registration/manifest 配置与校验；
- 服务拥有的版本化迁移程序集、Bundle 或等价不可变产物；
- 启动登记、plan/apply 状态握手和 readiness 门禁；
- 最小业务数据库角色及权限需求声明；
- 备份策略/备份登记；
- 日志、Trace、指标和错误中可关联的 `OperationId`；
- SystemData 不可用、迁移失败、并发启动和错误目标数据库的自动化测试。

# 12. PF-02 与后续阶段门禁

PF-02 必须把数据库编排/环境引导纳入 SystemData 详细设计、任务拆分和验收，至少证明：

1. 受信服务可登记声明并获得 plan；
2. Development/测试环境可通过异步 Operation 幂等创建一个非 SystemData 业务库、最小角色与授权，并执行该服务迁移产物；
3. 生产策略在未审批或未备份时拒绝 apply；
4. 多副本并发请求只执行一次迁移；
5. 管理凭据不进入 API、数据库、日志、Trace 或审计载荷；
6. SystemData 失败或迁移失败时消费者保持 NotReady；
7. SystemData 自身仍由基础设施最小引导，未形成循环依赖；
8. SQLite 本地回退与 PostgreSQL 远程路径均有明确验证证据。

PF-03 及之后所有新服务在阶段详细设计中必须读取本文，并将 manifest、迁移产物、启动握手、readiness、最小角色、备份登记和 `OperationId` 纳入完成标准。
