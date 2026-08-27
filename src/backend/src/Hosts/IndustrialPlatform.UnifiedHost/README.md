# UnifiedHost

## 职责

UnifiedHost 是当前默认的统一进程部署入口。它在单一 ASP.NET Core 进程中组合 Identity、SystemData、ReferenceData 模块，注册统一日志、异常、认证授权、OpenAPI 和健康检查，协调模块自己的启动迁移，保持服务前缀兼容，并在生产环境托管 SPA。

## 非职责

- 不运行 YARP、不代理独立下游，不嵌入 Gateway。
- 不拥有模块业务规则、Migration/Seed 实现或数据表。
- 不作为跨服务业务编排器、Process Manager 或 Saga。
- 不把模块组合解释为模块化单体或数据所有权合并。

## 项目结构与调用链

`Program.cs` → `UnifiedHostModuleCatalog`（显式 `Identity` → `SystemData` → `ReferenceData`）→ 各模块自己的服务注册、健康检查和端点映射 → Controller/Application/Infrastructure。请求先由前缀兼容中间件剥离 `/identity`、`/systemdata`、`/referencedata`，再进入各模块原有 `/api/v1` Controller 路由。

`ModuleMigrationCoordinatorHostedService.cs` 按目录声明的 `identity → systemdata → referencedata` 顺序调用服务自有 `IServiceInitializer`，避免 Shared 数据库并发迁移；宿主只协调，不实现迁移。

生产 SPA 由 `UseStaticFiles` 和 fallback 托管；`/api`、`/health`、`/.well-known` 未知路径保持 404，不回退到 `index.html`。

## 运行入口

```powershell
./deploy/scripts/dev.ps1 start
Invoke-RestMethod http://localhost:5041/health/ready
```

或仅运行宿主：

```powershell
dotnet run --project src/backend/src/Hosts/IndustrialPlatform.UnifiedHost/IndustrialPlatform.UnifiedHost.csproj
```

Vite 开发服务器把 API Base URL 指向 `http://localhost:5041`。不要同时启动同样占用 `5041` 的独立 Identity Host。

## 依赖与配置

UnifiedHost 配置组合三个模块需要的 `DatabaseTopology`、Redis、日志、Identity 认证/JWT 等配置。Development 默认 Shared 物理目标；PerService 模式需为 `unifiedhost` 提供明确映射，因为当前进程使用单连接组合模块。

开发 CORS 仅允许 `http://localhost:5173` 和 `http://localhost:4173` 并允许凭据。生产 SPA 构建产物应位于宿主 `wwwroot`，本地 Vite 模式不要求该目录存在。

## 数据初始化

UnifiedHost 只协调，不拥有初始化实现。当前通过 `ModuleMigrationCoordinatorHostedService` 禁用模块各自并行 HostedService 后顺序运行 Identity/SystemData 迁移。失败会阻止宿主完成启动。

目标边界是调用各服务自己的 `Inspect → Plan → Apply → Verify` 初始化器，并由每个模块本地 ledger 决定 readiness。SystemData Observation 不是 UnifiedHost readiness 的替代事实。

新增统一部署模块时，在模块 API 项目实现 `IUnifiedHostModule`，明确写入 `UnifiedHostModuleCatalog.Modules`；同时提供服务注册、健康检查和必要端点映射，按依赖顺序放置。不要自动扫描实现，也不要把模块迁移或业务规则复制到宿主。

服务只进入 Gateway 的条件：它需要独立进程、独立伸缩或由 YARP 代理到下游。此类服务登记 Gateway 路由和健康地址，不进入 UnifiedHost 目录。统一进程模块才进入 UnifiedHost；目录模块的 `ExternalPathPrefix` 仅保持外部路径兼容，不承担 Gateway 路由职责。

## 测试入口

```powershell
dotnet test tests/UnifiedHost/IndustrialPlatform.UnifiedHost.Tests/IndustrialPlatform.UnifiedHost.Tests.csproj --configuration Release
```

重点验证模块组合、外部前缀兼容、认证/登录入口、健康聚合、SPA fallback 边界和迁移顺序。端到端登录使用计划指定的 Playwright real-login 门禁，仅在所需基础设施可用时运行并如实报告环境阻塞。

## 常见问题排查

### UnifiedHost 启动失败或停在迁移阶段

- 现象 → `5041` 未监听，日志停留在模块迁移。
- 首先检查 → Shared/PerService 拓扑映射、数据库可达性，以及 Identity/SystemData 本地 migration/seed 状态。
- 执行命令 → `dotnet test tests/UnifiedHost/IndustrialPlatform.UnifiedHost.Tests/IndustrialPlatform.UnifiedHost.Tests.csproj --configuration Release --filter FullyQualifiedName~Migration`
- 正常结果 → 协调器保持确定顺序且失败不会被吞掉。
- 异常时下一步 → 分别运行 Identity/SystemData Migration 测试并检查脱敏日志；不要启用两套并行迁移 HostedService。
相关代码入口 → `ModuleMigrationCoordinatorHostedService.cs`、`Program.cs`、各服务 `Modules/*Module.cs`。

### API 404 或页面未回退到 SPA

- 现象 → `/identity/api/v1/**` 等接口 404，或生产页面路由 404。
- 首先检查 → 服务前缀、Controller `/api/v1` 路由、`wwwroot/index.html` 是否存在，以及路径是否属于 API/health/JWKS 排除项。
- 执行命令 → `dotnet test tests/UnifiedHost/IndustrialPlatform.UnifiedHost.Tests/IndustrialPlatform.UnifiedHost.Tests.csproj --configuration Release`
- 正常结果 → 三个服务前缀兼容；未知 API 404；存在生产产物时非 API 页面回退 SPA。
- 异常时下一步 → 检查 `UnifiedHostModuleCatalog` 的 `ExternalPathPrefix` 和 fallback 条件；不要添加 YARP 或把 Gateway 串在 UnifiedHost 内。
相关代码入口 → `Program.cs`、各服务 Controller 与 `RoutePrefixConvention.cs`。

### 模块未启动或健康检查缺失

- 首先检查 → `UnifiedHostModuleCatalog.cs` 中是否显式登记，以及模块适配器是否调用了三项接口。
- 启动/迁移异常 → 检查 `ModuleMigrationCoordinatorHostedService.cs` 与对应服务初始化器。
- 健康检查异常 → 检查模块自己的 `Add*HealthChecks` 和 `/health/ready` 聚合结果。
- 路由异常 → 检查目录模块的 `ExternalPathPrefix`、Controller 路由和 Gateway 配置；不要将 YARP 或 Gateway 编排逻辑移入 UnifiedHost。
