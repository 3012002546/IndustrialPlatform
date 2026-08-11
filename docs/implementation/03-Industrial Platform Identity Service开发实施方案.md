# 03-Industrial Platform Identity Service开发实施方案

# Industrial Platform Identity Service开发实施方案

> 当前里程碑范围：完成 Identity 本地认证、客户 OIDC/SAML 联邦单点登录、用户/角色/权限管理、统一前端接入及看板/追溯类独立页面认证恢复，形成可供后续业务服务复用的身份与授权闭环；本阶段不实现看板、追溯业务页面本身。

版本：V2.1

阶段状态：已暂停。`TASK-ID-001～006` 已完成，暂停点为 `TASK-ID-007`；恢复前不得继续派遣 `TASK-ID-007～016`，暂停不表示 PF-00 已完成。

所属项目开发路线阶段：Phase 3「Identity 服务 + 页面」；前置为 Phase 2「统一前端第一批」，完成后向 Phase 4「ReferenceData 服务 + 页面」交付身份、权限和 SSO 契约。阶段定义见《01-Industrial Platform开发启动实施方案》第 2 节“开发阶段总体规划”。

服务：

```text
Identity Service
```

技术：

```text
.NET 10 WebAPI
Clean Architecture
DDD
SqlSugar
PostgreSQL
Redis
JWT
RabbitMQ
Vue 3
```

规格与蓝图依据：

- `docs/blueprint/07-PostgreSQL数据库规范及分库设计.md`
- `docs/blueprint/08-RabbitMQ事件总线设计规范.md`
- `docs/blueprint/13-Identity Service详细设计.md`
- `docs/blueprint/26-Industrial Platform数据库最终模型.md`
- `docs/blueprint/27-Industrial Platform API规范.md`
- `docs/blueprint/29-Industrial Platform自动化测试体系.md`
- `docs/blueprint/30-Industrial Platform日志审计与可观测性平台设计.md`
- `docs/blueprint/31-Industrial Platform权限体系与安全架构设计.md`
- `docs/implementation/02B-Industrial Platform统一前端第一批开发实施方案.md`
- `docs/superpowers/specs/2026-08-09-runnable-baseline-first-development-sequence-design.md`

---

# 1. 文档说明

## 1.1 文档目的

本文档定义 Identity Service 从现有健康检查骨架发展为真实身份认证与权限中心的完整实施设计，并补齐原方案缺失的前端协作内容。

交付内容覆盖：

- 用户、角色、权限领域模型。
- 密码、登录限制、JWT、Refresh Token、注销和撤销。
- 用户、角色、权限和登录审计 API。
- Redis 权限缓存和会话撤销。
- 操作审计、Outbox 和 Identity 集成事件。
- 客户统一身份平台的 OIDC/SAML 联邦单点登录。
- 看板、追溯等独立页面深链接的免重复登录与原路返回。
- 统一前端 `HttpAuthGateway`、真实会话切换、权限导航和管理页面。
- 前后端契约测试和登录/权限 E2E 验收。

## 1.2 当前输入状态

- Identity 已有 Domain、Application、Infrastructure、Api 四层项目骨架。
- `/health`、`/health/live`、`/health/ready` 和 PostgreSQL/Redis/Seq 健康检查已存在。
- 当前没有用户、角色、权限、数据库迁移、认证或业务 API。
- 当前开发配置仍临时指向 `industrial_platform`；`identity_db` 必须作为 `LogicalDatabaseName`，由 `DatabaseTopology` 解析到 Shared Development 或 PerService 的物理目标。
- BuildingBlocks 已提供 `Guid` 用户标识、`ICurrentUser`、ClaimConstants、统一 ApiResult、异常中间件和日志基础。
- 统一前端第一批完成后已具备登录页、AuthStore、`AuthGateway`、路由守卫和三端布局，但运行时仍使用 Mock。

## 1.3 执行前置

```text
TASK-BASE-001～006
    ↓
TASK-FE-001～010
    ↓
TASK-ID-001～016
```

现有 Identity 骨架和健康检查不等于本阶段完成。

---

# 2. 服务定位与职责边界

## 2.1 Identity负责

- 用户账号、密码和状态。
- 角色定义、用户角色关系。
- 权限目录、角色权限关系。
- 登录、当前用户、Access Token 和 Refresh Token。
- 单会话注销、全部会话注销和令牌撤销。
- 登录失败限制和安全审计。
- 为前端提供用户、角色、权限和登录审计 API。
- 发布用户和权限快照变化事件。
- 管理租户级外部身份提供方、外部账号绑定和平台 SSO 客户端。
- 为平台内独立应用和单页深链接签发一次性登录票据。

## 2.2 Identity不负责

- 工单、库存、设备、称量等业务授权规则。
- 工厂、车间、产线、工作中心等制造组织主数据。
- 业务数据范围的最终过滤。
- 前端业务页面和动态菜单设计器。
- Tenant 生命周期和订阅管理。
- LDAP/AD 直连、MFA、自助账号合并和社交账号登录。
- 充当客户统一身份平台本身；Identity 只作为联邦接入方和平台内部统一会话中心。

边界原则：

```text
Identity回答：用户是谁、具有哪些平台权限

业务服务回答：该用户是否可对当前业务对象执行具体操作
```

Identity 保存不透明 `TenantNId` 并写入用户上下文，但 Tenant 的创建、状态和订阅由未来 Tenant 能力负责。

---

# 3. 前后端协作目标

本阶段不是“后端 API 完成即结束”，而是完成以下纵向闭环：

```text
Vue LoginPage
    ↓
HttpAuthGateway
    ↓
Gateway /identity 前缀
    ↓
Identity API
    ↓
PostgreSQL + Redis
    ↓
AuthSession / CurrentUser
    ↓
路由、菜单和操作权限
```

必须满足：

- 不重写 02B 已完成的登录页、AuthStore、路由守卫和三端布局。
- `HttpAuthGateway` 实现与 `MockAuthGateway` 相同接口并复用契约测试。
- 生产模式不再允许 Mock 登录。
- PC 端增加用户、角色权限、权限目录和登录审计页面。
- 登录页支持租户配置的“企业统一登录”入口和自动跳转策略。
- 看板、追溯等页面可通过安全深链接直接打开，认证后返回原始页面而不是平台首页。
- 401 自动刷新、刷新失败退出、403 页面和权限隐藏形成统一行为。
- 前端隐藏不是安全控制，后端仍必须验证权限。

---

# 4. 服务架构

```text
Vue 3 Unified Frontend
          |
          | /identity/**
          v
Gateway（剥离 /identity）
          |
          v
IndustrialPlatform.Identity.Api
          |
          v
IndustrialPlatform.Identity.Application
          |
          v
IndustrialPlatform.Identity.Domain
          ^
          |
IndustrialPlatform.Identity.Infrastructure
     |           |           |
PostgreSQL     Redis      RabbitMQ/Outbox
```

跨服务规则：

- 其他服务不得直接读取 `identity_db`。
- 对外只通过 JWT 身份声明、版本化 API 契约和集成事件协作。
- Identity 不引用 ReferenceData、MasterData 或业务服务 Infrastructure。

---

# 5. 项目结构与引用关系

现有后端位置：

```text
src/backend/src/Services/Identity
├── IndustrialPlatform.Identity.Api
├── IndustrialPlatform.Identity.Application
├── IndustrialPlatform.Identity.Contracts
├── IndustrialPlatform.Identity.Domain
└── IndustrialPlatform.Identity.Infrastructure
```

目标测试结构：

```text
tests/Identity
├── IndustrialPlatform.Identity.Domain.Tests
├── IndustrialPlatform.Identity.Application.Tests
├── IndustrialPlatform.Identity.Infrastructure.Tests
├── IndustrialPlatform.Identity.Api.Tests
└── IndustrialPlatform.Identity.Contract.Tests
```

前端位置：

```text
src/frontend/src
├── auth/httpAuthGateway.ts
├── api/identity
├── components/permission
├── pages/pc/identity
├── router/identityRoutes.ts
└── stores/identity
```

引用方向：

```text
Api → Application → Domain

Api → Contracts

Application → Contracts

Infrastructure → Application + Domain

Contracts 不引用 Domain、Infrastructure 或 Api
```

Contracts 只承载跨进程 DTO、集成事件和版本信息；禁止放置仓储、数据库映射或领域行为。禁止 Domain 引用 Infrastructure、Api、Contracts 或前端契约。

---

# 6. 全局技术约束

- 数据库内部主键 `Id` 使用 `Guid`；业务身份使用 NId；`TenantNId` 沿用当前用户上下文的不透明非空字符串。
- 所有业务时间使用 `DateTimeOffset`，PostgreSQL 映射 `timestamptz`。
- 所有持久化表统一具备 BuildingBlocks Entity 生命周期：`Id`、`IsFrozen`、`IsLocked`、`IsDeleted`、`EntityType`、`CreatedOn`、`LastUpdatedOn`、`OptimisticVersion`、`ConcurrencyVersion`；字段定义和表清单只列本对象业务字段，不逐表重复公共字段。
- 领域实体自身的稳定业务标识统一使用 `NId`；其他实体或跨服务契约引用时使用 `{EntityName}NId`。Identity 中的 User、Role、Permission、SsoProvider、SsoClient 等均不得以 `Code` 作为实体身份字段；HTTP 错误 `code`、OAuth `authorization_code/client_id` 和已经签发的业务编码不属于实体 NId。
- NId 去除首尾空格，长度 1～128，使用 `^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$`，保存规范化比较值并在所属租户/作用域内大小写不敏感唯一；创建后不可原位修改。Name 必填且最大 256 字符。
- 同库父子表使用子表 `{ParentEntity}_Id + {ParentEntity}_IsDeleted` 复合外键引用父表 `(Id, IsDeleted)`。子表自身 `IsDeleted` 保持独立；父表必须提供可引用唯一键，软删除/恢复通过 `ON UPDATE CASCADE` 或同事务等价机制同步父级删除影子列，查询有效子项同时过滤自身和父级删除状态。
- PostgreSQL 物理字段使用 `snake_case`：`NId → n_id`、`TenantNId → tenant_n_id`、`UserNId → user_n_id`、`User_Id → user_id`、`User_IsDeleted → user_is_deleted`。跨服务、跨数据库只保存 `{EntityName}NId` 和必要快照，不建立数据库外键。
- 数据库名固定为 `identity_db`；表名前缀固定为 `identity_`。
- `identity_db` 是稳定的 `LogicalDatabaseName`：Development 默认解析为配置的共享 PostgreSQL `industrial_platform_dev` 或共享 SQLite 文件；Test/Staging/Production 由 SystemData 解析为服务专属物理数据库。共享 Development 不允许跨服务表访问或合并迁移账本。
- 登录名和各实体 NId 使用规范化比较值，不依赖数据库默认大小写规则。
- 写接口执行租户隔离、权限校验、乐观并发和操作审计。
- 统一返回 `ApiResult<T>` / `PageResult<T>`，时间使用带偏移 ISO 8601。
- 每个任务执行 TDD，并记录命令、退出码、通过/失败/跳过数量。
- 状态流转：`可派遣 → 已派遣 → 开发中 → 待验收 → 已完成`；设计冲突改为 `设计待确认`。

Entity 与 PostgreSQL 公共列映射固定为：

```text
Id                  → id uuid
IsFrozen            → is_frozen boolean
IsLocked            → is_locked boolean
IsDeleted           → is_deleted boolean
EntityType          → entity_type varchar
CreatedOn           → created_on timestamptz
LastUpdatedOn       → last_updated_on timestamptz
OptimisticVersion   → optimistic_version bigint
ConcurrencyVersion  → concurrency_version uuid
```

实体专用持久化时间统一使用 `On` 后缀，例如 LastLoginOn、ExpiresOn、RevokedOn；不得创建同义的公共创建/更新时间列。前端既有 `AuthSession.expiresAt` 只是 API 投影兼容字段。

---

# 7. 领域模型总览

聚合与实体：

```text
User Aggregate
├── User
└── UserRole（关系实体）

Role Aggregate
├── Role
└── RolePermission（关系实体）

Permission

RefreshSession

LoginAudit

OperationAudit

SsoProvider Aggregate
└── ExternalAccount（关系实体）

SsoClient Aggregate
└── SsoClientEndpoint

SsoBrowserSession
```

关系：

```text
User N:N Role N:N Permission
SsoProvider 1:N ExternalAccount N:1 User
SsoClient 1:N SsoClientEndpoint
User 1:N RefreshSession / SsoBrowserSession
```

Permission 是平台权限目录；第一阶段由迁移/种子数据注册，不提供任意创建 Permission.NId 的 UI。

---

# 8. User聚合设计

核心字段：

```text
TenantNId
NId
NormalizedNId
LoginName
NormalizedLoginName
Name
PasswordHash
Email
Phone
Status
FailedLoginCount
LockedUntil
AuthVersion
LastLoginOn
```

用户状态：

```text
Active
Disabled
```

临时锁定由 `LockedUntil` 表达，不与管理员禁用混为同一状态。

领域行为：

- Create。
- ChangeProfile。
- ChangePasswordHash。
- RecordLoginFailure。
- RecordLoginSuccess。
- Disable / Enable。
- IncrementAuthVersion。
- AssignRole / RemoveRole。

不变量：

- `NId` 是用户不可变业务标识，其他服务只保存 `UserNId`，不得保存 Identity 内部 `Id`。
- 同一租户内 `NormalizedNId` 全历史唯一且删除后不复用；`NormalizedLoginName` 对活动记录唯一。
- LoginName 去除首尾空格并按固定规则规范化；修改 LoginName 不改变 NId。
- Disabled 用户不能登录或刷新。
- `LockedUntil` 晚于当前时间时不能登录。
- 密码和安全状态变化推进 `AuthVersion`，使旧会话失效。
- 不允许移除系统中最后一个可用的系统管理员，除非经过独立恢复流程。

---

# 9. Role与Permission设计

## 9.1 Role

字段：

```text
TenantNId
NId
NormalizedNId
Name
Description
IsSystem
```

规则：

- Role.NId 在租户内唯一。
- 系统角色不能删除或修改 NId。
- 删除角色前必须解除用户关系或明确拒绝。
- 角色权限变化必须失效相关用户权限缓存。

## 9.2 Permission

字段：

```text
NId
NormalizedNId
Name
Type
ParentPermissionNId
ParentPermission_Id
ParentPermission_IsDeleted
Description
```

类型：

```text
Menu
Page
Action
Api
```

第一批 Permission.NId：

```text
identity.user.view
identity.user.create
identity.user.update
identity.user.status
identity.user.assign-role
identity.role.view
identity.role.create
identity.role.update
identity.role.assign-permission
identity.permission.view
identity.audit.login.view
identity.sso.view
identity.sso.manage
identity.sso.test
platform.home.view
platform.pda.view
platform.mobile.view
```

Permission.NId 全局唯一、发布后不可改名；废弃通过状态或新版本迁移表达。API/前端使用 `ParentPermissionNId`，数据库使用可空的 `(ParentPermission_Id, ParentPermission_IsDeleted)` 自引用复合外键；三者必须解析为同一父 Permission，两个外键列必须同时为空或同时非空。

## 9.3 UserRole与RolePermission关系实体

```text
UserRole
  TenantNId
  User_Id
  User_IsDeleted
  Role_Id
  Role_IsDeleted

RolePermission
  Role_Id
  Role_IsDeleted
  Permission_Id
  Permission_IsDeleted
```

- UserRole 使用两组复合外键分别引用 User、Role 的 `(Id, IsDeleted)`；RolePermission 同理引用 Role、Permission。
- 关系实体不作为独立业务资源暴露，因此不另造 NId；唯一性由父级组合和关系自身软删除状态控制。
- 聚合根软删除通过父级删除影子列使关系失效，但不覆盖关系实体自身的 IsDeleted。

---

# 10. 密码与登录安全设计

## 10.1 密码存储

- 密码禁止明文存储、日志和事件发布。
- 使用 BCrypt，工作因子固定为 12，并支持配置提高后的登录重哈希。
- 密码哈希只保存在 `identity_user.password_hash`。
- 密码比较使用恒定时间实现提供的验证方法。

## 10.2 第一阶段密码策略

```text
最少 12 个字符
至少一个大写字母
至少一个小写字母
至少一个数字
至少一个特殊字符
不得等于 LoginName 或 NId
```

第一阶段提供管理员重置密码和用户修改本人密码 API；不通过邮件发送密码。

## 10.3 登录限制

- 同一租户、NormalizedLoginName 和来源 IP 组合连续失败 5 次，锁定 15 分钟。
- 不存在用户与错误密码返回相同外部错误，避免账号枚举。
- 登录接口增加按 IP 和账号维度的限流，超过限制返回 429。
- 登录成功清零失败计数并写登录审计。
- Disabled、临时锁定和密码错误均写失败审计，但不得记录密码。

---

# 11. 数据库设计

数据库：

```text
identity_db
```

核心表如下。主要字段只列当前表业务字段，所有表的 Entity 生命周期字段统一由第 6 节补齐：

| 表 | 主要业务字段 | 关键约束/索引 |
| --- | --- | --- |
| `identity_user` | TenantNId、NId、NormalizedNId、LoginName、NormalizedLoginName、Name、PasswordHash、Email、Phone、Status、FailedLoginCount、LockedUntil、AuthVersion、LastLoginOn | TenantNId+NormalizedNId全历史唯一；活动记录TenantNId+NormalizedLoginName部分唯一；Status+LastUpdatedOn+Id索引；提供`(Id,IsDeleted)`可引用唯一键 |
| `identity_role` | TenantNId、NId、NormalizedNId、Name、Description、IsSystem | TenantNId+NormalizedNId全历史唯一；提供`(Id,IsDeleted)`可引用唯一键 |
| `identity_permission` | NId、NormalizedNId、Name、Type、ParentPermissionNId、ParentPermission_Id、ParentPermission_IsDeleted、Description、Status | NormalizedNId 全局唯一；父字段同时空/非空；自引用复合外键；提供`(Id,IsDeleted)`可引用唯一键 |
| `identity_user_role` | TenantNId、User_Id、User_IsDeleted、Role_Id、Role_IsDeleted | 两组父表复合外键；活动关系 TenantNId+User_Id+Role_Id 唯一 |
| `identity_role_permission` | Role_Id、Role_IsDeleted、Permission_Id、Permission_IsDeleted | 两组父表复合外键；活动关系 Role_Id+Permission_Id 唯一 |
| `identity_refresh_session` | TenantNId、NId、FamilyNId、User_Id、User_IsDeleted、TokenHash、ExpiresOn、UsedOn、RevokedOn、RevokeReason、ReplacedBySessionNId、ReplacedBySession_Id、ReplacedBySession_IsDeleted、UserAgentHash、IpAddressHash | User及可空替代会话复合外键；TokenHash、NId唯一；FamilyNId、User_Id索引 |
| `identity_login_audit` | TenantNId、UserNId、LoginNameSnapshot、Result、FailureReason、IpAddressHash、UserAgentHash、TraceId | TenantNId+CreatedOn、UserNId+CreatedOn索引；未知用户允许UserNId为空，不建立User外键 |
| `identity_operation_audit` | TenantNId、ActorUserNId、Action、ObjectType、ObjectNId、BeforeSummary、AfterSummary、TraceId | TenantNId+CreatedOn、ObjectType+ObjectNId索引；审计快照不建立业务对象外键 |
| `identity_sso_provider` | TenantNId、NId、NormalizedNId、Name、Protocol、AuthorityOrMetadataUrl、ClientIdOrEntityId、SecretOrCertificateReference、CallbackPath、Enabled、AutoRedirect、ProvisioningMode、LogoutMode、AllowedEmailDomains | TenantNId+NormalizedNId全历史唯一；提供`(Id,IsDeleted)`可引用唯一键 |
| `identity_external_account` | TenantNId、NId、SsoProvider_Id、SsoProvider_IsDeleted、User_Id、User_IsDeleted、ExternalSubject、ExternalName、ExternalEmail、LastLoginOn | Provider/User复合外键；Provider+ExternalSubject唯一；TenantNId+User_Id索引 |
| `identity_sso_client` | TenantNId、NId、NormalizedNId、Name、OAuthClientId、Enabled | TenantNId+NormalizedNId、OAuthClientId分别全历史唯一；提供`(Id,IsDeleted)`可引用唯一键 |
| `identity_sso_client_endpoint` | SsoClient_Id、SsoClient_IsDeleted、NId、EndpointType、Uri、NormalizedUri、Enabled | Client复合外键；Client+EndpointType+NormalizedUri唯一 |
| `identity_sso_browser_session` | TenantNId、NId、User_Id、User_IsDeleted、SessionHandleHash、AuthVersion、LastActivityOn、ExpiresOn、RevokedOn、RevokeReason | User复合外键；NId、SessionHandleHash唯一；User_Id、ExpiresOn、RevokedOn索引 |
| `identity_outbox` | EventId、EventType、EventVersion、Payload、EventCreatedTime、PublishedOn、RetryCount、LastError | EventId唯一；PublishedOn+EventCreatedTime索引 |

Refresh Token 只保存 SHA-256 哈希，不保存原始值。

迁移必须验证：

- 时间列为 `timestamptz`。
- 被引用父表具备 `(id,is_deleted)` 唯一键；全部子表复合外键、`ON UPDATE CASCADE` 或同事务等价同步真实生效。
- 父级 IsDeleted 不匹配的关系写入被拒绝；查询子项同时过滤自身 `is_deleted=false` 和父级影子列为 false。
- 软删除后 LoginName 可按活动记录部分唯一规则复用；User/Role/Permission/Provider/Client NId 作为跨服务稳定身份禁止复用。
- 双版本并发列存在。
- 审计和 Outbox 与业务写入可在同一事务提交。

---

# 12. JWT Access Token设计

Access Token：

```text
格式：JWT
签名：RS256
默认有效期：30分钟
```

私钥只由 Identity 持有；验证方使用公钥。JWT Header 包含 `kid`，支持密钥轮换。

Claims：

```json
{
  "sub": "user-nid",
  "user_name": "admin",
  "tenant_id": "tenant-nid",
  "role": ["SYSTEM_ADMIN"],
  "sid": "session-nid",
  "ver": 3,
  "jti": "token-guid",
  "iss": "industrial-platform-identity",
  "aud": "industrial-platform-api"
}
```

`sub` 的值是 UserNId，`tenant_id` 的值是 TenantNId，`role` 数组保存 RoleNId，`sid` 保存 RefreshSession.NId；这些 Claim 均不得写入数据库 Guid。

完整 permissions 不写入 Access Token，原因：

- 避免权限较多时令牌过大。
- 避免角色权限变化后旧权限持续到令牌自然过期。
- 前端从登录/当前用户响应获得权限集合。
- Identity 自身权限校验从版本化权限缓存读取。

公钥端点：

```text
Identity内部：GET /.well-known/jwks.json
浏览器/Gateway：GET /identity/.well-known/jwks.json
```

后续业务服务如何消费权限快照，在各服务实施方案中通过版本化授权契约确定；不得直接读取 Identity 数据库。

---

# 13. Refresh Token与会话设计

Refresh Token：

```text
随机 256 bit 不透明值
默认有效期 7 天
每次刷新旋转
```

`RefreshSession` 业务字段：

```text
TenantNId
NId
FamilyNId
User_Id
User_IsDeleted
TokenHash
ExpiresOn
UsedOn
RevokedOn
RevokeReason
ReplacedBySessionNId
ReplacedBySession_Id
ReplacedBySession_IsDeleted
UserAgentHash
IpAddressHash
```

RefreshSession.NId 写入 JWT `sid`；子表通过 `(User_Id, User_IsDeleted)` 引用 User `(Id, IsDeleted)`。旋转后的替代会话在业务契约使用 ReplacedBySessionNId，数据库使用可空的 `(ReplacedBySession_Id, ReplacedBySession_IsDeleted)` 自引用复合外键，三者必须一致。登录/刷新响应继续按 02B 前端既有契约投影为 `AuthSession.expiresAt`，不把 API 序列化字段误当作实体生命周期字段。

刷新流程：

```text
验证 Token 哈希
    ↓
验证用户状态、AuthVersion、过期与撤销
    ↓
将当前 Token 标记 Used
    ↓
生成同 Family 的新 Token
    ↓
提交数据库事务
    ↓
返回新 Access + Refresh Token
```

重复使用已经旋转的 Refresh Token 视为重放攻击：撤销整个 Family，并要求重新登录。

单会话注销：撤销当前 Family，并将 `sid` 写入 Redis 撤销键直到 Access Token 到期。

全部会话注销、禁用用户或修改密码：撤销该用户全部 RefreshSession，推进 `AuthVersion` 并失效用户/权限缓存。

---

# 14. Redis设计

Key：

```text
identity:user:{tenantNId}:{userNId}:v{authVersion}
identity:permission:{tenantNId}:{userNId}:v{authVersion}
identity:session:revoked:{sessionNId}
identity:login:fail:{tenantNId}:{normalizedLoginName}:{ipHash}
identity:rate:login:ip:{ipHash}
identity:sso:browser:{sessionNId}
```

规则：

- 缓存键必须包含租户和版本，禁止跨租户命中。
- 权限/用户状态变化先提交数据库，再删除缓存；删除失败记录告警并依赖短 TTL 收敛。
- Redis 不保存原始密码或原始 Refresh Token。
- Redis 不可用时登录限流和缓存允许明确降级，但令牌撤销校验不得静默放行；返回可诊断服务错误。

---

# 15. 认证API设计

Gateway 已使用 `/identity/**` 并剥离前缀。因此前端路径与 Identity 内部路径区分如下：

| 能力 | 前端经Gateway | Identity内部 |
| --- | --- | --- |
| 登录 | `POST /identity/api/v1/auth/login` | `POST /api/v1/auth/login` |
| 刷新 | `POST /identity/api/v1/auth/refresh` | `POST /api/v1/auth/refresh` |
| 注销 | `POST /identity/api/v1/auth/logout` | `POST /api/v1/auth/logout` |
| 注销全部 | `POST /identity/api/v1/auth/logout-all` | `POST /api/v1/auth/logout-all` |
| 当前用户 | `GET /identity/api/v1/auth/me` | `GET /api/v1/auth/me` |
| 修改密码 | `POST /identity/api/v1/auth/change-password` | `POST /api/v1/auth/change-password` |

## 15.1 登录请求

```json
{
  "loginName": "admin",
  "password": "example-password"
}
```

## 15.2 AuthSession响应

Identity API 使用 NId 契约，HttpAuthGateway 通过显式 Mapper 适配 02B 前端模型：

```json
{
  "success": true,
  "code": "200",
  "message": "success",
  "data": {
    "accessToken": "...",
    "refreshToken": "...",
    "expiresAt": "2026-08-10T16:30:00+08:00",
    "user": {
      "userNId": "USR-ADMIN",
      "loginName": "admin",
      "name": "系统管理员",
      "tenantNId": "development",
      "roleNIds": ["SYSTEM_ADMIN"],
      "permissionNIds": ["platform.home.view"]
    }
  }
}
```

前端兼容映射固定为 `userNId → AuthUser.userNId`、`loginName → AuthUser.username`、`name → AuthUser.displayName`、`roleNIds → AuthUser.roles`、`permissionNIds → AuthUser.permissions`。Phase 3 的 TASK-ID-010 必须同步调整 02B 暂定身份字段，不允许把数据库 Guid 暴露为跨服务用户标识。

## 15.3 刷新请求

```json
{
  "refreshToken": "opaque-token"
}
```

刷新成功返回完整新 `AuthSession`；失败返回 401，前端不得继续重试。

## 15.4 注销请求

```json
{
  "refreshToken": "opaque-token"
}
```

注销接口要求 Bearer Access Token。重复注销保持幂等，不暴露 Token 是否曾存在。

---

# 16. 用户、角色与权限API

## 16.1 用户

| Method | 内部路径 | 权限 |
| --- | --- | --- |
| GET | `/api/v1/users` | `identity.user.view` |
| GET | `/api/v1/users/{id}` | `identity.user.view` |
| POST | `/api/v1/users` | `identity.user.create` |
| PUT | `/api/v1/users/{id}` | `identity.user.update` |
| PUT | `/api/v1/users/{id}/status` | `identity.user.status` |
| PUT | `/api/v1/users/{id}/roles` | `identity.user.assign-role` |
| POST | `/api/v1/users/{id}/reset-password` | `identity.user.update` |

## 16.2 角色

| Method | 内部路径 | 权限 |
| --- | --- | --- |
| GET | `/api/v1/roles` | `identity.role.view` |
| GET | `/api/v1/roles/{id}` | `identity.role.view` |
| POST | `/api/v1/roles` | `identity.role.create` |
| PUT | `/api/v1/roles/{id}` | `identity.role.update` |
| PUT | `/api/v1/roles/{id}/permissions` | `identity.role.assign-permission` |

## 16.3 权限与审计

| Method | 内部路径 | 权限 |
| --- | --- | --- |
| GET | `/api/v1/permissions/tree` | `identity.permission.view` |
| GET | `/api/v1/audits/logins` | `identity.audit.login.view` |

所有列表采用统一分页、过滤和排序白名单。写接口接收原始乐观版本并对跨租户 ID 返回 404，避免泄漏其他租户资源存在性。

核心管理 DTO 字段固定为：

```text
CreateUserRequest
  nId, loginName, name, initialPassword, email, phone, roleNIds[]

UpdateUserRequest
  loginName, name, email, phone,
  expectedOptimisticVersion, expectedConcurrencyVersion

CreateRoleRequest
  nId, name, description, permissionNIds[]

UpdateRoleRequest
  name, description,
  expectedOptimisticVersion, expectedConcurrencyVersion

AssignUserRolesRequest
  roleNIds[], expectedOptimisticVersion, expectedConcurrencyVersion

AssignRolePermissionsRequest
  permissionNIds[], expectedOptimisticVersion, expectedConcurrencyVersion
```

User/Role/Permission 的 NId 创建后不可修改。管理响应可以返回本服务内部 `id` 供后续管理命令定位，但下游身份上下文、JWT、事件和跨服务 DTO 只能返回对应 NId。

第一批不提供删除系统管理员、创建任意 Permission.NId 或修改审计记录的 API。

---

# 17. 错误码设计

| HTTP | 错误码 | 含义 |
| ---: | --- | --- |
| 400 | `ID_VALIDATION_FAILED` | 请求校验失败 |
| 401 | `ID_AUTH_INVALID_CREDENTIALS` | 用户名或密码错误 |
| 401 | `ID_AUTH_REFRESH_INVALID` | Refresh Token 无效/过期/已撤销 |
| 401 | `ID_AUTH_REFRESH_REUSED` | 检测到旋转 Token 重放 |
| 403 | `ID_AUTH_ACCOUNT_DISABLED` | 账号已禁用 |
| 403 | `ID_PERMISSION_DENIED` | 权限不足 |
| 409 | `ID_USER_LOGIN_NAME_CONFLICT` | LoginName 冲突 |
| 409 | `ID_USER_NID_CONFLICT` | User.NId 冲突 |
| 409 | `ID_ROLE_NID_CONFLICT` | Role.NId 冲突 |
| 409 | `ID_PERMISSION_NID_CONFLICT` | Permission.NId 冲突 |
| 409 | `ID_CONCURRENCY_CONFLICT` | 乐观并发冲突 |
| 429 | `ID_AUTH_RATE_LIMITED` | 登录请求受限 |
| 503 | `ID_AUTH_SECURITY_STORE_UNAVAILABLE` | 撤销/安全状态不可用 |
| 400 | `ID_SSO_STATE_INVALID` | SSO state/nonce/一次性票据无效或过期 |
| 401 | `ID_SSO_EXTERNAL_AUTH_FAILED` | 外部身份平台认证失败 |
| 403 | `ID_SSO_ACCOUNT_NOT_LINKED` | 外部账号未绑定且不允许自动创建 |
| 409 | `ID_SSO_ACCOUNT_LINK_CONFLICT` | 外部账号已绑定其他平台用户 |
| 503 | `ID_SSO_PROVIDER_UNAVAILABLE` | 客户身份平台不可访问或元数据无效 |

错误响应使用统一 ApiResult，并可附 TraceId。密码、Token、内部哈希和用户是否存在不得出现在 message。

---

# 18. 权限校验设计

Identity 自身 API 使用策略授权：

```text
JWT Bearer认证
    ↓
读取 sub / tenant_id / sid / ver
    ↓
检查会话撤销与用户AuthVersion
    ↓
从版本化权限缓存读取权限
    ↓
Permission Policy判断
```

要求：

- 使用 BuildingBlocks `ICurrentUser` 提供当前用户上下文。
- 新增权限检查应使用 Policy/Handler，不把权限字符串散落在业务方法。
- TenantNId 只从经过验证的令牌读取，不信任请求体传入的租户标识。
- 系统管理员仍经过明确权限种子，不使用“角色名等于 admin 即全部放行”的隐藏后门。
- 前端权限集合用于交互显示，服务端权限缓存用于真实授权。

---

# 19. 审计设计

## 19.1 登录审计

记录：

```text
TenantNId
UserNId（无法识别时为空）
LoginNameSnapshot
Success
FailureCode
IpAddressHash
UserAgent摘要
TraceId
```

审计发生时间直接使用公共生命周期 `CreatedOn`，不再定义另一套重复发生时间列。

## 19.2 操作审计

场景：

- 创建/修改/禁用用户。
- 重置密码。
- 分配用户角色。
- 创建/修改角色。
- 分配角色权限。
- 注销全部会话。

记录 ActorUserNId、租户、目标类型/ObjectNId、操作、原因、前后值摘要和 TraceId；时间使用公共 CreatedOn。密码、Token 和哈希字段必须排除。

审计记录只追加，不提供修改和删除业务 API。

---

# 20. 集成事件与Outbox

版本化事件：

```text
Identity.UserCreated.v1
Identity.UserStatusChanged.v1
Identity.UserRolesChanged.v1
Identity.RolePermissionsChanged.v1
Identity.UserSecurityChanged.v1
```

公共字段：

```text
eventId
eventType
eventVersion
createdTime
tenantNId
subjectNId
traceId
```

事件沿用 BuildingBlocks EventBus 的 `CreatedTime`。`subjectNId` 根据事件固定为 UserNId 或 RoleNId；事件只包含业务标识、状态、版本和下游必要摘要，不包含数据库 Id、密码、Token、Email、Phone 或完整权限列表。

业务写入、操作审计和 Outbox 在同一数据库事务提交。发布至少一次；消费者必须按 `eventId` 去重。

---

# 21. 初始化与配置设计

## 21.1 配置

```text
Jwt:Issuer
Jwt:Audience
Jwt:AccessTokenMinutes = 30
Jwt:RefreshTokenDays = 7
Jwt:ActiveKeyId
Jwt:PrivateKeyPath / SecretProvider引用
Password:BcryptWorkFactor = 12
Login:MaxFailures = 5
Login:LockMinutes = 15
Sso:TicketLifetimeSeconds = 60
Sso:StateLifetimeMinutes = 5
Sso:AllowedClockSkewSeconds = 120
```

私钥、初始管理员密码和真实连接凭据不得提交仓库。

## 21.2 初始化数据

迁移/种子数据：

- 权限目录。
- `SYSTEM_ADMIN` 系统角色及全部第一批权限。
- 可选初始管理员。
- 租户级 SSO Provider/Client 仅创建结构，不提交客户地址、证书私钥或 Client Secret。

初始管理员只在数据库没有用户且显式提供以下环境变量时创建：

```text
IDENTITY_BOOTSTRAP_TENANT_NID
IDENTITY_BOOTSTRAP_USER_NID
IDENTITY_BOOTSTRAP_LOGIN_NAME
IDENTITY_BOOTSTRAP_PASSWORD
```

初始化必须幂等，不输出密码。生产环境缺少显式初始化配置时不得创建固定默认账号。

---

# 22. 前端HttpAuthGateway设计

位置：

```text
src/frontend/src/auth/httpAuthGateway.ts
```

实现 02B 稳定接口：

```typescript
interface AuthGateway {
    login(command: LoginCommand): Promise<AuthSession>;
    refresh(refreshToken: string): Promise<AuthSession>;
    logout(): Promise<void>;
    getCurrentUser(): Promise<AuthUser>;
}
```

路径统一使用 Gateway：

```text
/identity/api/v1/auth/login
/identity/api/v1/auth/refresh
/identity/api/v1/auth/logout
/identity/api/v1/auth/me
```

要求：

- `MockAuthGateway` 继续只用于开发和自动化测试。
- `HttpAuthGateway` 与 Mock 复用同一契约测试。
- DTO 映射集中在 Identity API 模块，不把后端字段判断散落到 Store。
- 生产 `VITE_AUTH_MODE` 固定为 `http`，配置为 `mock` 时构建/启动失败。
- 当前 Phase 3 延续 02B 的 sessionStorage TokenStorage；生产发布前必须单独评审 HttpOnly Cookie/BFF 迁移，不在本任务伪装已解决该安全权衡。

---

# 23. 前端刷新与会话协调

HTTP 401 处理：

```text
普通请求收到401
    ↓
检查是否已有刷新Promise
    ├── 有：等待同一Promise
    └── 无：调用AuthStore.refresh()
              ↓
       成功：原请求只重试一次
       失败：清理会话并跳转登录
```

规则：

- login、refresh、logout 请求本身不得触发自动刷新。
- 同一时刻只能存在一个刷新请求。
- 原请求最多重试一次，防止循环。
- 刷新失败清理会话并保留安全的相对 redirect。
- 403 不触发刷新，直接进入 403 或展示权限不足。
- logout 即使网络失败也清理本地状态。

---

# 24. 前端权限与导航设计

新增 PC 路由：

| 路径 | 权限 | 页面 |
| --- | --- | --- |
| `/pc/identity/users` | `identity.user.view` | 用户管理 |
| `/pc/identity/roles` | `identity.role.view` | 角色与权限分配 |
| `/pc/identity/permissions` | `identity.permission.view` | 权限目录只读查看 |
| `/pc/identity/login-audits` | `identity.audit.login.view` | 登录审计 |

菜单由本地路由/导航模型生成，并按当前用户 permissions 过滤。第一阶段不由后端下发任意组件路径。

按钮权限使用：

```vue
<PermissionGate permissionNId="identity.user.create">
    <el-button>创建用户</el-button>
</PermissionGate>
```

`PermissionGate` 和 `usePermission()` 只控制显示/禁用，不能代替服务端权限检查。

权限变更后：

- 当前用户重新调用 `/auth/me` 或完成新会话刷新。
- 菜单和按钮响应式更新。
- 当前路由权限被移除时跳转 403，不保留不可访问页面内容。

---

# 25. Identity PC管理页面设计

## 25.1 用户管理

功能：

- 分页、User.NId、LoginName、Name、状态过滤。
- 创建和编辑用户。
- 启用/禁用。
- 分配角色。
- 管理员重置密码。
- 并发冲突提示和重新加载。

状态变化必须二次确认；禁用当前登录用户或最后一个系统管理员时按后端规则拒绝并展示明确错误。

## 25.2 角色与权限

功能：

- 角色按 Role.NId、Name、状态分页。
- 创建和编辑非系统角色。
- 权限树查看和分配。
- 系统角色保护。
- 并发冲突处理。

## 25.3 权限目录

只读展示 Permission.NId、名称、类型和 ParentPermissionNId。第一阶段不允许页面创建或改名 Permission.NId。

## 25.4 登录审计

展示时间、UserNId、LoginNameSnapshot、成功/失败、失败码、来源摘要和 TraceId。页面不得展示密码、Token、完整 IP 或敏感请求体。

PC 页面遵守 02B 的布局、API、错误、权限、键盘和可访问性规范；PDA/Mobile 第一阶段只复用真实登录与退出，不增加 Identity 管理页面。

---

# 26. 联邦单点登录与独立页面设计

## 26.1 支持场景

场景一：客户已有统一登录管理平台。

```text
用户访问 Industrial Platform
    ↓
Identity 根据租户选择客户 IdP
    ↓
客户 IdP 完成登录
    ↓
Identity 映射外部账号并签发平台会话
```

场景二：用户从门户、消息或其他系统直接打开看板/追溯单页。

```text
打开 /dashboard/... 或 /trace/...
    ↓
无本地会话
    ↓
发起 SSO 并保存原始相对地址
    ↓
完成认证
    ↓
直接返回目标页面（不先进入平台首页）
```

场景三：看板、追溯未来独立部署为不同前端应用。各应用注册为平台 SSO Client，通过 Identity 获取一次性授权码/票据，不共享数据库和长期 Token。

## 26.2 协议与适配器

第一优先协议：

```text
OpenID Connect Authorization Code + PKCE
```

企业兼容协议：

```text
SAML 2.0 Service Provider
```

CAS、LDAP/AD 直连通过后续 `IExternalIdentityProvider` 适配器扩展，不写入核心登录流程。

```typescript
ExternalIdentityProvider
├── OidcExternalIdentityProvider
└── Saml2ExternalIdentityProvider
```

OIDC 必须验证 issuer、签名、audience、state、nonce、PKCE 和时间；SAML 必须验证签名、Audience、Recipient、InResponseTo、时间窗口和单次断言使用。

## 26.3 Provider与账号映射

`IdentitySsoProvider`：

```text
TenantNId
NId
NormalizedNId
Name
Protocol（Oidc / Saml2）
AuthorityOrMetadataUrl
ClientIdOrEntityId
SecretOrCertificateReference
CallbackPath
Enabled
AutoRedirect
ProvisioningMode（ExistingOnly / JustInTime）
LogoutMode（LocalOnly / Federated）
AllowedEmailDomains
```

`IdentityExternalAccount`：

```text
NId
SsoProvider_Id
SsoProvider_IsDeleted
ExternalSubject
User_Id
User_IsDeleted
ExternalName
ExternalEmail
LastLoginOn
```

ExternalAccount 通过两组复合外键引用 SsoProvider 和 User 的 `(Id, IsDeleted)`；管理 DTO 返回 `SsoProviderNId`、`UserNId`，不暴露内部 Guid。

默认 `ProvisioningMode=ExistingOnly`：外部账号必须预先绑定平台用户。JIT 创建只能由租户管理员显式启用，并配置默认角色、允许邮箱域和唯一匹配规则。

账号匹配优先使用不可变 external subject。禁止只凭 displayName 绑定；按 Email/用户名自动绑定时必须经过显式租户策略并防止绑定已有账号。

## 26.4 SSO端点

| 能力 | 前端经Gateway | Identity内部 |
| --- | --- | --- |
| Provider发现 | `GET /identity/api/v1/sso/discovery?connection=...` | `GET /api/v1/sso/discovery?connection=...` |
| 平台授权 | `GET /identity/api/v1/sso/authorize?clientId=...&returnUrl=...` | `GET /api/v1/sso/authorize` |
| 指定Provider登录 | `GET /identity/api/v1/sso/authorize/{providerNId}` | `GET /api/v1/sso/authorize/{providerNId}` |
| OIDC回调 | `GET /identity/api/v1/sso/callback/oidc/{providerNId}` | `GET /api/v1/sso/callback/oidc/{providerNId}` |
| SAML回调 | `POST /identity/api/v1/sso/callback/saml/{providerNId}` | `POST /api/v1/sso/callback/saml/{providerNId}` |
| 票据交换 | `POST /identity/api/v1/sso/exchange` | `POST /api/v1/sso/exchange` |
| 联邦注销 | `POST /identity/api/v1/sso/logout` | `POST /api/v1/sso/logout` |

Provider 管理：

```text
GET/POST/PUT /api/v1/sso/providers
POST /api/v1/sso/providers/{id}/test
GET/POST/DELETE /api/v1/sso/providers/{id}/accounts
GET/POST/PUT /api/v1/sso/clients
```

分别要求 `identity.sso.view`、`identity.sso.manage`、`identity.sso.test`。

管理请求/响应使用 `providerNId`、`userNId`、`clientNId` 和 `endpointNId` 表达业务引用；OAuth/SAML 协议字段继续使用标准名称 `clientId`、`entityId`、`externalSubject`。绑定请求不得接收 Identity 内部 User_Id 或 Provider_Id。

未登录 Provider 发现只接受租户自定义域名解析结果或不可猜测的 `connection` 令牌，只返回 nId、name、protocol 和 autoRedirect 等公开字段，不返回 authority、clientId、证书或密钥引用。

Provider 元数据获取必须限制为 HTTPS，执行 DNS/IP 校验、超时、响应大小和重定向次数限制，禁止访问 loopback、link-local、私网管理地址和云元数据地址，防止 SSRF。

## 26.5 一次性票据流程

Identity 回调成功后不得把 Access Token 或 Refresh Token 放入 URL。

```text
验证外部断言
    ↓
映射平台用户和权限
    ↓
生成60秒、单次使用的loginTicket
    ↓
重定向 /auth/sso/callback?ticket=...
    ↓
前端POST /sso/exchange
    ↓
返回标准AuthSession
```

`state` 保存 providerNId、tenantNId、nonce、PKCE verifier摘要、原始returnUrl和签发时间，存放Redis，5分钟过期且只能消费一次。

`returnUrl` 必须是已注册客户端下的站内相对路径或精确 Redirect URI。禁止协议相对地址、任意域名、双重编码绕过和 URL 中携带 Token。

## 26.6 平台浏览器SSO会话

本地账号登录和客户 IdP 登录成功后，Identity 同时建立平台浏览器 SSO 会话：

```text
Cookie名称：industrial_platform_sso
内容：随机不透明会话句柄
属性：HttpOnly + Secure + SameSite=Lax
默认空闲期限：8小时
默认绝对期限：12小时
```

`IdentitySsoBrowserSession` 业务字段：

```text
TenantNId
NId
User_Id
User_IsDeleted
SessionHandleHash
AuthVersion
LastActivityOn
ExpiresOn
RevokedOn
RevokeReason
```

BrowserSession 使用 `(User_Id, User_IsDeleted)` 引用 User `(Id, IsDeleted)`；Cookie 只携带随机句柄，数据库/Redis 仅保存哈希。NId 用于审计和会话管理，不得作为 Cookie 内容。

数据库保存句柄哈希、TenantNId、User复合外键、AuthVersion、公共CreatedOn及最后活动/过期/撤销时间；Redis只保存会话校验所需最小投影。两者都不保存Access Token或Refresh Token。

未来看板、追溯或新标签页访问 Identity authorize 端点时：

```text
存在有效平台SSO Cookie
    ↓
验证用户状态、AuthVersion和会话撤销
    ↓
无需再次输入密码
    ↓
签发60秒一次性票据并返回目标页面
```

各应用仍获取自己的 AuthSession，不从 Cookie 读取身份，也不跨应用复制 Refresh Token。

开发期前端与 Gateway 跨端口时，只允许对显式 CORS Origin 启用 credentials；不得使用 `AllowAnyOrigin` 与 credentials 组合。生产 Cookie 使用 Identity/Gateway 的中心认证域，业务应用通过顶层跳转访问该域。

平台注销撤销该浏览器 SSO 会话并清除 Cookie；联邦注销是否继续退出客户 IdP 由 `LogoutMode` 决定。

## 26.7 平台内部SSO Client

为未来独立看板、追溯应用登记：

```text
IdentitySsoClient
  TenantNId
  NId
  NormalizedNId
  Name
  OAuthClientId
  Enabled

IdentitySsoClientEndpoint
  SsoClient_Id
  SsoClient_IsDeleted
  NId
  EndpointType（Redirect / PostLogoutRedirect / Origin）
  Uri
Enabled
```

Endpoint 使用 `(SsoClient_Id, SsoClient_IsDeleted)` 引用 SsoClient `(Id, IsDeleted)`；同一 Client 下 NId 和 `EndpointType + NormalizedUri` 分别唯一。Redirect URI 精确匹配，不支持通配符域名。看板/追溯只获得当前页面所需平台会话，仍由自己的 API 验证权限。

同一统一前端内的独立页面直接复用 AuthStore；新标签页没有 sessionStorage 会话时，可利用客户 IdP 已存在的 SSO 会话快速完成跳转，无需再次输入密码。

## 26.8 独立页面模式

未来业务路由通过 route meta 声明：

```typescript
interface RouteMeta {
    standalone?: boolean;
    permissionNId?: string;
    requiresAuth?: boolean;
}
```

`standalone=true` 使用 `StandaloneLayout`：

- 不显示平台侧栏和无关菜单。
- 保留页面标题、当前用户、返回门户和退出入口。
- 仍执行鉴权、权限、TraceId、错误和审计逻辑。
- 认证后返回原始深链接，不改写为 `/pc/home`。
- 权限不足显示独立版 403，不加载受保护页面数据。

示例由后续模块最终确定：

```text
/dashboard/views/{dashboardId}
/trace/batches/{batchId}
```

这些路径是路由模式说明，不在 Identity 阶段实现看板或追溯业务页面。

## 26.9 iframe与单点注销

默认支持浏览器顶层打开或新标签页，不默认允许第三方 iframe。

客户门户确需 iframe 时必须：

- 配置精确 `frame-ancestors` 和来源白名单。
- 评估第三方 Cookie 限制并使用前台授权码/票据交换。
- 使用 `postMessage` 时校验精确 origin 和消息类型。
- 防止点击劫持，禁止 `*` 来源。

注销默认 `LocalOnly`，只撤销 Industrial Platform 会话，避免意外退出客户全部系统。租户显式配置 `Federated` 后，才调用 OIDC end-session 或 SAML Single Logout，并使用精确 post-logout redirect。

## 26.10 SSO前端页面

新增：

| 路径 | 页面 | 说明 |
| --- | --- | --- |
| `/auth/sso/callback` | SSO回调 | 交换一次性票据并恢复 returnUrl |
| `/auth/sso/error` | SSO错误 | 显示可诊断错误和 TraceId |
| `/auth/account-link-required` | 账号待绑定 | 不自动创建时提供管理员联系说明 |
| `/pc/identity/sso` | SSO管理 | ProviderNId、ClientNId、ClientEndpoint、账号绑定和连接测试 |

登录页根据租户配置显示“企业统一登录”；只有一个启用且 `AutoRedirect=true` 的 Provider 时可以自动跳转，但必须提供“使用平台账号登录”回退入口，避免 IdP 故障导致管理员完全失联。

---

# 27. 前后端错误映射

| 后端结果 | 前端行为 |
| --- | --- |
| `ID_AUTH_INVALID_CREDENTIALS` | 登录表单显示通用账号或密码错误 |
| `ID_AUTH_ACCOUNT_DISABLED` | 显示账号不可用，不保留会话 |
| `ID_AUTH_RATE_LIMITED` | 显示稍后重试，不自动重复提交 |
| `ID_AUTH_REFRESH_INVALID/REUSED` | 清理会话并回登录 |
| `ID_PERMISSION_DENIED` | 进入 403 或页面内权限提示 |
| User.NId/LoginName/Role.NId冲突 | 对应表单字段显示冲突 |
| 并发冲突 | 提示数据已更新，允许重新加载 |
| 503安全状态不可用 | 不推断登录/刷新成功，显示 TraceId |

前端不得通过中文 message 字符串判断错误类型，必须使用 code/status。

---

# 28. 安全与可观测性要求

安全：

- 登录、刷新、密码和用户管理端点只允许 HTTPS 生产部署。
- API 日志、审计、事件和前端控制台不得出现密码或 Token。
- JWT 私钥不进入镜像、仓库或普通 appsettings。
- Token、用户和权限响应设置 `Cache-Control: no-store`。
- 登录请求体不写入通用请求日志。
- Swagger 示例不得包含真实账号和密钥。

可观测性：

- 登录成功/失败、刷新成功/失败、重放检测、权限拒绝和缓存失效记录结构化指标。
- 日志包含服务、tenantNId（可用时）、userNId 和 TraceId。
- 指标不使用 LoginName、UserNId 作为高基数标签。
- 健康检查保持 liveness/readiness 区分；JWT 配置缺失应启动失败而不是运行时随机报错。

---

# 29. 自动化测试设计

## 29.1 后端测试

Domain：

- User.NId/LoginName分别规范化、唯一、改名边界和状态。
- 登录失败计数、锁定、成功清零。
- Role/Permission NId、ParentPermissionNId 和关系不变量。
- AuthVersion 推进和领域事件。

Application：

- 登录、刷新、注销、当前用户。
- User/Role/Permission NId用例、DTO映射和校验。
- 租户隔离、权限、并发和审计。

Infrastructure：

- PostgreSQL映射、迁移、NId索引、父表`(Id,IsDeleted)`唯一键、子表复合外键、删除状态同步、双重过滤和事务。
- BCrypt 哈希与重哈希。
- RS256 签名、kid 和 JWKS。
- Refresh Token 哈希、旋转、重放和撤销。
- Redis 权限缓存、撤销和不可用行为。
- Outbox原子性、subjectNId/CreatedTime和事件序列化。

API：

- 所有 15、16 节端点。
- 400/401/403/404/409/429/503。
- ApiResult/OpenAPI 契约。
- 登录限流和敏感字段不泄漏。
- OIDC state/nonce/PKCE/issuer/audience、SAML 签名与重放、外部账号映射、JIT 策略和一次性票据。
- returnUrl/Redirect URI 白名单、单点注销、Provider 不可用和 SSO 审计。

## 29.2 前端测试

- `HttpAuthGateway` 与 Mock 的共享契约测试。
- 登录成功/失败、自动刷新、单飞、重试一次和刷新失败。
- User/Role/Permission NId、登录审计页面和AuthUser映射。
- 路由、菜单和按钮权限。
- 401、403、409、429、503 错误反馈。
- PC/PDA/Mobile 真实登录、刷新、退出 E2E。
- 企业登录入口、SSO callback、票据交换、错误/待绑定页面和本地账号回退。
- 新标签页深链接认证后原路返回、StandaloneLayout、独立版 403 和 URL 无 Token。

## 29.3 验收门禁

```text
后端 restore/build/test
    +
PostgreSQL/Redis集成测试
    +
OpenAPI/契约测试
    +
前端 format/lint/typecheck/unit/coverage/build
    +
前后端 Playwright E2E
```

所有命令记录退出码、测试数、耗时和报告路径。外部环境无法执行的项目只能标记“待验收”。

本次 V2.1 仅调整开发设计和任务派发契约，没有执行后端或前端测试；仓库现有测试结果只能作为输入状态，不是本轮新鲜验收证据。

## 29.4 关键验收场景

1. 创建用户时 User.NId 与 LoginName 分别校验唯一；修改 LoginName 后 User.NId、JWT sub、UserNId 引用均不变化。
2. UserRole、RolePermission、Permission 自引用、RefreshSession、ExternalAccount、ClientEndpoint 和 BrowserSession 写入错误父 Id/IsDeleted 组合时被数据库拒绝。
3. 父表软删除/恢复同步父级删除影子列但不改写子表自身 IsDeleted；默认子项查询同时过滤两种删除状态。
4. 本地登录返回 `userNId/loginName/name/roleNIds/permissionNIds`，前端映射后不暴露数据库 Guid。
5. JWT `sub` 为 UserNId、`sid` 为 RefreshSession.NId；业务服务构造 ICurrentUser 时只得到 UserNId。
6. Refresh Token 每次旋转；旧 Token 重放撤销 Family，Redis 不可用时安全失败。
7. Role/Permission NId 变化限制、最后系统管理员保护、权限缓存失效和直接 API 403 均生效。
8. 用户、角色、权限和审计 PC 页面覆盖成功、空、校验、并发、无权限与服务不可用状态。
9. OIDC/SAML Provider 通过 providerNId 定位，外部账号绑定只使用 providerNId/userNId，票据过期或重放被拒绝。
10. ClientEndpoint 按 Client复合外键持久化，Redirect/PostLogoutRedirect/Origin 精确匹配且拒绝通配符和恶意 returnUrl。
11. 新标签打开看板或追溯深链接时，无有效会话则完成 SSO 后返回原地址；地址栏、历史、日志均无 Token。
12. Identity 事件使用 subjectNId 和 BuildingBlocks CreatedTime，不包含数据库 Id、密码、Token、Email、Phone 或完整权限列表。

---

# 30. 开发任务依赖

```text
ID-001 → ID-002 ─┐
          ID-003 ─┼→ ID-004 → ID-005 → ID-006 → ID-007 → ID-008 → ID-009
                  │
                  └──────────────────────────────────────┐

ID-005 + ID-006 → ID-010 → ID-011
ID-008 + ID-011 → ID-012
ID-005 + ID-006 + ID-007 → ID-013
ID-010 + ID-013 → ID-014
ID-008 + ID-011 + ID-013 + ID-014 → ID-015
ID-009 + ID-012 + ID-015 → ID-016
```

- ID-002（用户/密码）和 ID-003（角色/权限）在 ID-001 后可并行。
- 前端 ID-010 必须等待稳定认证 API，不得继续绑定 Mock DTO。
- ID-012 同时依赖管理 API 和前端权限基础。
- ID-013 建立联邦 SSO 后端能力；ID-014 完成登录页、callback 和独立页面恢复。
- ID-015 完成 SSO 管理页面和客户协议联调。
- ID-016 是前后端联合验收，不替代各任务自己的测试。

---

# 31. Identity开发任务拆分

## TASK-ID-001 对齐服务骨架、契约与独立数据库

**状态：** 已完成

**目标：** 在保留现有健康检查的基础上，固化五层边界、API 前缀、配置、测试项目和 `identity_db` 迁移基础。

**输入文档：** 本文第 4～6、11、15、21 节；现有 Identity 骨架和 TASK-BASE-006。

**依赖：** TASK-FE-010、TASK-BASE-006 已完成。

**允许修改范围：** Identity 后端项目、Identity 测试、解决方案注册和 Identity 开发配置；不得实现业务领域或修改其他服务业务代码。

**预期输出：** Domain/Application/Contracts/Infrastructure/Api五层项目边界、`/api/v1`路由约定、OpenAPI、`identity_db`配置、Entity公共生命周期、NId命名、父表Id+IsDeleted复合外键规则、架构测试和迁移执行框架。

**验证与证据：** 构建、架构引用、配置缺失、数据库名、健康检查和 OpenAPI 测试通过；证明未引用其他服务 Infrastructure。

**结果回写：** 回写项目结构、配置节、路由前缀和当前骨架偏差。

**建议提交：** `feat(identity): align service contracts and database boundary`

---

## TASK-ID-002 实现用户、密码与登录安全领域

**状态：** 已完成

**目标：** 实现以NId为稳定业务身份的User聚合、LoginName登录凭据、密码策略、登录失败计数、临时锁定、状态和AuthVersion。

**输入文档：** 本文第 8、10 节；BuildingBlocks Entity 生命周期。

**依赖：** TASK-ID-001。

**允许修改范围：** Identity Domain/Application 的 Users、Passwords、LoginSecurity 模块及测试。

**预期输出：** User.NId/NormalizedNId、LoginName/NormalizedLoginName、Name、状态/行为、不变量、BCrypt端口、登录安全策略和领域事件。

**验证与证据：** 覆盖NId/LoginName分别规范化与唯一、NId不随LoginName修改、密码强度、错误密码、不存在用户等时比较策略、五次失败锁定、成功清零、禁用、密码变化和AuthVersion。

**结果回写：** 回写字段、状态机、密码规则、锁定阈值和领域错误。

**建议提交：** `feat(identity): add user and login security domain`

---

## TASK-ID-003 实现角色、权限与关系领域

**状态：** 已完成

**目标：** 实现 Role、Permission、UserRole、RolePermission 和系统权限目录。

**输入文档：** 本文第 9、18 节。

**依赖：** TASK-ID-001。

**允许修改范围：** Identity Domain/Application 的 Roles、Permissions 模块及测试。

**预期输出：** Role/Permission NId、ParentPermissionNId、权限类型、系统角色保护、UserRole/RolePermission四组父级Id+IsDeleted复合外键和关系用例。

**验证与证据：** 覆盖Role/Permission NId冲突、Permission自引用复合外键、系统角色保护、重复关系、父级删除状态不匹配、跨租户关系、最后系统管理员保护、权限缓存失效信号和并发冲突。

**结果回写：** 回写Role/Permission NId、角色规则、父级复合外键、关系约束和错误码。

**建议提交：** `feat(identity): add role based permission domain`

---

## TASK-ID-004 实现持久化、迁移与初始化数据

**状态：** 已完成

**目标：** 将用户、角色、权限、会话、审计和 Outbox 映射到 `identity_db`，建立约束、索引和幂等种子。

**输入文档：** 本文第 11、21 节；TASK-ID-002、003 模型。

**依赖：** TASK-ID-002、TASK-ID-003。

**允许修改范围：** Identity Infrastructure Persistence、迁移、Repository、初始化和集成测试。

**预期输出：** 九张核心认证/授权表映射、表清单不重复Entity公共字段、被引用父表`(id,is_deleted)`唯一键、子表父级影子列复合外键、迁移、仓储、事务、Permission/SYSTEM_ADMIN种子和显式管理员初始化；五张SSO表由TASK-ID-013追加迁移。

**验证与证据：** PostgreSQL集成测试验证表、`timestamptz`、NId唯一/部分索引、父表复合唯一键、子表Id+IsDeleted外键、删除状态级联同步、子表双重过滤、软删除复用、双版本并发、Refresh Token哈希和初始化幂等；证明不记录默认密码。

**结果回写：** 回写最终表名、字段、索引、迁移标识和初始化方式。

**建议提交：** `feat(identity): add persistence migrations and seed data`

---

## TASK-ID-005 实现登录、JWT、JWKS与当前用户API

**状态：** 已完成

**目标：** 实现登录验证、RS256 Access Token、JWKS 和当前用户契约。

**输入文档：** 本文第 12、15、17、18 节；02B AuthSession/AuthUser。

**依赖：** TASK-ID-004。

**允许修改范围：** Identity Authentication/Application/Api、Contracts、配置和对应测试；前端仅允许添加共享契约夹具。

**预期输出：** login、me、RS256/kid/JWKS、`sub=UserNId`、AuthSession/AuthUser.userNId DTO、登录限流、成功/失败审计和标准错误。

**验证与证据：** 覆盖正确/错误/不存在/禁用/锁定账号、限流、Claims、issuer/audience/expiry、密钥缺失、JWKS、no-store、统一信封和敏感信息泄漏。

**结果回写：** 回写端点、DTO、Claims、有效期、错误码和密钥配置。

**建议提交：** `feat(identity): add login and signed access tokens`

---

## TASK-ID-006 实现Refresh Token旋转、注销与撤销

**状态：** 已完成

**目标：** 实现 RefreshSession、Token 旋转、重放检测、单会话/全部会话注销和密码修改后的撤销。

**输入文档：** 本文第 13、14、15 节。

**依赖：** TASK-ID-005。

**允许修改范围：** Identity Sessions/Authentication/Infrastructure/Api、Redis 和测试。

**预期输出：** RefreshSession.NId/FamilyNId、User父级及ReplacedBySession自引用复合外键、refresh、logout、logout-all、change-password、Token哈希、Family旋转、sid撤销和AuthVersion校验。

**验证与证据：** 覆盖成功旋转、过期、撤销、重复刷新、并发刷新、重放撤销 Family、幂等注销、全部注销、禁用/改密失效和 Redis 不可用 fail-closed。

**结果回写：** 回写 Token 长度、期限、表字段、Redis 键、撤销规则和错误码。

**建议提交：** `feat(identity): add refresh rotation and session revocation`

---

## TASK-ID-007 实现服务端RBAC、权限缓存与用户上下文

**状态：** 可派遣

**目标：** 实现 Identity API 的策略权限、租户上下文、版本化权限缓存和缓存失效。

**输入文档：** 本文第 14、18 节；BuildingBlocks Security。

**依赖：** TASK-ID-006。

**允许修改范围：** Identity Authorization/Application/Infrastructure/Api、必要的 BuildingBlocks Security 通用扩展及测试；不得加入 Identity 业务实体到 BuildingBlocks。

**预期输出：** JWT Bearer注册、PermissionNId Policy/Handler、仅暴露UserNId的ICurrentUser、UserNId+AuthVersion权限缓存、租户过滤和拒绝审计。

**验证与证据：** 覆盖未认证、权限有/无、跨租户、撤销 sid、AuthVersion 不一致、角色权限变化、缓存命中/失效/不可用及系统管理员无隐藏绕过。

**结果回写：** 回写 Policy 命名、Claim 映射、缓存 TTL/键和 BuildingBlocks 变更。

**建议提交：** `feat(identity): add tenant aware permission authorization`

---

## TASK-ID-008 实现用户角色管理API与审计

**状态：** 可派遣

**目标：** 实现用户、角色、权限目录和登录审计 API，并保证全部写操作可审计。

**输入文档：** 本文第 16、17、19 节。

**依赖：** TASK-ID-007。

**允许修改范围：** Identity Users/Roles/Permissions/Audits 的 Application/Api/Infrastructure 和测试。

**预期输出：** 第16节全部API、使用User/Role/Permission NId的分页与详情、乐观并发、租户隔离、操作审计、密码修改/重置和OpenAPI契约；跨服务DTO不暴露数据库Id。

**验证与证据：** 覆盖 CRUD/分配、400/403/404/409、跨租户、最后管理员保护、系统角色保护、缓存失效、审计前后值和敏感字段排除。

**结果回写：** 回写最终路由、请求/响应、过滤字段、PermissionNId、错误码和审计字段。

**建议提交：** `feat(identity): expose audited identity management APIs`

---

## TASK-ID-009 发布Identity集成事件

**状态：** 可派遣

**目标：** 通过 Outbox 发布稳定的用户和权限变化事件，并建立消费者契约夹具。

**输入文档：** 本文第 20 节；RabbitMQ 和 Outbox 规范。

**依赖：** TASK-ID-008。

**允许修改范围：** Identity Contracts/Infrastructure、Outbox 发布器和跨服务契约测试；下游只允许增加契约夹具。

**预期输出：** 五类v1事件、`subjectNId`与BuildingBlocks `CreatedTime`、Outbox原子提交、重试、序列化兼容和不含数据库Id/敏感字段的契约。

**验证与证据：** 覆盖原子性、重复发布、重试、事件版本、序列化、向后兼容和敏感字段扫描。

**结果回写：** 回写事件名、版本、字段、路由键、重试和消费者约束。

**建议提交：** `feat(identity): publish identity integration events`

---

## TASK-ID-010 前端接入HttpAuthGateway与真实会话

**状态：** 可派遣

**目标：** 用 `HttpAuthGateway` 替换生产运行时 Mock，并实现单飞刷新、请求重试和真实注销。

**输入文档：** 本文第 22、23、27 节；TASK-ID-005、006 API 契约；02B AuthGateway。

**依赖：** TASK-ID-005、TASK-ID-006、TASK-FE-010。

**允许修改范围：** `src/frontend/src/auth/**`、HTTP 认证拦截、Identity API DTO/mapper、运行配置和测试；不得重写登录页面和布局。

**预期输出：** HttpAuthGateway、http/mock装配、`AuthUser.userNId`及User字段投影、401单飞刷新、原请求重试一次、刷新失败退出和生产禁Mock。

**验证与证据：** 共享 AuthGateway 契约测试和登录/刷新/并发/注销测试通过；证明无循环刷新、无 token/password 日志且原 Mock 自动化测试仍可使用。

**结果回写：** 回写路径、DTO 映射、TokenStorage、刷新协调和 Phase 2 契约偏差。

**建议提交：** `feat(frontend): connect identity authentication`

---

## TASK-ID-011 前端实现权限导航与操作权限

**状态：** 可派遣

**目标：** 将真实 permissions 接入路由、菜单和按钮级交互控制。

**输入文档：** 本文第 24 节；TASK-ID-007 权限契约。

**依赖：** TASK-ID-007、TASK-ID-010。

**允许修改范围：** 前端 permission 组件/composable、Identity routes、PC 导航模型、AuthStore 权限刷新和测试。

**预期输出：** Identity PC路由、以PermissionNId驱动的菜单过滤、`PermissionGate.permissionNId`、`usePermission()`、权限变化响应和当前页面失权处理。

**验证与证据：** 覆盖有/无权限、菜单隐藏、直接路由 403、按钮隐藏/禁用、权限刷新、失权跳转和 PC/PDA/Mobile 首页权限保持。

**结果回写：** 回写路由、菜单、权限组件接口和权限刷新策略。

**建议提交：** `feat(frontend): add identity permission navigation`

---

## TASK-ID-012 前端实现Identity管理页面

**状态：** 可派遣

**目标：** 实现用户、角色权限、权限目录和登录审计 PC 页面并连接真实 API。

**输入文档：** 本文第 16、25、27 节；TASK-ID-008 API 契约。

**依赖：** TASK-ID-008、TASK-ID-011。

**允许修改范围：** 前端 Identity api/types/stores/pages/components/routes 和对应测试；不得增加 PDA/Mobile 管理页面。

**预期输出：** 四类PC页面、User/Role/Permission NId与Name/LoginName字段、分页过滤、表单校验、状态确认、角色/权限分配、并发冲突、审计查看和权限控制。

**验证与证据：** 组件和 E2E 覆盖成功/失败、权限、跨租户 404、409 重新加载、系统角色/最后管理员保护、敏感字段不展示和关键视口截图。

**结果回写：** 回写页面路由、API 映射、表单字段、状态、错误反馈和截图路径。

**建议提交：** `feat(frontend): add identity administration pages`

---

## TASK-ID-013 实现客户统一身份平台联邦SSO

**状态：** 可派遣

**目标：** 实现租户级 OIDC/SAML Provider、外部账号映射、一次性票据、平台 SSO Client 和可配置单点注销。

**输入文档：** 本文第 26、27、28、29 节；TASK-ID-005、006、007 的认证、会话和权限能力。

**依赖：** TASK-ID-005、TASK-ID-006、TASK-ID-007。

**允许修改范围：** Identity Sso/ExternalIdentity/Contracts/Application/Infrastructure/Api、数据库迁移、Redis、配置和测试；仅为 SSO Cookie、回调和显式 credentials Origin 允许最小修改 Gateway 配置/测试；不得实现看板或追溯业务页面。

**预期输出：** `IExternalIdentityProvider`、OIDC与SAML2适配器、Provider/ExternalAccount/Client/ClientEndpoint/BrowserSession五表NId模型及父级复合外键、第26.4节providerNId端点、HttpOnly平台SSO Cookie、state/nonce/PKCE、60秒一次性票据、ExistingOnly/JIT策略和LocalOnly/Federated注销。

**验证与证据：** 覆盖Provider/Client/Endpoint NId、Provider/User/Client父级复合外键、OIDC issuer/audience/signature/state/nonce/PKCE、SAML签名/Audience/InResponseTo/重放、账号绑定冲突、JIT域名和默认角色、票据过期/单次消费、Redirect URI精确匹配、Provider不可用、租户隔离、单点注销和URL/日志无Token。

**结果回写：** 回写协议版本、Provider/Client/Endpoint NId字段、端点、Callback URI、票据和state TTL、账号映射、JIT/注销策略及客户联调前置。

**建议提交：** `feat(identity): add enterprise identity federation`

---

## TASK-ID-014 前端接入SSO登录与独立页面恢复

**状态：** 可派遣

**目标：** 在统一登录页接入企业登录入口、回调票据交换和认证后原始深链接恢复。

**输入文档：** 本文第 23、26、27 节；TASK-ID-013 SSO 契约；02B 路由和布局规范。

**依赖：** TASK-ID-010、TASK-ID-013。

**允许修改范围：** 前端 SSO api/types/pages/router、登录页企业入口、AuthStore 回调装配、StandaloneLayout 基础和测试；不得实现看板/追溯业务组件。

**预期输出：** ProviderNId发现、企业登录入口、`/auth/sso/callback`、错误/待绑定页面、一次性票据交换、安全returnUrl、`permissionNId`独立页面route meta/Layout和本地账号回退。

**验证与证据：** 覆盖单/多 Provider、自动跳转、本地回退、票据成功/失败/过期、恶意 returnUrl、刷新/新标签页、认证后原路返回、独立版 403、退出和浏览器地址/历史/日志无 Access/Refresh Token。

**结果回写：** 回写页面路由、Provider 选择、回调状态、StandaloneLayout 接口、returnUrl 校验和错误映射。

**建议提交：** `feat(frontend): add federated sign in and standalone routes`

---

## TASK-ID-015 实现SSO管理页面与客户协议联调

**状态：** 可派遣

**目标：** 提供租户管理员可用的 SSO Provider、平台 Client、外部账号绑定和连接测试页面，并完成标准 IdP 联调。

**输入文档：** 本文第 24～27 节；TASK-ID-008 管理 API、TASK-ID-011 权限、TASK-ID-013/014 SSO 能力。

**依赖：** TASK-ID-008、TASK-ID-011、TASK-ID-013、TASK-ID-014。

**允许修改范围：** Identity SSO 管理 API、前端 `/pc/identity/sso` 页面、测试 IdP/SAML fixture、契约/E2E 和安全说明；不得提交客户真实 Secret、证书私钥或生产元数据。

**预期输出：** Provider/Client/ClientEndpoint NId管理、启停、连接测试、账号绑定/解绑、JIT/AutoRedirect/LogoutMode配置、密钥只写不回显、OIDC和SAML标准测试环境报告。

**验证与证据：** 覆盖 `identity.sso.*` 权限、租户隔离、字段校验、Secret 不回显、证书轮换、失败元数据、测试连接、绑定冲突、OIDC/SAML 成功失败 E2E、iframe 默认拒绝及显式来源白名单。

**结果回写：** 回写管理路由、字段、权限、密钥引用、测试 Provider 配置、兼容矩阵和客户交付配置清单。

**建议提交：** `feat(identity): add sso administration and compatibility tests`

---

## TASK-ID-016 完成Identity前后端联合验收

**状态：** 可派遣

**目标：** 从新环境完成本地登录、联邦 SSO、独立页面恢复、刷新、权限和管理页面的端到端验收，并形成后续服务身份输入契约。

**输入文档：** TASK-ID-001 至 TASK-ID-015 全部输出；本文第 28、29、32 节。

**依赖：** TASK-ID-009、TASK-ID-012、TASK-ID-015。

**允许修改范围：** Identity/前端集成测试、验收脚本、README、CI 和本文执行记录；只修复验收阻塞缺陷。

**预期输出：** 后端/前端全量报告、PostgreSQL/Redis 联调、本地与 OIDC/SAML 登录 E2E、六类目标视口认证路径、深链接/Standalone 验收、权限管理 E2E、事件契约报告和下一阶段契约清单。

**验证与证据：** 验证NId贯穿实体/API/前端/事件、父级Id+IsDeleted复合外键及删除同步、登录成功/失败/限流、刷新旋转/重放、单会话/全部/联邦注销、改密/禁用失效、401/403、SSO Provider/票据/绑定/JIT、认证后原路返回、用户/角色/权限/审计/SSO页面、生产禁Mock、敏感信息、Outbox事件、后端build/test和前端全门禁；记录退出码、测试数、覆盖率、耗时和报告路径。

**结果回写：** 更新全部 ID 任务状态、最终 API/权限/事件/SSO/配置、兼容矩阵、已知偏差、外部环境待验收项和后续模块输入契约。

**建议提交：** `feat(identity): complete local and federated authentication flow`

---

# 32. Identity完成标准

后端：

- `identity_db` 独立保存用户、角色、权限、会话、审计和 Outbox。
- 所有表统一具备 Entity 生命周期，字段清单不重复公共字段；全部同库父子关系使用父表 Id+IsDeleted 复合外键、删除影子同步和双重查询过滤。
- User、Role、Permission、SsoProvider、SsoClient 等实体身份统一使用 NId；跨服务身份上下文、JWT、事件和 DTO 不暴露数据库 Guid。
- 登录、当前用户、刷新、注销、全部注销和修改密码完整可用。
- Access Token 使用 RS256、kid、issuer、audience、sid、ver 和 jti。
- Refresh Token 只存哈希，支持旋转、重放检测和 Family 撤销。
- 用户、角色、权限和登录审计 API 具备租户、权限、并发和审计保护。
- 权限缓存、会话撤销和安全存储不可用时行为明确。
- 集成事件通过 Outbox 发布且不含敏感字段。
- OIDC/SAML 客户 IdP 接入、外部账号绑定、一次性票据和平台 SSO Client 可用。
- SSO state/nonce/PKCE、SAML 重放、Redirect URI 和单点注销策略经过安全验证。

前端：

- `HttpAuthGateway` 与 Mock 共享稳定接口和契约测试，AuthUser 使用 userNId，不暴露数据库内部标识。
- 生产模式使用真实 Identity 并禁止 Mock。
- 401 单飞刷新、失败退出、403 和安全 redirect 可用。
- PC 用户、角色权限、权限目录和登录审计页面连接真实 API。
- 登录页支持企业统一登录、本地账号回退和可诊断 SSO 错误。
- 新标签页打开看板/追溯类深链接时，认证后可以回到原页面且 URL 中没有 Token。
- `/pc/identity/sso` 可以管理 Provider、Client 和外部账号绑定，Secret 不回显。
- 菜单、路由和按钮权限与服务端 permissions 一致。
- PC/PDA/Mobile 都能使用真实登录、刷新和退出。

联合验收：

- 后端领域、应用、基础设施、API、并发、安全和契约测试通过。
- 前端 format、lint、typecheck、unit、coverage、build 和 E2E 通过。
- 本地账号、OIDC、SAML、独立页面恢复和本地/联邦注销关键路径通过。
- 日志、审计、事件、响应和浏览器控制台无密码、Token 和私钥。
- ReferenceData 可以消费稳定身份上下文和权限约定，不读取 Identity 数据库。

若 Docker、浏览器或密钥环境未具备，相关集成项只能标记“待验收”，不得直接标记“已完成”。

---

# 33. 执行记录

| 任务 | 状态 | 执行者/任务 | 提交 | 验证证据 | 结果回写 |
| --- | --- | --- | --- | --- | --- |
| TASK-ID-001 | 已完成 | 本轮 Claude 协作 | `feat(identity): align service contracts and database boundary` | 2026-08-11 全量 build 0 警告 0 错误;test 156/156(BB 104、Identity 26、RefData 13、Gateway 13);架构测试锁定 Contracts;迁移框架 SQLite 6 测试 | 新增 Contracts 项目;五层边界;`/api/v1` 路由约定;OpenAPI;`identity_db` 独立配置;迁移执行框架(账本 `identity_schema_migrations`、失败回滚、DB 不可达降级);`Microsoft.OpenApi` 钉到 2.7.5 规避 GHSA-v5pm-xwqc-g5wc;PostgreSQL 真实验证「待验收」 |
| TASK-ID-002 | 已完成 | 本轮 Claude 协作 | `feat(identity): add user and login security domain` | 2026-08-11 全量 build 0 警告 0 错误;test 238/238(BB 104、Identity 108、RefData 13、Gateway 13);Domain.Tests 83(新增 82:NId/PasswordPolicy/LoginAttemptPolicy/User/登录安全) | 新增 `Users/User.cs` 聚合根(14 字段、Create/ChangeProfile/ChangeLoginName/ChangePasswordHash/RecordLoginFailure/RecordLoginSuccess/Disable/Enable/EnsureLoginAllowed/IncrementAuthVersion)、`Identities/NId.cs` 值对象(`Value`/`Normalized`,正则+规范化+大小写不敏感相等)、`Passwords/IPasswordHasher.cs` 端口 + `PasswordPolicy`(12~128、大写/小写/数字/特殊字符、不得等于 LoginName/NId)、`LoginSecurity/LoginAttemptPolicy`(默认 5 次 15 分钟)、三个领域事件;登录拒绝抛 `UnauthorizedException`;明文密码不进领域;BCrypt 实现留 TASK-ID-004 |
| TASK-ID-003 | 已完成 | 本轮 Claude 协作 | `feat(identity): add role based permission domain` | 2026-08-11 全量 build 0 警告 0 错误;test 286/286(BB 104、Identity 156、RefData 13、Gateway 13);Domain.Tests 133(新增 50:PermissionCatalog/Permission/Role/UserRole) | 新增 `Permissions/Permission.cs` 聚合根(NId/ParentPermissionNId/Type 创建后不可变、Status、ChangeProfile 无事件)、`PermissionType`/`PermissionStatus` 枚举、`PermissionCatalog`(17 个第一批 NId 常量 + `FirstBatchNIds`,§9.2);`Roles/Role.cs` 聚合根(TenantNId/NId/IsSystem 创建后不可变、ChangeProfile、AssignPermission/UnassignPermission 发布 `RolePermissionsChangedEvent`、`Delete` 系统角色保护)、`RolePermission.cs` 关系实体;`Users/UserRole.cs` 关系实体、`UserRolesChangedEvent`;`User.cs` 增量新增 `_userRoles`/`UserRoles`/`AssignRole`/`RemoveRole`(跨租户/已删除角色/重复分配守卫、幂等解除、最后系统管理员保护 `activeHolderCountInTenant<=1` 拒绝);关系实体复合外键影子列在领域层快照父级状态,父级删除后的批量更新与数据库约束留 TASK-ID-004,权限缓存订阅留 TASK-ID-007;`GlobalSuppressions.cs` 豁免 CA1711(Permission/RolePermission 为领域术语,§9.2/§9.3) |
| TASK-ID-004 | 已完成 | 本轮 Claude 协作 | `feat(identity): add persistence migrations and seed data` | 2026-08-11 全量 build 0 警告 0 错误;test 313/313(BB 104、Identity 183、RefData 13、Gateway 13);Infrastructure.Tests 33(新增 27:迁移 9 + 仓储 13 + 哈希 5);PostgreSQL 真实验证「待验收」 | 新增 `Persistence/Entities/*Table.cs` POCO×5(identity_user/role/permission/user_role/role_permission,`[SugarTable]`+`[SugarColumn(ColumnName="snake_case")]`,public 无参构造)、`Persistence/TableMapper.cs` POCO↔聚合双向映射、`Persistence/Repositories/{IUser,IRole,IPermission}Repository`+实现×3(软删除过滤、双重过滤载入子项、双版本并发原子更新、事务内子项 diff 同步、Permission.GetByNIdAsync/GetAllAsync)、`Persistence/Migrations/IdentitySchemaMigrations.cs` 11 步(9 建表 + 2 种子:17 权限目录 + development 租户 SYSTEM_ADMIN 系统角色 + 可选 bootstrap 管理员 env 变量,幂等 check-then-insert,密码 BCrypt 因子 12 仅存哈希)、`Passwords/BcryptPasswordHasher.cs`(BCrypt.Net-Next 4.0.3,`IPasswordHasher` 实现);DDL 按 DbType 分支(SQLite TEXT/INTEGER/partial index `WHERE is_deleted = 0`、PostgreSQL uuid/timestamptz/BOOLEAN/partial `WHERE is_deleted = false`),复合外键 `(id,is_deleted)` ON UPDATE CASCADE 同步子表父级影子列,部分唯一索引软删后 NId/登录名复用;SQLite 连接串 `Foreign Keys=True`;`DependencyInjection.cs` 注册 11 迁移步骤 + 3 仓储 + `BcryptPasswordHasher`;refresh_session/login_audit/operation_audit/outbox 仅建表 DDL(实体/仓储留 ID-005/008/009),五张 SSO 表留 TASK-ID-013 |
| TASK-ID-005 | 已完成 | 本轮 Claude 协作 | `feat(identity): add login and signed access tokens` | 2026-08-11 全量 build 0 警告 0 错误;test 354/354(BB 104、Identity 224(Domain 133 + Infra 48 + Api 22 + App 20 + Contract 1)、RefData 13、Gateway 13);Application.Tests 20(新增)、Infrastructure.Tests 48(新增 15:密钥/签发/JWKS/审计/刷新/仓储)、Api.Tests 22(新增 4:JWKS no-store、me 401 信封、login 400 信封、login E2E 门控);登录 E2E 与 PostgreSQL 真实验证「待验收」(IDENTITY_E2E_DB=1) | 新增 Contracts `Authentication/AuthenticationContracts.cs`(LoginRequest 可空属性防 `[ApiController]` 推断 Required、AuthUser/AuthSession(accessToken/refreshToken/expiresAt/user)、JwksKey/JwksDocument camelCase 即 RFC 7517);Application 层(`Authentication/`):`AuthenticationOptions`(DefaultTenantNId=development/MaxLoginFailures=5/LockDuration=15m/IpRateLimitMaxAttempts=20/IpRateLimitWindow=1m/RefreshTokenLifetimeDays=7)、`Exceptions.cs`(AuthenticationException 基类 + InvalidCredentials 401/AccountDisabled 403/RateLimitExceeded 429/SecurityStoreUnavailable 503/SessionInvalid 401,§17 错误码)、端口(`IAuthenticationStore`/`IAccessTokenFactory`/`IJwksProvider`/`ILoginRateLimiter`/`ILoginAuditSink`/`IRefreshSessionStore`)、`AuthenticationService` 登录编排(校验→IP 限流→防枚举→持久锁/组合锁→RecordLoginSuccess→签发 sessionNId(`SES-`)+32B token→刷新会话哈希落库→token 签发→AuthUser+审计;LoggerMessage 记录 1001/1002,密码/Token/哈希/用户是否存在绝不进 message)+`AddIdentityApplication`;Infrastructure:实体 `LoginAuditTable`/`RefreshSessionTable`(ID-004 DDL 对齐)、`Security/Hashing.cs`(Sha256Hex)、`JwtOptions`(Identity:Jwt)/`RsaSigningKeyProvider`(空配置→RSA.Create(2048) 临时密钥+告警;配置 PEM→ImportFromPem,非法→启动失败 fail-closed)、`AccessTokenFactory`(RS256,kid 由 SigningCredentials.Key.KeyId 自动写 Header,claims=sub(userNId)/user_name/tenant_id/role[](RoleNId)/sid/ver/jti,不写 Guid/完整 permissions)、`JwksProvider`、`LoginRateLimiter`(Redis INCR/EXPIRE,键 `identity:rate:login:ip:{ipHash}`+`identity:login:fail:{tenant}:{normalized}:{ipHash}`,非 OCE 异常告警+降级放行)、`LoginAuditSink`/`RefreshSessionStore`(IP/UA/Token 只存 SHA-256 hex)、仓储扩展(GetByNormalizedLoginNameAsync/GetByNIdAsync/GetNIdsAsync/GetActivePermissionsForRolesAsync)、DI;Api:`AuthController`(POST api/v1/auth/login AllowAnonymous、GET api/v1/auth/me Authorize、`[ResponseCache(NoStore=true)]`,catch ValidationException→400 ID_VALIDATION_FAILED / AuthenticationException→按码 StatusCode,信封 ApiResult)、`AddIdentityAuthentication`(JwtBearer 自校验公钥验签+iss/aud/lifetime/ClockSkew 30s、MapInboundClaims=false、NameClaimType/RoleClaimType 对齐、OnChallenge 401「401」/OnForbidden 403 ID_PERMISSION_DENIED 统一信封)、Program(UseRouting/UseAuthentication/UseAuthorization/MapControllers + `/.well-known/jwks.json` minimal API no-store)、appsettings 加 Identity:Jwt/Authentication;`Directory.Packages.props` 钉 JwtBearer 10.0.10(对齐 MS.AspNetCore 10.0.10) |
| TASK-ID-006 | 已完成 | 本轮 Claude 协作 | `feat(identity): add refresh rotation and session revocation` | 2026-08-11 全量 build 0 警告 0 错误;test 387/387(BB 104、Identity 257(Domain 133 + Infra 56 + Api 26 + App 41 + Contract 1)、RefData 13、Gateway 13);Application.Tests 41(新增 21:旋转/重放/注销/改密)、Infrastructure.Tests 56(新增 8:旋转/撤销)、Api.Tests 26(新增 4:refresh 空体 400 信封、logout/logout-all/change-password 无 token 401 信封);`--vulnerable` 25/25 项目干净;PostgreSQL/Redis 真实验证「待验收」 | 新增 Contracts `Authentication/AuthenticationContracts.cs` 三个 DTO(RefreshRequest/LogoutRequest/ChangePasswordRequest,可空属性防 Required 推断);Application `Authentication/`:`Exceptions.cs` 新增 `RefreshTokenInvalidException`(401 `ID_AUTH_REFRESH_INVALID`)/`RefreshTokenReusedException`(401 `ID_AUTH_REFRESH_REUSED`);`IRefreshSessionStore` 扩展(StoredRefreshSession 投影/`RefreshRotationStatus{Rotated,Reused,Invalid}`/FindByRawTokenAsync/RotateAsync/RevokeFamilyAsync/RevokeAllForUserAsync)、新增 `ISessionRevocationStore`(RevokeAsync 尽力而为写 Redis/IsRevokedAsync fail-closed 抛 SecurityStoreUnavailable 503)、`IAuthenticationStore.FindByUserIdAsync`、`IAuthenticationService` 新增四用例 + `AuthenticationService` 实现(RefreshAsync:非空校验→SHA-256 定位→Redis sid 撤销校验 fail-closed→顺序重放撤销 Family+REUSED→已撤销/被替换/过期→INVALID→用户已删/禁用→INVALID→原子 RotateAsync→签发新 token(`sid`=新 sessionNId/`ver`=AuthVersion)→完整 AuthSession;LogoutAsync 撤销 Family+写 sid 撤销键,幂等;LogoutAllAsync/ChangePasswordAsync 捕获原始双版本→IncrementAuthVersion/ChangePasswordHash→双版本 UpdateUserAsync→RevokeAllForUserAsync);Infrastructure:`RefreshSessionStore` 四方法(FindByRawToken 哈希定位、RotateAsync 事务内先插替代会话再原子 UPDATE 守卫 `used_on IS NULL AND revoked_on IS NULL AND !is_deleted`,并发/顺序重用→Reused、RevokeFamilyAsync/RevokeAllForUserAsync 幂等置 RevokedOn+RevokeReason)、新增 `SessionRevocationStore`(Redis 键 `identity:session:revoked:{sid}`,写失败告警、校验失败抛 503 fail-closed)、`AuthenticationStore.FindByUserIdAsync`(组合 GetByIdAsync)、DI 注册 ISessionRevocationStore;Api:`AuthController` 新增 refresh(AllowAnonymous)/logout(Authorize,读 `sid`/`exp` claim 算 sid 撤销 TTL)/logout-all/change-password(Authorize,读 `sub`),catch ValidationException→400 ID_VALIDATION_FAILED、AuthenticationException→按码,成功返回 `ApiResult.Ok` 信封(ResultFilter 透传);§17 错误码 401 `ID_AUTH_REFRESH_INVALID`/`ID_AUTH_REFRESH_REUSED`、503 `ID_AUTH_SECURITY_STORE_UNAVAILABLE`(fail-closed);Token=32B base64url 不透明值、7 天(`RefreshTokenLifetimeDays`),只存 SHA-256 hex;E2E(IDENTITY_E2E_DB=1)与 PostgreSQL/Redis 真实验证「待验收」 |
| TASK-ID-007 | 可派遣 | - | - | - | - |
| TASK-ID-008 | 可派遣 | - | - | - | - |
| TASK-ID-009 | 可派遣 | - | - | - | - |
| TASK-ID-010 | 可派遣 | - | - | - | - |
| TASK-ID-011 | 可派遣 | - | - | - | - |
| TASK-ID-012 | 可派遣 | - | - | - | - |
| TASK-ID-013 | 可派遣 | - | - | - | - |
| TASK-ID-014 | 可派遣 | - | - | - | - |
| TASK-ID-015 | 可派遣 | - | - | - | - |
| TASK-ID-016 | 可派遣 | - | - | - | - |

---

# 34. 下一阶段输入契约

Identity 完成后，ReferenceData 及后续服务可以依赖：

```text
JWT身份声明
    sub = UserNId
    user_name = LoginName
    tenant_id = TenantNId
    role = RoleNId[]
    sid = RefreshSessionNId
    ver

ICurrentUser
    IsAuthenticated
    UserNId
    LoginName
    TenantNId
    RoleNIds
    PermissionNIds

平台PermissionNId命名规范
Identity权限变化事件
统一401/403错误契约
前端AuthStore与PermissionGate
客户IdP联邦SSO契约
平台SSO Client与Redirect URI规则
StandaloneLayout与认证后原路返回约定
```

后续服务仍需在自己的实施方案中定义业务权限和数据范围，不能把所有业务授权逻辑回填到 Identity。

跨服务只保存 UserNId、RoleNId、PermissionNId 及必要快照，不保存 Identity 数据库 Id、IsDeleted 或父级影子列，也不建立跨库外键。

---

# 35. 文档自审清单

- [x] 引用文件真实存在，阶段归属和当前代码/环境状态清晰。
- [x] 无 TBD、TODO、“适当处理”等占位或模糊实现选项。
- [x] 表/实体字段清单只列业务字段，Entity 生命周期在第 6 节统一定义。
- [x] 实体稳定业务标识使用 NId；Code 仅出现在 HTTP/OAuth 等非实体身份语义。
- [x] 同库父子表均明确 Id+IsDeleted 复合外键、删除同步和双重过滤；跨服务未建立数据库外键。
- [x] 本地登录、OIDC/SAML、平台 SSO Client、独立页面原路返回和本地账号回退边界明确。
- [x] API、JWT、ICurrentUser、事件、前端 AuthUser 和 PermissionGate 的 NId 命名前后一致。
- [x] 后端、前端、契约、数据库和 E2E 均有具体测试场景及证据格式。
- [x] 16 张任务卡均具备状态、目标、输入文档、依赖、允许修改范围、预期输出、验证与证据、结果回写、建议提交九字段。
- [x] 任务依赖、任务卡和执行记录编号一一对应。
- [x] 本次仅调整实施方案，未将历史测试描述为本轮新鲜验证证据。
- [x] `git diff --check` 通过。
