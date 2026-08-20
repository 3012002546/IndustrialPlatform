# SystemData Service

## 职责

SystemData 当前拥有组织、岗位、用户任职，以及数据库拓扑与服务初始化控制面。初始化边界固定为 `Topology + Orchestration + Policy + Observation`：登记、解析目标、生成计划、环境/审批/备份策略、Operation 状态、Runner 调度和脱敏 Observation。

## 非职责

- 不拥有其他服务的业务 Schema、Migration、Seed、Bootstrap、Verify 或 Ledger。
- 不保存或透传 SQL、数据库密码、管理员凭据、Seed Secret、任意命令或文件路径作为通用控制面契约。
- 不跨服务 Repository、直接写其他服务表，Shared 物理数据库也不例外。
- 不作为业务服务中介、跨服务 Saga 所有者或日常 readiness 的唯一事实源。

## 项目结构与调用链

| 层 | 目录与入口 |
| --- | --- |
| Domain | `IndustrialPlatform.SystemData.Domain/Organizations`、`Positions`、`Assignments`、`DatabaseOrchestration` |
| Application | 对应用例、Store/Port，以及 `DatabaseOrchestration/Runner/` 端口 |
| Contracts | `Administration/`、`DatabaseOrchestration/` 的 API 契约 |
| Infrastructure | `Persistence/`、`DatabaseOrchestration/`、`Topology/`、Migration 和 Runner 适配器 |
| API | `Controllers/`、`Authorization/`、`Modules/SystemDataModule.cs`、`Program.cs` |

管理调用链：Controller → Application Service → Application Store Port → Infrastructure Store → SystemData 自有表。初始化控制面链：Controller → `DatabaseOrchestrationService`/`DatabasePlanService` → Operation/Store → `DatabaseOperationRunner` → 目标适配端口 → 保存脱敏 Observation。

## 运行入口

```powershell
dotnet run --project src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Api/IndustrialPlatform.SystemData.Api.csproj
Invoke-RestMethod http://localhost:5042/health/ready
```

分布式模式下外部客户端应访问 Gateway 的 `/systemdata/**`；统一模式由 UnifiedHost 加载模块并保持同一外部前缀。

## 依赖与配置

- SystemData 自有数据库：组织、岗位、任职、registration/plan/operation/observation 和本地 Migration。
- `DatabaseTopology`：解析 Shared/PerService 目标；Test/Staging/Production 的限制以蓝图 33 为准。
- Redis/Seq：运行依赖和健康检查。
- `DatabaseOrchestration` 配置：Runner、产物/凭据适配和策略；Secret 只能来自受控输入。
- JWT/权限：复用 Identity 签发的身份上下文，SystemData 自己执行权限策略。

## 数据初始化

SystemData 自身存在 bootstrap 例外：基础设施只创建最小数据库/角色，SystemData 再运行自己的显式 Migration 与 SystemBaseline。它自己的 ledger 是运行事实。

对其他服务的目标流程是 `Registration → ResolveTopology → Inspect → Plan → Apply → Verify → Observation`。当前 `ServiceInitializerExecutor` 仍是待工作包 4 整改的占位边界；不得把现状描述成最终服务自有初始化已经完成。工作包 4 将引入消费端口及进程内/HTTP 适配器，SystemData 只持久化已有目标 ledger 与脱敏 Observation。

## 测试入口

当前测试项目：

```powershell
dotnet test tests/SystemData/IndustrialPlatform.SystemData.Domain.Tests/IndustrialPlatform.SystemData.Domain.Tests.csproj --configuration Release
dotnet test tests/SystemData/IndustrialPlatform.SystemData.Application.Tests/IndustrialPlatform.SystemData.Application.Tests.csproj --configuration Release
dotnet test tests/SystemData/IndustrialPlatform.SystemData.Contract.Tests/IndustrialPlatform.SystemData.Contract.Tests.csproj --configuration Release
dotnet test tests/SystemData/IndustrialPlatform.SystemData.Infrastructure.Tests/IndustrialPlatform.SystemData.Infrastructure.Tests.csproj --configuration Release
dotnet test tests/SystemData/IndustrialPlatform.SystemData.Api.Tests/IndustrialPlatform.SystemData.Api.Tests.csproj --configuration Release
```

工作包 3 将其收敛到 `tests/SystemData/IndustrialPlatform.SystemData.Tests/`，真实 PostgreSQL/Redis/RabbitMQ 测试移入统一 IntegrationTests；完成前不要使用未来路径作为当前门禁。

## 常见问题排查

### 初始化 Operation 长时间排队或失败

- 现象 → plan/apply 返回 OperationId 后状态不推进，或出现脱敏失败。
- 首先检查 → Runner HostedService、Operation step、环境策略、目标指纹和依赖可达性。
- 执行命令 → `dotnet test tests/SystemData/IndustrialPlatform.SystemData.Application.Tests/IndustrialPlatform.SystemData.Application.Tests.csproj --configuration Release --filter "FullyQualifiedName~DatabaseOperationRunner|FullyQualifiedName~EnvironmentGate"`
- 正常结果 → Runner 状态迁移和环境拒绝规则测试通过。
- 异常时下一步 → 再运行 Infrastructure Runner 测试并检查脱敏日志；不要直接改目标服务表或跳过审批/备份门禁。
相关代码入口 → `IndustrialPlatform.SystemData.Application/DatabaseOrchestration/Runner/`、`IndustrialPlatform.SystemData.Infrastructure/DatabaseOrchestration/Runner/`、`IndustrialPlatform.SystemData.Api/Controllers/ServiceInitializationController.cs`。

### SystemData readiness 为 503

- 现象 → `/health/ready` 返回 503。
- 首先检查 → 响应中的 postgres/redis/seq 检查及 SystemData 自己的 migration 事实。
- 执行命令 → `dotnet test tests/SystemData/IndustrialPlatform.SystemData.Api.Tests/IndustrialPlatform.SystemData.Api.Tests.csproj --configuration Release --filter FullyQualifiedName~HealthEndpointTests`
- 正常结果 → liveness 不依赖外部组件，readiness 能准确报告本服务依赖。
- 异常时下一步 → 检查对应依赖和 `DatabaseTopology`；其他已初始化服务是否 Ready 必须由其本地事实判断，不能由 SystemData 状态代替。
相关代码入口 → `IndustrialPlatform.SystemData.Api/Health/`、`IndustrialPlatform.SystemData.Infrastructure/Persistence/Migrations/`、`IndustrialPlatform.SystemData.Api/Modules/SystemDataModule.cs`。
