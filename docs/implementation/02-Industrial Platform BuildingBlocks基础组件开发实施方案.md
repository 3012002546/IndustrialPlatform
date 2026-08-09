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

所有实体继承：

```
Entity
```

功能：

* 主键
* 创建时间
* 更新时间
* 版本控制

示例：

```csharp
public abstract class Entity
{
    public Guid Id { get; protected set; }

    public DateTimeOffset CreateTime { get; protected set; }

    public DateTimeOffset? ModifyTime { get; protected set; }

    protected Entity()
    {
        CreateTime = DateTimeOffset.UtcNow;
    }
}
```

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

状态：

待开发

内容：

* 创建7个ClassLibrary
* 添加引用关系

验收：

Solution正常编译。

---

## Task-002 Entity基础模型

内容：

实现：

* Entity
* AggregateRoot
* ValueObject

验收：

UnitTest通过。

---

## Task-003 Result组件

实现：

* Result
* Result<T>

验收：

API统一返回。

---

## Task-004 Exception组件

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

---
