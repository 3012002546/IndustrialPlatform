# 可配置数据库拓扑与 SystemData 编排设计

## 1. 背景与决策

本地调试期间，为每个服务维护独立 PostgreSQL 数据库或 SQLite 文件会增加初始化、连接配置、备份和排障成本。测试、预生产和生产环境仍需要按服务分库，以保持故障隔离、最小权限、独立扩缩容和数据所有权边界。

本设计引入显式的数据库拓扑配置：

- `Development` 默认使用 `Shared`，所有服务共用一个物理数据库。
- `Test`、`Staging` 和 `Production` 默认并强制使用 `PerService`，每个服务使用独立物理数据库。
- PostgreSQL 和本地 SQLite 使用相同的拓扑语义。
- 共享物理数据库不改变服务的数据所有权。各服务仍拥有独立表前缀、迁移产物、迁移账本、仓储和 API/事件契约，禁止跨服务直接读写表。

SystemData 的数据库编排模块是本次变更的最高优先级。拓扑配置不能只停留在各服务连接串层面；SystemData 必须以同一规则解析目标物理数据库、生成计划、执行引导与迁移、维护状态并控制 readiness。

## 2. 配置契约

统一配置节使用以下结构：

```json
{
  "DatabaseTopology": {
    "Mode": "Shared",
    "SharedDatabaseName": "industrial_platform_dev",
    "SharedSqliteFile": "industrial-platform.dev.db",
    "ServiceDatabases": {
      "Identity": "identity_db",
      "ReferenceData": "referencedata_db",
      "SystemData": "systemdata_db",
      "MasterData": "masterdata_db",
      "OperationalData": "operationaldata_db"
    }
  }
}
```

`Mode` 只允许：

- `Shared`：所有服务解析到 `SharedDatabaseName` 或 `SharedSqliteFile`。
- `PerService`：按 `ServiceDatabases:{ServiceKey}` 解析 PostgreSQL 数据库名；SQLite 使用按服务配置或由受控规则生成的独立文件名。

配置优先级沿用 .NET Configuration：环境变量和私有本地配置可覆盖已提交的环境默认值。数据库拓扑解析器输出最终物理目标，业务服务不得自行拼接或覆盖数据库名。

启动校验规则：

- `Development` 缺省为 `Shared`，但允许显式切换到 `PerService`。
- `Test`、`Staging`、`Production` 只允许 `PerService`；配置为 `Shared` 时启动失败。
- `Shared` 缺少共享数据库名或 SQLite 文件名时启动失败。
- `PerService` 缺少当前 `ServiceKey` 的数据库名时启动失败。
- 未知模式、未知服务标识、非法数据库名和空白值均启动失败，不静默回退。
- 日志可以记录环境、模式和服务标识，但不得输出凭据或完整连接串。

## 3. 公共解析组件

BuildingBlocks 提供单一数据库拓扑解析组件，职责限制为：

1. 绑定并校验 `DatabaseTopology`。
2. 根据环境、Provider 和 `ServiceKey` 解析物理数据库目标。
3. 为现有 `SqlSugar` 配置生成最终连接串覆盖值。
4. 向 SystemData 和各服务返回相同的规范化结果。

规范化结果至少包含：

- `EnvironmentName`
- `Mode`
- `ServiceKey`
- `Provider`
- `LogicalDatabaseName`：服务在分库模式下的稳定数据库身份
- `PhysicalDatabaseName` 或 SQLite 文件路径
- `IsSharedPhysicalDatabase`

`LogicalDatabaseName` 始终保留。例如 Identity 在共享开发库中仍以 `identity_db` 作为逻辑身份，但实际连接 `industrial_platform_dev`。这样可避免开发配置反向污染测试和生产的分库清单。

## 4. SystemData 数据库编排

### 4.1 注册与清单

服务 manifest 继续声明稳定的 `ServiceKey`、逻辑数据库名、Provider、迁移产物、目标版本和 Owner。SystemData 在接收注册时结合环境拓扑解析出物理目标，不允许调用方直接提交任意物理数据库名或连接串。

登记和审计必须同时保留：

- 逻辑数据库身份
- 解析后的物理数据库身份
- 拓扑模式
- 配置/manifest 版本与校验值
- 迁移产物、目标版本和执行状态

### 4.2 计划与执行

在 `Shared` 模式下：

- SystemData 只为共享物理数据库生成一次 provision 计划。
- SystemData 自身通过基础设施最小引导进入该共享数据库，仍是唯一 bootstrap 例外。
- 其他服务不再请求创建独立物理数据库，但仍分别执行自己的版本化迁移产物。
- 每个服务继续使用唯一迁移账本，例如 `identity_schema_migrations`、`reference_data_schema_migrations` 和 `system_data_schema_migrations`。
- 对同一共享物理数据库的 DDL 迁移按物理数据库级 advisory lock 串行执行，避免不同服务同时修改目录和锁资源。
- 每个服务的迁移结果、失败、重试和 readiness 独立记录；一个服务迁移失败不得伪装为其他服务迁移成功。

在 `PerService` 模式下：

- SystemData 按服务生成独立数据库、角色、授权、备份和迁移计划。
- 继续执行既有的 `plan → 审批 → 备份 → apply` 生产门禁。
- 管理员凭据只存在于受控基础设施和 Secret Provider，不进入业务 API、数据库记录、日志、Trace 或审计载荷。

### 4.3 Readiness 与漂移

服务 readiness 以“当前服务的逻辑数据库身份 + 解析后的物理目标 + 迁移产物校验值 + 目标版本”为判断依据。

以下情况保持 `NotReady`：

- SystemData 不可用且当前目标状态尚未确认。
- 物理目标与拓扑解析结果不一致。
- 迁移失败、校验值不一致或目标版本未达到。
- 非 Development 环境被配置为共享模式。
- 同一环境内出现相互矛盾的拓扑配置版本。

拓扑从 `Shared` 切换到 `PerService` 或反向切换只改变目标解析，不自动复制数据。存在数据的环境必须走显式迁移/导入方案；SystemData 应报告 drift 并阻止直接 apply。

## 5. 本地与云端开发行为

### 5.1 SQLite

默认本地回退使用同一个 `industrial-platform.dev.db` 文件。各服务运行自己的显式迁移，并通过独立表前缀和迁移账本共存。不得使用 `EnsureCreated` 或删除重建替代迁移。

开发者可将 `Mode` 改为 `PerService`，恢复每服务独立 SQLite 文件，用于验证分库行为。

### 5.2 PostgreSQL

云端开发 Compose 默认只创建 `industrial_platform_dev`。初始化脚本根据拓扑模式决定：

- `Shared`：只创建共享开发数据库。
- `PerService`：创建配置中列出的服务数据库。

现有 `IdentityDatabase`、`ReferenceDataDatabase` 等零散开发配置迁移到 `DatabaseTopology`。兼容期如需读取旧键，只允许给出明确弃用告警，不能形成两套长期配置来源。

## 6. 错误处理与安全边界

- 所有数据库名先进行格式与允许字符校验，再交给参数化或安全引用的数据库管理命令。
- 不允许 API 接收任意 SQL、迁移文件路径、服务器地址、用户名、密码或完整连接串。
- 共享开发库可以使用开发专用角色；测试和生产继续使用每服务 owner/migrator/runtime 最小角色。
- 即使共享数据库，也禁止服务仓储、报表或临时脚本跨服务读取表；跨服务数据通过 API、事件或受治理的数据产品流转。
- 不新增独立 Migrator Service，迁移仍由 SystemData 编排并由服务拥有迁移产物。

## 7. 验证设计

至少覆盖以下自动化验证：

1. `Development + Shared + SQLite`：多个服务解析到同一文件，迁移账本互不覆盖。
2. `Development + Shared + PostgreSQL`：多个服务解析到同一数据库，SystemData 只生成一次 provision，并分别执行迁移。
3. `Development + PerService`：各服务解析到独立数据库或文件。
4. `Test/Staging/Production + Shared`：配置校验失败。
5. `PerService` 缺少当前服务数据库映射：启动失败。
6. 同一共享数据库的并发迁移：物理数据库级锁保证串行，结果分别入账。
7. 任一服务迁移失败：该服务保持 NotReady，其他已完成服务状态不被覆盖。
8. manifest 逻辑数据库名、SystemData 物理解析结果和实际连接目标不一致：报告 drift 并拒绝 apply。
9. 配置、日志、审计和 Trace 不泄露凭据或完整连接串。
10. 全仓扫描确认不存在 `EnsureCreated` 和业务 API 自行建库路径。

## 8. 文档与实施优先级

实施顺序如下：

1. **最高优先级：SystemData 数据库编排模块。** 更新数据模型、manifest/registration、拓扑解析、plan/provision/migrate、锁、Operation、readiness 和 drift 门禁。
2. BuildingBlocks 数据库拓扑配置、校验和规范化解析组件。
3. Identity、ReferenceData 及后续服务接入统一解析结果。
4. 本地 SQLite 默认值、私有开发配置样例、云端开发 Compose 与初始化脚本。
5. 更新数据库规范、SystemData 蓝图、实施方案模板、本地开发指南和各服务实施方案中的环境策略。

后续设计和任务不得再把“每服务一个物理数据库”写成所有环境的硬编码事实；应明确区分稳定的逻辑数据库所有权与可配置的物理数据库拓扑。

## 9. 完成标准

- 一处配置可在 Development 中切换 `Shared` / `PerService`。
- SQLite 与 PostgreSQL 使用一致的拓扑规则。
- Test、Staging、Production 无法启用共享数据库。
- SystemData 可正确编排共享开发库和独立环境库，且保持服务级迁移、状态和 readiness。
- 共享开发库只创建一次，各服务迁移账本、表前缀和数据所有权互不冲突。
- 现有分库生产设计、最小权限、备份和审批门禁不被削弱。
- 相关蓝图、实施方案、开发指南、配置样例和自动化验证保持一致。
