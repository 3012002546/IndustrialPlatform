# 14-MasterData Service详细设计

> 文档版本：v1.0
> 项目名称：Industrial Platform
> 服务名称：MasterData Service
> 服务定位：工业数字化平台主数据中心
> 技术基础：
>
> * .NET 10 WebAPI
> * Clean Architecture
> * DDD
> * SqlSugar
> * PostgreSQL
> * Redis
> * RabbitMQ
> * Serilog

---

# 1. 服务定位

MasterData Service（主数据服务）是 Industrial Platform 的基础业务服务。

工业系统中：

> 主数据决定业务系统稳定性。

MES、称量、追溯、批记录、LIMS、设备平台全部依赖主数据。

> 边界说明：ReferenceData 管理字典、配置、元数据与编码规则；MasterData 管理物料、设备、组织与 BOM 等业务主数据。MasterData 仅消费必要的 ReferenceData 能力，不承载 ReferenceData 职责。

整体关系：

```text
                    Identity

                       |

                ReferenceData

                       |

                 MasterData

                       |

 ------------------------------------------------

 MES        Weighting       Trace       Batch

                       |

                 IoT Platform

```

---

# 2. MasterData职责边界

## 负责

| 领域    | 说明                |
| ----- | ----------------- |
| 物料    | Material          |
| 物料分类  | Material Category |
| 单位    | Unit              |
| 工厂    | Factory           |
| 车间    | Workshop          |
| 产线    | Production Line   |
| 设备主数据 | Equipment Master  |
| BOM   | Bill Of Material  |
| 工艺路线  | Routing           |
| 工序    | Operation         |
| 资源    | Resource          |
| 版本管理  | Version           |
| 基础字典  | Dictionary        |

---

## 不负责

以下属于其他服务：

### MES

负责：

```
生产订单
生产执行
报工
派工
```

---

### IoT

负责：

```
设备实时数据

采集点

报警
```

---

### Weighting

负责：

```
称量任务

称量过程

称量结果
```

---

### Trace

负责：

```
批次流转

物料追踪

序列号
```

---

# 3. 主数据设计原则

Industrial Platform采用：

```
Master Data
+
Reference Data
+
Transaction Data
```

三层模型。

---

# 3.1 Master Data

长期稳定：

例如：

```
Material

Equipment

BOM

Routing
```

---

# 3.2 Reference Data

辅助：

例如：

```
单位

国家

状态

类型
```

---

# 3.3 Transaction Data

业务产生：

例如：

```
WorkOrder

Batch

WeighingRecord
```

不存储在MasterData。

---

# 4. Solution结构

目录：

```
src/Services/MasterData


IndustrialPlatform.MasterData.Domain


IndustrialPlatform.MasterData.Application


IndustrialPlatform.MasterData.Infrastructure


IndustrialPlatform.MasterData.WebApi


tests

IndustrialPlatform.MasterData.Tests

```

---

# 5. DDD领域划分

MasterData包含多个Bounded Context：

```
MasterData

|
├── Material Context
|
├── Equipment Context
|
├── Organization Context
|
├── Process Context
|
├── BOM Context
|
└── Dictionary Context

```

---

# 6. Material物料领域模型

工业核心对象。

## Material Aggregate

模型：

```
Material

 |
 |-- MaterialId
 |
 |-- Code
 |
 |-- Name
 |
 |-- Type
 |
 |-- Unit
 |
 |-- Status
 |
 |-- Versions

```

---

Entity:

```csharp
public class Material
    : AggregateRoot<Guid>
{

    public string Code {get;private set;}


    public string Name {get;private set;}


    public MaterialType Type {get;private set;}


}
```

---

# 7. Material类型设计

支持：

```
原材料

半成品

成品

包装材料

辅料

耗材

```

枚举：

```csharp
public enum MaterialType
{

 Raw = 1,


 SemiFinished = 2,


 Finished = 3,


 Package = 4

}
```

---

# 8. 物料编码规则

工业现场：

物料编码非常重要。

设计：

```
MaterialCodeRule
```

例如：

```
RM-000001

FG-000001

PK-000001

```

规则：

```
类型

年份

流水号
```

---

# 9. 物料版本管理

制造行业：

物料会变化。

例如：

```
产品A

V1

↓

V2

↓

V3

```

模型：

```
Material

 |

MaterialVersion

```

---

表：

```
md_material


md_material_version

```

---

字段：

```
version_no

effective_date

status

change_reason

```

---

# 10. 单位管理

支持：

```
kg

g

L

ml

pcs

box

```

模型：

```
Unit

```

支持换算：

例如：

```
1 KG

=

1000 G

```

---

# 11. Unit模型

```csharp
public class Unit
{

Guid Id;


string Code;


string Name;


decimal ConversionFactor;


}
```

---

# 12. 工厂组织模型

虽然Identity已有组织。

但是：

MasterData负责制造组织。

区别：

Identity:

```
谁属于哪里
```

MasterData:

```
哪里生产什么
```

---

模型：

```
Factory

 |
 Workshop

 |
 ProductionLine

 |
 WorkCenter

```

---

# 13. Factory模型

```csharp
public class Factory
{

Guid Id;


string Code;


string Name;


string Address;


}
```

---

# 14. Equipment设备主数据

注意：

区别：

IoT：

```
设备运行数据
```

MasterData：

```
设备是什么
```

---

模型：

```
Equipment


 |
 |
 EquipmentType


 |
 Location


 |
 Attributes

```

---

示例：

```
设备:

压片机001


类型:

Tablet Press


位置:

生产一车间

```

---

# 15. Equipment模型

```csharp
public class Equipment
    :AggregateRoot<Guid>
{


string Code;


string Name;


Guid LineId;


Guid TypeId;


}
```

---

# 16. BOM设计

BOM：

Bill Of Material

制造核心。

模型：

```
Product


 |

BOM


 |

BOMItem


```

---

例如：

产品：

```
A产品
```

BOM：

```
原料A 10kg

原料B 5kg

包装C 1个

```

---

# 17. BOM Aggregate

```
Bom


 |

BomItems[]

```

---

代码：

```csharp
public class Bom
:AggregateRoot<Guid>
{


List<BomItem> Items;


}
```

---

# 18. BOM版本管理

支持：

```
BOM V1

BOM V2

```

状态：

```
Draft

Released

Obsolete

```

---

# 19. Routing工艺路线

制造执行依赖。

模型：

```
Routing


 |

Operations

```

---

例如：

产品：

```
药片
```

路线：

```
混合

↓

压片

↓

包装

```

---

# 20. Operation工序模型

```csharp
public class Operation
{

Guid Id;


string Code;


string Name;


int Sequence;


}
```

---

# 21. 工艺版本

模型：

```
RoutingVersion

```

支持：

```
工艺变更

审批

生效时间

```

---

# 22. 基础字典设计

统一：

```
Dictionary

DictionaryItem

```

支持：

```
设备类型

报警等级

批次类型

状态
```

---

# 23. 数据库设计

数据库：

```
industrial_masterdata
```

---

表规划：

```
md_material

md_material_version


md_unit


md_factory


md_workshop


md_line


md_equipment


md_equipment_type


md_bom


md_bom_item


md_routing


md_operation


md_dictionary


```

---

# 24. 数据版本设计规范

所有重要主数据：

增加：

```
version

status

effective_time

```

例如：

```
BOM

V1

Released


V2

Draft

```

---

# 25. 发布机制设计

主数据生命周期：

```
Draft


 |

Review


 |

Released


 |

Expired

```

---

只有：

Released

数据：

才能被MES使用。

---

# 26. Redis缓存设计

高频数据：

缓存。

---

物料：

```
masterdata:material:{id}

```

---

设备：

```
masterdata:equipment:{id}

```

---

BOM：

```
masterdata:bom:{materialId}

```

---

# 27. RabbitMQ事件设计

MasterData发布事件。

---

物料发布：

```
MaterialReleasedIntegrationEvent

```

消息：

```json
{

materialId:"",

version:"V2"

}

```

消费者：

```
MES

Weighting

Trace

```

---

设备新增：

```
EquipmentCreatedEvent

```

消费者：

```
IoT Collector

```

---

# 28. API设计

## Material

查询：

```
GET

/api/materials

```

---

创建：

```
POST

/api/materials

```

---

发布版本：

```
POST

/api/materials/{id}/release

```

---

# 29. BOM API

查询：

```
GET

/api/boms/{materialId}

```

---

创建：

```
POST

/api/boms

```

---

发布：

```
POST

/api/boms/{id}/release

```

---

# 30. 数据权限设计

结合Identity：

```
TenantId


FactoryId


```

例如：

用户：

```
上海工厂生产主管

```

只能看到：

```
上海工厂物料
```

---

# 31. 审计设计

所有修改记录：

```
Material

BOM

Routing

Equipment

```

记录：

```
修改人

修改时间

旧值

新值

原因

```

---

# 32. 外部系统集成

未来支持：

## ERP

同步：

```
物料

BOM

订单
```

---

## LIMS

同步：

```
检验项目

规格

标准
```

---

## PLM

同步：

```
产品

工艺

版本
```

---

# 33. 第一版MVP范围

必须完成：

## 基础

√ 单位

√ 字典

---

## 物料

√ CRUD

√ 分类

√ 版本

---

## 工厂

√ 工厂

√ 车间

√ 产线

---

## 设备

√ 设备主数据

---

## BOM

√ BOM维护

√ 发布

---

## Routing

√ 工艺路线

---

# 34. Codex任务拆分

## Task01

创建Service模板

生成：

```
Domain

Application

Infrastructure

WebApi

```

---

## Task02

Material领域

生成：

```
Entity

Command

Query

Repository

API

```

---

## Task03

BOM领域

实现：

```
BOM Aggregate

Version

Release

```

---

## Task04

Equipment领域

实现：

```
Equipment

Type

Location

```

---

## Task05

RabbitMQ事件

实现：

```
MaterialReleasedEvent

EquipmentCreatedEvent

```

---

# 35. 完成标准

MasterData Service上线后：

```
Identity

    |

ReferenceData

    |

MasterData


    |

MES

Weighting

Trace

```

所有业务拥有统一：

```
物料

设备

组织

工艺

BOM

```

---

# 36. 后续扩展能力

未来增加：

```
低代码平台

↓

动态字段

↓

动态表单

↓

动态主数据模型

```

因此MasterData设计必须支持：

```
Metadata Driven

```

---

