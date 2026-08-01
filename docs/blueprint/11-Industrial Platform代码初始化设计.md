# 11-Industrial Platform代码初始化设计

> 文档版本：v1.0
> 项目名称：Industrial Platform
> 文档类型：工程初始化设计规范
> 目标：建立可长期演进的工业数字化平台代码基础设施
> 适用阶段：MVP → 商业产品化 → 微服务扩展

---

# 1. 文档目的

Industrial Platform 不以单个 MES 项目交付为目标，而是建设一个：

> 面向制造企业的工业数字化执行平台（Industrial Digital Execution Platform）

因此代码工程设计必须满足：

* 长生命周期维护
* 多产品线扩展
* 多团队协作
* 微服务独立部署
* 领域模型隔离
* 自动化测试
* DevOps持续交付
* Codex AI辅助开发

本章节定义：

* Solution结构
* 项目分层
* 服务边界
* Namespace规范
* NuGet规范
* Docker开发环境
* CI/CD基础
* Git规范
* 第一版初始化步骤

---

# 2. 总体代码仓库设计

Industrial Platform采用：

```
Monorepo
+
Multiple Services
+
Shared Kernel
+
Independent Deployment
```

原因：

个人开发阶段：

* 方便管理
* 方便Codex理解上下文
* 方便领域共享

商业阶段：

可以拆分：

```
Industrial.Platform.Identity

Industrial.Platform.MES

Industrial.Platform.IoT

Industrial.Platform.Trace
```

---

# 3. Git Repository结构

最终目录：

```
IndustrialPlatform
│
├── docs
│
├── src
│
├── tests
│
├── docker
│
├── deploy
│
├── scripts
│
├── tools
│
├── .github
│
├── .codex
│
├── IndustrialPlatform.sln
│
├── Directory.Build.props
│
├── Directory.Build.targets
│
├── Directory.Packages.props
│
├── global.json
│
├── README.md
│
└── LICENSE
```

---

# 4. 目录职责说明

## docs

架构文档：

```
docs

├── architecture

│   ├── overall.md
│   ├── domain-model.md
│   └── service-boundary.md


├── database

│   ├── postgres-standard.md


├── api

│   └── api-standard.md


├── development

│   ├── coding-standard.md
│   └── codex-guide.md

```

作用：

让：

* 人
* Codex
* 新开发人员

理解系统。

---

# 5. src目录设计

```
src
│
├── BuildingBlocks
│
├── Gateway
│
├── Services
│
├── Hosts
│
└── Shared
```

---

# 6. BuildingBlocks设计

公共基础能力：

```
src/BuildingBlocks
```

结构：

```
BuildingBlocks

├── IndustrialPlatform.BuildingBlocks.Domain

├── IndustrialPlatform.BuildingBlocks.Application

├── IndustrialPlatform.BuildingBlocks.Infrastructure

├── IndustrialPlatform.BuildingBlocks.EventBus

├── IndustrialPlatform.BuildingBlocks.Logging

├── IndustrialPlatform.BuildingBlocks.Security

└── IndustrialPlatform.BuildingBlocks.Common

```

---

# 6.1 Domain BuildingBlock

职责：

领域基础能力

包含：

```
Entity

AggregateRoot

ValueObject

DomainEvent

IDomainEvent

IRepository

Specification

```

例如：

```csharp
public abstract class AggregateRoot<T>
{
    public T Id {get;protected set;}

    private readonly List<IDomainEvent> events;


    public IReadOnlyCollection<IDomainEvent> DomainEvents
        => events;
}
```

---

# 6.2 Application BuildingBlock

公共应用层能力：

```
ICommand

IQuery

Handler

DTO

Result

Validation

```

后续可以接入：

MediatR

例如：

```
CreateWorkOrderCommand

CreateWorkOrderHandler

```

---

# 6.3 Infrastructure BuildingBlock

基础设施：

```
Database

Redis

RabbitMQ

MinIO

FileStorage

Email

```

---

# 6.4 EventBus

统一事件模型：

```
IntegrationEvent

EventPublisher

EventConsumer

Retry

DeadLetter

```

例如：

生产：

```
WorkOrderReleasedEvent
```

消费：

```
IoT Service

Trace Service

Batch Service
```

---

# 7. Gateway设计

统一入口：

```
src/Gateway
```

技术：

推荐：

YARP

结构：

```
IndustrialPlatform.Gateway

├── Program.cs

├── Routes

└── Middleware

```

职责：

* JWT验证
* 路由转发
* 限流
* 日志
* API聚合

访问：

```
Vue

↓

Gateway

↓

Micro Services

```

---

# 8. Services目录设计

核心业务微服务：

```
src/Services
```

第一阶段：

```
Services

├── Identity

├── ReferenceData

├── MasterData

├── MES

├── Weighting

├── IoT

├── Trace

└── BatchRecord

```

---

# 9. 单个微服务Solution结构

以：

MES Service

示例：

```
IndustrialPlatform.MES


├── Domain

├── Application

├── Infrastructure

├── WebApi

└── Tests

```

完整：

```
Services/MES


IndustrialPlatform.MES.Domain


IndustrialPlatform.MES.Application


IndustrialPlatform.MES.Infrastructure


IndustrialPlatform.MES.WebApi

```

---

# 10. Clean Architecture依赖关系

严格：

```
WebApi

 ↓

Application

 ↓

Domain


Infrastructure

 ↓

Application

 ↓

Domain

```

禁止：

```
Domain
引用
Infrastructure
```

禁止：

```
Application
引用
WebApi
```

---

# 11. 项目引用关系

## Domain

引用：

```
BuildingBlocks.Domain
```

不能引用：

```
SqlSugar

Redis

RabbitMQ
```

---

## Application

引用：

```
Domain

BuildingBlocks.Application
```

可以：

```
MediatR

FluentValidation
```

---

## Infrastructure

引用：

```
Application

Domain

BuildingBlocks.Infrastructure
```

包含：

```
SqlSugar

Redis

RabbitMQ

MinIO

```

---

## WebApi

引用：

```
Application

Infrastructure
```

包含：

```
JWT

Swagger

SignalR

```

---

# 12. Namespace规范

统一：

```
IndustrialPlatform
```

## 示例

Domain:

```csharp
namespace IndustrialPlatform.MES.Domain.WorkOrders;
```

Application:

```csharp
namespace IndustrialPlatform.MES.Application.WorkOrders.Commands;
```

Infrastructure:

```csharp
namespace IndustrialPlatform.MES.Infrastructure.Persistence;
```

WebApi:

```csharp
namespace IndustrialPlatform.MES.WebApi.Controllers;
```

---

# 13. 类命名规范

## Entity

```
WorkOrder
Material
Equipment
```

禁止：

```
WorkOrderEntity
```

---

## DTO

输入：

```
CreateWorkOrderRequest
```

输出：

```
WorkOrderDto
```

---

## Command

```
CreateWorkOrderCommand
```

---

## Query

```
GetWorkOrderQuery
```

---

## Event

领域事件：

```
WorkOrderCreatedDomainEvent
```

集成事件：

```
WorkOrderCreatedIntegrationEvent
```

---

# 14. NuGet统一管理

根目录：

```
Directory.Packages.props
```

示例：

```xml
<Project>


<ItemGroup>

<PackageVersion 
 Include="SqlSugarCore"
 Version="5.x"/>


<PackageVersion
 Include="Serilog.AspNetCore"
 Version="9.x"/>


<PackageVersion
 Include="RabbitMQ.Client"
 Version="7.x"/>


</ItemGroup>


</Project>
```

所有项目：

禁止：

```
PackageReference Version=
```

统一：

```xml
<PackageReference 
Include="SqlSugarCore"/>
```

---

# 15. 核心NuGet规划

## Web

```
Microsoft.AspNetCore.OpenApi

Swashbuckle.AspNetCore
```

---

## ORM

```
SqlSugarCore
```

---

## Validation

```
FluentValidation
```

---

## CQRS

```
MediatR
```

---

## Logging

```
Serilog.AspNetCore

Serilog.Sinks.Console

Serilog.Sinks.File
```

---

## Message

```
RabbitMQ.Client
```

---

## Cache

```
StackExchange.Redis
```

---

## Job

```
Hangfire.AspNetCore

Hangfire.PostgreSql
```

---

# 16. Docker开发环境

目录：

```
docker

├── docker-compose.yml

├── postgres

├── redis

├── rabbitmq

├── minio

├── prometheus

└── grafana

```

---

# 17. 本地开发环境

docker-compose：

包含：

```
PostgreSQL

Redis

RabbitMQ

MinIO

TimescaleDB

Prometheus

Grafana

```

架构：

```
Developer PC


.NET Service

     |

Docker Infrastructure


```

---

# 18. Docker Compose规划

服务：

```
postgres

port:
5432


redis

6379


rabbitmq

5672
15672


minio

9000
9001


grafana

3000


prometheus

9090

```

---

# 19. 配置管理规范

统一：

```
appsettings.json

appsettings.Development.json

appsettings.Production.json
```

示例：

```json
{
 "Database":{
   "ConnectionString":""
 },

 "Redis":{
   "ConnectionString":""
 },

 "RabbitMQ":{
 }

}
```

生产环境：

使用：

```
Environment Variable

Secret Manager

Vault
```

---

# 20. CI/CD基础设计

采用：

GitHub Actions

目录：

```
.github

└── workflows

    ├── build.yml

    ├── test.yml

    └── docker.yml

```

---

# 21. Build流程

代码提交：

```
git push


↓

Restore


↓

Build


↓

Test


↓

Docker Build


↓

Push Image

```

---

# 22. Docker镜像规范

命名：

```
industrial-platform/{service}
```

例如：

```
industrial-platform/mes-api

industrial-platform/iot-service

```

Tag：

```
latest

1.0.0

commit-id

```

---

# 23. Git规范

采用：

Git Flow 简化版

分支：

```
main

develop

feature/*

bugfix/*

release/*
```

---

# Commit规范

采用：

Conventional Commit

例如：

新增：

```
feat: add work order module
```

修复：

```
fix: resolve weight calculation bug
```

文档：

```
docs: update architecture document
```

---

# 24. Codex协作规范

根目录：

```
.codex
```

结构：

```
.codex

├── project-context.md

├── architecture.md

├── coding-rule.md

└── task-template.md

```

作用：

让Codex：

* 理解项目定位
* 遵守架构
* 自动生成符合规范代码

---

# 25. 第一版代码初始化步骤

## Step 1

创建Solution

```bash
dotnet new sln

IndustrialPlatform.sln
```

---

## Step 2

创建基础目录

```
src

tests

docker

docs

```

---

## Step 3

创建BuildingBlocks

```bash
dotnet new classlib
```

创建：

```
Domain

Application

Infrastructure

EventBus

```

---

## Step 4

创建第一个服务

选择：

Identity Service

原因：

所有系统基础。

创建：

```
Identity.Domain

Identity.Application

Identity.Infrastructure

Identity.WebApi

```

---

## Step 5

接入基础设施

完成：

```
PostgreSQL

Redis

Serilog

JWT

Swagger

RabbitMQ
```

---

## Step 6

Docker启动

执行：

```bash
docker compose up -d
```

---

## Step 7

运行第一个API

目标：

```
GET

/api/health
```

返回：

```json
{
 "status":"ok",
 "service":"identity"
}
```

---

# 26. 第一阶段完成标准

完成：

✅ Solution创建

✅ Clean Architecture模板

✅ Docker环境

✅ Identity Service运行

✅ JWT认证

✅ PostgreSQL连接

✅ Redis连接

✅ RabbitMQ连接

✅ Serilog日志

✅ CI Build成功

---

# 27. 后续微服务扩展能力

新增服务只需要复制模板：

```
Service Template

↓

MES

↓

Weighting

↓

IoT

↓

Trace

```

保持：

```
统一架构

统一日志

统一认证

统一事件

统一部署
```

---

# 28. 第一版工程目标

最终达到：

```
Industrial Platform v0.1


        Vue3

          |

       Gateway

          |

 -------------------

 Identity

 ReferenceData

 MasterData

 MES


 -------------------


 PostgreSQL

 Redis

 RabbitMQ

 MinIO

```

这是未来两年产品化开发的基础工程。

---

