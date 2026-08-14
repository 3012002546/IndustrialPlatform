# 13-Identity Service详细设计

> 文档版本：v1.0
> 项目名称：Industrial Platform
> 服务名称：Identity Service
> 服务定位：工业数字化平台统一身份认证与权限中心
> 技术基础：
>
> * .NET 10 WebAPI
> * Clean Architecture
> * DDD
> * SqlSugar
> * PostgreSQL
> * Redis
> * JWT
> * RabbitMQ
> * Serilog

---

# 1. 服务定位

Identity Service 是 Industrial Platform 所有业务服务的基础服务。

负责：

```
用户身份
+
组织架构
+
角色权限
+
认证授权
+
操作审计
+
安全策略
```

系统所有业务：

```
MES

设备平台

称量平台

追溯平台

批记录

Server Monitor

低代码平台
```

均依赖 Identity Service。

---

# 2. 服务边界

Identity Service：

负责：

| 能力   | 说明        |
| ---- | --------- |
| 用户管理 | 账号、状态、密码  |
| 用户组管理 | 租户级安全主体；组织账号并通过角色批量授权，不允许嵌套或直接分配权限 |
| 角色管理 | 安全角色；不得等同 SystemData 岗位 |
| 权限管理 | 菜单、API权限  |
| 认证   | 登录、Token  |
| 授权   | RBAC      |
| 审计   | 登录及操作记录   |
| 安全   | 密码策略、登录限制 |

不负责：

```
行政组织、岗位和用户任职（SystemData）

业务数据权限

生产权限

设备权限

工艺权限
```

例如：

MES：

```
张三
是否可以开工单

```

属于：

MES Service

Identity：

```
张三是谁

张三有什么角色
```

---

# 3. 服务架构

整体：

```
                 Vue3

                   |

              Gateway

                   |

          Identity Service


       --------------------

       Domain

       Application

       Infrastructure

       Api


       --------------------


 PostgreSQL

 Redis

 RabbitMQ

```

---

# 4. Solution结构

目录：

```
src/Services/Identity


IndustrialPlatform.Identity.Domain


IndustrialPlatform.Identity.Application


IndustrialPlatform.Identity.Infrastructure


IndustrialPlatform.Identity.Api


tests

IndustrialPlatform.Identity.Tests

```

---

# 5. DDD领域模型设计

核心聚合：

```
User Aggregate

Role Aggregate

Organization Aggregate

Permission Aggregate

```

---

# 6. User用户领域模型

## 6.1 User聚合

用户：

```
User

 |
 |-- UserId

 |-- Account

 |-- Password

 |-- Status

 |-- Organization

 |-- Roles

```

---

领域对象：

```csharp
public class User
    : AggregateRoot<Guid>
{

    public string Account {get;private set;}


    public string PasswordHash {get;private set;}


    public UserStatus Status {get;private set;}


}
```

---

# 7. 用户状态

枚举：

```csharp
public enum UserStatus
{

    Active = 1,


    Disabled = 2,


    Locked = 3


}
```

---

# 8. Organization组织模型

工业企业特点：

支持：

```
集团

 |

公司

 |

工厂

 |

车间

 |

产线

 |

班组

```

树形结构：

```
Organization

    |

 ParentId

```

例如：

```
ABC集团

 └── 上海公司

      └── 一号工厂

            └── 包装车间

```

---

# 9. Role角色模型

角色：

```
管理员

工艺工程师

生产主管

操作员

质量人员

维护人员

```

模型：

```
Role

 |

RolePermission

 |

Permission

```

---

# 10. Permission权限模型

采用：

RBAC

(Role Based Access Control)

模型：

```
User


 |

UserRole


 |

Role


 |

RolePermission


 |

Permission

```

---

# 11. 权限类型设计

支持：

## 菜单权限

例如：

```
生产管理

 └── 工单管理

```

---

## API权限

例如：

```
POST

/api/workorders

```

权限：

```
MES.WorkOrder.Create

```

---

## 数据权限

预留：

例如：

```
FactoryId

WorkshopId

LineId

```

后续由业务服务实现。

---

# 12. 数据库设计

数据库：

```
industrial_identity
```

表：

```
sys_user

sys_role

sys_permission

sys_user_role

sys_role_permission

sys_organization

sys_login_log

sys_operation_log

```

---

# 13. sys_user表

```sql
CREATE TABLE sys_user
(

id uuid PRIMARY KEY,


account varchar(50) NOT NULL,


password_hash varchar(200),


real_name varchar(50),


email varchar(100),


mobile varchar(30),


organization_id uuid,


status int,


created_time timestamptz,


updated_time timestamptz


);

```

---

# 14. sys_role表

```sql
CREATE TABLE sys_role
(

id uuid PRIMARY KEY,


code varchar(50),


name varchar(100),


description varchar(200),


created_time timestamptz


);

```

---

# 15. sys_permission表

```sql
CREATE TABLE sys_permission
(

id uuid PRIMARY KEY,


code varchar(100),


name varchar(100),


type int,


parent_id uuid,


path varchar(200)


);

```

---

# 16. 登录流程设计

流程：

```
用户输入账号密码


        |


Identity Service


        |


验证密码


        |


加载角色权限


        |


生成JWT


        |


返回Token


```

---

# 17. JWT设计

Token内容：

```json
{

"userId":"xxx",

"account":"admin",

"roles":[
"MES_ADMIN"
],

"permissions":[

"MES.WorkOrder.Create"

]

}

```

---

# 18. Access Token

有效期：

建议：

```
30分钟
```

---

# 19. Refresh Token

设计：

```
Access Token

30分钟


Refresh Token

7天

```

存储：

Redis

结构：

```
refresh_token:{userid}

{

token:"xxx",

expire:""

}

```

---

# 20. Redis设计

Key规范：

## 用户缓存

```
identity:user:{id}

```

---

## 权限缓存

```
identity:permission:{userid}

```

---

## Token

```
identity:refresh:{userid}

```

---

# 21. 登录接口设计

## 登录

POST

```
/api/auth/login

```

Request:

```json
{

"account":"<loginName>",

"password":"<由Secret Provider或一次性引导提供，禁止写入文档/代码/配置/日志>"

}

```

Response:

```json
{

"accessToken":"xxx",

"refreshToken":"xxx",

"expire":1800

}

```

---

# 22. 刷新Token

POST

```
/api/auth/refresh

```

请求：

```json
{

"refreshToken":"xxx"

}

```

---

# 23. 当前用户

GET

```
/api/auth/me

```

返回：

```json
{

"id":"",

"name":"管理员",

"permissions":[]

}

```

---

# 24. 用户管理API

## 用户列表

```
GET

/api/users

```

---

## 创建用户

```
POST

/api/users

```

---

## 修改状态

```
PUT

/api/users/{id}/status

```

---

# 25. 权限校验设计

业务服务：

Controller：

```csharp
[Permission(
"MES.WorkOrder.Create"
)]
public async Task Create()
{

}

```

---

实现：

Middleware读取：

```
JWT

 |

Permission Claim

 |

Authorize

```

---

# 26. Permission缓存机制

登录：

```
查询数据库

↓

生成权限集合

↓

缓存Redis

↓

写入JWT

```

---

运行：

```
API请求

↓

Gateway

↓

JWT

↓

Permission

↓

业务服务

```

---

# 27. 操作审计设计

所有重要操作记录：

例如：

```
创建用户

修改权限

删除角色

登录失败

```

表：

```
sys_operation_log
```

字段：

```
user_id

operation

module

ip

request

result

time

```

---

# 28. 登录审计

表：

```
sys_login_log
```

记录：

```
用户

时间

IP

设备

成功失败

原因

```

---

# 29. RabbitMQ事件设计

Identity发布：

## 用户创建事件

```
UserCreatedIntegrationEvent

```

消息：

```json
{

"userId":"xxx",

"account":"zhangsan"

}

```

消费者：

```
MES

Trace

Batch

```

---

# 30. Event场景

用户禁用：

```
UserDisabledEvent


↓

MES清理缓存


↓

Gateway刷新权限

```

---

# 31. 安全设计

## 密码

禁止明文。

采用：

```
BCrypt

```

---

## 登录限制

支持：

```
失败次数

锁定时间

IP限制

```

---

# 32. 多租户设计预留

工业软件通常：

一个平台：

多个企业

因此预留：

```
TenantId

```

所有核心表：

增加：

```
tenant_id

```

例如：

```
sys_user

tenant_id

```

---

# 33. 多工厂模型

最终：

```
Tenant


 |

Organization


 |

Factory


 |

Workshop


 |

Line

```

---

# 34. Identity Service配置

appsettings:

```json
{

"Jwt":

{

"Secret":"",

"ExpireMinutes":30

},


"Redis":

{

"Connection":""

}


}

```

---

# 35. 第一版MVP范围

必须完成：

## 用户

√ 创建

√ 修改

√ 禁用

---

## 登录

√ JWT

√ RefreshToken

---

## 权限

√ RBAC

√ API权限

---

## 审计

√ 登录日志

√ 操作日志

---

# 36. Codex开发任务拆分

建议拆分：

## Task 01

创建Identity Service工程

输出：

```
Domain

Application

Infrastructure

Api

```

---

## Task 02

数据库初始化

生成：

```
Migration

Seed Data

```

---

## Task 03

用户领域模型

实现：

```
User Aggregate

Repository

Service

```

---

## Task 04

JWT认证

实现：

```
Login

Token

Refresh

```

---

## Task 05

RBAC权限

实现：

```
Role

Permission

Authorize
```

---

## Task 06

审计

实现：

```
LoginLog

OperationLog

```

---

# 37. Identity Service完成标准

最终运行：

```
Browser

 |

Gateway

 |

Identity API


```

可以完成：

```
管理员登录


创建用户


分配角色


分配权限


访问受保护API

```

---

# 38. 后续服务依赖关系

Identity完成后：

```
                 Identity

                    |

              ReferenceData

                    |

               MasterData

                    |

            OperationalData

                    |

     --------------------------------

     |              |              |

    MES            IoT        Weighting


```

所有业务服务统一：

```
JWT认证

用户上下文

权限体系

审计体系

```

---

#
