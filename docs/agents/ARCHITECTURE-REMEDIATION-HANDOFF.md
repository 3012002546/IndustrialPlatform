# 架构收敛整改代码交接

本文只交接架构收敛实施计划的工作包 3、4。权威架构与规则读取：

- `docs/superpowers/specs/2026-08-20-industrial-platform-architecture-consolidation-design.md`
- `docs/superpowers/plans/2026-08-20-industrial-platform-architecture-consolidation.md`
- `docs/blueprint/29-Industrial Platform自动化测试体系.md`
- `docs/blueprint/32-Industrial Platform Service Host与内部模块边界.md`
- `docs/blueprint/33-Industrial Platform SystemData数据库编排与环境引导.md`

不要从本文扩展或重新裁决蓝图。

## 1. 工作包 3：测试项目收敛

### 允许文件

创建：

- `tests/Identity/IndustrialPlatform.Identity.Tests/IndustrialPlatform.Identity.Tests.csproj`
- `tests/SystemData/IndustrialPlatform.SystemData.Tests/IndustrialPlatform.SystemData.Tests.csproj`
- `tests/IntegrationTests/IndustrialPlatform.IntegrationTests/IndustrialPlatform.IntegrationTests.csproj`

移动/整理：

- `tests/Identity/IndustrialPlatform.Identity.*.Tests/**/*.cs` → `tests/Identity/IndustrialPlatform.Identity.Tests/`
- `tests/SystemData/IndustrialPlatform.SystemData.*.Tests/**/*.cs` 与 `IndustrialPlatform.SystemData.Testing/**/*.cs` → `tests/SystemData/IndustrialPlatform.SystemData.Tests/`
- 真实 PostgreSQL/Redis/RabbitMQ 测试 → `tests/IntegrationTests/IndustrialPlatform.IntegrationTests/`

修改：

- `src/backend/IndustrialPlatform.slnx`
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Domain/AssemblyMarker.cs`
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/AssemblyMarker.cs`
- `tests/UnitTests/README.md`
- `tests/IntegrationTests/README.md`

删除：仅合并完成且高价值行为已由目标项目验证覆盖的旧 Identity/SystemData 测试 `.csproj`、重复低价值测试和空目录。删除前记录原覆盖意图及替代测试。

### 目标结构与关键行为

后端测试最终保留 BuildingBlocks、Identity、SystemData、ReferenceData、Gateway、UnifiedHost、IntegrationTests 七个项目。Identity 登录/权限/管理员保护/Migration/Seed/Ledger 与 SystemData 拓扑/编排/readiness 必须先迁移并通过，再删除旧文件。真实中间件测试标注 `[Trait("Category", "Integration")]`。

### 验证命令

```powershell
rg --files tests -g '*.csproj'
rg -n "\[(Fact|Theory)\b" tests -g '*.cs'
dotnet build src/backend/IndustrialPlatform.slnx --configuration Release
dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build --filter "Category!=Integration"
dotnet test tests/IntegrationTests/IndustrialPlatform.IntegrationTests/IndustrialPlatform.IntegrationTests.csproj --configuration Release --no-build --filter "Category=Integration"
pnpm --dir src/frontend test:unit
git diff --check
```

Release Build 与常规测试必须通过。IntegrationTests 只有在明确命名的外部依赖不可用时可报告环境阻塞，不能隐藏编译或代码失败。

## 2. 工作包 4：服务初始化与 readiness

### 允许文件

创建：

- `src/backend/src/BuildingBlocks/IndustrialPlatform.Application.Abstractions/Initialization/ServiceInitializationContracts.cs`
- `src/backend/src/BuildingBlocks/IndustrialPlatform.Web/Initialization/InternalInitializationAuthentication.cs`
- `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Bootstrap/IdentityServiceInitializer.cs`
- `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Api/Controllers/InternalInitializationController.cs`
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Application/DatabaseOrchestration/Initialization/IServiceInitializationInvoker.cs`
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/DatabaseOrchestration/Initialization/InProcessServiceInitializationInvoker.cs`
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/DatabaseOrchestration/Initialization/HttpServiceInitializationInvoker.cs`
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/DatabaseOrchestration/Initialization/SystemDataServiceInitializer.cs`
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Api/Controllers/InternalInitializationController.cs`
- `src/backend/src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Infrastructure/Initialization/ReferenceDataServiceInitializer.cs`
- `src/backend/src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Infrastructure/Initialization/ReferenceDataInitializationLedger.cs`
- `src/backend/src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Api/Controllers/InternalInitializationController.cs`

修改：

- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/DatabaseOrchestration/Runner/ServiceInitializerExecutor.cs`
- 三个当前服务的 `Api/Modules/*Module.cs` 与 `Infrastructure/DependencyInjection.cs`
- `src/backend/src/Hosts/IndustrialPlatform.UnifiedHost/ModuleMigrationCoordinatorHostedService.cs`
- `src/backend/src/Hosts/IndustrialPlatform.UnifiedHost/Program.cs`
- 工作包 3 收敛后的 Identity、SystemData、ReferenceData、Gateway、UnifiedHost 测试项目
- `docs/agents/ARCHITECTURE-REMEDIATION-HANDOFF.md` 和 `docs/status/CURRENT.md` 的结果/状态部分

若实现需要修改清单外生产文件，停止扩张并交由 Codex 裁决；不得自行扩大为公共框架重构。

### 关键接口与边界

BuildingBlocks 的公共协议只允许：

```text
ServiceInitializationPolicy = Standard | Advanced
ServiceInitializationContext（非敏感环境/租户/Operation/服务/模块/ResolvedDatabaseTarget/版本/TraceId）
ServiceInitializationState
ServiceInitializationPlan
IServiceInitializer = Inspect / Plan / Apply / Verify
```

SystemData Application 定义 `IServiceInitializationInvoker`，四个方法与初始化器阶段一一对应。消费方业务代码依赖 Port：UnifiedHost 使用进程内适配器，独立 SystemData 使用受信 HTTP 适配器。

冻结所有权：

```text
SystemData = Topology + Orchestration + Policy + Observation
Service = Migration + Seed + Bootstrap + Verify + Ledger
Runtime readiness = local database fact
```

- `ServiceInitializerExecutor` 只翻译既有 Runner 请求并保存目标 ledger/脱敏 Observation。
- `SqlSeedBundleExecutor` 只保留 SystemData 自有或 legacy SQL bundle，不执行其他服务领域迁移。
- 内部 HTTP 使用 `X-Industrial-Initialization-Key`、常量时间比较、OperationNId 幂等；Key 不进入 appsettings、日志、Operation、Observation 或响应。
- UnifiedHost 只按 `identity → systemdata → referencedata` 调用初始化器。
- Gateway 保持纯 YARP，不得出现业务模块引用、静态文件托管或迁移服务。

### 必须先写的防回归测试

```text
Ready_service_does_not_require_systemdata_to_be_online
Gateway_only_proxies_and_does_not_load_service_modules
UnifiedHost_runs_initializers_in_identity_systemdata_referencedata_order
Service_initializer_context_contains_no_connection_password_or_seed_secret
Replayed_operation_id_is_idempotent
```

先确认新契约测试因接口/行为不存在而失败，同时现有登录、权限、SystemData 编排和 Gateway/UnifiedHost 边界测试继续通过，再实现生产代码。

### 验证命令

```powershell
dotnet build src/backend/IndustrialPlatform.slnx --configuration Release
dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build --filter "Category!=Integration"
pnpm --dir src/frontend test:unit
pnpm --dir src/frontend exec playwright test tests/e2e/real-login.spec.ts --config=playwright.real.config.ts
git diff --check
```

基础设施可用时另运行：

```powershell
dotnet test tests/IntegrationTests/IndustrialPlatform.IntegrationTests/IndustrialPlatform.IntegrationTests.csproj --configuration Release --no-build --filter "Category=Integration"
```

## 3. 禁止范围

- 不实现 MasterData、OperationalData、WorkOrder、Weighting、Trace、BatchRecord 或 ReferenceData 五模块业务代码。
- 不重写 Identity/SystemData 已完成业务，不批量重构 `Entity`，不改变生产程序集分层。
- 不把七个 Service Host 改成模块化单体，不新增通用跨服务框架。
- 不让 Gateway 加载模块/SPA/迁移，不让 UnifiedHost 运行 YARP/代理下游/承担业务编排。
- 不跨服务共享 Repository、外键或直接写表，不因 Shared 物理数据库弱化所有权。
- 不输出或提交 local Secret、服务器地址、账号、密码、密钥、临时管理员凭据。
- 不处理蓝图/Todo 或工作包 4 清单外的当前状态变更。

## 4. 停止条件

出现任一情况立即停止当前工作包，不提交：

1. fresh Release Build 失败，或 CS2012/access denied 锁未释放且未重新 Build。
2. Identity 登录、权限、管理员保护回归。
3. SystemData 拓扑/编排/readiness 关键行为回归。
4. Gateway 路由/代理边界或 UnifiedHost 登录、路径兼容、初始化顺序回归。
5. 测试迁移后高价值行为未通过，却准备删除旧测试/项目。
6. 需要修改允许文件以外的生产边界，或权威文档与计划存在不可消解冲突。
7. 外部依赖不可用但无法区分环境失败与代码失败。

记录完整命令、退出码、失败数和关键错误；保留工作树交回 Codex，不通过跳过测试、使用旧产物或扩大范围继续。

## 5. 既存改动保护

开始和提交前都运行：

```powershell
git status --short --branch --untracked-files=all
git diff --name-status
git diff --cached --name-status
git status --ignored --short
```

截至工作包 2 开始时，工作树已有且不属于本次架构整改提交的改动包括：`DSH.md`、`src/DEBUGGING.md`、多个 Development/launchSettings 文件、前端 shell/首页/登录页及测试、`tests/Directory.Build.props`、`tests/TestDevelopmentInfrastructureMode.cs`、BuildingBlocks 测试辅助改动、QA 截图和 `design-qa.md`。这些文件可能继续变化；以每次开始时的 status 为准。

禁止清理、覆盖、暂存或顺带提交既存改动。尤其不得暂存 `CLAUDE.md`、`DSH.md`、私有配置、`bin/`、`obj/`、`TestResults/`、前端构建输出、缓存、运行日志和 QA 产物。若既存改动与工作包 3/4 的允许路径重叠，先保留并向 Codex 报告，不自行覆盖或归因。

每个工作包只精确 `git add` 计划列出的路径，提交前读取 `git diff --cached --name-status` 和 `git diff --cached --check`。不得推送、force-push、rebase、merge 或 cherry-pick。

## 6. Work Package 4 结果

- 状态：已完成。服务初始化契约、上下文/状态均保持中立且不携带连接密码、种子秘密或文件路径；Identity、SystemData、ReferenceData 分别拥有本服务的 Migration、Seed、Bootstrap、Verify 与 Ledger。
- SystemData：通过 `IServiceInitializationInvoker` 端口提供进程内与 HTTP 适配器；Runner 仅经端口调用并写入脱敏目标种子台账。内部初始化端点统一使用 `X-Industrial-Initialization-Key`，缺失或错误返回 401，并使用常量时间比较。
- 入口边界：UnifiedHost 按 Identity → SystemData → ReferenceData 顺序协调初始化，不直接依赖具体迁移实现；Gateway 保持 YARP/CORS/路由/下游健康的纯代理边界，不加载业务模块、SPA 或初始化器。
- Readiness：各服务基于本地数据库事实判断，不依赖 SystemData 在线；初始化按 `OperationNId` 重放幂等。
- 验收：fresh Release Build 0 警告/0 错误；静态审查修正后的常规后端 1227/1227；五项最低测试及既有登录、权限、管理员保护、SystemData 编排、Gateway 路由、UnifiedHost 登录入口覆盖通过。UnifiedHost 已统一 Inspect → Plan → Apply（按需）→ Verify 核心路径并拒绝 Verify 未就绪；ReferenceData Inspect 不写库；Identity/SystemData 仅识别明确的 missing-table，single-flight 重放失败/取消/NotReady 不永久缓存。IntegrationTests 8/8 为外部环境门控未启用时的早退；前端 Vitest 因受限沙箱写入 `.vite-temp` 返回 EPERM；real-login E2E 因 Playwright runner 不可用未执行。
- 本工作包未实现未来业务服务、不推送；既有工作区改动继续保留，提交前后均须确认暂存区仅含 WP4 文件且最终为空。
