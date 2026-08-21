# IntegrationTests

统一承载真实 PostgreSQL、Redis、RabbitMQ 和跨服务链路测试，程序集为 `IndustrialPlatform.IntegrationTests`。真实依赖测试使用 `[Trait("Category", "Integration")]`，凭环境变量显式启用，避免把本地无依赖运行误报为真实基础设施验证。

运行集成门禁：

```powershell
dotnet test tests/IntegrationTests/IndustrialPlatform.IntegrationTests/IndustrialPlatform.IntegrationTests.csproj --configuration Release --no-build --filter "Category=Integration"
```

Identity 登录链路需要 `IDENTITY_E2E_DB=1`；SystemData PostgreSQL 链路需要 `SYSTEMDATA_PG_E2E=1` 及对应 `SYSTEMDATA_PG_*` 连接环境变量。未启用时测试按既有门控早退，报告中必须标记为环境未执行。

TASK-SD-010 真实控制面门禁使用 `SYSTEMDATA_CONTROL_PLANE_E2E=1`，并要求以下环境变量：

- PostgreSQL：`SYSTEMDATA_CONTROL_PLANE_PG_HOST`、`SYSTEMDATA_CONTROL_PLANE_PG_PORT`、`SYSTEMDATA_CONTROL_PLANE_PG_DATABASE`、`SYSTEMDATA_CONTROL_PLANE_PG_USERNAME`、`SYSTEMDATA_CONTROL_PLANE_PG_PASSWORD`
- Redis：`SYSTEMDATA_CONTROL_PLANE_REDIS_CONNECTION`
- RabbitMQ：`SYSTEMDATA_CONTROL_PLANE_RABBIT_HOST`、`SYSTEMDATA_CONTROL_PLANE_RABBIT_PORT`、`SYSTEMDATA_CONTROL_PLANE_RABBIT_USERNAME`、`SYSTEMDATA_CONTROL_PLANE_RABBIT_PASSWORD`、`SYSTEMDATA_CONTROL_PLANE_RABBIT_VHOST`

正式云联调还需在测试进程设置 `IndustrialPlatform__DevelopmentInfrastructureMode=Unified`。`tests/TestDevelopmentInfrastructureMode.cs` 只在没有显式模式且没有任何真实联调 gate 时注入 `Sqlite`；因此 IntegrationTests 的真实 gate 不会被测试模块初始化器覆盖。控制面门禁未开启时测试会被标记为 Skip，不计入通过；开启后缺配置或依赖不可达会失败。
