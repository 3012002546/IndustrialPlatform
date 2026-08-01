# 10-Codex协作开发规范

版本：v1.0
项目名称：Industrial Platform
适用对象：

* Codex
* 开发工程师
* 架构维护者

目标：

> 让 AI 成为长期参与 Industrial Platform 建设的开发伙伴，而不是一次性代码生成工具。

---

# 1. Codex在项目中的定位

Industrial Platform 是一个长期产品。

Codex 不应该被当作：

```
需求
 ↓
一句Prompt
 ↓
生成代码
 ↓
结束
```

而应该：

```
领域文档

↓

架构设计

↓

任务拆解

↓

代码实现

↓

测试

↓

Review

↓

提交

↓

持续演进

```

---

# 2. Codex工作模式

推荐：

## 文档驱动开发（Document Driven Development）

项目核心：

```
/docs

    架构设计

    领域模型

    数据库规范

    API规范

    任务列表

        ↓


Codex

        ↓


/src代码

```

---

# 3. Workspace规划

推荐：

创建一个根Workspace：

```
Industrial.Platform.code-workspace
```

包含：

```
Industrial.Platform


├── backend

├── frontend

├── agents

├── docs

├── docker

├── tests

└── scripts

```

---

# 4. Codex读取顺序规范

每次开始开发：

必须先读取：

```
1. README.md


2. docs/00-Architecture


3. docs/02-Service


4. docs/03-Database


5. 当前Task文档


6. 当前代码

```

禁止：

直接看一个文件开始修改。

---

# 5. 项目核心上下文文件

根目录增加：

```
AI_CONTEXT.md
```

作用：

告诉Codex：

项目是什么。

---

示例：

```markdown
# Industrial Platform


这是一个工业数字化执行平台。


技术：

.NET10

DDD

Microservice

PostgreSQL

RabbitMQ


核心领域：

MES

IoT

Trace

Weighting


开发原则：

DDD优先

接口隔离

事件驱动

禁止业务耦合

```

---

# 6. 每个服务必须有AI说明文件

例如：

```
backend/services/WorkOrder.Service

```

增加：

```
AI_CONTEXT.md
```

内容：

```markdown
# WorkOrder Service


职责：

生产工单管理。


负责：

创建工单

释放工单

执行状态


不负责：

设备

库存

质量


依赖：

MasterData API


发布事件：

WorkOrderCreatedEvent


```

---

# 7. Codex任务规范

所有开发：

基于Task。

目录：

```
docs/tasks


├── completed

├── doing

└── todo

```

---

Task格式：

```markdown
# TASK-WO-001


## 标题

创建WorkOrder领域模型


## 背景

实现生产工单核心领域


## 输入文档

docs/domain/workorder.md


## 修改范围


backend/services/WorkOrder.Service


## 输出


WorkOrder Aggregate


## 验收标准


Build成功

UnitTest通过


## Git提交


feat(workorder): add aggregate

```

---

# 8. Prompt模板

## 新功能开发

推荐：

```
你现在作为Industrial Platform架构工程师。


请先阅读：

/docs/AI_CONTEXT.md

/docs/00-Architecture

/docs/当前服务设计


任务：

TASK-XXX


要求：

1. 遵循DDD

2. 遵循Clean Architecture

3. 不修改无关模块

4. 先输出设计方案

5. 等确认后编码


```

---

# 9. 禁止直接生成代码

错误方式：

```
帮我写WorkOrder模块
```

结果：

AI容易：

* 创建Controller
* 创建Service
* 创建Repository
* 没有领域模型

---

正确：

阶段1：

```
设计领域模型
```

阶段2：

```
设计数据库
```

阶段3：

```
实现Domain
```

阶段4：

```
实现Application
```

阶段5：

```
实现API
```

---

# 10. DDD开发流程

每个业务：

严格：

```
业务需求

↓

Domain Model

↓

Aggregate

↓

Entity

↓

ValueObject

↓

Domain Event

↓

Application Service

↓

Repository

↓

API

```

---

例如：

称量：

不是：

```
WeightController

↓

Insert数据库

```

而是：

```
WeightTask

Aggregate

↓

CompleteWeight()

↓

WeightCompletedEvent

↓

RabbitMQ

↓

Trace

```

---

# 11. AI代码生成规则

## Rule 1

禁止胖Controller。

错误：

```csharp
public IActionResult Create()
{

验证

计算

保存

发送消息

}

```

---

正确：

```csharp
Controller

↓

Command

↓

Handler

↓

Domain

```

---

# Rule 2

禁止直接访问数据库。

错误：

```csharp
db.Queryable<Order>()
```

必须：

```csharp
IWorkOrderRepository
```

---

# Rule 3

公共代码进入BuildingBlocks。

例如：

错误：

```
WorkOrder.Service

Result.cs

```

正确：

```
Industrial.Core

Result.cs

```

---

# 12. Git协作规范

分支：

```
main

    产品稳定版本


develop

    集成开发


feature/*

    功能开发


hotfix/*

    紧急修复

```

---

示例：

```
feature/workorder-create

```

---

# 13. Commit规范

使用：

Conventional Commit

格式：

```
type(scope): message

```

---

类型：

| 类型       | 说明 |
| -------- | -- |
| feat     | 新增 |
| fix      | 修复 |
| refactor | 重构 |
| docs     | 文档 |
| test     | 测试 |
| perf     | 性能 |
| chore    | 维护 |

---

示例：

```
feat(weighting): add weight task domain


fix(trace): fix lot relation


docs(event): update rabbitmq spec

```

---

# 14. Code Review规范

每个Task完成：

必须检查：

## 架构

```
是否违反DDD？

是否跨服务引用？

是否业务耦合？

```

---

## 代码

检查：

```
命名

异常处理

日志

事务

性能

```

---

## 数据库

检查：

```
索引

字段类型

归档策略

```

---

# 15. AI生成代码验收流程

流程：

```
Codex生成


↓

Build


↓

Unit Test


↓

人工Review


↓

修改


↓

Commit


```

---

禁止：

```
AI生成

↓

直接提交

```

---

# 16. 日志规范

统一：

Serilog。

结构化日志。

例如：

```csharp
_logger.LogInformation(
"Create WorkOrder {OrderNo}",
orderNo);

```

---

禁止：

```csharp
Console.WriteLine()

```

---

# 17. 异常规范

统一：

```
IndustrialException

```

分类：

```
BusinessException

ValidationException

SystemException

```

---

例如：

业务：

```csharp
throw new BusinessException(
"库存不足");

```

---

# 18. API规范

统一：

REST。

例如：

创建：

```
POST /api/workorders
```

查询：

```
GET /api/workorders/{id}

```

分页：

```
GET /api/workorders?page=1&pageSize=20

```

---

返回：

统一：

```json
{
 "success":true,

 "data":{},

 "message":""

}

```

---

# 19. 数据库变更规范

禁止：

直接修改生产数据库。

流程：

```
Migration


↓

测试


↓

Review


↓

发布

```

---

目录：

```
database


├── migration

├── rollback

└── seed

```

---

# 20. 前端Codex规范

Vue：

必须：

```
Composition API


<script setup>

TypeScript


```

---

目录：

```
views

components

hooks

stores

api

```

---

禁止：

页面：

```
1000行.vue

```

---

拆：

```
页面

↓

组件

↓

Hook

↓

API

```

---

# 21. PDA/Mobile开发规范

三端统一：

```
PC

PDA

Mobile

```

共享：

```
packages


ui

hooks

api

```

---

业务：

响应式。

例如：

```css
desktop

tablet

mobile

```

---

# 22. Agent开发规范

设备Agent：

必须：

```
采集

↓

转换

↓

发送

```

禁止：

Agent直接写业务数据库。

---

例如：

错误：

```
PLC Agent

↓

insert MES数据库

```

正确：

```
PLC Agent

↓

RabbitMQ

↓

IoT Collector

↓

数据库

```

---

# 23. AI辅助架构演进

每次重大修改：

更新：

```
docs

```

例如：

新增服务：

必须增加：

```
docs/services/NewService.md

```

新增事件：

增加：

```
docs/events/Event.md

```

---

# 24. Codex长期使用建议

## 第一阶段

让Codex：

```
生成骨架

生成CRUD

生成测试

生成文档

```

---

## 第二阶段

让Codex：

```
领域建模

代码Review

性能优化

重构

```

---

## 第三阶段

让Codex：

```
架构分析

自动生成任务

自动维护文档

```

---

# 25. 推荐目录增加

最终：

```
Industrial.Platform


├── AI_CONTEXT.md


├── .codex


├── .github


├── docs


├── backend


├── frontend


├── agents


└── tests

```

---

# 26. .codex目录设计

增加：

```
.codex


├── instructions.md

├── architecture.md

├── coding-style.md

├── prompts

│
├── feature.md

├── bugfix.md

└── review.md

```

---

# 27. 最终Codex工作流

完整流程：

```
需求

↓

写文档

↓

拆Task

↓

Codex读取Context

↓

设计

↓

实现

↓

测试

↓

Review

↓

Commit

↓

更新文档


```

---

# 28. Industrial Platform最终工程体系

经过前面10篇设计：

你的平台已经形成：

```
Industrial Platform


                Docs

                 |

                 ↓


Architecture

                 |

                 ↓


DDD Microservices

                 |

                 ↓


.NET10 Backend

                 |

                 ↓


Vue3 Frontend

                 |

                 ↓


Industrial Agent

                 |

                 ↓


Docker Deployment

                 |

                 ↓


Codex Development System

```

---

# 
