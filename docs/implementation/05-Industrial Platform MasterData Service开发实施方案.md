# Industrial Platform MasterData Service 开发 TODO

版本：v1.0
当前阶段：Sprint 2
蓝图依据：`docs/blueprint/14-MasterData Service详细设计.md`

## 1. 目标与边界

MasterData 提供物料、单位、制造组织、仓库/库位、设备、BOM、工艺路线和批次策略等稳定定义。ReferenceData 负责字典、配置、元数据与编码规则；OperationalData 负责库存批次实例、余额、预留、流水和仓储单据。MasterData 禁止直接读写其他服务数据库。

当前执行前置：可运行基线、统一前端第一批、Identity 登录闭环和 ReferenceData 服务 + 页面阶段全部完成。MasterData 必须在同一阶段交付物料、单位、组织、仓库/库位、设备、BOM 和工艺路线 PC 管理页面，不再等待独立前端阶段。

依赖顺序：

```text
BuildingBlocks（已完成）→ 可运行基线 → 统一前端第一批 → Identity → ReferenceData → MasterData → OperationalData
```

## 2. 全局实施约束

- 项目结构采用 Api、Application、Contracts、Domain、Infrastructure 五层，依赖方向遵循蓝图 12。
- 标识使用 `Guid`；业务时间使用 `DateTimeOffset` / `DateTimeOffset?`；PostgreSQL 瞬时时间使用 `timestamptz`。
- 发布态主数据供下游使用；历史版本不可覆盖，失效通过状态和生效区间表达。
- 所有写接口支持租户隔离、数据权限、乐观并发和审计。
- 数据库名固定为 `masterdata_db`；表名前缀固定为 `md_`。
- 跨服务只使用 API、Contracts 和事件；禁止引用 Identity、ReferenceData 或 OperationalData 的 Infrastructure 项目。
- 每项任务遵循 TDD：先写失败测试，再实现最小代码，最后运行该任务列出的验证命令。
- 状态流转：`可派遣 → 已派遣 → 开发中 → 待验收 → 已完成`；发现蓝图冲突时改为 `设计待确认`。

## 3. 任务依赖图

```text
MD-001
  ├─ MD-002 ─ MD-003
  ├─ MD-004 ─ MD-005
  └─ MD-006
MD-003 + MD-004 ─ MD-007 ─ MD-008
MD-003..MD-008 ─ MD-009 ─ MD-010
```

## TASK-MD-001 创建 MasterData 服务骨架

**状态：** 可派遣

**目标：** 创建五层项目、测试项目、DI 注册、健康检查、配置和架构边界。

**输入文档：** 蓝图 06、12、14；现有 ReferenceData 服务骨架。

**依赖：** BuildingBlocks 已完成；TASK-BASE-006、TASK-FE-008、Identity 登录闭环和 ReferenceData 服务 + 页面阶段已完成。

**允许修改范围：** `src/backend/src/Services/MasterData/**`、`tests/MasterData/**`、`src/backend/IndustrialPlatform.slnx`。

**预期输出：** Api、Application、Contracts、Domain、Infrastructure 项目；`/health` 返回服务标识；架构测试阻止反向引用和跨服务 Infrastructure 引用。

**验证与证据：** 运行 `dotnet build src/backend/IndustrialPlatform.slnx --no-restore` 和 MasterData 架构/健康检查测试；记录命令、退出码与测试数量，并附 `dotnet list ... reference` 输出。

**结果回写：** 更新本任务状态；项目命名或依赖偏差回写蓝图 06、12、14。

**建议提交：** `feat(master-data): scaffold service boundaries`

## TASK-MD-002 实现单位、物料分类与批次策略定义

**状态：** 可派遣

**目标：** 建立 Unit、UnitConversion、MaterialCategory、BatchPolicy 等值对象和基础定义，为物料与 OperationalData 批次校验提供稳定输入。

**输入文档：** 蓝图 14 第 7、10、22、23 节；ReferenceData 已发布字典和编码规则契约。

**依赖：** TASK-MD-001。

**允许修改范围：** MasterData Domain/Application/Infrastructure 的 SharedDefinitions 模块及对应测试。

**预期输出：** 单位唯一性、精确换算、分类层级、批次号作用域、保质期和 FEFO/FIFO 策略；数据库映射与迁移。

**验证与证据：** 提供重复单位、非正换算因子、循环分类、非法批次策略和 `decimal` 精度测试结果；验证时间列为 `timestamptz`。

**结果回写：** 回写单位精度、分类深度和批次策略最终字段；影响 OperationalData 时同步蓝图 14A。

**建议提交：** `feat(master-data): add shared manufacturing definitions`

## TASK-MD-003 实现物料与物料版本生命周期

**状态：** 可派遣

**目标：** 实现 Material 聚合、物料类型与 MaterialVersion 的 Draft/Review/Released/Expired 生命周期。

**输入文档：** 蓝图 14 第 5 至 9、24、25 节。

**依赖：** TASK-MD-002。

**允许修改范围：** MasterData 的 Materials 模块、持久化映射、迁移和测试。

**预期输出：** 唯一物料编码、基本单位、版本不可变快照、生效区间、发布/失效用例和 `MaterialReleased` 领域事件。

**验证与证据：** 提供编码唯一性、版本递增、重叠生效区间、未审核发布、已发布版本修改和并发发布测试结果。

**结果回写：** 回写版本号规则、状态机、错误码和发布快照字段。

**建议提交：** `feat(master-data): add versioned materials`

## TASK-MD-004 实现制造组织层级

**状态：** 可派遣

**目标：** 实现 Factory、Workshop、ProductionLine、WorkCenter 及其层级完整性规则。

**输入文档：** 蓝图 14 第 12、13、30 节；Identity 租户与用户上下文契约。

**依赖：** TASK-MD-001。

**允许修改范围：** MasterData ManufacturingOrganizations 模块、权限适配与测试。

**预期输出：** 租户内编码唯一、父子关系、启停状态、工厂级数据权限过滤和组织查询 API。

**验证与证据：** 提供跨租户访问、非法父节点、循环层级、停用含活动子节点和并发编码冲突测试结果。

**结果回写：** 回写组织层级、删除/停用策略及 Identity 声明映射。

**建议提交：** `feat(master-data): add manufacturing organization hierarchy`

## TASK-MD-005 实现仓库、库位与库存权威模式定义

**状态：** 可派遣

**目标：** 实现 Warehouse、Location 及仓库级 `Internal` / `ExternalWms` 权威模式定义，不创建库存实例。

**输入文档：** 蓝图 14 的仓库边界说明；蓝图 14A 第 3、17 节。

**依赖：** TASK-MD-004。

**允许修改范围：** MasterData Warehouses 模块、Contracts 和对应测试；不得修改 OperationalData 实现。

**预期输出：** 仓库/库位层级、默认库位、质量用途、外部 WMS 标识、单一权威模式和发布查询契约。

**验证与证据：** 提供重复编码、跨工厂库位、无效默认库位、模式切换前置校验和单仓单权威测试；证明没有余额、批次实例或单据表。

**结果回写：** 回写 Warehouse/Location 契约和模式切换约束，并同步蓝图 14A 的消费字段。

**建议提交：** `feat(master-data): add warehouses and locations`

## TASK-MD-006 实现设备主数据

**状态：** 可派遣

**目标：** 实现 Equipment、EquipmentType、制造位置关联和设备主数据状态，不承载遥测或运行状态。

**输入文档：** 蓝图 14 第 14、15 节；IoT Collector 蓝图 17。

**依赖：** TASK-MD-004。

**允许修改范围：** MasterData EquipmentDefinitions 模块、Contracts 和测试。

**预期输出：** 设备编码、类型、工作中心/产线位置、静态属性、启停状态及 `EquipmentCreated`/`EquipmentChanged` 领域事件。

**验证与证据：** 提供重复编码、非法位置、跨工厂关联、停用和 IoT 契约兼容测试；证明未保存实时遥测。

**结果回写：** 回写设备位置规则、静态属性边界和事件字段。

**建议提交：** `feat(master-data): add equipment definitions`

## TASK-MD-007 实现 BOM 版本与发布

**状态：** 可派遣

**目标：** 实现 Bom 聚合、BomItem、版本、替代料、损耗率和 Draft/Released/Obsolete 状态机。

**输入文档：** 蓝图 14 第 16 至 18、24、25 节。

**依赖：** TASK-MD-003、TASK-MD-004。

**允许修改范围：** MasterData Boms 模块、持久化、API 和测试。

**预期输出：** 产品物料 BOM、行序号、用量与单位、工厂适用范围、生效区间、发布快照和 `BomReleased` 领域事件。

**验证与证据：** 提供空 BOM、重复行、非正用量、自引用/循环 BOM、重叠生效版本、发布后修改和并发发布测试结果。

**结果回写：** 回写 BOM 递归规则、替代料模型、版本状态机和错误码。

**建议提交：** `feat(master-data): add versioned bills of material`

## TASK-MD-008 实现工艺路线版本与发布

**状态：** 可派遣

**目标：** 实现 Routing、RoutingOperation、工序顺序、工作中心约束、版本和发布生命周期。

**输入文档：** 蓝图 14 第 19 至 21、24、25 节；WorkOrder 蓝图 15。

**依赖：** TASK-MD-007。

**允许修改范围：** MasterData Routings 模块、Contracts、持久化、API 和测试。

**预期输出：** 产品/工厂适用路线、顺序与可选工序、标准时间、BOM 行使用关系、生效版本和 `RoutingReleased` 领域事件。

**验证与证据：** 提供空路线、重复顺序、非法工作中心、循环前置关系、BOM 不兼容、重叠版本和发布后修改测试结果。

**结果回写：** 回写路线状态机、工序依赖模型和 WorkOrder 快照契约。

**建议提交：** `feat(master-data): add versioned routings`

## TASK-MD-009 实现查询 API、缓存、权限与审计

**状态：** 可派遣

**目标：** 为已实现主数据提供分页查询、按生效时间解析、Redis 缓存失效、工厂数据权限和变更审计。

**输入文档：** 蓝图 14 第 26、28 至 31 节；API 蓝图 27；日志审计蓝图 30；安全蓝图 31。

**依赖：** TASK-MD-003 至 TASK-MD-008。

**允许修改范围：** MasterData Api/Application/Infrastructure、缓存与审计适配及测试；`src/frontend` 的 MasterData api、types、stores、pages、router 和对应测试。

**预期输出：** `/api/master-data` 下稳定 API；当前/指定时间版本解析；ETag 或并发版本；缓存键包含租户和版本；审计保存操作者、原因、前后值；物料、单位、组织、仓库/库位、设备、BOM 和工艺路线管理页面。

**验证与证据：** 提供分页/过滤、跨租户隔离、工厂权限、缓存命中与失效、并发冲突、审计完整性和 OpenAPI 契约测试结果；提供页面权限、表单校验、版本发布流程和关键路径 E2E 结果。

**结果回写：** 回写最终路由、权限名称、缓存键、错误响应和分页约定。

**建议提交：** `feat(master-data): expose secured master data APIs`

## TASK-MD-010 发布集成事件并完成服务验收

**状态：** 可派遣

**目标：** 通过 Outbox 发布稳定版本事件，建立下游契约测试，并完成 MasterData 全量验收。

**输入文档：** 蓝图 08、14 第 27、33 至 35 节；OperationalData、WorkOrder、IoT Collector、Trace 对应蓝图。

**依赖：** TASK-MD-009。

**允许修改范围：** MasterData Contracts/Infrastructure/Api、下游契约测试和 MasterData 测试；下游业务实现仅允许添加契约测试夹具。

**预期输出：** MaterialReleased、BomReleased、RoutingReleased、WarehouseChanged、EquipmentCreated/Changed 等版本化事件；Outbox 原子提交、消费者兼容契约和服务验收报告。

**验证与证据：** 提供 Outbox 原子性、重复发布、事件序列化、向后兼容、全量 MasterData 测试与解决方案构建结果；记录退出码和测试总数。

**结果回写：** 更新全部 MD 状态；回写事件版本/字段、完成证据和所有设计偏差。

**建议提交：** `feat(master-data): publish master data integration events`

## 4. 完成标准

- `masterdata_db` 只保存稳定主数据定义及其版本、审计和 Outbox。
- 只有已发布且在生效区间内的定义可被下游解析。
- OperationalData 能通过契约获取物料、单位、仓库、库位和批次策略，不直接访问数据库。
- WorkOrder 能解析 BOM 和工艺路线发布快照；IoT Collector 能解析设备定义。
- 对应 MasterData PC 管理页面已连接真实 API，权限、契约和关键路径 E2E 全部通过。
- 领域、应用、基础设施、API 和契约测试全部通过，解决方案可构建。

## 5. 执行记录

| 任务 | 状态 | 执行者/任务 | 提交 | 验证证据 | 设计回写 |
| --- | --- | --- | --- | --- | --- |
| TASK-MD-001 | 可派遣 | - | - | - | - |
| TASK-MD-002 | 可派遣 | - | - | - | - |
| TASK-MD-003 | 可派遣 | - | - | - | - |
| TASK-MD-004 | 可派遣 | - | - | - | - |
| TASK-MD-005 | 可派遣 | - | - | - | - |
| TASK-MD-006 | 可派遣 | - | - | - | - |
| TASK-MD-007 | 可派遣 | - | - | - | - |
| TASK-MD-008 | 可派遣 | - | - | - | - |
| TASK-MD-009 | 可派遣 | - | - | - | - |
| TASK-MD-010 | 可派遣 | - | - | - | - |
