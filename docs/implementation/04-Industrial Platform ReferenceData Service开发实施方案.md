# 04-Industrial Platform ReferenceData Service开发实施方案

# Industrial Platform ReferenceData Service开发实施方案

> 当前里程碑范围：仅创建项目骨架；业务实现留待后续阶段。

版本：V1.0
阶段：Development Implementation Phase

服务：

ReferenceData Service

定位：

工业数字化平台元数据与动态配置服务

技术：

.NET 10 WebAPI

DDD

Clean Architecture

SqlSugar

PostgreSQL

Redis

RabbitMQ

---

# 1. 服务说明

## 1.1 服务定位

ReferenceData Service 是 Industrial Platform 平台级基础服务。

位于：

```text
Identity Service

        ↓

ReferenceData Service

        ↓

MasterData Service
```

它不是简单的数据字典服务。

核心定位：

> 为工业数字化平台提供统一参考数据、动态模型、参数配置和元数据管理能力。

---

# 1.2 为什么需要ReferenceData

传统MES系统：

```text
业务模块

↓

固定数据库字段

↓

固定页面
```

问题：

* 不同行业字段差异大
* 不同客户配置不同
* 需求变化频繁
* 二次开发成本高

Industrial Platform采用：

```text
基础模型

+

动态配置

+

扩展属性

+

元数据驱动
```

---

# 2. 服务职责

ReferenceData Service负责：

## 2.1 字典中心

Dictionary Center

例如：

设备状态：

```text
Running

Stopped

Alarm

Maintenance
```

---

## 2.2 参数配置中心

System Configuration

例如：

```text
MES.AutoDispatch.Enable=true

Weighting.DoubleCheck=true

Trace.Enable=true
```

---

## 2.3 EAV动态模型

Entity-Attribute-Value

支持：

动态属性。

例如：

设备：

固定：

```text
EquipmentCode

EquipmentName

EquipmentType
```

扩展：

```text
Power

Voltage

Protocol

IP

PLC Model
```

---

## 2.4 元数据模型

用于：

低代码平台。

包含：

* 实体定义
* 属性定义
* 页面配置
* 表单配置

---

## 2.5 编码规则

例如：

物料编码：

```text
RM-{yyyy}-{00000}
```

设备编码：

```text
EQ-{Factory}-{0000}
```

---

# 3. 服务边界

## ReferenceData负责

```text
通用能力

↓

动态配置

↓

元数据
```

---

## ReferenceData不负责

业务数据：

例如：

设备：

属于：

```text
MasterData Service
```

工单：

属于：

```text
WorkOrder Service
```

---

# 4. 项目结构

目录：

```text
src/Services/ReferenceData
```

结构：

```text
ReferenceData


├── ReferenceData.Api


├── ReferenceData.Application


├── ReferenceData.Domain


└── ReferenceData.Infrastructure
```

---

# 5. 创建项目

进入：

```bash
cd src/Services
```

创建：

```bash
dotnet new webapi \
-n ReferenceData.Api
```

创建：

```bash
dotnet new classlib \
-n ReferenceData.Application
```

创建：

```bash
dotnet new classlib \
-n ReferenceData.Domain
```

创建：

```bash
dotnet new classlib \
-n ReferenceData.Infrastructure
```

---

# 6. 项目依赖关系

```text
ReferenceData.Api

↓

ReferenceData.Application

↓

ReferenceData.Domain


ReferenceData.Infrastructure

↓

ReferenceData.Domain
```

---

# 7. Domain领域设计

目录：

```text
ReferenceData.Domain


├── Dictionary

├── Configuration

├── Metadata

├── EAV

├── CodingRule

└── Events
```

---

# 8. Dictionary字典模型

## 8.1 字典类型

例如：

```text
EquipmentStatus

MaterialType

Unit

QualityLevel
```

模型：

```text
DictionaryType

        ↓

DictionaryItem
```

---

# 9. DictionaryType实体

表：

```text
ref_dictionary_type
```

字段：

```text
Id

Code

Name

Description

Status

CreateTime
```

---

# 10. DictionaryItem实体

表：

```text
ref_dictionary_item
```

字段：

```text
Id

DictionaryTypeId

Code

Name

Sort

Enabled
```

---

# 11. 参数配置模型

配置模型：

```text
Configuration


Key

Value

Scope
```

---

# 12. Configuration实体

表：

```text
ref_configuration
```

字段：

```text
Id

Key

Value

DataType

Scope

Description
```

---

# 13. 配置范围设计

支持：

## 平台级

```text
Platform
```

## 租户级

```text
Tenant
```

## 工厂级

```text
Factory
```

例如：

```text
Weighting.RequireDoubleCheck

Factory-A=true

Factory-B=false
```

---

# 14. EAV模型设计

核心：

Entity

↓

Attribute

↓

Value

---

# 15. EntityDefinition

定义动态实体。

例如：

```text
Equipment

Material

Container
```

表：

```text
ref_entity_definition
```

字段：

```text
Id

Code

Name

Description
```

---

# 16. AttributeDefinition

定义属性。

例如：

设备属性：

```text
Power

Voltage

Manufacturer
```

表：

```text
ref_attribute_definition
```

字段：

```text
Id

EntityId

Code

Name

DataType

Required
```

---

# 17. Attribute类型

支持：

```text
String

Integer

Decimal

Boolean

Date

Enum

Reference
```

---

# 18. EntityAttributeValue

保存动态值。

表：

```text
ref_entity_attribute_value
```

字段：

```text
Id

EntityId

AttributeId

Value
```

---

# 19. EAV使用示例

设备：

MasterData：

```json
{
 "id":10001,
 "code":"MIX001",
 "name":"Mixer"
}
```

动态属性：

```json
[
 {
  "attribute":"Power",
  "value":"15KW"
 },
 {
  "attribute":"Material",
  "value":"316L"
 }
]
```

---

# 20. 编码规则模型

目标：

自动生成编码。

例如：

物料：

```text
RM-2026-00001
```

---

# 21. CodingRule实体

表：

```text
ref_coding_rule
```

字段：

```text
Id

Code

Name

Template

CurrentNumber
```

---

# 22. 编码模板

示例：

```text
RM-{YEAR}-{SEQ}
```

变量：

```text
YEAR

MONTH

FACTORY

SEQ
```

---

# 23. Metadata模型

为低代码提供基础。

包含：

```text
Entity

Field

Form

Page
```

---

# 24. 数据库设计

数据库：

```text
industrial_reference
```

---

# 25. 数据表列表

```text
ref_dictionary_type

ref_dictionary_item

ref_configuration

ref_entity_definition

ref_attribute_definition

ref_entity_attribute_value

ref_coding_rule

ref_metadata_form

ref_metadata_field
```

---

# 26. Application层设计

目录：

```text
ReferenceData.Application


├── Dictionary

├── Configuration

├── Metadata

├── EAV

└── Coding
```

---

# 27. Command设计

## 创建字典

```text
CreateDictionaryCommand
```

---

## 新增配置

```text
CreateConfigurationCommand
```

---

## 创建动态属性

```text
CreateAttributeDefinitionCommand
```

---

## 创建编码规则

```text
CreateCodingRuleCommand
```

---

# 28. Query设计

查询字典：

```text
GetDictionaryQuery
```

查询配置：

```text
GetConfigurationQuery
```

查询属性模板：

```text
GetEntityMetadataQuery
```

---

# 29. API设计

Base：

```text
/api/reference
```

---

# 30. Dictionary API

获取字典：

```http
GET

/api/reference/dictionaries/{code}
```

返回：

```json
{
 "code":"EquipmentStatus",
 "items":[]
}
```

---

# 31. Configuration API

查询：

```http
GET

/api/reference/configurations/{key}
```

---

# 32. Metadata API

获取实体模型：

```http
GET

/api/reference/metadata/{entity}
```

例如：

```text
/api/reference/metadata/equipment
```

返回：

```json
{
 "fields":[
  {
   "name":"Power",
   "type":"String"
  }
 ]
}
```

---

# 33. Redis设计

缓存：

## 字典缓存

Key：

```text
reference:dictionary:{code}
```

---

## 参数缓存

Key：

```text
reference:config:{key}
```

---

## 元数据缓存

Key：

```text
reference:metadata:{entity}
```

---

# 34. RabbitMQ事件

发布：

## 配置变化

```text
ConfigurationChangedEvent
```

---

## 元数据变化

```text
MetadataChangedEvent
```

---

# 35. 与MasterData关系

示例：

设备模型。

ReferenceData：

定义：

```text
Equipment属性模板
```

MasterData：

保存：

```text
Equipment实体
```

关系：

```text
ReferenceData

定义规则


        ↓


MasterData

存储业务数据
```

---

# 36. 前端页面规划

模块：

```text
reference


├── 字典管理

├── 参数配置

├── 动态模型

├── 编码规则

└── 元数据管理
```

---

# 37. 单元测试

目录：

```text
tests/ReferenceData/IndustrialPlatform.ReferenceData.Tests
```

测试：

* Dictionary规则
* Configuration规则
* EAV规则
* CodingRule规则

---

# 38. 开发任务拆分

## Task-001 创建工程

内容：

* 创建四层项目
* 添加引用

验收：

Build成功。

---

## Task-002 字典中心

实现：

* DictionaryType
* DictionaryItem

验收：

字典CRUD完成。

---

## Task-003 参数中心

实现：

* Configuration

验收：

配置读取成功。

---

## Task-004 EAV模型

实现：

* EntityDefinition
* AttributeDefinition
* Value

验收：

动态属性保存读取成功。

---

## Task-005 编码规则

实现：

* Template解析
* Sequence生成

验收：

自动编号成功。

---

## Task-006 Metadata

实现：

动态字段模型。

---

## Task-007 Redis缓存

实现：

字典、配置、元数据缓存。

---

## Task-008 RabbitMQ事件

实现：

配置变更通知。

---

# 39. ReferenceData Service完成标准

完成后：

Industrial Platform具备：

```text
统一字典

↓

统一配置

↓

动态属性

↓

元数据模型

↓

编码规则
```

支撑：

* MasterData Service
* 低代码平台
* 报表平台
* SaaS多租户
* AI工业助手

---
