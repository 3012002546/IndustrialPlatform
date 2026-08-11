# 25-Industrial Platform完整技术白皮书

> Industrial Platform
> 工业数字化执行平台
> Technical White Paper V1.0

> 平台基础层当前 Service Host 与内部模块边界以 `32-Industrial Platform Service Host与内部模块边界.md` 为权威母版；本文的长期目标服务名不等同于当前部署宿主。

---

# 文档说明

## 文档定位

本文档是：

```
Industrial Platform
工业数字化执行平台
```

最高层技术设计文档。

用于：

* 产品规划
* 架构评审
* 团队开发
* Codex AI开发输入
* 商业化演进

最终目标：

构建一套：

> 面向制造企业的新一代工业数字化基础平台。

---

# 1. 产品概述

## 1.1 产品名称

Industrial Platform

中文：

工业数字化执行平台

---

# 1.2 产品定位

Industrial Platform不是传统MES。

传统MES：

```
生产执行系统
```

Industrial Platform：

```
工业数字化基础平台

=
MES
+
工业数据平台
+
设备平台
+
AI平台
+
低代码平台
+
运维平台
```

---

# 1.3 产品目标

解决制造企业：

## 数据孤岛

传统：

```
ERP

 |

MES

 |

设备

 |

LIMS

 |

人工Excel

```

问题：

* 数据分散
* 查询困难
* 分析依赖人工

---

Industrial Platform：

```
ERP

 |

Industrial Platform

 |
 -----------------------

 MES

 IoT

 Quality

 Trace

 AI

 Data

```

形成：

统一工业数据中心。

---

# 2. 产品能力矩阵

| 领域             | 能力     |
| -------------- | ------ |
| MES            | 制造执行   |
| IoT            | 设备采集   |
| Weighting      | 智能称量   |
| Trace          | 全过程追溯  |
| Batch Record   | 电子批记录  |
| Data Platform  | 工业数据分析 |
| Low Code       | 快速配置   |
| AI Assistant   | 工业智能助手 |
| Operations Center | 运维监控、实施知识与受控助手 |

---

# 3. 总体架构

## 3.1 五层架构

```
                 用户层

 PC
 PDA
 Mobile
 Dashboard
 AI Chat


                 应用层

 MES
 OperationalData
 IoT
 Trace
 Batch
 Weighting
 AI


                 平台层

 Identity
 Tenant
 ReferenceData
 MasterData
 LowCode


                 数据层

 PostgreSQL
 Redis
 TimescaleDB
 MinIO
 Vector DB


                 基础设施层

 Docker
 Kubernetes
 Linux
 Windows Server

```

---

# 4. 微服务架构

## 当前平台基础层 Service Host

```
Industrial Platform

├── Identity Service

├── ReferenceData Service

├── SystemData Service

├── Collaboration Service

├── PlatformStudio Service

├── OperationsCenter Service

└── IoTCollector Service

```

七个宿主的内部模块、阶段映射和未来拆分规则见蓝图 32。MasterData、OperationalData、Planning、WorkOrder、Weighting、Trace、BatchRecord 与 Industrial Data 等名称保留为 MES 后续阶段或长期可拆分目标，不计入当前平台基础层七宿主。

`SystemData.Service` 同时提供后续服务数据库编排/环境引导控制面。新服务以 manifest 声明数据库与迁移期望，SystemData 负责 plan、异步 provision/apply、最小角色/授权、Operation 状态和审计；业务服务仍拥有领域 Schema 与迁移产物。SystemData 自身数据库由 PostgreSQL 18 基础设施最小引导，不新增 Database Migrator 核心宿主，完整边界见蓝图 33。

---

# 5. 技术架构

## Backend

```
.NET 10

ASP.NET Core WebAPI

Clean Architecture

DDD

SqlSugar

PostgreSQL

Redis

RabbitMQ

SignalR

Serilog

JWT

Hangfire

```

---

## Frontend

```
Vue3

TypeScript

Vite

Pinia

Element Plus

ECharts

```

---

## Mobile

统一代码：

```
Vue3

+

Capacitor

```

支持：

```
PC Web

PDA

Android

iOS

```

---

# 6. DDD领域设计

核心领域：

```
Manufacturing Domain

Equipment Domain

Material Domain

Trace Domain

Quality Domain

AI Domain

```

---

# 7. 核心业务模型

## Manufacturing

生产域：

```
ProductionPlan

WorkOrder

Operation

Route

Task

Execution

```

---

## Material

物料域：

```
Material

Batch

Container

Inventory

Consumption

```

---

## Equipment

设备域：

```
Equipment

Device

Sensor

Alarm

Maintenance

```

---

## Trace

追溯域：

```
Material

↓

Process

↓

Equipment

↓

Operator

↓

Product

```

---

# 8. Clean Architecture规范

每个服务：

```
Service

├── Api

├── Application

├── Domain

├── Infrastructure

└── Shared

```

---

# 9. 数据架构

## 9.1 数据分类

### 业务数据

PostgreSQL

例如：

```
WorkOrder

Material

Batch

```

---

### 时序数据

TimescaleDB

例如：

```
Temperature

Pressure

Speed

Energy

```

---

### 文件数据

MinIO

例如：

```
SOP

Report

Image

Document

```

---

### 监控数据

Prometheus

例如：

```
CPU

Memory

Service Health

```

---

### AI知识数据

Vector DB

例如：

```
SOP

Manual

Experience

```

---

# 10. 数据库设计原则

## 分库

```
identity_db

referencedata_db

masterdata_db

mes_db

iot_db

trace_db

batch_db

ai_db

```

---

## 数据生命周期

工业数据：

```
实时

↓

在线

↓

归档

↓

冷存储

```

---

# 11. 消息事件架构

采用：

RabbitMQ

架构：

```
Service

 |

Domain Event

 |

RabbitMQ

 |

Consumer

```

---

示例：

生产完成：

```
workorder.completed

```

消费者：

```
Trace Service

Batch Service

AI Service

```

---

# 12. 工业数据平台

## 定位

所有工业数据入口。

```
设备

↓

IoT Collector

↓

Industrial Data Platform

↓

分析

↓

AI

```

---

能力：

* 数据采集
* 数据清洗
* 数据建模
* 数据分析
* 数据服务

---

# 13. IoT体系

支持：

协议：

```
OPC UA

Modbus TCP

MQTT

S7

TCP

REST

```

---

数据流程：

```
设备

↓

Collector

↓

RabbitMQ

↓

TimescaleDB

↓

Dashboard

↓

AI

```

---

# 14. 低代码平台

目标：

减少MES实施成本。

能力：

## 模型配置

```
对象

字段

关系

```

---

## 页面配置

```
表单

列表

看板

流程

```

---

## 流程配置

```
审批

任务

状态机

```

---

# 15. AI平台

当前平台基础层的 KnowledgeAssistant、DataAssistant 与 ModelGateway 属于 `OperationsCenter.Service` 的独立内部模块。KnowledgeAssistant 使用带引用与适用版本的 RAG；DataAssistant 首期只访问注册的受控 Dataset/只读视图，永远禁止模型自由访问生产库；外部模型按项目默认关闭且调用可审计。长期 AI 平台能力不得反向放宽这些边界。

## AI定位

工业Copilot。

架构：

```
LLM

+

RAG

+

Agent

+

Industrial API

```

---

能力：

## 智能问答

例如：

```
设备报警E102怎么办？
```

---

## 数据分析

例如：

```
为什么今天OEE下降？
```

---

## 自动执行

例如：

```
创建维修工单

```

---

# 16. 安全体系

## 身份认证

JWT

支持：

```
用户

角色

权限

租户

```

---

## 数据权限

支持：

```
租户隔离

工厂隔离

产线隔离

```

---

## 审计

记录：

```
谁

什么时候

修改什么

为什么

```

---

# 17. 多租户SaaS设计

模型：

```
Tenant

 |

Factory

 |

Workshop

 |

Line

```

---

数据隔离：

方式：

## TenantNId

所有业务表：

```
TenantNId

```

---

高级：

数据库隔离。

---

# 18. 部署架构

## 单机部署

适合：

中小工厂。

```
Docker Compose

|

All Services

```

---

## 企业部署

```
Kubernetes


Ingress


Service


Pod


Database Cluster

```

---

# 19. DevOps体系

代码：

```
GitHub

```

CI/CD：

```
GitHub Actions

↓

Build

↓

Test

↓

Docker

↓

Deploy

```

---

# 20. 工程目录规划

最终：

```
IndustrialPlatform

├── docs

│
├── src

│   ├── Services

│
├── tests

│
├── docker

│
├── .github

│
├── .codex

│
└── README.md

```

---

# 21. Codex开发规范

Codex输入：

```
docs/

设计文档

↓

生成

↓

src代码

```

---

开发流程：

```
需求

↓

领域设计

↓

数据库

↓

API

↓

代码

↓

测试

↓

提交

```

---

# 22. MVP路线

## Phase 1

基础平台：

```
Identity

ReferenceData

MasterData

WorkOrder

Weighting

IoT

Trace

```

---

## Phase 2

工业平台：

```
Batch

Data Platform

Dashboard

LowCode

```

---

## Phase 3

智能平台：

```
AI Assistant

Agent

Predictive

```

---

# 23. 商业价值

## 对企业

降低：

* MES实施成本
* 数据孤岛
* 运维成本

提升：

* 生产透明度
* 质量追溯
* 数据决策

---

## 对个人开发者

形成：

```
个人工业软件产品

↓

行业解决方案

↓

SaaS平台

```

---

# 24. 长期演进路线

未来：

```
Industrial Platform

        |

数字化工厂平台

        |

工业操作系统

        |

AI驱动制造平台

```

---

# 25. 总结

Industrial Platform最终形成：

```
              Industrial Platform


       +----------------------+

       MES执行平台

       IoT数据平台

       追溯平台

       批记录平台

       低代码平台

       数据分析平台

       AI智能平台

       运维平台


       +----------------------+

```

核心理念：

> 用软件工程、工业知识和AI技术，打造下一代制造业数字化基础设施。

---
