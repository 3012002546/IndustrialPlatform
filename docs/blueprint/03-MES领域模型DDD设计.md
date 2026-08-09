# MES领域模型DDD设计

> 文档版本：V1.0
> 文档类型：领域驱动设计文档（DDD）
> 适用范围：Industrial Platform 制造执行域
> 架构模式：DDD + 微服务 + 事件驱动
> 核心目标：建立可扩展、可配置、跨行业 MES 领域模型

---

# 1. 设计目标

## 1.1 为什么采用DDD

传统MES开发模式：

```text
菜单

↓

页面

↓

表

↓

CRUD接口
```

容易导致：

* 模块边界混乱
* 业务逻辑分散
* 二开困难
* 行业复制困难

本平台采用：

```text
业务领域

↓

领域模型

↓

聚合

↓

服务

↓

数据库
```

---

# 2. MES领域总体划分

MES制造域：

```text
Manufacturing Domain


├── Master Data Domain
│
├── Planning Domain
│
├── Operational Data Domain
│
├── Production Domain
│
├── Workflow Domain
│
├── Weighting Domain
│
├── Equipment Domain
│
├── Quality Domain
│
├── Trace Domain
│
└── Batch Domain

```

---

# 3. 核心领域模型关系

整体业务链：

```text
客户订单

    |
    ↓

生产计划

    |
    ↓

生产工单

    |
    ↓

工艺路线

    |
    ↓

生产任务

    |
    ↓

物料批次

    |
    ↓

称量/投料

    |
    ↓

生产执行

    |
    ↓

质量检验

    |
    ↓

产品批次

    |
    ↓

追溯

```

---

# 4. 基础领域（Master Data）

## 4.1 Factory（工厂模型）

制造系统根模型。

结构：

```text
Factory

 |
 ├── Workshop

 |
 ├── ProductionLine

 |
 ├── Station

 |
 └── Equipment

```

---

## Entity

```csharp
Factory
{
    Id;

    Code;

    Name;

    Address;

    Status;

}

```

---

# 4.2 Resource资源模型

统一抽象：

生产资源。

包括：

* 人
* 设备
* 工位
* 工装

模型：

```text
Resource


ResourceType

ResourceGroup

ResourceStatus

```

---

# 5. Planning领域

## 5.1 聚合：ProductionPlan

生产计划。

Aggregate Root：

```text
ProductionPlan

 |
 └── PlanItem

```

---

## 生命周期

状态：

```text
Created

 ↓

Released

 ↓

Executing

 ↓

Completed

 ↓

Closed

```

---

## Domain Event

发布：

```text
ProductionPlanReleased

```

消费者：

* 工单服务
* 排产服务

---

# 6. WorkOrder领域（核心）

## 6.1 聚合

```text
WorkOrder


 |
 ├── Operation

 |
 ├── ExecutionBom

 |
 ├── MaterialRequirement

 |
 └── Task

```

---

# WorkOrder实体

```csharp
WorkOrder
{

    Id;

    OrderNo;

    Product;

    Qty;

    Status;

}

```

---

## 状态机

```text
Created

 |

Released

 |

Dispatched

 |

Running

 |

Completed

 |

Closed

```

---

## 工单事件

完成：

```text
WorkOrderCompleted

```

产生：

* 产品批次
* 消耗记录
* 追溯关系

---

# 7. Operation工序领域

## Operation

表示：

生产步骤。

例如：

```text
产品A


工序10:

装配


工序20:

测试


工序30:

包装

```

---

模型：

```text
Operation

{

Sequence;

Process;

StandardTime;

Resource;

}

```

---

# 8. Material领域

## 8.1 Material实体

基础物料。

```text
Material

 |

Revision

 |

BOM

```

---

## BOM模型

```text
Bom


 |

BomItem


 |

Material

```

---

# 9. Material Runtime领域

现场动态物料。

## 核心概念：

## Lot

批次。

```text
MaterialLot

{

LotNo;

Material;

Qty;

Status;

}

```

---

## Container

容器。

例如：

* 桶
* 箱
* 托盘

```text
Container

{

ContainerNo;

Type;

CurrentLot;

}

```

---

## Package

包装。

```text
Package

{

PackageNo;

Qty;

}

```

---

# 10. Weighting领域模型（重点）

称量独立领域。

---

# 10.1 核心聚合

```text
WeightTask


 |

WeightRecord


 |

MaterialLot


```

---

# WeightTask

称量任务。

```csharp
WeightTask
{

TaskNo;

WorkOrderId;

MaterialId;

TargetWeight;

Tolerance;

Status;

}

```

---

# 状态机

```text
Created

 |

Waiting

 |

Weighing

 |

Completed

 |

Released

```

---

# WeightRecord

实际记录。

```csharp
WeightRecord
{

RecordNo;

MaterialLot;

GrossWeight;

NetWeight;

Scale;

Operator;

Time;

}

```

---

# 领域事件

完成：

```text
WeightCompletedEvent

```

消费者：

* 批记录
* 追溯
* 工单

---

# 11. Equipment领域

## Equipment Aggregate

```text
Equipment


 |

EquipmentParameter


 |

EquipmentState


```

---

状态：

```text
Idle

Running

Alarm

Maintenance

Offline

```

---

事件：

```text
EquipmentAlarmEvent

EquipmentStatusChanged

```

---

# 12. IoT数据领域

设备实时数据。

不要直接进入业务库。

模型：

```text
Device


 |

Point


 |

Telemetry


```

---

例如：

```text
PLC001


Temperature


80℃

```

---

# 13. Workflow领域

流程执行。

模型：

```text
ProcessDefinition


 |

ProcessInstance


 |

TaskInstance

```

---

例如：

```text
工单启动


↓

扫码


↓

称量


↓

检验


↓

完成

```

---

# 14. Quality领域

建议独立。

## Inspection

检验任务。

```text
InspectionOrder


 |

InspectionItem


 |

Result

```

---

支持：

* IQC
* IPQC
* OQC
* 首检

---

# 15. Trace领域

追溯核心。

采用：

Graph模型思想。

---

关系：

```text
ProductLot


   ↑


MaterialLot


   ↑


WeightRecord


   ↑


Equipment


   ↑


Operator

```

---

Trace Event:

```text
MaterialConsumed

ProductProduced

ProcessCompleted

```

---

# 16. Batch Record领域

电子批记录。

聚合：

```text
BatchRecord


 |

BatchStep


 |

ExecutionRecord

```

---

记录：

```text
Who

When

Where

What

Result

```

---

# 17. Domain Event设计

统一事件格式：

```json
{

"EventId":

"AggregateId":

"EventType":

"OccurredTime":

"Data":

}

```

---

事件示例：

## 工单完成

```json
{

"EventType":

"WorkOrderCompleted",

"Order":

"WO001"

}

```

---

# 18. 微服务边界

最终：

```text
Planning.Service


WorkOrder.Service


OperationalData.Service


Weighting.Service


Equipment.Service


Workflow.Service


Quality.Service


Trace.Service


Batch.Service

```

---

# 19. 服务之间关系

原则：

禁止：

```text
Service A

直接访问

Service B 数据库

```

---

正确：

```text
WorkOrder

 |

Event

 |

Trace

```

---

# 20. 数据一致性设计

采用：

最终一致。

例如：

工单完成：

事务：

```text
WorkOrder DB

保存完成状态


发布事件


RabbitMQ


Trace消费


```

---

# 21. 状态机统一设计

MES大量业务都有状态。

抽象：

```csharp
IStateMachine

{

CurrentState;

Transition();

Validate();

}

```

支持：

* 工单
* 批次
* 称量
* 设备

---

# 22. 审计设计

所有Aggregate支持：

```csharp
IAuditableEntity

{

CreatedBy;

CreatedTime;

ModifiedBy;

ModifiedTime;

}

```

---

# 23. 归档设计

生产数据生命周期：

```text
生产执行

 |

实时库

 |

历史库

 |

归档库

```

---

# 24. 核心设计总结

MES核心不是页面。

核心是：

```text
对象模型


+

状态变化


+

事件流


+

追溯链

```

最终形成：

```text
Factory

↓

Resource

↓

Order

↓

Task

↓

Material

↓

Weight

↓

Equipment

↓

Quality

↓

Trace

↓

Batch Record

```

这套领域模型可以支撑：

* 电子MES
* 汽车MES
* 新能源MES
* 医药MES
* 食品MES
