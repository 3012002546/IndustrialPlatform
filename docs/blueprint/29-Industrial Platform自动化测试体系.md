# 29-Industrial Platform自动化测试体系

# Industrial Platform 自动化测试体系设计

版本：V1.0
项目：Industrial Platform
定位：工业数字化执行平台

---

# 1. 文档目标

本文档定义 Industrial Platform 的完整自动化测试体系。

目标：

* 支撑工业软件商业化交付
* 支撑微服务长期演进
* 支撑多人协作开发
* 支撑 Codex 自动生成测试代码
* 支撑持续集成 CI/CD
* 保证 MES 核心业务稳定性
* 保证设备数据、批记录、追溯等工业场景可靠性

Industrial Platform 不是简单 CRUD 系统。

测试体系必须覆盖：

```
业务领域
    |
    |
领域模型测试
    |
应用服务测试
    |
API测试
    |
数据库测试
    |
事件测试
    |
设备模拟测试
    |
UI测试
    |
AI能力测试
    |
生产环境验证
```

---

# 2. 测试体系总体架构

## 2.1 测试金字塔

```
                 UI自动化测试
                    ▲
                    |
              SignalR测试
              AI Agent测试
              RAG测试
                    ▲
                    |
              API自动化测试
              集成测试
                    ▲
                    |
          RabbitMQ事件测试
          IoT模拟测试
          数据库测试
                    ▲
                    |
              单元测试
              Domain测试

```

比例建议：

| 类型               |  比例 |
| ---------------- | --: |
| Domain Unit Test | 50% |
| Application Test | 20% |
| API Test         | 15% |
| Integration Test | 10% |
| UI/E2E Test      |  5% |

---

# 3. 测试技术栈设计

## 3.1 Backend测试技术

.NET 10生态：

| 用途    | 技术                    |
| ----- | --------------------- |
| 单元测试  | xUnit                 |
| 断言    | FluentAssertions      |
| Mock  | Moq                   |
| 测试数据  | Bogus                 |
| 数据库测试 | Testcontainers        |
| API测试 | WebApplicationFactory |
| 性能测试  | BenchmarkDotNet       |
| 压力测试  | k6                    |
| 覆盖率   | Coverlet              |
| 报告    | ReportGenerator       |

推荐：

```
xUnit
+
FluentAssertions
+
Moq
+
Testcontainers
+
Docker
```

---

# 4. 测试解决方案结构

最终工程：

```
IndustrialPlatform

├── src

│
├── tests
│
│
├── IndustrialPlatform.UnitTests
│
├── IndustrialPlatform.ApplicationTests
│
├── IndustrialPlatform.IntegrationTests
│
├── IndustrialPlatform.ApiTests
│
├── IndustrialPlatform.EventTests
│
├── IndustrialPlatform.IoTTests
│
├── IndustrialPlatform.AiTests
│
└── IndustrialPlatform.PerformanceTests

```

---

# 5. 微服务测试目录设计

每个Service独立测试。

例如：

WorkOrder Service

```
tests

└── Services

    └── WorkOrder

        ├── Domain.Tests

        ├── Application.Tests

        ├── Api.Tests

        ├── Integration.Tests

        └── Event.Tests

```

对应：

```
src

└── Services

    └── WorkOrder

        ├── Domain

        ├── Application

        ├── Infrastructure

        └── Api

```

---

# 6. DDD测试体系设计

DDD核心：

```
Domain
Application
Infrastructure
API

```

测试边界：

---

# 6.1 Domain层测试

目标：

验证业务规则。

例如：

工单状态：

```
Created

↓

Released

↓

Running

↓

Completed

↓

Closed

```

测试：

```csharp
[Fact]
public void Release_WorkOrder_Should_Success()
{
    var order =
        WorkOrder.Create(
            "WO001");


    order.Release();


    order.Status
        .Should()
        .Be(
        WorkOrderStatus.Released);
}

```

---

覆盖：

## 聚合根

例如：

```
WorkOrder

Batch

MaterialLot

Equipment

Recipe

```

测试：

* 创建规则
* 状态转换
* 领域事件
* 权限限制
* 数据一致性

---

# 6.2 Domain Event测试

例如：

工单完成：

产生：

```
WorkOrderCompletedEvent

```

测试：

```csharp
[Fact]
public void Complete_Should_Create_Event()
{

var order=
new WorkOrder();


order.Complete();


order.DomainEvents
.Should()
.ContainSingle();

}

```

---

# 7. Application测试

测试：

UseCase。

例如：

创建工单：

```
CreateWorkOrderCommand

        |

CreateWorkOrderHandler

        |

Domain

        |

Repository

```

测试：

```csharp
CreateWorkOrderHandlerTests

```

覆盖：

* Command
* Query
* Handler
* Validation
* Transaction

---

# 8. Infrastructure测试

主要：

数据库。

---

# 8.1 PostgreSQL测试

采用：

```
Testcontainers.PostgreSql

```

启动真实数据库。

示例：

```csharp
var container =
new PostgreSqlBuilder()
.Build();


await container.StartAsync();

```

测试：

* Repository
* EF/SqlSugar映射
* SQL正确性
* 索引
* 事务

---

# 8.2 数据库测试范围

## Entity 与通用仓储

必须覆盖：

```text
统一字段默认值
CreatedOn 与 LastUpdatedOn 创建时严格相等
EntityType 为具体派生实体完整类型名
冻结、锁定、软删除和恢复状态机
OptimisticVersion 与 ConcurrencyVersion 推进
默认查询排除软删除记录
软删除使用 UPDATE 而非 DELETE
双版本任一不匹配时拒绝更新、删除和恢复
并发失败不覆盖数据库中的较新记录
```

真实 PostgreSQL 迁移测试还必须验证：

- `id` 主键存在。
- 未自动生成 `(id, is_deleted)` 或 `is_deleted` 单列索引。
- 活跃业务键部分唯一索引拒绝未删除重复记录，并允许软删除后重用编码。
- 只有声明更新时间查询的表存在 `last_updated_on, id` B-tree 或经验证的 BRIN。

---

MES重点：

## MasterData

测试：

```
Material

Equipment

Factory

Process

```

---

## OperationalData

测试：

```text
InventoryBalance
InventoryLot
StockReservation
InventoryDocument
StockTransaction
```

必须覆盖单据状态机、负库存限制、预留核销、并发冲突、过账与冲销、API/事件幂等、Outbox/Inbox，以及 `Internal` / `ExternalWms` 双模式下单一库存权威规则。

---

## WorkOrder

测试：

```
WorkOrder

Operation

Task

Consume

Output

```

---

## Trace

测试：

```
MaterialLot

Genealogy

Track

```

---

# 9. API自动化测试

## 9.1 API测试框架

采用：

```
WebApplicationFactory

+
HttpClient

+
FluentAssertions

```

结构：

```
ApiTests

|
├── AuthTests

├── WorkOrderTests

├── BatchTests

├── WeightingTests

└── TraceTests

```

---

# 9.2 API测试内容

## 登录

测试：

```
POST

/api/auth/login

```

验证：

* Token生成
* Refresh Token
* 权限

---

## 工单API

例如：

```
POST

/api/workorders

```

测试：

成功：

```
201

```

失败：

```
400

403

409

```

---

# 10. RabbitMQ事件测试

Industrial Platform核心：

事件驱动。

架构：

```
Service

 |

RabbitMQ

 |

Consumer

```

---

# 10.1 测试方式

使用：

```
RabbitMQ TestContainer

```

启动：

```
RabbitMQ Docker

```

测试：

发布事件。

例如：

```
WorkOrderCompletedEvent

```

验证：

Batch Service收到。

---

# 10.2 事件测试目录

```
EventTests


├── PublishTests

├── ConsumeTests

├── RetryTests

└── DeadLetterTests

```

---

# 11. IoT模拟测试

工业平台特色。

不能依赖真实设备。

设计：

设备模拟器。

---

# 11.1 IoT Simulator

独立项目：

```
IoTSimulator


```

模拟：

PLC

仪表

传感器

---

数据：

```
Temperature

Weight

Pressure

Speed

Energy

```

---

# 11.2 测试流程

```
Simulator

 |

MQTT/TCP

 |

IoT Collector

 |

RabbitMQ

 |

MES

```

验证：

* 数据采集
* 数据转换
* 异常处理
* 断线恢复

---

# 12. SignalR测试

应用：

实时看板。

例如：

设备状态：

```
Running

↓

Alarm

```

推送：

```
EquipmentStatusChanged

```

测试：

```
Client

 |

SignalR Hub

 |

Server

```

验证：

* 连接
* 订阅
* 推送
* 重连

---

# 13. Frontend测试体系

技术：

Vue3

---

# 13.1 前端测试技术

| 用途   | 技术             |
| ---- | -------------- |
| Unit | Vitest         |
| 组件   | Vue Test Utils |
| E2E  | Playwright     |
| Mock | MSW            |
| 覆盖率  | c8             |

---

# 13.2 前端目录

```
web

├── src

└── tests

    ├── components

    ├── views

    ├── stores

    ├── api

    └── e2e

```

---

# 14. Vue组件测试

例如：

称量页面：

```
WeightingPanel.vue

```

测试：

* 输入重量
* 单位转换
* 超差报警
* 提交

---

# 15. PC/PDA/Mobile测试

三端统一。

测试：

```
PC

Tablet

Mobile

```

重点：

响应式布局。

---

# 16. AI Agent测试体系

Industrial Platform包含：

工业AI助手。

测试：

```
User

 |

AI Agent

 |

Tool

 |

MES

```

---

# 16.1 Agent测试

测试：

## 意图识别

输入：

```
查询今天生产情况

```

输出：

```
ProductionQuery

```

---

## Tool调用

验证：

AI是否正确调用：

```
GetWorkOrderStatus

GetEquipmentAlarm

```

---

# 17. RAG测试体系

工业知识库：

```
SOP

工艺文件

设备手册

质量标准

```

---

测试指标：

## Recall

召回正确率。

---

## Answer Accuracy

答案准确率。

---

## Citation

知识来源引用。

---

测试：

```
Question

↓

Retriever

↓

LLM

↓

Answer

```

---

# 18. 性能测试体系

## 18.1 k6压力测试

场景：

登录：

```
1000用户

```

MES：

```
创建工单

100 TPS

```

---

## 18.2 核心指标

| 指标      |     目标 |
| ------- | -----: |
| API响应   | <500ms |
| 查询      |    <1s |
| 事件延迟    |    <3s |
| SignalR |     实时 |
| IoT采集   |     秒级 |

---

# 19. CI/CD自动化测试流水线

GitHub Actions。

流程：

```
Push

 |

Build

 |

Unit Test

 |

Integration Test

 |

Docker Build

 |

Deploy


```

---

# 19.1 Pipeline

```
.github

└── workflows


    ├── build.yml

    ├── test.yml

    ├── docker.yml

    └── deploy.yml

```

---

# 20. GitHub Actions示例

```yaml
name:
 test


on:
 push


jobs:

 test:

  runs-on:
   ubuntu-latest


  steps:


  - uses:
      actions/checkout@v4


  - name:
      dotnet test

    run:

      dotnet test
      --collect:"XPlat Code Coverage"

```

---

# 21. 测试覆盖率要求

商业项目标准：

| 模块             | 覆盖率 |
| -------------- | --: |
| Domain         | 90% |
| Application    | 80% |
| Infrastructure | 70% |
| API            | 80% |
| Frontend       | 70% |

---

# 22. MVP阶段测试范围

第一阶段：

```
Identity Service

ReferenceData Service

MasterData Service

OperationalData Service

WorkOrder Service

Weighting Service

```

必须完成：

## Backend

√ Domain测试

√ Application测试

√ API测试

## Database

√ Repository测试

## Event

√ RabbitMQ核心事件

## Frontend

√ 核心页面测试

---

# 23. 后续扩展测试能力

未来增加：

```
自动化设备实验室

数字孪生测试环境

AI Agent Benchmark

工业仿真测试

生产数据回放测试

```

---

# 24. Codex自动生成测试规范

Codex生成Service时：

必须同时生成：

```
Service

├── src

│

└── tests

    ├── Domain.Tests

    ├── Application.Tests

    ├── Api.Tests

```

---

Prompt模板：

```
请为 WorkOrder Service
生成完整自动化测试。

要求：

1. DDD Domain测试

2. Application Handler测试

3. PostgreSQL TestContainer测试

4. API Integration测试

5. RabbitMQ事件测试

6. xUnit

7. FluentAssertions

8. 覆盖核心业务流程

```

---

# 25. 最终测试体系总结

Industrial Platform测试体系：

```
                 Production

                    ▲

              E2E Test

                    ▲

        API / Event / SignalR

                    ▲

       Application Integration

                    ▲

          Domain Unit Test


```

最终目标：

建立：

> 面向工业软件商业交付的自动化质量体系。

使 Industrial Platform 具备：

* 大规模MES交付能力
* 微服务持续演进能力
* 多租户SaaS稳定能力
* 工业AI可靠能力
* 设备接入可靠能力

---
