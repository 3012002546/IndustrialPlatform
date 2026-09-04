# 16-Industrial Platform OperationalData Service开发实施方案

# Industrial Platform OperationalData Service开发实施方案

版本：v1.0
当前阶段：MES-02，暂缓；达到总开发 TodoList 的恢复门禁后，由独立阶段管理会话复核并重新派遣任务
蓝图依据：`docs/blueprint/14A-OperationalData Service详细设计.md`

## 1. 目标与边界

OperationalData 是一个独立部署、内部模块化的微服务，负责运行期库存事实、库存批次实例、预留、仓储单据、库存流水和外部 WMS 防腐层。MasterData 继续拥有物料、仓库、库位和批次策略定义；ReferenceData 拥有通用单位/换算与状态机定义；WorkOrder 只发出业务需求；Trace 和 BatchRecord 只消费事实并建立投影。

当前执行前置：可运行基线、统一前端第一批、Identity、ReferenceData 和 MasterData 服务 + 页面阶段全部完成。OperationalData 必须在同一阶段交付库存查询、批次、收发退、调拨和盘点页面。

```text
MasterData → OperationalData → WorkOrder / Weighting / Trace / BatchRecord
```

## 2. 全局实施约束

- 对外保持一个 OperationalData 微服务，内部使用 Inventory、Lots、Documents、WarehouseOperations、WmsIntegration 模块。
- 数据库名固定为 `operationaldata_db`；其他服务不得直接读写。
- `operationaldata_db` 是稳定的 `LogicalDatabaseName`：Development 默认解析为配置的共享 PostgreSQL `industrial_platform_dev` 或共享 SQLite 文件；Test/Staging/Production 由 SystemData 解析为服务专属物理数据库。共享 Development 不允许跨服务表访问或合并迁移账本。
- .NET 时间使用 `DateTimeOffset` / `DateTimeOffset?`，PostgreSQL 瞬时时间使用 `timestamptz`。
- 余额只能由单据过账或冲销产生的不可变 StockTransaction 更新；禁止通用余额覆盖接口。
- 单个仓库只能有 `Internal` 或 `ExternalWms` 一个库存权威源。
- API、事件、WMS 命令和回执均使用幂等键；单据、流水、余额和 Outbox 在同一事务提交。
- 跨服务只使用 API、Contracts 和事件；禁止引用其他服务 Infrastructure 或数据库上下文。
- 每项业务数量都保存 `UnitDimensionNId`、`UnitNId`、`unitRevision`（整份 `UnitDimension.Revision`）、`sourceScope`、`sourceTenantNId` 和过账时的精度/舍入、必要换算快照；`sourceTenantNId` 是定义来源而非业务对象 `TenantNId`，Platform 来源为空，Tenant 来源等于可信当前租户。通用换算仅允许同来源、同维度、同修订；通用定义只从 ReferenceData 公开契约读取，库存事实不因单位停用或后续修订而失去可解释性。
- InventoryLot、库存与单据的业务执行状态由 OperationalData 自己的聚合和事务管理；ReferenceData StateMachine 仅提供可消费的定义契约，不能接管过账、库存校验、状态写入或历史。
- 每项任务先写失败测试，再实现最小代码，并保留命令、退出码与测试数量作为证据。
- 状态流转：`可派遣 → 已派遣 → 开发中 → 待验收 → 已完成`；发现蓝图冲突时改为 `设计待确认`。

## 3. 任务依赖图

```text
OD-001 → OD-002 → OD-003 → OD-004 → OD-005 → OD-006 → OD-007 → OD-008 → OD-009
```

## TASK-OD-001 创建 OperationalData 服务骨架

**状态：** 可派遣

**目标：** 建立五层项目和 Inventory、Lots、Documents、WarehouseOperations、WmsIntegration 模块边界。

**输入文档：** 蓝图 06、12、14A 第 1 至 5 节；MasterData TASK-MD-001 的项目约定。

**依赖：** TASK-BASE-006、TASK-FE-008、Identity 登录闭环、ReferenceData 服务 + 页面阶段和 TASK-MD-001 已完成。

**允许修改范围：** `src/backend/src/Services/OperationalData/**`、`tests/OperationalData/**`、`src/backend/IndustrialPlatform.slnx`。

**预期输出：** Api、Application、Contracts、Domain、Infrastructure 项目，DI、健康检查、模块边界和架构测试。

**验证与证据：** 运行解决方案构建、OperationalData 架构测试与健康检查测试；记录退出码和通过数量，附项目引用列表证明无跨服务 Infrastructure 引用。

**结果回写：** 更新状态；结构偏差回写蓝图 06、12、14A。

**建议提交：** `feat(operational-data): scaffold service boundaries`

## TASK-OD-002 实现库存批次与容器

**状态：** 可派遣

**目标：** 实现 InventoryLot、InventoryContainer、批次状态、供应商/生产批次关联及拆分、合并、换容器规则。

**输入文档：** 蓝图 14A 第 7 节；ReferenceData UnitOfMeasure 公开契约及 TASK-MD-002、003、005 发布的物料、批次策略、仓库和库位契约。

**依赖：** TASK-OD-001、TASK-MD-002、TASK-MD-003、TASK-MD-005。

**允许修改范围：** OperationalData Lots 模块、MasterData/ReferenceData 客户端适配、持久化和对应测试。

**预期输出：** 批次/容器聚合、状态机、唯一键、并发版本、MasterData 引用和 ReferenceData UnitReference 快照、数据库迁移。

**验证与证据：** 提供批次唯一性、冻结/解冻、拆分/合并、换容器、失效主数据、跨仓库库位和并发冲突测试结果。

**结果回写：** 回写实体、状态、唯一键、MasterData 快照与 ReferenceData UnitReference/换算快照字段；设计变化同步蓝图 14A、26。

**建议提交：** `feat(operational-data): add inventory lots and containers`

## TASK-OD-003 实现库存余额与不可变流水

**状态：** 可派遣

**目标：** 实现 InventoryBalance 和 StockTransaction，保证余额只能由库存流水更新。

**输入文档：** 蓝图 14A 第 6、11、19、21 节。

**依赖：** TASK-OD-002。

**允许修改范围：** OperationalData Inventory 模块、数据库映射、迁移和测试。

**预期输出：** 在手、预留、可用、冻结和在途数量及其 UnitReference/精度快照；唯一余额维度、乐观并发、不可变流水和余额重建能力。

**验证与证据：** 提供余额公式、负库存限制、流水不可修改、流水重放、并发冲突和事务回滚测试结果。

**结果回写：** 回写余额维度、索引、数量精度和并发策略。

**建议提交：** `feat(operational-data): add inventory ledger and balances`

## TASK-OD-004 实现库存单据、过账与冲销

**状态：** 可派遣

**目标：** 实现 InventoryDocument、单据行、Draft/Confirmed/Posting/Posted/Rejected/Cancelled 状态机、幂等过账和冲销。

**输入文档：** 蓝图 14A 第 9 至 11、16、18、21 节；ReferenceData 编码规则契约。

**依赖：** TASK-OD-003。

**允许修改范围：** OperationalData Documents 模块、应用用例、持久化和测试。

**预期输出：** 七类单据公共模型、单据编号、状态转换、过账服务、反向流水、审计链与 Outbox 原子写入。

**验证与证据：** 提供非法状态转换、重复幂等键、重复过账、冲销、原单保护、原子提交和失败回滚测试结果。

**结果回写：** 回写状态机、错误码、编号规则和冲销关联字段。

**建议提交：** `feat(operational-data): add inventory document posting`

## TASK-OD-005 实现库存预留与生产领料

**状态：** 可派遣

**目标：** 根据稳定需求标识建立/释放 StockReservation，并通过 MaterialIssue 核销预留和减少在手量。

**输入文档：** 蓝图 14A 第 8、13 节；WorkOrder 蓝图 15。

**依赖：** TASK-OD-004；WorkOrder 已确认需求标识与执行 BOM 行契约。

**允许修改范围：** OperationalData Inventory/WarehouseOperations/Contracts 及测试；WorkOrder 仅允许增加已确认的契约测试夹具。

**预期输出：** 预留、释放、超时释放、部分/完全领料、FIFO/FEFO/指定批次选择和 `InventoryReserved`/`MaterialIssued` 事件。

**验证与证据：** 提供库存不足、重复需求、并发抢占、部分核销、取消释放、冻结批次和事件契约测试结果。

**结果回写：** 回写 WorkOrder 请求、响应、事件版本和批次选择规则。

**建议提交：** `feat(operational-data): add reservations and material issue`

## TASK-OD-006 实现收料、退料与生产入库

**状态：** 可派遣

**目标：** 实现 Receipt、MaterialReturn、ProductionReceipt 的校验、库存批次创建/关联与过账。

**输入文档：** 蓝图 14A 第 12、14、15 节；MasterData 物料、仓库、库位与批次策略契约，以及 ReferenceData UnitOfMeasure 契约。

**依赖：** TASK-OD-004、TASK-OD-005。

**允许修改范围：** OperationalData WarehouseOperations/Lots/Documents/Contracts 及对应测试。

**预期输出：** 收料、生产退料、半成品/产成品入库，原领料/工单关联，待检/冻结状态及统一事件。

**验证与证据：** 提供失效物料、无效库位、重复收料、原领料超量退回、批次唯一、冻结库存及三类事件契约测试结果。

**结果回写：** 回写单据字段、批次来源、质量状态和事件字段。

**建议提交：** `feat(operational-data): add receipt return and production receipt`

## TASK-OD-007 实现调拨、盘点与库存调整

**状态：** 可派遣

**目标：** 实现同仓移动、跨仓在途调拨、盘点差异和经授权的库存调整。

**输入文档：** 蓝图 14A 第 16、22 节；MasterData 仓库/库位契约；安全蓝图 31。

**依赖：** TASK-OD-004、TASK-OD-006。

**允许修改范围：** OperationalData WarehouseOperations/Documents/Inventory、权限策略和测试。

**预期输出：** Transfer、Stocktake、Adjustment 用例；两阶段跨仓调拨、盘点审核、调整原因/证据、独立权限和审计记录。

**验证与证据：** 提供在途数量、部分到货、盘点不直接改库存、调整授权、反向流水、跨仓模式不一致和审计测试结果。

**结果回写：** 回写调拨阶段、盘点审批、调整原因和权限名称。

**建议提交：** `feat(operational-data): add transfer stocktake and adjustment`

## TASK-OD-008 发布事件并集成 Trace 与 BatchRecord

**状态：** 可派遣

**目标：** 通过 Outbox 发布稳定库存事实事件，并验证 Trace 与 BatchRecord 只建立投影和证据快照。

**输入文档：** 蓝图 08、14A 第 20、21 节、Trace 蓝图 18、BatchRecord 蓝图 19。

**依赖：** TASK-OD-005、TASK-OD-006、TASK-OD-007。

**允许修改范围：** OperationalData Contracts/Infrastructure 和跨服务契约测试；Trace/BatchRecord 仅允许增加消费者契约与去重测试。

**预期输出：** InventoryReserved/ReservationReleased、MaterialReceived/Issued/Returned、ProductionReceived、InventoryTransferred/Adjusted、InventoryLotCreated/StatusChanged 版本化事件。

**验证与证据：** 提供 Outbox 原子性、Inbox 去重、重复/乱序投递、事件兼容和消费者契约测试；证明消费者不回写库存。

**结果回写：** 回写事件版本、字段、路由键、消费者状态与兼容策略。

**建议提交：** `feat(operational-data): publish inventory integration events`

## TASK-OD-009 实现外部 WMS 适配器并完成验收

**状态：** 可派遣

**目标：** 实现仓库级 `Internal` / `ExternalWms` 模式、WMS 命令/回执/投影/对账，并完成服务全量验收。

**输入文档：** 蓝图 14A 第 17、18、21 至 25 节；目标客户 WMS 契约；TASK-MD-005 仓库模式契约。

**依赖：** TASK-OD-004 至 TASK-OD-008。

**允许修改范围：** OperationalData WmsIntegration/Contracts/Infrastructure/Api、配置、测试和验收文档；`src/frontend` 的 OperationalData api、types、stores、pages、router 和对应测试。

**预期输出：** 幂等 WMS 请求、回执、超时查询、安全重试、人工确认、库存投影、对账、错误映射及双模式能力矩阵；库存查询、批次、收发退、调拨和盘点页面连接真实 API。

**验证与证据：** 提供重复/乱序回执、超时、拒绝、重试、人工确认、投影修复、模式切换保护、单仓单权威及全量 OperationalData 测试结果；提供页面权限、业务状态、错误反馈和收发退/调拨/盘点关键路径 E2E 结果。

**结果回写：** 更新全部 OD 状态；回写能力矩阵、错误映射、对账规则、验证命令与测试总数。

**建议提交：** `feat(operational-data): add external wms integration`

## 4. 完成标准

- 无 WMS 时可完成收、发、退、调、盘、调整和生产入库闭环。
- 有 WMS 时单据等待明确回执后更新本地投影，超时不推断成功。
- 所有库存变化均可追溯到单据、不可变流水、操作者和业务时间。
- MasterData、ReferenceData、WorkOrder、Weighting、Trace、BatchRecord 均不成为库存权威；ReferenceData 只提供单位/状态定义，不接管业务执行。
- 对应 OperationalData 页面已连接真实 API，权限、契约和关键路径 E2E 全部通过。
- 领域、应用、基础设施、API、并发、幂等、契约和双模式测试全部通过。

## 5. 执行记录

| 任务 | 状态 | 执行者/任务 | 提交 | 验证证据 | 设计回写 |
| --- | --- | --- | --- | --- | --- |
| TASK-OD-001 | 可派遣 | - | - | - | - |
| TASK-OD-002 | 可派遣 | - | - | - | - |
| TASK-OD-003 | 可派遣 | - | - | - | - |
| TASK-OD-004 | 可派遣 | - | - | - | - |
| TASK-OD-005 | 可派遣 | - | - | - | - |
| TASK-OD-006 | 可派遣 | - | - | - | - |
| TASK-OD-007 | 可派遣 | - | - | - | - |
| TASK-OD-008 | 可派遣 | - | - | - | - |
| TASK-OD-009 | 可派遣 | - | - | - | - |
