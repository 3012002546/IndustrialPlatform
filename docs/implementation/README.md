# Industrial Platform 实施文档索引

本目录是 Industrial Platform 开发实施方案和可派遣开发 TODO 的唯一维护源。蓝图决策来自 `docs/blueprint`；实际开发在其他任务或被派遣的协作任务中完成，当前任务只负责拆分、派遣、跟踪、验收信息汇总和结果回写。

总体开发优先级和阶段门禁统一读取 `docs/blueprint/09-Industrial Platform开发总TodoList.md`。PF-01～PF-10 对应本目录 04～13，PF-10A 使用 13A，PF-11 使用 14；MES 文档从 15 开始。

所有 PF 阶段还必须读取 `docs/blueprint/32-Industrial Platform Service Host与内部模块边界.md`。阶段编号不等于 Service Host；后续阶段可以向前一阶段创建的宿主增加独立模块，但不得合并 Schema/表前缀、契约、权限或测试，也不得跨模块直读 Repository。

PF-02 及其后的新服务必须以 `docs/blueprint/07-PostgreSQL数据库规范及分库设计.md` 与 `docs/blueprint/33-Industrial Platform SystemData数据库编排与环境引导.md` 为权威拓扑来源，并以 `docs/implementation/05-Industrial Platform SystemData开发实施方案.md` 为 PF-02 控制面计划。PF-02 把数据库编排/环境引导纳入 SystemData；后续服务必须交付 registration/manifest、服务自有迁移产物、SystemData 启动握手/readiness、最小业务角色、备份登记和可观测 `OperationId`，不得自行持有管理员凭据建库或使用 `EnsureCreated` 代替迁移。

## 协作边界

开发 TODO 按 `待细化 → 可派遣 → 已派遣 → 开发中 → 待验收 → 已完成` 流转。实现发现蓝图冲突时，状态改为 `设计待确认`，回到 `docs/blueprint` 修订后再派遣。

所有新增或重构的实施方案必须从 [`TEMPLATE-开发实施方案.md`](TEMPLATE-开发实施方案.md) 开始，并保持“详细设计正文 → 任务依赖 → 九字段任务卡 → 完成标准 → 执行记录 → 下一阶段输入契约”的顺序。每个 PF 阶段只保留一个阶段管理会话；该会话负责详细设计、任务派遣、跟踪、验收和结果回写，实际编码由被派遣任务执行。

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
| 02B | [统一前端第一批开发 TODO](02B-Industrial%20Platform统一前端第一批开发实施方案.md) | `TASK-FE-001～010` 已完成 |
| 03 | [Identity Service实施方案](03-Industrial%20Platform%20Identity%20Service开发实施方案.md) | PF-00 已暂停；`TASK-ID-001～006` 已完成，恢复时从 `TASK-ID-007` 继续 |
| 04 | [PF-01 视觉主题与平台外壳实施方案](04-Industrial%20Platform视觉主题与平台外壳开发实施方案.md) | `TASK-PF01-001～007` 已完成；真实 Identity 联合验收 real E2E 19/19；外部真机 safe-area 待验收 |
| 05 | [PF-02 SystemData实施方案](05-Industrial%20Platform%20SystemData开发实施方案.md) | PF-02 控制面计划；数据库拓扑以蓝图 07、33 为准，当前为已形成设计/任务卡、待书面审阅，尚未开发 |
| 06 | [PF-03 ReferenceData实施方案](06-Industrial%20Platform%20ReferenceData%20Service开发实施方案.md) | 代码仅骨架；现有详细设计和任务卡由 PF-03 会话复核 |
| 07 | PF-04 File / Notification / Audit实施方案（待 PF-04 会话创建） | 三个模块分开建模，在同一阶段管理会话协调和派遣 |
| 08 | PF-05 Collaboration实施方案（待 PF-05 会话创建） | 待阶段管理会话设计和派遣 |
| 09 | PF-06 RemoteAssistance实施方案（待 PF-06 会话创建） | 先验证再决定适配或自研路线 |
| 10 | PF-07 Scheduler / Platform Health实施方案（待 PF-07 会话创建） | 两个模块分开建模，在同一阶段管理会话协调和派遣 |
| 11 | PF-08 Low Code实施方案（待 PF-08 会话创建） | 待阶段管理会话设计和派遣 |
| 12 | PF-09 Dashboard & Report实施方案（待 PF-09 会话创建） | 两个产品边界分开建模，在同一阶段管理会话协调和派遣 |
| 13 | PF-10 ServerMonitor实施方案（待 PF-10 会话创建） | 创建 `OperationsCenter.Service`，本阶段只处理 ServerMonitor |
| 13A | PF-10A Operations Center Knowledge & Assistant实施方案（待 PF-10A 会话创建） | 设计待确认；先补齐 IssueTracking 与 KnowledgeBase 完整数据闭环，不提前创建文档 |
| 14 | PF-11 IoT Collector实施方案（待 PF-11 会话创建） | 待阶段管理会话复核蓝图并派遣 |
| 15 | [MES-01 MasterData实施方案](15-Industrial%20Platform%20MasterData%20Service开发实施方案.md) | 暂缓；保留现有设计和未提交调整，恢复前复核 |
| 16 | [MES-02 OperationalData实施方案](16-Industrial%20Platform%20OperationalData%20Service开发实施方案.md) | 暂缓；恢复前按母版重构和复核 |

## 当前开发顺序

```text
BuildingBlocks / 可运行基线 / 统一前端第一批（已完成，Docker 实机项除外）
→ PF-00 Identity（已暂停，停在 TASK-ID-007）
→ PF-01 视觉、主题与平台外壳（TASK-PF01-001～007 已完成）
→ PF-02 SystemData + PF-03 ReferenceData
→ PF-04 File / Notification / Audit
→ PF-05 Collaboration
→ PF-06 RemoteAssistance
→ PF-07 Scheduler / Platform Health
→ PF-08 Low Code
→ PF-09 Dashboard / Report
→ PF-10 ServerMonitor
→ PF-10A Operations Center Knowledge & Assistant（设计待确认）
→ PF-11 IoT Collector
→ MES-01 MasterData
→ MES-02 OperationalData
→ MES生产闭环
```

BuildingBlocks 原基础搭建和 Entity 调整不重复派遣。详细任务状态在对应实施文档中维护；`docs/blueprint/09-Industrial Platform开发总TodoList.md` 只维护总体阶段、门禁、阶段管理会话入口和结果索引。

每个 PF 阶段（包括 PF-10A）只开一个阶段管理会话。该会话根据蓝图、母版、项目记忆和当前代码反复完善详细设计，确认后直接在对应编号实施文档中生成九字段任务卡并负责派遣、跟踪和验收，不再拆成规格会话、计划会话和开发会话。被派遣任务继续按“后端用例与契约 → 对应页面 → 契约测试与关键路径 E2E → 阶段验收”纵向交付。
