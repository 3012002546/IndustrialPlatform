# 02-Industrial Platform BuildingBlocks基础组件开发实施方案

# Industrial Platform BuildingBlocks基础组件开发实施方案

> 当前里程碑状态：BuildingBlocks 基础组件及 Entity 生命周期/并发调整均已完成；本文统一记录当前设计、历史任务、验证证据和后续服务复用契约。

版本：V2.0

所属项目开发路线阶段：Phase 0「BuildingBlocks 原基础搭建」与 Phase 0A「Entity 生命周期与并发调整」（均已完成）；阶段定义见《01-Industrial Platform开发启动实施方案》第 2 节“开发阶段总体规划”。

模块：

```text
IndustrialPlatform.BuildingBlocks
```

技术：

```text
.NET 10
Clean Architecture
DDD
SqlSugar
Redis
RabbitMQ
Serilog
```

规格与蓝图依据：

- `docs/blueprint/07-PostgreSQL数据库规范及分库设计.md`
- `docs/blueprint/08-RabbitMQ事件总线设计规范.md`
- `docs/blueprint/12-.NET10 Clean Architecture模板设计.md`
- `docs/blueprint/26-Industrial Platform数据库最终模型.md`
- `docs/blueprint/27-Industrial Platform API规范.md`
- `docs/blueprint/29-Industrial Platform自动化测试体系.md`
- `docs/blueprint/30-Industrial Platform日志审计与可观测性平台设计.md`
- `docs/blueprint/31-Industrial Platform权限体系与安全架构设计.md`
- `docs/superpowers/specs/2026-08-09-entity-lifecycle-concurrency-soft-delete-design.md`

---

# 1. 文档说明

## 1.1 文档目的

BuildingBlocks 是 Industrial Platform 所有微服务共享的技术基础。

本文统一定义：

- 七个 BuildingBlocks 项目的职责和引用边界。
- Entity、AggregateRoot、ValueObject、事件、Result 和异常契约。
- SqlSugar 仓储、工作单元、Redis、RabbitMQ、日志、安全和 Web 基础能力。
- Entity 冻结、锁定、软删除、恢复和双版本并发基线。
- 自动化测试、关键技术决策和已知限制。
- 已完成任务的统一九字段记录、完成标准和执行证据。

## 1.2 当前实现状态

- 七个 BuildingBlocks 类库已经创建并加入解决方案。
- SharedKernel、Infrastructure、EventBus、Logging、Security 和 Web 基础能力已经实现。
- Entity 生命周期、双版本并发和软删除仓储已经完成。
- `CLAUDE.md` 当前记录 BuildingBlocks 测试 102/102、当时全解决方案 140/140 通过。
- 本次 V2.0 仅统一文档格式，没有重新运行构建或测试；所有测试数量均标记为历史证据。

## 1.3 使用规则

后续服务可以复用 BuildingBlocks，但不得：

- 将 MES 业务逻辑放入 BuildingBlocks。
- 绕过 Entity 生命周期和并发契约。
- 修改公共接口而不评估全部消费者。
- 在单个服务任务中顺手扩展通用组件。

发现公共能力缺口时，必须建立独立 BuildingBlocks 变更任务、兼容性评估和全解决方案验证。

---

# 2. 设计目标与边界

## 2.1 建设目标

建立统一的：

```text
领域模型基础
应用层基础
数据访问基础
缓存与分布式锁
消息通信基础
日志与TraceId
用户上下文
异常与API响应
```

## 2.2 BuildingBlocks包含

- 业务无关的 Entity、AggregateRoot、ValueObject 和领域事件接口。
- Result、异常、Repository 和 UnitOfWork 抽象。
- SqlSugar、Redis 和 RabbitMQ 的通用适配。
- Serilog、TraceId、当前用户和 Web 中间件。
- 可被所有服务复用的 DI 注册扩展。

## 2.3 BuildingBlocks不包含

- 用户、物料、工单、库存、称量、设备和追溯实体。
- 任一服务专属 DTO、数据库表和 API。
- 业务权限码和业务状态机。
- 服务间工作流编排。
- 客户项目专属规则和配置。

错误示例：

```text
IndustrialPlatform.SharedKernel.WorkOrderEntity
```

正确位置：

```text
IndustrialPlatform.WorkOrder.Domain.WorkOrder
```

---

# 3. 设计原则

核心原则：

```text
稳定
+
业务无关
+
低耦合
+
可测试
+
向后兼容优先
```

规则：

- SharedKernel 不依赖数据库、缓存、消息队列或 Web 框架。
- 公共抽象保持最小，不为单一服务增加专用方法。
- Infrastructure 只实现技术适配，不包含领域决策。
- Web 只处理协议、异常、结果和请求日志。
- Security 只提供认证用户上下文和声明常量，不拥有 Identity 业务。
- EventBus 只负责集成事件传输，不承担业务事务和 Saga 决策。

---

# 4. 项目结构

位置：

```text
src/backend/src/BuildingBlocks
```

结构：

```text
BuildingBlocks
├── IndustrialPlatform.SharedKernel
├── IndustrialPlatform.Application.Abstractions
├── IndustrialPlatform.Infrastructure
├── IndustrialPlatform.EventBus
├── IndustrialPlatform.Logging
├── IndustrialPlatform.Security
└── IndustrialPlatform.Web
```

测试：

```text
tests/BuildingBlocks/IndustrialPlatform.BuildingBlocks.Tests
```

---

# 5. 项目职责与引用关系

## 5.1 项目职责

| 项目 | 职责 |
| --- | --- |
| SharedKernel | Entity、聚合、值对象、事件、Result、异常、Repository/UoW 抽象 |
| Application.Abstractions | 应用层公共边界和程序集标识 |
| Infrastructure | SqlSugar、Redis、仓储和工作单元实现 |
| EventBus | RabbitMQ 连接、发布、消费、订阅和集成事件 |
| Logging | Serilog 配置、Console/File/Seq 和 TraceId enrich |
| Security | ICurrentUser、Claims 和 HttpContext 用户解析 |
| Web | ApiResult、PageResult、异常/请求日志中间件和结果过滤 |

## 5.2 当前实际引用关系

```text
Application.Abstractions → SharedKernel

Infrastructure → Application.Abstractions + SharedKernel

EventBus → SharedKernel

Security → Application.Abstractions

Web → Application.Abstractions

Logging → 无 BuildingBlocks 项目引用
```

架构测试锁定：

- SharedKernel 不引用其他 BuildingBlocks 项目。
- Domain 级抽象不能反向引用 Infrastructure。
- BuildingBlocks 不引用业务服务项目。

---

# 6. 全局技术约束

- 目标框架固定 `net10.0`。
- Nullable 开启，警告视为错误，分析级别使用 latest-recommended。
- 包版本使用 Central Package Management，在根 `Directory.Packages.props` 统一声明。
- 标识使用 `Guid`。
- 时间使用 `DateTimeOffset`，禁止 `DateTime`。
- 获取当前时间使用 `DateTimeOffset.UtcNow`。
- PostgreSQL 瞬时时间映射 `timestamptz`。
- 所有异步公共接口接收 `CancellationToken`。
- 公共接口变更必须运行架构测试、BuildingBlocks 全量测试和全解决方案测试。

---

# 7. SharedKernel结构

```text
IndustrialPlatform.SharedKernel
├── Entities
├── Events
├── ValueObjects
├── Results
├── Exceptions
└── Interfaces
```

SharedKernel 不引用 SqlSugar、Redis、RabbitMQ、Serilog、ASP.NET Core 或任何服务项目。

---

# 8. Entity生命周期设计

当前 Entity 字段：

```csharp
public abstract class Entity
{
    public Guid Id { get; protected set; }
    public bool IsFrozen { get; protected set; }
    public bool IsLocked { get; protected set; }
    public bool IsDeleted { get; protected set; }
    public string EntityType { get; protected set; }
    public DateTimeOffset CreatedOn { get; protected set; }
    public DateTimeOffset LastUpdatedOn { get; protected set; }
    public long OptimisticVersion { get; protected set; }
    public Guid ConcurrencyVersion { get; protected set; }
}
```

创建规则：

- `Id` 和 `ConcurrencyVersion` 为非空 Guid。
- `CreatedOn` 与 `LastUpdatedOn` 使用同一个 UTC 时间值。
- `EntityType` 是具体运行时类型的完整名，无法获取时回退短名。
- 三个状态标记为 false。
- `OptimisticVersion` 初始为 0。

派生实体普通修改：

```text
EnsureCanModify()
    ↓
修改领域字段
    ↓
Touch()
```

`EnsureCanModify()` 阻止对已删除、已锁定或已冻结实体的普通修改。

---

# 9. 状态转换设计

公共状态方法：

```text
Freeze / Unfreeze
Lock / Unlock
MarkDeleted / Restore
```

规则：

- 重复到达相同目标状态时幂等，不推进版本。
- 每次实际变化更新 `LastUpdatedOn`、递增 `OptimisticVersion`、重新生成 `ConcurrencyVersion`。
- 删除、锁定、冻结之间的非法组合抛 `BusinessException`。
- 异常包含实体类型和操作名，但不暴露敏感数据。
- `Restore` 恢复软删除实体，不执行物理重建。

物理删除不属于通用业务仓储，只允许独立运维/合规流程显式实现。

---

# 10. AggregateRoot、ValueObject与领域事件

## 10.1 AggregateRoot

AggregateRoot 继承 Entity，并维护领域事件集合：

```text
AddDomainEvent
DomainEvents
ClearDomainEvents
```

领域事件只表示聚合内已经发生的事实，不直接依赖 RabbitMQ。

## 10.2 ValueObject

值对象：

- 无独立标识。
- 通过组成字段比较相等性。
- 设计为不可变。
- 不承担仓储和外部调用。

## 10.3 IDomainEvent

```csharp
public interface IDomainEvent
{
    DateTimeOffset OccurredOn { get; }
}
```

领域事件和集成事件必须区分：领域事件在进程内表达事实，集成事件通过 EventBus 跨服务发布。

---

# 11. Result与异常体系

## 11.1 Result

SharedKernel 提供：

```text
Result
Result<T>
```

工厂方法放在非泛型 `Result` 类，避免 CA1000 泛型静态成员分析警告。

无约束泛型 `T? Data` 的当前语义：

- 引用类型失败时可以为 null。
- 值类型失败时为 default，不把 `Result<int>.Data` 解释为可空 int。

## 11.2 异常

```text
DomainException
├── BusinessException
├── ValidationException
├── UnauthorizedException
├── NotFoundException
└── ConcurrencyException
```

Domain/Application 抛出语义异常，Web 中间件统一映射 HTTP 状态和 ApiResult；页面或 Controller 不自行拼接异常响应。

---

# 12. Repository与UnitOfWork抽象

当前仓储接口：

```csharp
public interface IRepository<TEntity>
    where TEntity : Entity
{
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task UpdateAsync(TEntity entity, long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion, CancellationToken cancellationToken = default);
    Task DeleteAsync(TEntity entity, long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion, CancellationToken cancellationToken = default);
    Task RestoreAsync(TEntity entity, long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion, CancellationToken cancellationToken = default);
}
```

规则：

- GetById 默认排除软删除记录。
- Update/Delete/Restore 必须接收调用方读取时的两个原始版本。
- 冲突抛 `ConcurrencyException`，不得覆盖数据库较新记录。
- 各服务可以定义面向聚合的专用 Repository，但不得削弱并发条件。

工作单元：

```csharp
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

---

# 13. SqlSugar基础设施设计

结构：

```text
IndustrialPlatform.Infrastructure
├── Database
│   ├── SqlSugarOptions
│   └── SqlSugarDbContext
├── Repository
│   └── BaseRepository<TEntity>
├── Transaction
│   └── SqlSugarUnitOfWork
└── Extensions
    └── SqlSugarServiceCollectionExtensions
```

BaseRepository 行为：

- Add 保存新实体。
- GetById 默认增加 `IsDeleted=false` 过滤。
- Update 使用 `Id + IsDeleted=false + OptimisticVersion + ConcurrencyVersion` 原子更新。
- Delete 调用 `MarkDeleted()` 并执行 UPDATE，不执行物理 DELETE。
- Restore 使用 `Id + IsDeleted=true + 双版本` 原子更新。
- 条件更新影响行数不是 1 时抛并发异常。

SharedKernel 不引用 SqlSugar；SqlSugar 映射和表达式只存在于 Infrastructure。

---

# 14. Redis缓存与分布式锁

## 14.1 缓存接口

```text
SetAsync<T>
GetAsync<T>
RemoveAsync
GetOrAddAsync<T>
```

缓存键、TTL 和失效规则由具体服务定义，BuildingBlocks 不包含业务键前缀。

## 14.2 分布式锁

```text
TryAcquireAsync(key, token, expiry)
ReleaseAsync(key, token)
```

释放锁只有 token 与持有者匹配时才删除。

## 14.3 连接行为

Redis 连接使用 `AbortOnConnectFail=false`：依赖不可达时返回断开的复用器并后台重试，避免 DI 首次解析直接抛异常导致健康检查逃逸为 500。真实操作失败仍向调用方明确报告。

---

# 15. RabbitMQ EventBus设计

结构：

```text
IndustrialPlatform.EventBus
├── Abstractions
├── Connection
├── Events
├── Producer
├── Consumer
├── Subscriptions
├── Options
└── Extensions
```

能力：

- `IntegrationEvent` / `IIntegrationEvent`。
- `IEventBus.PublishAsync<TEvent>()`。
- 消费者注册、订阅管理和后台消费。
- RabbitMQ 连接、交换机、队列和路由配置。

约束：

- EventBus 提供至少一次传输基础，不承诺 exactly-once。
- 业务服务负责 Outbox、Inbox、幂等和死信处置策略。
- 事件契约放在服务 Contracts，不放入 BuildingBlocks。
- routingKey 未指定时使用事件类型名。

---

# 16. Logging设计

结构：

```text
IndustrialPlatform.Logging
├── Options
├── Enrichers
├── Internal
└── Extensions
```

支持：

```text
Console
File
Seq
TraceId
```

日志配置通过 `SerilogOptions` 绑定。TraceIdEnricher 为日志增加请求关联标识。

要求：

- 服务名由调用服务配置。
- 不记录密码、Token、连接串和密钥。
- File/Seq 可按环境启停。
- 日志初始化失败和外部 Sink 不可用行为必须可诊断。

---

# 17. Security设计

Security 只提供当前用户上下文：

```text
ICurrentUser
CurrentUser
ClaimConstants
AddCurrentUser
```

当前接口：

```text
IsAuthenticated
UserId（Guid?）
UserName
TenantId
Roles
```

ClaimConstants：

```text
sub
user_name
tenant_id
role
```

Security 不负责登录、密码、令牌签发、角色持久化和业务权限判定；这些属于 Identity 或具体业务服务。

---

# 18. Web基础组件设计

结构：

```text
IndustrialPlatform.Web
├── Results
├── Middleware
├── Filters
└── Extensions
```

组件：

| 组件 | 职责 |
| --- | --- |
| `ApiResult` / `ApiResult<T>` | 统一 API 信封 |
| `PageResult<T>` | 统一分页数据 |
| `ExceptionMiddleware` | 异常到 HTTP/ApiResult 映射 |
| `RequestLoggingMiddleware` | 请求耗时、结果和 TraceId 日志 |
| `ResultFilter` | 将控制器结果包装为统一信封 |
| `UseIndustrialWeb` | 注册统一 Web 管线 |

ApiResult 字段：

```text
success
code
message
data
```

已经是 ApiResult 的响应不得重复包装。健康检查、文件、流和明确排除的端点不强制套业务信封。

---

# 19. 包版本与配置

当前中央包版本：

| 包 | 版本 |
| --- | --- |
| SqlSugarCore | `5.1.4.216` |
| StackExchange.Redis | `3.1.3` |
| RabbitMQ.Client | `7.2.2` |
| Serilog | `4.4.0` |
| Serilog.AspNetCore | `10.0.0` |
| Serilog.Settings.Configuration | `10.0.1` |
| Serilog.Sinks.Console | `6.1.1` |
| Serilog.Sinks.File | `7.0.0` |
| Serilog.Sinks.Seq | `9.1.0` |
| SQLitePCLRaw.bundle_e_sqlite3 | `2.1.12` |
| xunit | `2.9.3` |

新增或升级包必须：

- 在根 `Directory.Packages.props` 更新。
- 运行 deprecated/vulnerable 审计。
- 评估全部 BuildingBlocks 消费者。
- 保持零警告构建。

---

# 20. 自动化测试设计

测试文件覆盖：

```text
AssemblyBoundaryTests
ProjectReferenceArchitectureTests
EntityTests
ValueObjectTests
ResultTests
ExceptionTests
BaseRepositoryTests
SqlSugarRegistrationTests
RedisRegistrationTests
EventBusTests
EventBusRegistrationTests
LoggingTests
SecurityTests
WebTests
ResultFilterTests
WebRegistrationTests
```

核心必测：

- 项目引用边界。
- Entity 默认值、状态转换、幂等、非法状态和版本推进。
- 软删除、恢复、默认过滤和双版本冲突。
- Result 泛型语义和异常继承。
- SqlSugar/Redis/EventBus/Logging/Security/Web DI 注册。
- 统一响应、异常映射和敏感信息保护。

历史验证记录：

- 基础组件完成时 BuildingBlocks 64/64 通过。
- Entity 生命周期调整后 BuildingBlocks 102/102 通过。
- `CLAUDE.md` 当前记录当时全解决方案 140/140 通过。

这些是历史证据，本次文档格式调整未重新执行测试。

---

# 21. 关键技术决策与已知限制

## 21.1 关键决策

- `Result<T>` 工厂方法位于非泛型 Result，满足 CA1000。
- `ICurrentUser.UserId` 使用 Guid，与 Entity.Id 一致。
- Entity 使用 `OptimisticVersion(long) + ConcurrencyVersion(Guid)` 双版本。
- 删除为软删除，通用 Repository 不提供物理删除。
- Redis 使用 `AbortOnConnectFail=false` 支持健康检查可诊断降级。
- SQLitePCLRaw 固定 2.1.12，保持历史漏洞和 RID 警告清理结果。

## 21.2 已知限制

- SqlSugar 5.1.4 SQLite provider 读写 DateTimeOffset 时可能丢失 UTC offset；SQLite 仓储测试按墙钟一致验证。
- PostgreSQL `timestamptz` 的精确映射必须由具体服务真实数据库集成测试验证。
- xunit 2.9.3 被 NuGet 标记为 Legacy；迁移 xunit.v3 涉及测试 SDK 和断言兼容，应作为独立任务，不在文档整理中处理。
- EventBus 当前提供基础传输；Outbox、Inbox、死信和业务重试由服务实施方案定义。

---

# 22. 任务依赖

```text
Task-001
├→ Task-002 → Task-003 → Task-004
├→ Task-005
├→ Task-006
├→ Task-007
├→ Task-008
└→ Task-009

Task-001～009 → TASK-BB-010
```

说明：

- Task-001～009 是原始 BuildingBlocks 基础搭建任务，均已完成。
- TASK-BB-010 是后续批准的 Entity 生命周期、双版本并发和软删除调整，已完成。
- 已完成任务不得重新派遣；公共能力后续变更必须建立新任务编号。

---

# 23. BuildingBlocks开发任务拆分

## Task-001 创建项目结构

**状态：** 已完成

**目标：** 创建七个 BuildingBlocks 类库、测试项目、解决方案注册和项目引用边界。

**输入文档：** 本文第 3～6 节；Clean Architecture 与 DDD 分层蓝图。

**依赖：** 无。

**允许修改范围：** BuildingBlocks 项目、BuildingBlocks 测试和解决方案文件；不得实现 MES 业务。

**预期输出：** 七个 class library、程序集标识、测试项目和架构边界测试。

**验证与证据：** 历史提交 `b7ec369` 创建项目骨架；后续架构测试持续验证引用关系。该证据未在本轮重跑。

**结果回写：** 项目路径、引用关系和解决方案注册已反映在本文第 4、5 节。

**建议提交：** 历史提交 `b7ec369 feat: scaffold building blocks projects`

---

## Task-002 Entity基础模型

**状态：** 已完成（最终生命周期见 TASK-BB-010）

**目标：** 建立 Entity、AggregateRoot、ValueObject 和 IDomainEvent 原始领域基础。

**输入文档：** 本文第 7～10 节。

**依赖：** Task-001。

**允许修改范围：** SharedKernel Entities/ValueObjects/Events 和对应测试。

**预期输出：** 可被业务领域继承的实体、聚合、值对象和领域事件基类/接口。

**验证与证据：** 历史实现归入 `cd37911`；基础完成阶段 BuildingBlocks 测试包含在 64/64 通过记录中。最终 Entity 行为由 102/102 历史测试覆盖。

**结果回写：** 原始 Entity 字段已由 TASK-BB-010 替换，当前契约见第 8～10 节。

**建议提交：** 历史归档提交 `cd37911 feat: complete building blocks and align operational data blueprint`

---

## Task-003 Result组件

**状态：** 已完成

**目标：** 实现统一 Result 和 Result<T> 领域/应用返回模型。

**输入文档：** 本文第 11.1 节。

**依赖：** Task-002。

**允许修改范围：** SharedKernel Results 和测试。

**预期输出：** 成功/失败工厂、消息和类型化 Data 语义。

**验证与证据：** ResultTests 覆盖工厂、成功失败和引用/值类型 Data；历史 BuildingBlocks 64/64 记录通过。

**结果回写：** CA1000 和无约束泛型语义记录在第 11、21 节。

**建议提交：** 历史归档提交 `cd37911`

---

## Task-004 Exception组件

**状态：** 已完成

**目标：** 建立领域异常基类和业务、校验、未授权、未找到异常。

**输入文档：** 本文第 11.2、18 节。

**依赖：** Task-003。

**允许修改范围：** SharedKernel Exceptions、Web 异常映射和测试。

**预期输出：** DomainException 体系及统一 Web 映射基础。

**验证与证据：** ExceptionTests 和 WebTests 历史通过；ConcurrencyException 后由 TASK-BB-010 增加。

**结果回写：** 当前异常树和 Web 边界见第 11、18 节。

**建议提交：** 历史归档提交 `cd37911`

---

## Task-005 SqlSugar基础组件

**状态：** 已完成（最终并发仓储见 TASK-BB-010）

**目标：** 实现 SqlSugar 配置、DbContext、通用仓储、工作单元和 DI 注册。

**输入文档：** 本文第 12、13、19 节。

**依赖：** Task-001、Task-002。

**允许修改范围：** Infrastructure Database/Repository/Transaction/Extensions、SharedKernel 仓储接口和测试。

**预期输出：** SqlSugarOptions、SqlSugarDbContext、BaseRepository、SqlSugarUnitOfWork 和注册扩展。

**验证与证据：** SqlSugarRegistrationTests、BaseRepositoryTests 和架构测试历史通过；最终软删除/双版本行为计入 BuildingBlocks 102/102。

**结果回写：** 当前仓储签名和原子更新规则见第 12、13 节。

**建议提交：** 基础实现 `cd37911`；并发调整 `c0b01b0`

---

## Task-006 Redis组件

**状态：** 已完成

**目标：** 实现通用缓存、GetOrAdd 和 token 安全释放的分布式锁。

**输入文档：** 本文第 14、19 节。

**依赖：** Task-001。

**允许修改范围：** Infrastructure Caching/Redis Extensions 和测试。

**预期输出：** ICacheService、CacheService、IDistributedLock、RedisDistributedLock、RedisOptions 和 DI 注册。

**验证与证据：** RedisRegistrationTests 历史通过；TASK-BASE-003 后增加 `AbortOnConnectFail=false` 并通过依赖健康检查测试。

**结果回写：** 缓存接口、锁和连接降级行为见第 14 节。

**建议提交：** 基础实现 `cd37911`；连接行为调整归入 `c0b01b0`

---

## Task-007 RabbitMQ组件

**状态：** 已完成

**目标：** 实现 RabbitMQ 连接、集成事件发布、消费、订阅管理和 DI 注册。

**输入文档：** 本文第 15、19 节；RabbitMQ 蓝图。

**依赖：** Task-001。

**允许修改范围：** EventBus 全项目及对应测试。

**预期输出：** IEventBus、RabbitMqEventBus、连接、消费者后台服务、订阅管理、Options 和注册扩展。

**验证与证据：** EventBusTests 和 EventBusRegistrationTests 历史通过，计入基础阶段 BuildingBlocks 64/64。

**结果回写：** 当前能力和 Outbox/Inbox 边界见第 15、21 节。

**建议提交：** 历史归档提交 `cd37911`

---

## Task-008 Logging组件

**状态：** 已完成

**目标：** 实现统一 Serilog 配置、Console/File/Seq Sink 和 TraceId enrich。

**输入文档：** 本文第 16、19 节；可观测性蓝图。

**依赖：** Task-001。

**允许修改范围：** Logging 项目和测试。

**预期输出：** SerilogOptions、配置构建器、TraceIdEnricher 和 DI 扩展。

**验证与证据：** LoggingTests 历史验证选项绑定、Sink 和 TraceId；计入基础阶段 BuildingBlocks 64/64。

**结果回写：** 日志能力和敏感信息边界见第 16 节。

**建议提交：** 历史归档提交 `cd37911`

---

## Task-009 Security与Web补充组件

**状态：** 已完成

**目标：** 实现当前用户上下文、声明常量、统一 API/分页结果、异常/请求日志中间件和结果过滤。

**输入文档：** 本文第 17、18 节；API 和安全蓝图。

**依赖：** Task-001、Task-003、Task-004。

**允许修改范围：** Security、Web 项目和对应测试。

**预期输出：** ICurrentUser、CurrentUser、ClaimConstants、ApiResult、PageResult、ExceptionMiddleware、RequestLoggingMiddleware、ResultFilter 和注册扩展。

**验证与证据：** SecurityTests、WebTests、ResultFilterTests 和注册测试历史通过，计入基础阶段 BuildingBlocks 64/64。

**结果回写：** 当前用户、Claims 和 Web 信封契约见第 17、18 节。

**建议提交：** 历史归档提交 `cd37911`

---

## TASK-BB-010 调整Entity生命周期、并发与软删除基线

**状态：** 已完成（2026-08-10）

**目标：** 替换旧 Entity 审计字段，增加冻结、锁定、软删除、实体类型、双版本并发和并发安全仓储。

**输入文档：** Entity 生命周期规格；本文第 8、9、12、13、20、21 节。

**依赖：** Task-001 至 Task-009 已完成；在业务实体开发和剩余运行基线任务前完成。

**允许修改范围：** SharedKernel Entity/Exceptions/Interfaces、Infrastructure Repository、BuildingBlocks 测试；因接口影响只允许 Identity/ReferenceData 骨架最小签名适配。

**预期输出：** Entity 九字段、EnsureCanModify/Touch、六个状态方法、ConcurrencyException、Update/Delete/Restore 双版本接口、软删除过滤和原子条件更新；删除旧 CreateTime/ModifyTime/Version，不保留别名。

**验证与证据：** 历史记录：SDK 10.0.302，restore/build 0 警告 0 错误；当时全解决方案 112/112，通过后 BuildingBlocks 102/102；随后 `CLAUDE.md` 记录全解决方案扩展为 140/140。覆盖默认值、时间相等、EntityType、状态幂等/保护、版本推进、软删除、过滤、双版本冲突和较新记录保护。本轮未重跑。

**结果回写：** 当前 Entity、Repository 和测试限制已回写本文第 8、9、12、13、21 节及相关蓝图。

**建议提交：** 历史实现归入 `c0b01b0 feat(platform): integrate entity lifecycle and runnable baseline`

---

# 24. BuildingBlocks完成标准

当前已达到：

- 七个项目职责明确，架构测试锁定实际引用关系。
- SharedKernel 不依赖技术框架或业务服务。
- Entity 具备冻结、锁定、软删除、恢复和双版本并发。
- Repository 默认过滤软删除并使用原子条件更新。
- Result、异常、Repository、UnitOfWork 契约可被服务复用。
- SqlSugar、Redis、RabbitMQ、Serilog 具备统一配置和 DI 注册。
- Security 提供 Guid 用户上下文和稳定 Claim 名称。
- Web 提供统一 API/分页信封、异常和请求日志管线。
- BuildingBlocks 测试历史记录 102/102 通过。
- 未包含 MES 或客户项目业务逻辑。

本次 V2.0 只完成文档格式统一，不改变代码完成状态，也不产生新的测试验收结论。

---

# 25. 执行记录

| 任务 | 状态 | 执行者/任务 | 提交 | 验证证据 | 结果回写 |
| --- | --- | --- | --- | --- | --- |
| Task-001 | 已完成 | 历史任务 | `b7ec369` | 项目/解决方案骨架；架构测试后续持续通过 | 第 4、5 节 |
| Task-002 | 已完成 | 历史任务 | `cd37911` | 基础 64/64；最终 Entity 测试计入 102/102 | 第 7～10 节 |
| Task-003 | 已完成 | 历史任务 | `cd37911` | ResultTests 历史通过 | 第 11、21 节 |
| Task-004 | 已完成 | 历史任务 | `cd37911` | ExceptionTests/WebTests 历史通过 | 第 11、18 节 |
| Task-005 | 已完成 | 历史任务 | `cd37911`、`c0b01b0` | DI、仓储、软删除、并发测试计入 102/102 | 第 12、13 节 |
| Task-006 | 已完成 | 历史任务 | `cd37911`、`c0b01b0` | Redis DI 与健康检查历史通过 | 第 14 节 |
| Task-007 | 已完成 | 历史任务 | `cd37911` | EventBus/注册测试历史通过 | 第 15 节 |
| Task-008 | 已完成 | 历史任务 | `cd37911` | LoggingTests 历史通过 | 第 16 节 |
| Task-009 | 已完成 | 历史任务 | `cd37911` | Security/Web/Filter/注册测试历史通过 | 第 17、18 节 |
| TASK-BB-010 | 已完成 | 历史任务 | `c0b01b0` | SDK 10.0.302；build 0/0；当时 112/112，后续 BB 102/102、全量 140/140 | 第 8、9、12、13、21 节 |

---

# 26. 后续服务输入契约

后续服务可以依赖：

```text
Entity生命周期与双版本并发
AggregateRoot与领域事件集合
ValueObject相等性
Result与DomainException
IRepository与IUnitOfWork
SqlSugar/Redis/EventBus/Logging注册扩展
ICurrentUser与ClaimConstants
ApiResult、PageResult与Industrial Web管线
```

后续服务必须自行定义：

- 领域聚合和业务不变量。
- 服务专属 Repository 查询。
- 数据库映射、业务唯一索引和迁移。
- 缓存键、TTL 和失效策略。
- 事件 Contracts、Outbox、Inbox 和幂等。
- 业务权限、数据权限和审计内容。

---

# 27. 公共组件变更规则

BuildingBlocks 后续变更统一采用：

```text
独立设计/缺口说明
    ↓
影响消费者清单
    ↓
新任务编号与九字段任务卡
    ↓
失败测试
    ↓
最小兼容实现
    ↓
BuildingBlocks + 全解决方案验证
    ↓
版本与迁移说明
```

禁止直接覆盖已完成 Task-001～009 或 TASK-BB-010 的历史记录。新能力使用新的 `TASK-BB-XXX` 编号，并在本文执行记录追加一行。
