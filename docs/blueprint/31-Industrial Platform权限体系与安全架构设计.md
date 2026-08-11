# 31-Industrial Platform权限体系与安全架构设计

版本：V1.0
项目：Industrial Platform
定位：工业数字化执行平台

---

# SystemData 数据库编排安全补充

- SystemData 普通运行连接与 provisioning 管理凭据使用不同身份、不同权限和不同 Secret；普通连接不能创建数据库、角色或跨库授权。
- provisioning Secret 只能由 Secret Provider、容器/Kubernetes Secret 或受控环境注入，不写数据库、不经 API 返回、不记录日志/Trace/审计。
- registration/query、plan、apply/provision 和 operation status 分别授权；查询和计划允许受信服务按自身 `ServiceKey` 最小访问，apply 仅平台管理员或获明确授权的发布服务执行。
- 生产 apply 必须校验已批准 plan、备份登记、幂等键和目标环境；服务身份不得通过篡改 manifest 提升数据库权限。
- SystemData 为每个服务授予最小业务数据库角色，业务服务不得持有管理员凭据，也不得在 SystemData 不可用时自行建库。
- 所有调用执行认证、授权、限流、审计和脱敏，完整边界读取蓝图 33。

# 1. 文档目标

本文档设计 Industrial Platform 企业级权限、安全、身份认证体系。

目标：

* 支撑工业企业多组织、多工厂、多车间应用
* 支撑SaaS多租户模式
* 支撑MES复杂数据权限
* 支撑设备、生产、质量安全隔离
* 支撑微服务安全通信
* 满足工业软件审计要求
* 支撑未来国际化部署

Industrial Platform权限不是简单后台管理权限。

传统：

```
用户
 |
角色
 |
菜单
```

无法满足MES。

工业场景：

```
用户

 |
组织

 |
工厂

 |
车间

 |
产线

 |
设备

 |
业务对象

 |
数据范围

 |
操作权限
```

---

# 2. 安全总体架构

整体：

```
                 Client

                   |
                   |

          API Gateway

                   |

        Identity Service

                   |

    ----------------------------

    |            |             |

 RBAC          ABAC        Data Permission


    |

Micro Services


    |

Audit Service

```

---

# 3. 安全技术栈

## Backend

| 能力   | 技术            |
| ---- | ------------- |
| 认证   | JWT           |
| 授权   | RBAC+ABAC     |
| 协议   | OAuth2/OIDC   |
| 密码   | PBKDF2/Argon2 |
| 密钥   | Vault         |
| 网关   | YARP          |
| 安全审计 | Audit Service |
| 传输   | HTTPS/TLS     |

---

# 4. Identity Service定位

Identity Service负责：

* 用户认证
* Token签发
* 权限管理
* 角色管理
* 组织管理
* 租户管理
* 登录审计

架构：

```
Identity Service


├── Authentication

├── Authorization

├── Tenant

├── Organization

├── User

├── Role

├── Permission

└── Security Audit

```

---

# 5. 用户模型设计

## User

表：

```
sys_user
```

字段：

```sql
Id

TenantId

UserName

PasswordHash

RealName

Email

Phone

Status

LastLoginTime

CreateTime

```

---

# 6. 组织模型设计

工业企业：

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
设备

```

模型：

```
Organization


Id

ParentId

Type

Name


```

---

类型：

```csharp
public enum OrganizationType
{

Group,

Company,

Factory,

Workshop,

Line,

EquipmentArea

}

```

---

# 7. RBAC权限模型

基础权限模型：

```
User

 |

Role

 |

Permission

```

---

## 核心表

### Role

```
sys_role

```

字段：

```
Id

TenantId

Name

Code

```

---

### Permission

```
sys_permission

```

字段：

```
Id

Code

Name

Type

```

---

类型：

```
Menu

Button

API

Data

```

---

# 8. 权限编码规范

统一：

```
模块:资源:动作


```

例如：

工单：

```
workorder:view

workorder:create

workorder:release

workorder:close

```

称量：

```
weighting:start

weighting:confirm

weighting:cancel

```

---

# 9. ABAC动态权限模型

RBAC不足。

例如：

操作员：

只能操作：

```
自己的车间

自己的设备

自己的班次

```

需要：

Attribute Based Access Control。

---

模型：

```
Subject

+

Action

+

Resource

+

Environment


```

---

例：

用户：

```
Role=Operator

Workshop=A


```

资源：

```
WorkOrder

Workshop=A

```

允许。

---

# 10. 数据权限设计

MES重点。

数据权限：

```
用户

 |
数据范围

 |
业务数据

```

---

类型：

## 全部

```
ALL

```

---

## 工厂

```
Factory=001

```

---

## 车间

```
Workshop=Mixing

```

---

## 自定义

```
SQL Rule

```

---

# 11. 数据权限实现

方案：

Service统一拦截。

例如：

查询工单：

原：

```sql
select *
from work_order

```

自动增加：

```sql
where factory_id='F001'

```

---

实现：

```
IDataPermissionFilter


        |

QueryInterceptor


        |

Repository


```

---

# 12. 字段权限设计

工业场景：

不同岗位看到不同字段。

例如：

生产员：

```
物料编码

数量

状态

```

质量：

```
检验结果

标准

偏差

```

---

模型：

```
PermissionField


Table

Field

Role

Visible

Editable


```

---

# 13. API安全设计

所有API：

必须：

```
HTTPS

+

JWT

+

Permission Check

```

---

Controller：

```csharp
[Authorize]

[Permission(
"workorder:create")]

public IActionResult Create()

```

---

# 14. JWT设计

Payload：

```json
{

"userId":"001",

"tenantId":"T001",

"roles":[
"Operator"
],

"permissions":[

"workorder:create"

]

}

```

---

# 15. Refresh Token设计

原因：

工业现场：

长时间登录。

流程：

```
Access Token

15-30分钟


Refresh Token

7-30天

```

---

表：

```
sys_refresh_token

```

字段：

```
UserId

Token

ExpireTime

Device

IP

```

---

# 16. OAuth2 / OpenID Connect

未来支持：

企业统一认证。

支持：

* Azure AD
* LDAP
* AD域
* Keycloak

流程：

```
Enterprise Identity

        |

Identity Service

        |

Industrial Platform

```

---

# 17. 多租户安全设计

对应：

23-多租户SaaS架构。

隔离：

## 数据隔离

方式：

```
TenantId字段

```

---

所有业务表：

必须包含：

```sql
TenantId

```

例如：

```
work_order

Id

TenantId

OrderNo

```

---

# 18. 微服务间认证

服务调用：

不能直接信任。

采用：

Service Identity。

例如：

```
WorkOrder Service


        |

Client Credential


        |

Batch Service

```

---

Token：

```
service_token

```

---

# 19. API Gateway安全

架构：

```
Client

 |

YARP Gateway

 |

Service

```

负责：

* Token验证
* 限流
* 黑名单
* IP限制
* 请求日志

---

# 20. 防护策略

## 登录保护

包括：

* 密码错误次数限制
* 验证码
* MFA

---

## API保护

包括：

* Rate Limit
* 防重放
* 请求签名

---

# 21. 工业现场安全

MES特殊。

设备：

```
PLC

仪表

机器人

```

不能直接暴露。

架构：

```
设备网络


 |

Edge Gateway


 |

Industrial Platform


```

---

# 22. 操作审计

所有关键操作：

必须记录：

```
谁

什么时候

什么地点

修改什么

修改前

修改后

```

---

例如：

修改配方：

```
User:

Operator01


Action:

Recipe Update


Before:

Temperature=80


After:

Temperature=85

```

---

# 23. 安全事件管理

记录：

```
security_event


```

类型：

```
LoginFailed

PermissionDenied

TokenExpired

ApiAttack

```

---

# 24. 密钥管理

禁止：

代码写：

```csharp
string key="123456";

```

---

采用：

```
Environment Variable

+

Vault

+

Kubernetes Secret

```

---

# 25. 数据加密

敏感数据：

例如：

* 密码
* Token
* API Key

采用：

AES256。

---

数据库：

```
Encrypted Column

```

---

# 26. 安全开发规范

禁止：

## SQL拼接

错误：

```csharp
sql =
"select * from user where id="
+id;

```

---

推荐：

ORM参数化。

---

禁止：

日志输出密码：

错误：

```
Password=123456

```

---

# 27. 前端安全规范

Vue3：

必须：

* Token安全存储
* XSS防护
* CSRF防护
* 路由权限
* 按钮权限

---

按钮：

```vue
<Permission
code="workorder:create">

<Button>

创建工单

</Button>

</Permission>

```

---

# 28. AI Agent安全体系

AI助手特殊。

必须控制：

```
User

 |

AI Agent

 |

Tool

 |

MES API

```

---

权限继承：

用户：

只能查询：

```
自己的工厂

```

AI不能突破。

---

例如：

用户：

```
查询所有工厂生产

```

AI：

拒绝。

---

# 29. RAG安全

工业知识库：

必须权限过滤。

流程：

```
Question

 |

Permission Filter

 |

Vector Search

 |

LLM

```

---

禁止：

跨租户知识泄漏。

---

# 30. CI/CD安全扫描

Pipeline增加：

```
Code

 |

Build

 |

Unit Test

 |

Security Scan

 |

Deploy

```

---

工具：

* SonarQube
* Trivy
* Dependabot

---

# 31. MVP安全范围

第一阶段：

必须完成：

## Identity

√ 用户

√ 角色

√ 权限

## API

√ JWT

√ Refresh Token

## 数据

√ TenantId

√ 基础数据权限

## 审计

√ 登录日志

√ 操作日志

---

# 32. 后续扩展

未来：

## 零信任架构

```
Never Trust

Always Verify

```

---

## 生物识别

支持：

* 指纹
* 人脸
* 工牌

---

## 工业安全态势感知

AI分析：

```
异常登录

异常操作

异常设备行为

```

---

# 33. Codex生成规范

生成任何Service：

必须包含：

```
Security

├── Authorization

├── Permission

├── Audit

├── Tenant Isolation

└── Authentication

```

Prompt：

```
请生成 Industrial Platform
XXX Service。

要求：

1. 支持JWT认证

2. 支持RBAC

3. 支持ABAC

4. 支持Tenant隔离

5. 支持数据权限

6. 支持Audit日志

7. 支持OpenTelemetry Trace

8. 满足工业MES安全要求

```

---

# 34. 总结

Industrial Platform安全体系：

```
                 Identity


                    |

              Authorization


                    |

        ----------------------

        RBAC

        ABAC

        Data Permission


        ----------------------


                    |

              Business Service


                    |

              Audit System


```

最终目标：

打造：

> 面向大型工业企业、多工厂、多租户、多角色、多设备环境的企业级安全体系。

---
