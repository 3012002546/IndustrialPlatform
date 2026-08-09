# MasterData 与 OperationalData 开发 TODO 设计

## 目标

依据 `14-MasterData Service详细设计.md` 与 `14A-OperationalData Service详细设计.md`，建立两份可独立派遣、跟踪、验收和回写的开发 TODO 文档，替代 MVP 蓝图中的重复任务明细。

## 文档职责

- `docs/implementation/05-Industrial Platform MasterData Service开发实施方案.md`：MasterData 开发 TODO 的唯一维护源。
- `docs/implementation/06-Industrial Platform OperationalData Service开发实施方案.md`：OperationalData 开发 TODO 的唯一维护源。
- `docs/implementation/README.md`：登记两份实施文档、当前状态和依赖顺序。
- `docs/blueprint/09-MES MVP第一阶段开发TodoList.md`：只保留 Sprint 目标、范围、任务编号和实施文档链接，不重复维护任务正文。

蓝图定义业务边界，实施文档定义开发顺序与验收证据。实现若发现设计冲突，任务状态改为 `设计待确认`，先修订蓝图，再恢复派遣。

## 依赖与范围

开发顺序固定为：

```text
BuildingBlocks → Identity → ReferenceData → MasterData → OperationalData
```

MasterData 负责物料、单位、制造组织、仓库/库位定义、设备、BOM、工艺路线和批次策略等稳定定义，不负责库存实例、余额和仓储单据。

OperationalData 负责库存批次实例、余额、预留、不可变流水、仓储单据和 WMS 适配，不复制 MasterData 的定义所有权。单个仓库只能配置 `Internal` 或 `ExternalWms` 一种库存权威模式。

本次只修改 Markdown 文档，不修改应用代码、项目文件、测试、数据库迁移或部署配置。

## MasterData 任务结构

MasterData 拆分为以下可独立验收的任务：

1. `TASK-MD-001`：服务骨架、项目引用、健康检查和架构约束。
2. `TASK-MD-002`：单位、物料分类、批次策略等基础定义。
3. `TASK-MD-003`：物料与物料版本生命周期。
4. `TASK-MD-004`：工厂、车间、产线和工作中心层级。
5. `TASK-MD-005`：仓库、库位与库存权威模式定义。
6. `TASK-MD-006`：设备、设备类型及制造位置关联。
7. `TASK-MD-007`：BOM、版本、发布和生效规则。
8. `TASK-MD-008`：工艺路线、工序、版本、发布和生效规则。
9. `TASK-MD-009`：查询 API、缓存、审计与数据权限。
10. `TASK-MD-010`：Outbox 集成事件、消费者契约和完整验收。

每项任务必须包含状态、目标、输入文档、依赖、允许修改范围、预期输出、验证命令/证据、结果回写位置和建议提交消息。时间使用 `DateTimeOffset`，数据库瞬时时间使用 `timestamptz`。

## OperationalData 任务结构

沿用已经确认的 `TASK-OD-001` 至 `TASK-OD-009`：

1. 服务骨架与内部模块边界。
2. 库存批次与容器。
3. 库存余额与不可变流水。
4. 库存单据、过账与冲销。
5. 库存预留与生产领料。
6. 收料、退料与生产入库。
7. 调拨、盘点与库存调整。
8. Outbox 事件及 Trace/BatchRecord 契约。
9. 外部 WMS 适配器与双权威模式测试。

正式实施文档会补充全局约束、任务依赖图、逐任务验证命令、跨服务修改限制和完成标准，不改变已确认的领域拆分。

## 状态与执行规则

统一状态流转：

```text
待细化 → 可派遣 → 已派遣 → 开发中 → 待验收 → 已完成
```

当前 MasterData 与 OperationalData 业务代码尚未创建，所有任务初始状态为 `可派遣`。任务必须按依赖顺序推进；允许并行的测试或契约工作只能在其输入契约稳定后开始。

每次状态更新必须记录：执行任务或负责人、Git 提交、验证命令、退出码、测试通过/失败数量、设计偏差和回写文档。

## 验收标准

- 两份实施文档均可在不阅读其他任务正文的情况下独立派遣。
- 每个任务都有明确、有限的文件范围和可复现验证证据。
- MasterData 与 OperationalData 的所有权没有重叠。
- OperationalData 的每项前置 MasterData 能力都有明确任务依赖。
- MVP 蓝图不再复制详细任务内容，避免状态漂移。
- 文档不存在占位标记、未定义任务编号或含糊的实现与测试要求。
- `git diff --check -- docs` 通过，并确认没有业务代码变更。
