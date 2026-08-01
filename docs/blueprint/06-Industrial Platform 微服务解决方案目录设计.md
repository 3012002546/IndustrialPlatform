# Industrial Platform 微服务解决方案目录设计

版本：v1.0
项目名称：Industrial Platform
定位：工业数字化执行平台（Industrial Digital Execution Platform）
架构：DDD + Clean Architecture + Microservices + Event Driven Architecture

---

# 1. 设计目标

Industrial Platform 不按照传统 MES 项目方式组织代码。

传统 MES：

```
一个项目
 ├── MES.Web
 ├── MES.Service
 ├── MES.DAL
 └── MES.Model
```

问题：

* 项目交付一次性开发
* 业务耦合严重
* 二开越来越困难
* 无法沉淀产品能力

Industrial Platform 采用：

```
平台能力
+
业务微服务
+
工业数据能力
+
设备生态
+
运维生态
```

目标：

> 建立一个可持续演进 5~10 年的个人工业数字化产品基础。

---

# 2. 整体代码仓库规划

推荐采用：

## 第一阶段：Monorepo（推荐）

原因：

个人开发 + Codex辅助 + 产品孵化阶段

更适合：

* 统一规范
* 快速重构
* AI辅助开发
* 跨服务修改

结构：

```
Industrial.Platform
│
├── backend
├── frontend
├── agents
├── docs
├── docker
├── scripts
├── tests
└── README.md
```

---

## 第二阶段：拆分独立仓库

商业化以后：

```
Industrial.Identity
Industrial.MES
Industrial.IoT
Industrial.Monitor
Industrial.Web
Industrial.Mobile
```

通过：

* Nuget Package
* API
* RabbitMQ

集成。

---

# 3. 顶层目录设计

完整结构：

```
Industrial.Platform

│
├── backend
│
├── frontend
│
├── agents
│
├── docs
│
├── docker
│
├── tests
│
├── scripts
│
├── tools
│
└── README.md
```

---

# 4. Backend目录设计

## 4.1 总体结构

```
backend

│
├── services
│
├── building-blocks
│
├── shared
│
├── gateways
│
├── workers
│
└── libraries
```

---

# 5. Services 微服务目录

所有业务服务独立。

```
backend/services

│
├── Identity.Service
│
├── Permission.Service
│
├── Audit.Service
│
├── File.Service
│
├── Notification.Service
│
│
├── ReferenceData.Service
│
├── MasterData.Service
│
├── Planning.Service
│
├── Material.Service
│
├── MaterialRuntime.Service
│
├── WorkOrder.Service
│
├── Workflow.Service
│
├── Weighting.Service
│
├── Equipment.Service
│
├── IoTCollector.Service
│
├── OEE.Service
│
├── Quality.Service
│
├── Trace.Service
│
├── BatchRecord.Service
│
├── Report.Service
│
├── Dashboard.Service
│
└── ServerMonitor.Api
```

---

# 6. 单个微服务内部结构

以：

```
WorkOrder.Service
```

为例。

采用 Clean Architecture：

```
WorkOrder.Service

│
├── src
│
│
├── WorkOrder.Api
│
├── WorkOrder.Application
│
├── WorkOrder.Domain
│
├── WorkOrder.Infrastructure
│
├── WorkOrder.Contracts
│
└── WorkOrder.Persistence
```

---

# 7. 各层职责

## 7.1 Api层

职责：

* Controller
* Middleware
* Authentication
* Swagger
* SignalR Hub

例如：

```
WorkOrder.Api

Controllers

 ├── WorkOrderController.cs
 └── DispatchController.cs


Hubs

 └── WorkOrderHub.cs
```

---

# 7.2 Application层

业务用例。

结构：

```
Application


├── Commands

│
├── Queries

│
├── Services

│
├── DTOs

│
├── Validators

│
├── EventHandlers

│
└── Mapping
```

例如：

创建工单：

```
CreateWorkOrderCommand

       |
       |
CreateWorkOrderHandler

       |
       |
Domain
```

---

# 7.3 Domain层

核心业务。

结构：

```
Domain

├── Entities

├── Aggregates

├── ValueObjects

├── DomainEvents

├── Interfaces

├── Rules

└── Exceptions
```

例如：

生产工单：

```
WorkOrder

AggregateRoot


包含:

OrderNo

Product

Quantity

Status

Routing

```

---

# 7.4 Infrastructure层

外部技术实现。

```
Infrastructure


├── Repository

├── Redis

├── RabbitMQ

├── ExternalApi

├── Cache

├── FileStorage

└── Services
```

---

# 7.5 Persistence层

数据库。

```
Persistence


├── DbContext

├── EntityConfigurations

├── Migrations

├── Seed

└── SqlSugar
```

---

# 8. Building Blocks基础组件

公共能力。

```
building-blocks


├── Industrial.Core

├── Industrial.Domain

├── Industrial.EventBus

├── Industrial.Logging

├── Industrial.Security

├── Industrial.Cache

├── Industrial.Storage

├── Industrial.Messaging

└── Industrial.Background
```

---

## Industrial.Core

包含：

```
Result<T>

PagedResult<T>

SnowflakeId

CurrentUser

BaseEntity
```

---

## Industrial.Domain

DDD基础：

```
Entity

AggregateRoot

DomainEvent

ValueObject

Repository
```

---

## Industrial.EventBus

RabbitMQ封装。

例如：

```
IEventBus

PublishAsync()

Subscribe()
```

---

# 9. Gateway目录

统一入口。

```
backend/gateways


├── ApiGateway

├── WebGateway

└── MobileGateway
```

技术：

推荐：

* YARP

结构：

```
ApiGateway


Routes

Clusters

Authentication

RateLimit

```

---

# 10. Worker服务

后台任务。

```
backend/workers


├── Scheduler.Worker

├── EventConsumer.Worker

├── DataArchive.Worker

└── ReportWorker
```

技术：

.NET Worker Service

* Hangfire

---

# 11. Frontend目录设计

采用：

Vue3 + TypeScript + Monorepo

```
frontend


├── apps

├── packages

├── components

├── layouts

└── tools
```

---

# 12. Apps

```
frontend/apps


│
├── web

│
├── pda

│
└── mobile
```

实际上：

代码共享。

例如：

```
web

生产管理


pda

生产执行


mobile

管理查看

```

---

# 13. Packages共享

```
frontend/packages


├── ui

├── api

├── auth

├── permission

├── charts

├── utils

├── hooks

└── device
```

---

# 14. 页面结构规范

例如 MES：

```
src/views


Manufacturing

├── WorkOrder

├── Dispatch

├── Report

├── Trace


Equipment

├── Monitor

├── Alarm


Quality

├── Inspection

└── LIMS

```

---

# 15. Agent目录设计

工业现场采集。

```
agents


│
├── ServerMonitor.Agent

├── Equipment.Agent

├── OPC.Agent

├── Modbus.Agent

└── Gateway.Agent
```

---

## ServerMonitor.Agent

.NET Worker

结构：

```
ServerMonitor.Agent


├── Collector

│
├── CpuCollector

├── MemoryCollector

├── DiskCollector

├── NetworkCollector

├── ServiceCollector


├── Transport

│
├── Http

├── RabbitMQ


├── Storage

└── Config
```

支持：

Windows:

```
Windows Service
```

Linux:

```
systemd
```

---

# 16. Docs目录设计

重点。

未来给Codex使用。

```
docs


│
├── 00-Architecture

│
├── 01-Domain

│
├── 02-Service

│
├── 03-Database

│
├── 04-Event

│
├── 05-Frontend

│
├── 06-Deployment

│
├── 07-Development

│
└── 08-Codex
```

---

详细：

```
docs


00-Architecture

├── Overall.md
├── Microservice.md


01-Domain

├── MES.md
├── Trace.md
├── Batch.md


02-Service

├── WorkOrder.md
├── Weighting.md


03-Database

├── PostgreSQL.md
├── TableDesign.md


04-Event

├── RabbitMQ.md
├── EventCatalog.md


08-Codex

├── CodingRule.md
├── PromptTemplate.md

```

---

# 17. Docker部署目录

```
docker


│
├── development

│
├── test

│
├── production

│
└── compose
```

---

## development

本地开发：

```
docker-compose.yml


postgres

redis

rabbitmq

minio

grafana

prometheus

```

---

## production

生产：

```
docker-compose.yml


identity

mes

iot

monitor

gateway
```

---

# 18. Tests目录设计

```
tests


├── UnitTests


├── IntegrationTests


├── ApiTests


└── PerformanceTests

```

---

例如：

```
WorkOrder.Tests


├── Domain

├── Application

└── Api

```

---

# 19. Scripts目录

自动化。

```
scripts


├── database

├── docker

├── deploy

├── migrate

└── backup
```

---

# 20. Codex开发规范设计

为了AI长期协作。

每个服务必须包含：

```
README.md

ARCHITECTURE.md

API.md

DATABASE.md

TODO.md

```

例如：

```
WorkOrder.Service


README.md

说明服务用途


ARCHITECTURE.md

DDD设计


TODO.md

Codex任务列表

```

---

# 21. 推荐开发顺序

## Phase 0 基础框架

创建：

```
Industrial.Core

Industrial.Domain

Industrial.EventBus

Identity.Service

ApiGateway

Frontend Shell
```

---

## Phase 1 MES MVP

优先：

```
ReferenceData

MasterData

Material

WorkOrder

Workflow

Weighting

Trace
```

---

## Phase 2 工业连接

```
Equipment

IoTCollector

OEE

Quality
```

---

## Phase 3 产品化

```
Report

Dashboard

LowCode

BatchRecord
```

---

# 22. 最终完整目录

```
Industrial.Platform

├── backend

│   ├── services

│   ├── building-blocks

│   ├── gateways

│   ├── workers


├── frontend

│   ├── apps

│   └── packages


├── agents


├── docs


├── docker


├── tests


├── scripts


└── README.md

```

---

# 23. 最终建议

你的项目不建议一开始拆成几十个 Git 仓库。

推荐：

```
第一年：

Industrial.Platform
        |
        |
        MonoRepo


第二年：

拆分：

Industrial.MES

Industrial.Monitor

Industrial.IoT

Industrial.Platform.Core

```

原因：

你目前最大的价值不是服务数量，而是：

* MES领域模型
* 工业经验
* 业务沉淀
* AI协同开发效率

这个目录结构可以直接作为：

```
Codex Workspace
        +
.NET 10 Solution
        +
Docker环境
        +
MES产品基础工程
```

