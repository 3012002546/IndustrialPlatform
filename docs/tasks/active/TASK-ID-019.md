# TASK-ID-019 接入 SystemData 编排与正式 admin 引导

## 任务编号

`TASK-ID-019`

## 状态

`已派遣`

## 负责人

Harness

## 目标

将 Identity Schema、三层幂等种子和正式 ADMIN 引导接入 SystemData 初始化 Operation，并实现随机临时密码的一次性安全交付与可恢复门禁。

## 输入文档与精确章节

- `docs/implementation/03-Industrial Platform Identity Service开发实施方案.md` §21、§29A.4、§29A.5、§29A.8、§31 `TASK-ID-019`。
- `docs/implementation/05-Industrial Platform SystemData开发实施方案.md` §7.1.2～§7.1.7、§9.2、§11.8、§12。
- `docs/blueprint/07-PostgreSQL数据库规范及分库设计.md`。
- `docs/blueprint/33-Industrial Platform SystemData数据库编排与环境引导.md` §12～§13。
- `docs/agents/ENGINEERING-NOTES.md` 的 .NET、数据库和 Identity 条目。

## 无需读取

- PF-02 组织/岗位/任职设计、前端设计、其他阶段实施方案和历史会话记录。

## 依赖

- TASK-ID-017、TASK-ID-018、TASK-SD-004 已具备实现基础。
- SystemData 全 Runner 生产环境门禁仍可作为外部待验收项，不阻止本任务完成可本地验证的代码与契约。

## Worktree

`D:\Code\Industrial Platform\IndustrialPlatform-worktrees\pf-00`

## 分支

`task/pf-00-id-019`

## 允许修改范围

- `src/backend/src/Services/Identity/**` 中与初始化、种子、bootstrap、readiness、配置和安全凭据交付直接相关的文件。
- `tests/Identity/**` 对应测试。
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Contracts/**` 中初始化公开契约的最小兼容扩展。
- `tests/SystemData/**` 中仅针对上述初始化公开契约的测试。
- `docs/evidence/TASK-ID-019.md`。

## 禁止修改范围

- SystemData 的 Organizations、Positions、Assignments、行政组织迁移和仓储。
- `SystemDataSchemaMigrations.cs`、SDM-004～006 及其他 PF-02 持久化注册文件。
- 前端、ReferenceData、其他业务模块、部署脚本。
- `AGENTS.md`、`CLAUDE.md`、`DSH.md`、`docs/status/CURRENT.md`、总体蓝图和任务卡。
- SystemData 不得引用 Identity Infrastructure、直写 Identity 表、接收或透传 Secret 值。

## 预期输出

- `identity_seed_ledger` 与 SystemCatalog、TenantSecurity、BootstrapAdmin 三层幂等种子。
- 稳定 `ADMIN`/`SYSTEM_ADMIN`、安全随机临时密码、一次性领取和紧急恢复门禁。
- admin `MustChangePassword=false`，重复启动/apply 不覆盖现有 admin。
- Identity 初始化 manifest、bootstrap status/readiness 与 SystemData Operation 兼容契约。
- 明文密码不进入数据库、Operation、日志、Trace、审计或事件。

## 定向验证命令

```powershell
dotnet build tests/Identity/IndustrialPlatform.Identity.Infrastructure.Tests/IndustrialPlatform.Identity.Infrastructure.Tests.csproj --configuration Release
dotnet test tests/Identity/IndustrialPlatform.Identity.Infrastructure.Tests/IndustrialPlatform.Identity.Infrastructure.Tests.csproj --configuration Release --no-build
dotnet build tests/Identity/IndustrialPlatform.Identity.Api.Tests/IndustrialPlatform.Identity.Api.Tests.csproj --configuration Release
dotnet test tests/Identity/IndustrialPlatform.Identity.Api.Tests/IndustrialPlatform.Identity.Api.Tests.csproj --configuration Release --no-build
dotnet build tests/SystemData/IndustrialPlatform.SystemData.Contract.Tests/IndustrialPlatform.SystemData.Contract.Tests.csproj --configuration Release
dotnet test tests/SystemData/IndustrialPlatform.SystemData.Contract.Tests/IndustrialPlatform.SystemData.Contract.Tests.csproj --configuration Release --no-build
```

## 验收标准

- 首次、重复和并发初始化行为可由测试证明。
- 已存在或异常 admin 不被静默覆盖或自动修复。
- 临时密码满足策略且每次随机，只能领取一次。
- 敏感值扫描无数据库、日志、事件和 Operation 泄漏。
- 所有定向 Release 构建 0 错误，测试 0 失败。

## 结果回写位置

`docs/evidence/TASK-ID-019.md`

## 建议提交信息

`feat(identity): add orchestrated admin bootstrap`
