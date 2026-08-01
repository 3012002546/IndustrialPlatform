# 15-WorkOrder Service详细设计

> Industrial Platform
> 工业数字化执行平台
> WorkOrder Service（生产工单服务）

版本：v1.0
定位：MES核心业务服务
架构：DDD + Clean Architecture + Microservice + Event Driven

---

# 1. Service定位

## 1.1 服务职责

WorkOrder Service 是 Industrial Platform 的**生产执行核心服务**。

负责：

* 生产工单管理
* 工单生命周期管理
* 工单拆分
* 工单派工
* 工单状态控制
* 工单与计划关联
* 工单与物料需求关联
* 工单与BOM关联
* 工单与工艺路线关联
* 工单执行进度管理
* 生产异常记录入口

不负责：

| 业务     | 所属服务                  |
| ------ | --------------------- |
| 物料基础信息 | MasterData Service    |
| 用户权限   | Identity Service      |
| 设备采集   | IoT Collector Service |
| 称量执行   | Weighting Service     |
| 质量检验   | Quality Service（未来）   |
| 批记录    | Batch Record Service  |
| 追溯     | Trace Service         |

---

# 2. WorkOrder Service在整体架构位置

```
                    Identity Service
                          |
                          |
                  MasterData Service
                          |
                          |
Planning Service
       |
       |
       v

+-----------------------------+
|                             |
|     WorkOrder Service       |
|                             |
+-----------------------------+

       |
       |
       +------------+
       |            |
       v            v

Weighting Service   IoT Collector

       |
       v

Trace Service

       |
       v

Batch Record Service
```

---

# 3. 业务定位

传统MES：

```
订单
 |
生产计划
 |
生产工单
 |
派工
 |
生产执行
 |
报工
 |
完工
```

Industrial Platform：

```
销售需求
 |
Planning Service
 |
生产计划
 |
WorkOrder Service
 |
生产执行
 |
事件流
 |
+----------------+
|
+--称量
+--设备
+--质量
+--追溯
+--批记录
```

WorkOrder 是整个生产过程的业务中心。

---

# 4. 核心业务模型

## 4.1 Aggregate设计

核心聚合：

```
WorkOrder Aggregate

        |
        |
        +-- WorkOrderItem
        |
        +-- WorkOrderMaterial
        |
        +-- WorkOrderOperation
        |
        +-- WorkOrderRoute
        |
        +-- WorkOrderStatusHistory

```

---

# 5. Domain Model设计

## 5.1 WorkOrder

生产工单聚合根

```csharp
public class WorkOrder : AggregateRoot<Guid>
{

    public string OrderNo {get;private set;}

    public string ProductId {get;private set;}

    public decimal PlanQty {get;private set;}

    public decimal CompletedQty {get;private set;}


    public WorkOrderStatus Status {get;private set;}


    public DateTime PlanStartTime {get;private set;}

    public DateTime PlanEndTime {get;private set;}


    private readonly List<WorkOrderOperation> _operations;


}
```

---

# 6. 工单状态机设计

## 6.1 状态定义

```
Draft
 |
 v

Released
 |
 v

Scheduled
 |
 v

Running
 |
 +------+
 |      |
 v      v

Paused Completed

 |
 v

Closed


Cancelled
```

---

## 6.2 状态说明

| 状态        | 说明  |
| --------- | --- |
| Draft     | 创建  |
| Released  | 发布  |
| Scheduled | 排产  |
| Running   | 生产中 |
| Paused    | 暂停  |
| Completed | 完成  |
| Closed    | 关闭  |
| Cancelled | 取消  |

---

## 6.3 状态转换规则

```csharp
public enum WorkOrderStatus
{

 Draft=0,

 Released=10,

 Scheduled=20,

 Running=30,

 Paused=40,

 Completed=50,

 Closed=60,

 Cancelled=99

}
```

状态转换：

```
Draft
 |
Release()

Released


Released
 |
Schedule()

Scheduled


Scheduled
 |
Start()

Running


Running
 |
Pause()

Paused


Paused
 |
Resume()

Running


Running
 |
Complete()

Completed


Completed
 |
Close()

Closed

```

---

# 7. 工单业务流程

## 7.1 创建工单

来源：

* MRP
* ERP接口
* 手工创建

流程：

```
Create WorkOrder

        |
        |
校验产品

        |
        |
读取BOM

        |
        |
读取工艺路线

        |
        |
生成工单

        |
        |
发布事件

```

事件：

```
WorkOrderCreatedEvent

```

---

# 8. WorkOrder Service Solution设计

目录：

```
src/services

/WorkOrder

    /Industrial.WorkOrder.API

    /Industrial.WorkOrder.Application

    /Industrial.WorkOrder.Domain

    /Industrial.WorkOrder.Infrastructure

    /Industrial.WorkOrder.Contracts

```

---

# 9. Clean Architecture结构

## API

```
Controllers

Middleware

Authentication

DependencyInjection

Program.cs

```

---

## Application

```
Commands

Queries

DTO

Validators

Handlers

Services

```

示例：

```
CreateWorkOrderCommand

CreateWorkOrderHandler


ReleaseWorkOrderCommand

ReleaseWorkOrderHandler


StartWorkOrderCommand

StartWorkOrderHandler

```

---

## Domain

```
Aggregates

Entities

ValueObjects

Events

Enums

Repositories

```

---

## Infrastructure

```
Persistence

Repositories

RabbitMQ

Redis

ExternalService

```

---

# 10. 数据库设计

数据库：

```
mes_workorder
```

---

# 10.1 work_orders

```sql
CREATE TABLE work_orders
(

id uuid PRIMARY KEY,


order_no varchar(50),


product_id varchar(50),


plan_qty numeric(18,3),


completed_qty numeric(18,3),


status int,


plan_start_time timestamp,


plan_end_time timestamp,


created_time timestamp,


created_by varchar(50)


);

```

---

# 10.2 work_order_operations

工序

```sql
CREATE TABLE work_order_operations
(

id uuid PRIMARY KEY,


work_order_id uuid,


operation_code varchar(50),


operation_name varchar(100),


sequence int,


status int


);

```

---

# 10.3 work_order_materials

工单物料

```sql
CREATE TABLE work_order_materials
(

id uuid PRIMARY KEY,


work_order_id uuid,


material_id varchar(50),


require_qty numeric(18,3),


issued_qty numeric(18,3)


);

```

---

# 10.4 work_order_status_history

状态追踪

```sql
CREATE TABLE work_order_status_history
(

id uuid,


work_order_id uuid,


old_status int,


new_status int,


remark varchar(200),


created_time timestamp

);

```

---

# 11. Repository设计

接口：

```csharp
public interface IWorkOrderRepository
{

Task<WorkOrder?> GetAsync(Guid id);


Task AddAsync(
WorkOrder entity);


Task UpdateAsync(
WorkOrder entity);


}

```

---

# 12. API设计

Base:

```
/api/workorders

```

---

## 创建工单

POST

```
/api/workorders

```

Request:

```json
{

"productId":"P001",

"qty":1000,

"planStart":"2026-08-01"

}

```

Response:

```json
{

"id":"xxxx",

"orderNo":"WO202608010001"

}

```

---

## 发布工单

POST

```
/api/workorders/{id}/release

```

---

## 开始生产

POST

```
/api/workorders/{id}/start

```

---

## 暂停

POST

```
/api/workorders/{id}/pause

```

---

## 完工

POST

```
/api/workorders/{id}/complete

```

---

## 查询

GET

```
/api/workorders


```

支持：

```
status

product

date

line

```

---

# 13. RabbitMQ事件设计

Exchange:

```
industrial.workorder.exchange

```

类型：

topic

---

# 13.1 工单创建

RoutingKey:

```
workorder.created

```

Payload:

```json
{

"workOrderId":"",

"orderNo":"WO001",

"productId":"P001",

"qty":1000

}

```

消费者：

```
Trace Service

Batch Record Service

```

---

# 13.2 工单开始

RoutingKey:

```
workorder.started

```

消费者：

```
IoT Collector

Weighting Service

```

---

# 13.3 工单完成

RoutingKey:

```
workorder.completed

```

消费者：

```
Trace

Quality

BatchRecord

```

---

# 14. Redis设计

用途：

* 工单实时状态
* 当前生产任务
* PDA快速查询

Key设计：

```
workorder:{id}

```

Value:

```json
{

"status":"Running",

"qty":500,

"machine":"LINE01"

}

```

---

生产线当前任务：

```
line:{lineId}:current-order

```

---

# 15. SignalR实时推送

Hub:

```
WorkOrderHub

```

事件：

```
工单状态变化

生产数量变化

异常

```

客户端：

```
PC MES

PDA

大屏

```

---

# 16. 与其他服务交互

## MasterData

获取：

```
产品

BOM

工艺路线

单位

```

方式：

REST + Cache

---

## Planning Service

输入：

```
生产计划

```

输出：

```
WorkOrder

```

---

## Weighting Service

发送：

```
工单开始

需要称量物料

```

---

## IoT Collector

发送：

```
设备生产数据

```

---

# 17. MVP第一阶段范围

必须实现：

## 工单基础

✅ 创建工单

✅ 编辑工单

✅ 删除

✅ 发布

✅ 状态机

---

## 工单执行

✅ 开始

✅ 暂停

✅ 完成

---

## 查询

✅ 列表

✅ 详情

✅ 状态历史

---

## 事件

✅ Created

✅ Started

✅ Completed

---

# 18. 第二阶段扩展

## 高级排产

增加：

```
APS算法

产能计算

资源约束

```

---

## 工序执行

增加：

```
Operation Dispatch

工位任务

```

---

## 电子批记录

增加：

```
EBR

Batch Step

Signature

```

---

# 19. Codex开发任务拆分

建议拆成：

---

## Task-01

初始化WorkOrder Service

生成：

```
Solution

Project

依赖

Docker配置

```

提交：

```
feat:init workorder service
```

---

## Task-02

Domain模型

实现：

```
WorkOrder Aggregate

状态机

Domain Events

```

提交：

```
feat:add workorder domain
```

---

## Task-03

数据库

实现：

```
Tables

Migration

Repository

```

提交：

```
feat:add workorder persistence
```

---

## Task-04

Application CQRS

实现：

```
Commands

Queries

Handlers

Validators

```

提交：

```
feat:add workorder application
```

---

## Task-05

API

实现：

```
Controller

Swagger

JWT

```

提交：

```
feat:add workorder api
```

---

## Task-06

RabbitMQ

实现：

```
Events

Publisher

Consumer

```

提交：

```
feat:add workorder messaging
```

---

## Task-07

Redis缓存

实现：

```
Status Cache

CurrentOrder Cache

```

提交：

```
feat:add redis cache
```

---

## Task-08

测试

包含：

```
Domain Test

API Test

Integration Test

```

提交：

```
test:add workorder tests
```

---

# 20. 后续演进路线

WorkOrder Service最终成为：

```
Production Execution Kernel

```

能力：

```
订单中心

+
生产调度

+
执行控制

+
过程追踪

+
工业事件中心

```

未来支持：

* 离散制造
* 流程制造
* 医药GMP
* 食品批次
* 化工生产
* 新能源制造

---

