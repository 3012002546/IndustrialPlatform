继续输出：

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

## 第一阶段

实现：

✅ OPC UA

✅ Modbus TCP

✅ MQTT

✅ 设备管理

✅ 点位配置

✅ 数据采集

✅ RabbitMQ事件

✅ TimescaleDB存储

---

# 23. 第二阶段

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

# 24. Codex任务拆分

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
