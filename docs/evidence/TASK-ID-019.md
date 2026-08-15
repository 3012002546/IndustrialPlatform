# TASK-ID-019 验证证据

## 状态

实现与内部验证完成;按协作规则保留未提交工作树,交由 Codex 最终 Release 编译门禁后统一提交与集成。

## 主要修改范围

- Identity Domain/Application/Infrastructure/Api:`User.MustChangePassword`;`identity_seed_ledger` 与 `identity_bootstrap_credential` 表(ID-019-01/02)及 `must_change_password` 列(ID-019-03);三层幂等种子执行器(SystemCatalog/TenantSecurity/BootstrapAdmin)与 `identity_seed_ledger` 记账;稳定 `ADMIN`/`SYSTEM_ADMIN`、RNG 随机临时密码(≥20 满足策略)、一次性领取与紧急恢复门禁;admin `MustChangePassword=false`、重复执行不覆盖、异常 admin 不自动修复;bootstrap 状态/readiness(与 SystemData 形状兼容)与 `GET /api/v1/bootstrap/status`、`GET /api/v1/bootstrap/readiness`、`POST /api/v1/bootstrap/recover`;`IdentityInitializationService`(SchemaMigration→SystemSeed→BootstrapAdmin→Verify)。
- SystemData.Contracts:仅 `ServiceInitializationReadinessV2` 增加 `BootstrapStatus` 最小兼容扩展。
- 测试:Identity Infrastructure/Api/Domain 测试适配与新增(种子/引导/恢复/并发收敛/敏感扫描),SystemData Contract 测试新增 BootstrapStatus 序列化。
- 移除:环境变量密码引导(`IDENTITY_BOOTSTRAP_*`)与迁移内种子步骤(ID-004-10/11)。

## 验证命令与结果

本机限制:SDK 10.0.400 缺 workload locator 目录(`MSBuildEnableWorkloadResolver=false`),NuGet 审计源不可达(`-p:NuGetAudit=false`),vstest testhost 需父进程句柄访问。以下为新鲜 Release 构建 + `--no-build` 测试:

| 命令 | 结果 |
| --- | --- |
| `dotnet build tests/Identity/IndustrialPlatform.Identity.Infrastructure.Tests/...csproj --configuration Release` | 0 警告 0 错误 |
| `dotnet test ...Identity.Infrastructure.Tests... --configuration Release --no-build` | 127/127 通过 |
| `dotnet build tests/Identity/IndustrialPlatform.Identity.Api.Tests/...csproj --configuration Release` | 0 警告 0 错误 |
| `dotnet test ...Identity.Api.Tests... --configuration Release --no-build` | 50/50 通过 |
| `dotnet build tests/SystemData/IndustrialPlatform.SystemData.Contract.Tests/...csproj --configuration Release` | 0 警告 0 错误 |
| `dotnet test ...SystemData.Contract.Tests... --configuration Release --no-build` | 16/16 通过 |

连带:Identity Domain.Tests 163/163、Application.Tests 138/138、Contract.Tests 38/38 通过。

## 剩余风险

- SystemData 全 Runner 生产门禁(plan→审批→备份→apply→verify、PostgreSQL advisory lock 并发)为外部待验收项。
- PostgreSQL 真库行为(timestamptz/BOOLEAN、23505 冲突识别)标记「待验收」;SQLite 已覆盖等效语义。
- 状态/readiness 在数据库完全未 provision 时的 503 NotReady 由 SystemData/health 层负责。
- 普通新建用户一次性临时密码交付与完整 bootstrap 权限接线属 TASK-ID-020。
