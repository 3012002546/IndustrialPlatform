# Identity Service

## 职责

Identity 拥有身份认证、JWT/JWKS、权限授权、用户/用户组/角色管理、登录审计、SSO、初始化系统目录与管理员引导。它是当前完整前后端管理功能的主要参考实现。

## 非职责

- 不拥有组织、岗位、业务主数据或 MES 流程。
- 不把权限缓存或 Redis 会话当成唯一业务事实。
- 不替其他服务执行 Migration/Seed，也不通过 Gateway 充当服务间调用总线。
- 不向 SystemData 返回管理员密码、Token、哈希或其他 Secret。

## 项目结构与调用链

| 层 | 目录与入口 |
| --- | --- |
| Domain | `IndustrialPlatform.Identity.Domain/`：用户、角色、权限、登录安全、SSO 聚合和规则 |
| Application | `IndustrialPlatform.Identity.Application/`：Authentication、Authorization、Management、UserGroups、Sso、Bootstrap 用例及端口 |
| Contracts | `IndustrialPlatform.Identity.Contracts/`：HTTP DTO 与版本化 Integration Event |
| Infrastructure | `IndustrialPlatform.Identity.Infrastructure/`：SqlSugar 持久化、JWT/JWKS、Redis、Outbox、Migration、Seed |
| API | `IndustrialPlatform.Identity.Api/Controllers/`、`Modules/IdentityModule.cs`、`Program.cs` |

典型用户管理链：`UsersController` → `IUserManagementService`/`UserManagementService` → Application 定义的 Store 端口 → Infrastructure Store/Repository → Identity 数据表和同事务 Outbox。Domain Event 只在 Identity 内部表达聚合变化；对外通知映射成 Contracts 中的 Integration Event。

## 运行入口

独立 API Host（分布式模式下由 Gateway 代理）：

```powershell
dotnet run --project src/backend/src/Services/Identity/IndustrialPlatform.Identity.Api/IndustrialPlatform.Identity.Api.csproj
Invoke-RestMethod http://localhost:5041/health/ready
```

统一模式由 UnifiedHost 通过 `AddIdentityModule` 加载，不应同时启动占用 `5041` 的独立 Identity Host。

## 依赖与配置

- PostgreSQL/SQLite：Identity 业务事实、Migration/Seed Ledger、审计和 Outbox。
- Redis：权限缓存、限流、刷新会话/撤销和 SSO 一次性状态；关键授权必须 fail-closed 或按既定数据库回退语义处理。
- JWT/JWKS：`Identity:Jwt`、认证扩展和签名密钥提供器。
- Seq/Serilog：结构化日志与 TraceId。
- `DatabaseTopology`：决定 Shared 或 PerService 物理目标，不改变 Identity 数据所有权。

只提交非敏感默认配置；本地 Secret 使用受控环境输入或私有 local 配置，禁止写进文档、日志和仓库。

## 数据初始化

当前入口在 `Infrastructure/Persistence/Migrations/` 和 Application `Bootstrap/`：Schema Migration → SystemBaseline → TenantBaseline → 按策略 SecretBootstrap(admin) → Verify。独立 Host 默认注册启动迁移；UnifiedHost 当前由 `ModuleMigrationCoordinatorHostedService` 按顺序协调。

Identity 拥有自己的 Migration、Seed、Bootstrap、Verify 和 Ledger。SystemData 未来只能通过中立初始化端口触发并保存脱敏 Observation，不能拥有这些实现。日常 readiness 最终应取 Identity 本地 ledger/数据库事实，不依赖 SystemData 在线。

## 测试入口

当前测试按层分布：

```powershell
dotnet test tests/Identity/IndustrialPlatform.Identity.Domain.Tests/IndustrialPlatform.Identity.Domain.Tests.csproj --configuration Release
dotnet test tests/Identity/IndustrialPlatform.Identity.Application.Tests/IndustrialPlatform.Identity.Application.Tests.csproj --configuration Release
dotnet test tests/Identity/IndustrialPlatform.Identity.Contract.Tests/IndustrialPlatform.Identity.Contract.Tests.csproj --configuration Release
dotnet test tests/Identity/IndustrialPlatform.Identity.Infrastructure.Tests/IndustrialPlatform.Identity.Infrastructure.Tests.csproj --configuration Release
dotnet test tests/Identity/IndustrialPlatform.Identity.Api.Tests/IndustrialPlatform.Identity.Api.Tests.csproj --configuration Release
```

工作包 3 会收敛为 `tests/Identity/IndustrialPlatform.Identity.Tests/`；在该工作包完成前以现有项目为准。前端对应测试位于 `src/frontend/tests/`。

## 常见问题排查

### 登录返回 401 或管理页面返回 403

- 现象 → 正确账号无法登录，或登录后管理接口被拒绝。
- 首先检查 → `/health/ready`、JWT 配置、令牌 claims、后端 PermissionCatalog 与前端 `PERMISSIONS` 是否一致。
- 执行命令 → `dotnet test tests/Identity/IndustrialPlatform.Identity.Api.Tests/IndustrialPlatform.Identity.Api.Tests.csproj --configuration Release --filter "FullyQualifiedName~AuthEndpointTests|FullyQualifiedName~PermissionAuthorizationTests"`
- 正常结果 → 登录端点测试和权限授权测试通过。
- 异常时下一步 → 分别检查认证、权限评估、数据库目录/Seed 和 Redis 状态，不得临时绕过授权。
相关代码入口 → `IndustrialPlatform.Identity.Api/Controllers/AuthController.cs`、`IndustrialPlatform.Identity.Api/Authorization/`、`IndustrialPlatform.Identity.Application/Authentication/`、`IndustrialPlatform.Identity.Application/Authorization/`。

### 启动迁移或管理员引导失败

- 现象 → Host 启动失败、readiness 503 或 admin 初始化命令失败。
- 首先检查 → DatabaseTopology、数据库可达性、本地 migration/seed ledger 及 Secret 输入。
- 执行命令 → `dotnet test tests/Identity/IndustrialPlatform.Identity.Infrastructure.Tests/IndustrialPlatform.Identity.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~Migration|FullyQualifiedName~Seed|FullyQualifiedName~Bootstrap"`
- 正常结果 → Migration、Seed 与 Bootstrap 的幂等/漂移规则测试通过。
- 异常时下一步 → 查看脱敏日志定位 Schema、checksum 或 Secret 缺失；禁止删除重建、固定默认密码或打印临时凭据。
相关代码入口 → `IndustrialPlatform.Identity.Infrastructure/Persistence/Migrations/`、`IndustrialPlatform.Identity.Infrastructure/Bootstrap/`、`IndustrialPlatform.Identity.Application/Bootstrap/`。
