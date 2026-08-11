# CLAUDE.md

Industrial Platform 的 Claude 开发指南。

## 项目概览

.NET 10 微服务平台(工业/MES 领域),Clean Architecture + DDD。服务依赖顺序:
`BuildingBlocks → Identity → ReferenceData → MasterData`。

当前里程碑:BuildingBlocks 与可运行基线代码已完成（Docker 真实环境联调仍待验收）；统一前端第一批（PC/PDA/Mobile 三端基础壳,FE-001~FE-010）已完成；无 Docker 环境下后端三服务运行验证已通过（探测结论与调试方式见 `src/DEBUGGING.md`）；Identity、ReferenceData 保持服务骨架，业务功能尚未实现。

## 协作约定

本项目为协作处理模式。当前只负责**代码及功能实现**;文档、架构设计、部署等其他工作由协作方负责,不主动处理。

## 开发设计与任务派遣文档规范

项目所有新增或重构的开发实施方案统一使用：

`docs/implementation/TEMPLATE-开发实施方案.md`

该规则适用于后端服务、前端阶段、BuildingBlocks、基础设施、外部集成和跨模块专项设计。后续开发设计及任务派遣不得另起不兼容格式。

统一顺序：

```text
文档说明与当前状态
→ 目标、职责和明确边界
→ 总体架构、项目结构和全局约束
→ 领域/组件、数据、API/事件、页面详细设计
→ 错误、安全、审计、可观测性和测试设计
→ 任务依赖
→ 九字段开发任务卡
→ 完成标准、执行记录和下一阶段输入契约
```

每个可派遣任务必须包含且只使用以下统一字段：

```text
状态
目标
输入文档
依赖
允许修改范围
预期输出
验证与证据
结果回写
建议提交
```

强制规则：

- 详细业务和技术设计写在任务拆分之前；任务卡引用对应章节，不重复整篇设计，也不得只有一句摘要。
- 有前端或外部消费者时必须按“后端用例与契约 → 页面/调用方 → 契约测试与 E2E → 阶段验收”纵向设计；不适用时写明原因。
- API、事件、数据库、权限、页面和错误必须给出稳定契约及明确边界，禁止使用 `TBD`、`TODO`、“适当处理”等占位表达。
- 状态统一为 `待细化 → 可派遣 → 已派遣 → 开发中 → 待验收 → 已完成`；设计冲突使用 `设计待确认`。
- 历史测试和提交必须标注为历史证据，不得表述为本轮重新验证。
- 外部环境未具备时相关项目只能标记“待验收”。
- 任务编号、依赖图和执行记录必须一一对应；公共基线的历史任务不得覆盖，新增变更使用新编号。
- 文档完成前执行引用、占位符、契约一致性、任务字段和 `git diff --check` 自审。

统一数据建模规则：

- 表/实体字段定义及“主要字段”只列当前表业务字段；每张实体表统一必备的 `Entity` 生命周期字段在全局约束集中定义，不逐表重复。
- 实体自身稳定业务标识统一使用 `NId`；其他业务表引用时使用 `{EntityName}NId`。`Code` 仅用于生成编码结果等非实体身份语义。
- 同库父子表以主表 `Id + IsDeleted` 建立复合外键；子表分别保存 `{ParentEntity}_Id`、`{ParentEntity}_IsDeleted`，且保留自身独立的 `IsDeleted`。PostgreSQL 物理列使用 `snake_case`，被引用主表声明 `unique (id, is_deleted)`。
- 父表软删除/恢复必须通过 `ON UPDATE CASCADE` 或同一事务内等价机制同步子表父删除状态快照；有效子记录查询同时过滤子表自身和父引用的删除状态。
- 跨服务、跨数据库只引用 `{EntityName}NId` 及必要快照，通过 API/事件同步，不建立数据库外键。

当前格式基准文档：

- `docs/implementation/02-Industrial Platform BuildingBlocks基础组件开发实施方案.md`
- `docs/implementation/02B-Industrial Platform统一前端第一批开发实施方案.md`
- `docs/implementation/03-Industrial Platform Identity Service开发实施方案.md`

## 常用命令

> Windows 需先设置本地 CLI home(否则 NuGet 恢复异常)。

```bash
export DOTNET_CLI_HOME="$PWD/.dotnet_cli_home"
dotnet restore src/backend/IndustrialPlatform.slnx
dotnet build src/backend/IndustrialPlatform.slnx --no-restore
dotnet test tests/BuildingBlocks/IndustrialPlatform.BuildingBlocks.Tests/IndustrialPlatform.BuildingBlocks.Tests.csproj --no-build
```

统一前端(工具链由根 `.mise.toml` 钉定 node 24.18.0 / pnpm 11.16.0;方案见 `docs/implementation/02B-…`):

```bash
cd src/frontend
pnpm install --frozen-lockfile   # 严格按锁文件安装
pnpm lint && pnpm typecheck && pnpm test:unit:coverage && pnpm build
pnpm test:e2e                   # 基于 pnpm preview,需先 build
```

## 工程约束

- `Directory.Build.props`:`net10.0`、Nullable enable、**TreatWarningsAsErrors**、`AnalysisLevel=latest-recommended`。任何分析器警告都是错误,必须消除。
- `Directory.Packages.props`:Central Package Management(CPM)。新增第三方包需先在中央 props 声明版本,再在各 csproj 用无版本 `PackageReference`。
- `.editorconfig`:4 空格缩进;测试项目局部 `.editorconfig` 关闭 CA1707(允许测试方法名下划线命名)。
- **时间类型规范**:禁止使用 `DateTime`,一律使用 `DateTimeOffset`(保留时区偏移;获取当前时间用 `DateTimeOffset.UtcNow`)。
- 架构测试 `tests/BuildingBlocks/IndustrialPlatform.BuildingBlocks.Tests/ProjectReferenceArchitectureTests.cs` 锁定各 csproj 引用关系,改动引用会失败。
- BuildingBlocks 禁止包含 MES 业务逻辑(工单/称量/设备等)。当前引用方向:Application.Abstractions→SharedKernel；Infrastructure→Application.Abstractions+SharedKernel；EventBus→SharedKernel；Security/Web→Application.Abstractions；Logging 不引用其他 BuildingBlocks 项目。

## BuildingBlocks 实施进度

### 已实现 ✅

| Task | 内容 | 位置 | 验收 |
| --- | --- | --- | --- |
| Task-001 | 项目结构:7 个 classlib + 引用关系 + 架构测试 | `src/backend/src/BuildingBlocks/*` | 解决方案编译通过 ✅ |
| Task-002 | Entity / AggregateRoot / ValueObject / IDomainEvent | `SharedKernel/Entities|ValueObjects|Events` | 单元测试 ✅ |
| TASK-BB-010 | Entity 生命周期与并发调整:冻结/锁定/软删除/恢复、双版本并发、软删除过滤仓储 | `SharedKernel/Entities|Exceptions|Interfaces`、`Infrastructure/Repository` | 单元 + SQLite 仓储测试 ✅ |
| Task-003 | Result / Result<T> 统一返回模型 | `SharedKernel/Results` | 单元测试 ✅ |
| Task-004 | 异常体系:DomainException 基类 + Business/Validation/Unauthorized/NotFound | `SharedKernel/Exceptions` | 单元测试 ✅ |
| Task-005 | SqlSugar 组件:SqlSugarDbContext / BaseRepository\<T\> / SqlSugarUnitOfWork + DI 扩展 | `Infrastructure/{Database,Repository,Transaction,Extensions}` | 编译 + DI 注册测试 ✅ |
| Task-006 | Redis 组件:ICacheService / CacheService / RedisDistributedLock + DI 扩展 | `Infrastructure/Caching` | 编译 + DI 注册测试 ✅ |
| Task-007 | RabbitMQ 组件:IntegrationEvent / Producer / Consumer + DI 扩展 | `EventBus/{Events,Abstractions,Producer,Consumer,Connection,Subscriptions,Options,Extensions}` | 编译 + 订阅管理/DI 注册测试 ✅ |
| Task-008 | Logging 组件:Serilog 配置(Console/File/Seq) + TraceId | `Logging/{Options,Enrichers,Internal,Extensions}` | 编译 + 选项绑定/增强器/DI 注册测试 ✅ |
| 补充 | Security(ICurrentUser/ClaimConstants/CurrentUser) + Web(ApiResult/PageResult/ExceptionMiddleware/RequestLoggingMiddleware/ResultFilter) | `Security/*`、`Web/{Results,Middleware,Filters,Extensions}` | 编译 + 声明读取/结果包装/注册测试 ✅ |

**当前测试:104 通过 / 0 失败**(BuildingBlocks 测试项目);全解决方案 387 通过 / 0 失败(BuildingBlocks 104、Identity 257、ReferenceData 13、Gateway 13)。

### 关键技术决策

- **CA1000**:泛型类型禁止静态成员 → `Result<T>` 的工厂方法(Ok/Fail)全部放在非泛型 `Result` 类上。
- **无约束泛型 `T? Data` 语义**:`Result<int>.Data` 是 `int`(值类型失败时为 default),`Result<string>.Data` 可为 null。测试按此编写。
- **NuGet 版本(已定)**:SqlSugarCore 5.1.4.216 / StackExchange.Redis 3.1.3 / RabbitMQ.Client 7.2.2 / Serilog 4.4.0 系列 / Microsoft.Extensions.* 10.0.10。已写入 `Directory.Packages.props`。
- **依赖安全(已清零)**:SqlSugarCore 升级到 5.1.4.216 后,其捆绑的可选提供程序(Oracle/SqlClient/System.Drawing/Newtonsoft 等)传递漏洞(NU1902/3/4)与 SQLite RID 警告(NETSDK1206)均消失;SQLitePCLRaw 全家(bundle/core/provider/lib.e_sqlite3)由 `SQLitePCLRaw.bundle_e_sqlite3` 钉到 2.1.12,修复 lib.e_sqlite3 2.1.11 的 GHSA-2m69-gcr7-jv3q 高危漏洞。`Directory.Build.props` 无任何 `WarningsNotAsErrors` 豁免,严格零警告基线;全 18 个项目 `--vulnerable`/`--deprecated` 审计均干净。
- **ICurrentUser.UserId 类型**:采用 `Guid`(与 SharedKernel `Entity.Id` 一致,不采用设计文档 §25 的 `long`),已在 Security 组件落地;声明类型常量见 `Security/ClaimConstants.cs`。
- **Entity 生命周期与双版本并发(TASK-BB-010)**:`Entity` 含 `IsFrozen/IsLocked/IsDeleted/EntityType/CreatedOn/LastUpdatedOn/OptimisticVersion(long)/ConcurrencyVersion(Guid)`;创建时 `CreatedOn == LastUpdatedOn` 取同一 `UtcNow`、`EntityType` 为完整类型名、双版本初始化。业务修改用 `EnsureCanModify()+Touch()`(protected),状态转换 `Freeze/Unfreeze/Lock/Unlock/MarkDeleted/Restore` 为 public 且幂等;状态冲突抛含操作名与实体类型的 `BusinessException`。更新/删除/恢复接口必须传调用方读取时的原始双版本,仓储以 `Id+IsDeleted+双版本` 原子 UPDATE,影响行数非 1 抛 `ConcurrencyException`;删除为软删除,默认查询排除已删除记录,物理删除只允许运维流程。
- **SqlSugar 时间测试替身限制**:SqlSugar 5.1.4 的 SQLite provider 存储 `DateTimeOffset` 时丢弃 UTC 偏移(读回为本地偏移),仓储时间断言按墙钟一致;PostgreSQL `timestamptz` 精确映射待 TASK-BASE-003 用真实库验收。
- **Redis 连接不急切抛异常(TASK-BASE-003)**:`AddRedis` 的 `IConnectionMultiplexer` 使用 `AbortOnConnectFail=false`,Redis 不可达时返回断开的复用器并后台重试,避免首次解析即抛异常导致健康检查工厂逃逸成 500(仅当连接建立后 `PingAsync` 才报错,由检查捕获为 Unhealthy)。
- **Gateway(TASK-BASE-004)**:YARP 2.3.0 反向代理(依赖方向仅 Logging/Web,不含业务)。统一入口 `http://localhost:5080`;`/identity`、`/referencedata` 前缀转发用 `PathRemovePrefix` transform 剥离(`PathPrefix` 是追加语义,踩坑点)。代理错误统一为 `ApiResult` 信封:`IForwarderErrorFeature` 中间件按 `ForwarderError` 映射 503「下游服务不可用」/ 504「网关转发请求超时」(YARP 2.3 无 `IProxyErrorHandler`),未匹配路由 fallback 404「路由不存在」。集群 `HttpRequest.ActivityTimeout` = `Gateway:RequestTimeoutSeconds`(默认 10s)驱动 504。平台健康聚合:`/health/ready` 对每个下游 GET `/health/ready`(超时 10s,匹配下游依赖全挂 ~6s 的最坏就绪耗时,3s 会临界截断),任一 Unhealthy 整体 503,响应不含凭据;`/health` 静态、`/health/live` 不查下游。开发期 CORS(`Gateway:Cors:AllowedOrigins`,默认 Vue3 dev 5173/preview 4173),预检在网关短路。测试配置注入须用 `UseSetting`(`ConfigureAppConfiguration` 晚于 Program 启动读取,对 minimal API 无效);.NET 10 无 `AddCheck(name, Func<IServiceProvider,IHealthCheck>)` 重载,参数化健康检查用 `AddTypeActivatedCheck<T>(name, ..., args: [...])`。

## Identity 服务实施进度

实施方案见 `docs/implementation/03-Industrial Platform Identity Service开发实施方案.md`(TASK-ID-001~016)。

### 已实现 ✅

| 任务 | 内容 | 验收 |
| --- | --- | --- |
| TASK-ID-001 | 对齐服务骨架、契约与独立数据库:新增 Contracts 项目(零 ProjectReference)、五层边界、`/api/v1` 路由约定(RoutePrefixConvention)、OpenAPI(`Microsoft.AspNetCore.OpenApi` 10.0.10)、`identity_db` 独立配置、测试项目重构为 5 个、迁移执行框架(SchemaMigrationRunner + 账本 `identity_schema_migrations` + 启动后台服务)、BuildingBlocks 架构测试同步锁定 Contracts | 2026-08-11 全量 build 0 警告 0 错误、test 156/156(BB 104、Identity 26、RefData 13、Gateway 13);迁移框架 SQLite 6 测试(账本创建/幂等/失败回滚/重试/DB 不可达降级);PostgreSQL 真实验证「待验收」 ✅ |
| TASK-ID-002 | 用户、密码与登录安全领域:`Users/User.cs` 聚合根(14 字段,Create/ChangeProfile/ChangeLoginName/ChangePasswordHash/RecordLoginFailure/RecordLoginSuccess/Disable/Enable/EnsureLoginAllowed/IncrementAuthVersion,AuthVersion 安全版本递增使旧会话失效)、`Identities/NId.cs` 值对象(正则+规范化+大小写不敏感相等)、`Passwords/IPasswordHasher.cs` 端口 + `PasswordPolicy`(12~128、大写/小写/数字/特殊字符、不得等于 LoginName/NId)、`LoginSecurity/LoginAttemptPolicy`(默认 5 次 15 分钟)、三个领域事件(UserCreated/UserStatusChanged/UserSecurityChanged);登录拒绝抛 `UnauthorizedException` | 2026-08-11 全量 build 0 警告 0 错误、test 238/238(BB 104、Identity 108、RefData 13、Gateway 13);Domain.Tests 83(新增 82);明文密码不进领域,BCrypt 实现留 TASK-ID-004 ✅ |
| TASK-ID-003 | Role、Permission 与关系领域:`Permissions/Permission.cs` 聚合根(NId/ParentPermissionNId/Type 创建后不可变、Status、ChangeProfile 无事件)、`PermissionType`/`PermissionStatus` 枚举、`PermissionCatalog`(17 个第一批 NId 常量,§9.2)、`Roles/Role.cs` 聚合根(IsSystem 创建后不可变、AssignPermission/UnassignPermission 发布 RolePermissionsChangedEvent、Delete 系统角色保护)、`RolePermission`/`UserRole` 关系实体与 `UserRolesChangedEvent`;`User.cs` 增量新增 AssignRole/RemoveRole(跨租户/已删除角色/重复分配守卫、幂等解除、最后系统管理员保护);复合外键影子列领域层快照,父级删除批量更新与 DB 约束留 TASK-ID-004,权限缓存订阅留 TASK-ID-007 | 2026-08-11 全量 build 0 警告 0 错误、test 286/286(BB 104、Identity 156、RefData 13、Gateway 13);Domain.Tests 133(新增 50);CA1711 经 `GlobalSuppressions.cs` 豁免(Permission/RolePermission 为领域术语) ✅ |
| TASK-ID-004 | 持久化、迁移与初始化数据:POCO 表模型×5(`identity_user/role/permission/user_role/role_permission`,`[SugarTable]`+`[SugarColumn(ColumnName="snake_case")]`)+ `TableMapper` 双向映射 + 仓储×3(`IUser/IRole/IPermissionRepository`,软删除过滤、双重过滤载入子项、双版本并发原子更新、事务内子项 diff 同步、Permission.GetByNIdAsync/GetAllAsync)+ 迁移 11 步(9 建表 + 2 种子:17 权限目录 + development 租户 SYSTEM_ADMIN 系统角色 + 可选 bootstrap 管理员 env,幂等,BCrypt 因子 12 仅存哈希,绝不输出密码)+ `BcryptPasswordHasher`(BCrypt.Net-Next 4.0.3);DDL 按 DbType 分支(SQLite TEXT/INTEGER、PostgreSQL uuid/timestamptz),复合外键 `(id,is_deleted)` ON UPDATE CASCADE 同步子表影子列,部分唯一索引软删复用;SQLite 连接串 `Foreign Keys=True`;refresh_session/login_audit/operation_audit/outbox 仅建表 DDL;DI 注册 11 迁移步骤 + 3 仓储 + hasher | 2026-08-11 全量 build 0 警告 0 错误、test 313/313(BB 104、Identity 183、RefData 13、Gateway 13);Infrastructure.Tests 33(新增 27:迁移 9 + 仓储 13 + 哈希 5);PostgreSQL 真实验证「待验收」 ✅ |
| TASK-ID-005 | 登录、JWT、JWKS 与当前用户 API:Contracts `Authentication/AuthenticationContracts.cs`(LoginRequest 可空防 Required 推断、AuthSession/AuthUser、JwksDocument);Application `Authentication/`(选项 `AuthenticationOptions`、`AuthenticationException` 基类+5 派生错误码、端口 `IAuthenticationStore/IAccessTokenFactory/IJwksProvider/ILoginRateLimiter/ILoginAuditSink/IRefreshSessionStore`、`AuthenticationService` 登录编排:校验→IP 限流→防枚举(不存在用户与错误密码同错误)→持久锁+Redis 组合锁→RecordLoginSuccess→签发 `SES-` session NId+32B refresh token→刷新会话哈希落库→RS256 token→AuthUser+审计,密码/Token/哈希/用户是否存在绝不进 message)+`AddIdentityApplication`;Infrastructure `Security/`(`RsaSigningKeyProvider`:空配置临时密钥+告警、非法 PEM 启动失败 fail-closed、`AccessTokenFactory` RS256 kid 自动 Header、claims=sub(userNId)/user_name/tenant_id/role[](RoleNId)/sid/ver/jti、`JwksProvider`、`Hashing.Sha256Hex`)+`Authentication/`(LoginRateLimiter Redis 降级放行、LoginAuditSink/RefreshSessionStore 只存哈希)+实体×2+仓储扩展;Api `AuthController`(POST api/v1/auth/login、GET api/v1/auth/me、`[ResponseCache(NoStore=true)]`、ValidationException→400 ID_VALIDATION_FAILED/AuthenticationException→按码)、`AddIdentityAuthentication`(JwtBearer 公钥验签+iss/aud/lifetime/MapInboundClaims=false、OnChallenge/OnForbidden 统一信封)、`/.well-known/jwks.json` minimal API no-store、appsettings Identity:Jwt;CPM 钉 JwtBearer 10.0.10 | 2026-08-11 全量 build 0 警告 0 错误、test 354/354(BB 104、Identity 224、RefData 13、Gateway 13);Application.Tests 20、Infrastructure.Tests 48(新增 15)、Api.Tests 22(新增 4);`--vulnerable` 25/25 项目干净;登录 E2E(IDENTITY_E2E_DB=1)与 PostgreSQL 真实验证「待验收」 ✅ |
| TASK-ID-006 | Refresh Token 旋转、注销与撤销:Contracts `RefreshRequest/LogoutRequest/ChangePasswordRequest`;Application `Exceptions.cs` 新增 `RefreshTokenInvalidException`(401 ID_AUTH_REFRESH_INVALID)/`RefreshTokenReusedException`(401 ID_AUTH_REFRESH_REUSED)、`IRefreshSessionStore` 扩展(StoredRefreshSession/RefreshRotationStatus/FindByRawTokenAsync/RotateAsync/RevokeFamilyAsync/RevokeAllForUserAsync)、新增 `ISessionRevocationStore`(写 Redis `identity:session:revoked:{sid}` 尽力而为、校验 fail-closed 抛 503)、`IAuthenticationStore.FindByUserIdAsync`、`AuthenticationService` 四用例(RefreshAsync:哈希定位→sid 撤销校验→顺序重放撤销 Family+REUSED→已撤销/替换/过期 INVALID→用户状态→原子 RotateAsync→签发新 token(sid/ver);LogoutAsync 幂等;LogoutAllAsync/ChangePasswordAsync 推进 AuthVersion+撤销全部会话);Infrastructure `RefreshSessionStore` 旋转/撤销四方法(事务内先插替代再原子 UPDATE 守卫 `used_on IS NULL AND revoked_on IS NULL` 防并发/顺序重用)+新增 `SessionRevocationStore`(fail-closed)+`AuthenticationStore.FindByUserIdAsync`+DI;Api `AuthController` 新增 refresh(AllowAnonymous)/logout(logout 读 sid+exp 算 sid 撤销 TTL)/logout-all/change-password(读 sub),成功 ApiResult.Ok 信封;32B base64url token 只存 SHA-256、7 天 | 2026-08-11 全量 build 0 警告 0 错误、test 387/387(BB 104、Identity 257、RefData 13、Gateway 13);Application.Tests 41(新增 21)、Infrastructure.Tests 56(新增 8)、Api.Tests 26(新增 4);`--vulnerable` 25/25 项目干净;E2E(IDENTITY_E2E_DB=1)与 PostgreSQL/Redis 真实验证「待验收」 ✅ |

### 待实施 ⏳

| 任务 | 内容 |
| --- | --- |
| TASK-ID-007~016 | 服务端 RBAC/权限缓存、管理 API、Outbox 集成事件、前端接入与管理页、SSO 与联合验收 |

### 会话进度快照(2026-08-11)

- **当前状态:** TASK-ID-001~006 已完成并提交(develop),最新 `cd46e3c`(`feat(identity): add refresh rotation and session revocation`);全量 build 0 警告 0 错误、test 387/387、`--vulnerable` 25/25 项目干净;PostgreSQL/Redis 全链路(登录/刷新 E2E、撤销 fail-closed)「待验收」。
- **暂停点:** 用户选择暂停,下一步 **TASK-ID-007(服务端 RBAC、权限缓存与用户上下文)**,建议提交 `feat(identity): add server-side rbac and permission cache`;设计依据 03 文档 §14/§18 + BuildingBlocks `Security`。
- **ID-007 输入就绪:** token 已携带 `sub/user_name/tenant_id/role[]/sid/ver`;`AuthVersion` 递增(LogoutAllAsync/ChangePasswordHash)即权限缓存失效信号;`Identity` 自身端点直接读 token claims(`ICurrentUser.UserId` 为 Guid?,与 §12 `sub=UserNId` 存在已知偏差,见 TASK-ID-005 决策)。
- **GitHub Actions Bootstrap 环境变量竞态(已修复):** 2026-08-11 `backend-ci` 在 Ubuntu 上失败的实际步骤是 `dotnet test`,不是编译;`IdentityMigrationTests` 两个 bootstrap 用例期望创建 1 个管理员但得到 0。根因是多个 Infrastructure.Tests 测试类并行读写进程级 `IDENTITY_BOOTSTRAP_*` 环境变量,其他类的清理会竞态清空迁移用例刚设置的值。修复提交 `667bebc` 使用 `BootstrapEnvironmentTestGroup` xUnit Collection,只串行化 `IdentityMigrationTests`、`IdentityRepositoryTests`、`AuthenticationStoreTests`、`LoginAuditAndRefreshSessionStoreTests`、`RefreshSessionRotationTests`,不关闭整个测试程序集并行。后续任何读写同组环境变量的测试类必须加入该 Collection,不得依赖 Windows 本地偶然通过。验证:原失败测试 2/2 通过、Infrastructure.Tests 56/56 连续 3 轮通过、全量 build 0 警告 0 错误、test 387/387,GitHub Actions run `31474513470` 成功。

### 关键技术决策

- **Contracts 零引用**:`IndustrialPlatform.Identity.Contracts` 无任何 ProjectReference;集成事件在 TASK-ID-009 引入 EventBus 引用时再更新架构测试。
- **迁移执行框架容忍 DB 不可用**:`SchemaMigrationBackgroundService` 启动时执行迁移,捕获 DB 不可达异常记录告警跳过,保持无 Docker 服务可运行基线(TASK-BASE-006)。每个步骤 `BeginTran → Apply → 记账 → Commit`,失败回滚且不记账;失败步骤的部分 DDL 会随事务回滚(实测验证)。
- **依赖安全**:`Microsoft.AspNetCore.OpenApi` 的传递依赖 `Microsoft.OpenApi` 2.0.0 存在高严重性漏洞(GHSA-v5pm-xwqc-g5wc),已在 CPM 钉到 2.7.5(Identity.Api 与 Api.Tests 显式引用强制解析),NU1903 归零。
- **测试项目结构**:Domain/Application/Infrastructure/Api/Contract 五个测试项目;Infrastructure.Tests 用 SQLite 模拟集成测试,PostgreSQL 真实验证「待验收」。

实施方案见 `docs/implementation/02A-Industrial Platform可运行基线开发实施方案.md`。

### 已实现 ✅

| Task | 内容 | 验收 |
| --- | --- | --- |
| TASK-BASE-001 | 固化当前后端构建与测试基线 | 2026-08-09 SDK 10.0.302:restore/build 0 警告 0 错误;全量 test 74/74 通过(BuildingBlocks 64、Identity 5、ReferenceData 5);`--vulnerable` 审计全干净;`--deprecated` 发现 1 项偏差(见下) ✅ |
| TASK-BASE-003 | 统一开发配置(Serilog/SqlSugar/Redis/RabbitMQ 配置节)+ 依赖健康检查(`/health` `/health/live` `/health/ready`,Postgres/Redis/RabbitMQ/Seq,响应不含凭据)+ Redis 连接降级修复 | 2026-08-10 全量 build 0 警告 0 错误、test 140/140(BB 102、Identity 12、RefData 13、Gateway 13);真实依赖联调待有 Docker 环境 ✅ |
| TASK-BASE-004 | 统一 API 入口 Gateway(服务路由、平台健康聚合、统一错误、开发期 CORS) | 2026-08-10 全量 build 0 警告 0 错误、test 140/140(Gateway 13);统一入口 http://localhost:5080,路由 `/identity` `/referencedata`(前缀剥离),健康聚合/统一错误/CORS 见实施记录 ✅ |
| TASK-BASE-005 | 一键启动/停止/状态脚本 | 2026-08-10 无 Docker 环境实测通过(start/status/重复 start/端口冲突/stop 退出码符合预期);命令入口与规则见 `deploy/scripts/README.md` ✅ |
| TASK-BASE-006 | 新环境冒烟验收与前端 API 契约 | 2026-08-10 `smoke.ps1` 全流程 PASS(总耗时 52.9s、构建 0/0、test 140/140、三服务/网关转发/404 信封探测全过);前端契约(Base URL 5080、前缀、CORS、信封、错误码)见根 README「前端 API 契约」;Docker 留后续验收 ✅ |

### 待实施 ⏳

| Task | 内容 |
| --- | --- |
| TASK-BASE-002 | Docker Compose 本地基础设施(PostgreSQL/Redis/RabbitMQ/Seq)+ 健康检查 + 持久化卷(交付物已完成,真实联调与验收留 Docker 环境) |

### 已知偏差

- **xunit 2.9.3 被 NuGet 标记为 `Legacy`**(替代项 xunit.v3),影响 3 个测试项目(BuildingBlocks/Identity/ReferenceData.Tests)。不影响构建(0 警告)与测试(74/74);`--deprecated` 审计退出码仍为 0。迁移到 xunit.v3 涉及测试 SDK 集成与断言 API 变更,待独立任务处理,未在固化基线时强行迁移。

## 统一前端实施进度(02B)

### 已实现 ✅

| Task | 内容 | 验收 |
| --- | --- | --- |
| TASK-FE-001 | Vue3+TS+Vite 单包工程、严格 TS、Pinia/Router/Element Plus/Axios/Vitest/MSW/Playwright、八个稳定命令 | 2026-08-10 工具链由根 `.mise.toml` 钉定 node 24.18.0/pnpm 11.16.0;install --frozen-lockfile / format / lint / typecheck / unit(4/4)/ coverage(100%)/ build / E2E(1 passed)全过 ✅ |
| TASK-FE-002 | `createIndustrialApp()` 统一装配工厂、Design Token(`tokens.css`)、焦点/reduced-motion 基线、AppPage/AppEmptyState/AppErrorAlert/MockModeBanner | 2026-08-10 format/lint/typecheck 通过;unit 20/20;coverage 95/100/100/94.7(阈值 70);build + E2E 通过 ✅ |
| TASK-FE-003 | 类型安全运行配置(`parseRuntimeConfig`,生产禁 mock)、统一 HTTP 客户端(`createHttpClient`,X-Correlation-Id/Bearer 注入、信封解包)、统一错误层(`ApiError`/`normalizeError` 十类映射)、TraceId 提取、敏感日志脱敏 | 2026-08-10 format/lint/typecheck 通过;unit 71/71(新增 runtimeConfig/correlation/redact/errors 单元 + httpClient 契约);coverage 93.71/89.61/87.09/93.83(阈值 70);build + E2E 通过 ✅ |
| TASK-FE-004 | 认证边界(`AuthGateway` 契约 + 可复用契约测试套件)、`MockAuthGateway`(mock.admin/Mock@123456,三权限)、版本化会话存储(`industrial-platform.auth.mock.v1`,坏数据清理)、`AuthStore`(登录/恢复/刷新单飞/退出/权限,password 不入 Store/Storage)、`setAuthGateway/getCurrentSession` Phase 3 替换点 | 2026-08-10 format/lint/typecheck 通过;unit 112/112(新增 41);coverage 95.18/90.94/92.18/96.29(阈值 70);build + E2E 通过 ✅ |
| TASK-FE-005 | 终端识别(`detectTerminal` 三档宽度+触控、覆盖键 `industrial-platform.terminal.override.v1` pc/pda/mobile/auto、优先级 显式路由>覆盖>自动)、`deviceStore`(建议/生效/覆盖)、七条稳定路由 + `ROUTE_NAMES`、Route Meta 模块增强、唯一全局守卫(会话→权限→终端分流→标题) | 2026-08-10 format/lint/typecheck 通过;unit 141/141(新增 device/deviceStore/routerGuards 30);coverage 93.35/90.03/90.24/94.64(阈值 70);build + E2E 通过 ✅ |
| TASK-FE-006 | PC 管理框架:`layouts/PcLayout.vue`(56px 顶栏 + 240/64px 侧栏 + 主内容区 + 跳到主内容入口)、`components/navigation/**`(NavigationItem 模型 + 首页菜单 + 权限过滤 + 路由高亮 + 折叠态)、折叠持久化 `industrial-platform.pc.sidebar.collapsed.v1`、用户菜单/终端信息/Mock 横幅/退出入口;`/pc` 父路由挂载 PcLayout(FE-007 替换首页桩) | 2026-08-10 format/lint/typecheck 通过;unit 158/158(新增 PcLayout 10 + PcNavMenu 6);coverage 93.91/90.79/92.78/95.3(阈值 70);build + E2E 通过 ✅ |
| TASK-FE-007 | 登录页/403/404 公共页面 + PC Mock 首页:LoginPage(必填校验 + aria 错误关联、密码显隐、提交防重、统一错误、站内 redirect、密码不入存储)、ForbiddenPage(返回有权限首页/重新登录、TraceId 条件展示)、NotFoundPage(路径纯文本转义、返回首页/上一页、无历史回落)、PcHomePage(欢迎信息、终端/认证模式/数据来源、无伪指标空状态);装配按 authMode 注入 Mock 网关(http 抛错);E2E 11 用例 + 2 张 PC 首页截图 | 2026-08-10 format/lint/typecheck 通过;unit 182/182(新增 LoginPage 9 + ForbiddenPage 6 + NotFoundPage 5 + PcHomePage 4);coverage 95.71/92.21/94.53/96.58(阈值 70);build 通过;E2E 12/12 ✅ |
| TASK-FE-008 | PDA 基础壳:PdaLayout(48px 顶栏 + 返回/首页/退出 48px 触控按钮 + 用户/终端/Mock 标识 + 可滚动主区)、PdaHomePage(现场任务空状态,无扫码/称量/工单伪业务入口)、`/pda` 父路由挂载;返回无历史回落首页、退出回登录、横竖屏自适应 | 2026-08-10 format/lint/typecheck 通过;unit 195/195(新增 PdaLayout 8 + PdaHomePage 5);coverage 96.02/92.05/94.92/96.81(阈值 70);build 通过;E2E 19/19(新增 pda 7 用例 + 480×800/800×480 截图)✅ |
| TASK-FE-009 | Mobile 基础壳:MobileLayout(44px 顶栏 + 底部导航 首页/我的 Tab + safe-area padding-bottom + 终端/Mock 标识)、MobileHomePage(业务空状态,无任务/消息/审批假入口)、MobileMyPage(用户信息 + 44px 退出入口)、`/mobile` 父路由挂载 + `mobile-my` 新路由;root 桩改名 rootStub | 2026-08-10 format/lint/typecheck 通过;unit 212/212(新增 MobileLayout 8 + MobileHomePage 5 + MobileMyPage 4);coverage 96.32/91.81/95.36/97.04(阈值 70);build 通过;E2E 27/27(新增 mobile 8 用例 + 360×800/390×844 截图)✅ |
| TASK-FE-010 | 三端集成与第一批验收:新增 `screens.spec.ts`(六视口统一截图 + 无横向滚动 + safe-area Token 消费断言)与 `console.spec.ts`(§18.2 全流程无 console/page error + 敏感日志),补齐 favicon 集成点(`public/favicon.svg`),clean install 门禁(node 24.18.0/pnpm 11.16.0 经 `mise exec`),README 新增 Phase 3 Identity 接入清单 | 2026-08-10 clean install ✅;format:check/lint/typecheck 退出码 0;unit 212/212;coverage 96.32/91.81/95.36/97.04(阈值 70);build 通过;E2E 35/35(smoke 1+pc 12+pda 7+mobile 8+screens 7+console 1)✅ |

### 待实施 ⏳

| Task | 内容 |
| --- | --- |
| Phase 3(前端) | HttpAuthGateway + 真实 Identity 登录/刷新/撤销 + 权限映射 + preview E2E;接入清单见 `src/frontend/README.md`「Phase 3 Identity 接入清单」 |
| 外部环境待验收 | Docker 真实依赖联调(TASK-BASE-002)、真实 `env(safe-area-inset-bottom)` 与真机 PDA/Mobile、Identity 服务联调 |

### 已知偏差

- Element Plus 全量导入使主 chunk 1.02M(gzip 331.57k),build 有 chunk>500k 警告,第一批可接受,留待后续按需引入/代码分割。

## 目录速览

- `src/backend/src/BuildingBlocks/` — 7 个共享组件
- `src/backend/src/Services/{Identity,ReferenceData}/` — 服务骨架(Domain/Application/Infrastructure/Api),仅有 `/health`
- `src/backend/src/Gateway/IndustrialPlatform.Gateway/` — 统一 API 入口(YARP 反向代理 + 平台健康聚合 + 统一错误 + 开发期 CORS)
- `src/frontend/` — Vue3+TS+Vite 单包工程(PC/PDA/Mobile 统一前端,第一批三端基础壳已完成,见 02B 方案)
- `src/DEBUGGING.md` — 本地调试指南(后端 VS2026 / 前端 VS Code;无 Docker 环境的验证结论与已知预期)
- `tests/` — BuildingBlocks/Identity/ReferenceData/Gateway 测试项目 + 分类占位目录
- `docs/blueprint/` — 31 份蓝图;`docs/implementation/` — 实施文档;`docs/superpowers/` — 规格与计划
- `docker/` — 本地基础设施 Compose 编排(postgres:18-alpine / redis:7.4-alpine / rabbitmq:4-management / datalust/seq:2025)
- `deploy/scripts/` — 一键开发脚本 `dev.ps1`(start/stop/status,PID 文件 `.run/`)与冒烟脚本 `smoke.ps1`,见 `deploy/scripts/README.md`
