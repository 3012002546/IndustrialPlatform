继续输出：

# 18-Trace Service详细设计.md

> Industrial Platform
> 工业数字化执行平台
> Trace Service（生产追溯服务）

版本：v1.0

定位：

工业产品全生命周期追溯核心服务

架构：

DDD
+
Clean Architecture
+
Event Driven
+
Industrial Data Graph
+
Audit Trace

---

# 1. Service定位

## 1.1 服务职责

Trace Service 是 Industrial Platform 的**生产过程数据关联中心**。

负责：

* 产品追溯
* 原材料追溯
* 批次追溯
* 序列号追溯
* 工单关联
* 设备关联
* 人员关联
* 称量记录关联
* 工艺参数关联
* 质量数据关联
* 正向追溯
* 反向追溯

---

Trace 是事件驱动的关系投影，不负责库存余额、库存批次状态或仓储单据过账。库存移动事实以 OperationalData 发布的事件为准。

## 1.2 核心目标

实现：

```
一个产品

可以找到：

用了什么物料

来自哪个批次

谁生产

什么设备

什么时间

什么工艺参数

什么质量结果

```

同时：

```
一个原材料批次

可以找到：

生产了哪些产品

流向哪些客户

影响哪些批次

```

---

# 2. 在Industrial Platform中的位置

整体关系：

```
                  WorkOrder Service

                         |
                         |
                         v

                  Trace Service

                         |
        +-----------+-----------+-----------+

        |           |           |           |

 OperationalData  Weighting   IoT Collector   Quality


        |           |           |           |

 库存批次与移动   称量数据      设备数据      检验数据

```

Trace Service 是：

```
工业数据关系层

Industrial Data Relationship Layer

```

---

# 3. 追溯模型设计

传统MES：

```
工单
 |
产品
 |
结束

```

Industrial Platform：

```
                     Product


                        |

                        |

                  Trace Graph


     +------------------+------------------+

     |                  |                  |


 Material           Equipment          Operator


 Batch              Parameter          Quality


```

---

# 4. Trace核心思想

不是简单日志。

采用：

## Trace Graph

模型：

```
Node

+

Relationship

```

例如：

```
MaterialBatch

      |
      |
 consumed_by

      |

WorkOrder

      |
      |
produced

      |

ProductBatch


```

---

# 5. Domain模型设计

核心Aggregate：

```
TraceRecord Aggregate


        |

        +-- TraceNode


        +-- TraceRelation


        +-- TraceEvent


```

---

# 6. TraceRecord

追溯记录聚合根

```csharp
public class TraceRecord
    : AggregateRoot<Guid>
{


    public string TraceNo
    {
        get;
        private set;
    }


    public TraceType Type
    {
        get;
        private set;
    }


    public List<TraceNode> Nodes
    {
        get;
        private set;
    }


}
```

---

# 7. TraceNode

追溯节点

类型：

```csharp
public enum TraceNodeType
{

Product,

Material,

Batch,

WorkOrder,

Equipment,

Operator,

Parameter,

Quality


}
```

---

示例：

```
Node

{

Id:"MAT001",

Type:"MaterialBatch",

Code:"BATCH20260801"

}

```

---

# 8. TraceRelation

关系模型

```csharp
public class TraceRelation
{

public Guid FromId {get;private set;}


public Guid ToId {get;private set;}


public string RelationType
{
get;
private set;
}


}
```

---

关系：

```
MaterialBatch

    |

Consumed


WorkOrder



Produced


ProductBatch


```

---

# 9. 追溯类型设计

## 9.1 正向追溯

输入：

```
原材料批次

```

查询：

```
生产了哪些产品

```

流程：

```
MaterialBatch

        |

        |

WorkOrder

        |

        |

Product

```

---

## 9.2 反向追溯

输入：

```
产品序列号

```

查询：

```
用了哪些原料

```

流程：

```
Product

 |

WorkOrder

 |

MaterialBatch


```

---

# 10. Trace业务流程

## 10.0 库存批次移动

OperationalData 发送：

```text
material.received
material.issued
material.returned
production.received
inventory.transferred
```

Trace 消费事件并建立库存批次、工单、投入物料和产出批次之间的关系。Trace 只更新追溯图，不回写库存。

---

## 10.1 称量完成

Weighting发送：

```
weighting.completed

```

Trace消费：

```
收到事件

 |

创建Material节点

 |

创建Consumption关系

 |

保存


```

---

## 10.2 工单完成

WorkOrder发送：

```
workorder.completed

```

Trace：

```
创建Product节点

 |

关联WorkOrder

 |

生成生产链


```

---

# 11. Solution结构

目录：

```
src/services


/Trace


    /Industrial.Trace.Api


    /Industrial.Trace.Application


    /Industrial.Trace.Domain


    /Industrial.Trace.Infrastructure


    /Industrial.Trace.Contracts


```

---

# 12. Clean Architecture

## Domain

```
Aggregates

Entities

ValueObjects

DomainEvents

Specifications

```

---

## Application

```
Commands

Queries

Handlers

DTO

Services

```

---

## Infrastructure

```
PostgreSQL

SqlSugar

Redis

RabbitMQ

Search

```

---

# 13. 数据库设计

数据库：

```
mes_trace

```

---

# 13.1 trace_nodes

追溯节点

```sql
CREATE TABLE trace_nodes
(

id uuid PRIMARY KEY,


node_type varchar(50),


node_code varchar(100),


name varchar(200),


created_time timestamptz


);

```

---

# 13.2 trace_relations

关系表

```sql
CREATE TABLE trace_relations
(

id uuid PRIMARY KEY,


from_node uuid,


to_node uuid,


relation_type varchar(50),


created_time timestamptz


);

```

---

# 13.3 trace_events

事件历史

```sql
CREATE TABLE trace_events
(

id uuid,


event_type varchar(50),


source varchar(50),


payload jsonb,


created_time timestamptz


);

```

---

# 13.4 product_trace

产品追溯索引

```sql
CREATE TABLE product_trace
(

product_id varchar(50),


serial_no varchar(100),


trace_node_id uuid


);

```

---

# 14. PostgreSQL优化设计

## JSONB扩展

工业数据变化大：

采用：

```
jsonb

```

例如：

设备参数：

```json
{

"temperature":35.5,

"speed":100,

"pressure":2.5

}

```

---

## 索引

节点：

```sql
CREATE INDEX idx_trace_code

ON trace_nodes(node_code);

```

关系：

```sql
CREATE INDEX idx_trace_relation

ON trace_relations(from_node,to_node);

```

---

# 15. API设计

Base:

```
/api/trace

```

---

# 15.1 查询产品追溯

GET

```
/api/trace/product/{sn}

```

返回：

```json
{

"product":"P001",


"materials":[

{

"name":"MaterialA",

"batch":"B001"

}

],


"equipment":[

"LINE01"

]

}

```

---

# 15.2 查询批次去向

GET

```
/api/trace/material/{batch}

```

---

# 15.3 Trace Graph

GET

```
/api/trace/graph/{id}

```

返回：

```json
{

nodes:[],

edges:[]

}

```

用于：

ECharts Graph

---

# 16. RabbitMQ事件设计

Exchange：

```
industrial.trace.exchange

```

---

# 16.1 Material Consumption

RoutingKey:

```
trace.material.consumed

```

Payload:

```json
{

materialBatch:"B001",

workOrder:"WO001",

qty:100

}

```

---

# 16.2 Product Created

RoutingKey:

```
trace.product.created

```

---

# 16.3 Device Parameter

RoutingKey:

```
trace.device.parameter

```

来源：

IoT Collector

---

# 17. Redis设计

## 热门追溯缓存

Key：

```
trace:product:{sn}

```

Value：

```json
{

nodes:[],

relations:[]

}

```

---

## 批次缓存

```
trace:batch:{batch}

```

---

# 18. 与其他服务集成

## OperationalData

获取：

```text
库存批次创建与状态变化

收料、领料、退料和生产入库事实

仓库、库位和容器移动事实
```

---

## WorkOrder

获取：

```
生产关系

```

---

## Weighting

获取：

```
物料消耗

批次

称量结果

```

---

## IoT Collector

获取：

```
设备参数

运行状态

```

---

## Quality

获取：

```
检验结果

合格状态

```

---

# 19. 前端设计

Vue3页面：

```
trace


├── ProductTrace.vue

├── BatchTrace.vue

├── TraceGraph.vue

└── Timeline.vue


```

---

使用：

```
ECharts Graph

```

展示：

```
       原料A

        |

        |

     工单001

      /   \

设备01   人员01

        |

        |

      产品001


```

---

# 20. MVP范围

第一阶段：

实现：

✅ 产品追溯

✅ 批次追溯

✅ 工单关联

✅ 称量关联

✅ 基础Graph查询

✅ RabbitMQ事件

---

# 21. 第二阶段扩展

## 高级追溯

增加：

```
供应链追溯

供应商批次

物流

客户

```

---

## GMP追溯

增加：

```
电子签名

审计日志

批记录关联


```

---

## 数字孪生

结合：

```
IoT数据

Trace Graph


```

形成：

```
Digital Twin Trace

```

---

# 22. Codex任务拆分

---

## Task-01

初始化服务

提交：

```
feat:init trace service

```

---

## Task-02

领域模型

实现：

```
TraceNode

TraceRelation

TraceAggregate

```

提交：

```
feat:add trace domain

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

提交：

```
feat:add trace persistence

```

---

## Task-04

追溯查询

实现：

```
ProductTraceQuery

BatchTraceQuery

GraphQuery


```

提交：

```
feat:add trace query

```

---

## Task-05

RabbitMQ消费者

实现：

```
WorkOrderConsumer

WeightingConsumer

IoTConsumer


```

提交：

```
feat:add trace events

```

---

## Task-06

前端追溯页面

实现：

```
Graph

Timeline

Search


```

提交：

```
feat:add trace ui

```

---

## Task-07

测试

包含：

```
Domain Test

Integration Test

Graph Query Test

```

---

# 23. 后续演进能力

Trace Service最终成为：

```
Industrial Trace Platform

```

能力：

```
产品生命周期追踪

+

物料流转追踪

+

设备过程追踪

+

质量追踪

+

法规合规


```

支撑：

* 医药GMP
* 食品安全
* 新能源电池
* 汽车制造
* 半导体

---
