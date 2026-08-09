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
| 01 | [启动实施方案](01-Industrial%20Platform开发启动实施方案.md) | bootstrap |
| 02 | [BuildingBlocks基础组件实施方案](02-Industrial%20Platform%20BuildingBlocks基础组件开发实施方案.md) | BuildingBlocks skeleton |
| 03 | [Identity Service实施方案](03-Industrial%20Platform%20Identity%20Service开发实施方案.md) | Identity skeleton |
| 04 | [ReferenceData Service实施方案](04-Industrial%20Platform%20ReferenceData%20Service开发实施方案.md) | ReferenceData skeleton |
