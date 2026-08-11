# 20-Industrial Platform部署架构设计

> Industrial Platform
> 工业数字化执行平台
>
> Deployment Architecture Design
>
> 版本：v1.0

> 当前平台基础层部署宿主以 `32-Industrial Platform Service Host与内部模块边界.md` 为准；本文中的其他服务名称表示 MES 后续阶段或未来可拆分目标。

---

# 1. 部署架构定位

Industrial Platform 不按照传统 MES 单体软件部署。

采用：

```
微服务
+
容器化
+
边缘计算
+
工业网络隔离
+
云边协同
+
自动化运维
```

设计。

目标：

支持：

* 小型工厂单机部署
* 中型工厂服务器部署
* 集团多工厂部署
* SaaS云部署
* 工业边缘节点部署

---

# 2. 整体部署模型

Industrial Platform采用：

```
                    Internet

                       |

                API Gateway

                       |

        --------------------------------

        Cloud Platform

        Identity

        SystemData

        ReferenceData

        Collaboration

        PlatformStudio

        OperationsCenter

        IoTCollector

        MasterData

        OperationalData

        MES Services

        Data Platform

        AI Platform


        --------------------------------


                       |

                 VPN / 专线


                       |


        Factory Edge Network


        IoT Collector

        OPC UA Gateway

        PLC

        Equipment


```

---

# 3. 部署形态设计

支持三种模式。

---

# 3.1 单机版部署

适合：

* 小型工厂
* Demo
* 项目交付

结构：

```
Windows Server

    |

Docker Desktop


    |

Industrial Platform


├── Gateway

├── Identity

├── SystemData

├── ReferenceData

├── Collaboration

├── PlatformStudio

├── OperationsCenter

├── IoTCollector

├── MasterData

├── OperationalData

├── WorkOrder

├── Weighting

├── Trace

├── BatchRecord


├── PostgreSQL

├── Redis

├── RabbitMQ

├── MinIO


```

特点：

* 一台服务器运行
* 快速交付
* 无需K8S

---

# 3.2 企业服务器部署

适合：

* 生产环境
* 工厂MES

架构：

```
                 LoadBalancer


                      |


              API Gateway


                      |


 ------------------------------------------------

Service Cluster


Identity

SystemData

ReferenceData

Collaboration

PlatformStudio

OperationsCenter

MasterData

OperationalData

WorkOrder

Weighting

IoT Collector

Trace

BatchRecord


 ------------------------------------------------



Infrastructure


PostgreSQL Cluster

Redis Cluster

RabbitMQ Cluster

MinIO Cluster



```

---

# 3.3 云SaaS部署

未来支持：

```
                  Cloud


              Kubernetes


                  |


        ---------------------

        Tenant A


        Tenant B


        Tenant C


        ---------------------


                  |


             Factory Edge


```

---

# 4. Kubernetes架构设计

生产推荐：

```
K8S Cluster


namespace:

industrial-system


```

---

# Namespace规划

```
industrial-system


├── gateway


├── identity


├── mes


├── iot


├── data


├── monitoring


└── infrastructure

```

---

# 5. Service Host 部署模型

当前每个 Service Host 独立 Deployment。同宿主内部模块不因阶段或领域名称自动拆成独立 Deployment；未来物理拆分目标仍按相同部署模型演进。

例如：

WorkOrder:

```
Deployment


workorder-api


 replicas:3


Service


workorder-service


ConfigMap


Secret


```

---

# 6. Docker镜像规范

统一：

```
registry

industrial-platform/


    identity:v1.0


    referencedata:v1.0


    masterdata:v1.0


    workorder:v1.0


    weighting:v1.0


```

---

# 7. Dockerfile设计

.NET10统一模板：

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0


WORKDIR /app


COPY publish .


ENTRYPOINT ["dotnet",
"Industrial.WorkOrder.Api.dll"]

```

---

# 8. Docker Compose开发环境

目录：

```
docker


├── docker-compose.yml


├── postgres


├── redis


├── rabbitmq


├── minio


└── seq

```

---

# compose服务

```yaml
services:


 postgres:

   image: postgres


 redis:

   image: redis


 rabbitmq:

   image: rabbitmq


 minio:

   image: minio/minio


```

---

# 9. 数据库部署设计

## PostgreSQL

生产：

推荐：

```
PostgreSQL 18

+
Patroni

+
etcd


```

架构：

```
        PostgreSQL Primary


              |


     ------------------


     Replica1


     Replica2


```

---

# 数据库策略

当前按 Service Host 隔离数据库或数据库凭据；同宿主模块至少使用独立 Schema 或表前缀、独立迁移，禁止跨模块直读 Repository。下列服务数据库名称是 `LogicalDatabaseName`，图示表达 Test/Staging/Production 的每服务物理数据库拓扑，物理目标由 SystemData 解析；Development 可使用配置的共享 PostgreSQL `industrial_platform_dev` 或共享 SQLite 文件，但仍禁止跨服务表访问和合并迁移账本。完整契约见蓝图 07、蓝图 33。未来拆分后可迁移为独立数据库：

```
industrial_identity


industrial_masterdata


industrial_workorder


industrial_weighting


industrial_trace


industrial_batch


```

符合：

DDD Bound Context

---

## 数据库初始化与环境引导

- 当前服务器基础 PostgreSQL 18 与 SystemData 自身数据库仍由 `deploy/cloud-dev` Compose/init 或等价部署步骤做最小引导，解决 SystemData bootstrap paradox。
- SystemData 启动后，其他服务数据库统一通过其数据库编排 API 登记、plan、provision/apply 和查询 Operation 状态；不部署独立 Database Migrator Service，不向业务 API 分发管理员凭据。
- Development/测试可按策略自动 provision + migrate；生产默认 `plan → 审批 → 备份 → apply`。
- provisioning 管理凭据由 Secret Provider/环境注入，并与 SystemData 普通运行连接分离；API、日志和审计不得返回或记录凭据。
- SystemData 不可用或迁移失败时，目标服务保持 NotReady；禁止静默连接到错误数据库或自行建库。
- `RemoteDevelopment.Enabled=false` 时服务保留 SQLite 本地回退；启用云端时使用 SystemData 编排的 PostgreSQL。完整流程读取蓝图 33。

---

# 10. Redis部署

用途：

* 缓存
* Session
* 实时状态
* 分布式锁

架构：

```
Redis Sentinel


        |

Master


        |

Slave


```

---

# 11. RabbitMQ部署

事件总线。

生产：

```
RabbitMQ Cluster


Node1


Node2


Node3


```

启用：

```
Quorum Queue


Publisher Confirm


Dead Letter Exchange

```

---

# 12. MinIO部署

对象存储。

保存：

```
Batch PDF

图片

附件

设备文件

报表文件

```

架构：

```
MinIO Cluster


Node1

Node2

Node3

Node4


```

---

# 13. IoT边缘部署架构

工业现场特殊。

不能让PLC直接访问云。

采用：

Edge Gateway

```
             Cloud


               |


             VPN


               |


        Edge Server


               |


       IoT Collector


               |


----------------------------


PLC

SCADA

Equipment


```

---

# 14. IoT Collector部署

独立：

```
Industrial.IoT.Edge


```

运行环境：

支持：

Windows Server

Linux

---

# 协议支持

第一阶段：

```
OPC UA

Modbus TCP

TCP Socket

MQTT


```

未来：

```
EtherNet/IP

S7

BACnet

```

---

# 15. 网络架构设计

工业网络：

```
设备层


    ↓


控制层


    ↓


MES层


    ↓


企业网络


    ↓


云平台


```

---

# 网络隔离

推荐：

```
VLAN


设备网络

10.10.0.0


MES网络

10.20.0.0


办公网络

10.30.0.0


```

---

# 16. VPN设计

远程运维：

```
Engineer


  |

VPN


  |

Factory


  |

MES Server


```

支持：

* WireGuard
* OpenVPN
* ZeroTier

---

# 17. 配置管理

统一：

```
Configuration Service

```

存储：

```
appsettings

connection strings

RabbitMQ

Redis

JWT

```

---

# 18. 服务发现

开发：

```
Docker DNS


```

生产：

```
Kubernetes Service Discovery


```

---

# 19. API Gateway设计

推荐：

```
YARP


```

架构：

```
Client


 |

Gateway


 |

Service


```

负责：

* JWT验证
* 路由
* 限流
* 日志
* 灰度发布

---

# 20. 监控体系

Industrial Platform 自带：

`OperationsCenter.Service` 的 ServerMonitor 模块

结合：

```
Prometheus

+

Grafana

+

Serilog

+

OpenTelemetry


```

---

# 监控指标

## 服务指标

```
CPU

Memory

Request Count

Latency

Error Rate

```

---

## MES指标

```
当前生产批次

设备状态

生产效率

异常次数

```

---

## IoT指标

```
采集频率

通讯异常

数据延迟


```

---

# 21. 日志体系

架构：

```
Application


 |

Serilog


 |

Loki / Seq


 |

Grafana


```

日志分类：

```
Business Log


Audit Log


Exception Log


Device Log


```

---

# 22. CI/CD设计

代码：

GitHub

```
Developer


 |

Git Push


 |

GitHub Actions


 |

Build


 |

Test


 |

Docker Build


 |

Registry


 |

Deploy


```

---

# 23. GitHub目录规划

最终：

```
IndustrialPlatform


├── docs


│
├── src


│
├── tests


│
├── docker


│
├── deploy


│
├── scripts


│
├── .github


│
└── .codex


```

---

# 24. .codex工程辅助设计

目录：

```
.codex


├── architecture.md


├── coding-rule.md


├── domain-rule.md


├── database-rule.md


├── api-rule.md


├── task-template.md


```

---

# 25. Codex开发流程

标准流程：

```
需求文档


    ↓


领域设计


    ↓


生成Solution


    ↓


生成Domain


    ↓


生成Application


    ↓


生成Infrastructure


    ↓


生成API


    ↓


生成测试


    ↓


Code Review


```

---

# 26. 测试体系

目录：

```
tests


├── UnitTests


├── IntegrationTests


├── ApiTests


└── PerformanceTests


```

---

# 27. 工业现场部署方案

推荐：

```
Edge Server


Windows Server 2022


+

Docker


+

IoT Collector


+

MES Gateway



```

规格：

最低：

```
CPU 8 Core

Memory 16GB

SSD 512GB

```

推荐：

```
CPU 16 Core

Memory 32GB

SSD 1TB


```

---

# 28. 高可用设计

核心服务：

```
API

Gateway

Identity


```

至少：

```
2 replicas


```

数据库：

```
Primary

Replica


```

---

# 29. 数据备份

策略：

数据库：

每日全备

每小时增量

对象：

MinIO同步

配置：

Git保存

---

# 30. 灾备设计

RPO：

≤15分钟

RTO：

≤2小时

方案：

```
生产中心


        ↓


备份中心


```

---

# 31. MVP部署范围

第一阶段：

不引入K8S。

采用：

```
Docker Compose


+

单服务器


+

PostgreSQL


+

Redis


+

RabbitMQ


+

MinIO


```

原因：

个人开发阶段：

优先：

```
产品验证

功能沉淀

快速迭代

```

---

# 32. 后续演进路线

## Phase 1

个人产品开发：

```
Docker Compose


```

---

## Phase 2

项目交付：

```
Docker + Linux Server


```

---

## Phase 3

集团工厂：

```
Kubernetes


```

---

## Phase 4

SaaS：

```
Multi Tenant Kubernetes


```

---

# 总结

Industrial Platform部署架构核心思想：

```
开发环境

Docker Compose


        ↓


项目现场

Linux/Docker


        ↓


大型企业

Kubernetes


        ↓


SaaS云平台

Multi Tenant Cloud


```

最终形成：

```
工业现场
    |
边缘计算
    |
MES微服务
    |
工业数据平台
    |
AI智能应用

```
