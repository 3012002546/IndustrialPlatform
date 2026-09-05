# ReferenceData Service

## 职责

ReferenceData 是一个 Service Host，包含 Dictionary、Parameter、DynamicProperty、Metadata、CodingRule、StateMachine、UnitOfMeasure 七个逻辑模块。当前实现覆盖领域规则、应用用例、版本化 Contracts、HTTP API、SqlSugar 持久化、Redis 缓存、服务级 Outbox、初始化与 readiness，并可由独立 Host 或 UnifiedHost 复用。

StateMachine 的正式领域命名为 `StateMachineDefinition` 根、`StateNode`、`StateTransition`；它只维护版本化定义并判定转换是否在定义上允许。业务服务拥有实例当前状态、权限、业务前置条件、事务与状态历史，不建设通用 `SetStatus`。UnitOfMeasure 使用 `UnitDimension` 根和随其 Revision 整份快照的 `UnitDefinition` 子项；每个单位保存 `FactorToBase`、`OffsetToBase`、`DecimalPlaces`、`RoundingMode`，不建立两两 Conversion 图或独立 Factor 表。物料专属包装比例仍归 MasterData。

## 非职责

- 不拥有物料、设备、工艺等主数据，也不接管状态机实例、业务事务或物料专属换算。
- 七个逻辑模块不机械拆成七套 Migration、Outbox、Inbox、连接或初始化账本；没有真实入站消费者时不创建 Inbox/Checkpoint。
- 不跨服务读取/写入 Identity、SystemData 或未来 MES 服务数据库。
- 不因 Shared 物理数据库而共享 Repository、表所有权或数据库外键。

## 项目结构与调用链

| 层 | 目录与入口 |
| --- | --- |
| Domain | `IndustrialPlatform.ReferenceData.Domain/`：七个模块的聚合、值对象和领域规则 |
| Application | `IndustrialPlatform.ReferenceData.Application/`：七个模块的用例、授权、缓存端口 |
| Contracts | `IndustrialPlatform.ReferenceData.Contracts/`：HTTP DTO 与版本化 Integration Event |
| Infrastructure | `IndustrialPlatform.ReferenceData.Infrastructure/`：持久化、Migration、缓存、Outbox、初始化 |
| API | `IndustrialPlatform.ReferenceData.Api/`：Controller/Endpoint、模块注册和健康端点 |

业务调用链是 Controller/Endpoint → Application 用例/端口 → Domain（需要领域规则时）→ Infrastructure Repository。七个模块通过进程内 Application 契约协作，不直接访问彼此 Repository/表，也不引入内部 HTTP 或 RabbitMQ。

## 运行入口

```powershell
dotnet run --project src/backend/src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Api/IndustrialPlatform.ReferenceData.Api.csproj
Invoke-RestMethod http://localhost:62311/health/ready
```

分布式模式经 Gateway `/referencedata/**`；统一模式由 UnifiedHost 加载并保持该前缀。

## 依赖与配置

- SqlSugar/PostgreSQL 或 SQLite：ReferenceData 自有业务事实、Migration/Seed Ledger 与 Outbox。
- Redis：缓存；缓存不是权威事实。
- RabbitMQ/EventBus：发布版本化 Integration Event；出现真实入站消费者前不预建消费基础设施。
- Seq/Serilog：日志和 TraceId。
- `DatabaseTopology`：物理目标选择；不改变服务级数据所有权。

## 数据初始化

当前边界是一个 `referencedata_db`、一个 PostgreSQL `reference_data` Schema、`dictionary_*`、`parameter_*`、`dynamic_property_*`、`metadata_*`、`coding_rule_*`、`state_machine_*`、`unit_of_measure_*` 模块表前缀、服务级 Migration/Ledger、一个带 `ModuleKey` 的 Outbox 和共享基础设施。七个模块共享一个初始化单元；没有实际入站事件消费者时不创建 Inbox/Checkpoint；只有某模块形成独立持久化生命周期并完成边界评审后才可拆分。

当前服务级迁移流版本为 `reference-data-2.7-011`，依次覆盖 baseline、七模块表、共享 Outbox、完整性约束与缓存 generation。PostgreSQL 使用 `reference_data.schema_migrations` 与 `reference_data.seed_ledger`，SQLite 使用等价的扁平表名；初始化器执行 `Inspect → Plan → Apply → Verify` 并校验迁移 checksum、目标身份和 baseline seed。

## 测试入口

```powershell
dotnet test tests/ReferenceData/IndustrialPlatform.ReferenceData.Tests/IndustrialPlatform.ReferenceData.Tests.csproj --configuration Release
```

测试覆盖七模块领域行为、HTTP 契约、持久化、缓存、Outbox、初始化、权限和健康端点；真实外部中间件链路归统一 IntegrationTests。

## 常见问题排查

### readiness 或 capability health 异常

- 现象 → `/health/ready` 因 PostgreSQL/初始化事实返回 503，或 `/health/capabilities` 报告 Redis、RabbitMQ、Seq 降级。
- 首先检查 → 两个健康端点、本地基础设施状态和 ReferenceData 的 Development 配置/拓扑。
- 执行命令 → 先运行 `docker compose -f docker/docker-compose.yml ps`，再运行 `dotnet test tests/ReferenceData/IndustrialPlatform.ReferenceData.Tests/IndustrialPlatform.ReferenceData.Tests.csproj --configuration Release --filter FullyQualifiedName~HealthEndpointTests`。
- 正常结果 → 数据库身份、Migration 与 RequiredSeed 决定 core readiness；Redis、RabbitMQ、Seq 作为 capability health 可报告 Degraded，不机械阻断 Ready。
- 异常时下一步 → 核对初始化 ledger/checksum、目标连接及可选依赖配置，不能简单删除检查或伪装 Healthy。
相关代码入口 → `IndustrialPlatform.ReferenceData.Api/Health/`、`IndustrialPlatform.ReferenceData.Api/Modules/ReferenceDataModule.cs`、`IndustrialPlatform.ReferenceData.Infrastructure/DependencyInjection.cs`。

### 新模块设计导致七套基础设施

- 现象 → 一个普通 ReferenceData 模块准备新增独立数据库、Migration 或 Outbox。
- 首先检查 → 是否真的具有独立持久化生命周期，而不只是逻辑领域边界。
- 执行命令 → `rg -n "ReferenceData|Initialization Unit|初始化单元" 'docs/blueprint/32-Industrial Platform Service Host与内部模块边界.md' 'docs/blueprint/33-Industrial Platform SystemData数据库编排与环境引导.md'`
- 正常结果 → 默认保持一个 Host、七个逻辑模块、服务级基础设施。
- 异常时下一步 → 停止实现并发起边界评审，不在功能提交中自行拆分。
相关代码入口 → `docs/blueprint/32-Industrial Platform Service Host与内部模块边界.md`、本 README 的项目结构。
