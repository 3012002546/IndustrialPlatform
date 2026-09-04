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

平台基础层当前 Service Host、内部模块和阶段映射以 `32-Industrial Platform Service Host与内部模块边界.md` 为权威母版；本文中按领域列出的独立 Service 名称如与母版冲突，表示未来可拆分目标，不表示当前独立部署宿主。

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

 MES Service                  Operations Center

 Planning                     Agent

 OperationalData              Alarm

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

 SystemData

 ReferenceData

 Audit

 File

 Notification

 Collaboration

 Remote Assistance

 Scheduler

 Platform Health

 Low Code

 Dashboard

 Report


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

## 3.2 当前平台优先开发顺序

```text
BuildingBlocks
      ↓
Identity
      ↓
平台视觉与外壳
      ↓
SystemData + ReferenceData
      ↓
Audit + File + Notification
      ↓
Collaboration + RemoteAssistance
      ↓
Scheduler + Platform Health
      ↓
Low Code + Dashboard & Report
      ↓
ServerMonitor
      ↓
Operations Center Knowledge & Assistant
      ↓
IoT Collector
      ↓
MasterData + OperationalData + MES业务域
```

以上是产品开发优先级，不表示所有模块存在直接技术依赖。实际阶段编号、并行条件和门禁以 `09-Industrial Platform开发总TodoList.md` 为准。

SystemData 负责行政组织、岗位、菜单导航、功能开关、服务目录、主题默认值，以及后续服务数据库的受控编排与环境引导；ReferenceData 负责字典、参数、动态配置、元数据、编码规则、状态机定义与通用计量单位/换算；MasterData 负责物料、设备、制造组织、仓库、库位、BOM 及物料专属单位比例；OperationalData 负责库存批次、余额、预留、收发退和仓储业务单据。业务当前状态、前置条件、权限、事务和历史由相应业务服务拥有，ReferenceData 只提供固定版本定义，不提供跨业务 SetStatus。四者边界独立。数据库编排只管理登记、plan/apply、数据库/角色/授权与迁移执行状态，各业务服务仍拥有自己的领域 Schema 和版本化迁移产物，完整决策读取蓝图 33。

平台基础功能、工业合规聊天、远程协助、主题体系和独立模块边界详见 `05-Industrial Platform平台基础功能与独立模块设计.md`。

当前平台基础层固定为七个核心 Service Host：`Identity.Service`、`SystemData.Service`、`ReferenceData.Service`、`Collaboration.Service`、`PlatformStudio.Service`、`OperationsCenter.Service`、`IoTCollector.Service`。Worker、Agent、Screego、TURN 与本地模型运行时属于辅助部署单元，不计入核心 Service Host 数量。

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

## 4.2 微服务与内部模块原则

独立 Service Host：

拥有：

* 独立代码
* 独立数据库
* 独立部署
* 独立扩展

当前初期部署允许一个 Service Host 承载多个内部模块。共享宿主不等于共享领域：模块必须独立建模，使用独立 Schema 或表前缀、契约、权限、迁移与测试；禁止跨模块直读 Repository 或数据表，并预留未来物理拆分。阶段编号也不等于微服务数量。

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

OperationalData Service

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

下列名称是服务 `LogicalDatabaseName`，图示表达 Test/Staging/Production 的每服务物理数据库拓扑，物理目标由 SystemData 解析。Development 可使用配置的共享 PostgreSQL `industrial_platform_dev` 或共享 SQLite 文件，但仍禁止跨服务表访问和合并迁移账本；完整契约见蓝图 07、蓝图 33。

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

本章保留领域拆分和未来目标服务设计。当前平台基础层的正式部署宿主与模块归属读取蓝图 32，不得把下列每个领域标题直接解释为当前独立进程或数据库。

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

此处 MasterData 的“单位”仅指物料单位选用与物料专属换算；通用维度、单位及换算定义归 ReferenceData，不建立另一套单位中心。

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

# OperationalData Service

操作数据域与库存中心。

负责现场动态物料、库存事实和仓储业务单据。

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
* 库存余额与预留
* 收料、领料、退料和生产入库
* 调拨、盘点和库存调整
* 外部 WMS 对接

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

# 7. Operations Center（运维与实施知识中心）

## OperationsCenter.Service

当前独立 Service Host，内部包含 `ServerMonitor`、`ProjectWorkspace`、`KnowledgeBase`、`IssueTracking`、`KnowledgeAssistant`、`DataAssistant` 和 `ModelGateway`。其权威母版边界见蓝图 32；PF-10 单独处理 ServerMonitor，PF-10A 处理其余模块并先补齐 IssueTracking 与 KnowledgeBase 完整数据闭环。各模块的表、API 和页面留给对应阶段管理任务设计。

### ServerMonitor 模块

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
* Operations Center设计
* 前端三端架构
* 开发路线规划

的基础版本。
