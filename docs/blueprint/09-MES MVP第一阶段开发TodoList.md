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

原固定 12 Sprint 顺序调整为阶段门禁。各阶段完成前不得提前宣称后续服务可交付；具体日历根据可运行基线和前端第一批的实际工期重新排期。

```text
Phase 0  BuildingBlocks（已完成）
Phase 1  可运行基线
Phase 2  统一前端第一批
Phase 3  Identity 登录闭环
Phase 4  ReferenceData 服务 + 页面
Phase 5  MasterData 服务 + 页面
Phase 6  OperationalData 服务 + 页面
Phase 7+ 生产闭环服务 + 对应页面
```

---

# 4. Phase 0 BuildingBlocks（已完成）

`CLAUDE.md` 当前记录：BuildingBlocks 测试 64 通过 / 0 失败，全解决方案 74 通过 / 0 失败。执行后续任务前必须重新验证，但不得重复派遣已完成的组件任务。

---

# 5. Phase 1 项目可运行基线

目标：先让基础设施、后端骨架和统一入口真实运行。

```text
PostgreSQL + Redis + RabbitMQ + Seq
→ Identity / ReferenceData health
→ Gateway / 平台 health
→ 一键启动、停止与诊断
→ 新环境冒烟验收
```

任务编号：`TASK-BASE-001` 至 `TASK-BASE-006`。

详细任务统一维护在：`docs/implementation/02A-Industrial Platform可运行基线开发实施方案.md`。

---

# 6. Phase 2 统一前端第一批

固定范围：

```text
Vue 3 + TypeScript + Vite
登录页
PC 管理框架
首页仪表盘
403 / 404
PDA 基础壳
Mobile 基础壳
MockAuthGateway / HttpAuthGateway 替换边界
```

暂缓物料、库存、工单、称量、设备和追溯业务页面，以及离线、扫码、打印、蓝牙和 Capacitor。

任务编号：`TASK-FE-001` 至 `TASK-FE-008`。

详细任务统一维护在：`docs/implementation/02B-Industrial Platform统一前端第一批开发实施方案.md`。

---

# 7. Phase 3 Identity 登录闭环

在现有服务骨架上完成用户、角色、权限、JWT、RefreshToken、注销和撤销，并将前端从 Mock 登录切换到真实 Identity API。

阶段验收必须覆盖登录、刷新、401、403、菜单与按钮权限及关键路径 E2E。详细任务维护在实施文档 03。

---

# 8. Phase 4 ReferenceData 服务 + 页面

完成字典、配置、元数据和编码规则，同时交付相应 PC 管理页面、契约测试和关键路径 E2E。详细任务维护在实施文档 04。

---

# 9. Phase 5 MasterData 服务 + 页面

目标：建立物料、单位、制造组织、仓库/库位、设备、BOM、工艺路线和批次策略等稳定定义，并同步交付对应管理页面。

任务编号：`TASK-MD-001` 至 `TASK-MD-010`。详细任务统一维护在：`docs/implementation/05-Industrial Platform MasterData Service开发实施方案.md`。

---

# 10. Phase 6 OperationalData 服务 + 页面

目标：建立库存批次、余额、预留、仓储单据、不可变流水和 WMS 适配，并同步交付库存查询、批次、收发退、调拨和盘点页面。

任务编号：`TASK-OD-001` 至 `TASK-OD-009`。详细任务统一维护在：`docs/implementation/06-Industrial Platform OperationalData Service开发实施方案.md`。

---

# 11. Phase 7 Planning + WorkOrder

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

# 12. Phase 8 Workflow 生产流程 + 页面

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

# 13. Phase 9 Weighting 称量平台 + 页面

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

# 14. Phase 10 Equipment + IoT Collector + 页面

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

# 15. Phase 11 Trace 追溯 + 页面

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

# 16. Phase 12 Report 报表 + 页面

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

# 17. 业务页面纵向交付规范

本节不再作为业务服务完成后的独立前端 Sprint。下列 PC/PDA/Mobile 页面必须随对应服务在同一阶段交付，并完成契约测试与关键路径 E2E。

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

# 18. MVP 全链路集成测试

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

# 19. 产品化整理

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

# 20. Codex任务拆分规范

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

# 21. 每个任务标准格式

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

# 22. Git提交规范

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

# 23. 测试规范

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

# 24. 第一阶段最终成果

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

# 25. Codex执行建议

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

# 26. 后续扩展路线

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
