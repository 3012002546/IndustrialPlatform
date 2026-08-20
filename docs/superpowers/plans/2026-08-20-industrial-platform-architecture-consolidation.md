# Industrial Platform Architecture Consolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 用四个工作包对齐 Industrial Platform 的权威文档、接手指南、测试门禁和当前服务初始化代码，同时保持现有登录、权限、数据库编排和两种部署入口不回退。

**Architecture:** 保留七个 Service Host 长期规划和现有生产程序集边界；SystemData 负责拓扑、编排、策略与 Observation，各服务负责自己的 Migration、Seed、Bootstrap、Verify 与 Ledger。Gateway 继续作为分布式反向代理，UnifiedHost 继续作为当前统一进程组合宿主。

**Tech Stack:** .NET 10、ASP.NET Core、SqlSugar、YARP、PostgreSQL、SQLite、Redis、RabbitMQ、xUnit、Vue 3、TypeScript、Vitest、Playwright。

**Spec:** `docs/superpowers/specs/2026-08-20-industrial-platform-architecture-consolidation-design.md`

## Global Constraints

- 只执行下面四个工作包，不增加新的 PF、子计划或通用框架。
- 生产代码只覆盖 BuildingBlocks、Identity、SystemData、ReferenceData、Gateway、UnifiedHost 和直接相关前端入口。
- 不实现 MasterData、OperationalData、WorkOrder、Weighting、Trace、BatchRecord 等未来服务业务代码。
- 不重写 Identity/SystemData 已完成业务，不批量重构 `Entity`，不改变生产程序集分层。
- Gateway 不加载业务模块、不托管 SPA、不执行迁移；UnifiedHost 不运行 YARP、不代理下游、不承担业务编排。
- 保留工作区既存改动；不得暂存或提交 `CLAUDE.md`、`DSH.md`、私有配置、构建输出、测试产物和 QA 截图。
- 源码变化后必须先运行全新 Release Build，再运行 `--no-build` 测试；不能用旧产物声明通过。
- 测试项目收敛以关键行为不回退为前提，不以测试数量减少为验收目标。
- 每个工作包只创建一次范围内提交；任何范围外发现只记录，不顺带实现。

---

### Work Package 1: 对齐蓝图与开发 Todo

**Files:**

- Modify: `docs/blueprint/09-Industrial Platform开发总TodoList.md`
- Modify: `docs/blueprint/29-Industrial Platform自动化测试体系.md`
- Modify: `docs/blueprint/32-Industrial Platform Service Host与内部模块边界.md`
- Modify: `docs/blueprint/33-Industrial Platform SystemData数据库编排与环境引导.md`
- Modify: `docs/blueprint/README.md`
- Modify: `docs/implementation/05-Industrial Platform SystemData开发实施方案.md`
- Modify: `docs/implementation/06-Industrial Platform ReferenceData Service开发实施方案.md`
- Modify: `docs/implementation/TEMPLATE-开发实施方案.md`
- Modify: `docs/status/CURRENT.md`
- Modify: `README.md`

**Interfaces:**

- Consumes: 本计划对应设计规格及当前 Git/构建/测试基线。
- Produces: 唯一有效的服务边界、初始化所有权、测试分层和“架构收敛整改”Todo 阶段。

- [ ] **Step 1: 保存实施前基线，不修改任何文件**

Run:

```powershell
git status --short --branch
git log -3 --oneline --decorate
dotnet build src/backend/IndustrialPlatform.slnx --configuration Release
dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build
pnpm --dir src/frontend test:unit
```

Expected: 记录每条命令退出码和测试失败数；若已有失败，将其列为既存基线，不在本包修复。

- [ ] **Step 2: 只在权威文档冻结已批准规则**

Required content:

```text
SystemData = Topology + Orchestration + Policy + Observation
Service = Migration + Seed + Bootstrap + Verify + Ledger
Runtime readiness = local database fact
Service Host != Domain Module != Initialization Unit != Deployment Unit
ReferenceData = one host + five logical modules + service-level infrastructure
Initialization = Standard | Advanced
```

在 `09` 中只增加一个“架构收敛整改”阶段，内部引用本计划的四个工作包；完成后仍从 PF-03 继续。其他文档只修正与上述规则直接冲突的段落。

- [ ] **Step 3: 修正当前状态与入口描述**

Required state:

```text
Identity 当前范围已完成
SystemData 已完成范围与实际 Git 一致
ReferenceData 仍为服务骨架
统一部署入口 = UnifiedHost
分布式部署入口 = Gateway
```

删除 README 和状态文档中已经失效的“Identity/统一前端未实现”等描述，不重写历史 evidence。

- [ ] **Step 4: 检查文档冲突与链接**

Run:

```powershell
rg -n "SystemData.*业务.*迁移|Gateway.*托管.*前端|UnifiedHost.*YARP|每个模块.*独立.*Outbox|每个模块.*独立.*迁移" README.md docs/blueprint docs/implementation docs/status
rg -n "\]\([^)]*\.md" README.md docs/blueprint docs/implementation docs/status
git diff --check
```

Expected: 第一条只允许历史说明或明确否定句；文档相对路径均指向现有文件；`git diff --check` 无输出。

- [ ] **Step 5: 提交工作包 1**

```powershell
git add README.md docs/blueprint docs/implementation docs/status/CURRENT.md
git diff --cached --name-status
git commit -m "docs: align architecture boundaries and roadmap"
```

Expected: 暂存内容只包含本工作包列出的正式文档。

---

### Work Package 2: 补齐可接手架构文档与代码交接

**Files:**

- Create: `docs/DEVELOPMENT.md`
- Create: `src/backend/src/BuildingBlocks/README.md`
- Create: `src/backend/src/Services/Identity/README.md`
- Create: `src/backend/src/Services/SystemData/README.md`
- Create: `src/backend/src/Services/ReferenceData/README.md`
- Modify: `src/backend/src/Gateway/README.md`
- Create: `src/backend/src/Hosts/IndustrialPlatform.UnifiedHost/README.md`
- Create: `docs/agents/ARCHITECTURE-REMEDIATION-HANDOFF.md`
- Modify: `README.md`

**Interfaces:**

- Consumes: 工作包 1 的权威边界和当前真实代码路径。
- Produces: 面向维护者的开发入口、组件 README、排障路径和供代码执行者使用的单一交接说明。

- [ ] **Step 1: 编写六个组件 README**

每份 README 固定使用以下八个标题，不增加服务级子文档：

```markdown
## 职责
## 非职责
## 项目结构与调用链
## 运行入口
## 依赖与配置
## 数据初始化
## 测试入口
## 常见问题排查
```

排障条目固定写成：

```text
现象 → 首先检查 → 执行命令 → 正常结果 → 异常时下一步 → 相关代码入口
```

Gateway README 必须说明 YARP、路由前缀、CORS、下游健康和代理错误；UnifiedHost README 必须说明模块组合、SPA、路径兼容和启动迁移协调。两份文档不得互相复制职责。

- [ ] **Step 2: 编写 `docs/DEVELOPMENT.md` 的前后端功能路径**

Required flow:

```text
功能归属 → 权限/API 契约 → 后端四层 → Migration/Seed
→ 前端 types/API/page/router/menu/permission → 测试 → 双入口验证
```

必须用当前 Identity 管理页面和现有目录作为路径示例，解释 PostgreSQL、Redis、RabbitMQ、SignalR、Outbox、Domain Event、Integration Event 的适用条件；不创建示例业务代码。

- [ ] **Step 3: 编写跨服务协作与未来业务链**

Required rules:

```text
同步查询/命令 = consumer-owned port + versioned contract
状态变化 = Outbox + Integration Event
同宿主模块 = module contract / in-process event
长流程 = owning business domain process manager
多服务查询 = projection/read model
Shared physical database != shared ownership
```

用 `MasterData → WorkOrder → Weighting → Trace/BatchRecord` 解释校验、快照、幂等、超时、有限重试、契约版本、TraceId、补偿和最终一致性。

- [ ] **Step 4: 编写代码整改交接说明**

`ARCHITECTURE-REMEDIATION-HANDOFF.md` 只包含工作包 3、4 的允许文件、禁止范围、关键接口、验证命令、停止条件和既存改动保护，不复述全部蓝图。

- [ ] **Step 5: 验证文档可执行性并提交**

Run:

```powershell
$paths = @(
  'docs/DEVELOPMENT.md',
  'src/backend/src/BuildingBlocks/README.md',
  'src/backend/src/Services/Identity/README.md',
  'src/backend/src/Services/SystemData/README.md',
  'src/backend/src/Services/ReferenceData/README.md',
  'src/backend/src/Gateway/README.md',
  'src/backend/src/Hosts/IndustrialPlatform.UnifiedHost/README.md',
  'docs/agents/ARCHITECTURE-REMEDIATION-HANDOFF.md'
)
$paths | ForEach-Object { if (-not (Test-Path $_)) { throw "Missing: $_" } }
rg -n "现象|首先检查|执行命令|相关代码入口" $paths
git diff --check
```

Expected: 八份文档存在，组件 README 均包含可执行排障路径，所有引用命令和代码路径可在仓库找到。

Commit:

```powershell
git add README.md docs/DEVELOPMENT.md docs/agents/ARCHITECTURE-REMEDIATION-HANDOFF.md src/backend/src/BuildingBlocks/README.md src/backend/src/Services/Identity/README.md src/backend/src/Services/SystemData/README.md src/backend/src/Services/ReferenceData/README.md src/backend/src/Gateway/README.md src/backend/src/Hosts/IndustrialPlatform.UnifiedHost/README.md
git commit -m "docs: add service ownership and development guide"
```

---

### Work Package 3: 收敛测试项目与门禁

**Files:**

- Create: `tests/Identity/IndustrialPlatform.Identity.Tests/IndustrialPlatform.Identity.Tests.csproj`
- Create: `tests/SystemData/IndustrialPlatform.SystemData.Tests/IndustrialPlatform.SystemData.Tests.csproj`
- Create: `tests/IntegrationTests/IndustrialPlatform.IntegrationTests/IndustrialPlatform.IntegrationTests.csproj`
- Move: `tests/Identity/IndustrialPlatform.Identity.*.Tests/**/*.cs` into `tests/Identity/IndustrialPlatform.Identity.Tests/`
- Move: `tests/SystemData/IndustrialPlatform.SystemData.*.Tests/**/*.cs` and `IndustrialPlatform.SystemData.Testing/**/*.cs` into `tests/SystemData/IndustrialPlatform.SystemData.Tests/`
- Move: external PostgreSQL/Redis/RabbitMQ tests into `tests/IntegrationTests/IndustrialPlatform.IntegrationTests/`
- Modify: `src/backend/IndustrialPlatform.slnx`
- Modify: `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Domain/AssemblyMarker.cs`
- Modify: `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/AssemblyMarker.cs`
- Modify: `tests/UnitTests/README.md`
- Modify: `tests/IntegrationTests/README.md`
- Delete: merged Identity/SystemData test `.csproj` files and empty directories

**Interfaces:**

- Consumes: 工作包 1 的三档测试门禁和工作包 2 的测试入口说明。
- Produces: 六个常规服务测试项目加一个 IntegrationTests 项目；保留关键行为，不改变生产程序集边界。

- [ ] **Step 1: 建立可比较的测试清单**

Run:

```powershell
rg --files tests -g '*.csproj'
rg -n "\[(Fact|Theory)\b" tests -g '*.cs'
dotnet build src/backend/IndustrialPlatform.slnx --configuration Release
dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build --logger "console;verbosity=minimal"
```

Expected: 保存项目列表、通过/失败/跳过总数和关键失败；若 Release Build 失败，不开始移动测试。

- [ ] **Step 2: 创建三个目标项目**

Identity/SystemData 常规项目引用各自 Api、Application、Contracts、Domain、Infrastructure；IntegrationTests 引用当前三个服务 API、Gateway、UnifiedHost，并包含 `Npgsql`、`SqlSugarCore`、`Microsoft.AspNetCore.Mvc.Testing` 和 xUnit。公共测试包版本继续由仓库集中配置，不在各项目写新版本号。

Update friend assemblies:

```csharp
[assembly: InternalsVisibleTo("IndustrialPlatform.SystemData.Tests")]
[assembly: InternalsVisibleTo("IndustrialPlatform.IntegrationTests")]
```

删除旧测试程序集名称，只保留生产程序集之间原有的 `InternalsVisibleTo`。

- [ ] **Step 3: 迁移高价值测试，先不删除旧文件**

迁移顺序固定为 Domain → Application → Contract → Infrastructure → API。文件名冲突时按能力目录放置，例如：

```text
IndustrialPlatform.Identity.Tests/Domain/
IndustrialPlatform.Identity.Tests/Application/
IndustrialPlatform.Identity.Tests/Infrastructure/
IndustrialPlatform.Identity.Tests/Api/
IndustrialPlatform.SystemData.Tests/DatabaseOrchestration/
IndustrialPlatform.SystemData.Tests/Administration/
```

真实 PostgreSQL、Redis、RabbitMQ 和跨服务测试添加：

```csharp
[Trait("Category", "Integration")]
```

Run after each service migration:

```powershell
dotnet test tests/Identity/IndustrialPlatform.Identity.Tests/IndustrialPlatform.Identity.Tests.csproj --configuration Release
dotnet test tests/SystemData/IndustrialPlatform.SystemData.Tests/IndustrialPlatform.SystemData.Tests.csproj --configuration Release
```

Expected: 登录、权限、管理员保护、迁移、Seed、Ledger、拓扑、编排和 readiness 测试通过。

- [ ] **Step 4: 删除重复测试和旧项目**

只删除设计规格允许的六类低价值测试。每次删除前，在提交说明草稿中记录被哪个保留测试覆盖。更新 `IndustrialPlatform.slnx`，使其中只引用：BuildingBlocks、Identity、SystemData、ReferenceData、Gateway、UnifiedHost、IntegrationTests 七个后端测试项目。

- [ ] **Step 5: 运行三档门禁并提交**

Run:

```powershell
dotnet build src/backend/IndustrialPlatform.slnx --configuration Release
dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build --filter "Category!=Integration"
dotnet test tests/IntegrationTests/IndustrialPlatform.IntegrationTests/IndustrialPlatform.IntegrationTests.csproj --configuration Release --no-build --filter "Category=Integration"
pnpm --dir src/frontend test:unit
git diff --check
```

Expected: Release Build 和常规测试必须通过；IntegrationTests 只有在依赖不可用时可作为明确环境阻塞，不得隐藏代码失败。

Commit:

```powershell
git add src/backend/IndustrialPlatform.slnx src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Domain/AssemblyMarker.cs src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/AssemblyMarker.cs tests/Identity tests/SystemData tests/IntegrationTests tests/UnitTests/README.md
git diff --cached --name-status
git commit -m "test: consolidate service test projects"
```

---

### Work Package 4: 对齐当前服务初始化与 Readiness

**Files:**

- Create: `src/backend/src/BuildingBlocks/IndustrialPlatform.Application.Abstractions/Initialization/ServiceInitializationContracts.cs`
- Create: `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Bootstrap/IdentityServiceInitializer.cs`
- Create: `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Application/DatabaseOrchestration/Initialization/IServiceInitializationInvoker.cs`
- Create: `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/DatabaseOrchestration/Initialization/InProcessServiceInitializationInvoker.cs`
- Create: `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/DatabaseOrchestration/Initialization/HttpServiceInitializationInvoker.cs`
- Create: `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/DatabaseOrchestration/Initialization/SystemDataServiceInitializer.cs`
- Modify: `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/DatabaseOrchestration/Runner/ServiceInitializerExecutor.cs`
- Create: `src/backend/src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Infrastructure/Initialization/ReferenceDataServiceInitializer.cs`
- Create: `src/backend/src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Infrastructure/Initialization/ReferenceDataInitializationLedger.cs`
- Create: `src/backend/src/BuildingBlocks/IndustrialPlatform.Web/Initialization/InternalInitializationAuthentication.cs`
- Create: `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Api/Controllers/InternalInitializationController.cs`
- Create: `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Api/Controllers/InternalInitializationController.cs`
- Create: `src/backend/src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Api/Controllers/InternalInitializationController.cs`
- Modify: service module/DI files under the three current services
- Modify: `src/backend/src/Hosts/IndustrialPlatform.UnifiedHost/ModuleMigrationCoordinatorHostedService.cs`
- Modify: `src/backend/src/Hosts/IndustrialPlatform.UnifiedHost/Program.cs`
- Test: merged test projects created by Work Package 3

**Interfaces:**

- Consumes: `ResolvedDatabaseTarget`,现有 Identity 初始化服务、SystemData Runner/Observation、现有服务迁移与 Seed 实现。
- Produces: 中立服务初始化契约、三服务自有初始化器、进程内/HTTP 调用适配器、本地 readiness 和不依赖迁移实现类的 UnifiedHost 协调器。

- [ ] **Step 1: 先写防回归与新契约失败测试**

Required tests:

```csharp
[Fact]
public async Task Ready_service_does_not_require_systemdata_to_be_online();

[Fact]
public async Task Gateway_only_proxies_and_does_not_load_service_modules();

[Fact]
public async Task UnifiedHost_runs_initializers_in_identity_systemdata_referencedata_order();

[Fact]
public async Task Service_initializer_context_contains_no_connection_password_or_seed_secret();

[Fact]
public async Task Replayed_operation_id_is_idempotent();
```

Run the focused tests and confirm the new contract tests fail because the interfaces or behavior do not yet exist; existing login、权限、SystemData 编排和 Gateway/UnifiedHost 测试必须继续通过。

- [ ] **Step 2: 添加中立初始化契约**

Implement these exact public shapes in `ServiceInitializationContracts.cs`:

```csharp
public enum ServiceInitializationPolicy { Standard, Advanced }

public sealed record ServiceInitializationContext(
    string EnvironmentName,
    string TenantNId,
    string OperationNId,
    string ServiceKey,
    string ModuleKey,
    ResolvedDatabaseTarget DatabaseTarget,
    string DesiredVersion,
    ServiceInitializationPolicy Policy,
    string TraceId);

public sealed record ServiceInitializationState(
    string ServiceKey,
    string ModuleKey,
    string? ObservedVersion,
    bool MigrationReady,
    bool RequiredSeedReady,
    bool BootstrapReady,
    bool Ready,
    string? Reason);

public sealed record ServiceInitializationPlan(
    string ServiceKey,
    string ModuleKey,
    string? CurrentVersion,
    string DesiredVersion,
    bool RequiresApply,
    IReadOnlyList<string> Steps);

public interface IServiceInitializer
{
    string ServiceKey { get; }
    string ModuleKey { get; }
    Task<ServiceInitializationState> InspectAsync(ServiceInitializationContext context, CancellationToken cancellationToken);
    Task<ServiceInitializationPlan> PlanAsync(ServiceInitializationContext context, ServiceInitializationState inspection, CancellationToken cancellationToken);
    Task<ServiceInitializationState> ApplyAsync(ServiceInitializationContext context, ServiceInitializationPlan plan, CancellationToken cancellationToken);
    Task<ServiceInitializationState> VerifyAsync(ServiceInitializationContext context, CancellationToken cancellationToken);
}
```

No contract may carry SQL、connection password、administrator credential、seed secret value or filesystem path.

- [ ] **Step 3: 封装三个服务自己的初始化器**

Identity initializer wraps `IdentityInitializationService` and `BootstrapService` and discards any one-time credential from its control-plane response; SystemData initializer wraps its existing startup migration/ledger code. ReferenceData creates only service-level `reference_data_schema_migrations` and `reference_data_seed_ledger`, applies the single baseline version `reference-data-baseline-v1`, and does not create five module infrastructures. Each initializer must use its own repository/ledger and return only `ServiceInitializationState`.

Run:

```powershell
dotnet test tests/Identity/IndustrialPlatform.Identity.Tests/IndustrialPlatform.Identity.Tests.csproj --configuration Release --filter FullyQualifiedName~Initializer
dotnet test tests/SystemData/IndustrialPlatform.SystemData.Tests/IndustrialPlatform.SystemData.Tests.csproj --configuration Release --filter FullyQualifiedName~Initializer
dotnet test tests/ReferenceData/IndustrialPlatform.ReferenceData.Tests/IndustrialPlatform.ReferenceData.Tests.csproj --configuration Release --filter FullyQualifiedName~Initializer
```

Expected: 每个服务可独立 Inspect/Plan/Apply/Verify，重复 OperationId 不重复写 Migration/Seed Ledger。

- [ ] **Step 4: 将 SystemData 占位执行器改为调用端口**

Implement:

```csharp
public interface IServiceInitializationInvoker
{
    Task<ServiceInitializationState> InspectAsync(ServiceInitializationContext context, CancellationToken cancellationToken);
    Task<ServiceInitializationPlan> PlanAsync(ServiceInitializationContext context, ServiceInitializationState inspection, CancellationToken cancellationToken);
    Task<ServiceInitializationState> ApplyAsync(ServiceInitializationContext context, ServiceInitializationPlan plan, CancellationToken cancellationToken);
    Task<ServiceInitializationState> VerifyAsync(ServiceInitializationContext context, CancellationToken cancellationToken);
}
```

`ServiceInitializerExecutor` must translate the existing Runner request into this port and persist only the existing target ledger plus脱敏 Observation. Keep `SqlSeedBundleExecutor` only for SystemData-owned/legacy SQL bundles; it must not become the owner of another service's domain migration.

- [ ] **Step 5: 配置进程内与分布式适配器**

UnifiedHost selects `InProcessServiceInitializationInvoker` from registered `IServiceInitializer` instances. Independent SystemData selects `HttpServiceInitializationInvoker` from configured internal service URLs.

Internal endpoints use a dedicated authentication scheme with header:

```text
X-Industrial-Initialization-Key
```

The key is configuration/secret input only, compared in constant time, never written to `appsettings*.json`, logs, Operation, Observation or response. A missing/invalid key returns 401. Requests require `OperationNId`; replay returns the same ledger-backed result.

- [ ] **Step 6: 让 UnifiedHost 只依赖初始化器**

Replace direct references to `IdentityStartupMigrations` and `SystemDataStartupMigrations` in `ModuleMigrationCoordinatorHostedService` with ordered `IServiceInitializer` calls:

```text
identity → systemdata → referencedata
```

Gateway code must remain free of service module references, static-file hosting and migration services. Add or retain tests that enforce both boundaries.

- [ ] **Step 7: 运行完整防回归门禁**

Run:

```powershell
dotnet build src/backend/IndustrialPlatform.slnx --configuration Release
dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build --filter "Category!=Integration"
pnpm --dir src/frontend test:unit
pnpm --dir src/frontend exec playwright test tests/e2e/real-login.spec.ts --config=playwright.real.config.ts
git diff --check
```

Where infrastructure is available, also run:

```powershell
dotnet test tests/IntegrationTests/IndustrialPlatform.IntegrationTests/IndustrialPlatform.IntegrationTests.csproj --configuration Release --no-build --filter "Category=Integration"
```

Expected: Identity 登录/权限/管理员保护、SystemData 编排、Gateway 路由、UnifiedHost 登录入口和初始化顺序全部通过。任何关键回归立即停止，不提交本工作包。

- [ ] **Step 8: 更新交接结果并提交工作包 4**

Update only the result/status portions of `docs/agents/ARCHITECTURE-REMEDIATION-HANDOFF.md` and `docs/status/CURRENT.md` with fresh command results and remaining external-environment checks.

```powershell
git add src/backend/src/BuildingBlocks/IndustrialPlatform.Application.Abstractions/Initialization/ServiceInitializationContracts.cs src/backend/src/BuildingBlocks/IndustrialPlatform.Web/Initialization/InternalInitializationAuthentication.cs src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Bootstrap/IdentityServiceInitializer.cs src/backend/src/Services/Identity/IndustrialPlatform.Identity.Api/Controllers/InternalInitializationController.cs src/backend/src/Services/Identity/IndustrialPlatform.Identity.Api/Modules/IdentityModule.cs src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/DependencyInjection.cs src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Application/DatabaseOrchestration/Initialization/IServiceInitializationInvoker.cs src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/DatabaseOrchestration/Initialization src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/DatabaseOrchestration/Runner/ServiceInitializerExecutor.cs src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Api/Controllers/InternalInitializationController.cs src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Api/Modules/SystemDataModule.cs src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/DependencyInjection.cs src/backend/src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Infrastructure/Initialization src/backend/src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Api/Controllers/InternalInitializationController.cs src/backend/src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Api/Modules/ReferenceDataModule.cs src/backend/src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Infrastructure/DependencyInjection.cs src/backend/src/Hosts/IndustrialPlatform.UnifiedHost/ModuleMigrationCoordinatorHostedService.cs src/backend/src/Hosts/IndustrialPlatform.UnifiedHost/Program.cs tests/Identity/IndustrialPlatform.Identity.Tests tests/SystemData/IndustrialPlatform.SystemData.Tests tests/ReferenceData/IndustrialPlatform.ReferenceData.Tests tests/Gateway/IndustrialPlatform.Gateway.Tests tests/UnifiedHost/IndustrialPlatform.UnifiedHost.Tests docs/agents/ARCHITECTURE-REMEDIATION-HANDOFF.md docs/status/CURRENT.md
git diff --cached --name-status
git diff --cached --check
git commit -m "refactor: align service initialization ownership"
```

Expected: staged files belong only to this work package; unrelated frontend styling、调试配置、local secrets、QA artifacts remain unstaged.

---

## Completion Gate

The consolidation is complete only when:

- Four work-package commits exist and contain no unrelated files.
- Authority docs, service READMEs and `docs/DEVELOPMENT.md` match actual code paths.
- Backend Release Build and regular tests pass from fresh artifacts.
- Frontend unit tests pass.
- Integration/E2E results are either passing or explicitly blocked by a named external dependency; code failures cannot be waived.
- Gateway distributed path and UnifiedHost consolidated path each pass a separate login/routing smoke check.
- `HEAD...origin/develop` and remaining local modifications are reported accurately; implementation does not push or force-push unless separately authorized.
