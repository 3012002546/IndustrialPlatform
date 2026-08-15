# TASK-SD-005 验证证据

## 状态

已实现，待 Codex 验收。Release 构建 0 警告、0 错误；定向测试 0 失败。

## 提交哈希

`d125867b487ff2957b42cf2af1ec7ad9977f7791 feat(systemdata): add organization position and assignment domain`

## 修改文件

新增（Domain 聚合与规则）：

- `SystemData.Domain/Organizations/AdministrativeOrganization.cs`、`AdministrativeOrganizationType.cs`、`OrganizationStatus.cs`、`OrganizationMovePreview.cs`
- `SystemData.Domain/Positions/Position.cs`、`PositionStatus.cs`
- `SystemData.Domain/Assignments/UserAssignment.cs`、`AssignmentScheduleRules.cs`、`AssignmentProjection.cs`、`AssignmentState.cs`、`EffectivePeriod.cs`
- `SystemData.Domain/SystemDataDomainGuard.cs`

新增（Application 端口，仅端口不实现 API 用例）：

- `SystemData.Application/Organizations/IAdministrativeOrganizationStore.cs`（含 `GetDependencyCountsAsync`、`GetSubtreeCountsAsync`、`GetDescendantNIdsAsync`、`NameAvailableAsync`，均接受 `DateTimeOffset now`）
- `SystemData.Application/Positions/IPositionStore.cs`
- `SystemData.Application/Assignments/IUserAssignmentStore.cs`、`IUserAssignmentAdvisoryLock.cs`（`IUserAssignmentLockHandle`：Commit=释放，Dispose 未提交=回滚）

新增（Infrastructure 持久化）：

- `Persistence/Entities/AdministrativeOrganizationTable.cs`、`PositionTable.cs`、`UserAssignmentTable.cs`
- `Persistence/StoreConflictGuard.cs`（唯一键冲突与双版本不匹配 → `ConcurrencyException`）
- `Persistence/SystemData/AdministrativeOrganizationStore.cs`、`PositionStore.cs`、`UserAssignmentStore.cs`、`UserAssignmentAdvisoryLock.cs`

修改：

- `Persistence/Migrations/SystemDataSchemaMigrations.cs`（新增 `SDM-004-03` 组织、`SDM-005-01` 岗位、`SDM-006-01` 任职）
- `Persistence/TableMapper.cs`（三组双向映射，组织写路径生成 `NormalizedName`）
- `Infrastructure/DependencyInjection.cs`（注册 4 个持久化端口）

新增/修改（测试）：

- `Domain.Tests/AdministrativeOrganizationDomainTests.cs`、`PositionDomainTests.cs`、`UserAssignmentDomainTests.cs`、`AssignmentScheduleRulesTests.cs`
- `Infrastructure.Tests/SystemDataAggregateStoreTests.cs`（新增）、`SystemDataOrchestrationStoreTests.cs`（表数断言扩至 14 张迁移、13 个表名）

## 关键决策

- 表名按方案 §8.1：`system_data_organization`、`system_data_position`、`system_data_user_assignment`。
- 组织全历史 NId 唯一（`(tenant_n_id, normalized_n_id)` 部分唯一索引 `WHERE is_deleted = 0`）；同父活动名称唯一（`(tenant_n_id, parent_organization_n_id, normalized_name)`）；名称可读性校验经 `normalized_name` 列而非 LOWER() 表达式索引（provider 可移植、查询确定）。
- 复合外键 `(id, is_deleted)` 指向父快照列，`ON UPDATE CASCADE`；子侧 `parent_organization_is_deleted`/`organization_is_deleted`/`position_is_deleted` 快照列支持父删除 shadow 过滤；查询双重过滤（自身 `is_deleted=false` 且父快照 false）。
- 左闭右开区间 `[EffectiveFrom, EffectiveTo)`，`EffectiveTo` 可空表示开放区间；任职持久状态为 `Enabled/Cancelled`，当前/计划/已结束由投影（`GetProjection(now)`）推导。
- 移动预览与停用门统一“当前或未来任职”语义（`State==Enabled AND (EffectiveTo==null OR EffectiveTo>now)`），因此 `GetDependencyCountsAsync`/`GetSubtreeCountsAsync` 签名携带 `DateTimeOffset now`。
- 子树计数在租户内一次加载父子投影，内存 BFS 展开（树浅）。
- 按用户 advisory lock 双分支：PostgreSQL `pg_advisory_xact_lock(hashtextextended(...))` 包在事务内（Commit=提交、Dispose 未提交=回滚，`Interlocked.Exchange` 幂等）；SQLite 替身为静态字典 + `SemaphoreSlim`。
- 双版本乐观并发：写路径 `WHERE OptimisticVersion == expected AND ConcurrencyVersion == expected`，`EnsureSingleRowAffected` 不匹配抛 `ConcurrencyException`；插入唯一键冲突经 `StoreConflictGuard` 沿 inner chain 判断 `UNIQUE constraint failed`/`duplicate key`/`23505`。

## 验证命令与逐项结果

```powershell
dotnet build tests/SystemData/IndustrialPlatform.SystemData.Domain.Tests/IndustrialPlatform.SystemData.Domain.Tests.csproj --configuration Release
dotnet test tests/SystemData/IndustrialPlatform.SystemData.Domain.Tests/IndustrialPlatform.SystemData.Domain.Tests.csproj --configuration Release --no-build
dotnet build tests/SystemData/IndustrialPlatform.SystemData.Infrastructure.Tests/IndustrialPlatform.SystemData.Infrastructure.Tests.csproj --configuration Release
dotnet test tests/SystemData/IndustrialPlatform.SystemData.Infrastructure.Tests/IndustrialPlatform.SystemData.Infrastructure.Tests.csproj --configuration Release --no-build
```

- 两个测试项目 Release 构建：0 警告、0 错误。
- Domain.Tests：`已通过! - 失败: 0，通过: 153，已跳过: 0`（四类型组织矩阵、多根公司、移动/停用门、时间化任职、主任职历史拆分、区间重叠/覆盖规则）。
- Infrastructure.Tests：`已通过! - 失败: 0，通过: 84，已跳过: 0`（迁移建表 14 步、组织/岗位/任职仓储往返、唯一冲突与双版本并发、同级名称可用性、依赖/子树计数、SQLite advisory lock 替身）。

## 剩余风险

- PostgreSQL advisory lock 与并发区间语义（`pg_advisory_xact_lock` 分支）本地无 PostgreSQL 环境，未实测，标记**待外部验收**；SQLite 替身路径已由 `AdvisoryLock_Sqlite_*` 测试覆盖。
- DateTimeOffset 经 SqlSugar SQLite TEXT 存储后偏移丢失（墙钟保留、读回本地偏移），测试比较 `.DateTime`；PostgreSQL 上的偏移往返验证标记**待外部验收**。
- 迁移编号 `SDM-004-03/005-01/006-01` 已全局唯一（ledger `system_data_schema_migrations`），跨服务唯一性待集成阶段复核。

## 范围外发现

无。`IIdentityUserDirectory`（用户目录对接）属于 SD-006，未在本任务范围。
