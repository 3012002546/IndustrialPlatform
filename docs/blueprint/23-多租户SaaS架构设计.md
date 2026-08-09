# 23-多租户SaaS架构设计

> Industrial Platform
> 工业数字化执行平台
>
> Multi Tenant SaaS Architecture Design
>
> 版本：v1.0

---

# 1. 平台定位

Industrial Platform最终目标不是：

> 一个工厂项目MES系统

而是：

> 面向制造企业的工业数字化SaaS平台。

支持：

* 单工厂部署
* 集团多工厂
* 多企业租户
* 云端工业平台

演进路线：

```text
项目MES

    ↓

产品化MES

    ↓

工业数字化平台

    ↓

Industrial SaaS Platform

```

---

# 2. 为什么需要多租户设计

传统MES：

```text
一个客户

    |

一套系统

    |

一个数据库

```

问题：

* 代码无法复用
* 升级困难
* 运维成本高
* 无法形成产品

SaaS：

```text
Industrial Platform


        |

 ----------------------------


 Tenant A

 Tenant B

 Tenant C


 ----------------------------


共享平台能力

独立业务数据


```

---

# 3. 多租户目标

支持：

## 企业级隔离

例如：

```text
集团总部

    |

----------------

A工厂

B工厂

C工厂

----------------

```

---

## SaaS租户

例如：

```text
客户A

账号100人

生产基地3个


客户B

账号50人

生产基地1个

```

---

# 4. 多租户架构模式

常见三种：

---

# 模式一

## Database Per Tenant

每个租户独立数据库。

结构：

```text
Tenant A

industrial_a


Tenant B

industrial_b


Tenant C

industrial_c

```

优点：

* 数据隔离最高
* 安全性最好

缺点：

* 数据库数量多

适合：

大型制造集团。

---

# 模式二

## Schema Per Tenant

一个数据库：

多个Schema。

```text
PostgreSQL


tenant_a


tenant_b


tenant_c


```

优点：

* 管理简单

缺点：

* 扩展有限

---

# 模式三

## Shared Database

共享表：

增加：

```sql
tenant_id

```

例如：

```sql
work_order


id

tenant_id

order_no

```

优点：

* 成本最低
* SaaS最佳

缺点：

* 隔离要求高

---

# 5. Industrial Platform推荐方案

采用：

## 混合模式

```text
普通客户

↓

Shared Database


集团客户

↓

Database Per Tenant


```

---

# 6. Tenant领域模型

新增服务：

```text
Tenant Service

```

负责：

* 租户管理
* 企业信息
* 工厂组织
* 套餐
* 资源限制

---

# 7. Tenant Service架构

目录：

```text
src/services/Tenant


Industrial.Tenant.Api


Industrial.Tenant.Application


Industrial.Tenant.Domain


Industrial.Tenant.Infrastructure


Industrial.Tenant.Contracts


```

---

# 8. Tenant领域模型

## Tenant

租户。

```csharp
public class Tenant
{

Guid Id;


string Code;


string Name;


TenantStatus Status;


TenantPlan Plan;


DateTimeOffset ExpireTime;


}

```

---

# 状态：

```text
Trial

Active

Suspended

Expired

Disabled

```

---

# 9. Tenant结构模型

```text
Tenant


 |

Organization


 |

Factory


 |

Workshop


 |

Line


 |

Equipment


```

---

# 10. Factory模型

工厂。

```csharp
public class Factory
{

Guid Id;


Guid TenantId;


string Code;


string Name;


string Location;


}

```

---

# 11. Tenant Context设计

所有请求：

必须携带：

```text
TenantId

```

来源：

JWT。

---

JWT：

```json
{
"userId":"001",

"tenantId":"TENANT001",

"roles":[

"Manager"

]

}

```

---

# 12. .NET实现

创建：

```csharp
public interface ITenantContext
{

Guid TenantId {get;}

}

```

---

实现：

```csharp
public class TenantContext
    : ITenantContext
{

public Guid TenantId {get;set;}

}

```

---

# 13. 请求流程

```text
User


 |

Login


 |

Identity Service


 |

JWT


 |

API Gateway


 |

Tenant Middleware


 |

Service


 |

Database Filter


```

---

# 14. 数据隔离设计

所有业务表：

增加：

```sql
tenant_id uuid not null

```

例如：

work_order:

```sql
CREATE TABLE work_order
(

id uuid,


tenant_id uuid,


order_no varchar(50)


)

```

---

# 15. ORM自动隔离

SqlSugar支持：

全局过滤。

例如：

```csharp
QueryFilter.AddTableFilter<WorkOrder>(
x=>x.TenantId==tenantId
);

```

---

效果：

业务代码：

```csharp
db.Queryable<WorkOrder>()
.ToList();

```

自动：

```sql
WHERE tenant_id='xxx'

```

---

# 16. 数据权限模型升级

原：

```text
User

 |

Role

 |

Permission

```

升级：

```text
Tenant


 |

Organization


 |

Role


 |

User


 |

Permission


```

---

# 17. 权限模型

支持：

## 功能权限

例如：

```text
查看批记录

审核批记录

发布批记录

```

---

## 数据权限

例如：

用户：

只能看：

```text
一号工厂

```

---

# 18. SaaS资源模型

Tenant Plan。

例如：

```text
Basic


Professional


Enterprise


```

---

套餐限制：

```text
用户数量

设备数量

数据存储

API调用

```

---

# 19. Redis设计

## Tenant缓存

Key：

```text
tenant:{id}

```

保存：

```json
{
"name":"xxx",

"plan":"Enterprise",

"status":"Active"

}

```

---

## 用户租户缓存

```text
user:tenant:{userId}

```

---

# 20. RabbitMQ租户事件

Exchange：

```text
industrial.tenant

```

---

## TenantCreated

Routing:

```text
tenant.created

```

Payload：

```json
{
"tenantId":"",

"name":""

}

```

---

## TenantDisabled

```text
tenant.disabled

```

---

# 21. API设计

Base：

```text
/api/tenant

```

---

## 创建租户

POST

```text
/create

```

---

## 查询租户

GET

```text
/{id}

```

---

## 创建工厂

POST

```text
/factory/create

```

---

# 22. SaaS管理后台

新增：

```text
SaaS Admin Portal

```

功能：

* 租户管理
* 用户管理
* 套餐管理
* 使用量统计
* 系统配置

---

# 23. 使用量统计

统计：

## 用户

```text
当前用户数

```

---

## 设备

```text
在线设备数量

```

---

## 数据

```text
每天数据量

```

---

## API

```text
调用次数

```

---

# 24. 计费模型设计

未来支持：

## 按用户

```text
100用户/月

```

---

## 按设备

```text
100台设备/月

```

---

## 按数据量

```text
1TB/月

```

---

## 按功能模块

例如：

MES：

基础版

IoT：

高级版

AI：

企业版

---

# 25. 多工厂场景

集团：

```text
Tenant:

某集团


Factory:

上海工厂

江苏工厂

广东工厂


```

---

数据：

```text
总部

查看所有


工厂管理员

查看自己的


班组

查看自己的区域


```

---

# 26. 与已有服务融合

## Identity

增加：

TenantId。

---

## ReferenceData

支持租户级字典、配置、元数据与编码规则。

---

## MasterData

支持：

租户物料。

---

## WorkOrder

增加：

TenantId

FactoryId

---

## IoT Collector

设备绑定：

```text
Tenant

Factory

Equipment

```

---

## Batch Record

批记录：

```text
Tenant隔离

```

---

# 27. 部署架构

SaaS：

```text

                 Cloud


              Kubernetes


                  |


        ----------------------


        Tenant A


        Tenant B


        Tenant C


        ----------------------


                  |


             Edge Gateway


                  |


              Factory


```

---

# 28. SaaS安全设计

包括：

* 租户隔离
* 数据权限
* API限流
* 审计日志
* 加密存储

---

# 29. 数据备份

租户级：

支持：

```text
Backup Tenant A

Restore Tenant A

```

---

# 30. MVP范围

第一阶段：

实现：

* Tenant模型
* TenantId隔离
* JWT租户识别
* 数据过滤
* 基础租户管理

---

第二阶段：

增加：

* SaaS后台
* 套餐管理
* 使用统计

---

第三阶段：

增加：

* 商业计费
* 自动开通
* 云部署

---

# 31. Codex任务拆分

## Task-01

创建Tenant Service

生成：

```text
Domain

Application

Infrastructure

Api

```

---

## Task-02

实现Tenant Context

包含：

```text
JWT TenantId

Middleware

DI注入

```

---

## Task-03

数据库租户改造

所有核心表：

增加：

```sql
tenant_id

```

---

## Task-04

SqlSugar全局过滤

实现：

```text
自动租户隔离

```

---

## Task-05

Identity集成

实现：

```text
用户-租户关系

```

---

## Task-06

SaaS Admin

Vue实现：

```text
租户管理

套餐管理

使用统计

```

---

# 32. 后续扩展能力

## 国际化

支持：

```text
中文

英文

日文

```

---

## 多币种

支持：

```text
USD

CNY

EUR

```

---

## 全球部署

未来：

```text
Asia Region

Europe Region

America Region

```

---

# 33. Industrial Platform最终商业模型

演进：

```text
MES项目交付


        ↓


标准化MES产品


        ↓


工业数字化SaaS


        ↓


工业操作系统


```

---

# 34. 完整架构汇总

当前：

```text
Identity Service

        |

Tenant Service

        |

ReferenceData

        |

MasterData

        |

OperationalData

        |

Planning

        |

WorkOrder

        |

Weighting

        |

IoT Collector

        |

Trace

        |

Batch Record

        |

Industrial Data Platform

        |

Low Code Platform

        |

AI Platform


```

---

# 总结

多租户架构让 Industrial Platform 从：

> 工程项目

升级为：

> 可复制、可交付、可商业化的软件产品。

核心能力：

```text
DDD业务模型

+
微服务

+
工业数据平台

+
低代码配置

+
SaaS租户

+
AI能力


=

Industrial Platform

```
