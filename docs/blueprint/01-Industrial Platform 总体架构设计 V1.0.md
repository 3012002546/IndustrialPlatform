# Industrial Platform 总体架构设计 V1.0

> 文档版本：V1.0
> 文档类型：系统架构设计文档
> 项目定位：工业数字化执行平台（Industrial Platform）
> 架构模式：DDD + 微服务 + 事件驱动 + 工业数据平台
> 技术方向：.NET 10 + Vue3 + PostgreSQL + RabbitMQ

---

# 1. 文档说明

## 1.1 编写目的

本文档用于定义个人工业数字化平台（Industrial Platform）的整体技术架构，为后续：

* MES系统研发
* 微服务拆分
* 数据库设计
* 前后端开发
* 部署运维
* 二次开发扩展

提供统一架构标准。

---

## 1.2 产品定位

本系统不是单纯 MES 系统，而是：

> 面向制造企业的工业数字化基础平台。

覆盖：

* 生产执行 MES
* 设备管理
* 工业数据采集
* 称量执行
* 质量追溯
* 批记录
* 数据分析
* IT运维监控

整体定位：

```
Industrial Manufacturing Platform

        +
        
Industrial Operation Platform

        =
        
Industrial Digital Platform
```

---

# 2. 产品总体目标

## 2.1 建设目标

打造：

> 一个可配置、可扩展、可复制的轻量工业数字化平台。

支持：

| 领域    | 支持 |
| ----- | -- |
| 离散制造  | √  |
| 流程制造  | √  |
| 电子行业  | √  |
| 新能源   | √  |
| 医药    | √  |
| 食品    | √  |
| 汽车零部件 | √  |

---

# 3. 总体架构设计

## 3.1 总体逻辑架构

```
                         用户层
                           |
        -----------------------------------
        |                |                |
      PC Web           PDA              Mobile
      Vue3             Vue3             Vue3
        |                |                |
        -----------------------------------
                           |
                    API Gateway
                           |
 =================================================

                  应用服务层

 =================================================


 制造执行域                    运维管理域

 ----------------             ----------------

 MES Service                  Server Monitor

 Planning                     Agent

 Material                     Alarm

 WorkOrder                    Dashboard

 Workflow

 Weighting

 Equipment

 Trace

 Batch Record


 =================================================

                  平台能力层

 =================================================


 Identity

 Permission

 Audit

 File

 Notification

 Report

 Dashboard

 Archive


 =================================================

                  基础设施层

 =================================================


 PostgreSQL

 Redis

 RabbitMQ

 MinIO

 TimescaleDB

 Elasticsearch



```

---

## 3.2 基础服务依赖顺序

```text
BuildingBlocks
      ↓
Identity
      ↓
ReferenceData
      ↓
MasterData
```

ReferenceData 负责字典、配置、元数据与编码规则；MasterData 负责物料、设备、组织与 BOM 等业务主数据。两者边界独立，后续业务服务按上述顺序建立依赖。

---

# 4. 架构设计原则

## 4.1 领域驱动设计 DDD

系统按照业务领域拆分。

禁止：

```
一个大数据库

一个大项目

一个Service层
```

采用：

```
领域

↓

服务

↓

数据库

↓

接口
```

---

## 4.2 微服务原则

每个服务：

拥有：

* 独立代码
* 独立数据库
* 独立部署
* 独立扩展

例如：

```
WorkOrder Service


WorkOrder.Api

WorkOrder.Domain

WorkOrder.Application

WorkOrder.Infrastructure


workorder_db

```

---

## 4.3 事件驱动

服务之间：

不直接依赖。

采用：

RabbitMQ。

例如：

工单完成：

```
WorkOrder Service

        |
        |
 WorkOrderCompleted Event

        |
        |
-------------------------

库存服务

追溯服务

报表服务

```

---

# 5. 技术架构

# 5.1 后端技术栈

| 类别    | 技术                 |
| ----- | ------------------ |
| 开发语言  | C#                 |
| 框架    | .NET 10 WebAPI     |
| 架构    | Clean Architecture |
| ORM   | SqlSugar           |
| 接口    | REST API           |
| 实时通信  | SignalR            |
| 认证    | JWT                |
| 缓存    | Redis              |
| 消息队列  | RabbitMQ           |
| 日志    | Serilog            |
| 任务调度  | Hangfire           |
| 验证    | FluentValidation   |
| 对象映射  | Mapster            |
| API文档 | Swagger            |

---

# 5.2 前端技术栈

统一：

```
Vue3

+

TypeScript

+

Vite

+

Pinia

+

Element Plus

+

ECharts

```

支持：

```
PC Browser

PDA Browser

Mobile App
```

---

# 5.3 数据存储

## 业务数据库

PostgreSQL

采用：

Database Per Service

例如：

```
identity_db

mdm_db

material_db

wo_db

equipment_db

trace_db

weight_db

```

---

## 缓存

Redis

用途：

* Session
* Token
* 实时状态
* 分布式锁
* 临时数据

---

## 消息

RabbitMQ

用途：

领域事件：

```
OrderCreated

MaterialIssued

WeightCompleted

EquipmentAlarm

BatchCompleted

```

---

## 文件存储

MinIO

用途：

* 工艺文件
* 图片
* PDF
* 检验报告
* 批记录附件

---

# 6. 微服务划分

# 6.1 基础平台服务

## Identity Service

负责：

用户体系。

功能：

* 用户
* 角色
* 权限
* 数据权限
* 登录

数据库：

```
identity_db

sys_user

sys_role

sys_permission

```

---

## Audit Service

审计服务。

所有业务操作记录：

```
谁

什么时候

什么数据

修改前

修改后

```

表：

```
audit_log

```

---

## File Service

统一文件。

支持：

```
上传

下载

预览

版本
```

---

## Notification Service

统一消息。

支持：

```
邮件

企业微信

微信

短信

SignalR
```

---

# 6.2 MES制造域

---

# Master Data Service

基础数据中心。

负责：

## 工厂模型

```
Factory

 |
Workshop

 |
Line

 |
Station

 |
Equipment

```

## 基础数据

```
物料

单位

人员

班次

供应商

```

---

# Planning Service

计划管理。

功能：

* 生产计划
* 计划拆分
* 产能计算
* 排产

模型：

```
Plan

 |

WorkOrder
```

---

# Material Service

物料管理。

功能：

* 物料
* BOM
* 版本
* 替代料

---

# Material Runtime Service

物料运行时。

负责：

现场动态物料。

核心：

```
Lot

Container

Package

```

支持：

* 批次
* 托盘
* 最小包装
* 物料移动

---

# WorkOrder Service

工单中心。

功能：

* 工单
* 工序
* 执行BOM
* 消耗
* 产出

模型：

```
WorkOrder

 |

Operation

 |

Task

```

---

# Workflow Service

流程服务。

负责：

工单执行流程。

支持：

* 审批
* 工艺流程
* 任务流

---

# Weighting Service

称量服务。

独立领域。

支持：

* 人工称量
* 配方称量
* 增量称量
* 减量称量
* 自动称量
* 高精度称量

设备：

```
电子秤

PLC

OPC UA

Modbus

TCP

Serial
```

---

# Equipment Service

设备管理。

负责：

设备模型：

```
Factory

 |

Line

 |

Equipment

 |

Device
```

功能：

* 台账
* 参数
* 状态
* 保养

---

# IoT Collector Service

设备数据采集。

支持：

```
OPC UA

MQTT

Modbus

TCP

PLC
```

---

# OEE Service

设备效率。

计算：

```
Availability

×

Performance

×

Quality
```

---

# Trace Service

追溯。

支持：

正向：

```
订单

↓

产品

↓

批次

↓

物料

↓

设备

↓

人员

```

反向：

```
物料批次

↓

影响产品

```

---

# Batch Record Service

电子批记录。

支持：

```
生产记录

称量记录

设备记录

检验记录

电子签名

```

---

# Report Service

报表。

支持：

* 生产报表
* OEE报表
* 批记录报表
* 自定义报表

---

# Dashboard Service

可视化。

支持：

* 生产看板
* 设备看板
* 管理驾驶舱

---

# 7. Operation Platform（运维平台）

## Server Monitor Service

独立项目。

定位：

> IT基础设施监控平台。

不属于MES。

结构：

```
Server Monitor

      |

Agent

      |

Collector

      |

Dashboard

```

支持：

Windows：

```
CPU

Memory

Disk

Network

Service

IIS
```

Linux：

```
CPU

Load

Disk

Docker

Process

```

---

# 8. 数据架构设计

## 8.1 数据分类

```
业务数据

生产订单

物料

批次



实时数据

设备状态

服务器状态



历史数据

生产记录

日志



审计数据

操作记录

```

---

# 8.2 数据生命周期

```
实时数据

   |
Redis

   |
PostgreSQL

   |
Archive

   |
MinIO


```

---

# 9. 部署架构

## 初期部署

Docker Compose：

```
Nginx

 |

API Gateway

 |

Services

 |

----------------

PostgreSQL

Redis

RabbitMQ

MinIO


```

---

## 后期

Kubernetes：

```
K8S

 |

Namespace


 |

MES Services


 |

Pods

```

---

# 10. 安全设计

## 身份认证

JWT：

```
Access Token

Refresh Token
```

---

## 数据权限

支持：

```
工厂

车间

产线

```

---

## 审计

所有关键操作：

必须记录：

```
User

IP

Time

Action

Before

After
```

---

# 11. 代码仓库规划

```
IndustrialPlatform


├── docs

│
├── backend

│   ├── Services

│
├── frontend

│   ├── web

│   ├── pda

│   └── mobile


├── agents

│
│── deploy

│
└── tools

```

---

# 12. 后续扩展方向

未来增加：

## WMS

仓储执行

## QMS

质量管理

## APS

高级排产

## Low Code

配置平台

## AI Assistant

智能分析：

```
异常分析

生产预测

设备预测维护

```

---

# 13. 总结

Industrial Platform 最终目标：

```
工业数字化基础平台

=

MES

+

设备

+

称量

+

追溯

+

数据

+

运维

+
 
低代码
```

架构核心：

```
DDD

+

Microservice

+

Event Driven

+

Industrial Data

```

本架构作为后续：

* MES领域设计
* Server Monitor设计
* 前端三端架构
* 开发路线规划

的基础版本。
