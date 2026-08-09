# 26-Industrial Platform数据库最终模型

> Industrial Platform
> 工业数字化执行平台
> Database Architecture & Data Model V1.0

---

# 文档说明

本文档定义：

```
Industrial Platform
```

最终数据库体系。

目标：

为后续：

* Codex自动生成数据库脚本
* 微服务开发
* 数据迁移
* 自动化测试
* 企业部署

提供统一数据库设计基线。

---

# 1. 数据库总体设计

## 1.1 设计原则

Industrial Platform采用：

```
领域数据库隔离
+
统一数据规范
+
事件驱动同步
+
工业数据冷热分层
+
审计追踪
```

---

# 2. 数据库整体规划

最终数据库：

```
industrial_platform

├── identity_db
│
├── tenant_db
│
├── referencedata_db
│
├── masterdata_db
│
├── planning_db
│
├── workorder_db
│
├── weighting_db
│
├── iot_db
│
├── trace_db
│
├── batch_db
│
├── quality_db
│
├── industrial_data_db
│
├── lowcode_db
│
├── ai_db
│
├── monitor_db
│
└── audit_db
```

---

# 3. PostgreSQL规范

## 3.1 Schema规范

每个数据库：

```
public
```

核心业务。

```
audit
```

审计。

```
archive
```

历史归档。

例如：

```
workorder_db

public

audit

archive

```

---

# 4. 通用字段规范

所有业务表：

```sql
id uuid primary key
```

---

基础字段：

```sql
tenant_id uuid not null

factory_id uuid

create_time timestamptz

create_user varchar(50)

update_time timestamptz

update_user varchar(50)

is_deleted boolean default false

version int
```

---

# 5. 基础审计模型

所有领域对象继承：

```
BaseEntity
```

模型：

```csharp
public abstract class EntityBase
{

public Guid Id {get;set;}

public Guid TenantId {get;set;}

public DateTimeOffset CreateTime {get;set;}

public string CreateUser {get;set;}

public DateTimeOffset? UpdateTime {get;set;}

public string UpdateUser {get;set;}

public bool IsDeleted {get;set;}

}
```

---

# 6. Identity数据库设计

数据库：

```
identity_db
```

负责：

* 用户
* 角色
* 权限
* Token

---

## 6.1 用户表

identity_user

```sql
create table identity_user
(

id uuid primary key,

username varchar(100),

password_hash varchar(500),

email varchar(200),

phone varchar(50),

status int,

create_time timestamptz

);
```

---

## 6.2 角色

identity_role

字段：

```
id

name

code

description

```

---

## 6.3 用户角色

identity_user_role

关系：

```
User

N:N

Role
```

---

## 6.4 权限

identity_permission

例如：

```
workorder:view

workorder:create

workorder:approve

```

---

# 7. Tenant数据库

tenant_db

支持SaaS。

---

## tenant

```sql
tenant
{

id

name

code

status

expire_time

}

```

---

## 工厂组织模型

```
Tenant

 |

Factory

 |

Workshop

 |

Line

 |

Station

```

---

表：

```
tenant_factory

tenant_workshop

tenant_line

tenant_station

```

---

# 8. MasterData数据库

masterdata_db

基础主数据。

---

## 8.1 物料

material

```sql
material

id

code

name

specification

unit

material_type

```

---

## 8.2 设备模型

equipment_model

例如：

```
Mixer

Filling Machine

Packing Machine

```

---

## 8.3 设备

equipment

```sql
equipment

id

code

name

model_id

line_id

status

```

---

## 8.4 BOM

material_bom

结构：

```
Product

 |

BOM

 |

Material

```

---

# 8A. OperationalData数据库

数据库：

```text
operationaldata_db
```

OperationalData 独占库存事实和仓储业务单据：

```text
inventory_balance
inventory_lot
inventory_container
stock_reservation
inventory_document
inventory_document_line
stock_transaction
wms_request
wms_callback
outbox_message
inbox_message
```

核心约束：

- `inventory_balance` 按仓库、库位、物料、库存批次和库存状态唯一。
- `stock_transaction` 不可变，使用单据过账和冲销保持完整审计链。
- 单据、流水、余额和 Outbox 在同一本地事务提交。
- 时间列使用 `timestamptz`；幂等键和外部 WMS 标识建立唯一索引。
- 同一仓库只允许 `Internal` 或 `ExternalWms` 一个库存权威源。

---

# 9. Planning数据库

planning_db

生产计划。

---

## production_plan

字段：

```
id

plan_no

product_id

quantity

start_time

end_time

status

```

---

## capacity_plan

产能配置。

```
line_id

capacity

shift

```

---

# 10. WorkOrder数据库

workorder_db

核心MES数据库。

---

# 10.1 工单

work_order

```sql
work_order
{

id

order_no

product_id

quantity

status

plan_id

}

```

---

状态：

```
Created

Released

Running

Completed

Closed

```

---

# 10.2 工序

work_order_operation

```sql
id

work_order_id

operation_id

sequence

status

```

---

# 10.3 执行任务

execution_task

```sql
id

operation_id

task_type

operator

start_time

end_time

```

---

# 11. Weighting数据库

weighting_db

称量执行。

---

## weighing_order

称量任务。

```sql
id

order_no

material_id

target_qty

actual_qty

status

```

---

## weighing_record

称量记录。

```sql
id

weighing_order_id

device_id

weight

operator

time

```

---

## 称量防错

weighing_check

例如：

```
物料扫码

容器扫码

设备确认

```

---

# 12. IoT数据库

iot_db

设备数据。

---

## device_point

测点。

```sql
id

equipment_id

code

name

datatype

```

---

## realtime_data

实时数据。

建议：

TimescaleDB。

字段：

```
time

point_id

value

quality

```

---

## alarm_record

报警。

```
id

equipment_id

alarm_code

message

level

time

```

---

# 13. Trace数据库

trace_db

全过程追溯。

核心：

Trace Graph。

---

## trace_object

追溯对象。

例如：

```
Material Batch

Product Batch

Container

Pallet

```

---

## trace_relation

关系表。

```sql
trace_relation

from_id

to_id

relation_type

time

```

例如：

```
原料批次

↓

生产批次

↓

成品批次

```

---

# 14. Batch Record数据库

batch_db

电子批记录。

---

## batch_record

```sql
id

batch_no

product_id

status

```

---

## batch_step

步骤。

```
batch_id

operation

parameter

result

```

---

# 15. Quality数据库

quality_db

质量管理。

---

## inspection_record

检验记录。

```
batch_id

item

value

result

```

---

## deviation

偏差。

```
id

batch_id

description

reason

action

```

---

# 16. Industrial Data数据库

industrial_data_db

工业数据中心。

---

数据模型：

```
Raw Data

↓

Clean Data

↓

Business Data

↓

Analytics Data

```

---

## data_asset

数据资产。

```
id

name

type

source

```

---

## metric_definition

指标。

例如：

```
OEE

Energy Consumption

Yield

```

---

# 17. LowCode数据库

lowcode_db

配置平台。

---

## entity_definition

实体。

```
id

name

fields_json

```

---

## page_definition

页面。

```
id

layout_json

```

---

## workflow_definition

流程。

```
id

node_json

```

---

# 18. AI数据库

ai_db

---

## ai_conversation

会话。

```
id

user_id

agent

title

```

---

## ai_message

消息。

```
conversation_id

role

content

token

```

---

## ai_agent

智能体。

```
id

name

prompt

tools

```

---

## ai_knowledge_document

知识库。

```
id

name

type

source

```

---

## ai_vector_chunk

向量。

使用：

pgvector。

```
id

document_id

content

embedding

```

---

# 19. Monitor数据库

monitor_db

Server Monitor。

---

## server_node

服务器。

```
id

name

ip

os

```

---

## server_metric

指标。

```
server_id

cpu

memory

disk

time

```

---

# 20. Audit数据库

统一审计。

---

## audit_log

```sql
audit_log

id

tenant_id

user_id

action

module

before_json

after_json

time

```

---

# 21. 数据归档设计

工业数据量巨大。

采用：

冷热分离。

---

## 热数据

在线：

```
0-3个月
```

---

## 温数据

查询：

```
3个月-2年
```

---

## 冷数据

归档：

```
2年以上
```

存储：

```
MinIO

对象存储

```

---

# 22. 数据同步架构

禁止：

跨数据库直接Join。

采用：

事件。

例如：

工单完成：

```
WorkOrder Service

发布

workorder.completed

↓

Trace

Batch

AI

消费

```

---

# 23. 数据访问规范

微服务：

只能访问：

```
自己的数据库
```

禁止：

```
WorkOrder

直接查询

Trace DB

```

---

# 24. 索引规范

所有表：

必须：

```
Primary Key

TenantId Index

CreateTime Index

Business No Index

```

例如：

```sql
create index idx_workorder_tenant

on work_order(tenant_id);

```

---

# 25. 分区设计

大表：

例如：

iot_realtime_data

按照时间：

```sql
PARTITION BY RANGE(time)
```

月份：

```
iot_data_202608

iot_data_202609

```

---

# 26. 数据库迁移

采用：

```
DbUp

Flyway

EF Migration

```

推荐：

DbUp。

目录：

```
database

├── identity

├── workorder

├── trace

├── ai

└── scripts

```

---

# 27. Codex生成任务拆分

## Task01

生成数据库工程

```
/database

```

---

## Task02

生成所有初始化SQL

包含：

```
Create Database

Create Table

Index

Seed Data

```

---

## Task03

生成SqlSugar实体

例如：

```
WorkOrderEntity.cs

MaterialEntity.cs

```

---

## Task04

生成Repository

```
IRepository

Repository

```

---

## Task05

生成测试数据

```
Factory

Material

Equipment

WorkOrder

```

---

# 28. 最终数据库架构

```
                    Industrial Platform


 PostgreSQL

 |
 + identity_db

 + tenant_db

 + referencedata_db

 + masterdata_db

 + workorder_db

 + weighting_db

 + trace_db

 + batch_db

 + quality_db


 TimescaleDB

 |
 + IoT Data


 MinIO

 |
 + Documents


 Vector DB

 |
 + AI Knowledge


 Prometheus

 |
 + Monitor


```

---
