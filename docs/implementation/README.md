# Industrial Platform 实施文档索引

本目录是 Industrial Platform 开发实施方案和可派遣开发 TODO 的唯一维护源。蓝图决策来自 `docs/blueprint`；实际开发在其他任务或被派遣的协作任务中完成，当前任务只负责拆分、派遣、跟踪、验收信息汇总和结果回写。

总体开发优先级和阶段门禁统一读取 `docs/blueprint/09-Industrial Platform开发总TodoList.md`。本目录文件编号用于稳定引用，不代表当前执行顺序。

## 协作边界

开发 TODO 按 `待细化 → 可派遣 → 已派遣 → 开发中 → 待验收 → 已完成` 流转。实现发现蓝图冲突时，状态改为 `设计待确认`，回到 `docs/blueprint` 修订后再派遣。

所有新增或重构的实施方案必须从 [`TEMPLATE-开发实施方案.md`](TEMPLATE-开发实施方案.md) 开始，并保持“详细设计正文 → 任务依赖 → 九字段任务卡 → 完成标准 → 执行记录 → 下一阶段输入契约”的顺序。

每个可派遣任务统一包含：状态、目标、输入文档、依赖、允许修改范围、预期输出、验证与证据、结果回写、建议提交。不得只写实现摘要或笼统的“测试通过”。

## 时间类型约束

- .NET 时间值使用 `DateTimeOffset` / `DateTimeOffset?`，当前 UTC 时间使用 `DateTimeOffset.UtcNow`。
- API 时间使用包含 `Z` 或明确偏移量的 ISO 8601 / RFC 3339 格式。
- PostgreSQL 瞬时时间使用 `timestamp with time zone`（`timestamptz`），以 UTC 保存。
- 本目录后续新增或修订的实施方案不得把 `DateTime` 用作业务时间类型。

| 编号 | 文档 | 当前范围 |
| --- | --- | --- |
| TEMPLATE | [统一开发实施方案模板](TEMPLATE-开发实施方案.md) | 后续开发设计和任务派遣的强制母版 |
| 01 | [启动实施方案](01-Industrial%20Platform开发启动实施方案.md) | 总体路线：可运行基线优先 |
| 02 | [BuildingBlocks基础组件实施方案](02-Industrial%20Platform%20BuildingBlocks基础组件开发实施方案.md) | 原基础搭建已完成；`TASK-BB-010` Entity 生命周期/并发/软删除调整已完成 |
| 02A | [可运行基线开发 TODO](02A-Industrial%20Platform可运行基线开发实施方案.md) | `TASK-BASE-001～006` 已完成（`BASE-002/003` 真实依赖与容器验收留待 Docker 环境，`BASE-005/006` 无 Docker 实测通过）；Phase 2 前端输入契约已登记 |
| 02B | [统一前端第一批开发 TODO](02B-Industrial%20Platform统一前端第一批开发实施方案.md) | `TASK-FE-001`～`TASK-FE-010`，基线完成后执行 |
| 03 | [Identity Service实施方案](03-Industrial%20Platform%20Identity%20Service开发实施方案.md) | PF-00 开发中；`TASK-ID-001/002` 已完成，继续 `TASK-ID-003～016` |
| 04 | [ReferenceData Service实施方案](04-Industrial%20Platform%20ReferenceData%20Service开发实施方案.md) | PF-03 代码仅骨架；详细设计已存在，开发前在独立会话复核并修订任务状态 |
| 05 | [MasterData Service开发 TODO](05-Industrial%20Platform%20MasterData%20Service开发实施方案.md) | MES-01 暂缓；保留现有设计和任务卡，恢复前独立复核 |
| 06 | [OperationalData Service开发 TODO](06-Industrial%20Platform%20OperationalData%20Service开发实施方案.md) | MES-02 暂缓；保留现有设计和任务卡，恢复前独立复核 |

## 当前开发顺序

```text
BuildingBlocks / 可运行基线 / 统一前端第一批（已完成，Docker 实机项除外）
→ PF-00 Identity（进行中）
→ PF-01 视觉、主题与平台外壳
→ PF-02 SystemData + PF-03 ReferenceData
→ PF-04A Audit + PF-04B File + PF-04C Notification
→ PF-05 Collaboration
→ PF-06 RemoteAssistance
→ PF-07 Scheduler / Platform Health
→ PF-08 Low Code
→ PF-09 Dashboard / Report
→ PF-10 Server Monitor
→ PF-11 IoT Collector
→ MES-01 MasterData
→ MES-02 OperationalData
→ MES生产闭环
```

BuildingBlocks 原基础搭建和 Entity 调整不重复派遣。详细任务状态分别在 02、02A、02B、03、04、05、06 及后续阶段实施文档中维护；`docs/blueprint/09-Industrial Platform开发总TodoList.md` 只维护总体阶段、门禁、独立会话入口和结果索引。

每个 PF/MES 阶段先在独立会话完成详细设计和书面规格，再在新的计划会话生成独立实施方案和任务卡。开发继续按“后端用例与契约 → 对应页面 → 契约测试与关键路径 E2E → 阶段验收”纵向交付，不把前端推迟到所有后端完成之后。
