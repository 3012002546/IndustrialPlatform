# ReferenceData Service

## 职责

ReferenceData 是一个 Service Host，长期包含 Dictionary、Parameter、DynamicProperty、Metadata、CodingRule、StateMachine、UnitOfMeasure 七个逻辑模块。当前代码仅为服务骨架：四层项目、基础设施注册、健康端点以及供独立 Host/UnifiedHost 复用的模块入口。

StateMachine 的正式领域命名为 `StateMachineDefinition` 根、`StateNode`、`StateTransition`；它只维护版本化定义并判定转换是否在定义上允许。业务服务拥有实例当前状态、权限、业务前置条件、事务与状态历史，不建设通用 `SetStatus`。UnitOfMeasure 使用 `UnitDimension` 根和随其 Revision 整份快照的 `UnitDefinition` 子项；每个单位保存 `FactorToBase`、`OffsetToBase`、`DecimalPlaces`、`RoundingMode`，不建立两两 Conversion 图或独立 Factor 表。物料专属包装比例仍归 MasterData。

## 非职责

- 当前不声称已实现七个模块的业务 API、页面或领域模型。
- 七个逻辑模块不机械拆成七套 Migration、Outbox、Inbox、连接或初始化账本；没有真实入站消费者时不创建 Inbox/Checkpoint。
- 不跨服务读取/写入 Identity、SystemData 或未来 MES 服务数据库。
- 不因 Shared 物理数据库而共享 Repository、表所有权或数据库外键。

## 项目结构与调用链

| 层 | 目录与当前状态 |
| --- | --- |
| Domain | `IndustrialPlatform.ReferenceData.Domain/`，当前骨架 |
| Application | `IndustrialPlatform.ReferenceData.Application/`，当前骨架 |
| Infrastructure | `IndustrialPlatform.ReferenceData.Infrastructure/DependencyInjection.cs`，SqlSugar/Redis/RabbitMQ 注册 |
| API | `IndustrialPlatform.ReferenceData.Api/Program.cs`、`Modules/ReferenceDataModule.cs`、`Health/` |

未来业务调用链仍必须是 Controller → Application 用例/端口 → Domain（需要领域规则时）→ Infrastructure Repository。七个模块通过进程内公开 Application 契约协作，不直接访问彼此 Repository/表，也不引入内部 HTTP 或 RabbitMQ。

## 运行入口

```powershell
dotnet run --project src/backend/src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Api/IndustrialPlatform.ReferenceData.Api.csproj
Invoke-RestMethod http://localhost:62311/health/ready
```

分布式模式经 Gateway `/referencedata/**`；统一模式由 UnifiedHost 加载并保持该前缀。业务端点仍待 PF-03，不应把健康端点等同于业务完成。

## 依赖与配置

- SqlSugar/PostgreSQL 或 SQLite：未来 ReferenceData 自有数据。
- Redis：缓存；缓存不是权威事实。
- RabbitMQ/EventBus：未来版本化 Integration Event 发布；出现真实入站消费者前不预建消费基础设施。
- Seq/Serilog：日志和 TraceId。
- `DatabaseTopology`：物理目标选择；不改变服务级数据所有权。

## 数据初始化

目标边界是一个 `referencedata_db`、一个 PostgreSQL `reference_data` Schema、`dictionary_*`、`parameter_*`、`dynamic_property_*`、`metadata_*`、`coding_rule_*`、`state_machine_*`、`unit_of_measure_*` 模块表前缀、服务级 Migration/Ledger、一个带 `ModuleKey` 的 Outbox 和共享基础设施。当前七个模块共享一个初始化单元；没有实际入站事件消费者时不创建 Inbox/Checkpoint；只有某模块形成独立持久化生命周期并完成边界评审后才可拆分。

当前骨架使用 `reference_data_schema_migrations`、`reference_data_seed_ledger` 和单一 `reference-data-baseline-v1` 占位事实，尚不是 PF-03 的正式迁移实现。TASK-RD-001 将其收敛为一个服务级迁移流；PostgreSQL 目标表为 `reference_data.schema_migrations` 与 `reference_data.seed_ledger`，SQLite 使用等价全名。

## 测试入口

```powershell
dotnet test tests/ReferenceData/IndustrialPlatform.ReferenceData.Tests/IndustrialPlatform.ReferenceData.Tests.csproj --configuration Release
```

当前测试验证配置、健康端点和服务边界。新增业务时需增加领域/应用行为、API 契约和持久化测试；真实外部中间件链路归统一 IntegrationTests。

## 常见问题排查

### readiness 返回 RabbitMQ/Redis/PostgreSQL 异常

- 现象 → 当前骨架的 `/health/ready` 会把 PostgreSQL、Redis、RabbitMQ、Seq 一并计入并可能返回 503；这是 PF-03 前的已知实现差距。
- 首先检查 → 本地基础设施状态和 ReferenceData 的 Development 配置/拓扑。
- 执行命令 → 先运行 `docker compose -f docker/docker-compose.yml ps`，再运行 `dotnet test tests/ReferenceData/IndustrialPlatform.ReferenceData.Tests/IndustrialPlatform.ReferenceData.Tests.csproj --configuration Release --filter FullyQualifiedName~HealthEndpointTests`。
- 目标结果 → TASK-RD-001 后，数据库身份/Migration/RequiredSeed/Bootstrap 决定 core readiness；Redis、RabbitMQ、Seq 进入 capability health，能够安全回源、积压或降级时报告 Degraded 而不机械阻断 Ready。
- 异常时下一步 → 在任务实施前只按当前行为排查；实施时通过健康检查标签/谓词收敛，不能简单删除检查或伪装 Healthy。
相关代码入口 → `IndustrialPlatform.ReferenceData.Api/Health/`、`IndustrialPlatform.ReferenceData.Api/Modules/ReferenceDataModule.cs`、`IndustrialPlatform.ReferenceData.Infrastructure/DependencyInjection.cs`。

### 新模块设计导致七套基础设施

- 现象 → 一个普通 ReferenceData 模块准备新增独立数据库、Migration 或 Outbox。
- 首先检查 → 是否真的具有独立持久化生命周期，而不只是逻辑领域边界。
- 执行命令 → `rg -n "ReferenceData|Initialization Unit|初始化单元" 'docs/blueprint/32-Industrial Platform Service Host与内部模块边界.md' 'docs/blueprint/33-Industrial Platform SystemData数据库编排与环境引导.md'`
- 正常结果 → 默认保持一个 Host、七个逻辑模块、服务级基础设施。
- 异常时下一步 → 停止实现并发起边界评审，不在功能提交中自行拆分。
相关代码入口 → `docs/blueprint/32-Industrial Platform Service Host与内部模块边界.md`、本 README 的项目结构。
