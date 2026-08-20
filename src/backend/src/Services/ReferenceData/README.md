# ReferenceData Service

## 职责

ReferenceData 是一个 Service Host，长期包含 Dictionary、Parameter、Metadata、DynamicProperty、CodingRule 五个逻辑模块。当前代码仅为服务骨架：四层项目、基础设施注册、健康端点以及供独立 Host/UnifiedHost 复用的模块入口。

## 非职责

- 当前不声称已实现五个模块的业务 API、页面或领域模型。
- 五个逻辑模块不机械拆成五套 Migration、Outbox、Inbox 或初始化账本。
- 不跨服务读取/写入 Identity、SystemData 或未来 MES 服务数据库。
- 不因 Shared 物理数据库而共享 Repository、表所有权或数据库外键。

## 项目结构与调用链

| 层 | 目录与当前状态 |
| --- | --- |
| Domain | `IndustrialPlatform.ReferenceData.Domain/`，当前骨架 |
| Application | `IndustrialPlatform.ReferenceData.Application/`，当前骨架 |
| Infrastructure | `IndustrialPlatform.ReferenceData.Infrastructure/DependencyInjection.cs`，SqlSugar/Redis/RabbitMQ 注册 |
| API | `IndustrialPlatform.ReferenceData.Api/Program.cs`、`Modules/ReferenceDataModule.cs`、`Health/` |

未来业务调用链仍必须是 Controller → Application 用例/端口 → Domain（需要领域规则时）→ Infrastructure Repository。五个模块通过公开契约或进程内事件协作，不直接访问彼此表。

## 运行入口

```powershell
dotnet run --project src/backend/src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Api/IndustrialPlatform.ReferenceData.Api.csproj
Invoke-RestMethod http://localhost:62311/health/ready
```

分布式模式经 Gateway `/referencedata/**`；统一模式由 UnifiedHost 加载并保持该前缀。业务端点仍待 PF-03，不应把健康端点等同于业务完成。

## 依赖与配置

- SqlSugar/PostgreSQL 或 SQLite：未来 ReferenceData 自有数据。
- Redis：缓存；缓存不是权威事实。
- RabbitMQ/EventBus：未来版本化 Integration Event 发布/消费。
- Seq/Serilog：日志和 TraceId。
- `DatabaseTopology`：物理目标选择；不改变服务级数据所有权。

## 数据初始化

目标边界是服务级 Migration、Outbox、Inbox 和基础设施，当前五模块共享一个初始化单元。只有某模块形成独立持久化生命周期并完成边界评审后才可拆分。

工作包 4 只会增加 ReferenceData 服务级初始化与本地 readiness 骨架，使用 `reference_data_schema_migrations`、`reference_data_seed_ledger` 和单一 `reference-data-baseline-v1`；不会实现五个业务模块。

## 测试入口

```powershell
dotnet test tests/ReferenceData/IndustrialPlatform.ReferenceData.Tests/IndustrialPlatform.ReferenceData.Tests.csproj --configuration Release
```

当前测试验证配置、健康端点和服务边界。新增业务时需增加领域/应用行为、API 契约和持久化测试；真实外部中间件链路归统一 IntegrationTests。

## 常见问题排查

### readiness 返回 RabbitMQ/Redis/PostgreSQL 异常

- 现象 → `/health/ready` 返回 503，并显示某依赖 Unhealthy。
- 首先检查 → 本地基础设施状态和 ReferenceData 的 Development 配置/拓扑。
- 执行命令 → 先运行 `docker compose -f docker/docker-compose.yml ps`，再运行 `dotnet test tests/ReferenceData/IndustrialPlatform.ReferenceData.Tests/IndustrialPlatform.ReferenceData.Tests.csproj --configuration Release --filter FullyQualifiedName~HealthEndpointTests`。
- 正常结果 → 容器 healthy，健康检查测试通过；无基础设施时 liveness 仍可用而 readiness 明确失败。
- 异常时下一步 → 逐项检查目标地址和非敏感配置；不要关闭健康检查伪装 Ready。
相关代码入口 → `IndustrialPlatform.ReferenceData.Api/Health/`、`IndustrialPlatform.ReferenceData.Api/Modules/ReferenceDataModule.cs`、`IndustrialPlatform.ReferenceData.Infrastructure/DependencyInjection.cs`。

### 新模块设计导致五套基础设施

- 现象 → 一个普通 Dictionary/Parameter 功能准备新增独立数据库、Migration 或 Outbox。
- 首先检查 → 是否真的具有独立持久化生命周期，而不只是逻辑领域边界。
- 执行命令 → `rg -n "ReferenceData|Initialization Unit|初始化单元" 'docs/blueprint/32-Industrial Platform Service Host与内部模块边界.md' 'docs/blueprint/33-Industrial Platform SystemData数据库编排与环境引导.md'`
- 正常结果 → 默认保持一个 Host、五个逻辑模块、服务级基础设施。
- 异常时下一步 → 停止实现并发起边界评审，不在功能提交中自行拆分。
相关代码入口 → `docs/blueprint/32-Industrial Platform Service Host与内部模块边界.md`、本 README 的项目结构。
