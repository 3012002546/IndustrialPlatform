# BuildingBlocks

## 职责

BuildingBlocks 提供可被各服务复用、但不拥有具体业务语义的基础能力：共享内核与数据库拓扑值对象、应用层抽象、SqlSugar 仓储与工作单元、Redis 缓存和分布式锁、RabbitMQ 事件总线、Serilog/TraceId、安全上下文，以及 ASP.NET Core 的统一结果信封、异常与请求日志中间件。

优先从已有扩展方法和接口接入，不在业务服务中复制数据库、缓存、日志、安全或 Web 管线封装。

## 非职责

- 不保存 Identity、SystemData 或未来 MES 领域规则。
- 不把所有实体强制改造成复杂聚合；简单 CRUD 可使用与复杂度匹配的模型。
- 不定义跨服务业务契约；跨服务契约归拥有方或消费方端口。
- 不承担服务初始化编排。中立初始化协议由后续整改放在 `IndustrialPlatform.Application.Abstractions/Initialization/`，SystemData 只消费协议，各服务拥有实现。

## 项目结构与调用链

| 项目 | 主要入口 |
| --- | --- |
| `IndustrialPlatform.SharedKernel` | `Entities/`、`Events/`、`Results/`、`Topology/` |
| `IndustrialPlatform.Application.Abstractions` | 与业务无关的应用层契约；当前仅程序集标记，后续承载中立初始化协议 |
| `IndustrialPlatform.Infrastructure` | `Database/SqlSugarDbContext.cs`、`Repository/BaseRepository{TEntity}.cs`、`Caching/` |
| `IndustrialPlatform.EventBus` | `IEventBus`、`RabbitMqEventBus`、消费者后台服务和订阅管理 |
| `IndustrialPlatform.Logging` | `UseIndustrialSerilog`、`TraceIdEnricher` |
| `IndustrialPlatform.Security` | `ICurrentUser`、Claim 常量和注册扩展 |
| `IndustrialPlatform.Web` | `UseIndustrialWeb`、`AddIndustrialApi`、`ApiResult`、异常/日志中间件 |

典型调用链为：业务服务的 API/模块注册 → BuildingBlocks 扩展方法 → 具体基础设施实现。领域层只能依赖 SharedKernel，不应反向依赖 Web、Infrastructure 或 EventBus。

## 运行入口

BuildingBlocks 没有独立进程。由各独立 API Host、Gateway 或 UnifiedHost 在 `Program.cs`/模块注册类中组合。验证组合关系可运行：

```powershell
dotnet build src/backend/IndustrialPlatform.slnx --configuration Release
dotnet test tests/BuildingBlocks/IndustrialPlatform.BuildingBlocks.Tests/IndustrialPlatform.BuildingBlocks.Tests.csproj --configuration Release
```

## 依赖与配置

- SQL：服务通过 `AddSqlSugar` 及 `DatabaseTopology` 解析目标；数据所有权仍归服务。
- Redis：`AddRedis`、`ICacheService`、`IDistributedLock`；缓存不可成为业务事实唯一来源。
- RabbitMQ：`AddEventBus` 与 `IEventBus`；只发送版本化 Integration Event。
- Web：`AddIndustrialApi` 与 `UseIndustrialWeb` 统一信封、异常和请求日志。
- 日志/安全：使用 `UseIndustrialSerilog`、`AddCurrentUser`，不要在业务服务另建平行封装。

配置节和 Secret 值由宿主提供；不得在 BuildingBlocks 内固化服务地址、凭据或业务 Seed。

## 数据初始化

BuildingBlocks 不拥有业务 Migration、Seed、Bootstrap 或 Ledger。`ResolvedDatabaseTarget` 等拓扑类型只表达非敏感数据库目标。冻结边界是：SystemData 负责 `Topology + Orchestration + Policy + Observation`，目标服务负责 `Migration + Seed + Bootstrap + Verify + Ledger`。

工作包 4 将增加最小 `Inspect → Plan → Apply → Verify` 中立协议；协议不得携带 SQL、密码、管理员凭据、Seed Secret 或文件路径。

## 测试入口

```powershell
dotnet test tests/BuildingBlocks/IndustrialPlatform.BuildingBlocks.Tests/IndustrialPlatform.BuildingBlocks.Tests.csproj --configuration Release
```

涉及公共引用边界时重点运行 `ProjectReferenceArchitectureTests.cs`；涉及 Web/数据库/缓存/事件总线注册时运行同项目完整测试。修改公共组件后还需执行解决方案 Release Build 和受影响服务测试。

## 常见问题排查

### 服务启动时公共依赖未注册

- 现象 → 出现 `Unable to resolve service` 或中间件未生效。
- 首先检查 → 宿主 `Program.cs` 和服务 `Modules/*Module.cs` 是否调用现有注册扩展。
- 执行命令 → `rg -n "AddSqlSugar|AddRedis|AddEventBus|AddIndustrialApi|UseIndustrialWeb" src/backend/src -g '*.cs'`
- 正常结果 → 能从宿主/模块追踪到唯一 BuildingBlocks 注册入口。
- 异常时下一步 → 检查项目引用方向，运行 BuildingBlocks 测试；不要先在服务内复制实现。
相关代码入口 → `IndustrialPlatform.Infrastructure/Extensions/`、`IndustrialPlatform.EventBus/Extensions/`、`IndustrialPlatform.Web/Extensions/`。

### 数据库目标解析错误

- 现象 → 服务连接错误数据库或启动时报拓扑映射缺失。
- 首先检查 → 当前环境的 `DatabaseTopology` 与对应 `ServiceKey`。
- 执行命令 → `rg -n 'DatabaseTopology|ServiceDatabases|SharedDatabaseName|SharedSqliteFile' src/backend/src -g 'appsettings*.json'`
- 正常结果 → Shared 或 PerService 配置能由 `DatabaseTopologyResolver` 得到明确目标。
- 异常时下一步 → 对照蓝图 33；禁止通过回退数据库、默认密码或 `EnsureCreated` 绕过。
相关代码入口 → `IndustrialPlatform.SharedKernel/Topology/DatabaseTopologyResolver.cs`、`IndustrialPlatform.SharedKernel/Topology/ResolvedDatabaseTarget.cs`。
