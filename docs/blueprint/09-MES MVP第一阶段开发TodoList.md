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

ReferenceData.Service

Permission.Service

Audit.Service


MasterData.Service

OperationalData.Service

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


IndustrialPlatform.slnx

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

# 7. Sprint 3 OperationalData 操作域

目标：

建立一个独立 OperationalData 微服务，在无 WMS 时提供轻量仓储能力，在有 WMS 时作为 MES 统一接口和防腐层。

功能范围：

```text
库存批次与容器
库存余额、预留、冻结和在途量
收料、领料、退料和生产入库
调拨、盘点和库存调整
不可变库存流水
Internal / ExternalWms 库存权威模式
```

数据库：

```text
operationaldata_db
```

任务按依赖顺序派遣：

## TASK-OD-001 创建 OperationalData 服务骨架

**状态：** 可派遣

**目标：** 建立单一 OperationalData 微服务和 Inventory、Lots、Documents、WarehouseOperations、WmsIntegration 模块边界。

**输入文档：** `14A-OperationalData Service详细设计.md`、`06-Industrial Platform 微服务解决方案目录设计.md`、`12-.NET10 Clean Architecture模板设计.md`。

**依赖：** BuildingBlocks、Identity、ReferenceData 和 MasterData 服务骨架可用。

**允许修改范围：** `src/backend/src/Services/OperationalData/**`、`tests/Services/OperationalData/**`、解决方案注册和对应架构测试。

**预期输出：** Api、Application、Contracts、Domain、Infrastructure 项目，正确项目引用、DI 注册、健康检查和架构测试。

**验证与证据：** 提供解决方案构建命令、架构测试命令、退出码和通过数量；证明未引入其他服务数据库引用。

**结果回写：** 更新本节任务状态和 `docs/implementation` 实施进度；结构偏差回写 14A。

**建议提交：** `feat(operational-data): scaffold service boundaries`

---

## TASK-OD-002 实现库存批次与容器

**状态：** 可派遣

**目标：** 实现 InventoryLot、InventoryContainer、批次状态、供应商批次/生产批次关联及拆分合并规则。

**输入文档：** 14A 第 7 节、MasterData 的物料/仓库/库位/批次策略定义。

**依赖：** TASK-OD-001。

**允许修改范围：** OperationalData Domain/Application/Infrastructure 的 Lots 模块及对应测试。

**预期输出：** 批次和容器聚合、状态机、仓储持久化映射及领域测试；所有时间使用 `DateTimeOffset`/`timestamptz`。

**验证与证据：** 提供批次唯一性、冻结/解冻、拆分/合并和并发版本测试结果。

**结果回写：** 回写实体、状态和唯一键的最终命名；设计变化同步到 14A 与数据库模型。

**建议提交：** `feat(operational-data): add inventory lots and containers`

---

## TASK-OD-003 实现库存余额与不可变流水

**状态：** 可派遣

**目标：** 实现 InventoryBalance 和 StockTransaction，保证余额只能由库存流水更新。

**输入文档：** 14A 第 6、11、19、21 节。

**依赖：** TASK-OD-002。

**允许修改范围：** OperationalData 的 Inventory 模块、数据库映射、迁移和测试。

**预期输出：** 在手量、预留量、可用量、冻结量、在途量，乐观并发和不可变流水。

**验证与证据：** 提供余额公式、负库存限制、流水重放、并发冲突和事务回滚测试结果。

**结果回写：** 回写余额维度、索引和并发策略。

**建议提交：** `feat(operational-data): add inventory ledger and balances`

---

## TASK-OD-004 实现库存单据与过账状态机

**状态：** 可派遣

**目标：** 实现 InventoryDocument、单据行、Draft/Confirmed/Posting/Posted/Rejected/Cancelled 状态机、过账和冲销。

**输入文档：** 14A 第 9、10、16、21 节。

**依赖：** TASK-OD-003。

**允许修改范围：** OperationalData 的 Documents 模块、应用用例、持久化和测试。

**预期输出：** 七类单据的公共模型、状态转换、幂等过账、反向流水和审计链。

**验证与证据：** 提供非法状态转换、重复过账、冲销和单据/流水/余额原子提交测试结果。

**结果回写：** 回写最终状态机、错误码和单据编号规则。

**建议提交：** `feat(operational-data): add inventory document posting`

---

## TASK-OD-005 实现库存预留与生产领料

**状态：** 可派遣

**目标：** 根据 WorkOrder 物料需求建立和释放 StockReservation，并通过 MaterialIssue 过账核销预留。

**输入文档：** 14A 第 8、13 节和 WorkOrder 详细设计。

**依赖：** TASK-OD-004；WorkOrder 可提供稳定需求标识。

**允许修改范围：** OperationalData 的 Inventory/WarehouseOperations 模块及契约测试；WorkOrder 仅允许增加已确认的契约适配。

**预期输出：** 预留、释放、部分领料、完全领料和批次选择策略。

**验证与证据：** 提供可用量不足、重复需求、部分核销、工单取消释放和 `MaterialIssued` 契约测试结果。

**结果回写：** 回写 WorkOrder 与 OperationalData 的请求/事件契约。

**建议提交：** `feat(operational-data): add reservations and material issue`

---

## TASK-OD-006 实现收料、退料与生产入库

**状态：** 可派遣

**目标：** 实现 Receipt、MaterialReturn 和 ProductionReceipt 的校验、库存批次创建/关联及过账。

**输入文档：** 14A 第 12、14、15 节。

**依赖：** TASK-OD-004、TASK-OD-005。

**允许修改范围：** OperationalData WarehouseOperations/Lots/Documents 模块及对应契约测试。

**预期输出：** 收料、退料、半成品/产成品入库，以及待检/冻结库存状态。

**验证与证据：** 提供原领料关联、重复收料、批次唯一性、冻结库存和 `MaterialReceived`/`MaterialReturned`/`ProductionReceived` 契约测试结果。

**结果回写：** 回写单据字段、批次来源和质量状态约定。

**建议提交：** `feat(operational-data): add receipt return and production receipt`

---

## TASK-OD-007 实现调拨、盘点与库存调整

**状态：** 可派遣

**目标：** 实现同仓移动、跨仓在途调拨、盘点差异和经授权的库存调整。

**输入文档：** 14A 第 16、22 节。

**依赖：** TASK-OD-004、TASK-OD-006。

**允许修改范围：** OperationalData WarehouseOperations/Documents/Inventory 模块、授权策略和测试。

**预期输出：** Transfer、Stocktake、Adjustment 用例、权限和审计记录。

**验证与证据：** 提供在途量、盘点不直接改库存、调整授权、反向流水和审计测试结果。

**结果回写：** 回写调拨阶段、盘点审批和调整原因模型。

**建议提交：** `feat(operational-data): add transfer stocktake and adjustment`

---

## TASK-OD-008 集成 Trace 与 BatchRecord

**状态：** 可派遣

**目标：** 通过 Outbox 发布稳定库存事件，并由 Trace 和 BatchRecord 建立投影和证据快照。

**输入文档：** 14A 第 20、21 节、Trace 与 BatchRecord 详细设计。

**依赖：** TASK-OD-005、TASK-OD-006、TASK-OD-007。

**允许修改范围：** OperationalData Contracts/Outbox、Trace 和 BatchRecord 对应消费者及契约测试。

**预期输出：** InventoryReserved、MaterialReceived、MaterialIssued、MaterialReturned、ProductionReceived、InventoryTransferred、InventoryAdjusted 和批次状态事件。

**验证与证据：** 提供 Outbox 原子性、Inbox 去重、乱序/重复投递和消费者契约测试结果；证明 Trace/BatchRecord 未回写库存。

**结果回写：** 回写事件版本、字段和消费者状态。

**建议提交：** `feat(operational-data): publish inventory integration events`

---

## TASK-OD-009 实现外部 WMS 适配器

**状态：** 可派遣

**目标：** 实现按仓库配置的 `Internal` / `ExternalWms` 模式和外部 WMS 命令、回执及库存投影。

**输入文档：** 14A 第 17、18、21 节和目标客户 WMS 契约。

**依赖：** TASK-OD-004 至 TASK-OD-008。

**允许修改范围：** OperationalData WmsIntegration/Contracts/Infrastructure、配置和契约测试。

**预期输出：** 带幂等键的 WMS 请求、回执、超时查询、安全重试、人工确认入口和投影更新。

**验证与证据：** 提供重复回执、超时、拒绝、重试、乱序消息和同一仓库单一库存权威测试结果。

**结果回写：** 回写 WMS 适配器能力矩阵、外部错误映射和对账规则。

**建议提交：** `feat(operational-data): add external wms adapter`

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

WorkOrder 通过 OperationalData 建立库存预留并发起领料、退料和生产入库；WorkOrder 不保存库存余额或库存批次状态。

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

Weighting 从 OperationalData 获取已预留的库存批次上下文，发布实际称量结果，但不直接扣减库存或对接外部 WMS。

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

Trace 必须消费 OperationalData 的收料、领料、退料、生产入库、调拨和库存批次状态事件，建立谱系投影；Trace 不作为库存权威源。

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

库存预留


↓

生产领料


↓

生产执行


↓

称量


↓

退料或生产入库


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

## 当前任务边界

本任务只负责：

* 维护蓝图设计
* 将开发目标拆分为可派遣 TODO
* 派遣、跟踪和汇总其他开发任务
* 根据实现结果更新 TODO 状态并回写设计决策

代码实现、自动化测试和具体工程修改默认由其他任务或被派遣的协作者完成。本任务只有在用户明确改变范围时才直接进入开发。

## TODO 生命周期

统一状态：

```
待细化 → 可派遣 → 已派遣 → 开发中 → 待验收 → 已完成
```

实现发现蓝图冲突时标记为 `设计待确认`，先修订蓝图，再重新派遣。

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


## Dependencies


## Modify


## Output


## Test


## Evidence


## Result Writeback


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

├── 操作数据域与轻量 WMS

├── 库存批次、收料、领料、退料和生产入库

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
