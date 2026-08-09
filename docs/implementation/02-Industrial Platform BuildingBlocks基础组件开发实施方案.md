# 02-Industrial Platform BuildingBlocks基础组件开发实施方案

# Industrial Platform BuildingBlocks基础组件开发实施方案

> 当前里程碑范围：仅创建项目骨架；业务实现留待后续阶段。

版本：V1.0
阶段：Development Implementation Phase
模块：

IndustrialPlatform.BuildingBlocks

技术：

.NET 10

Clean Architecture

DDD

Microservices

---

# 1. 文档说明

## 1.1 文档目的

BuildingBlocks 是 Industrial Platform 所有微服务共享基础组件。

所有 Service：

* Identity Service
* ReferenceData Service
* MasterData Service
* OperationalData Service
* WorkOrder Service
* Weighting Service
* IoT Collector Service
* Trace Service
* Batch Record Service

均依赖 BuildingBlocks。

目标：

建立统一：

* 领域模型基础
* 应用层基础
* 数据访问基础
* 消息通信基础
* 日志基础
* 异常处理基础
* API响应规范

---

# 2. BuildingBlocks设计原则

## 2.1 核心原则

遵循：

```
稳定基础能力
+
业务无关
+
高复用
+
低耦合
```

BuildingBlocks禁止包含：

* MES业务逻辑
* 工单逻辑
* 称量逻辑
* 设备逻辑

例如：

错误：

```
BuildingBlocks

└── WorkOrderEntity
```

正确：

```
WorkOrder Service

└── WorkOrderEntity
```

---

## 2.2 时间类型规范

所有时间字段一律使用：

```
DateTimeOffset
```

禁止使用：

```
DateTime
```

原因：

* 保留时区偏移信息
* 跨时区存储与比较准确
* 工业/MES 场景的设备时间、班次时间常涉及时区换算

约定：

* 获取当前时间统一使用 `DateTimeOffset.UtcNow`
* PostgreSQL 时间列映射为 `timestamp with time zone`（`timestamptz`），以 UTC 保存
* API 响应中的时间序列化为带时区偏移的 ISO 8601 格式

---

# 3. 项目结构

位置：

```
src/BuildingBlocks
```

最终结构：

```
BuildingBlocks


├── IndustrialPlatform.SharedKernel


├── IndustrialPlatform.Application.Abstractions


├── IndustrialPlatform.Infrastructure


├── IndustrialPlatform.EventBus


├── IndustrialPlatform.Logging


├── IndustrialPlatform.Web


└── IndustrialPlatform.Security

```

---

# 4. 创建.NET项目

进入：

```
src/BuildingBlocks
```

创建：

```bash
dotnet new classlib \
-n IndustrialPlatform.SharedKernel
```

创建：

```bash
dotnet new classlib \
-n IndustrialPlatform.Application.Abstractions
```

创建：

```bash
dotnet new classlib \
-n IndustrialPlatform.Infrastructure
```

创建：

```bash
dotnet new classlib \
-n IndustrialPlatform.EventBus
```

创建：

```bash
dotnet new classlib \
-n IndustrialPlatform.Logging
```

创建：

```bash
dotnet new classlib \
-n IndustrialPlatform.Security
```

创建：

```bash
dotnet new classlib \
-n IndustrialPlatform.Web
```

---

# 5. 项目引用关系

依赖方向：

```
Security

        ↓

Web

        ↓

Application

        ↓

SharedKernel



Infrastructure

        ↓

SharedKernel



EventBus

        ↓

SharedKernel
```

规则：

禁止：

```
SharedKernel

引用

Infrastructure
```

---

# 6. SharedKernel设计

项目：

```
IndustrialPlatform.SharedKernel
```

职责：

DDD基础模型。

目录：

```
SharedKernel


├── Entities


├── Events


├── ValueObjects


├── Results


├── Exceptions


├── Constants


└── Extensions
```

---

# 7. Entity基类

所有实体继承非泛型 `Entity`。本节以规格 `docs/superpowers/specs/2026-08-09-entity-lifecycle-concurrency-soft-delete-design.md` 为准。

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

创建时 `CreatedOn` 与 `LastUpdatedOn` 使用同一个时间值；三个状态标记为 `false`；乐观版本为 `0`；并发版本为非空 Guid。

派生实体修改前调用 `EnsureCanModify()`，修改后调用 `Touch()`。Freeze/Unfreeze、Lock/Unlock、MarkDeleted/Restore 均为幂等显式状态转换；每次实际变化推进更新时间和双版本。

`IRepository<TEntity>` 的 Update/Delete/Restore 接收调用方原始 `OptimisticVersion` 与 `ConcurrencyVersion`。Infrastructure 使用 `Id + IsDeleted + 双版本` 原子更新；删除为软删除，默认查询排除已删除记录。SharedKernel 不引用 SqlSugar。

---

# 8. AggregateRoot设计

领域聚合根：

```
AggregateRoot
```

示例：

```csharp
public abstract class AggregateRoot
    : Entity
{

    private readonly List<IDomainEvent> _events = new();


    public IReadOnlyCollection<IDomainEvent> DomainEvents
        => _events;


    protected void AddDomainEvent(
        IDomainEvent domainEvent)
    {
        _events.Add(domainEvent);
    }


    public void ClearDomainEvents()
    {
        _events.Clear();
    }
}
```

---

# 9. ValueObject设计

值对象特点：

* 无唯一ID
* 不可变

例如：

物料编码：

```
MaterialNId
```

示例：

```csharp
public abstract class ValueObject
{
    protected abstract IEnumerable<object>
        GetEqualityComponents();


    public override bool Equals(
        object? obj)
    {
        return obj is ValueObject other
            &&
            GetEqualityComponents()
            .SequenceEqual(
                other.GetEqualityComponents());
    }
}
```

---

# 10. Domain Event设计

项目：

```
IndustrialPlatform.EventBus
```

领域事件：

示例：

工单创建：

```
WorkOrderCreatedEvent
```

模型：

```csharp
public interface IDomainEvent
{
    DateTimeOffset OccurredOn { get; }
}
```

---

# 11. Result统一返回模型

所有API统一：

成功：

```json
{
 "success":true,
 "data":{}
}
```

失败：

```json
{
 "success":false,
 "message":"error"
}
```

---

实现：

```csharp
public class Result<T>
{

    public bool Success {get;set;}


    public string? Message {get;set;}


    public T? Data {get;set;}

}
```

---

# 12. Exception体系

统一异常：

目录：

```
Exceptions
```

类型：

```
BusinessException

ValidationException

UnauthorizedException

NotFoundException
```

---

# 13. Application基础组件

项目：

```
IndustrialPlatform.Application.Abstractions
```

目录：

```
Application


├── Commands

├── Queries

├── DTOs

├── Validators

├── Interfaces

└── Behaviors
```

---

# 14. CQRS规范

Command：

修改数据。

例如：

```
CreateWorkOrderCommand
```

Query：

查询数据。

例如：

```
GetWorkOrderQuery
```

---

# 15. DTO规范

禁止：

Controller直接返回Entity。

错误：

```csharp
return order;
```

正确：

```csharp
return WorkOrderDto;
```

---

# 16. Repository接口

位置：

```
SharedKernel.Interfaces
```

定义：

```csharp
public interface IRepository<TEntity>
{
    Task<TEntity?> GetAsync(long id);


    Task AddAsync(
        TEntity entity);


    Task UpdateAsync(
        TEntity entity);
}
```

---

# 17. UnitOfWork设计

接口：

```csharp
public interface IUnitOfWork
{

    Task<int> SaveChangesAsync();

}
```

职责：

事务提交。

---

# 18. SqlSugar基础封装（已实现）

项目：

```
IndustrialPlatform.Infrastructure
```

目录：

```
Infrastructure


├── Database

├── Repository

├── Transaction

└── Extensions
```

---

# 19. 数据库连接配置

appsettings.json

```json
{
 "ConnectionStrings":
 {
   "Default":
   "Host=localhost;Database=IndustrialPlatform"
 }
}
```

---

# 20. Redis组件（已实现）

项目：

```
Infrastructure
```

功能：

* 缓存
* 分布式锁
* Session

接口：

```csharp
public interface ICacheService
{

Task SetAsync<T>(
string key,
T value);


Task<T?> GetAsync<T>(
string key);

}
```

---

# 21. RabbitMQ EventBus

项目：

```
IndustrialPlatform.EventBus
```

职责：

微服务事件通信。

结构：

```
EventBus


├── Producer

├── Consumer

├── Message

└── RabbitConnection
```

---

# 22. 消息模型

示例：

```csharp
public class IntegrationEvent
{

public Guid Id {get;}

public DateTimeOffset CreateTime {get;}

}
```

---

# 23. Serilog日志组件

项目：

```
IndustrialPlatform.Logging
```

统一：

* Console
* File
* Seq

日志格式：

```
时间

服务

用户

TraceId

Message

Exception
```

---

# 24. Web基础组件

项目：

```
IndustrialPlatform.Web
```

包含：

## 全局异常处理中间件

```
ExceptionMiddleware
```

## 请求日志

```
RequestLoggingMiddleware
```

## API返回包装

```
ResultFilter
```

---

# 25. Security组件

项目：

```
IndustrialPlatform.Security
```

功能：

* JWT
* 用户上下文
* 权限检查

接口：

```csharp
public interface ICurrentUser
{

long UserId {get;}

string UserName {get;}

}
```

---

# 26. NuGet包规划

## SharedKernel

无需第三方。

---

## Infrastructure

安装：

```
SqlSugarCore

StackExchange.Redis

Microsoft.Extensions.Configuration
```

---

## EventBus

安装：

```
RabbitMQ.Client
```

---

## Logging

安装：

```
Serilog

Serilog.AspNetCore

Serilog.Sinks.Console

Serilog.Sinks.Seq
```

---

# 27. BuildingBlocks开发任务拆分

## Task-001 创建项目结构

状态：已完成

内容：

* 创建7个ClassLibrary
* 添加引用关系

验收：

Solution正常编译。

---

## Task-002 Entity基础模型

状态：已完成（原始基线；后续调整见 TASK-BB-010）

内容：

实现：

* Entity
* AggregateRoot
* ValueObject

验收：

UnitTest通过。

---

## Task-003 Result组件

状态：已完成

实现：

* Result
* Result<T>

验收：

API统一返回。

---

## Task-004 Exception组件

状态：已完成

实现：

* BusinessException
* ValidationException

---

## Task-005 SqlSugar基础组件

状态：已完成

实现：

* SqlSugarDbContext
* BaseRepository\<TEntity\>
* SqlSugarUnitOfWork

验收：

Solution编译通过；DI注册测试通过。

---

## Task-006 Redis组件

状态：已完成

实现：

* CacheService
* DistributedLock

验收：

Solution编译通过；DI注册测试通过。

---

## Task-007 RabbitMQ组件

状态：已完成

实现：

* EventBus
* Producer
* Consumer

验收：

Solution编译通过；DI注册测试通过。

---

## Task-008 Logging组件

状态：已完成

实现：

* Serilog配置
* TraceId

验收：

Solution编译通过；选项绑定/增强器测试通过。

---

## Task-009 补充组件

状态：已完成

实现：

* Security: ICurrentUser / ClaimConstants / CurrentUser / AddCurrentUser
* Web: ApiResult / PageResult / ExceptionMiddleware / RequestLoggingMiddleware / ResultFilter

验收：

Solution编译通过；Security 声明读取测试、Web 结果包装与注册测试通过。

---

## TASK-BB-010 调整 Entity 生命周期、并发与软删除基线

**状态：** 可派遣

**目标：** 按已批准规格替换旧 Entity 审计字段，增加冻结、锁定、软删除、实体类型、双版本并发和并发安全仓储行为。

**输入文档：** `docs/superpowers/specs/2026-08-09-entity-lifecycle-concurrency-soft-delete-design.md`、蓝图 07、12、26、29，以及本实施方案第 7、15、16 节。

**依赖：** 原 BuildingBlocks Task-001 至 Task-009 已完成；在 TASK-BASE-002 和任何业务实体开发前完成本任务。已完成的 TASK-BASE-001 验证结果不回退。

**允许修改范围：** `src/backend/src/BuildingBlocks/IndustrialPlatform.SharedKernel/Entities/**`、SharedKernel 仓储接口与异常、`IndustrialPlatform.Infrastructure/Repository/**`、BuildingBlocks 对应测试；因接口编译影响，仅允许对 Identity/ReferenceData 骨架和测试做最小签名适配。禁止实现业务服务、前端、Docker 或部署功能。

**预期输出：** 新 `Entity` 八字段；`EnsureCanModify`、Touch、Freeze/Unfreeze、Lock/Unlock、MarkDeleted/Restore；专用并发异常；Update/Delete/Restore 双版本仓储接口；默认软删除过滤和原子条件更新。删除旧 `CreateTime`、`ModifyTime`、`Version` 属性，不保留兼容别名。

**验证与证据：** 严格执行 TDD，提交失败测试与通过测试证据；验证默认值、`CreatedOn == LastUpdatedOn`、EntityType、状态幂等与保护、版本推进、软删除非物理删除、默认过滤、双版本冲突和较新记录不被覆盖。运行 restore、全解决方案 build/test，记录退出码、警告、测试通过/失败/跳过数量，并证明 SharedKernel 未引用 SqlSugar。

**结果回写：** 更新本任务状态和最终提交；代码字段、方法或仓储签名偏差先回写规格与蓝图并标记 `设计待确认`；完成后由代码协作方更新 `CLAUDE.md` 的 BuildingBlocks 进度和测试总数。

**建议提交：** `refactor(shared-kernel): add entity lifecycle and concurrency`

---

# 28. BuildingBlocks完成标准

完成后：

所有Service可以引用：

```
IndustrialPlatform.SharedKernel

IndustrialPlatform.Application.Abstractions

IndustrialPlatform.Infrastructure

IndustrialPlatform.EventBus

IndustrialPlatform.Logging

IndustrialPlatform.Security
```

并具备：

* 统一实体模型
* 统一异常
* 统一返回
* 数据访问能力
* Redis能力
* MQ能力
* 日志能力
* 统一冻结、锁定、软删除与恢复生命周期
* 双版本并发安全更新

---
