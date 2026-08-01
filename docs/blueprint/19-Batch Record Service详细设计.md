# 19-Batch Record Service详细设计

> Industrial Platform
> 工业数字化执行平台
> Batch Record Service（批记录服务）

版本：v1.0
定位：工业批记录管理服务
架构：DDD + Clean Architecture + Microservice + Event Driven

---

# 1. 服务定位

## 1.1 背景

在制药、化工、食品、新能源、精密制造等行业中：

生产过程不仅需要：

* 工单执行
* 物料消耗
* 称量记录
* 设备运行数据
* 质量检测数据

还需要形成：

> 一个完整、不可篡改、可追溯的生产批次执行档案。

传统MES通常通过：

```
生产订单
    |
工单
    |
生产记录
    |
报表
```

形成记录。

但是对于高合规行业：

需要：

```
Batch Record

=
计划
+
工单
+
物料
+
人员
+
设备
+
参数
+
称量
+
过程数据
+
质量结果
+
异常
+
审批
+
电子签名
```

形成完整批生产历史。

---

# 2. Batch Record Service目标

## 核心目标

建设工业数字化平台中的：

> 生产批记录中心

负责：

* 批次生命周期管理
* 批生产记录采集
* 执行过程归档
* 参数快照保存
* 电子批记录生成
* 审计追踪
* 批次审核
* 批放行支持

---

# 3. 服务边界

Batch Record Service 不负责：

| 功能   | 所属服务                     |
| ---- | ------------------------ |
| 物料定义 | MasterData               |
| 生产计划 | Planning                 |
| 工单执行 | WorkOrder                |
| 称量执行 | Weighting                |
| 设备采集 | IoT Collector            |
| 质量检测 | 未来 Quality Service       |
| 用户权限 | Identity                 |
| 数据分析 | Industrial Data Platform |

Batch Record只负责：

```
生产过程结果聚合
+
批记录管理
+
合规归档
```

---

# 4. 服务架构

整体关系：

```
                 Identity
                    |
                    |
              Batch Record
                    |
 ------------------------------------------------
 |              |              |                 |
WorkOrder   Weighting     IoT Collector     Quality
 |
 |
Trace Service
```

事件驱动：

```
WorkOrderCompleted
        |
        ↓

Create Batch Record


WeightCompleted
        |
        ↓

Append Batch Record


EquipmentParameterChanged
        |
        ↓

Record Process Parameter


InspectionCompleted
        |
        ↓

Append Quality Result


BatchReleased
        |
        ↓

Archive Batch
```

---

# 5. Solution结构设计

目录：

```
src/services/BatchRecord


Industrial.BatchRecord.Api

Industrial.BatchRecord.Application

Industrial.BatchRecord.Domain

Industrial.BatchRecord.Infrastructure

Industrial.BatchRecord.Contracts

Industrial.BatchRecord.Worker

```

---

# 6. Clean Architecture设计

## 6.1 Domain

核心领域：

```
Domain

├── Entities

│
├── Aggregates

│
├── ValueObjects

│
├── Events

│
├── Services

│
└── Specifications

```

---

# 7. 核心领域模型

## 7.1 BatchRecord 聚合

核心聚合根：

```
BatchRecord
```

职责：

代表一次生产批次完整生命周期。

属性：

```csharp
public class BatchRecord
{
    public Guid Id {get;set;}

    public string BatchNo {get;set;}

    public Guid WorkOrderId {get;set;}

    public string ProductCode {get;set;}

    public string ProductName {get;set;}

    public BatchStatus Status {get;set;}


    public DateTime? StartTime {get;set;}

    public DateTime? EndTime {get;set;}


    public List<BatchMaterialRecord> Materials {get;set;}

    public List<BatchProcessRecord> Processes {get;set;}

    public List<BatchEquipmentRecord> Equipments {get;set;}

}
```

---

# 8. Batch生命周期状态机

## 状态定义

```
Draft
 |
 |
Created
 |
 |
Running
 |
 |
Completed
 |
 |
Reviewing
 |
 |
Approved
 |
 |
Released
 |
 |
Archived

```

状态说明：

| 状态        | 说明   |
| --------- | ---- |
| Draft     | 草稿   |
| Created   | 批次建立 |
| Running   | 生产中  |
| Completed | 生产完成 |
| Reviewing | 审核   |
| Approved  | 批准   |
| Released  | 放行   |
| Archived  | 归档   |

---

# 状态转换

```csharp
Draft
 -> Created

Created
 -> Running

Running
 -> Completed

Completed
 -> Reviewing

Reviewing
 -> Approved

Approved
 -> Released

Released
 -> Archived

```

禁止：

```
Released
    |
Running
```

---

# 9. 子领域模型

## 9.1 BatchMaterialRecord

物料消耗记录。

```csharp
public class BatchMaterialRecord
{

Guid Id;

Guid BatchId;


string MaterialCode;


decimal PlanQty;


decimal ActualQty;


string Unit;


DateTime ConsumeTime;

}

```

来源：

```
Weighting Service
WorkOrder Service

```

---

# 9.2 BatchProcessRecord

生产过程参数。

例如：

温度：

```
120℃
```

压力：

```
2.5MPa
```

转速：

```
1500rpm
```

模型：

```csharp
public class BatchProcessRecord
{

Guid Id;


Guid BatchId;


string ParameterCode;


decimal Value;


string Unit;


DateTime RecordTime;


}

```

数据来源：

```
IoT Collector
```

---

# 9.3 BatchEquipmentRecord

设备使用记录。

```csharp
public class BatchEquipmentRecord
{

Guid BatchId;


Guid EquipmentId;


DateTime StartTime;


DateTime EndTime;


string Status;

}

```

---

# 9.4 BatchApproval

审核记录。

```csharp
public class BatchApproval
{

Guid BatchId;


string UserId;


string Action;


string Comment;


DateTime Time;


}

```

---

# 10. 数据库设计

数据库：

```
industrial_batch_record

```

---

# 10.1 batch_record

```sql
CREATE TABLE batch_record
(

id uuid PRIMARY KEY,


batch_no varchar(50),


work_order_id uuid,


product_code varchar(50),


product_name varchar(100),


status varchar(30),


start_time timestamp,


end_time timestamp,


created_time timestamp,


created_by varchar(50)

);

```

---

# 10.2 batch_material_record

```sql
CREATE TABLE batch_material_record
(

id uuid PRIMARY KEY,


batch_id uuid,


material_code varchar(50),


plan_qty numeric,


actual_qty numeric,


unit varchar(20),


consume_time timestamp

);

```

---

# 10.3 batch_process_record

```sql
CREATE TABLE batch_process_record
(

id uuid PRIMARY KEY,


batch_id uuid,


parameter_code varchar(50),


parameter_value numeric,


unit varchar(20),


record_time timestamp

);

```

---

# 10.4 batch_approval

```sql
CREATE TABLE batch_approval
(

id uuid PRIMARY KEY,


batch_id uuid,


user_id varchar(50),


action varchar(30),


comment text,


create_time timestamp

);

```

---

# 10.5 batch_audit_log

审计表。

```sql
CREATE TABLE batch_audit_log
(

id uuid,


batch_id uuid,


operation varchar(50),


old_value jsonb,


new_value jsonb,


operator varchar(50),


time timestamp

);

```

---

# 11. Redis设计

## 11.1 当前生产批次缓存

Key:

```
batch:running:{batchNo}

```

Value:

```json
{
 "status":"Running",
 "workOrder":"WO001",
 "product":"P001"
}

```

---

## 11.2 批过程实时缓存

```
batch:process:{batchId}

```

保存：

```
最新参数
设备状态
异常信息

```

---

# 12. RabbitMQ事件设计

Exchange：

```
industrial.batchrecord

```

类型：

topic

---

# 发布事件

## BatchCreated

RoutingKey:

```
batch.created

```

Payload:

```json
{

"batchId":"",

"batchNo":"",

"workOrderId":""

}

```

---

## BatchCompleted

```
batch.completed

```

---

## BatchReleased

```
batch.released

```

---

# 消费事件

---

## WorkOrderCompleted

来源：

WorkOrder Service

动作：

创建BatchRecord

Routing:

```
workorder.completed

```

---

## WeightCompleted

来源：

Weighting

动作：

追加物料记录

---

## EquipmentDataCollected

来源：

IoT Collector

动作：

保存过程参数

---

## QualityResultCompleted

未来：

Quality Service

动作：

追加质量结果

---

# 13. API设计

Base:

```
/api/batch-record

```

---

# 13.1 创建批记录

POST

```
/api/batch-record/create

```

请求：

```json
{

"workOrderId":"",

"batchNo":""

}

```

---

# 13.2 查询批记录

GET

```
/api/batch-record/{id}

```

---

# 13.3 查询批生产过程

GET

```
/api/batch-record/{id}/process

```

---

# 13.4 审核

POST

```
/api/batch-record/{id}/approve

```

---

# 13.5 发布

POST

```
/api/batch-record/{id}/release

```

---

# 14. 前端设计

Vue模块：

```
src/views/batch-record


├── BatchList.vue

├── BatchDetail.vue

├── BatchProcess.vue

├── BatchMaterial.vue

├── BatchApproval.vue

```

---

# PDA场景

现场：

```
批次查询

↓

扫码

↓

查看生产状态

↓

填写异常

↓

提交

```

---

# 15. MES业务流程

完整流程：

```
生产计划

↓

WorkOrder

↓

生产开始

↓

创建Batch Record

↓

称量

↓

物料进入批记录

↓

设备运行

↓

采集参数

↓

生产完成

↓

质量确认

↓

审核

↓

批放行

↓

归档

```

---

# 16. MinIO设计

批记录附件：

```
bucket:

industrial-batch-record


目录:

/year/month/batchNo/

```

保存：

```
批记录PDF

电子签名

附件

图片

扫描文件

```

---

# 17. PDF批记录生成

服务：

```
BatchReport Worker

```

流程：

```
Batch Released

        |

Generate PDF

        |

Upload MinIO

        |

Save URL

```

技术：

推荐：

```
QuestPDF

```

---

# 18. MVP范围

## 第一阶段

必须：

* BatchRecord创建
* 生命周期管理
* 物料记录
* 称量记录集成
* 工艺参数记录
* 查询页面
* 审计日志

---

## 第二阶段

增加：

* PDF批记录
* 审核流程
* 电子签名
* MinIO归档

---

## 第三阶段

增加：

* MES电子批记录规范
* 多工厂
* 多租户
* AI批记录分析

---

# 19. Codex开发任务拆分

## Task-01

创建Service工程

输出：

```
BatchRecord Service Solution

```

---

## Task-02

实现Domain模型

包含：

```
BatchRecord Aggregate

Status Machine

Domain Events

```

---

## Task-03

数据库初始化

生成：

```
PostgreSQL migration

```

---

## Task-04

实现Application

包含：

```
Commands

Queries

DTO

Handlers

```

---

## Task-05

实现RabbitMQ

消费者：

```
WorkOrderCompleted

WeightCompleted

EquipmentDataCollected

```

---

## Task-06

实现API

完成：

```
CRUD

状态流转

查询

```

---

## Task-07

Vue页面

完成：

```
批记录列表

详情

过程追踪

审核

```

---

# 20. 后续扩展能力

## 20.1 电子批记录规范

支持：

```
FDA 21 CFR Part11

EU GMP Annex11

```

---

## 20.2 AI分析

未来：

```
Batch Record

↓

AI Agent

↓

异常分析

↓

工艺优化建议

```

---

## 20.3 数字孪生

结合：

```
IoT Collector

+
Batch Record

+
TimescaleDB


形成：

生产过程数字模型

```

---

# 21. 与整体Industrial Platform关系

最终形成：

```
Identity Service

        |

ReferenceData

        |

MasterData

        |

Planning

        |

WorkOrder

        |

-------------------------

Weighting

IoT Collector

-------------------------

        |

Trace

        |

Batch Record

        |

Industrial Data Platform

        |

AI Assistant

```

---

# 22. 总结

Batch Record Service 是 Industrial Platform 从：

> MES执行系统

升级到：

> 工业数字化平台

的重要服务。

它连接：

```
计划
 ↓
执行
 ↓
数据
 ↓
追溯
 ↓
合规
```

最终形成企业级：

```
Digital Batch Record Platform

```
