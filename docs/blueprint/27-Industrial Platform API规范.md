# 27-Industrial Platform API规范

> Industrial Platform
> 工业数字化执行平台
> API Design Standard V1.0

---

# 文档说明

本文档定义：

```text
Industrial Platform
```

统一 API 开发规范。

目标：

保证：

* 微服务接口统一
* 前后端协作规范
* Codex自动生成接口代码
* 多语言扩展能力
* 企业级API治理

---

# 1. API总体设计原则

Industrial Platform采用：

```text
REST API
+
Event API
+
Realtime API
+
AI API
```

四类接口。

---

# 2. API架构

```text
                Frontend

 PC
 PDA
 Mobile

                 |

            API Gateway

                 |

 --------------------------------

 Identity

 ReferenceData

 MasterData

 WorkOrder

 IoT

 Trace

 Batch

 AI

```

---

# 3. API版本规范

统一：

```http
/api/v1/
```

例如：

```http
GET

/api/v1/workorders

```

版本升级：

```http
/api/v2/
```

---

# 4. URL设计规范

## 资源命名

使用：

复数名词。

正确：

```http
/workorders
/materials
/equipments
```

错误：

```http
/getWorkOrder
/queryMaterial
```

---

# 5. HTTP Method规范

| Method | 用途    |
| ------ | ----- |
| GET    | 查询    |
| POST   | 新增/执行 |
| PUT    | 完整更新  |
| PATCH  | 部分更新  |
| DELETE | 删除    |

---

# 6. API示例

## 查询工单

```http
GET

/api/v1/workorders/{id}

```

Response:

```json
{
 "success":true,

 "data":
 {
   "id":"xxx",
   "orderNo":"WO20260001",
   "status":"Running"
 }

}
```

---

# 7. 统一返回模型

所有API：

```json
{
 "success":true,

 "code":"200",

 "message":"success",

 "data":{}

}
```

---

# 8. ApiResult定义

C#：

```csharp
public class ApiResult<T>
{

public bool Success {get;set;}

public string Code {get;set;}

public string Message {get;set;}

public T Data {get;set;}

}
```

---

# 9. 分页规范

所有列表查询：

Request：

```http
GET

/api/v1/workorders?pageIndex=1&pageSize=20

```

Response：

```json
{
"items":[],

"total":100,

"pageIndex":1,

"pageSize":20

}
```

---

# 10. 分页模型

```csharp
public class PageResult<T>
{

public List<T> Items {get;set;}

public long Total {get;set;}

public int PageIndex {get;set;}

public int PageSize {get;set;}

}
```

---

# 11. 查询条件规范

统一：

```json
{
 "keyword":"",

 "status":"Running",

 "startTime":"",

 "endTime":""
}
```

---

# 12. DTO设计规范

禁止：

Controller直接返回Entity。

错误：

```csharp
return WorkOrderEntity;
```

正确：

```text
Entity

↓

DTO

↓

Response
```

---

# 13. DTO目录规范

```text
Application

├── DTOs

│
├── Requests

│    CreateWorkOrderRequest.cs

│

├── Responses

│    WorkOrderDto.cs

```

---

# 14. 命令模型

采用CQRS思想。

例如：

创建工单：

```text
CreateWorkOrderCommand
```

---

C#：

```csharp
public class CreateWorkOrderCommand
{

public string ProductId {get;set;}

public decimal Quantity {get;set;}

}
```

---

# 15. Query模型

查询：

```text
GetWorkOrderQuery
```

---

# 16. Controller规范

示例：

```csharp
[ApiController]

[Route("api/v1/workorders")]

public class WorkOrderController:ControllerBase

{


[HttpGet("{id}")]

public async Task<ApiResult<WorkOrderDto>> Get(Guid id)

{


}


}

```

---

# 17. Service调用规范

Controller：

只负责：

```text
参数

认证

返回
```

业务：

Application Service。

结构：

```text
Controller

↓

Application Service

↓

Domain

↓

Repository

```

---

# 18. Identity API设计

服务：

Identity Service

---

## 登录

POST

```http
/api/v1/auth/login
```

Request：

```json
{

"username":"admin",

"password":"123456"

}
```

Response：

```json
{

"token":"xxxxx",

"expires":3600

}
```

---

## 获取当前用户

GET

```http
/api/v1/auth/me

```

返回：

```json
{

"userId":"",

"roles":[],

"permissions":[]

}

```

---

# 19. ReferenceData API

当前里程碑仅提供 `/health`，不增加业务端点。

ReferenceData 的后续 API 边界限定为字典、配置、元数据与编码规则；物料、设备、组织与 BOM 归 MasterData。

---

# 20. MasterData API

## 查询物料

```http
GET

/api/v1/materials

```

---

## 创建物料

POST

```http
/api/v1/materials

```

Request：

```json
{

"code":"MAT001",

"name":"原料A"

}
```

---

# 21. WorkOrder API

## 创建工单

POST

```http
/api/v1/workorders

```

---

## 发布工单

POST

```http
/api/v1/workorders/{id}/release

```

---

## 开始执行

POST

```http
/api/v1/workorders/{id}/start

```

---

## 完成

POST

```http
/api/v1/workorders/{id}/complete

```

---

# 22. Weighting API

## 创建称量任务

POST

```http
/api/v1/weighing/orders

```

---

## 扫码称量

POST

```http
/api/v1/weighing/records

```

Request：

```json
{

"materialBatch":"B001",

"weight":20.5,

"device":"Scale01"

}

```

---

# 23. IoT API

## 查询设备实时数据

GET

```http
/api/v1/iot/devices/{id}/metrics

```

---

## 查询报警

GET

```http
/api/v1/iot/alarms

```

---

# 24. Trace API

## 查询追溯链

GET

```http
/api/v1/trace/{batchNo}

```

Response：

```json
{

"batch":"B001",

"nodes":[],

"relations":[]

}
```

---

# 25. Batch Record API

## 创建批记录

POST

```http
/api/v1/batch-records

```

---

## 执行步骤

POST

```http
/api/v1/batch-records/{id}/steps

```

---

# 26. Industrial Data API

## 指标查询

GET

```http
/api/v1/metrics/oee

```

Response：

```json
{

"value":86.5,

"unit":"%"

}

```

---

# 27. Low Code API

## 获取页面配置

GET

```http
/api/v1/pages/{code}

```

返回：

```json
{

"layout":{},

"components":[]

}

```

---

# 28. AI Assistant API设计

AI属于特殊接口。

---

# 28.1 AI聊天

POST

```http
/api/v1/ai/chat

```

Request：

```json
{

"agent":"ProductionAgent",

"message":
"为什么今天产量下降?"

}

```

Response：

```json
{

"answer":

"主要原因设备停机45分钟",

"sources":[]

}

```

---

# 28.2 Streaming接口

支持：

SSE。

```http
POST

/api/v1/ai/chat/stream

```

返回：

```text
data:
正在分析...

data:
查询设备数据...

data:
分析完成

```

---

# 28.3 AI Agent执行

POST

```http
/api/v1/ai/agents/run

```

Request:

```json
{

"agent":

"EquipmentAgent",

"task":

"分析设备异常"

}

```

---

# 29. SignalR实时接口

用于：

* 设备状态
* 看板
* AI输出

Hub：

```text
/api/hubs/industrial
```

---

## 推送事件

设备状态：

```json
{

"type":

"equipment.status",

"data":

{}

}

```

---

生产状态：

```json
{

"type":

"workorder.progress"

}

```

---

AI输出：

```json
{

"type":

"ai.message"

}

```

---

# 30. RabbitMQ事件API

## 事件规范

格式：

```json
{

eventId:"",

eventType:"",

timestamp:"",

tenantId:"",

data:{}

}

```

---

# 31. 核心领域事件

## 工单完成

RoutingKey:

```text
workorder.completed

```

Data:

```json
{

"workOrderId":"",

"quantity":100

}

```

消费者：

```text
Trace

Batch

AI

```

---

## 设备报警

RoutingKey:

```text
equipment.alarm

```

消费者：

```text
AI Agent

Maintenance

Dashboard

```

---

# 32. JWT规范

Header：

```http
Authorization:

Bearer token

```

Payload：

```json
{

userId:"",

tenantId:"",

roles":[]

}

```

---

# 33. Tenant隔离规范

所有请求：

必须包含：

```text
TenantId
```

来源：

JWT。

禁止：

客户端传TenantId。

---

# 34. 权限控制

采用：

RBAC + 数据权限。

示例：

```text
workorder.create

workorder.approve

trace.query

```

---

# 35. API日志

所有请求记录：

```text
RequestId

User

Tenant

API

耗时

结果
```

---

# 36. API异常规范

统一异常：

```json
{

"success":false,

"code":

"WO_001",

"message":

"工单状态错误"

}

```

---

# 37. 错误码体系

格式：

```text
模块_编号
```

例如：

```text
WO_001

MT_001

AI_001

```

---

# 38. OpenAPI规范

每个服务必须生成：

```text
/swagger

```

包含：

* API说明
* Request
* Response
* 示例

---

# 39. API安全规范

必须：

* HTTPS
* JWT
* 参数验证
* 防SQL注入
* 限流
* 审计

---

# 40. Codex开发任务拆分

## Task01

创建API基础框架

生成：

```text
ApiResult

ExceptionMiddleware

Swagger

JWT

```

---

## Task02

生成Identity API

包括：

```text
Login

Token

Permission

```

---

## Task03

生成 ReferenceData API

包括：

```text
Dictionary

Configuration

Metadata

CodingRule
```

---

## Task04

生成MES API

包括：

```text
MasterData

WorkOrder

Weighting

Trace

Batch
```

---

## Task04

生成工业数据API

包括：

```text
IoT

Metrics

Dashboard
```

---

## Task05

生成AI API

包括：

```text
Chat

Agent

RAG

Streaming
```

---

# 41. 最终API体系

```text
Industrial Platform API


/api/v1

 |

 + auth

 + users

 + materials

 + equipments

 + workorders

 + weighting

 + trace

 + batch

 + iot

 + metrics

 + lowcode

 + ai


 + hubs

 + events

```

---
