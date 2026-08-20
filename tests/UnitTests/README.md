# 常规后端测试

常规门禁保留六个可重复运行的测试项目：BuildingBlocks、Identity、SystemData、ReferenceData、Gateway 和 UnifiedHost。

Identity 的 Domain、Application、Contract、Infrastructure、API 测试统一位于 `tests/Identity/IndustrialPlatform.Identity.Tests/`；SystemData 的五层测试及 Testing 辅助统一位于 `tests/SystemData/IndustrialPlatform.SystemData.Tests/`。

常规门禁：

```powershell
dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build --filter "Category!=Integration"
```

SQLite、内存替身和 `WebApplicationFactory` 测试放在常规项目；真实 PostgreSQL、Redis、RabbitMQ 或跨服务链路放在 IntegrationTests。
