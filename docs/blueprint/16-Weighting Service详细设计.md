继续输出：

# 16-Weighting Service详细设计.md

> Industrial Platform
> 工业数字化执行平台
> Weighting Service（称量执行服务）

版本：v1.0

定位：

MES核心执行子系统

架构：

DDD
+
Clean Architecture
+
Microservice
+
Event Driven
+
Industrial Data Platform

---

# 1. Service定位

## 1.1 服务职责

Weighting Service 是 Industrial Platform 面向工业现场的**称量执行核心服务**。

负责：

* 原料称量
* 配料称量
* 投料称量
* 批次称量
* 多秤管理
* 称量任务生成
* PDA称量执行
* 称量防错
* 容差判断
* 复核确认
* 电子签名
* 称量记录
* 称量追溯

---

## 1.2 不负责

| 业务    | 服务                    |
| ----- | --------------------- |
| 物料主数据 | MasterData Service    |
| 生产工单  | WorkOrder Service     |
| 设备通讯  | IoT Collector Service |
| 批记录   | Batch Record Service  |
| 质量检验  | Quality Service       |
| 追溯    | Trace Service         |

---

# 2. 工业场景定位

传统MES称量：

```
工单
 |
配方
 |
人工查看
 |
拿料
 |
称量
 |
记录
```

Industrial Platform：

```
WorkOrder
    |
    |
Weighting Service
    |
    |
称量任务
    |
    |
PDA执行
    |
    |
电子秤数据
    |
    |
自动判断
    |
    |
追溯事件
```

---

# 3. 支持行业场景

## 3.1 离散制造

例如：

汽车零件

```
物料A
+
物料B
+
物料C

=> 产品
```

---

## 3.2 食品

```
配方
 |
批次
 |
原料称量
 |
投料

```

---

## 3.3 医药GMP

支持：

* 双人复核
* 电子签名
* 审计追踪
* 批记录

---

## 3.4 化工

支持：

* 高精度称量
* 防错
* 批次隔离

---

# 4. 服务架构位置

```
                WorkOrder Service

                       |
                       |
                       v


              Weighting Service


                       |
       +---------------+--------------+

       |                              |

 IoT Collector                 PDA Client


       |

       v


   Scale Device


```

---

# 5. 核心领域模型

## Aggregate设计

核心聚合：

```
WeightTask Aggregate


        |
        |
        +-- WeightItem

        |
        |
        +-- MaterialBatch

        |
        |
        +-- WeightRecord

        |
        |
        +-- Approval

```

---

# 6. Domain Model

## 6.1 WeightTask

称量任务

```csharp
public class WeightTask 
    : AggregateRoot<Guid>
{


    public string TaskNo {get;private set;}


    public string WorkOrderId {get;private set;}


    public WeightTaskStatus Status 
    {
        get;
        private set;
    }


    public List<WeightItem> Items
    {
        get;
        private set;
    }

}
```

---

# 6.2 WeightItem

称量明细

```csharp
public class WeightItem
{


    public string MaterialId {get;private set;}


    public decimal TargetQty {get;private set;}


    public decimal ActualQty {get;private set;}


    public decimal Tolerance {get;private set;}


    public WeightStatus Status {get;private set;}

}

```

---

# 7. 称量状态机设计

## 7.1 WeightTask状态

```
Created

 |
 v

Released

 |
 v

Executing

 |
 +-------------+

 |             |

 v             v

Completed   Cancelled


```

---

## 7.2 WeightItem状态

```
Waiting

 |

Picking

 |

Weighting

 |

Qualified

 |

Confirmed


```

---

# 8. 称量业务流程

## 8.1 创建称量任务

来源：

* WorkOrder
* Recipe
* Batch

流程：

```
生产工单

    |

读取BOM

    |

生成称量需求

    |

创建WeightTask

    |

发布事件

```

事件：

```
WeightTaskCreatedEvent

```

---

# 8.2 PDA执行流程

```
登录

 |

扫描任务二维码

 |

扫描物料条码

 |

扫描批次

 |

连接电子秤

 |

读取重量

 |

判断误差

 |

确认

 |

完成

```

---

# 9. 防错模型设计

## 9.1 防错规则

支持：

### 物料防错

```
要求：

Material=A001


扫描：

A002


结果：

Reject

```

---

### 批次防错

```
Batch Expired

Reject

```

---

### 数量防错

目标：

```
10kg

```

允许：

```
9.95~10.05

```

---

# 10. Solution结构设计

```
src/services


/Weighting


    /Industrial.Weighting.API


    /Industrial.Weighting.Application


    /Industrial.Weighting.Domain


    /Industrial.Weighting.Infrastructure


    /Industrial.Weighting.Contracts

```

---

# 11. Clean Architecture

## Domain

```
Aggregates

Entities

ValueObjects

DomainEvents

Rules

```

---

## Application

```
Commands

Queries

DTO

Handlers

Validators

```

---

## Infrastructure

```
SqlSugar

Repository

RabbitMQ

Redis

ScaleAdapter

```

---

# 12. 数据库设计

数据库：

```
mes_weighting

```

---

# 12.1 weight_tasks

```sql
CREATE TABLE weight_tasks
(

id uuid PRIMARY KEY,


task_no varchar(50),


work_order_id uuid,


status int,


created_time timestamp


);

```

---

# 12.2 weight_items

```sql
CREATE TABLE weight_items
(

id uuid PRIMARY KEY,


task_id uuid,


material_id varchar(50),


target_qty numeric(18,3),


actual_qty numeric(18,3),


tolerance numeric(18,3),


status int


);

```

---

# 12.3 weight_records

称量历史

```sql
CREATE TABLE weight_records
(

id uuid PRIMARY KEY,


item_id uuid,


scale_code varchar(50),


weight numeric(18,3),


operator varchar(50),


created_time timestamp


);

```

---

# 12.4 material_batch

物料批次

```sql
CREATE TABLE material_batches
(

id uuid,


material_id varchar(50),


batch_no varchar(50),


qty numeric(18,3)


);

```

---

# 13. API设计

Base:

```
/api/weighting

```

---

## 获取任务

GET

```
/api/weighting/tasks


```

---

## 创建任务

POST

```
/api/weighting/tasks

```

Request:

```json
{

"workOrderId":"WO001"

}

```

---

## 开始称量

POST

```
/tasks/{id}/start

```

---

## 扫描物料

POST

```
/items/{id}/scan-material

```

Request:

```json
{

"barcode":"A001"

}

```

---

## 上传重量

POST

```
/items/{id}/weight

```

Request:

```json
{

"value":10.02,

"scale":"SCALE01"

}

```

---

## 确认

POST

```
/items/{id}/confirm

```

---

# 14. RabbitMQ事件设计

Exchange:

```
industrial.weighting.exchange

```

---

# 14.1 创建称量任务

RoutingKey:

```
weighting.created

```

Payload:

```json
{

"taskId":"",

"workOrderId":"",

"items":10

}

```

消费者：

```
Trace Service

BatchRecord Service


```

---

# 14.2 称量完成

RoutingKey:

```
weighting.completed

```

Payload:

```json
{

"taskId":"",

"material":"A001",

"qty":10

}

```

消费者：

```
WorkOrder

Trace

BatchRecord

```

---

# 15. Redis设计

## 当前任务缓存

```
weighting:task:{id}

```

结构：

```json
{

"status":"Executing",

"operator":"001",

"device":"PDA01"

}

```

---

## PDA当前任务

```
pda:{device}:task

```

---

# 16. 电子秤接口设计

定义统一接口：

```csharp
public interface IScaleAdapter
{

Task<decimal> ReadWeightAsync();


bool Connect();


bool Disconnect();


}

```

---

实现：

```
/Adapters


    Mettler

    Toledo

    Ohaus

    SerialPort

    TCP


```

---

# 17. IoT Collector集成

推荐架构：

```
电子秤

 |

RS232

 |

IoT Collector

 |

RabbitMQ

 |

Weighting Service


```

优势：

* 称量服务不依赖硬件
* 支持多厂家
* 支持边缘计算

---

# 18. PDA端设计

Vue3统一架构：

```
apps

/mobile


pages


 /weighting


    task.vue


    scan.vue


    weighing.vue


```

支持：

* 扫码
* 蓝牙秤
* 摄像头
* 离线缓存

---

# 19. 审计追踪设计

所有关键动作记录：

```
weight_audit_log

```

字段：

```
user

action

before

after

time

device

```

支持：

GMP审计

---

# 20. MVP范围

## 第一阶段

必须实现：

✅ 创建称量任务

✅ PDA执行

✅ 扫码

✅ 重量录入

✅ 容差判断

✅ 称量记录

✅ RabbitMQ事件

---

# 第二阶段

增加：

## 自动称量

```
电子秤自动读取

```

---

## 防错增强

```
库位

批次

有效期

```

---

## GMP能力

```
电子签名

审计

双人复核

```

---

# 21. Codex任务拆分

---

## Task-01

初始化服务

```
feat:init weighting service

```

---

## Task-02

领域模型

实现：

```
WeightTask

WeightItem

StateMachine

```

提交：

```
feat:add weighting domain

```

---

## Task-03

数据库

实现：

```
Tables

Repository

Migration

```

---

## Task-04

任务创建

实现：

```
CreateWeightTask

```

---

## Task-05

PDA API

实现：

```
Scan

Weight

Confirm

```

---

## Task-06

电子秤接口

实现：

```
IScaleAdapter

MockScale

```

---

## Task-07

消息事件

实现：

```
RabbitMQ Publisher

Consumer

```

---

## Task-08

测试

包括：

```
Domain Test

API Test

Integration Test

```

---

# 22. 后续扩展能力

Weighting Service最终演进：

```
Industrial Weighing Platform

```

支持：

* 原料称量
* 配料系统
* 自动投料
* AGV配送
* 智能仓库
* 电子批记录
* GMP生产

成为：

```
MES
 +
WMS
 +
Batch
 +
IoT

核心连接服务

```

---

