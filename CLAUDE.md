# CLAUDE.md

Industrial Platform 的 Claude 开发指南。

## 项目概览

.NET 10 微服务平台(工业/MES 领域),Clean Architecture + DDD。服务依赖顺序:
`BuildingBlocks → Identity → ReferenceData → MasterData`。

当前里程碑:仅骨架与基础组件;业务功能、前端、Docker、部署均为后续工作。

## 协作约定

本项目为协作处理模式。当前只负责**代码及功能实现**;文档、架构设计、部署等其他工作由协作方负责,不主动处理。

## 常用命令

> Windows 需先设置本地 CLI home(否则 NuGet 恢复异常)。

```bash
export DOTNET_CLI_HOME="$PWD/.dotnet_cli_home"
dotnet restore src/backend/IndustrialPlatform.slnx
dotnet build src/backend/IndustrialPlatform.slnx --no-restore
dotnet test tests/BuildingBlocks/IndustrialPlatform.BuildingBlocks.Tests/IndustrialPlatform.BuildingBlocks.Tests.csproj --no-build
```

## 工程约束

- `Directory.Build.props`:`net10.0`、Nullable enable、**TreatWarningsAsErrors**、`AnalysisLevel=latest-recommended`。任何分析器警告都是错误,必须消除。
- `Directory.Packages.props`:Central Package Management(CPM)。新增第三方包需先在中央 props 声明版本,再在各 csproj 用无版本 `PackageReference`。
- `.editorconfig`:4 空格缩进;测试项目局部 `.editorconfig` 关闭 CA1707(允许测试方法名下划线命名)。
- **时间类型规范**:禁止使用 `DateTime`,一律使用 `DateTimeOffset`(保留时区偏移;获取当前时间用 `DateTimeOffset.UtcNow`)。
- 架构测试 `tests/BuildingBlocks/IndustrialPlatform.BuildingBlocks.Tests/ProjectReferenceArchitectureTests.cs` 锁定各 csproj 引用关系,改动引用会失败。
- BuildingBlocks 禁止包含 MES 业务逻辑(工单/称量/设备等)。依赖方向:Security→Web→Application.Abstractions→SharedKernel;Infrastructure→SharedKernel;EventBus→SharedKernel。

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

**当前测试:102 通过 / 0 失败**(BuildingBlocks 测试项目);全解决方案 140 通过 / 0 失败(BuildingBlocks 102、Identity 12、ReferenceData 13、Gateway 13)。

### 关键技术决策

- **CA1000**:泛型类型禁止静态成员 → `Result<T>` 的工厂方法(Ok/Fail)全部放在非泛型 `Result` 类上。
- **无约束泛型 `T? Data` 语义**:`Result<int>.Data` 是 `int`(值类型失败时为 default),`Result<string>.Data` 可为 null。测试按此编写。
- **NuGet 版本(已定)**:SqlSugarCore 5.1.4.216 / StackExchange.Redis 3.1.3 / RabbitMQ.Client 7.2.2(待 Task-007)/ Serilog 4.4.0 系列(待 Task-008),Microsoft.Extensions.* 10.0.10。已写入 `Directory.Packages.props`。
- **依赖安全(已清零)**:SqlSugarCore 升级到 5.1.4.216 后,其捆绑的可选提供程序(Oracle/SqlClient/System.Drawing/Newtonsoft 等)传递漏洞(NU1902/3/4)与 SQLite RID 警告(NETSDK1206)均消失;SQLitePCLRaw 全家(bundle/core/provider/lib.e_sqlite3)由 `SQLitePCLRaw.bundle_e_sqlite3` 钉到 2.1.12,修复 lib.e_sqlite3 2.1.11 的 GHSA-2m69-gcr7-jv3q 高危漏洞。`Directory.Build.props` 无任何 `WarningsNotAsErrors` 豁免,严格零警告基线;全 18 个项目 `--vulnerable`/`--deprecated` 审计均干净。
- **ICurrentUser.UserId 类型**:采用 `Guid`(与 SharedKernel `Entity.Id` 一致,不采用设计文档 §25 的 `long`),已在 Security 组件落地;声明类型常量见 `Security/ClaimConstants.cs`。
- **Entity 生命周期与双版本并发(TASK-BB-010)**:`Entity` 含 `IsFrozen/IsLocked/IsDeleted/EntityType/CreatedOn/LastUpdatedOn/OptimisticVersion(long)/ConcurrencyVersion(Guid)`;创建时 `CreatedOn == LastUpdatedOn` 取同一 `UtcNow`、`EntityType` 为完整类型名、双版本初始化。业务修改用 `EnsureCanModify()+Touch()`(protected),状态转换 `Freeze/Unfreeze/Lock/Unlock/MarkDeleted/Restore` 为 public 且幂等;状态冲突抛含操作名与实体类型的 `BusinessException`。更新/删除/恢复接口必须传调用方读取时的原始双版本,仓储以 `Id+IsDeleted+双版本` 原子 UPDATE,影响行数非 1 抛 `ConcurrencyException`;删除为软删除,默认查询排除已删除记录,物理删除只允许运维流程。
- **SqlSugar 时间测试替身限制**:SqlSugar 5.1.4 的 SQLite provider 存储 `DateTimeOffset` 时丢弃 UTC 偏移(读回为本地偏移),仓储时间断言按墙钟一致;PostgreSQL `timestamptz` 精确映射待 TASK-BASE-003 用真实库验收。
- **Redis 连接不急切抛异常(TASK-BASE-003)**:`AddRedis` 的 `IConnectionMultiplexer` 使用 `AbortOnConnectFail=false`,Redis 不可达时返回断开的复用器并后台重试,避免首次解析即抛异常导致健康检查工厂逃逸成 500(仅当连接建立后 `PingAsync` 才报错,由检查捕获为 Unhealthy)。
- **Gateway(TASK-BASE-004)**:YARP 2.3.0 反向代理(依赖方向仅 Logging/Web,不含业务)。统一入口 `http://localhost:5080`;`/identity`、`/referencedata` 前缀转发用 `PathRemovePrefix` transform 剥离(`PathPrefix` 是追加语义,踩坑点)。代理错误统一为 `ApiResult` 信封:`IForwarderErrorFeature` 中间件按 `ForwarderError` 映射 503「下游服务不可用」/ 504「网关转发请求超时」(YARP 2.3 无 `IProxyErrorHandler`),未匹配路由 fallback 404「路由不存在」。集群 `HttpRequest.ActivityTimeout` = `Gateway:RequestTimeoutSeconds`(默认 10s)驱动 504。平台健康聚合:`/health/ready` 对每个下游 GET `/health/ready`(超时 10s,匹配下游依赖全挂 ~6s 的最坏就绪耗时,3s 会临界截断),任一 Unhealthy 整体 503,响应不含凭据;`/health` 静态、`/health/live` 不查下游。开发期 CORS(`Gateway:Cors:AllowedOrigins`,默认 Vue3 dev 5173/preview 4173),预检在网关短路。测试配置注入须用 `UseSetting`(`ConfigureAppConfiguration` 晚于 Program 启动读取,对 minimal API 无效);.NET 10 无 `AddCheck(name, Func<IServiceProvider,IHealthCheck>)` 重载,参数化健康检查用 `AddTypeActivatedCheck<T>(name, ..., args: [...])`。

## 可运行基线实施进度

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

## 目录速览

- `src/backend/src/BuildingBlocks/` — 7 个共享组件
- `src/backend/src/Services/{Identity,ReferenceData}/` — 服务骨架(Domain/Application/Infrastructure/Api),仅有 `/health`
- `src/backend/src/Gateway/IndustrialPlatform.Gateway/` — 统一 API 入口(YARP 反向代理 + 平台健康聚合 + 统一错误 + 开发期 CORS)
- `src/frontend/` — Vue3 PC/PDA/Mobile 三端骨架(空目录)
- `tests/` — BuildingBlocks/Identity/ReferenceData/Gateway 测试项目 + 分类占位目录
- `docs/blueprint/` — 31 份蓝图;`docs/implementation/` — 实施文档;`docs/superpowers/` — 规格与计划
- `docker/` — 本地基础设施 Compose 编排(postgres:18-alpine / redis:7.4-alpine / rabbitmq:4-management / datalust/seq:2025)
- `deploy/scripts/` — 一键开发脚本 `dev.ps1`(start/stop/status,PID 文件 `.run/`)与冒烟脚本 `smoke.ps1`,见 `deploy/scripts/README.md`
