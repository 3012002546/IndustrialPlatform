# 08-RabbitMQ事件总线设计规范

版本：v1.0
项目名称：Industrial Platform
消息架构：RabbitMQ + Event Driven Architecture
设计思想：

> 服务之间不直接依赖，通过事件实现业务解耦。

---

# 1. 事件驱动架构目标

Industrial Platform采用：

```
DDD领域模型

        +

领域事件 Domain Event

        +

RabbitMQ消息总线

        +

微服务异步通信
```

---

传统MES：

```
WorkOrder Service

       |
       |
       调用

       |
       ↓

Equipment Service

       |
       ↓

Quality Service

```

问题：

* 强耦合
* 一个服务异常导致链路失败
* 难以扩展
* 无法支持工业实时场景

---

Industrial Platform：

```
             WorkOrder Service


                    |
                    |
          WorkOrderCreated Event

                    |
                    ↓


     RabbitMQ Event Bus


       |             |             |

       ↓             ↓             ↓


Equipment      Material       Trace


Service        Service        Service

```

---

# 2. RabbitMQ在平台中的定位

RabbitMQ负责：

## 业务事件

例如：

```
工单创建

生产开始

批次生成

物料消耗

质量完成

```

---

## 工业数据事件

例如：

```
设备状态变化

报警产生

采集异常

```

---

## 系统事件

例如：

```
用户登录

权限变化

配置更新

```

---

# 3. 消息架构

整体：

```
                 Industrial.EventBus


                         |


                     RabbitMQ


                         |


 ------------------------------------------------

 |                 |                 |


Business Exchange  IoT Exchange    System Exchange


```

---

# 4. RabbitMQ部署规划

生产：

建议：

```
RabbitMQ Cluster


Node1

Node2

Node3


+

Mirrored Queue

+

Quorum Queue

```

---

开发：

Docker：

```
rabbitmq:management
```

---

# 5. Exchange设计

采用Topic Exchange。

结构：

```
industrial.business


industrial.iot


industrial.system


industrial.command


industrial.integration

```

---

# 6. Exchange详细规划

## 6.1 Business Exchange

业务事件。

```
industrial.business

```

例如：

```
mes.workorder.created

mes.workorder.started

mes.material.consume

mes.batch.created

```

---

## 6.2 IoT Exchange

设备。

```
industrial.iot

```

例如：

```
iot.device.online

iot.device.offline

iot.sensor.changed

iot.alarm.created

```

---

## 6.3 System Exchange

平台。

```
industrial.system

```

例如：

```
system.user.login

system.permission.changed

system.file.uploaded

```

---

# 7. Routing Key规范

格式：

```
domain.aggregate.event

```

例如：

工单：

```
mes.workorder.created
```

设备：

```
iot.equipment.status.changed
```

质量：

```
mes.quality.inspection.completed

```

---

# 8. Event命名规范

统一：

过去式。

错误：

```
CreateWorkOrder

```

正确：

```
WorkOrderCreated

```

原因：

事件描述已经发生。

---

# 9. Event模型设计

基础接口：

```csharp
public interface IIntegrationEvent
{
    Guid EventId {get;}

    DateTime CreatedTime {get;}

    string EventType {get;}
}

```

---

基础类：

```csharp
public abstract class IntegrationEvent
{

public Guid EventId {get;set;}

public DateTime CreatedTime {get;set;}

public string EventType 
=> GetType().Name;


}

```

---

# 10. 示例：工单创建事件

Namespace：

```
Industrial.Contracts.Events.MES
```

代码：

```csharp
public class WorkOrderCreatedEvent 
    : IntegrationEvent
{


public long WorkOrderId {get;set;}


public string OrderNo {get;set;}


public long ProductId {get;set;}


public decimal Quantity {get;set;}


}

```

---

# 11. Domain Event和Integration Event区别

非常重要。

## Domain Event

领域内部。

例如：

```
WorkOrderCreatedDomainEvent

```

位置：

```
WorkOrder.Domain
```

作用：

通知领域逻辑。

---

## Integration Event

跨服务。

例如：

```
WorkOrderCreatedEvent

```

位置：

```
WorkOrder.Contracts

```

作用：

RabbitMQ传播。

---

关系：

```
Domain Event


      |

      |

Event Handler


      |

      |

Integration Event


      |

      |

RabbitMQ


```

---

# 12. Outbox Pattern设计

工业系统必须使用。

原因：

避免：

```
数据库提交成功

↓

RabbitMQ发送失败

```

导致数据不一致。

---

方案：

事务：

```
BEGIN


保存业务数据


保存Event


COMMIT


```

数据库：

增加：

```
event_outbox

```

---

表结构：

```sql
create table event_outbox
(

id bigint,


event_type varchar(200),


payload jsonb,


status varchar(20),


created_time timestamp


)

```

---

后台：

```
Outbox Worker


扫描


↓

发送RabbitMQ


↓

修改状态

```

---

# 13. 消息消费设计

每个服务：

独立Consumer。

例如：

Material Service：

监听：

```
mes.workorder.started

```

---

结构：

```
Material.Service


Consumers


├── WorkOrderStartedConsumer

├── MaterialConsumedConsumer

```

---

# 14. 消费幂等设计

工业系统必须。

原因：

RabbitMQ：

至少一次。

可能：

```
Event

↓

Consumer

↓

失败

↓

重新消费

```

---

方案：

建立：

```
event_consume_log

```

表：

```sql
create table event_consume_log
(

event_id uuid,


consumer varchar(200),


consume_time timestamp,


status varchar(20)


)

```

---

消费流程：

```
收到Event


↓

检查event_id


↓

已存在?

   是

   直接返回


↓

执行业务


↓

保存记录

```

---

# 15. 消息确认机制

Consumer：

采用：

Manual ACK。

流程：

```
Receive


↓

Business Process


↓

Database Commit


↓

ACK

```

---

禁止：

```
Auto ACK

```

---

# 16. 消息失败处理

设计：

## Retry Queue

例如：

```
workorder.retry.5s

workorder.retry.30s

workorder.retry.5m

```

---

流程：

```
Consumer失败


↓

Retry Queue


↓

重新消费


↓

成功


```

---

# 17. 死信队列 DLQ

超过次数：

进入：

```
industrial.dlq

```

结构：

```
DLQ


├── message

├── error

├── stacktrace

├── retryCount

```

---

管理页面：

Dashboard显示。

---

# 18. Command和Event区别

很多MES容易混乱。

## Command

请求。

例如：

```
StartProductionCommand

```

特点：

* 一个目标
* 一个处理者

---

## Event

事实。

例如：

```
ProductionStartedEvent

```

特点：

* 多订阅者

---

示例：

```
用户点击开始生产


↓

Command


StartWorkOrderCommand


↓

WorkOrder Service


↓

Event


WorkOrderStarted

```

---

# 19. MES核心事件设计

## 工单域

```
mes.workorder.created


mes.workorder.released


mes.workorder.started


mes.workorder.paused


mes.workorder.completed


mes.workorder.cancelled

```

---

## 物料域

```
mes.material.received


mes.material.issued


mes.material.consumed


mes.material.returned

```

---

## 称量域

```
mes.weight.task.created


mes.weight.started


mes.weight.completed


mes.weight.failed

```

---

## 设备域

```
iot.device.online


iot.device.offline


iot.device.alarm


iot.device.status.changed

```

---

## 质量域

```
quality.inspection.created


quality.inspection.completed


quality.inspection.failed

```

---

## 追溯域

```
trace.batch.created


trace.material.linked


trace.product.completed

```

---

# 20. 设备数据事件设计

设备：

```
PLC

Sensor

Instrument

```

数据流：

```
设备


↓

Agent


↓

RabbitMQ


↓

IoT Collector


↓

TimescaleDB


↓

OEE / Dashboard


```

---

事件：

```json
{
"deviceId":1001,

"tag":"Temperature",

"value":35.6,

"time":"2026-07-31T10:00:00"

}

```

---

# 21. RabbitMQ队列规划

示例：

```
Queues


├── mes.workorder.queue


├── mes.material.queue


├── mes.trace.queue


├── iot.collector.queue


├── monitor.alert.queue


└── notification.queue

```

---

# 22. Contract项目设计

独立：

```
backend/shared/contracts

```

结构：

```
Contracts


├── Events

│
├── MES

│   ├── WorkOrderCreatedEvent.cs

│
├── IoT

│   ├── DeviceAlarmEvent.cs

│
└── System

```

---

目的：

避免：

```
服务A引用服务B

```

改为：

```
A

↓

Contracts

↓

B

```

---

# 23. RabbitMQ封装设计

不要业务直接调用。

错误：

```csharp
channel.BasicPublish()

```

---

统一：

```
Industrial.EventBus

```

接口：

```csharp
public interface IEventBus
{


Task PublishAsync<T>(
T eventMessage);


void Subscribe<T>(
Func<T,Task> handler);


}

```

---

# 24. 配置规范

appsettings.json

```json
{
"RabbitMQ":
{
"Host":"rabbitmq",

"Port":5672,

"User":"admin",

"Password":"xxx"

}

}

```

---

# 25. Docker目录

```
docker


rabbitmq


├── docker-compose.yml

├── config

└── definitions.json

```

---

# 26. Codex开发规范

每增加事件：

必须生成：

```
/docs/events


EventName.md


```

包含：

```
事件说明

生产者

消费者

Payload

版本

兼容策略

```

---

# 27. 版本管理

事件必须支持版本。

例如：

```
WorkOrderCreated.v1


WorkOrderCreated.v2

```

禁止：

直接修改旧事件。

---

# 28. 最终事件架构

```
                    MES Service


                         |


                  Domain Event


                         |


                  Outbox Table


                         |


                  Event Publisher


                         |


                    RabbitMQ


 ------------------------------------------------


 |              |              |               |


Material     Equipment      Trace        Quality


```

---

# 29. MVP阶段实现范围

不要一次实现全部。

第一阶段：

必须：

```
√ RabbitMQ基础封装

√ EventBus

√ Outbox

√ Consumer

√ Retry

√ DLQ

√ WorkOrder事件

√ Weight事件

√ Trace事件

```

---

# 
