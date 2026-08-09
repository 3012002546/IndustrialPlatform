# 03-Industrial Platform Identity Service开发实施方案

# Industrial Platform Identity Service开发实施方案

> 当前里程碑范围：仅创建项目骨架；业务实现留待后续阶段。

版本：V1.0
阶段：Development Implementation Phase

服务：

Identity Service

技术：

.NET 10 WebAPI

DDD

Clean Architecture

SqlSugar

PostgreSQL

Redis

JWT

---

# 1. 文档说明

## 1.1 服务定位

Identity Service 是 Industrial Platform 平台基础服务。

负责：

* 用户认证
* 用户管理
* 角色管理
* 权限管理
* 组织管理
* Token管理

所有业务服务依赖 Identity Service 提供身份能力。

依赖关系：

```text
User

↓

Role

↓

Permission

↓

Service Authorization
```

---

# 2. 服务职责边界

## Identity Service负责

包含：

* 用户登录
* JWT Token生成
* Refresh Token
* 用户信息
* 角色授权
* 权限验证
* 组织架构

---

## Identity Service不负责

不包含：

* MES业务权限
* 工单权限规则
* 设备权限规则

例如：

错误：

```text
Identity Service

判断用户是否可以关闭设备
```

正确：

```text
IoT Service

判断设备业务权限

Identity提供基础授权
```

---

# 3. 项目结构

目录：

```text
src/Services/Identity
```

结构：

```text
Identity


├── Identity.Api


├── Identity.Application


├── Identity.Domain


└── Identity.Infrastructure

```

---

# 4. 创建项目

进入：

```bash
cd src/Services
```

创建Solution项目：

```bash
dotnet new webapi \
-n Identity.Api
```

创建类库：

```bash
dotnet new classlib \
-n Identity.Application
```

```bash
dotnet new classlib \
-n Identity.Domain
```

```bash
dotnet new classlib \
-n Identity.Infrastructure
```

---

# 5. 项目引用关系

必须保持：

```text
Identity.Api

↓

Identity.Application

↓

Identity.Domain


Identity.Infrastructure

↓

Identity.Domain
```

禁止：

```text
Domain

引用

Infrastructure
```

---

# 6. Domain领域设计

目录：

```text
Identity.Domain


├── Entities


├── Aggregates


├── ValueObjects


├── Events


└── Enums
```

---

# 7. 用户聚合设计

核心聚合：

User

关系：

```text
User

|

├── UserRole

|

Role

|

Permission
```

---

# 8. User实体

文件：

```text
Entities/User.cs
```

代码：

```csharp
public class User : AggregateRoot
{
    public string UserName { get; private set; }

    public string PasswordHash { get; private set; }

    public string Email { get; private set; }


    private readonly List<UserRole> _roles = new();


    public IReadOnlyCollection<UserRole> Roles
        => _roles;


    private User()
    {

    }


    public User(
        string userName,
        string passwordHash)
    {
        UserName = userName;

        PasswordHash = passwordHash;
    }
}
```

---

# 9. Role实体

文件：

```text
Entities/Role.cs
```

模型：

```csharp
public class Role : AggregateRoot
{

    public string Name {get;private set;}


    private readonly List<RolePermission>
        _permissions=new();


}
```

---

# 10. Permission实体

权限模型：

```text
Permission


Id

Code

Name

Type
```

例如：

```text
system.user.create

system.user.delete

workorder.view
```

---

# 11. ValueObject设计

## Password

密码禁止明文保存。

模型：

```text
PasswordHash
```

规则：

* BCrypt
* Salt

---

# 12. Domain Event设计

用户创建事件：

```text
UserCreatedEvent
```

用途：

通知：

* 审计服务
* 消息中心

---

# 13. 数据库设计

数据库：

```text
industrial_identity
```

---

# 14. 用户表

表：

```sql
sys_user
```

字段：

```text
Id

UserName

PasswordHash

Email

Phone

Status

CreateTime

ModifyTime

IsDeleted
```

---

# 15. 角色表

```sql
sys_role
```

字段：

```text
Id

Name

Code

Description

CreateTime
```

---

# 16. 权限表

```sql
sys_permission
```

字段：

```text
Id

Code

Name

Type

ParentId
```

---

# 17. 用户角色关系

```sql
sys_user_role
```

字段：

```text
UserId

RoleId
```

---

# 18. 角色权限关系

```sql
sys_role_permission
```

字段：

```text
RoleId

PermissionId
```

---

# 19. Refresh Token表

```sql
sys_refresh_token
```

字段：

```text
Id

UserId

Token

ExpireTime

CreateTime

Revoked
```

---

# 20. Application层设计

目录：

```text
Identity.Application


├── Commands

├── Queries

├── DTOs

├── Services

└── Validators
```

---

# 21. 登录功能设计

Command：

```text
LoginCommand
```

输入：

```json
{
 "username":"admin",
 "password":"123456"
}
```

---

处理流程：

```text
Controller

↓

LoginCommand

↓

UserRepository

↓

Password验证

↓

生成JWT

↓

返回Token
```

---

# 22. LoginResponse

返回：

```json
{
 "accessToken":"",
 "refreshToken":"",
 "expireSeconds":3600
}
```

---

# 23. JWT设计

Payload：

```json
{
 "sub":"10001",

 "username":"admin",

 "roles":[
   "admin"
 ],

 "permissions":[
   "system.user.create"
 ]
}
```

---

# 24. API设计

Base：

```text
/api/identity
```

---

## 登录

接口：

```http
POST

/api/identity/auth/login
```

请求：

```json
{
 "username":"admin",
 "password":"123456"
}
```

响应：

```json
{
 "success":true,

 "data":
 {
  "token":"xxx"
 }
}
```

---

## 刷新Token

```http
POST

/api/identity/auth/refresh
```

---

## 当前用户

```http
GET

/api/identity/users/current
```

---

# 25. Infrastructure实现

目录：

```text
Identity.Infrastructure


├── Persistence

├── Repository

├── Authentication

└── Services
```

---

# 26. SqlSugar配置

DbContext：

```csharp
public class IdentityDbContext
{

public SqlSugarClient Db {get;}

}
```

---

# 27. Repository实现

接口：

```csharp
IUserRepository
```

实现：

```text
UserRepository
```

负责：

* 用户查询
* 用户保存

---

# 28. 密码服务

接口：

```csharp
IPasswordHasher
```

实现：

```text
BCryptPasswordHasher
```

---

# 29. JWT服务

接口：

```csharp
ITokenService
```

实现：

```text
JwtTokenService
```

职责：

* 创建Token
* 验证Token

---

# 30. Redis使用场景

Identity Redis：

用于：

## RefreshToken缓存

Key：

```text
identity:refresh:{userid}
```

---

## 登录状态

Key：

```text
identity:user:{userid}
```

---

# 31. RabbitMQ事件

发布事件：

## 用户创建

```text
UserCreatedEvent
```

## 用户禁用

```text
UserDisabledEvent
```

---

# 32. Controller设计

目录：

```text
Identity.Api.Controllers
```

包含：

```text
AuthController

UsersController

RolesController

PermissionController
```

---

# 33. 权限验证

实现：

```csharp
[Permission("system.user.create")]
```

使用：

Middleware + Attribute

---

# 34. 初始化管理员

系统启动初始化：

创建：

用户：

```text
admin
```

角色：

```text
Administrator
```

权限：

全部。

---

# 35. 单元测试

目录：

```text
tests/Identity/IndustrialPlatform.Identity.Tests
```

测试：

## User

* 创建用户
* 修改密码
* 禁用用户

## Login

* 正确密码
* 错误密码

## Permission

* 权限判断

---

# 36. 开发任务拆分

## Task-001 创建Identity工程

内容：

* 创建四层项目
* 添加引用

验收：

Build成功。

---

## Task-002 创建数据库

内容：

* 创建identity数据库
* 创建表结构

验收：

数据库初始化成功。

---

## Task-003 Domain实现

内容：

* User
* Role
* Permission

验收：

领域测试通过。

---

## Task-004 登录功能

实现：

* Login API
* JWT

验收：

获取Token成功。

---

## Task-005 权限系统

实现：

* RBAC
* Permission Attribute

验收：

接口权限控制成功。

---

## Task-006 Redis集成

实现：

* Token缓存

---

## Task-007 RabbitMQ事件

实现：

用户事件发布。

---

# 37. Identity Service完成标准

完成后：

系统具备：

```text
用户登录

↓

JWT认证

↓

权限验证

↓

用户管理

↓

角色管理

↓

权限管理
```

并可以支撑后续：

ReferenceData Service

MasterData Service

OperationalData Service

WorkOrder Service

Weighting Service

---
