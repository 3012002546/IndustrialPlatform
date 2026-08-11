# 15-Industrial Platform MasterData Service开发实施方案

# Industrial Platform MasterData Service开发实施方案

> 当前里程碑范围：在 BuildingBlocks、统一运行基线、统一前端、Identity 登录闭环和 ReferenceData 完整纵向交付之后，完成单位、物料分类、批次策略、物料及版本、制造组织、仓库/库位、设备、BOM、工艺路线、缓存和集成事件，并同步交付对应 PC 管理页面、契约测试与关键路径 E2E；库存实例、生产执行、设备遥测和跨系统同步作业不在本阶段实现。

版本：V2.0

所属项目开发路线阶段：MES-01「MasterData」，当前暂缓。达到 `docs/blueprint/09-Industrial Platform开发总TodoList.md` 的恢复门禁后，在 MasterData 阶段管理会话中根据母版、项目记忆和当前代码复核本文，再派遣开发任务。

服务：

```text
IndustrialPlatform.MasterData
```

技术：

```text
.NET 10 Web API
DDD / Clean Architecture
SqlSugar 5.1.4.216
PostgreSQL
Redis / StackExchange.Redis 3.1.3
RabbitMQ.Client 7.2.2
Vue 3.5 + TypeScript 5.9 + Vite 8
Pinia 3 + Vue Router 4 + Axios 1 + Element Plus 2
Vitest 4 + Playwright 1.62
```

规格、蓝图与实施依据：

- `CLAUDE.md`
- `docs/blueprint/01-Industrial Platform 总体架构设计 V1.0.md`
- `docs/blueprint/07-PostgreSQL数据库规范及分库设计.md`
- `docs/blueprint/08-RabbitMQ事件总线设计规范.md`
- `docs/blueprint/14-MasterData Service详细设计.md`
- `docs/blueprint/14A-OperationalData Service详细设计.md`
- `docs/blueprint/15-WorkOrder Service详细设计.md`
- `docs/blueprint/17-IoT Collector Service详细设计.md`
- `docs/blueprint/23-多租户SaaS架构设计.md`
- `docs/blueprint/27-Industrial Platform API规范.md`
- `docs/blueprint/28-Industrial Platform前端工程规范.md`
- `docs/blueprint/29-Industrial Platform自动化测试体系.md`
- `docs/blueprint/30-Industrial Platform日志审计与可观测性平台设计.md`
- `docs/blueprint/31-Industrial Platform权限体系与安全架构设计.md`
- `docs/implementation/02-Industrial Platform BuildingBlocks基础组件开发实施方案.md`
- `docs/implementation/02A-Industrial Platform可运行基线开发实施方案.md`
- `docs/implementation/02B-Industrial Platform统一前端第一批开发实施方案.md`
- `docs/implementation/03-Industrial Platform Identity Service开发实施方案.md`
- `docs/implementation/06-Industrial Platform ReferenceData Service开发实施方案.md`

---

# 1. 文档说明

## 1.1 文档目的

本文同时承担 MasterData 的开发详细设计、前后端协作契约和任务派遣唯一维护源。开发人员不得仅依据蓝图中的示例类、旧 API 路径或原 V1.0 任务摘要推断聚合边界、数据字段、状态机和验收条件。

目标读者包括后端、统一前端、测试、集成和任务验收人员。实现发现设计冲突时，应先把选择、影响和稳定契约回写本文，再继续开发或验收。

## 1.2 当前输入状态

截至本次文档设计核对：

- BuildingBlocks、可运行基线和统一前端第一批已有实现；其构建、测试及冒烟数字只属于 `CLAUDE.md` 和 02A/02B 的历史证据，本轮未重新运行构建或测试。
- 当前后端仅有 Identity 和 ReferenceData 服务骨架；仓库中不存在 `src/backend/src/Services/MasterData`，也不存在 MasterData 测试项目、数据库迁移、Contracts、业务 API 或页面。
- Identity 和 ReferenceData 的业务能力尚未实现；因此 Phase 3、Phase 4 和本方案的开发前置均未满足。
- 统一前端已有 PC/PDA/Mobile 基础壳，但 MasterData API、类型、Store、路由、权限和页面均不存在。
- 蓝图 14 提供了领域方向与示例，但其中 `industrial_masterdata`、旧式 `/api/materials` 路径、基础字典归属和简化字段不是最终实现契约；本方案统一数据库名为 `masterdata_db`，API 使用版本化前缀，字典与编码规则消费 ReferenceData。
- 本文当前状态为“设计已细化、任务可在前置满足后派遣”，不代表任何 TASK-MD 已实现或已验证。

## 1.3 执行前置

```text
BuildingBlocks 与可运行基线（已有历史实现证据）
    ↓
TASK-FE-010 统一前端第一批验收（已有历史实现证据）
    ↓
TASK-ID-016 Identity 前后端联合验收（待实施）
    ↓
TASK-RD-014 ReferenceData 前后端联合验收（待实施）
    ↓
TASK-MD-001～TASK-MD-010
    ↓
OperationalData / WorkOrder / IoT Collector 等后续服务
```

前置环境或上游契约缺失不影响本文评审，但会阻止相关任务进入“已完成”。外部 PostgreSQL、Redis、RabbitMQ 或浏览器联调环境未具备时，对应验收只能标记“待验收”。

---

# 2. 服务定位、目标与职责边界

## 2.1 MasterData负责

- 单位及单位换算、物料分类和批次策略等制造主数据定义。
- 物料身份与不可覆盖的发布版本、生效区间和生命周期。
- 工厂、车间、产线、工作中心等制造组织结构。
- 仓库、库位、用途和库存权威模式定义，但不保存库存事实。
- 设备类型、设备身份、制造位置和静态扩展属性，但不保存遥测与运行事实。
- BOM、BOM 行、替代料、损耗率、版本和发布快照。
- 工艺路线、工序、前置关系、工作中心约束、标准时间、BOM 行使用关系、版本和发布快照。
- 上述能力的版本化 API、PC 管理页面、权限、审计、缓存、Outbox 事件和消费契约。

MasterData 是稳定制造业务定义的权威来源；发布后供业务执行使用的版本必须可复现，不能被后续编辑覆盖。

## 2.2 MasterData不负责

| 不属于本服务的内容 | 权威归属 | MasterData 允许保存的引用或快照 |
| --- | --- | --- |
| 用户、角色、登录、租户成员关系 | Identity | `tenantId`、`userId`、权限与工厂数据范围上下文 |
| 字典、配置、元数据定义、编码规则 | ReferenceData | 对应 NId、规则版本、schema revision 和必要显示快照 |
| 库存批次实例、余额、预留、流水和仓储单据 | OperationalData | 物料、单位、仓库、库位和批次策略的 NId/发布版本 |
| 生产订单、派工、报工与执行状态 | WorkOrder/MES | Material/BOM/Routing 发布快照引用 |
| 设备采集点、遥测、报警和在线状态 | IoT Collector/IoT Platform | EquipmentNId 和静态设备标识 |
| 称量任务、过程和结果 | Weighting | 物料、单位、BOM 与批次策略引用 |
| 批次谱系、序列号和追踪关系 | Trace | 发布事件携带的稳定 NId 与版本 |
| ERP/LIMS/PLM 同步调度、重试与对账 | 后续集成服务 | 外部系统标识与来源字段；不在本阶段启动同步作业 |

MasterData 禁止直接读写其他服务数据库；其他服务也禁止直接读取 `masterdata_db`。

## 2.3 本阶段取舍

- PC 管理端完整交付；PDA/Mobile 不新增主数据维护入口。
- ReferenceData 负责字典、元数据和编码规则。MasterData 不创建 `md_dictionary`，物料/设备类型等可配置枚举通过 ReferenceData 契约解析。
- 批次策略只定义批号作用域、保质期和 FIFO/FEFO 等规则；具体批次、库存和拣选决策属于 OperationalData。
- 仓库只声明 `Internal` 或 `ExternalWms` 权威模式；本阶段不实现 WMS 接口、库存同步或切换作业。
- 设备静态扩展属性可以按 ReferenceData 的 schema revision 保存；实时测点和遥测不得写入 MasterData。
- 外部 ERP/LIMS/PLM 仅保留来源与外部标识扩展点，不实现连接器和同步流程。

---

# 3. 前后端及跨服务协作目标

纵向交付链固定为：

```text
领域聚合、版本与发布规则
    ↓
应用命令、查询、权限与事务
    ↓
版本化 API、DTO、错误码和事件 V1
    ↓
MasterData PC 管理页面
    ↓
OperationalData / WorkOrder / IoT 等消费契约
    ↓
契约测试、组件测试、关键路径 E2E
    ↓
Phase 5 联合验收
```

统一前端只通过 Gateway 调用业务 API。前端不得根据显示文本推断状态、权限或实体类型；状态值、权限码、错误码和并发版本均来自稳定契约。

后续服务只通过 `IndustrialPlatform.MasterData.Contracts`、同步 API 或版本化事件消费能力，禁止引用 MasterData.Domain/Infrastructure。跨服务只保存 `{EntityName}NId`、发布版本及必要快照，不建立跨库外键。

---

# 4. 总体架构与数据流

```text
PC Browser
  │ Bearer Token / tenant_id / permission / factory scope
  ▼
API Gateway  /masterdata/api/v1/master-data/**
  │ 去除 /masterdata 前缀
  ▼
MasterData.Api  /api/v1/master-data/**
  │
  ├── Application：命令、查询、授权、校验和事务编排
  │       ├── Domain：单位、物料、组织、仓储定义、设备、BOM、Routing
  │       └── Contracts：DTO、错误码和集成事件 V1
  │
  └── Infrastructure
          ├── masterdata_db：权威数据、迁移、Outbox、审计
          ├── Redis：已发布读取模型缓存
          └── RabbitMQ：发布版本化变更事件

ReferenceData
  └── 字典、编码、元数据与配置契约

OperationalData / WorkOrder / IoT / Weighting / Trace
  ├── 同步读取当前或指定时间的已发布定义
  └── 消费事件并维护本服务所需投影或快照
```

写入链路必须在同一数据库事务内提交聚合变更、审计和 Outbox；缓存失效与 RabbitMQ 投递在事务提交后执行。读取链路按租户和工厂范围授权，只返回未软删除且满足状态/生效时间的结果。

---

# 5. 项目结构与引用关系

目标结构：

```text
src/backend/src/Services/MasterData/
├── IndustrialPlatform.MasterData.Domain/
├── IndustrialPlatform.MasterData.Application/
├── IndustrialPlatform.MasterData.Contracts/
├── IndustrialPlatform.MasterData.Infrastructure/
└── IndustrialPlatform.MasterData.Api/

tests/MasterData/
├── IndustrialPlatform.MasterData.Domain.Tests/
├── IndustrialPlatform.MasterData.Application.Tests/
├── IndustrialPlatform.MasterData.Infrastructure.Tests/
├── IndustrialPlatform.MasterData.Api.Tests/
└── IndustrialPlatform.MasterData.Contracts.Tests/

src/frontend/src/
├── api/masterData/
├── types/masterData/
├── stores/masterData/
├── pages/masterData/
└── router/
```

引用方向：

```text
Domain → SharedKernel
Contracts → SharedKernel（仅确有公共契约类型时）
Application → Domain + Contracts + Application.Abstractions + Security
Infrastructure → Application + Domain + Contracts + Infrastructure + EventBus
Api → Application + Contracts + Infrastructure + Web + Logging
```

禁止 Domain 引用 Application、Infrastructure 或 Api；禁止 MasterData 引用其他服务的 Domain/Infrastructure；跨服务编译期依赖只允许稳定 Contracts。新增项目引用必须由架构测试锁定。

---

# 6. 全局技术与实施约束

- 目标框架、Nullable、分析器、警告即错误和中央包版本遵循根 `Directory.Build.props`、`Directory.Packages.props` 与 `.editorconfig`。
- 实体技术主键 `Id` 使用 `Guid`；实体稳定业务标识使用 `NId`，业务表引用使用 `{EntityName}NId`。`Code` 只表示编码结果，不承担跨服务身份语义。
- 所有业务时间使用 `DateTimeOffset`/`DateTimeOffset?`；PostgreSQL 映射 `timestamptz`，禁止 `DateTime`。
- 每个实体应用 BuildingBlocks `Entity` 的冻结、锁定、软删除、审计时间和双版本并发约束。写请求必须携带调用方读取时的 `OptimisticVersion` 与 `ConcurrencyVersion`。
- 数据库固定为 `masterdata_db`，表前缀固定为 `md_`，PostgreSQL 物理名称使用 `snake_case`。
- 所有租户数据必须带 `TenantId` 并在仓储层强制隔离；工厂范围数据还必须通过授权过滤器限制 FactoryNId。
- 发布版本不可原地修改。修订必须创建新草稿；失效通过状态与生效区间表达，历史记录不得物理覆盖。
- API 基础路径固定为 `/api/v1/master-data`；Gateway 外部路径固定为 `/masterdata/api/v1/master-data`。
- 所有写用例采用 TDD；每项任务先形成失败测试，再实现最小行为，并按任务卡记录证据。
- 稳定错误使用业务错误码，不以异常文本形成前端契约；日志不得包含 Token、密码、连接串或完整敏感前后值。
- 状态只使用 `待细化 → 可派遣 → 已派遣 → 开发中 → 待验收 → 已完成`；设计冲突使用 `设计待确认`。本文十个任务当前均为“可派遣”，但必须等待各自前置满足。

## 6.1 统一数据建模约束

- 下文实体表只列业务字段；所有实体同时继承统一生命周期字段，不逐表重复：`Id`、`IsFrozen`、`IsLocked`、`IsDeleted`、`EntityType`、`CreatedOn`、`LastUpdatedOn`、`OptimisticVersion`、`ConcurrencyVersion`。
- 同库父子表使用父表 `Id + IsDeleted` 复合外键；子表保存 `{ParentEntity}_Id`、`{ParentEntity}_IsDeleted`，并保留自身独立 `IsDeleted`。父表声明 `unique (id, is_deleted)`。
- 父表软删除/恢复必须用 `ON UPDATE CASCADE` 或同一事务内等价机制同步子表父删除状态快照；有效子记录查询同时过滤自身和父引用删除状态。
- 唯一索引默认包含 `tenant_id` 和业务作用域；软删除后的编码是否可复用由聚合明确约束，本阶段默认不复用。
- 跨服务只保存 NId、外部标识和必要快照，不建立数据库外键。

---

# 7. 领域模型与发布生命周期详细设计

## 7.1 单位、分类与批次策略

| 聚合/实体 | 主要业务字段 | 核心约束 |
| --- | --- | --- |
| Unit | `NId`、`TenantId`、`Code`、`Name`、`Symbol`、`Dimension`、`DecimalPlaces`、`IsBaseUnit`、`Status` | 租户内 Code 唯一；小数位 0～12；停用前检查有效物料/BOM 引用 |
| UnitConversion | `FromUnitNId`、`ToUnitNId`、`Factor`、`Offset`、`Precision` | 同一维度；Factor 大于 0；禁止重复方向和循环产生不一致换算 |
| MaterialCategory | `NId`、`ParentNId`、`Code`、`Name`、`Path`、`Level`、`Status` | 租户内 Code 唯一；禁止循环；停用父节点前处理活动子节点 |
| BatchPolicy | `NId`、`Code`、`LotScope`、`ShelfLifeDays`、`PickingStrategy`、`RequireExpiryDate`、`Status` | `ShelfLifeDays` 非负；PickingStrategy 为 FIFO/FEFO；只定义策略，不创建批次 |

换算统一为 `target = source × Factor + Offset`，精度由目标单位和换算记录共同限制；质量、长度、体积等不同 Dimension 禁止换算。

## 7.2 物料与版本

`Material` 保存稳定身份，`MaterialVersion` 保存可审阅、发布和按时间解析的业务定义：

| 类型 | 主要业务字段 |
| --- | --- |
| Material | `NId`、`TenantId`、`Code`、`CurrentReleasedVersionNId`、`Status`、`ExternalSource`、`ExternalId` |
| MaterialVersion | `NId`、`MaterialNId`、`VersionNo`、`Name`、`ShortName`、`MaterialType`、`CategoryNId`、`BaseUnitNId`、`BatchPolicyNId`、`SchemaRevision`、`Attributes`、`EffectiveFrom`、`EffectiveTo`、`Status`、`ChangeReason`、`ReviewedBy`、`ReviewedOn`、`ReleasedBy`、`ReleasedOn` |

版本状态机：

```text
Draft → Review → Released → Expired
  ↑       │
  └───────┘ Reject
```

- 一个 Material 可有多个草稿/历史版本，但同一 `VersionNo` 唯一。
- 发布前必须完成分类、单位、批次策略、元数据 revision 和生效区间校验。
- 同一 Material 的 Released 生效区间不得重叠；解析时传入 `asOf`，只返回覆盖该时刻的唯一发布版本。
- Released 内容不可修改；变更时复制为新 Draft。`CurrentReleasedVersionNId` 只是当前读取优化，不替代按时间解析。
- 发布产生领域事件，Outbox 转换为 `MaterialReleasedIntegrationEventV1`。

## 7.3 制造组织

```text
Factory
  └── Workshop
        └── ProductionLine
              └── WorkCenter
```

每个节点包含 `NId`、`TenantId`、`Code`、`Name`、父节点 NId、`Status`；Factory 另含地址和时区，WorkCenter 可含能力标签。只能按固定层级挂接，禁止循环和跨租户父子关系。停用节点前必须确认没有活动子节点；删除不替代停用。

Identity 表达“谁属于哪里”，MasterData 表达“制造地点与能力是什么”。用户的工厂数据范围从 Identity 声明映射为 FactoryNId 过滤，不在 MasterData 复制成员关系。

## 7.4 仓库与库位定义

| 类型 | 主要业务字段 | 约束 |
| --- | --- | --- |
| Warehouse | `NId`、`TenantId`、`FactoryNId`、`Code`、`Name`、`AuthorityMode`、`ExternalWmsId`、`Status` | `AuthorityMode` 仅 `Internal`/`ExternalWms`；外部模式必须有 ExternalWmsId |
| Location | `NId`、`WarehouseNId`、`ParentLocationNId`、`Code`、`Name`、`Purpose`、`IsDefault`、`Status` | 仓库内 Code 唯一；父库位同仓；每用途最多一个默认库位 |

Warehouse/Location 不包含数量、批次、可用量、预留或库存状态。权威模式切换只允许在 OperationalData/WMS 前置校验返回可切换结果后执行；本阶段定义校验接口和状态，不实现数据搬迁。

## 7.5 设备主数据

`EquipmentType` 定义类型与 ReferenceData schema revision；`Equipment` 保存 `NId`、`TenantId`、`Code`、`Name`、`EquipmentTypeNId`、`FactoryNId`、`WorkshopNId`、`ProductionLineNId`、`WorkCenterNId`、静态 `Attributes`、`ExternalSource`、`ExternalId` 和 `Status`。

- 位置引用必须形成同一 Factory 下的合法祖先链；不得直接把设备挂到其他租户或工厂节点。
- 静态属性必须按指定 schema revision 校验，保存强类型 JSON 与 revision；不自动追随新 schema 覆盖旧值。
- 运行、故障、在线、报警和遥测属于 IoT；MasterData 不保存这些字段。
- 创建与有效变更产生 `EquipmentCreatedIntegrationEventV1` 或 `EquipmentChangedIntegrationEventV1`。

## 7.6 BOM 版本与发布

| 类型 | 主要业务字段 |
| --- | --- |
| Bom | `NId`、`TenantId`、`ProductMaterialNId`、`FactoryNId`、`Code`、`CurrentReleasedVersionNId`、`Status` |
| BomVersion | `NId`、`BomNId`、`VersionNo`、`EffectiveFrom`、`EffectiveTo`、`Status`、`ChangeReason`、发布审计字段 |
| BomItem | `NId`、`BomVersionNId`、`Sequence`、`ComponentMaterialNId`、`Quantity`、`UnitNId`、`ScrapRate`、`IsOptional` |
| BomItemSubstitute | `NId`、`BomItemNId`、`SubstituteMaterialNId`、`Priority`、`ConversionRatio`、`UsageLimit` |

- BOM 至少一个有效行；Sequence 在版本内唯一；Quantity 与 ConversionRatio 大于 0，ScrapRate 在 `[0,1)`。
- 产品、组件、替代料必须解析为可用物料；单位必须能与组件基础单位换算。
- 禁止直接自引用，也必须用依赖图检测跨 BOM 版本循环。
- 发布版本不可修改，生效区间不得与同一产品/工厂作用域的其他发布版本重叠。
- 发布快照包含产品版本、组件版本、单位换算和替代料规则，保证 WorkOrder 后续重放不受主数据新版本影响。

## 7.7 工艺路线版本与发布

| 类型 | 主要业务字段 |
| --- | --- |
| Routing | `NId`、`TenantId`、`ProductMaterialNId`、`FactoryNId`、`Code`、`CurrentReleasedVersionNId`、`Status` |
| RoutingVersion | `NId`、`RoutingNId`、`VersionNo`、`BomVersionNId`、`EffectiveFrom`、`EffectiveTo`、`Status`、发布审计字段 |
| RoutingOperation | `NId`、`RoutingVersionNId`、`Code`、`Name`、`Sequence`、`WorkCenterNId`、`SetupMinutes`、`RunMinutes`、`IsOptional` |
| RoutingOperationDependency | `OperationNId`、`PredecessorOperationNId`、`DependencyType` |
| RoutingOperationBomUsage | `OperationNId`、`BomItemNId`、`UsageQuantity` |

- 路线至少一个工序；Code 和 Sequence 在版本内唯一；时间非负。
- 工作中心必须处于路线 Factory 范围且启用；工序依赖图必须无环。
- 引用的 BOM 版本必须已发布、产品和工厂兼容，并覆盖路线生效区间。
- 发布快照包含工序顺序、依赖、工作中心和 BOM 使用关系；发布产生 `RoutingReleasedIntegrationEventV1`。

---

# 8. 数据与持久化设计

核心表：

| 模块 | 表 |
| --- | --- |
| 单位与定义 | `md_unit`、`md_unit_conversion`、`md_material_category`、`md_batch_policy` |
| 物料 | `md_material`、`md_material_version` |
| 制造组织 | `md_factory`、`md_workshop`、`md_production_line`、`md_work_center` |
| 仓储定义 | `md_warehouse`、`md_location` |
| 设备 | `md_equipment_type`、`md_equipment` |
| BOM | `md_bom`、`md_bom_version`、`md_bom_item`、`md_bom_item_substitute` |
| Routing | `md_routing`、`md_routing_version`、`md_routing_operation`、`md_routing_operation_dependency`、`md_routing_operation_bom_usage` |
| 横切 | `md_audit_entry`、`md_outbox_message` |

持久化约束：

- 所有租户表索引以 `tenant_id` 开头；Code 唯一性包含明确业务作用域和软删除策略。
- `effective_from`、`effective_to` 使用 `timestamptz`，采用左闭右开区间 `[from,to)`；空 `effective_to` 表示无上限。
- 金额外的数量/换算统一使用显式 PostgreSQL `numeric(p,s)`；精度在迁移和契约测试中锁定，禁止依赖 provider 默认值。
- JSONB 只用于经 ReferenceData schema revision 验证的静态扩展属性，不用于替代关系表或关键查询字段。
- 发布命令通过事务和并发条件保证“校验区间、更新当前指针、写审计、写 Outbox”原子完成。
- migration 必须可前向执行；破坏性 schema 变更需独立迁移计划，不在普通任务中清空或重建数据库。

---

# 9. Application用例、API、事件与外部集成契约

## 9.1 通用应用约定

- 命令用例：创建、修改、送审、驳回、发布、失效、启停、软删除/恢复。
- 查询用例：分页列表、详情、树、当前发布版本、指定 `asOf` 解析、版本历史和发布快照。
- 分页参数 `pageNumber` 从 1 开始，`pageSize` 默认 20、最大 200；排序字段使用白名单。
- 写请求携带 `optimisticVersion`、`concurrencyVersion` 和非空 `changeReason`；创建请求除外。
- `ApiResult<T>`/`PageResult<T>`、CorrelationId、TraceId 和异常信封沿用 BuildingBlocks/Web 契约。

## 9.2 API路径

基础路径：`/api/v1/master-data`。

| 资源 | 核心 API |
| --- | --- |
| Units | `GET/POST /units`、`GET/PUT /units/{unitNId}`、`POST /units/{unitNId}/conversions`、`POST /units/{unitNId}/enable|disable` |
| Material definitions | `GET/POST /material-categories`、`GET/POST /batch-policies` |
| Materials | `GET/POST /materials`、`GET /materials/{materialNId}`、`POST /materials/{materialNId}/versions`、`PUT /materials/{materialNId}/versions/{versionNId}`、`POST .../submit-review|reject|release|expire`、`GET /materials/{materialNId}/resolve?asOf=` |
| Organizations | `GET /organizations/tree`、`POST/PUT /factories`、`/workshops`、`/production-lines`、`/work-centers`、启停端点 |
| Warehouses | `GET/POST /warehouses`、`GET/PUT /warehouses/{warehouseNId}`、`POST/PUT /locations`、`POST /warehouses/{warehouseNId}/authority-mode` |
| Equipment | `GET/POST /equipment-types`、`GET/POST /equipment`、`GET/PUT /equipment/{equipmentNId}`、启停端点 |
| BOM | `GET/POST /boms`、`GET /boms/{bomNId}`、`POST /boms/{bomNId}/versions`、版本编辑/发布/失效、`GET /boms/resolve?productMaterialNId=&factoryNId=&asOf=` |
| Routing | `GET/POST /routings`、`GET /routings/{routingNId}`、`POST /routings/{routingNId}/versions`、版本编辑/发布/失效、`GET /routings/resolve?productMaterialNId=&factoryNId=&asOf=` |

管理 API 返回完整草稿与并发字段；运行时 resolve API 只返回已发布快照，不暴露内部主键、审计前后值或软删除数据。

## 9.3 集成事件

事件 envelope 至少包含 `eventId`、`eventType`、`eventVersion`、`occurredOn`、`tenantId`、`aggregateNId`、`correlationId` 和 `payload`。

| 事件 | 关键 payload |
| --- | --- |
| `MaterialReleasedIntegrationEventV1` | MaterialNId、VersionNId/VersionNo、生效区间、类型、分类、基础单位、批次策略、schema revision |
| `BomReleasedIntegrationEventV1` | BomNId、BomVersionNId/VersionNo、ProductMaterialNId、FactoryNId、生效区间、snapshotVersion |
| `RoutingReleasedIntegrationEventV1` | RoutingNId、RoutingVersionNId/VersionNo、ProductMaterialNId、FactoryNId、BomVersionNId、生效区间、snapshotVersion |
| `WarehouseChangedIntegrationEventV1` | WarehouseNId、FactoryNId、AuthorityMode、状态、changeKind |
| `EquipmentCreatedIntegrationEventV1` | EquipmentNId、Code、TypeNId、FactoryNId、位置链、schema revision |
| `EquipmentChangedIntegrationEventV1` | EquipmentNId、TypeNId、位置链、状态、schema revision、changeKind |

消费者按 `eventId` 幂等。V1 已发布字段只能向后兼容追加；删除、重命名或语义变化必须发布新事件版本。领域事件不直接作为跨服务消息。

## 9.4 缓存与一致性

缓存键必须含版本前缀与租户：

```text
masterdata:v1:{tenantId}:material:{materialNId}:{asOfBucket}
masterdata:v1:{tenantId}:bom:{productMaterialNId}:{factoryNId}:{asOfBucket}
masterdata:v1:{tenantId}:routing:{productMaterialNId}:{factoryNId}:{asOfBucket}
masterdata:v1:{tenantId}:equipment:{equipmentNId}:{optimisticVersion}
masterdata:v1:{tenantId}:organization:{factoryNId}:{optimisticVersion}
```

只缓存已发布读取模型。写事务提交后删除受影响的当前键并发布事件；缓存不可用时回源数据库，不能把基础设施故障伪装成“数据不存在”。负缓存只允许用于稳定 NId 短 TTL 查询，发布/恢复后必须失效。

## 9.5 外部集成边界

本阶段不实现 ERP、LIMS、PLM 或 WMS 连接器。实体预留 `ExternalSource`、`ExternalId`，唯一索引为租户 + 来源 + 外部标识；外部系统写入仍必须经过相同应用用例、权限/系统身份、并发、审计和发布规则，禁止直接写库。

---

# 10. 页面、路由与交互设计

仅交付 PC 管理端。路由统一挂在 `/pc/master-data`，菜单和按钮由权限码控制：

| 页面 | 路由 | 核心能力 |
| --- | --- | --- |
| 单位与基础定义 | `/pc/master-data/definitions` | 单位/换算、分类树、批次策略列表与启停 |
| 物料管理 | `/pc/master-data/materials` | 筛选分页、物料详情、版本时间线、草稿编辑、送审/发布/失效 |
| 制造组织 | `/pc/master-data/organizations` | 工厂到工作中心树、节点编辑、启停校验 |
| 仓库与库位 | `/pc/master-data/warehouses` | 仓库列表、库位树、默认用途、权威模式确认 |
| 设备管理 | `/pc/master-data/equipment` | 类型、位置、静态属性表单、启停 |
| BOM管理 | `/pc/master-data/boms` | 版本列表、行/替代料编辑、循环校验、发布 |
| 工艺路线 | `/pc/master-data/routings` | 工序表、前置关系、工作中心、BOM 行使用、发布 |

页面共同规则：

- 列表保留 URL 查询参数；空状态、加载、无权限、业务错误和网络错误使用统一基础组件。
- 编辑页读取并回传双并发版本；409 冲突保留用户输入并提供刷新/对比入口，不自动覆盖。
- 发布动作展示版本、生效区间和影响摘要，要求填写变更原因并二次确认。
- Released 字段只读；创建新修订是唯一修改入口。
- 树节点支持键盘导航，表单错误与控件用 `aria-describedby` 关联；不得只用颜色表达状态。
- 页面不自行实现单位换算、循环检测或发布资格等权威业务规则；前端可预校验，最终以后端结果为准。

---

# 11. 错误、安全、审计与可观测性

## 11.1 稳定错误码

| 错误码 | HTTP | 语义 |
| --- | --- | --- |
| `MD_NOT_FOUND` | 404 | 指定主数据不存在或不可见 |
| `MD_DUPLICATE_CODE` | 409 | 业务作用域内编码冲突 |
| `MD_CONCURRENCY_CONFLICT` | 409 | 双版本并发检查失败 |
| `MD_INVALID_STATE_TRANSITION` | 409 | 生命周期转换不允许 |
| `MD_EFFECTIVE_RANGE_OVERLAP` | 409 | 已发布生效区间重叠 |
| `MD_REFERENCE_NOT_ACTIVE` | 422 | 引用的主数据或 ReferenceData 定义不可用 |
| `MD_HIERARCHY_INVALID` | 422 | 层级、跨工厂或循环关系非法 |
| `MD_UNIT_CONVERSION_INVALID` | 422 | 单位维度、因子或精度非法 |
| `MD_BOM_CYCLE_DETECTED` | 422 | BOM 自引用或递归循环 |
| `MD_ROUTING_DEPENDENCY_CYCLE` | 422 | 工序前置关系成环 |
| `MD_FACTORY_SCOPE_DENIED` | 403 | 无目标工厂数据权限 |
| `MD_AUTHORITY_MODE_CHANGE_REJECTED` | 409 | 仓库权威模式切换前置校验失败 |

## 11.2 安全与授权

建议权限码：

```text
masterdata.definitions.read / manage
masterdata.materials.read / manage / review / release
masterdata.organizations.read / manage
masterdata.warehouses.read / manage
masterdata.equipment.read / manage
masterdata.boms.read / manage / release
masterdata.routings.read / manage / release
masterdata.audit.read
```

租户隔离和工厂数据范围是服务端强制条件；拥有页面权限不等于拥有所有工厂数据。跨租户 NId 对无权限调用者统一表现为不可见，避免枚举泄露。写入外部属性 JSON 前按 ReferenceData schema 限制字段、类型、大小和深度。

## 11.3 审计与可观测性

- 创建、修改、启停、送审、驳回、发布、失效、删除/恢复和权威模式切换必须记录操作者、租户、工厂范围、原因、时间、CorrelationId、聚合 NId、版本和脱敏前后值。
- 日志以 TraceId/CorrelationId、TenantId、AggregateNId、Operation 和结果码形成结构化字段；不记录 Token 或完整大对象。
- 指标至少覆盖命令成功/失败、发布冲突、解析延迟、缓存命中/回源、Outbox 待投递/重试/失败和消费延迟。
- `/health`、`/health/live`、`/health/ready` 沿用平台语义；ready 检查 PostgreSQL/Redis/RabbitMQ，但响应不泄露连接信息。

---

# 12. 自动化测试与验收设计

| 层次 | 必测内容 |
| --- | --- |
| Domain | 状态机、区间重叠、单位换算、层级循环、BOM 循环、Routing 依赖、发布后不可变 |
| Application | 权限、租户/工厂范围、命令事务、ReferenceData 引用校验、并发和错误映射 |
| Infrastructure | PostgreSQL 映射/索引/复合外键、软删除、事务、缓存失效、Outbox、RabbitMQ 序列化 |
| API/Contracts | 路径、DTO、分页、双并发字段、错误信封、OpenAPI、事件 V1 向后兼容 |
| Frontend unit/component | Store、权限、表单、树、版本时间线、冲突恢复、发布确认和可访问性 |
| E2E | 物料创建到发布、BOM 发布、Routing 发布、权限拒绝、并发冲突和读取解析 |

关键验收场景：

1. 创建单位/分类/批次策略，创建物料草稿，送审并发布；指定生效时刻只能解析唯一版本。
2. 两个客户端基于同一版本编辑，后提交者收到 `MD_CONCURRENCY_CONFLICT`，页面不丢输入。
3. 跨租户和无 FactoryNId 范围访问被拒绝，列表也不泄露数量。
4. BOM 空行、重复序号、自引用、间接循环和重叠发布均失败；有效发布写入快照与 Outbox。
5. Routing 的非法工作中心、环形依赖、BOM 不兼容失败；有效发布可被 WorkOrder 契约夹具消费。
6. Redis 不可用时已发布读取回源数据库；缓存失效后不会返回旧版本。
7. 页面关键路径无未处理 console/page error，并满足键盘与错误关联基线。

每次任务证据必须记录日期、环境、命令、退出码、通过/失败数量和待验收项。引用 02A/02B 或 `CLAUDE.md` 的数字必须显式写“历史证据”；未在该轮执行的命令不得写成当前通过。

---

# 13. 开发任务依赖

```text
TASK-MD-001
  ├── TASK-MD-002 ── TASK-MD-003
  └── TASK-MD-004 ──┬── TASK-MD-005
                     └── TASK-MD-006

TASK-MD-003 + TASK-MD-004 ── TASK-MD-007 ── TASK-MD-008

TASK-MD-003～TASK-MD-008 ── TASK-MD-009 ── TASK-MD-010
```

TASK-MD-002 与 TASK-MD-004 在 001 完成后可并行；005 与 006 在 004 完成后可并行。009 负责把各纵向能力接入统一查询、安全、缓存和 PC 页面，010 才执行跨服务契约与阶段联合验收。

---

# 14. MasterData开发任务拆分

## TASK-MD-001 对齐服务骨架、Contracts、鉴权与测试结构

**状态：** 可派遣

**目标：** 创建五层服务、分层测试、DI、健康检查、开发配置和架构边界，并接入 Gateway/解决方案。

**输入文档：** 本文第 1～6、11～12 节；蓝图 01、07、08、14、23、27、29～31；现有 ReferenceData 服务骨架仅作结构参考。

**依赖：** TASK-ID-016、TASK-RD-014 已完成；BuildingBlocks 与运行基线可用。

**允许修改范围：** `src/backend/src/Services/MasterData/**`、`tests/MasterData/**`、`src/backend/IndustrialPlatform.slnx`、Gateway 路由/健康聚合和必要中央工程配置。

**预期输出：** Domain/Application/Contracts/Infrastructure/Api 项目；三类健康端点；配置绑定；认证/租户/工厂范围接入点；架构测试阻止反向引用和跨服务 Infrastructure 引用。

**验证与证据：** 运行解决方案构建、MasterData 架构/DI/配置/健康测试和项目引用检查；记录命令、退出码、警告与测试数量。真实依赖不可用时 ready 联调标记“待验收”。

**结果回写：** 回写项目名、引用图、端口、Gateway 路由、健康检查和所有设计偏差。

**建议提交：** `feat(master-data): scaffold service boundaries`

## TASK-MD-002 实现单位、物料分类与批次策略定义

**状态：** 可派遣

**目标：** 建立 Unit、UnitConversion、MaterialCategory 和 BatchPolicy 纵向闭环，为物料、BOM 与 OperationalData 提供稳定定义。

**输入文档：** 本文第 7.1、8、9、11～12 节；ReferenceData 已发布字典、编码与元数据契约。

**依赖：** TASK-MD-001。

**允许修改范围：** MasterData 的 Definitions Domain/Application/Contracts/Infrastructure/Api、对应测试；不修改 ReferenceData 实现。

**预期输出：** 单位唯一性与精确换算、分类树、批号作用域/保质期/FIFO/FEFO 策略、持久化迁移、版本化 API、权限和契约测试。

**验证与证据：** 覆盖重复单位、跨维度/非正因子、精度、循环分类、活动子节点、非法批次策略、租户隔离和 PostgreSQL numeric/timestamptz 映射。

**结果回写：** 回写单位精度、换算公式、分类深度、批次策略字段、错误码和 OperationalData 消费契约。

**建议提交：** `feat(master-data): add manufacturing definitions`

## TASK-MD-003 实现物料与物料版本生命周期

**状态：** 可派遣

**目标：** 实现 Material 稳定身份、MaterialVersion 的 Draft/Review/Released/Expired 生命周期和按时间解析。

**输入文档：** 本文第 7.2、8～9、11～12 节；蓝图 14 第 6～9、24～25 节。

**依赖：** TASK-MD-002。

**允许修改范围：** MasterData Materials 模块、持久化、API、事件转换和对应测试。

**预期输出：** 租户内唯一编码、版本递增、ReferenceData schema 校验、生效区间、发布快照、发布/失效用例和 `MaterialReleasedIntegrationEventV1` Outbox 记录。

**验证与证据：** 覆盖编码冲突、版本冲突、重叠区间、未审核发布、Released 修改、指定时刻解析、双并发发布和事件字段。

**结果回写：** 回写版本号规则、状态机、快照字段、错误码、API 与事件最终契约。

**建议提交：** `feat(master-data): add versioned materials`

## TASK-MD-004 实现制造组织层级

**状态：** 可派遣

**目标：** 实现 Factory、Workshop、ProductionLine、WorkCenter 固定层级及工厂数据范围过滤。

**输入文档：** 本文第 7.3、8～12 节；Identity 租户、用户和数据范围契约。

**依赖：** TASK-MD-001。

**允许修改范围：** MasterData Organizations 模块、权限适配、API、持久化和测试。

**预期输出：** 租户/作用域内唯一编码、父子完整性、启停、组织树 API、FactoryNId 权限过滤和 PC 可消费契约。

**验证与证据：** 覆盖跨租户、非法父类型、循环、跨工厂、停用含活动子节点、并发编码冲突和数据范围不泄露。

**结果回写：** 回写层级、删除/停用策略、Identity 声明映射、权限码和错误码。

**建议提交：** `feat(master-data): add manufacturing organization hierarchy`

## TASK-MD-005 实现仓库、库位与库存权威模式定义

**状态：** 可派遣

**目标：** 实现 Warehouse、Location 与 `Internal`/`ExternalWms` 权威模式，明确不创建库存事实。

**输入文档：** 本文第 2.2、7.4、8～12 节；蓝图 14A 的库存与 WMS 边界。

**依赖：** TASK-MD-004。

**允许修改范围：** MasterData Warehouses 模块、Contracts、持久化、API 和测试；不修改 OperationalData 业务实现。

**预期输出：** 仓库/库位层级、默认用途、外部 WMS 标识、权威模式切换前置契约、查询/管理 API 和 `WarehouseChangedIntegrationEventV1`。

**验证与证据：** 覆盖重复编码、跨工厂库位、层级循环、无效默认库位、模式切换失败、单仓单权威、事件契约；证明无余额、批次实例或单据表。

**结果回写：** 回写 Warehouse/Location DTO、切换约束、错误码及 OperationalData 消费字段。

**建议提交：** `feat(master-data): add warehouses and locations`

## TASK-MD-006 实现设备主数据

**状态：** 可派遣

**目标：** 实现 EquipmentType、Equipment、制造位置和静态扩展属性，不承载遥测或运行状态。

**输入文档：** 本文第 7.5、8～12 节；蓝图 14 第 14～15 节；蓝图 17。

**依赖：** TASK-MD-004。

**允许修改范围：** MasterData Equipment 模块、Contracts、持久化、API 和测试。

**预期输出：** 设备编码、类型、合法位置链、schema revision、静态属性、启停及 `EquipmentCreatedIntegrationEventV1`/`EquipmentChangedIntegrationEventV1`。

**验证与证据：** 覆盖重复编码、非法位置、跨工厂、元数据类型/大小、停用、双并发和 IoT 消费兼容；证明未保存遥测、报警和在线状态。

**结果回写：** 回写位置规则、属性边界、事件字段、权限码和错误码。

**建议提交：** `feat(master-data): add equipment definitions`

## TASK-MD-007 实现BOM版本与发布

**状态：** 可派遣

**目标：** 实现 Bom、BomVersion、BomItem、替代料、损耗率和 Draft/Review/Released/Expired 发布生命周期。

**输入文档：** 本文第 7.6、8～12 节；蓝图 14 第 16～18、24～25 节。

**依赖：** TASK-MD-003、TASK-MD-004。

**允许修改范围：** MasterData Boms 模块、持久化、API、事件转换和测试。

**预期输出：** 产品/工厂作用域 BOM、行与替代料、递归循环检测、发布快照、按时间解析和 `BomReleasedIntegrationEventV1`。

**验证与证据：** 覆盖空 BOM、重复行、非正用量、非法单位、自引用/间接循环、重叠区间、Released 修改、并发发布及快照可重放。

**结果回写：** 回写递归规则、替代料模型、版本状态机、快照、错误码和事件字段。

**建议提交：** `feat(master-data): add versioned bills of material`

## TASK-MD-008 实现工艺路线版本与发布

**状态：** 可派遣

**目标：** 实现 Routing、RoutingVersion、工序顺序/依赖、工作中心约束、BOM 行使用和发布生命周期。

**输入文档：** 本文第 7.7、8～12 节；蓝图 14 第 19～21、24～25 节；蓝图 15。

**依赖：** TASK-MD-007。

**允许修改范围：** MasterData Routings 模块、Contracts、持久化、API、事件转换和测试。

**预期输出：** 产品/工厂路线、工序、无环依赖、标准时间、BOM 兼容校验、发布快照、按时间解析和 `RoutingReleasedIntegrationEventV1`。

**验证与证据：** 覆盖空路线、重复 Code/Sequence、非法工作中心、环形依赖、负时间、BOM 不兼容、区间重叠和 Released 修改。

**结果回写：** 回写状态机、依赖模型、BOM 使用、WorkOrder 快照契约、错误码和事件字段。

**建议提交：** `feat(master-data): add versioned routings`

## TASK-MD-009 完成统一API、缓存、安全、审计与PC页面

**状态：** 可派遣

**目标：** 为 TASK-MD-002～008 的纵向能力补齐统一分页/解析、Redis、一致授权、审计、可观测性和全部 MasterData PC 页面。

**输入文档：** 本文第 3、9～12 节；02B 前端契约；Identity 权限/会话契约；ReferenceData 运行时契约。

**依赖：** TASK-MD-003～TASK-MD-008。

**允许修改范围：** MasterData Api/Application/Infrastructure、缓存/审计适配及测试；`src/frontend` 的 MasterData api/types/stores/pages/router、菜单、权限和对应测试。

**预期输出：** `/api/v1/master-data` 稳定 API、当前/指定时刻解析、缓存回源与失效、工厂数据权限、完整审计，以及单位、物料、组织、仓库/库位、设备、BOM、Routing 管理页面。

**验证与证据：** 覆盖分页/过滤、租户隔离、工厂权限、缓存命中/失效/降级、并发冲突、审计完整性、OpenAPI；覆盖页面权限、表单、版本发布、冲突恢复、可访问性和关键路径 E2E。

**结果回写：** 回写最终路由、DTO、权限码、缓存键、错误响应、页面路由和分页约定。

**建议提交：** `feat(master-data): deliver secured management experience`

## TASK-MD-010 发布集成事件并完成Phase 5联合验收

**状态：** 可派遣

**目标：** 完成 Outbox 到 RabbitMQ 的可靠发布、下游消费契约夹具和 MasterData 前后端全量阶段验收。

**输入文档：** 本文第 9.3、11～12、15、17 节；蓝图 08、14、14A、15、17 及后续消费者契约。

**依赖：** TASK-MD-009。

**允许修改范围：** MasterData Contracts/Infrastructure/Api、MasterData 全部测试、前端 MasterData 测试、下游契约测试夹具和本实施文档；不得实现下游业务功能。

**预期输出：** 所列 V1 事件、Outbox 原子提交/重试、消费者兼容夹具、API/页面/E2E/真实依赖验收报告和下一阶段稳定输入。

**验证与证据：** 覆盖 Outbox 原子性、重复投递、序列化、向后兼容、缓存一致性、全量 MasterData 测试、前端质量门禁、解决方案构建和真实 PostgreSQL/Redis/RabbitMQ 联调；记录退出码、数量与环境。环境缺失项只能标“待验收”。

**结果回写：** 更新全部 MD 状态、执行记录、事件版本/字段、完成证据、环境待验收项和所有设计偏差。

**建议提交：** `feat(master-data): complete phase five delivery`

---

# 15. MasterData完成标准

## 15.1 领域与数据

- 单位、分类、批次策略、物料、组织、仓储定义、设备、BOM 和 Routing 均有明确聚合、状态机、不变量和数据库约束。
- `masterdata_db` 只保存稳定主数据定义、版本、快照、审计与 Outbox；无库存事实、生产执行、遥测或其他服务权威数据。
- 只有 Released 且覆盖目标时刻的版本可被运行时解析；历史版本不被覆盖。

## 15.2 API、事件与跨服务

- API 路径、DTO、分页、并发、错误码和 OpenAPI 稳定，并通过契约测试。
- Outbox 与聚合事务原子提交；事件 V1 可重复投递、消费者可幂等处理且兼容规则明确。
- OperationalData、WorkOrder 和 IoT 等仅用 API/Contracts/事件消费，不直接读库。

## 15.3 前端与用户路径

- 七类 PC 管理入口连接真实 API，权限、工厂范围、状态、错误和冲突交互与后端一致。
- 物料、BOM 和 Routing 的创建、修订、发布与解析关键路径 E2E 通过；PDA/Mobile 不出现越界维护入口。

## 15.4 安全、审计与可观测性

- 租户、权限与 FactoryNId 数据范围在服务端强制执行，无跨租户/跨工厂泄露。
- 关键写操作有原因、操作者、前后值、版本和关联标识审计；日志脱敏，指标和健康检查可定位失败。

## 15.5 自动化与环境验收

- 领域、应用、Infrastructure、API、Contracts、前端组件和 E2E 按测试矩阵通过，解决方案可零警告构建。
- PostgreSQL、Redis、RabbitMQ 与 Gateway 的真实环境联调完成；环境不具备时相关项保持“待验收”，不得将模拟或历史结果标为已完成。
- 未实现 ERP/LIMS/PLM/WMS 连接器、库存、执行和遥测等暂缓能力。

---

# 16. 执行记录

> 本表仅记录本方案任务。02A、02B 和 `CLAUDE.md` 中的历史构建/测试数字不作为本轮验证结果。任务完成时必须填写实际执行者/任务、提交、日期化证据与设计回写。

| 任务 | 状态 | 执行者/任务 | 提交 | 验证证据 | 结果回写 |
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

---

# 17. 下一阶段输入契约

后续服务可以稳定依赖：

```text
稳定标识：MaterialNId、UnitNId、FactoryNId、WarehouseNId、LocationNId、EquipmentNId、BomNId、RoutingNId
版本标识：MaterialVersionNId、BomVersionNId、RoutingVersionNId、VersionNo、snapshotVersion
解析语义：tenantId + business scope + asOf，仅返回唯一 Released 版本
API：/api/v1/master-data 下的读取与 resolve 契约
事件：MaterialReleased、BomReleased、RoutingReleased、WarehouseChanged、EquipmentCreated/Changed V1
权限：masterdata.* 权限码与 FactoryNId 数据范围
一致性：双并发版本、Outbox 至少一次投递、消费者 eventId 幂等
已知限制：不提供库存事实、生产执行、遥测、外部系统连接器或跨库外键
```

OperationalData 必须自行设计批次实例、库存余额/预留/流水和仓储单据；WorkOrder 必须自行设计订单、执行状态与主数据快照保存；IoT 必须自行设计测点、遥测和报警。它们不能从 MasterData 的静态定义推断或反向写入这些业务事实。

如果下游需要当前事件未包含的字段，应先评估同步读取、快照或新事件版本，禁止直接读取 `masterdata_db`。

---

# 18. 文档自审清单

- [x] 引用文件真实存在。
- [x] 当前代码/环境状态与 `CLAUDE.md` 一致，MasterData 未被描述为已实现。
- [x] 无未决标记、待办标记或模糊处理表达。
- [x] 职责边界、权威归属和前后端纵向交付链明确。
- [x] 实体稳定业务标识使用 `NId`；`Code` 未作为跨服务身份。
- [x] 字段表未逐表重复生命周期字段，完整持久化设计应用统一生命周期约束。
- [x] 同库父子表复合外键、软删除同步和双重查询过滤明确；跨服务无数据库外键。
- [x] API、事件、类型、页面路由、权限和错误码前后一致。
- [x] 原文十项需求均有任务、验证和结果回写，未因套用母版丢失。
- [x] 每个任务具备且只使用统一九字段。
- [x] 任务依赖、任务标题与执行记录编号一一对应。
- [x] 历史证据与本轮轻量文档验证严格区分。
- [x] `git diff --check` 通过。
