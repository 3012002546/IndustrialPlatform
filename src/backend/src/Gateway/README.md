# Gateway

## 职责

Gateway 是多进程/未来分布式部署的外部 YARP 入口。它按 `/identity`、`/systemdata`、`/referencedata` 前缀代理到独立 API Host，转发时剥离前缀，并统一处理 CORS、下游 readiness 聚合和代理错误信封。

| 能力 | 当前入口 |
| --- | --- |
| YARP 路由/集群 | `IndustrialPlatform.Gateway/Configuration/GatewayRouteFactory.cs` |
| 服务和超时配置 | `IndustrialPlatform.Gateway/Configuration/GatewayOptions.cs`、`appsettings*.json` 的 `Gateway` 节 |
| CORS | `Program.cs` 与 `Gateway:Cors` |
| 下游健康聚合 | `IndustrialPlatform.Gateway/Health/GatewayServiceHealthCheck.cs` |
| 代理错误 | `IndustrialPlatform.Gateway/Errors/GatewayProxyErrorMiddleware.cs`：不可达 503、转发超时 504 |

## 非职责

- 不加载 Identity、SystemData、ReferenceData 业务模块。
- 不托管 SPA 或静态文件，不执行任何服务 Migration/Seed。
- 不作为服务间调用总线、业务编排器或 Saga。
- 不代理 UnifiedHost；两种部署角色是并列入口，不串联。

## 项目结构与调用链

`Program.cs` 绑定 `GatewayOptions` → `GatewayRouteFactory` 生成 YARP routes/clusters → 浏览器请求按前缀转发到独立 Host。`GatewayServiceHealthCheck` 逐个访问已配置下游 `/health/ready`，只汇总下游本地 readiness。`GatewayProxyErrorMiddleware` 读取 YARP Forwarder error 并写统一 `ApiResult`。

外部路径固定保持：`/identity/**`、`/systemdata/**`、`/referencedata/**`。Gateway 剥离服务前缀后，下游继续匹配自己的 `/api/v1/**`。

## 运行入口

推荐用脚本启动完整分布式拓扑：

```powershell
./deploy/scripts/dev.ps1 start -IndependentServices
Invoke-RestMethod http://localhost:5080/health/ready
```

只启动 Gateway：

```powershell
dotnet run --project src/backend/src/Gateway/IndustrialPlatform.Gateway/IndustrialPlatform.Gateway.csproj
```

只有 Gateway 而没有独立 API Host 时，liveness 可用，下游 readiness/代理请求会明确失败。

## 依赖与配置

`Gateway:Services[]` 每项包含稳定名称、PathPrefix 和 DestinationUrl；`RequestTimeoutSeconds` 控制 YARP activity timeout；`Gateway:Cors:AllowedOrigins` 控制浏览器来源。配置只保存路由目标，不承载业务数据库或服务 Secret。

开发端口为 `5080`。前端分布式验证把 API Base URL 指向该端口，不能直连内部业务 Host。

## 数据初始化

Gateway 不执行、协调或观察数据初始化，也不持有任何服务 Ledger。启动 Gateway 不代表下游已初始化；它只能根据下游各自 `/health/ready` 汇总结果。

## 测试入口

```powershell
dotnet test tests/Gateway/IndustrialPlatform.Gateway.Tests/IndustrialPlatform.Gateway.Tests.csproj --configuration Release
```

重点覆盖路由前缀剥离、CORS、健康聚合、404 信封、下游不可达 503 和超时 504。修改配置模型或中间件时运行完整 Gateway 测试，不只做浏览器访问。

## 常见问题排查

### Gateway 返回 404

- 现象 → `http://localhost:5080/<path>` 返回“路由不存在”。
- 首先检查 → 请求是否带 `/identity`、`/systemdata` 或 `/referencedata` 前缀，以及服务配置是否有效。
- 执行命令 → `dotnet test tests/Gateway/IndustrialPlatform.Gateway.Tests/IndustrialPlatform.Gateway.Tests.csproj --configuration Release --filter FullyQualifiedName~RoutingTests`
- 正常结果 → 已配置前缀被代理并剥离，未知前缀返回统一 404。
- 异常时下一步 → 检查 `GatewayRouteFactory` 与 `Gateway:Services`，不要在业务 Controller 添加 Gateway 专用路由。
相关代码入口 → `IndustrialPlatform.Gateway/Configuration/GatewayRouteFactory.cs`、`IndustrialPlatform.Gateway/appsettings.Development.json`、`IndustrialPlatform.Gateway/Program.cs`。

### Gateway 返回 503 或 504

- 现象 → 路由存在但返回“下游服务不可用”或“网关转发请求超时”。
- 首先检查 → 对应独立 API Host 是否启动、DestinationUrl 和下游 `/health/ready`。
- 执行命令 → `./deploy/scripts/dev.ps1 status; Invoke-RestMethod http://localhost:5080/health/ready`
- 正常结果 → 所有配置下游 Ready，代理请求返回下游响应。
- 异常时下一步 → 直接访问目标 Host 的 readiness，区分连接失败、依赖 NotReady 与业务响应；不要把 Gateway 改成进程内宿主。
相关代码入口 → `IndustrialPlatform.Gateway/Health/GatewayServiceHealthCheck.cs`、`IndustrialPlatform.Gateway/Errors/GatewayProxyErrorMiddleware.cs`、`IndustrialPlatform.Gateway/Configuration/GatewayOptions.cs`。
