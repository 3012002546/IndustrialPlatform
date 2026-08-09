# Entity 生命周期、并发与软删除设计

## 1. 目标

调整 BuildingBlocks 的 `Entity` 基类，为所有业务实体提供一致的生命周期状态、审计时间、实体类型、乐观并发令牌和软删除语义，同时避免 SharedKernel 依赖 SqlSugar 或 PostgreSQL。

本次变更发生在业务实体尚未开发的阶段，可以直接替换旧的 `CreateTime`、`ModifyTime` 和 `Version`，不保留兼容别名。

## 2. 统一字段

```csharp
public Guid Id { get; protected set; }
public bool IsFrozen { get; protected set; }
public bool IsLocked { get; protected set; }
public bool IsDeleted { get; protected set; }
public string EntityType { get; protected set; }
public DateTimeOffset CreatedOn { get; protected set; }
public DateTimeOffset LastUpdatedOn { get; protected set; }
public long OptimisticVersion { get; protected set; }
public Guid ConcurrencyVersion { get; protected set; }
```

字段约束：

- `Id` 创建时生成，也允许由受保护构造函数传入。
- `IsFrozen`、`IsLocked`、`IsDeleted` 初始值均为 `false`。
- `EntityType` 保存具体运行时实体的完整类型名，即 `GetType().FullName`；若运行时无法提供完整名，则回退到 `GetType().Name`。
- 构造实体时只读取一次 `DateTimeOffset.UtcNow`，将同一个值赋给 `CreatedOn` 和 `LastUpdatedOn`。
- `CreatedOn` 和 `LastUpdatedOn` 均不可为空。
- `OptimisticVersion` 为 `long`，初始值为 `0`。
- `ConcurrencyVersion` 为 `Guid`，创建时生成且不得为 `Guid.Empty`。
- `IsDeleted` 保持布尔类型，不使用数字删除标记。

## 3. 状态与时间推进

实体提供明确的受保护或公共状态行为，禁止业务代码直接设置属性：

- `EnsureCanModify()`：派生实体在修改自身字段前调用，集中校验删除、锁定和冻结状态。
- `Touch()`：普通业务修改完成后推进审计与并发字段。
- `Freeze()` / `Unfreeze()`：冻结或解冻业务修改。
- `Lock()` / `Unlock()`：锁定或解锁管理操作。
- `MarkDeleted()`：标记软删除。
- `Restore()`：恢复软删除实体。

每次实际状态变化或普通业务修改都执行一次统一推进：

```text
LastUpdatedOn = DateTimeOffset.UtcNow
OptimisticVersion += 1
ConcurrencyVersion = Guid.NewGuid()
```

重复调用已经满足目标状态的方法不产生新的版本：例如已冻结实体再次 `Freeze()`、已删除实体再次 `MarkDeleted()` 均保持幂等。

## 4. 修改保护规则

普通业务修改必须在变更字段前调用 `EnsureCanModify()`，完成实际字段修改后调用 `Touch()`；规则如下：

- `IsDeleted = true`：禁止普通业务修改、冻结、解冻、锁定和解锁，只允许恢复。
- `IsLocked = true`：禁止普通业务修改、冻结、解冻和软删除，只允许解锁。
- `IsFrozen = true`：禁止普通业务修改、锁定和软删除，只允许解冻。
- 恢复后保留删除前的冻结和锁定状态；由于软删除仅允许未冻结且未锁定实体执行，正常恢复结果为未冻结、未锁定。

状态冲突抛出明确的 `BusinessException`，错误信息包含操作和实体类型，不包含敏感业务数据。

## 5. 并发更新契约

调用方读取实体后必须保存两个原始版本值：

```csharp
long expectedOptimisticVersion;
Guid expectedConcurrencyVersion;
```

普通更新由领域行为先修改实体并推进新版本；仓储更新接口接收原始版本：

```csharp
Task UpdateAsync(
    TEntity entity,
    long expectedOptimisticVersion,
    Guid expectedConcurrencyVersion,
    CancellationToken cancellationToken = default);
```

更新 SQL 的条件必须同时包含：

```sql
WHERE "Id" = @id
  AND "IsDeleted" = false
  AND "OptimisticVersion" = @expectedOptimisticVersion
  AND "ConcurrencyVersion" = @expectedConcurrencyVersion
```

更新值包含实体当前的 `LastUpdatedOn`、`OptimisticVersion` 和新 `ConcurrencyVersion`。影响行数不是 `1` 时抛出专用并发异常，不把并发冲突伪装成未找到。

## 6. 软删除与恢复

`DeleteAsync` 改为软删除，不执行物理 `DELETE`：

1. 校验调用方提交的两个原始版本。
2. 调用 `MarkDeleted()`。
3. 执行带 `Id`、`IsDeleted = false` 和双版本条件的原子 `UPDATE`。
4. 影响行数不是 `1` 时抛出并发异常。

仓储接口相应接收原始版本：

```csharp
Task DeleteAsync(
    TEntity entity,
    long expectedOptimisticVersion,
    Guid expectedConcurrencyVersion,
    CancellationToken cancellationToken = default);
```

默认 `GetByIdAsync` 和通用查询必须过滤 `IsDeleted = true`。查询已删除记录与恢复属于显式能力，不通过默认仓储方法泄漏；恢复也必须执行双版本原子校验。

恢复接口保持显式：

```csharp
Task RestoreAsync(
    TEntity entity,
    long expectedOptimisticVersion,
    Guid expectedConcurrencyVersion,
    CancellationToken cancellationToken = default);
```

物理删除只允许由独立的数据保留、归档或运维流程执行，不属于通用仓储接口。

## 7. PostgreSQL 索引策略

### 7.1 强制规则

每张实体表必须以 `Id` 为主键，使用 PostgreSQL 自动生成的唯一 B-tree 主键索引。

不强制创建以下索引：

- `(Id, IsDeleted)`：`Id` 已唯一定位最多一行，复合索引不能进一步缩小主键读取和并发更新的扫描范围。
- `IsDeleted` 单列索引：布尔列选择性低，通常无法为活跃数据查询提供足够收益。
- `LastUpdatedOn` 索引：是否创建由实际查询、同步和表规模决定。

### 7.2 活跃业务唯一性

需要唯一业务编码的实体使用部分唯一索引，只约束未删除记录。例如：

```sql
CREATE UNIQUE INDEX "UX_Material_Tenant_Code_Active"
ON "Material" ("TenantId", "Code")
WHERE "IsDeleted" = false;
```

该规则允许软删除后重用业务编码，同时保留历史记录。

### 7.3 可选更新时间索引

普通表存在更新时间排序、游标分页或增量同步时，使用稳定次序的复合 B-tree：

```sql
CREATE INDEX "IX_{Table}_LastUpdatedOn_Id"
ON "{Table}" ("LastUpdatedOn" DESC, "Id");
```

只同步活跃数据时可改为部分索引：

```sql
CREATE INDEX "IX_{Table}_Active_LastUpdatedOn_Id"
ON "{Table}" ("LastUpdatedOn" DESC, "Id")
WHERE "IsDeleted" = false;
```

超大且近似按时间顺序追加的流水或事件表，可经过 `EXPLAIN (ANALYZE, BUFFERS)` 验证后使用 BRIN：

```sql
CREATE INDEX "IX_{Table}_LastUpdatedOn_BRIN"
ON "{Table}" USING BRIN ("LastUpdatedOn");
```

不默认执行 PostgreSQL `CLUSTER`；如果个别只读或批量加载表需要物理重排，必须另行定义维护窗口和重新聚集策略。

### 7.4 分层边界

- SharedKernel 不引用 SqlSugar，不通过 ORM Attribute 声明数据库索引。
- Infrastructure 负责 SqlSugar 查询过滤、并发更新和软删除实现。
- 各服务迁移负责业务唯一索引和可选 `LastUpdatedOn` 索引。
- 架构或迁移测试验证主键、部分唯一索引和按需时间索引，不制造统一冗余索引。

## 8. 代码修改范围

预期修改：

- `IndustrialPlatform.SharedKernel/Entities/Entity.cs`
- `IndustrialPlatform.SharedKernel/Interfaces/IRepository.cs`
- SharedKernel 新增专用并发异常，或在现有异常体系中增加明确类型。
- `IndustrialPlatform.Infrastructure/Repository/BaseRepository{TEntity}.cs`
- BuildingBlocks 的 Entity、Repository 和数据库集成测试。
- 仍引用 `CreateTime`、`ModifyTime` 或 `Version` 的代码和测试。
- BuildingBlocks 实施文档、数据库规范及 `CLAUDE.md` 的实现进度由对应协作方按最终代码回写。

## 9. 测试要求

实体测试：

- 所有默认字段。
- `CreatedOn == LastUpdatedOn`。
- `EntityType` 等于具体派生类型完整名称。
- `Touch()` 推进时间、长整型版本和 Guid 令牌。
- Freeze/Unfreeze、Lock/Unlock、MarkDeleted/Restore 的状态和幂等性。
- 冻结、锁定和删除状态下的非法修改。

仓储测试：

- 新增实体保存全部统一字段。
- 默认按 Id 查询排除软删除实体。
- 软删除执行 `UPDATE` 而不是 `DELETE`。
- 正确原始版本更新成功。
- 任一原始版本不匹配时更新和删除失败。
- 并发失败不覆盖数据库中的较新记录。
- 显式恢复按双版本校验。

数据库与索引测试：

- `Id` 主键存在。
- 没有自动生成 `(Id, IsDeleted)` 或 `IsDeleted` 单列索引。
- 示例业务表的活跃业务键部分唯一索引存在且允许重用已删除编码。
- 仅配置了增量查询的表存在 `LastUpdatedOn, Id` 索引。

## 10. 完成标准

- 旧属性 `CreateTime`、`ModifyTime`、`Version` 已从代码与测试中移除。
- 新实体的 `CreatedOn` 与 `LastUpdatedOn` 严格相等且均非空。
- 所有状态变化都会正确推进双版本并发字段。
- 通用仓储默认过滤软删除，删除使用并发安全的原子更新。
- SharedKernel 不依赖 SqlSugar 或 PostgreSQL。
- 索引规则以查询场景为依据，不生成冗余的统一复合索引。
- 全解决方案构建与测试通过，警告数为零。
