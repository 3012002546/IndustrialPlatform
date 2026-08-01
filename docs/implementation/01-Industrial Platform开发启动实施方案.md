# 01-Industrial Platform开发启动实施方案

# Industrial Platform开发启动实施方案

版本：V1.0
阶段：Development Implementation Phase
项目类型：工业数字化执行平台
技术路线：

.NET 10 + DDD + Clean Architecture + Microservices + Vue3

---

# 1. 文档说明

## 1.1 文档目的

本文档用于指导 Industrial Platform 从架构设计阶段进入工程开发阶段。

前期已经完成：

* 产品定位设计
* 微服务架构设计
* DDD领域模型设计
* 数据库模型设计
* API规范设计
* 前端工程规范设计
* 测试体系设计
* 安全体系设计

本阶段不再讨论架构设计。

目标：

> 将设计文档转换为真实可运行的软件工程。

最终输出：

一个完整的软件仓库：

```
IndustrialPlatform
```

包含：

* 后端微服务
* Vue3前端
* 自动化测试
* Docker环境
* 部署脚本
* CI/CD流程
* Codex辅助开发规范

---

# 2. 开发阶段总体规划

## Phase 0 工程初始化

目标：

完成项目基础工程。

包含：

* Git仓库初始化
* Visual Studio Solution创建
* Backend目录规划
* Frontend初始化
* Docker环境
* CI/CD基础
* Codex工程配置

完成标准：

开发环境可以启动：

* PostgreSQL
* Redis
* RabbitMQ
* Backend API
* Frontend

---

# Phase 1 BuildingBlocks基础组件

目标：

建立所有微服务共享基础能力。

项目：

```
IndustrialPlatform.BuildingBlocks
```

包含：

## Domain基础

* Entity基类
* AggregateRoot
* ValueObject
* DomainEvent
* Enumeration

## Application基础

* CQRS基础
* Command
* Query
* DTO
* Validator

## Infrastructure基础

* SqlSugar封装
* Redis封装
* RabbitMQ封装
* Serilog封装

## Common组件

* Result统一返回
* Exception体系
* 分页模型
* 用户上下文
* 时间服务
* ID生成器

---

# Phase 2 Identity Service

目标：

完成平台认证授权。

功能：

* 用户
* 角色
* 权限
* 组织
* JWT
* RefreshToken

---

# Phase 3 MasterData Service

目标：

完成工业基础数据。

包含：

* 工厂
* 车间
* 产线
* 设备
* 物料
* 工艺
* 单位

---

# Phase 4 WorkOrder Service

目标：

完成生产执行核心。

包含：

* 生产计划
* 工单
* 工序
* BOM
* 路由
* 执行任务

---

# Phase 5 Weighting Service

目标：

完成工业称量平台。

支持：

* 人工称量
* PDA称量
* 自动秤
* 条码
* 批次
* 防错校验

---

# Phase 6 IoT Collector Service

目标：

完成设备数据采集。

支持：

* OPC UA
* Modbus
* MQTT
* TCP
* Serial

---

# Phase 7 Trace Service

目标：

完成生产追溯。

包含：

* 原料追溯
* 产品追溯
* 设备追溯
* 人员追溯

---

# Phase 8 Batch Record Service

目标：

完成电子批记录。

---

# Phase 9 Vue3统一前端

目标：

实现：

PC

PDA

Mobile

三端统一。

---

# Phase 10 MVP业务闭环

最终实现：

```
基础数据

↓

生产计划

↓

生产工单

↓

称量执行

↓

设备采集

↓

生产追溯

↓

电子批记录

↓

数据分析
```

---

# 3. Git仓库初始化

## 3.1 创建仓库

仓库名称：

```
IndustrialPlatform
```

初始化：

```bash
mkdir IndustrialPlatform

cd IndustrialPlatform

git init
```

---

# 3.2 Git分支规范

采用简化Git Flow。

分支：

```
main

develop

feature/*

bugfix/*

release/*
```

说明：

## main

生产稳定版本。

示例：

```
v1.0.0
```

---

## develop

开发主分支。

所有功能合并到：

```
develop
```

---

## feature

功能开发。

示例：

```
feature/identity-service

feature/masterdata-service

feature/workorder-service
```

---

# 3.3 Commit规范

采用：

Conventional Commit

格式：

```
type(scope): message
```

示例：

新增：

```
feat(identity): add login api
```

修复：

```
fix(order): fix status transition
```

文档：

```
docs(api): update api document
```

重构：

```
refactor(domain): optimize entity
```

---

# 4. 最终仓库目录

最终结构：

```
IndustrialPlatform

├── docs

├── src

│
├── backend

│
├── frontend


├── tests


├── docker


├── deploy


├── .github


└── .codex
```

---

# 5. Backend工程规划

目录：

```
src/backend
```

结构：

```
backend

├── IndustrialPlatform.sln


├── src


│
├── BuildingBlocks


│
├── Services


│
├── Gateway



└── Tools
```

---

# 6. Visual Studio 2026 Solution规划

Solution：

```
IndustrialPlatform.sln
```

项目结构：

```
IndustrialPlatform.sln


├── BuildingBlocks


├── Identity


├── MasterData


├── WorkOrder


├── Weighting


├── IoTCollector


├── Trace


├── BatchRecord


└── Gateway
```

---

# 7. Microservice工程结构规范

以 Identity Service 为例：

```
Identity


├── Identity.Api


├── Identity.Application


├── Identity.Domain


└── Identity.Infrastructure
```

---

# 7.1 API层

职责：

* Controller
* Middleware
* Authentication
* API配置

禁止：

业务逻辑。

---

# 7.2 Application层

职责：

* UseCase
* Command
* Query
* DTO
* Service

---

# 7.3 Domain层

职责：

核心业务。

包含：

```
Entities

Aggregates

ValueObjects

DomainEvents
```

禁止引用：

Infrastructure。

---

# 7.4 Infrastructure层

职责：

外部实现。

包含：

* Database
* Repository
* Cache
* MQ
* External API

---

# 8. Frontend工程规划

目录：

```
src/frontend
```

技术：

* Vue3
* TypeScript
* Vite
* Pinia
* Element Plus
* ECharts

---

结构：

```
frontend


├── src


│
├── api


├── assets


├── components


├── layouts


├── router


├── stores


├── utils


├── hooks


├── views


├── permissions


├── pc


├── pda


└── mobile


```

---

# 9. 三端统一开发规范

原则：

业务代码共享。

端差异只存在：

* 页面布局
* 操作方式
* 展示方式

共享：

```
api

store

hooks

components

utils
```

---

# 10. Tests目录规划

目录：

```
tests
```

结构：

```
tests


├── UnitTests


│
├── Identity.Tests

├── MasterData.Tests

├── WorkOrder.Tests


├── IntegrationTests


├── ApiTests


├── PerformanceTests


└── E2ETests
```

---

# 11. Docker目录规划

目录：

```
docker
```

结构：

```
docker


├── docker-compose.yml


├── postgres


├── redis


├── rabbitmq


├── nginx


└── seq
```

---

# 12. Deploy目录规划

生产部署。

结构：

```
deploy


├── docker-compose


├── kubernetes


├── nginx


├── scripts


└── environment
```

---

# 13. Github Actions规划

目录：

```
.github


├── workflows


│
├── backend-ci.yml

├── frontend-ci.yml

├── docker-build.yml


├── ISSUE_TEMPLATE


└── pull_request_template.md
```

---

# 14. .codex目录规划

目录：

```
.codex
```

结构：

```
.codex


├── project-context.md

├── architecture.md

├── coding-rule.md

├── database-rule.md

├── api-rule.md

├── task-template.md

└── commit-rule.md
```

---

# 15. Codex协作开发方式

禁止：

直接要求：

```
帮我写代码
```

推荐流程：

```
需求

↓

任务拆分

↓

技术方案

↓

代码实现

↓

测试

↓

Review

↓

Commit
```

---

# 16. Codex任务模板

示例：

```
任务：

实现 Identity 登录接口


背景：

Industrial Platform


技术：

.NET10

DDD

Clean Architecture


要求：

1. 创建领域模型

2. 创建Application Service

3. 创建API接口

4. 增加Unit Test

5. 增加数据库实体


验收：

登录成功返回JWT Token

测试通过
```

---

# 17. 开发规范

## Backend规范

必须：

* DDD
* Clean Architecture
* 异步编程
* DTO隔离
* Repository模式
* Domain不依赖Infrastructure

---

## Database规范

所有业务表必须包含：

```
Id

CreateTime

CreateUser

ModifyTime

ModifyUser

IsDeleted

Version
```

---

## API规范

统一：

```
/api/{service}/{resource}
```

例如：

```
GET

/api/workorder/orders
```

---

# 18. MVP第一阶段开发路线

目标：

完成工业生产闭环。

优先顺序：

```
Identity

↓

MasterData

↓

WorkOrder

↓

Weighting

↓

Trace

↓

BatchRecord
```

---

# 19. Phase0详细TodoList

## Git初始化

* [ ] 创建Github仓库
* [ ] 初始化Git
* [ ] 创建分支策略
* [ ] 创建README

---

## Backend初始化

* [ ] 创建Solution
* [ ] 创建项目目录
* [ ] 创建.NET10 WebAPI模板
* [ ] 配置Nullable
* [ ] 配置EditorConfig

---

## BuildingBlocks

* [ ] 创建SharedKernel
* [ ] 创建Entity基类
* [ ] 创建Result模型
* [ ] 创建异常体系
* [ ] 创建DomainEvent

---

## 数据库

* [ ] 安装PostgreSQL
* [ ] 创建数据库
* [ ] 配置SqlSugar
* [ ] 创建基础表

---

## Docker

* [ ] PostgreSQL
* [ ] Redis
* [ ] RabbitMQ
* [ ] Seq

---

## Frontend

* [ ] 创建Vue3项目
* [ ] 配置TypeScript
* [ ] 配置Vite
* [ ] 配置Pinia
* [ ] 配置Element Plus

---

## CI/CD

* [ ] 创建Github Actions
* [ ] Backend Build
* [ ] Frontend Build

---

# 20. Phase0完成标准

开发人员新环境：

执行：

```bash
git clone IndustrialPlatform

docker compose up -d

dotnet build

npm install

npm run dev
```

能够启动：

Backend

Frontend

Database

Redis

RabbitMQ

Industrial Platform正式进入编码阶段。

---

