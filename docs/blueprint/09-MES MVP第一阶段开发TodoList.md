# 09-MES MVP第一阶段开发TodoList

版本：v1.0
项目名称：Industrial Platform
阶段目标：构建可运行的 MES 产品核心闭环

---

# 1. MVP阶段定位

## 目标

不是做完整MES。

第一阶段目标：

> 建立一个具备真实生产执行能力的工业数字化平台核心。

实现：

```
基础平台
+
主数据
+
工单
+
生产执行
+
称量
+
设备连接
+
追溯
+
基础报表
```

最终形成：

```
订单

 ↓

生产计划

 ↓

工单

 ↓

生产执行

 ↓

物料消耗

 ↓

称量

 ↓

设备数据

 ↓

质量

 ↓

批次追溯

 ↓

报表
```

---

# 2. MVP总体范围

## 包含服务

第一阶段：

```
Identity.Service

Permission.Service

Audit.Service


MasterData.Service

Material.Service

WorkOrder.Service

Workflow.Service

Weighting.Service

Equipment.Service

IoTCollector.Service

Trace.Service

Report.Service


ApiGateway

Frontend.Web

```

---

## 暂缓

第二阶段：

```
OEE.Service

BatchRecord.Service

LowCode.Service

Notification.Service增强

AI分析
```

---

# 3. 开发阶段规划

周期：

约：

```
6个月
```

按照：

```
Sprint = 2周
```

共：

```
12 Sprint
```

---

# 4. Sprint 0 项目初始化

## 目标

建立工业平台工程基础。

时间：

第1-2周

---

## Backend任务

### 创建Solution

目录：

```
backend


Industrial.Platform.sln

```

创建：

```
Industrial.Core

Industrial.Domain

Industrial.EventBus

Industrial.Infrastructure

Industrial.Security

```

---

### 完成基础组件

任务：

```
□ Result<T>

□ Exception体系

□ Entity基类

□ AggregateRoot

□ DomainEvent

□ Snowflake ID

□ Repository接口

□ UnitOfWork

```

---

## 数据库

创建：

```
PostgreSQL

Redis

RabbitMQ

MinIO

```

Docker：

```
docker/development

```

---

## Frontend

初始化：

```
Vue3

TypeScript

Vite

Pinia

Element Plus

ECharts

```

目录：

```
frontend/apps/web

```

---

## 验收

完成：

```
√ 后端启动

√ 前端启动

√ Docker启动

√ Swagger访问

√ 数据库连接
```

---

# 5. Sprint 1 基础平台

## Identity Service

功能：

```
用户

登录

JWT

刷新Token

密码管理

```

---

数据库：

identity_db

表：

```
sys_user

sys_role

sys_refresh_token

```

---

接口：

```
POST /login

POST /refresh

GET /userinfo

```

---

## Permission Service

功能：

```
角色

权限

菜单

数据权限

```

表：

```
sys_permission

sys_role_permission

sys_user_role

```

---

## Audit Service

功能：

记录：

```
登录

操作

数据变化

```

表：

```
audit_log

entity_change_log

```

---

验收：

```
用户登录

JWT认证

权限控制

操作记录
```

---

# 6. Sprint 2 MasterData 服务

## 目标

建立MES基础数据。

---

功能：

## 工厂

```
Factory
```

## 车间

```
Workshop

```

## 产线

```
Line

```

## 工作中心

```
WorkCenter

```

## 物料

```
Material

```

## 产品

```
Product

```

---

数据库：

masterdata_db

---

接口：

```
GET /materials

POST /materials

PUT /materials/{id}

```

---

前端：

```
基础数据管理

```

---

# 7. Sprint 3 Material服务

## 功能

库存基础：

```
物料批次

库存

入库

出库

领料

退料

```

---

表：

```
material_lot

inventory

material_transaction

```

---

事件：

发布：

```
MaterialIssuedEvent

```

---

# 8. Sprint 4 Planning + WorkOrder

## Planning

功能：

```
生产计划

产能配置

计划拆分

```

---

WorkOrder

核心。

功能：

```
创建工单

释放工单

关闭工单

暂停工单

```

---

状态：

```
Created

Released

Running

Completed

Cancelled

```

---

事件：

发布：

```
WorkOrderCreatedEvent

WorkOrderReleasedEvent

WorkOrderCompletedEvent

```

---

# 9. Sprint 5 Workflow生产流程

目标：

支持：

```
工艺路线

工序

生产过站

```

---

模型：

```
Route

↓

Operation

↓

Station

```

---

功能：

```
开始工序

完成工序

异常暂停

```

---

事件：

```
OperationStartedEvent

OperationCompletedEvent

```

---

# 10. Sprint 6 Weighting称量平台

这是你的核心优势模块。

独立设计。

---

## 功能范围

### 称量任务

```
生成任务

派工

执行

完成

审核

```

---

### 支持设备

第一阶段：

```
串口

TCP

OPC UA

Modbus

```

---

### 数据记录

必须保存：

```
目标重量

实际重量

误差

操作人

设备

时间

```

---

数据库：

weighting_db

表：

```
weight_task

weight_record

weight_item

weight_device

```

---

事件：

```
WeightCompletedEvent

```

---

# 11. Sprint 7 Equipment + IoT Collector

## Equipment Service

管理：

```
设备档案

设备状态

设备参数

```

---

表：

```
equipment

equipment_parameter

```

---

## IoT Collector

.NET Worker

负责：

```
连接设备

采集数据

转换协议

发送事件

```

---

支持：

```
OPC UA

Modbus TCP

TCP

HTTP
```

---

数据流：

```
设备


↓

Agent


↓

RabbitMQ


↓

IoT Collector


↓

TimescaleDB

```

---

# 12. Sprint 8 Trace追溯

核心能力。

---

实现：

```
原料批次

↓

生产批次

↓

工单

↓

设备

↓

人员

↓

质量

```

---

数据库：

trace_db

表：

```
trace_lot

trace_relation

trace_event

```

---

查询：

支持：

正向：

```
原料去哪了

```

反向：

```
产品用了哪些材料

```

---

# 13. Sprint 9 Report报表

第一阶段：

基础报表。

---

功能：

```
生产日报

工单完成率

物料消耗

称量记录

追溯查询

```

---

技术：

```
Report Service

DevExpress Reports

或者

自研模板引擎
```

---

# 14. Sprint 10 Frontend完善

统一：

PC/PDA/Mobile。

---

实现：

## PC

```
生产管理

计划管理

报表

配置

```

---

## PDA

```
工单执行

扫码

称量

过站

```

---

## Mobile

```
查看

审批

报警

```

---

# 15. Sprint 11 集成测试

测试：

## 生产流程

完整链路：

```
创建产品


↓

创建计划


↓

生成工单


↓

生产执行


↓

称量


↓

设备采集


↓

追溯


↓

报表

```

---

## 性能测试

指标：

API：

```
1000 req/s

```

RabbitMQ：

```
10000 msg/min

```

Timescale：

```
百万级数据查询

```

---

# 16. Sprint 12 产品化整理

目标：

形成产品基础。

---

完成：

```
Docker部署

配置中心

日志体系

异常监控

数据库备份

升级脚本

文档

```

---

# 17. Codex任务拆分规范

以后所有开发任务：

必须拆小。

例如：

错误：

```
开发WorkOrder服务

```

正确：

```
TASK-WO-001

创建WorkOrder.Domain项目


输入：

DDD设计文档


输出：

Entity结构


验收：

Build成功


Commit:

feat(workorder): add domain project

```

---

# 18. 每个任务标准格式

文件：

```
docs/tasks


TASK-XXXX.md

```

模板：

```markdown

# Task


## Goal


## Input


## Modify


## Output


## Test


## Commit Message


```

---

# 19. Git提交规范

采用：

Conventional Commit。

格式：

```
type(scope): message
```

---

示例：

新增：

```
feat(workorder): add create work order api
```

修复：

```
fix(weighting): fix weight calculation
```

文档：

```
docs(database): update schema design
```

---

# 20. 测试规范

每个服务必须：

```
UnitTest

IntegrationTest

ApiTest

```

---

例如：

WorkOrder：

```
WorkOrder.Domain.Tests


WorkOrder.Application.Tests


WorkOrder.Api.Tests

```

---

# 21. 第一阶段最终成果

完成后：

你拥有：

```
Industrial Platform v1.0


├── 用户权限

├── MES基础数据

├── 生产计划

├── 工单管理

├── 生产执行

├── 称量系统

├── 设备采集

├── 数据追溯

├── 基础报表

└── Docker部署
```

---

# 22. Codex执行建议

建议工作方式：

## 一个任务一个Context

例如：

```
Codex


TASK-WO-001

↓

完成

↓

提交


TASK-WO-002

↓

完成

↓

提交

```

不要：

```
一次让AI开发整个MES

```

---

# 23. 后续扩展路线

MVP完成后：

加入：

```
Batch Record

OEE

电子签名

GMP合规

LIMS增强

低代码配置

AI生产分析

数字孪生

```

---

# 
