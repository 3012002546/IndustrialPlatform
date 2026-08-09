# Industrial Platform 实施文档索引

本目录是 Industrial Platform 开发实施方案和可派遣开发 TODO 的唯一维护源。蓝图决策来自 `docs/blueprint`；实际开发在其他任务或被派遣的协作任务中完成，当前任务只负责拆分、派遣、跟踪、验收信息汇总和结果回写。

## 协作边界

开发 TODO 按 `待细化 → 可派遣 → 已派遣 → 开发中 → 待验收 → 已完成` 流转。实现发现蓝图冲突时，状态改为 `设计待确认`，回到 `docs/blueprint` 修订后再派遣。每个可派遣 TODO 必须给出任务编号、目标、输入文档、依赖、允许修改范围、预期输出、验证命令或验收证据，以及结果回写位置。

## 时间类型约束

- .NET 时间值使用 `DateTimeOffset` / `DateTimeOffset?`，当前 UTC 时间使用 `DateTimeOffset.UtcNow`。
- API 时间使用包含 `Z` 或明确偏移量的 ISO 8601 / RFC 3339 格式。
- PostgreSQL 瞬时时间使用 `timestamp with time zone`（`timestamptz`），以 UTC 保存。
- 本目录后续新增或修订的实施方案不得把 `DateTime` 用作业务时间类型。

| 编号 | 文档 | 当前范围 |
| --- | --- | --- |
| 01 | [启动实施方案](01-Industrial%20Platform开发启动实施方案.md) | 总体路线：可运行基线优先 |
| 02 | [BuildingBlocks基础组件实施方案](02-Industrial%20Platform%20BuildingBlocks基础组件开发实施方案.md) | 原基础搭建已完成；`TASK-BB-010` Entity 调整可派遣 |
| 02A | [可运行基线开发 TODO](02A-Industrial%20Platform可运行基线开发实施方案.md) | `TASK-BASE-001`～`TASK-BASE-006`，当前第一优先级 |
| 02B | [统一前端第一批开发 TODO](02B-Industrial%20Platform统一前端第一批开发实施方案.md) | `TASK-FE-001`～`TASK-FE-008`，基线完成后执行 |
| 03 | [Identity Service实施方案](03-Industrial%20Platform%20Identity%20Service开发实施方案.md) | 骨架已完成；真实登录闭环待开发 |
| 04 | [ReferenceData Service实施方案](04-Industrial%20Platform%20ReferenceData%20Service开发实施方案.md) | 骨架已完成；业务功能与页面待开发 |
| 05 | [MasterData Service开发 TODO](05-Industrial%20Platform%20MasterData%20Service开发实施方案.md) | `TASK-MD-001`～`TASK-MD-010`，可派遣 |
| 06 | [OperationalData Service开发 TODO](06-Industrial%20Platform%20OperationalData%20Service开发实施方案.md) | `TASK-OD-001`～`TASK-OD-009`，可派遣 |

## 当前开发顺序

```text
BuildingBlocks 原基础搭建（已完成）
→ TASK-BB-010 Entity 生命周期与并发调整
→ 可运行基线剩余任务（TASK-BASE-002 起）
→ 统一前端第一批
→ Identity 登录闭环
→ ReferenceData 服务 + 页面
→ MasterData 服务 + 页面
→ OperationalData 服务 + 页面
```

BuildingBlocks 原基础搭建的完成进度以 `CLAUDE.md` 的代码协作记录为依据，不重复派遣；新批准的 Entity 调整只派遣 `TASK-BB-010`。详细任务状态分别在 02、02A、02B、05、06 中更新；`docs/blueprint/09-MES MVP第一阶段开发TodoList.md` 只维护阶段范围和任务编号索引。

从 ReferenceData 开始，每个业务服务按“后端用例与契约 → 对应业务页面 → 契约测试与关键路径 E2E → 阶段验收”纵向交付，不再把全部前端推迟到业务服务之后。
