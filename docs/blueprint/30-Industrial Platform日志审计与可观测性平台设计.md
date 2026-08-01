# 30-Industrial Platform日志审计与可观测性平台设计

版本：V1.0
项目：Industrial Platform
定位：工业数字化执行平台

---

# 1. 文档目标

本文档设计 Industrial Platform 企业级日志、审计、监控、链路追踪体系。

目标：

* 支撑微服务生产环境运行
* 快速定位工业现场问题
* 满足MES审计要求
* 支撑多租户SaaS运营
* 支撑设备异常追溯
* 支撑AI Agent行为审计
* 支撑长期运维

Industrial Platform不是普通业务系统。

工业场景特点：

```
设备异常
    |
    |
采集数据
    |
    |
业务事件
    |
    |
生产执行
    |
    |
质量追溯
    |
    |
责任分析
```

因此：

日志 ≠ Debug信息

日志 = 工业生产证据链。

---

# 2. 可观测性总体架构

整体设计：

```
                 Industrial Platform


                       |
        --------------------------------

        |              |               |

      Logs          Metrics          Trace


        |              |               |

   Serilog       Prometheus       OpenTelemetry


        |              |               |

        --------------------------------

                       |

              Observability Platform


                       |

        --------------------------------

        |              |               |

     Loki/ELK       Grafana        Jaeger


```

---

# 3. 可观测性三大支柱

## 3.1 Logs 日志

记录：

* 系统行为
* 用户操作
* 业务事件
* 异常信息

例如：

```
用户张三执行工单释放

WorkOrder=WO001

Result=Success

```

---

## 3.2 Metrics 指标

记录：

系统状态。

例如：

```
API请求数量

CPU

Memory

RabbitMQ队列长度

数据库连接数

IoT采集数量

```

---

## 3.3 Trace 链路

记录：

一次业务全过程。

例如：

```
称量任务


Weighting Service

        |
        |
RabbitMQ

        |
        |

Batch Record Service

        |

Trace Service

```

---

# 4. 技术选型

## Backend

| 能力    | 技术                 |
| ----- | ------------------ |
| 日志    | Serilog            |
| 日志格式  | JSON               |
| 链路    | OpenTelemetry      |
| 指标    | Prometheus         |
| 展示    | Grafana            |
| Trace | Jaeger             |
| 日志存储  | Loki/ElasticSearch |

---

# 5. Serilog统一日志设计

## 5.1 日志结构

采用结构化日志。

禁止：

```csharp
_logger.LogInformation(
"创建工单成功");
```

推荐：

```csharp
_logger.LogInformation(
"Create WorkOrder Success {@WorkOrder}",
workOrder);

```

---

# 5.2 日志模型

统一：

```json
{
 "Timestamp":
 "2026-08-01T10:00:00",

 "Level":
 "Information",

 "Service":
 "WorkOrder",

 "TenantId":
 "TENANT001",

 "UserId":
 "USER001",

 "TraceId":
 "xxx",

 "BusinessId":
 "WO001",

 "Message":
 "WorkOrder Created"

}

```

---

# 6. 日志分类设计

Industrial Platform分：

---

## 6.1 Application Log

应用运行日志。

例如：

```
Command执行

Query执行

Handler调用

```

---

## 6.2 Business Log

业务日志。

例如：

```
工单创建

工单关闭

批记录审核

```

---

## 6.3 Audit Log

审计日志。

例如：

```
修改配方

修改物料属性

删除基础数据

```

---

## 6.4 Device Log

设备日志。

例如：

```
PLC断线

仪表通讯失败

采集异常

```

---

# 7. 日志等级规范

| 级别          | 用途     |
| ----------- | ------ |
| Trace       | 详细调试   |
| Debug       | 开发调试   |
| Information | 正常业务   |
| Warning     | 异常但可恢复 |
| Error       | 错误     |
| Critical    | 系统故障   |

生产环境：

```
Information
以上

```

---

# 8. Business Audit审计体系

工业软件必须具备：

不可抵赖。

---

# 8.1 审计模型

数据库：

Audit Service

表：

```
audit_log

```

字段：

```sql
Id

TenantId

UserId

ServiceName

Action

BusinessType

BusinessId

BeforeData

AfterData

CreateTime

IpAddress

Device

```

---

# 8.2 审计场景

必须记录：

## 基础数据

```
物料修改

设备修改

工艺修改

```

---

## 生产执行

```
工单释放

批次结束

称量确认

```

---

## 质量

```
检验结果修改

批记录审核

```

---

# 9. 数据变更追踪设计

采用：

Before / After

例如：

修改配方：

修改前：

```json
{
"temperature":80
}

```

修改后：

```json
{
"temperature":85
}

```

---

# 10. OpenTelemetry链路设计

## 10.1 Trace结构

一次生产动作：

```
TraceId


Weighting API


 |
 |
Weight Command


 |
 |
RabbitMQ


 |
 |
Batch Service


 |
 |
Trace Service


```

---

# 10.2 Trace字段

统一：

```
TraceId

SpanId

TenantId

UserId

BusinessId

EquipmentId

BatchId

```

---

# 11. .NET OpenTelemetry集成

安装：

```
OpenTelemetry

OpenTelemetry.Exporter.Jaeger

OpenTelemetry.Exporter.Prometheus

```

配置：

```csharp
builder.Services
.AddOpenTelemetry()
.WithTracing(
x =>
{
x.AddAspNetCoreInstrumentation();

x.AddHttpClientInstrumentation();

x.AddRabbitMQInstrumentation();

});

```

---

# 12. Prometheus指标设计

## 12.1 系统指标

```
cpu_usage

memory_usage

disk_usage

```

---

## 12.2 服务指标

例如：

WorkOrder：

```
workorder_created_total

workorder_completed_total

```

---

## 12.3 工业指标

特色：

```
equipment_running_count

equipment_alarm_count

production_output

material_consumption

```

---

# 13. Grafana工业监控大屏

设计：

---

## 系统驾驶舱

显示：

```
服务数量

在线状态

CPU

Memory

异常数量

```

---

## MES生产驾驶舱

显示：

```
今日计划

完成数量

异常工单

设备状态

```

---

## IoT驾驶舱

显示：

```
设备在线率

采集数量

通讯异常

```

---

# 14. ELK/Loki日志平台

推荐：

中小规模：

```
Serilog

 |

Loki

 |

Grafana

```

大型：

```
Serilog

 |

Kafka

 |

ElasticSearch

 |

Kibana

```

---

# 15. 微服务日志规范

每个Service必须包含：

```
ServiceName

Version

Environment

TenantId

TraceId

UserId

```

---

例如：

```
WorkOrder.Service

v1.0.0

Production

```

---

# 16. RabbitMQ监控设计

监控：

队列：

```
message_count

consumer_count

failed_count

```

异常：

```
Dead Letter Queue

Retry Queue

```

---

# 17. IoT Collector监控设计

IoT特殊。

需要：

## 设备连接状态

```
DeviceOnline

DeviceOffline

```

---

## 数据质量

例如：

```
采集间隔

数据丢失率

异常值

```

---

# 18. SignalR监控

指标：

```
connection_count

message_send_total

message_failed_total

```

---

# 19. AI Agent审计体系

Industrial AI Assistant需要记录：

---

## 用户问题

例如：

```
今天3号线生产多少？

```

---

## Agent过程

记录：

```
Prompt

Tool调用

Knowledge来源

Answer

```

---

## AI Audit表

```
ai_request_log


Id

UserId

Question

Tools

Documents

Answer

Token

CreateTime

```

---

# 20. RAG可观测性

记录：

```
Question


↓

Embedding


↓

Vector Search


↓

Documents


↓

LLM


↓

Answer

```

指标：

```
Recall

Similarity

Token

Latency

```

---

# 21. Docker部署架构

开发环境：

```
docker-compose


|
|
├── PostgreSQL

├── Redis

├── RabbitMQ

├── Prometheus

├── Grafana

├── Loki

└── Jaeger


```

---

# 22. Kubernetes生产架构

未来：

```
K8S


 |
 |
Ingress


 |
 |
Services


 |
 |
OpenTelemetry Collector


 |
 |
Observability Cluster


```

---

# 23. CI/CD集成

Pipeline：

```
Code Commit

↓

Build

↓

Unit Test

↓

Docker Image

↓

Deploy

↓

Health Check

↓

Monitoring


```

---

# 24. Health Check设计

每个微服务：

必须提供：

```
/health


/readiness


/liveness


```

例如：

检查：

```
Database

Redis

RabbitMQ

External API

```

---

# 25. 测试环境监控

MVP阶段：

必须具备：

```
Docker Compose

+

Grafana

+

Prometheus

```

---

# 26. MVP实现范围

第一阶段：

完成：

## 日志

√ Serilog统一配置

√ JSON日志

√ TraceId

## 审计

√ 用户操作日志

√ 数据修改记录

## 监控

√ HealthCheck

√ Prometheus

## 展示

√ Grafana基础Dashboard

---

# 27. 后续扩展能力

未来增加：

## 工业数字孪生监控

```
设备

状态

能耗

产能

```

---

## AI智能运维

例如：

AI发现：

```
某设备振动异常

预计2小时后故障

```

---

## 自动故障分析

流程：

```
日志

+

指标

+

Trace

+

AI

=

故障报告

```

---

# 28. Codex开发规范

生成任何Service：

必须包含：

```
Service

├── Logging

├── HealthCheck

├── Metrics

├── Trace

└── Audit

```

Prompt：

```
请为 Industrial Platform
生成 WorkOrder Service

要求：

1. 集成Serilog

2. OpenTelemetry

3. HealthCheck

4. Prometheus Metrics

5. Audit日志

6. TraceId贯穿RabbitMQ

7. Docker环境支持

```

---

# 29. 最终架构总结

Industrial Platform可观测性：

```
                 User


                  |

              Business


                  |

          ----------------


          Logs

          Metrics

          Trace


          ----------------


                  |

          Observability


                  |

          AI Analysis


```

最终目标：

打造：

> 面向工业生产环境的可诊断、可追踪、可审计、可智能分析的平台基础设施。

---

