# Server Monitor 独立产品设计文档 V1.0

> 文档版本：V1.0
> 产品名称：Server Monitor Platform
> 产品定位：工业数字化平台运维监控子系统
> 架构模式：Agent + Server + Dashboard
> 技术方向：.NET Worker Agent + Vue3；业务存储、时序存储和推送适配在实施13核验，TimescaleDB是候选而非所有部署的必装依赖。

> 2026-11-17顺序更新：PF-10A优先创建KnowledgeHub.Service，PF-10后续扩展并只交付ServerMonitor。本文历史指标/协议示例是详细设计输入，尚无实施13及可派遣字段/API/页面规格。宿主、独立装配及跨阶段边界以蓝图32§2.3、§5.3为准。

> 2026-11-17用户最终澄清：`D:\Code\ServerOps\ServerOpsCenter`继续独立维护使用，作为平台监控参考，不默认迁入或API集成。Industrial Platform通常部署现场，监控按现场内网设计，不依赖VPN或旧系统运行；Agent拉取/上报、端口和节点凭据按现场条件细化，不直接沿用旧系统配置。源码初评见[评估记录§8](../evidence/2026-11-16-pf10-pf11-blueprint-alignment.md#8-serveropscenter源码初评2026-11-17)；阶段编号与真实监控闭环目标保留，PF10后于PF10A，实际节点验证及候选代码复用清单待定。

---

# 1. 产品概述

## 1.1 产品定位

ServerMonitor首先是Industrial Platform的原生监控功能，同时保留相对独立交付能力。PF10先验平台节点权限、告警/通知、审计和健康摘要，再用同一核心独立部署或嵌入其他MES；最小装配为监控模块、Agent、指标/告警存储及必要公共能力，不强制安装知识/问题/助手。当前Host和派遣前清单见[蓝图32§2.1～2.2](32-Industrial%20Platform%20Service%20Host与内部模块边界.md)，本文历史独立产品设想不自动扩大PF10范围。

目标：

> 对企业服务器、网络、应用、中间件进行统一监控，并为工业平台提供运行状态保障。

不属于 MES 核心业务。

关系：

```text
Industrial Platform

        |
        |
 ----------------------

 MES

 Server Monitor

 ----------------------

```

---

# 1.2 使用场景

## 场景1：MES项目运维

例如：

客户现场：

```text
MES服务器

数据库服务器

接口服务器

文件服务器

打印服务器

```

统一监控：

* CPU
* 内存
* 磁盘
* 网络
* 服务状态

---

## 场景2：云服务器管理

例如：

个人服务器：

```text
Linux VPS

Windows Server

Docker服务器

```

查看：

* 在线状态
* IP
* CPU
* 内存
* Docker状态

---

## 场景3：多项目运维

一个平台管理：

```text
项目A

项目B

项目C


服务器数量:

100+
```

---

# 2. 总体架构

## 2.1 架构设计

采用：

> Agent主动采集模型

不采用：

> Server主动扫描模型

原因：

工业现场：

* VPN隔离
* 防火墙限制
* 网络不稳定

---

总体：

```text

                Web Portal
                 Vue3

                    |
                    |

             Monitor API


                    |

        ----------------------

        |                    |

Metric模块            Alarm模块


        |

        |

    TimeSeries DB


        |

        |

 --------------------------

        Agent


 Windows Agent

 Linux Agent

 Docker Agent


```

---

# 3. 当前宿主与模块划分

```text
KnowledgeHub.Service（PF-10装配ServerMonitor）


├── ServerMonitor公开API/应用契约

├── ServerMonitor.Domain

├── ServerMonitor.Application

├── ServerMonitor.Infrastructure（含指标/告警存储适配）


├── ServerMonitor.Agent（独立辅助部署单元）

│
├── Windows Collector

├── Linux Collector

├── Docker Collector


├── Monitor.Web


├── Alarm（模块内部告警能力，不另建Host）

└── 运维看板（不重建PF09通用Report服务）

```

---

# 4. Agent采集端设计

## 4.1 技术架构

Agent：

采用：

.NET Worker Service

运行方式：

Windows：

```text
Windows Service
```

Linux：

```bash
systemd service
```

---

# 4.2 Agent职责

负责：

1. 本地数据采集

2. 数据格式转换

3. 心跳发送

4. 指标上传

不负责：

* 数据存储
* 报表
* 告警

---

# 4.3 Agent结构

```text
ServerMonitor.Agent


├── Host

│
├── Collector

│
├── Reporter

│
├── Heartbeat

│
├── Configuration

```

---

# 5. 指标采集设计

# 5.1 系统指标

## CPU

采集：

```text
CPU使用率

CPU核心数量

CPU频率

Load
```

---

## Memory

采集：

```text
Total

Used

Available

Usage%

```

---

## Disk

采集：

```text
磁盘容量

已用空间

剩余空间

IO速度

```

---

## Network

采集：

```text
IP

MAC

上传速度

下载速度

丢包率

连接数量

```

---

# 5.2 Windows专属

## 服务状态

例如：

```text
SQL Server

IIS

MES Service

Windows Service

```

状态：

```text
Running

Stopped

Error
```

---

## IIS

采集：

```text
Application Pool

Request Count

Response Time

Error Count

```

---

## Event Log

采集：

```text
系统异常

应用异常

安全日志

```

---

# 5.3 Linux专属

采集：

```text
Load Average

Process

Systemd

Docker

Network Socket

```

---

# 5.4 Docker监控

支持：

```text
Container


CPU

Memory

Network

Restart Count

Status

```

---

# 6. Collector插件设计

采用插件模式。

接口：

```csharp
public interface IMetricCollector
{

    string Name { get; }


    Task<MetricData> CollectAsync();

}

```

---

实现：

```text
CpuCollector

MemoryCollector

DiskCollector

NetworkCollector

DockerCollector

```

---

未来扩展：

```text
MysqlCollector

PostgreSQLCollector

RedisCollector

NginxCollector

IISCollector

```

---

# 7. 数据模型设计

## 7.1 Server

服务器信息。

表：

server_info

字段：

| 字段      | 说明      |
| ------- | ------- |
| Id      | 主键      |
| Name    | 服务器名称   |
| OS      | 操作系统    |
| IP      | IP地址    |
| AgentId | Agent编号 |
| Status  | 在线状态    |

---

## 7.2 Agent

agent_info

```text
AgentId

ServerId

Version

LastHeartbeat

Status

```

---

# 7.3 Metric

核心时序数据。

表：

metric_data

结构：

```text
Id

ServerId

MetricName

MetricValue

Unit

CollectTime

```

例如：

```text
Server01

cpu_usage

35

%

2026-07-31 10:00

```

---

# 8. 时序数据库设计

## 推荐

TimescaleDB

原因：

基于 PostgreSQL。

适合：

* CPU曲线
* 网络流量
* 设备数据

---

结构：

```text
PostgreSQL


+

TimescaleDB Extension


```

---

# 9. 数据生命周期

## 热数据

最近：

30天

保存：

PostgreSQL

---

## 温数据

半年：

压缩。

---

## 冷数据

归档：

MinIO

格式：

```text
Parquet

JSON

CSV

```

---

# 10. 心跳机制

Agent：

每30秒：

发送：

```json
{
"agentId":"001",

"status":"online",

"time":"2026-07-31"

}

```

---

服务端判断：

```text
超过3分钟

没有心跳

=> Offline

```

---

# 11. 告警中心设计

独立：

Alarm Service

---

# 11.1 告警规则

模型：

AlarmRule

例如：

CPU:

```text
条件：

CPU > 90%

持续：

5分钟

级别：

Warning
```

---

# 11.2 告警状态

状态机：

```text
Normal


 |

Trigger


 |

Alarm


 |

Recover

```

---

# 11.3 告警类型

## 系统

* CPU过高
* 内存不足
* 磁盘不足

## 网络

* 丢包
* 延迟

## 服务

* 服务停止
* Docker退出

## 应用

* IIS异常
* API异常

---

# 12. 通知设计

平台原生通过SystemData内Notification公开契约发送通知；独立部署使用已声明的等价适配，不因通知需要强制安装整套平台。以下渠道是候选清单，须按实际实现列启用条件、失败/重试与验收范围，不能视为首期全部已支持。

支持：

```text
企业微信

邮件

短信

Webhook

SignalR

```

---

# 13. Web Dashboard设计

Vue3。

---

## 首页

服务器总览：

```text
服务器数量

在线

离线

异常


```

---

## Server Detail

查看：

```text
CPU

Memory

Disk

Network

Process

Service

```

---

## 图表

使用：

ECharts

例如：

CPU趋势：

```text
|

90%

|

50%

|

10%

----------------

时间

```

---

# 14. 与Industrial Platform集成

保持松耦合。

方式：

## 方式1：API

MES调用：

```http
GET

/api/server/status

```

---

## 方式2：RabbitMQ事件

事件：

```json
ServerOfflineEvent

{

server:"MES01",

time:"2026-07-31"

}

```

消费者：

```text
ServerMonitor内部Alarm模块

可选PlatformStudio Dashboard公开订阅方

SystemData.Notification或独立通知适配

```

---

# 15. 安全设计

## Agent认证

采用：

Agent Token

注册：

```text
Server

|

生成Token

|

Agent配置

```

---

## 通信

HTTPS：

```text
TLS1.3

```

---

## 数据权限

支持：

```text
项目

客户

服务器组

```

---

# 16. 部署设计

## 小规模

Docker Compose：

```text
Nginx

KnowledgeHub.Service（只启用ServerMonitor）

必要身份/审计/通知适配（不新增Alarm Host）

PostgreSQL

Redis

RabbitMQ

```

---

上述为部署示意，Redis/RabbitMQ/时序扩展须按能力声明，不能由图示变成全部必装项；业务Provider、三种数据库的表名/schema、初始化/升级/恢复矩阵须在实施13补齐。监控SQL Server等目标的能力与平台自身使用哪种数据库分开验收。

## 大规模（未来扩容候选）

Kubernetes：

```text
Monitor API

多个实例


Metric Service

多个实例


TimescaleDB

```

---

# 17. 后续扩展

## 17.1 数据库监控

增加：

```text
Mysql

PostgreSQL

SQL Server

Oracle

```

采集：

* 连接数
* 慢SQL
* 锁

---

## 17.2 应用监控

增加：

APM能力。

例如：

```text
API耗时

异常数量

请求量

```

---

## 17.3 网络监控

支持：

```text
Ping

TCP Port

HTTP Health Check

```

---

# 18. 历史开发路线候选（非派遣Todo）

本章各阶段是早期产品能力拆解，未表示已实施或获当前PF10派遣。正式实施13必须先给平台兼容、最小Agent/存储/公共适配、字段/API/线框和真实节点门禁，再确定首期；不因为旧独立产品示例引入知识/问题/模型依赖或单独新增ServerMonitor核心Host。

当前PF10最低闭环沿总Todo§17：一个受管节点完成注册→采集→状态→告警→通知→告警处置记录；不能仅完成下列历史“第一阶段MVP”就宣布PF10完成。告警处置不扩成PF10A的问题/知识闭环。

## 第一阶段 MVP

目标：

服务器基础监控。

完成：

* Agent
* CPU
* Memory
* Disk
* Network
* Dashboard
* 在线状态

---

## 第二阶段

增加：

* Windows Service
* Linux Process
* Docker
* 告警

---

## 第三阶段

平台化：

* 多租户
* 报表
* 自动巡检
* AI异常分析

---

# 19. 总结

Server Monitor 最终定位：

```text
轻量级工业IT运维监控平台

=

Agent采集

+

时序数据

+

实时Dashboard

+

告警中心

+

工业平台集成

```

架构原则：

```text
独立产品

独立部署

服务数据所有权独立；物理拓扑由配置与初始化契约决定

事件集成
```

这样设计后：

* MES项目现场服务器可以接入
* 自己云服务器可以管理
* 首先完成平台原生运行保障链，再验同核心的独立/外部MES装配
