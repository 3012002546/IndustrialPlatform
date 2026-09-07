继续输出：

> 2026-09-07 执行前置：PF-10B 标签管理平台在 PF-11 之前，详见总 Todo 与蓝图 35/实施 13B。IoTCollector 只复用 Device Agent/Runtime 的受限设备连接与诊断语义，不依赖 Label 任务/模板/数据库；采集点、时序质量、边缘缓存仍由本服务拥有。

PF11先兼容平台身份/权限、设备引用、数据质量/时标、事件与健康契约，再用同一采集核心和已选驱动独立部署接外部MES。最小依赖为采集/边缘执行及必要存储和安全适配，不强制安装Label、报表或MES；排期前置不等于运行依赖。详见[蓝图32§2.1～2.2](32-Industrial%20Platform%20Service%20Host与内部模块边界.md)，具体装配/接口/页面在实施14派遣前细化。

# 17-IoT Collector Service详细设计.md

> Industrial Platform
> 工业数字化执行平台
> IoT Collector Service（工业设备数据采集服务）

版本：v1.0

定位：

工业设备连接与数据采集基础平台

架构：

DDD
+
Clean Architecture
+
Industrial IoT
+
Event Driven
+
Edge Computing

---

# 1. Service定位

## 1.1 服务职责

IoT Collector Service 是 Industrial Platform 的**工业设备数据入口服务**。

负责：

* PLC数据采集
* 设备状态采集
* 工艺参数采集
* 设备报警采集
* 设备通讯协议适配
* 数据清洗
* 数据转换
* 边缘缓存
* 实时数据推送
* 设备事件发布

---

## 1.2 支持协议

第一阶段：

| 协议          | 用途        |
| ----------- | --------- |
| OPC UA      | 主流工业设备    |
| Modbus TCP  | 仪表、PLC    |
| MQTT        | IoT设备     |
| TCP Socket  | 自定义设备     |
| Serial Port | RS232/485 |

后续：

* Siemens S7
* Mitsubishi MC
* Omron FINS
* EtherNet/IP
* Profinet

---

# 2. 在Industrial Platform中的位置

整体数据流：

```
                MES

                 |
                 |

       WorkOrder Service

                 |
                 |

        IoT Collector Service

                 |
        +--------+--------+

        |                 |

     PLC设备          仪表设备


```

工业现场：

```
设备

 |

PLC

 |

IoT Collector Agent

 |

RabbitMQ

 |

Industrial Platform

```

---

# 3. 设计目标

## 3.1 设备解耦

传统：

```
MES
 |
PLC Driver
 |
设备
```

问题：

* MES复杂
* 驱动混乱
* 难维护

设计：

```
MES

|

标准事件

|

IoT Collector

|

各种协议

|

设备

```

---

# 4. 核心能力模型

IoT Collector包含：

```
Device Management

+

Protocol Adapter

+

Data Collector

+

Data Processor

+

Event Publisher

+

Realtime Gateway

```

---

# 5. Domain模型设计

核心Aggregate：

```
Device Aggregate


       |
       |
       +-- DevicePoint

       |
       |
       +-- DeviceConnection

       |
       |
       +-- DeviceAlarm


```

---

# 6. Device模型

设备实体：

```csharp
public class Device
    : AggregateRoot<Guid>
{


    public string Code {get;private set;}


    public string Name {get;private set;}


    public DeviceType Type {get;private set;}


    public DeviceStatus Status {get;private set;}


    public List<DevicePoint> Points {get;}

}

```

---

# 7. DevicePoint

设备采集点

例如：

```
PLC001

Temperature

Speed

Pressure

RunStatus

```

模型：

```csharp
public class DevicePoint
{


    public string Tag {get;private set;}


    public string Address {get;private set;}


    public DataType DataType {get;private set;}


    public decimal Value {get;private set;}

}

```

---

# 8. 设备状态机设计

## Device状态

```
Unknown

 |

Offline

 |

Online

 |

Running

 |

Alarm


```

---

状态转换：

```
Offline

 |
Connect()

Online


Online

 |
Start()

Running


Running

 |
Alarm

Alarm


```

---

# 9. Solution结构

目录：

```
src/services


/IoTCollector


    /Industrial.IoTCollector.Api


    /Industrial.IoTCollector.Application


    /Industrial.IoTCollector.Domain


    /Industrial.IoTCollector.Infrastructure


    /Industrial.IoTCollector.Contracts


```

---

# 10. Clean Architecture设计

## Domain

```
Aggregates

Entities

ValueObjects

Events

Rules

```

---

## Application

```
Commands

Queries

Collectors

DTO

Handlers

```

---

## Infrastructure

```
SqlSugar

RabbitMQ

Redis

ProtocolAdapters

TimescaleDB

```

---

# 11. 数据库设计

数据库：

```
iot_platform

```

---

# 11.1 devices

设备表

```sql
CREATE TABLE devices
(

id uuid PRIMARY KEY,


code varchar(50),


name varchar(100),


protocol varchar(30),


ip varchar(50),


port int,


status int,


created_time timestamptz


);

```

---

# 11.2 device_points

采集点

```sql
CREATE TABLE device_points
(

id uuid,


device_id uuid,


tag varchar(100),


address varchar(100),


data_type varchar(30),


collect_interval int


);

```

---

# 11.3 device_data

实时数据

建议：

TimescaleDB

```sql
CREATE TABLE device_data
(

time timestamptz,


device_id uuid,


point_id uuid,


value numeric


);

```

创建：

Hypertable

```sql
SELECT create_hypertable(
'device_data',
'time'
);

```

---

# 11.4 device_alarm

报警记录

```sql
CREATE TABLE device_alarm
(

id uuid,


device_id uuid,


level int,


message varchar(200),


time timestamptz


);

```

---

# 12. Protocol Adapter设计

核心接口：

```csharp
public interface IProtocolAdapter
{


Task ConnectAsync();


Task DisconnectAsync();


Task<DeviceValue> ReadAsync(
string address);


}

```

---

实现：

```
Adapters


 /OpcUa


 /Modbus


 /Mqtt


 /Tcp


 /Serial


```

---

# 13. OPC UA设计

示例：

配置：

```json
{
 "endpoint":
"opc.tcp://192.168.1.10:4840",

 "nodes":[

 {
 "tag":"Speed",
 "nodeId":"ns=2;s=Speed"
 }

 ]

}

```

---

# 14. Modbus设计

配置：

```json
{

"ip":"192.168.1.20",

"port":502,


"register":

40001


}

```

---

# 15. MQTT设计

Topic：

设备上传：

```
factory/device001/data

```

消息：

```json
{

"device":"001",

"temperature":30.5,

"speed":100


}

```

---

# 16. 数据采集流程

```
Timer


 |

Collector Worker


 |

Protocol Adapter


 |

Read Device


 |

Data Processor


 |

+-------------+

|

Redis

|

TimescaleDB

|

RabbitMQ


```

---

# 17. Background Worker设计

.NET Worker Service：

```
CollectorWorker


ExecuteAsync()


while(true)

{

 collect();

 process();

 publish();

}


```

---

# 18. Redis设计

## 实时状态

Key：

```
device:{id}:status

```

Value：

```json
{

"status":"Running",

"value":100,

"time":""

}

```

---

## 当前设备连接

```
device:{id}:connection

```

---

# 19. RabbitMQ事件设计

Exchange:

```
industrial.iot.exchange

```

---

# 19.1 数据事件

RoutingKey：

```
device.data.changed

```

Payload:

```json
{

"deviceId":"",

"tag":"Speed",

"value":100,


"time":""

}

```

消费者：

```
MES

Dashboard

Analytics


```

---

# 19.2 设备上线

RoutingKey：

```
device.online

```

---

# 19.3 设备报警

RoutingKey：

```
device.alarm

```

Payload：

```json
{

"device":"",

"level":"High",

"message":"Motor Alarm"

}

```

消费者：

```
MES

Monitor

Notification


```

---

# 20. SignalR实时推送

Hub：

```
DeviceHub

```

推送：

* 在线状态
* 实时参数
* 报警

应用：

```
设备看板

Andon

MES主页

```

---

# 21. 与Server Monitor集成

区别：

Server Monitor：

```
服务器资源

CPU

Memory

Disk

Network

```

IoT Collector：

```
工业设备

PLC

Sensor

Machine


```

统一：

```
Industrial Monitoring Platform

```

---

# 22. MVP范围

## 当前PF11首期

先从OPC UA、Modbus TCP、MQTT等候选中按实际设备选择一种协议，完成设备连接、点位、采集、缓存、断线恢复、质量/时标、幂等写入与监控闭环；其余协议另行追加。协议、消息投递和时序存储组件须在实施14派遣前核验，不将RabbitMQ/TimescaleDB示例或三协议候选当作已实现或同时必需的首期门禁。平台与外部最小装配按蓝图32§2.1～2.2。

---

# 23. 远期扩展候选（不属于当前PF11首期）

扩展：

## 边缘计算Agent

部署：

```
工厂现场服务器


IoT Agent.exe


```

能力：

* 本地缓存
* 断网续传
* 数据压缩
* 协议转换

---

## AI预测维护

增加：

```
设备数据

|

AI模型

|

预测故障


```

---

# 24. 历史功能拆解（非派遣Todo）

以下Task-01～09保留作功能设计素材，不是当前可派遣任务。正式TASK-PF11编号、最小依赖、字段/API/线框、单协议选择与验收须先在实施14完成；当前该实施方案尚未创建，不能直接按下面清单编码或据示例增加必需平台依赖。

---

## Task-01

初始化IoT服务

提交：

```
feat:init iot collector
```

---

## Task-02

设备领域模型

实现：

```
Device

DevicePoint

DeviceStatus


```

提交：

```
feat:add device domain
```

---

## Task-03

数据库

实现：

```
Device Table

Point Table

Timescale Table

```

提交：

```
feat:add iot persistence
```

---

## Task-04

协议框架

实现：

```
IProtocolAdapter


MockAdapter


```

提交：

```
feat:add protocol abstraction
```

---

## Task-05

Modbus实现

提交：

```
feat:add modbus adapter
```

---

## Task-06

OPC UA

提交：

```
feat:add opcua adapter
```

---

## Task-07

采集Worker

实现：

```
BackgroundService

Scheduler

```

提交：

```
feat:add collector worker
```

---

## Task-08

消息事件

实现：

```
RabbitMQ Publisher

Device Events

```

提交：

```
feat:add iot events
```

---

## Task-09

测试

包含：

```
Adapter Test

Domain Test

Integration Test

```

---

# 25. 后续演进能力

IoT Collector 最终成为：

```
Industrial Edge Platform

```

能力：

```
设备连接

+

实时数据

+

边缘计算

+

协议转换

+

工业事件中心

+

AI分析入口


```

支撑：

* MES
* APS
* OEE
* 能源管理
* 设备预测维护
* 数字孪生

---
