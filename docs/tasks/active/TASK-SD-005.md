# TASK-SD-005 实现行政组织、岗位与任职领域及持久化

## 任务编号

`TASK-SD-005`

## 状态

`已派遣`

## 负责人

Claude Code

## 目标

实现 AdministrativeOrganization、Position、UserAssignment 聚合、时间区间、主任职规则、仓储以及 SDM-004～006 迁移。

## 输入文档与精确章节

- `docs/implementation/05-Industrial Platform SystemData开发实施方案.md` §2.3、§6、§7.2～§7.4、§8、§12.1～§12.3、§14 `TASK-SD-005`。
- `docs/agents/ENGINEERING-NOTES.md` 的 .NET、SqlSugar 与 SystemData 条目。
- BuildingBlocks `Entity` 生命周期、双版本并发与复合外键现有实现。

## 无需读取

- Identity TASK-ID-019、SystemData API/前端章节、其他阶段实施方案和历史 evidence。

## 依赖

- TASK-SD-004 已具备实现基础。

## Worktree

`D:\Code\Industrial Platform\IndustrialPlatform-worktrees\pf-02`

## 分支

`task/pf-02-sd-005`

## 允许修改范围

- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Domain/**` 中 Organizations、Positions、Assignments。
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Application/**` 中上述领域所需端口，不实现 API 用例。
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/**` 中上述表模型、映射、仓储和 SDM-004～006。
- `tests/SystemData/IndustrialPlatform.SystemData.Domain.Tests/**` 对应测试。
- `tests/SystemData/IndustrialPlatform.SystemData.Infrastructure.Tests/**` 对应测试。
- `docs/evidence/TASK-SD-005.md`。

## 禁止修改范围

- Identity 全部代码和测试。
- SystemData Contracts、Api、前端、资源/导航、功能开关、服务目录和主题策略。
- SystemData ServiceInitialization/DatabaseOrchestration 公开契约。
- `AGENTS.md`、`CLAUDE.md`、`DSH.md`、`docs/status/CURRENT.md`、总体蓝图和任务卡。

## 预期输出

- 四类型组织树、父子矩阵、多根公司、移动和停用规则。
- 组织专属岗位、时间化多任职与主任职历史拆分。
- 双版本并发、软删除过滤、复合外键和全历史 NId 唯一。
- 按 UserNId advisory lock 端口、SQLite 替身和 PostgreSQL DDL。
- SDM-004～006 迁移与领域/仓储测试。

## 定向验证命令

```powershell
dotnet build tests/SystemData/IndustrialPlatform.SystemData.Domain.Tests/IndustrialPlatform.SystemData.Domain.Tests.csproj --configuration Release
dotnet test tests/SystemData/IndustrialPlatform.SystemData.Domain.Tests/IndustrialPlatform.SystemData.Domain.Tests.csproj --configuration Release --no-build
dotnet build tests/SystemData/IndustrialPlatform.SystemData.Infrastructure.Tests/IndustrialPlatform.SystemData.Infrastructure.Tests.csproj --configuration Release
dotnet test tests/SystemData/IndustrialPlatform.SystemData.Infrastructure.Tests/IndustrialPlatform.SystemData.Infrastructure.Tests.csproj --configuration Release --no-build
```

## 验收标准

- §12.3 的组织、岗位和任职规则由 TDD 覆盖。
- SQLite 外键、过滤、映射和乐观并发验证通过。
- PostgreSQL advisory lock 与并发区间若缺少环境，必须明确记录为待外部验收。
- 所有定向 Release 构建 0 错误，测试 0 失败。

## 结果回写位置

`docs/evidence/TASK-SD-005.md`

## 建议提交信息

`feat(systemdata): add organization position and assignment domain`
