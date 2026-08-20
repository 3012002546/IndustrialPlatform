# IntegrationTests

统一承载真实 PostgreSQL、Redis、RabbitMQ 和跨服务链路测试，程序集为 `IndustrialPlatform.IntegrationTests`。真实依赖测试使用 `[Trait("Category", "Integration")]`，凭环境变量显式启用，避免把本地无依赖运行误报为真实基础设施验证。

运行集成门禁：

```powershell
dotnet test tests/IntegrationTests/IndustrialPlatform.IntegrationTests/IndustrialPlatform.IntegrationTests.csproj --configuration Release --no-build --filter "Category=Integration"
```

Identity 登录链路需要 `IDENTITY_E2E_DB=1`；SystemData PostgreSQL 链路需要 `SYSTEMDATA_PG_E2E=1` 及对应 `SYSTEMDATA_PG_*` 连接环境变量。未启用时测试按既有门控早退，报告中必须标记为环境未执行。
