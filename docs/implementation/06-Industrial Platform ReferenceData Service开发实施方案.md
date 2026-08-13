# 06-Industrial Platform ReferenceData Service开发实施方案

# Industrial Platform ReferenceData Service开发实施方案

> 当前里程碑范围：在现有四层服务骨架和健康检查基础上，完成字典、参数配置应用域（单值/多值）、EAV 动态配置集、元数据定义、编码规则、缓存与变更事件，并同步交付对应 PC 管理页面、契约测试和关键路径 E2E；物料、设备、工单等业务实体的 EAV 属性值、低代码页面运行时和规则引擎不在本阶段实现。

版本：V2.4（PF-03 详细设计复核版）

所属项目开发路线阶段：PF-03「ReferenceData」。当前代码只有服务骨架；本文是在既有 V2.3 详细设计和 14 张任务卡上完成的复核调整，不是从零设计或平行方案。经本次逐项书面确认，`TASK-RD-001～014` 可进入“待派遣”，但复核完成不等于已经派遣或允许自动开始开发；任何实际派遣、开发和状态推进仍需另行明确指令。阶段定义见 `docs/blueprint/09-Industrial Platform开发总TodoList.md`。

服务：

```text
IndustrialPlatform.ReferenceData
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

规格与蓝图依据：

- `docs/blueprint/01-Industrial Platform 总体架构设计 V1.0.md`
- `docs/blueprint/07-PostgreSQL数据库规范及分库设计.md`
- `docs/blueprint/08-RabbitMQ事件总线设计规范.md`
- `docs/blueprint/09-Industrial Platform开发总TodoList.md`
- `docs/blueprint/21-低代码配置平台设计.md`
- `docs/blueprint/23-多租户SaaS架构设计.md`
- `docs/blueprint/27-Industrial Platform API规范.md`
- `docs/blueprint/28-Industrial Platform前端工程规范.md`
- `docs/blueprint/29-Industrial Platform自动化测试体系.md`
- `docs/blueprint/30-Industrial Platform日志审计与可观测性平台设计.md`
- `docs/blueprint/31-Industrial Platform权限体系与安全架构设计.md`
- `docs/blueprint/32-Industrial Platform Service Host与内部模块边界.md`
- `docs/blueprint/33-Industrial Platform SystemData数据库编排与环境引导.md`
- `docs/implementation/05-Industrial Platform SystemData开发实施方案.md`
- `docs/implementation/02-Industrial Platform BuildingBlocks基础组件开发实施方案.md`
- `docs/implementation/02A-Industrial Platform可运行基线开发实施方案.md`
- `docs/implementation/02B-Industrial Platform统一前端第一批开发实施方案.md`
- `docs/implementation/03-Industrial Platform Identity Service开发实施方案.md`

---

# 1. 文档说明

## 1.1 文档目的

本文同时承担 ReferenceData 的开发详细设计、前后端协作契约和任务派遣唯一维护源。开发人员不应再依据 V1.0 中“创建四层项目”或“实现对应页面”等摘要描述自行推断领域边界、API、状态机和验收条件。

目标读者包括后端、统一前端、测试、集成和任务验收人员。任何实现偏差必须先回写本文，再继续开发或验收。

## 1.2 当前输入状态

截至本次文档设计核对：

- 后端已存在 Api、Application、Domain、Infrastructure 四个项目，并已加入解决方案。
- Api 已提供 `/health`、`/health/live`、`/health/ready`，Infrastructure 已注册 PostgreSQL、Redis 和 RabbitMQ 基础能力。
- 现有单一测试项目覆盖程序集边界、开发配置绑定和健康检查；本次只核对源文件，不把这些测试表述为本轮重新执行的证据。
- 尚无 ReferenceData 业务聚合、数据库迁移、业务 API、Contracts 项目、缓存策略、Outbox、权限和管理页面。
- 统一前端当前仅有基础 Vue 工程文件，ReferenceData API、路由、Store 和页面均不存在。
- V1.0 同时写有“仅创建项目骨架”和“完成业务能力”的冲突表述；V2.4 以 PF-03 五模块纵向交付和当前真实骨架为准，骨架不再作为待创建目标。

## 1.3 执行前置

```text
BuildingBlocks 与可运行基线
    ↓
Identity 可信身份、TenantNId/UserNId 与权限判定契约就绪
    ↓
PF-02 TASK-SD-001～004 数据库编排链验收
    ↓
PF-01 统一前端与真实 Gateway 基线可消费
    ↓
TASK-RD-001～TASK-RD-014
    ↓
MasterData 等下游按公开契约消费
```

前置环境缺失不影响本文复核结论，但会阻止对应开发任务从“待派遣”进入开发或验收。不得再以旧 Identity 任务编号替代权威契约门禁。

---

# 2. 服务定位、目标与职责边界

## 2.1 ReferenceData负责

- 字典定义、字典项、发布版本和租户覆盖。
- 非敏感参数配置应用域及其单值键、多值键的类型约束、作用域覆盖、有效值解析和变更历史。
- 由 ReferenceData 自身拥有的多行、异构字段动态配置集，包括字段定义、配置记录、强类型字段值和整份修订发布。
- 动态实体的元数据定义、属性约束、发布版本和读取契约。
- 编码模板、序列分区、预览、幂等生成和规则版本。
- 上述能力的 PC 管理页面、权限、审计、缓存、变更事件和消费契约。

ReferenceData 是定义和规则的权威来源，不是所有“基础数据”的统称。

正式宿主固定为现有 `ReferenceData.Service`，不新建核心 Service Host。宿主内固定划分五个内部模块：`Dictionary`、`Parameter`、`Metadata`、`DynamicProperty`、`CodingRule`。五模块必须独立建模、独立 Schema 或等价表前缀、独立公开契约、权限、迁移账本、Outbox/Inbox、缓存命名空间和测试；禁止跨模块直读 Repository/数据表或建立数据库外键，模块间只通过公开契约协作，为未来物理拆分保留边界。

## 2.2 ReferenceData不负责

| 不属于本服务的内容 | 权威归属 | 本服务允许保存的引用 |
| --- | --- | --- |
| 用户、角色、登录、SSO | Identity | `TenantNId`、`UserNId` 和权限上下文 |
| 物料、单位、设备、组织、工厂、仓库、库位、BOM、工艺路线 | MasterData | 对应实体 NId 或不透明外部标识，不建立跨库外键 |
| 库存批次、余额、预留、收发退、调拨、盘点单据 | OperationalData | CodingRuleNId 和已生成编码 |
| 工单、称量、追溯、批记录业务事实 | 对应业务服务 | DictionaryNId、ConfigurationKeyNId、元数据版本和编码结果 |
| 密码、连接串、API Key、证书和私钥 | 受信 Secret Provider | Parameter 只允许保存 `SecretRef`、版本标识和脱敏状态，不保存或解析秘密本体 |
| 物料、设备、工单等实体的动态属性实际值 | 拥有该实体的业务服务 | 元数据定义和 `schemaRevision` |
| 低代码页面生成、任意脚本和规则执行 | 后续低代码平台 | 稳定的元数据读取 API |

正例：ReferenceData 定义 `Equipment` 可扩展属性 `Voltage` 为 Decimal，并发布 schema revision 3。

反例：ReferenceData 不保存设备 `MIX-001` 的 `Voltage=380`；该值由 MasterData 保存并记录使用的 schema revision。

## 2.3 本阶段取舍

本阶段采用“核心 ReferenceData”边界：完整交付字典、参数配置应用域（单值/多值）、ReferenceData 自有动态配置集、元数据定义和编码规则。动态配置集允许保存配置记录和值，但只用于规则矩阵、映射表、阈值表等配置数据；不创建 `ref_entity_attribute_value`，不保存业务实体实例，不实现表单页面 JSON、动态列表运行时或通用规则引擎。

`DynamicProperty` 是正式的大模块边界，覆盖动态属性及 EAV 能力；现有完整 `DynamicConfigDefinition` 并非临时模型或待机械改名对象，而是该模块当前的核心聚合根。其 `DynamicConfigFieldDefinition`、`DynamicConfigRecord`、`DynamicConfigFieldValue` 领域命名和版本化四表语义继续保留；模块级 Schema、API 前缀、权限、契约命名空间、迁移和测试统一归属 `DynamicProperty`。`Metadata` 负责实体 Schema/属性结构和兼容性差异，`DynamicProperty` 负责动态字段、记录和值；两者只通过 `EntitySchemaNId/schemaRevision` 等公开契约引用。

三类容易混淆的能力按以下规则选择：

| 场景 | 使用模型 | 示例 |
| --- | --- | --- |
| 一个应用域下管理单值/多值键 | ConfigurationAppDomain / ConfigurationKey | `Weighting.RequireDoubleCheck=true`、`Trace.AllowedSources=[MES,WMS]` |
| 一个配置主题包含多行、不同类型字段 | DynamicConfigDefinition EAV | 称量容差矩阵、报警阈值表、接口字段映射 |
| 描述业务实体允许有哪些扩展属性 | EntitySchema | Equipment 可扩展 `Voltage`、`Protocol` |

---

# 3. 前后端及跨服务协作目标

纵向交付链固定为：

```text
领域聚合与作用域规则
    ↓
应用命令、查询与事务
    ↓
版本化 API、DTO 和集成事件
    ↓
ReferenceData PC 管理页面
    ↓
MasterData 等服务的运行时读取契约
    ↓
契约测试、页面组件测试与关键路径 E2E
    ↓
PF-03 验收
```

统一前端只通过 Gateway 调用业务 API。MasterData 和后续服务只通过 ReferenceData.Contracts、同步 API 或版本化事件消费能力，禁止引用 ReferenceData.Domain/Infrastructure 或读取 `referencedata_db`。

PF-03 的用户界面仅面向 PC 管理端；PDA、Mobile 和独立看板不需要 ReferenceData 管理入口，但可以间接消费已发布结果。

---

# 4. 总体架构与数据流

```text
PC Browser
  │ Bearer Token / tenant_id / permission
  ▼
API Gateway  /referencedata/api/v1/reference-data/**
  │ 去除 /referencedata 前缀
  ▼
ReferenceData.Api  /api/v1/reference-data/**
  │
  ├── Application：命令、查询、权限、校验、事务编排
  │       │
  │       ├── Domain：字典、参数配置应用域（单值/多值）、动态配置 EAV、元数据、编码规则
  │       └── Contracts：DTO、错误码、集成事件 V1
  │
  └── Infrastructure
          ├── referencedata_db：权威数据、迁移、Outbox、审计
          ├── Redis：已发布读取模型缓存
          └── RabbitMQ：发布变更事件

MasterData / OperationalData / 业务服务
  ├── 同步读取有效字典、参数配置应用域、动态配置和元数据
  ├── 同步幂等生成编码
  └── 异步消费发布/变更事件并失效本地缓存
```

数据权威与事务规则：

- PostgreSQL 是唯一权威来源；Redis 不保存不可恢复状态。
- 字典、动态配置、元数据和编码规则的发布，与对应 Outbox 消息写入同一数据库事务。
- 配置写入、历史记录和 Outbox 消息在同一事务提交。
- 编码生成的序列推进、幂等记录和返回编码在同一事务提交；Redis 锁不得作为唯一正确性保证。
- 同步 API 返回当前权威结果；异步事件用于缓存失效和下游投影，不替代首次读取与对账。

---

# 5. 项目结构与引用关系

当前后端结构：

```text
src/backend/src/Services/ReferenceData
├── IndustrialPlatform.ReferenceData.Api
├── IndustrialPlatform.ReferenceData.Application
├── IndustrialPlatform.ReferenceData.Domain
└── IndustrialPlatform.ReferenceData.Infrastructure

tests/ReferenceData
└── IndustrialPlatform.ReferenceData.Tests
```

目标后端结构：

```text
src/backend/src/Services/ReferenceData
├── IndustrialPlatform.ReferenceData.Api
├── IndustrialPlatform.ReferenceData.Application
├── IndustrialPlatform.ReferenceData.Contracts
├── IndustrialPlatform.ReferenceData.Domain
└── IndustrialPlatform.ReferenceData.Infrastructure

tests/ReferenceData
├── IndustrialPlatform.ReferenceData.Domain.Tests
├── IndustrialPlatform.ReferenceData.Application.Tests
├── IndustrialPlatform.ReferenceData.Infrastructure.Tests
├── IndustrialPlatform.ReferenceData.Api.Tests
└── IndustrialPlatform.ReferenceData.Contract.Tests
```

前端目标结构：

```text
src/frontend/src
├── api/referenceData
├── pages/pc/referenceData
│   ├── dictionaries
│   ├── configurations
│   ├── dynamicProperties
│   ├── metadata
│   └── codingRules
├── router/referenceDataRoutes.ts
├── stores/referenceData
└── types/referenceData
```

引用方向：

```text
Api → Application → Domain
Api → Contracts
Application → Contracts
Infrastructure → Application + Domain
Contracts 不引用 Domain、Infrastructure 或 Api
```

Domain 只引用 SharedKernel；Application 可引用 Application.Abstractions；Infrastructure 复用 BuildingBlocks 的 Infrastructure、EventBus 和 Logging；Api 复用 Web 与 Security。禁止新增业务服务之间的项目引用。

---

# 6. 全局技术与实施约束

- 聚合和内部实体标识使用 `Guid`；租户与用户主体统一使用可信身份上下文的 `TenantNId`、`UserNId`。当前 BuildingBlocks/Identity 的旧租户属性命名偏移只能在宿主组合根临时适配，不得进入五模块公开契约、领域模型或持久化模型；平台级记录允许 `tenant_nid` 为空。
- 所有业务时间使用 `DateTimeOffset`，PostgreSQL 使用 `timestamptz` 并以 UTC 保存。
- 聚合继承 BuildingBlocks 当前 Entity 生命周期、软删除和 `OptimisticVersion + ConcurrencyVersion` 双版本并发模型。
- `referencedata_db` 是稳定的 `LogicalDatabaseName`，物理目标由 SystemData 的可信 `DatabaseTopology` 配置解析，业务 API 和 manifest 不接受地址、路径或凭据。Development 默认 `Shared`、可显式 `PerService`；Test/Staging/Production 只允许 `PerService`。
- Shared 只共享物理数据库，不合并服务或模块的数据所有权。PostgreSQL 中五模块分别使用 `reference_dictionary`、`reference_parameter`、`reference_metadata`、`reference_dynamic_property`、`reference_coding_rule` Schema；SQLite 等不支持 Schema 的 Provider 使用等价模块前缀。每模块拥有独立 migration ledger、Repository、Outbox/Inbox 和最小权限。
- 领域实体自身的稳定业务标识统一命名为 `NId`，禁止以 `Code` 表示实体业务标识；其他实体引用该业务标识时使用 `{EntityName}NId`，例如 `Material.NId` 与业务表中的 `MaterialNId`。
- `Code` 只保留给“已经生成的业务编码值”等确实表达编码结果、而非实体身份的字段；NId 保存规范化比较值，展示名称保留原始大小写，同一作用域内大小写不敏感唯一。
- 写请求不能从 Body 指定当前租户；`TenantNId/UserNId` 必须来自已验证 JWT/可信服务身份。平台级写入使用独立高权限声明，不与普通租户管理权限复用。
- 统一返回 `ApiResult<T>` / `PageResult<T>`；时间使用含 `Z` 或偏移量的 ISO 8601 字符串。
- 所有写接口执行模块独立权限校验和乐观并发；编码生成额外要求 `Idempotency-Key`。
- Parameter 可保存普通非敏感值；密码、Token、私钥、连接串或客户 IdP Secret 只保存受信 Secret Provider 的 `SecretRef`、版本标识和脱敏状态，普通查询不得解析或返回秘密本体。
- 每项任务执行 TDD，并记录命令、退出码、通过/失败/跳过数量、报告路径和外部环境限制。
- 状态流转统一为 `设计待确认 → 待派遣 → 已派遣 → 开发中 → 待验收 → 已完成`。本次只完成详细设计复核并获准将任务卡置为“待派遣”；未经后续明确指令不得进入“已派遣”。

所有持久化实体必须直接继承 02 第 8 节定义的 BuildingBlocks Entity，公共字段和 PostgreSQL 列名固定为：

```text
Id                  → id uuid
IsFrozen            → is_frozen boolean
IsLocked            → is_locked boolean
IsDeleted           → is_deleted boolean
EntityType          → entity_type varchar
CreatedOn           → created_on timestamptz
LastUpdatedOn       → last_updated_on timestamptz
OptimisticVersion   → optimistic_version bigint
ConcurrencyVersion  → concurrency_version uuid
```

禁止业务实体重复声明或使用其他创建/更新时间命名。领域专用时间使用 `PublishedOn`、`GeneratedOn` 等 `On` 后缀；集成事件沿用 BuildingBlocks EventBus 的 `CreatedTime`，不把事件字段误写成实体生命周期字段。

业务实体和数据表的“字段定义”“主要字段”只列当前对象的业务字段，不逐表重复上述九个 Entity 生命周期字段；`Id`、软删除、双版本和创建/更新时间均由本节全局契约自动补齐。本服务的业务表、Outbox、审计、历史、幂等和序列表均必须具备该公共生命周期，不为技术记录表建立另一套同义字段。

同库父子表统一使用“主表 Id + 主表 IsDeleted”复合外键。子表必须增加独立的 `{ParentEntity}_Id`、`{ParentEntity}_IsDeleted` 两列，引用主表 `(Id, IsDeleted)`；子表自己的 `IsDeleted` 仍属于子表生命周期，不得复用为主表软删除标记。例如：

```text
MaterialProperty(Material_Id, Material_IsDeleted)
    → Material(Id, IsDeleted)

PostgreSQL:
material_property(material_id, material_is_deleted)
    → material(id, is_deleted)
```

主表必须为 `(Id, IsDeleted)` 建立可引用唯一键。主表软删除/恢复时，外键的 `IsDeleted` 部分使用 `ON UPDATE CASCADE` 同步到子表影子列；仓储查询子表时同时过滤子表自身 `is_deleted=false` 和父表影子列 `{parent}_is_deleted=false`。跨服务、跨数据库只保存 `{EntityName}NId`，不建立数据库外键。

---

# 7. 公共作用域、版本与发布模型

## 7.1 作用域

`ReferenceScopeType` 固定为：

```text
Platform
Tenant
Factory
```

约束：

- `Platform`：`TenantNId` 和 `ScopeNId` 均为空，仅独立的平台级权限可维护。
- `Tenant`：`TenantNId` 来自可信身份上下文，`ScopeNId` 为空；当前即使单租户也必须完整可用并携带该身份。
- `Factory`：`TenantNId` 来自可信身份上下文，`ScopeNId` 为工厂不透明标识。
- 有效值解析顺序为 `Factory → Tenant → Platform`，命中第一条已发布/启用记录即停止。
- PF-03 完整交付 Platform 和 Tenant。Factory 只保留契约扩展点，在 MasterData 提供权威工厂归属校验前，创建、更新和解析均保持禁用并返回 `REF-SCOPE-FACTORY-NOT-READY`；不得以旧 Phase 编号代替该依赖门禁。

## 7.2 发布型聚合

Dictionary、DynamicProperty、Metadata 和 CodingRule 统一使用：

```text
Draft → Published → Superseded
  │          └────→ Disabled
  └───────────────→ Disabled
```

- 只有 Draft 可编辑。
- 发布后内容不可原位覆盖；修改必须从已发布版本克隆新 Draft。
- 同一作用域、同一 NId 最多一个 Published 版本。
- 发布新版本时，旧 Published 在同一事务变为 Superseded。
- Disabled 不参与运行时解析；历史版本保留用于审计和回放。

## 7.3 配置型聚合

配置采用当前值加不可变历史记录：

```text
Active ↔ Disabled
```

每次修改都增加 `Revision`、记录前后值并发布事件，不使用 Draft。调用方需要审批流程时，应在调用方工作流完成后再提交配置变更，ReferenceData 不内置通用审批引擎。

---

# 8. 字典中心详细设计

## 8.1 DictionaryDefinition聚合

核心字段：

```text
TenantNId: string?
ScopeType: ReferenceScopeType
ScopeId: string?
NId: string
Name: string
Description: string?
Revision: int
Status: Draft | Published | Superseded | Disabled
Items: DictionaryItem[]
PublishedOn: DateTimeOffset?
```

DictionaryItem 字段：

```text
DictionaryDefinition_Id: Guid
DictionaryDefinition_IsDeleted: bool
NId: string
Name: string
Description: string?
Sort: int
Enabled: bool
```

不变量：

- DictionaryDefinition.NId 使用 `^[A-Za-z][A-Za-z0-9_.-]{1,63}$`，发布后不可变。
- 同一字典版本内 DictionaryItem.NId 大小写不敏感唯一。
- DictionaryItem.NId 使用 `^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$`；Name 必填，Sort 为非负整数。
- 禁用项仍保留，运行时默认只返回 Enabled 项；管理查询可包含禁用项。
- 发布前至少包含一个启用项。
- 已被消费的 DictionaryItem.NId 不支持重命名，只能禁用并新增 NId。
- DictionaryItem 使用 `(DictionaryDefinition_Id, DictionaryDefinition_IsDeleted)` 复合外键引用 DictionaryDefinition `(Id, IsDeleted)`。

## 8.2 解析语义

租户覆盖以“整本字典替换”为单位，不做平台项与租户项的隐式合并。若租户发布同名字典，运行时返回完整租户版本；否则回退平台版本。响应必须包含 `sourceScope`、`revision` 和 `publishedOn`，便于消费者缓存和审计。

---

# 9. 参数配置中心详细设计

## 9.1 聚合关系与配置路径

```text
┌──────────────────────────────────────────┐
│ ConfigurationAppDomain                  │
│ 配置应用域聚合根                         │
│ NId、Name、Description、Scope、Revision │
└─────────────────────┬────────────────────┘
                      │ 1:N
                      ▼
┌──────────────────────────────────────────┐
│ ConfigurationKey                        │
│ 配置键定义                               │
│ ConfigurationAppDomain_Id、             │
│ ConfigurationAppDomain_IsDeleted、NId、 │
│ DataType、ValueMode、Value、             │
│ DefaultValue、IsMandatory、IsReadOnly   │
└─────────────────────┬────────────────────┘
                      │ 1:N，仅Multi
                      ▼
┌──────────────────────────────────────────┐
│ ConfigurationKeyMultiValue              │
│ 多值键明细                               │
│ ConfigurationKey_Id、                   │
│ ConfigurationKey_IsDeleted、NId、       │
│ Value、Sort、IsDefault、Enabled         │
└──────────────────────────────────────────┘
```

应用域用于把同一子系统或功能的一组配置键组织在一起。外部稳定路径为：

```text
{AppDomainNId}.{KeyNId}

Weighting.RequireDoubleCheck
Trace.AllowedSources
```

AppDomainNId 和 KeyNId 均不允许包含点号，避免完整路径无法无歧义拆分。Parameter Configuration 与第 10 节动态配置的区别是：这里每个 Key 的结构固定，只在单值/多值间选择；需要多列、多行异构记录时使用动态配置 EAV。

## 9.2 ConfigurationAppDomain聚合根

以下只列业务字段；所有生命周期字段统一继承第 6 节和 BuildingBlocks Entity，不在业务模型中另造时间/状态字段：

```text
TenantNId: string?
ScopeType: Platform | Tenant | Factory
ScopeId: string?
NId: string
Name: string
Description: string?
Status: Active | Disabled
Revision: long
Keys: ConfigurationKey[]
```

不变量：

- NId 使用 `^[A-Za-z][A-Za-z0-9_-]{1,63}$`，同一作用域大小写不敏感唯一。
- 同一 AppDomain 下 Key.NId 大小写不敏感唯一；第一阶段最多 500 个 Key。
- Key/MultiValue 的每次实际变化都通过聚合根行为执行，同时推进 AppDomain.Revision 和 Entity 双版本字段。
- Disabled AppDomain 不参与当前作用域解析，但允许继续回退到下级作用域。
- Factory 作用域仍遵循第 7.1 节门禁，在 MasterData 提供工厂权威校验前禁止创建和修改。

## 9.3 ConfigurationKey实体

```text
ConfigurationAppDomain_Id: Guid
ConfigurationAppDomain_IsDeleted: bool
NId: string
Name: string
Description: string?
DataType: String | Integer | Decimal | Boolean | Date | DateTime | Enum | Json | Reference
ValueMode: Single | Multi
Value: ConfigurationScalar?
DefaultValue: ConfigurationScalar?
IsMandatory: bool
IsReadOnly: bool
DictionaryNId: string?
ReferenceTarget: string?
Status: Active | Disabled
Sort: int
MultiValues: ConfigurationKeyMultiValue[]
```

设计约束：

- ValueMode 是唯一持久化来源；`isMultiValue` 只可作为 DTO 派生字段，禁止与 ValueMode 同时落库形成双真相。
- Single：允许 Value/DefaultValue，禁止 MultiValue 行。
- Multi：Value 和 DefaultValue 必须为空，实际值全部来自 MultiValue 行。
- IsMandatory：Single 必须能解析出 Value 或 DefaultValue；Multi 必须至少有一条 Enabled 明细。
- IsReadOnly：管理 API 禁止修改 NId、类型、模式、值、默认值和明细；只允许受信任的启动同步/迁移流程更新，并完整审计。页面以只读方式展示。
- Enum 必须提供已发布 DictionaryNId；Reference 必须提供 ReferenceTarget；其他类型禁止填写对应引用字段。
- NId 使用与 AppDomain 相同规则。已被消费的 NId 不支持原位改名，只能禁用旧 Key 并新增 Key。
- DataType 或 ValueMode 已产生实际值后禁止原位切换；需要变更时新增 Key，避免消费者在同一路径下遇到类型突变。

## 9.4 ConfigurationScalar值对象

ConfigurationScalar 统一校验 Single Value、DefaultValue 和 MultiValue.Value：

```text
DataType
CanonicalValue
```

持久化使用 PostgreSQL `jsonb` 保存规范化值，原因是配置键按完整路径读取，不执行数据库数值计算；类型仍由 DataType 决定并在写入时强校验：

- String 最大 4 KiB。
- Integer 使用 Int64。
- Decimal 使用 `numeric(28,10)` 可无损表达的十进制值，禁止二进制浮点输入。
- Boolean 只接受 JSON `true/false`。
- Date 使用 `yyyy-MM-dd`；DateTime 使用含偏移量 ISO 8601 并规范化为 UTC。
- Enum 保存 DictionaryItem.NId；Reference 保存不超过 128 字符的不透明标识。
- Json 最大 64 KiB，禁止脚本、SQL、表达式和可执行模板。
- SQL NULL 表示“未配置”；JSON `null` 不作为有效配置值，空字符串只对 String 有效。

## 9.5 ConfigurationKeyMultiValue实体

```text
ConfigurationKey_Id: Guid
ConfigurationKey_IsDeleted: bool
NId: string
Name: string?
Value: ConfigurationScalar
Sort: int
IsDefault: bool
Enabled: bool
```

约束：

- 只允许挂在 ValueMode=Multi 的 Key 下。
- 同一 Key 下 NId 大小写不敏感唯一；NId 是明细稳定标识，Value 变化不改变引用身份。
- 同一 Key 下启用明细的规范化 Value 不得重复；持久化 `canonical_value_hash` 并建立 ConfigurationKey_Id+ValueHash 部分唯一索引。
- IsDefault 可有多条，表达多选默认集合；默认项必须同时 Enabled。
- 第一阶段每个 Multi Key 最多 1,000 条明细，读取按 Sort、NId 稳定排序。
- MultiValue 变更使用 AppDomain 聚合双版本令牌，不提供绕过聚合根的独立更新入口。
- ConfigurationKey 使用 `(ConfigurationAppDomain_Id, ConfigurationAppDomain_IsDeleted)` 引用 AppDomain `(Id, IsDeleted)`；MultiValue 使用 `(ConfigurationKey_Id, ConfigurationKey_IsDeleted)` 引用 Key `(Id, IsDeleted)`。

## 9.6 有效配置解析

解析顺序按完整路径逐 Key 执行：

```text
Factory AppDomain/Key
    ↓ 未找到、Domain禁用或Key禁用
Tenant AppDomain/Key
    ↓ 未找到、Domain禁用或Key禁用
Platform AppDomain/Key
```

命中 Active Key 后停止继续回退：

- Single：Value 非空返回 Value；否则返回 DefaultValue；两者都为空且 Mandatory 时返回 `REF-CONFIG-MANDATORY-VALUE-MISSING`，可选时返回显式 null。
- Multi：返回全部 Enabled 明细；空集合且 Mandatory 时返回同一必填错误，可选时返回显式空数组。
- 显式 null/空数组会阻断回退，避免租户“清空”后意外继承平台值。
- IsReadOnly 只影响写入，不影响解析。

`ResolveConfiguration(appDomainNId, keyNId, factoryId?)` 响应包含完整路径、DataType、ValueMode、值、默认信息、命中作用域、AppDomain Revision 和 LastUpdatedOn。消费者不得从 JSON 文本猜测类型。

## 9.7 变更历史与边界

每次 AppDomain、Key 或 MultiValue 变更都追加历史记录，包含对象类型/ID、完整路径、变更原因、前后值摘要、AppDomain Revision、用户、TraceId 和 CreatedOn。历史只追加，不直接更新。

不提供 Secret 类型。疑似密码、Token、私钥、连接串或客户 IdP Secret 的 NId/Value 必须拒绝并记录不含原值的安全审计。

---

# 10. EAV（实体-属性-值）动态配置模型设计

## 10.1 适用范围与模型选择

动态配置 EAV 用于“配置结构本身可变、每个配置主题包含多行记录、字段类型彼此不同”的场景，例如：

```text
称量容差矩阵
  物料类型(Enum) + 最小重量(Decimal) + 最大重量(Decimal) + 双人复核(Boolean)

设备报警阈值
  设备类型(Enum) + 指标编码(String) + 警告值(Decimal) + 停机值(Decimal)

外部接口字段映射
  外部字段(String) + 内部字段(String) + 必填(Boolean) + 默认值(Json)
```

不适用场景：

- 单值或多值 Key 使用第 9 节 ConfigurationAppDomain/ConfigurationKey。
- 固定且高频查询的核心业务数据使用对应业务服务的正常关系模型。
- 物料、设备、工单等业务实例的扩展属性值保存在拥有该实体的服务。
- 需要脚本、公式、审批或页面生成时交给后续规则/低代码平台，不在 EAV 值中嵌入可执行内容。

## 10.2 四表版本化模型

```text
┌──────────────────────────────────────────┐
│ ref_dynamic_config_definition            │
│ NId、Revision、Scope、Status、Name       │
└─────────────────────┬────────────────────┘
                      │ 1:N（同一修订）
          ┌───────────┴────────────┐
          ▼                        ▼
┌────────────────────────┐  ┌────────────────────────┐
│ ref_dynamic_config_    │  │ ref_dynamic_config_    │
│ field                  │  │ record                 │
│ Definition_Id、        │  │ Definition_Id、        │
│ Definition_IsDeleted、 │  │ Definition_IsDeleted、 │
│ NId、DataType          │  │ NId、Sort、Enabled     │
└───────────┬────────────┘  └───────────┬────────────┘
            └──────────────┬────────────┘
                           ▼
              ┌────────────────────────────┐
              │ ref_dynamic_config_value   │
              │ Record/Field各自_Id、       │
              │ _IsDeleted、对应类型值列   │
              └────────────────────────────┘
```

用户示例中的四层关系被保留，但 `config_definition` 不是可被原地覆盖的单行：每个 Definition 行代表同一 `NId` 的一个 Revision。字段、记录和值必须全部引用同一个 Definition 修订，数据库约束和应用校验都禁止跨修订混用。

## 10.3 DynamicConfigDefinition聚合

```text
TenantNId: string?
ScopeType: Platform | Tenant
ScopeId: string?
NId: string                      // 跨修订稳定业务标识
Name: string
Description: string?
Revision: int
Status: Draft | Published | Superseded | Disabled
Fields: DynamicConfigFieldDefinition[]
Records: DynamicConfigRecord[]
PublishedOn: DateTimeOffset?
```

不变量：

- NId 使用第 8 节 NId 规则，同一作用域内大小写不敏感唯一。
- 同一 NId 同时最多一个 Draft、一个 Published；发布新 Revision 时旧 Published 变为 Superseded。
- 只有 Draft 可修改字段、记录和值；Published 只读。
- Clone 必须复制字段、记录和值形成完整快照，不允许新 Revision 引用旧 Revision 的子项。
- 发布前至少有一个 Enabled 字段和一个 Enabled 记录；整份数据通过校验后原子发布。
- Platform/Tenant 采用整份配置集替换，不进行隐式行级合并；Factory 作用域遵循第 7.1 节受限策略。

## 10.4 DynamicConfigFieldDefinition

```text
DynamicConfigDefinition_Id: Guid
DynamicConfigDefinition_IsDeleted: bool
NId: string
Name: string
DataType: String | Integer | Decimal | Boolean | Date | DateTime | Enum | Json | Reference
Required: bool
Enabled: bool
Sort: int
DefaultValue: typed value?
MinLength / MaxLength: int?
MinValue / MaxValue: decimal?
Scale: int?
Pattern: string?
DictionaryNId: string?
ReferenceTarget: string?
Description: string?
```

字段约束：

- 同一 Definition 修订内 NId 唯一；NId 发布后不能原位改名。
- Decimal 的 Scale 范围为 0～10，数据库精度固定为 `numeric(28,10)`。
- Enum 必须引用已发布字典；Reference 必须声明 ReferenceTarget，但不建立跨服务外键。
- Json 只允许合法 JSON 数据，单值最大 64 KiB；禁止脚本、SQL、表达式和可执行模板。
- DefaultValue 在字段表以规范化 `default_value_json` 保存，仅承载一个已按 DataType 校验的默认标量/Json；创建 Draft 记录时复制到对应强类型 Value 列，之后不随默认值变更而隐式改写。
- Pattern 最大 256 字符并采用安全正则策略。
- 已发布字段的删除通过新 Revision 中 `Enabled=false` 表达，保留历史可解释性。
- 第一阶段每个 Definition 最多 100 个字段，超过限制返回稳定错误，不允许无界扩张。
- Field 使用 `(DynamicConfigDefinition_Id, DynamicConfigDefinition_IsDeleted)` 复合外键引用 Definition `(Id, IsDeleted)`。

## 10.5 DynamicConfigRecord

```text
DynamicConfigDefinition_Id: Guid
DynamicConfigDefinition_IsDeleted: bool
NId: string
Name: string?
Category: string?
Sort: int
Enabled: bool
Values: DynamicConfigFieldValue[]
```

NId 是配置行的稳定业务标识，同一 Definition 修订内大小写不敏感唯一。Category 只用于分组和筛选，不决定字段结构；若不同类别需要完全不同的字段集合，应拆成不同 DynamicConfigDefinition，避免形成无法验证的稀疏万能表。

Record 使用 `(DynamicConfigDefinition_Id, DynamicConfigDefinition_IsDeleted)` 复合外键引用 Definition `(Id, IsDeleted)`。

第一阶段单个 Revision 最多 10,000 条记录。运行时 API 必须分页，禁止默认一次返回完整数据集。字段或记录的任何变更都使用 Definition 聚合的双版本令牌并推进其版本，不为子项建立绕过聚合的独立并发入口。

## 10.6 DynamicConfigFieldValue强类型存储

```text
DynamicConfigDefinition_Id: Guid
DynamicConfigDefinition_IsDeleted: bool
DynamicConfigRecord_Id: Guid
DynamicConfigRecord_IsDeleted: bool
DynamicConfigFieldDefinition_Id: Guid
DynamicConfigFieldDefinition_IsDeleted: bool
ValueType: String | Integer | Decimal | Boolean | Date | DateTime | Enum | Json | Reference
StringValue: string?
IntegerValue: long?
DecimalValue: decimal(28,10)?
BooleanValue: bool?
DateValue: DateOnly?
DateTimeValue: DateTimeOffset?
JsonValue: jsonb?
ReferenceValue: string?
```

值语义：

- `(DynamicConfigRecord_Id, DynamicConfigFieldDefinition_Id)` 唯一。
- Value 分别通过 `(DynamicConfigRecord_Id, DynamicConfigRecord_IsDeleted)` 和 `(DynamicConfigFieldDefinition_Id, DynamicConfigFieldDefinition_IsDeleted)` 引用 Record、Field 的 `(Id, IsDeleted)`；同时通过包含 `DynamicConfigDefinition_Id` 的复合唯一键/外键保证 Record 与 Field 属于同一 Definition 修订，禁止跨修订拼接。
- Check Constraint 按 ValueType 保证每行只有对应一个强类型值列非空；领域层再校验 ValueType 必须等于 Field.DataType。普通 Check Constraint 不跨表读取 Field 定义。
- 可选字段没有值时不创建 Value 行；空字符串是一个真实 StringValue，与缺失值不同。
- Required 字段对每个 Enabled Record 必须存在有效值；DefaultValue 仅在创建 Draft 记录时显式展开，不在运行时临时猜值。
- Date 使用 PostgreSQL `date`，DateTime 使用 `timestamptz`；Decimal 禁止通过字符串或浮点数保存。
- String/Enum 最大 4 KiB，ReferenceValue 最大 128 字符，JsonValue 不接受 JSON `null` 充当缺失值。
- 不提供通用“任意字段任意运算”查询。运行时支持按 Record.NId 精确读取、Category 筛选和分页，分析类查询由专用投影承担。
- 第一阶段单个 Revision 最多 200,000 条 Value 记录；与字段数、记录数上限同时校验。

## 10.7 生命周期与发布流程

```text
创建 Definition Draft
    ↓
定义字段
    ↓
录入/批量编辑配置记录和值
    ↓
完整校验
  ├── 字段约束
  ├── Required覆盖
  ├── Enum字典
  ├── 类型列一致性
  └── Record.NId唯一
    ↓
发布整个 Revision
    ↓
旧 Published → Superseded
    ↓
缓存失效 + Outbox事件
```

Definition、Fields、Records、Values、旧版状态切换和 Outbox 必须在一个 PostgreSQL 事务提交。任何一条记录失败都不得产生半发布状态。

## 10.8 读取、覆盖与示例

运行时先按 Tenant 查找 Published Definition；不存在时回退 Platform。租户一旦发布同 NId Revision，整份租户配置集替换平台集，不合并 Record.NId。Schema 响应返回 Revision；Records 和单条 Record 查询必须携带该 Revision，服务从 Published 或 Superseded 快照读取同版数据，禁止“当前 Schema + 另一 Revision Records”。

示例响应语义：

```json
{
  "nId": "WeightingToleranceMatrix",
  "revision": 3,
  "sourceScope": "Tenant",
  "fields": [
    { "nId": "MaterialType", "dataType": "Enum", "dictionaryNId": "MaterialType" },
    { "nId": "MinWeight", "dataType": "Decimal", "required": true },
    { "nId": "RequireDoubleCheck", "dataType": "Boolean", "required": true }
  ],
  "records": [
    {
      "nId": "RAW_MATERIAL",
      "values": {
        "MaterialType": "RawMaterial",
        "MinWeight": 10.0000000000,
        "RequireDoubleCheck": true
      }
    }
  ]
}
```

JSON 只是 API 投影；数据库仍使用四表和强类型列，不把整份数据集作为不可检索 JSON Blob 保存。

---

# 11. 元数据定义详细设计

## 11.1 EntitySchema聚合

核心字段：

```text
TenantNId: string?
ScopeType: Platform | Tenant
NId: string
Name: string
Description: string?
Revision: int
Status: Draft | Published | Superseded | Disabled
Attributes: AttributeDefinition[]
```

EntitySchema.NId 是跨服务稳定的 Schema 业务标识，如 `Equipment`、`Material`、`Container`。它不是实体实例 ID，也不授权 ReferenceData 读取实体数据；其他服务引用时字段命名为 `EntitySchemaNId`。

## 11.2 AttributeDefinition

```text
EntitySchema_Id: Guid
EntitySchema_IsDeleted: bool
NId: string
Name: string
DataType: String | Integer | Decimal | Boolean | Date | DateTime | Enum | Reference
Required: bool
IsArray: bool
Enabled: bool
Sort: int
DefaultValue: string?
MinLength / MaxLength: int?
MinValue / MaxValue: decimal?
Pattern: string?
DictionaryNId: string?
ReferenceTarget: string?
Description: string?
```

不变量：

- 同一 Schema 版本内 AttributeDefinition.NId 大小写不敏感唯一。
- Enum 必须填写已发布 DictionaryNId；非 Enum 禁止填写 DictionaryNId。
- Reference 必须填写 ReferenceTarget；ReferenceData 只校验格式，不查询目标服务数据库。
- Date 使用 `yyyy-MM-dd`；DateTime 使用含偏移量的 ISO 8601。
- Pattern 最大 256 字符，禁止包含可执行脚本；前后端都必须限制正则执行时间或采用安全校验策略。
- 发布时执行完整交叉校验，禁止引用不存在或未发布的字典。
- 已发布属性不能物理删除；新版本以 `Enabled=false` 表示停止接收新值，并保留历史语义。
- AttributeDefinition 使用 `(EntitySchema_Id, EntitySchema_IsDeleted)` 复合外键引用 EntitySchema `(Id, IsDeleted)`。

## 11.3 版本消费

运行时返回 `nId + revision + attributes`。业务服务保存动态属性值时必须自行保存 `entitySchemaNId + schemaRevision` 并执行值校验。ReferenceData 不保存 `EntityAttributeValue`，也不负责业务记录随 Schema 升级的迁移。

---

# 12. 编码规则详细设计

## 12.1 CodingRule聚合

```text
TenantNId: string?
ScopeType: Platform | Tenant
NId: string
Name: string
TargetEntityNId: string
Template: string
ResetPolicy: Never | Yearly | Monthly | Daily
Revision: int
Status: Draft | Published | Superseded | Disabled
```

第一阶段支持的模板 Token：

```text
{YYYY} {MM} {DD} {TENANT} {FACTORY} {SEQ:n}
```

其中 `n` 为 1～12。未知 Token、多个 `{SEQ:n}`、缺少序列 Token、未提供必需上下文或渲染后长度超过 128 均拒绝发布或生成。`{FACTORY}` 只有请求提供且通过工厂作用域校验后才能使用。

## 12.2 序列与幂等

- 序列表使用 `(CodingRule_Id, CodingRule_IsDeleted)` 复合外键引用 CodingRule `(Id, IsDeleted)`；序列分区键由 CodingRule_Id、规则 Revision、ResetPolicy 周期和上下文哈希组成。
- Preview 只校验并展示样例，不推进序列，也不创建幂等记录。
- Generate 必须携带 `Idempotency-Key`；同一租户、规则、键和相同请求重复调用返回同一编码。
- 相同 Idempotency-Key 对应不同请求摘要时返回 `REF-IDEMPOTENCY-CONFLICT`。
- 序列通过数据库原子更新/行锁保证唯一；Redis 分布式锁只可减少竞争，不作为正确性前提。
- 已生成编码不回收、不复用；规则发布新 Revision 后使用独立序列分区。
- Idempotency Record 在线保留 7 天；过期清理不影响编码审计和序列连续性。

---

# 13. 数据与持久化设计

稳定逻辑数据库（物理目标由 SystemData 的可信 `DatabaseTopology` 解析）：

```text
referencedata_db
```

核心表。下表只列业务字段；所有表的 Entity 生命周期字段由第 6 节统一补齐，不在每一行重复：

| 表 | 主要内容 | 关键约束/索引 |
| --- | --- | --- |
| `reference_dictionary.dictionary_definition/item` | 字典定义、修订、项与发布 | 模块内复合外键；作用域+规范化NId+Revision唯一 |
| `reference_parameter.configuration_app_domain/key/key_multi_value/history` | 参数应用域、键、单值/多值和领域历史 | 按 Key 整体覆盖；历史只追加；Secret 仅保存 SecretRef |
| `reference_dynamic_property.dynamic_config_definition/field/record/value` | `DynamicConfigDefinition` 完整 EAV 聚合 | 四表同 Revision；强类型值 Check Constraint；整份原子发布 |
| `reference_metadata.entity_schema/attribute_definition` | EntitySchema 完整快照与属性定义 | 属性只属于同一 Schema 修订；发布生成兼容性差异摘要 |
| `reference_coding_rule.coding_rule/sequence/idempotency_record` | 规则、原子序列与幂等响应 | `TenantNId+RuleNId+IdempotencyKey` 幂等；Preview 不占号 |
| 每模块 `outbox_message/inbox_message/consumer_checkpoint` | 模块独立事件发布、幂等消费与位点 | 不共表、不跨模块迁移；宿主仅复用组件 |
| 每模块 `schema_migrations` | 模块独立迁移账本 | 独立记录 Desired/Observed Version；不得由共享账本掩盖失败 |

上述“主要内容”有意不重复 Entity 生命周期字段。所有领域表均遵守第 6 节公共字段、父表 `(Id, IsDeleted)` 可引用唯一键及子表 `{ParentEntity}_Id + {ParentEntity}_IsDeleted` 复合外键规则。聚合根软删除通过父级影子列使全部子项不可见，子项自身的 `IsDeleted` 仍保持独立；不允许绕过聚合单独恢复子项。模块之间不得建立外键，跨模块引用只保存稳定 NId/Revision 并通过公开契约校验。

迁移规则：

- 迁移文件按任务顺序追加，禁止修改已在共享环境执行的迁移。
- PostgreSQL 大小写不敏感唯一使用规范化列或函数索引，不依赖默认排序规则。
- 平台级空租户的唯一约束使用明确的部分唯一索引，避免 `NULL` 导致重复。
- 发布切换、动态配置四表快照、配置历史、编码生成和 Outbox 均使用本地事务；禁止分布式事务。
- 本阶段创建的 EAV 值表只保存 ReferenceData 自有配置记录；不创建业务实体属性值表、低代码页面表或跨服务外键。

## 13.1 SystemData 数据库编排消费与 readiness

ReferenceData 只作为 PF-02 控制面的消费者，不重复实现数据库编排。宿主登记 `ServiceKey=referencedata`、`Provider`、稳定 `LogicalDatabaseName=referencedata_db`、Owner、DesiredState、AutoProvision/AutoMigrate，以及一个宿主级签名 Migration Bundle/Version；Bundle 内声明 Dictionary、Parameter、Metadata、DynamicProperty、CodingRule 五个独立迁移单元、账本和期望版本。物理目标完全由可信 `DatabaseTopology` 解析。

启动握手固定为：`登记/查询 → plan/OperationId → provision/migrate/drift → 五模块版本复核 → readiness`。SystemData 不可用、目标错误、drift、任一模块迁移失败或版本不一致时，整个宿主保持 `NotReady`，但 liveness 仍可 Healthy；readiness 返回五模块脱敏状态、OperationId 和 TraceId，不泄露地址、路径或凭据，也不开放部分业务流量。

Shared 物理目标只 provision 一次；同一物理目标 DDL 使用 PostgreSQL advisory lock 或等效锁串行化，迁移和 readiness 仍按模块独立报告。ReferenceData 不持有管理员凭据、不自行建库、不使用 `EnsureCreated`，也不在远程失败时回退 SQLite、旧 Schema 或错误数据库。拓扑变化不隐式 copy、rename、merge 或 split；已有数据必须报告 drift 并走显式迁移/import。SystemData 自身由 PostgreSQL 18 基础设施最小 bootstrap 的例外不属于 PF-03。

---

# 14. Application用例设计

| 能力 | Commands | Queries |
| --- | --- | --- |
| 字典 | CreateDraft、UpdateDraft、Add/Update/DisableItem、CloneRevision、Publish、Disable | GetAdminDetail、SearchAdmin、GetEffectiveDictionary |
| 参数配置 | Create/Update/Enable/DisableAppDomain、Add/Update/Enable/DisableKey、Add/Update/Enable/DisableMultiValue | SearchAppDomains、GetAppDomainDetail、GetKeyHistory、ResolveConfigurationKey、ResolveConfigurationDomain |
| 动态配置 | CreateDraft、UpdateFields、Add/Update/DisableRecord、CloneRevision、Publish、Disable | SearchAdmin、GetAdminDetail、GetEffectiveDynamicConfigSchema、GetEffectiveRecords、GetEffectiveRecord |
| 元数据 | CreateDraft、UpdateDraft、Add/Update/RemoveAttribute、CloneRevision、Publish、Disable | SearchAdmin、GetAdminDetail、GetEffectiveSchema |
| 编码规则 | CreateDraft、UpdateDraft、CloneRevision、Publish、Disable、GenerateCode | SearchAdmin、GetAdminDetail、PreviewCode、GetEffectiveRule |

所有 Command Handler 固定执行：

```text
认证与权限
  → 租户/作用域校验
  → 请求校验
  → 加载聚合及双版本检查
  → 领域行为
  → 本地事务保存 + 历史/Outbox/审计
  → 提交后缓存失效
```

查询默认排除软删除；Admin 查询可查看 Draft、Superseded 和 Disabled，运行时查询只返回 Published/Active。

---

# 15. API契约设计

## 15.1 路径与通用约定

```text
Gateway：/referencedata/api/v1/reference-data/**
服务内部：/api/v1/reference-data/**
```

列表请求统一使用 `pageIndex`、`pageSize`、`keyword`、`status`、`scopeType`；`pageSize` 范围 1～100。写请求中的 `expectedOptimisticVersion: long` 和 `expectedConcurrencyVersion: Guid` 用于并发控制。

## 15.2 管理API

| Method | 服务内部路径 | Request/Response | 权限 |
| --- | --- | --- | --- |
| GET | `/admin/dictionaries` | Query → `PageResult<DictionarySummaryDto>` | `ReferenceData.Dictionary.Read` |
| POST | `/admin/dictionaries` | `CreateDictionaryRequest` → `DictionaryDetailDto`，201 | `ReferenceData.Dictionary.Create` |
| GET | `/admin/dictionaries/{id}` | → `DictionaryDetailDto` | `ReferenceData.Dictionary.Read` |
| PUT | `/admin/dictionaries/{id}` | `UpdateDictionaryRequest` → `DictionaryDetailDto` | `ReferenceData.Dictionary.Update` |
| POST | `/admin/dictionaries/{id}/clone` | 并发版本 → 新 Draft | `ReferenceData.Dictionary.Create` |
| POST | `/admin/dictionaries/{id}/publish` | 并发版本 → Published | `ReferenceData.Dictionary.Publish` |
| POST | `/admin/dictionaries/{id}/disable` | 并发版本+原因 → Disabled | `ReferenceData.Dictionary.Disable` |
| GET | `/admin/configuration-domains` | Query → `PageResult<ConfigurationAppDomainSummaryDto>` | `ReferenceData.Parameter.Read` |
| POST | `/admin/configuration-domains` | `CreateConfigurationAppDomainRequest` → `ConfigurationAppDomainDetailDto`，201 | `ReferenceData.Parameter.Create` |
| GET | `/admin/configuration-domains/{id}` | → `ConfigurationAppDomainDetailDto` | `ReferenceData.Parameter.Read` |
| PUT | `/admin/configuration-domains/{id}` | `UpdateConfigurationAppDomainRequest` → `ConfigurationAppDomainDetailDto` | `ReferenceData.Parameter.Update` |
| POST | `/admin/configuration-domains/{id}/enable` | `ChangeConfigurationStateRequest` → Domain DTO | `ReferenceData.Parameter.Update` |
| POST | `/admin/configuration-domains/{id}/disable` | `ChangeConfigurationStateRequest` → Domain DTO | `ReferenceData.Parameter.Disable` |
| POST | `/admin/configuration-domains/{id}/keys` | `CreateConfigurationKeyRequest` → `ConfigurationKeyDto`，201 | `ReferenceData.Parameter.Create` |
| PUT | `/admin/configuration-domains/{id}/keys/{keyId}` | `UpdateConfigurationKeyRequest` → `ConfigurationKeyDto` | `ReferenceData.Parameter.Update` |
| POST | `/admin/configuration-domains/{id}/keys/{keyId}/enable` | `ChangeConfigurationStateRequest` → Key DTO | `ReferenceData.Parameter.Update` |
| POST | `/admin/configuration-domains/{id}/keys/{keyId}/disable` | `ChangeConfigurationStateRequest` → Key DTO | `ReferenceData.Parameter.Disable` |
| POST | `/admin/configuration-domains/{id}/keys/{keyId}/values` | `CreateConfigurationMultiValueRequest` → MultiValue DTO，201 | `ReferenceData.Parameter.Create` |
| PUT | `/admin/configuration-domains/{id}/keys/{keyId}/values/{valueId}` | `UpdateConfigurationMultiValueRequest` → MultiValue DTO | `ReferenceData.Parameter.Update` |
| POST | `/admin/configuration-domains/{id}/keys/{keyId}/values/{valueId}/enable` | `ChangeConfigurationStateRequest` → MultiValue DTO | `ReferenceData.Parameter.Update` |
| POST | `/admin/configuration-domains/{id}/keys/{keyId}/values/{valueId}/disable` | `ChangeConfigurationStateRequest` → MultiValue DTO | `ReferenceData.Parameter.Disable` |
| GET | `/admin/configuration-domains/{id}/keys/{keyId}/history` | → `PageResult<ConfigurationHistoryDto>` | `ReferenceData.Parameter.Read` |
| GET | `/admin/dynamic-properties/configurations` | Query → `PageResult<DynamicConfigSummaryDto>` | `ReferenceData.DynamicProperty.Read` |
| POST | `/admin/dynamic-properties/configurations` | `CreateDynamicConfigRequest` → `DynamicConfigDetailDto`，201 | `ReferenceData.DynamicProperty.Create` |
| GET | `/admin/dynamic-properties/configurations/{id}` | → `DynamicConfigDetailDto` | `ReferenceData.DynamicProperty.Read` |
| PUT | `/admin/dynamic-properties/configurations/{id}` | `UpdateDynamicConfigRequest` → `DynamicConfigDetailDto` | `ReferenceData.DynamicProperty.Update` |
| POST | `/admin/dynamic-properties/configurations/{id}/records` | `CreateDynamicConfigRecordRequest` → `DynamicConfigRecordDto`，201 | `ReferenceData.DynamicProperty.Update` |
| PUT | `/admin/dynamic-properties/configurations/{id}/records/{recordId}` | `UpdateDynamicConfigRecordRequest` → `DynamicConfigRecordDto` | `ReferenceData.DynamicProperty.Update` |
| POST | `/admin/dynamic-properties/configurations/{id}/records/{recordId}/disable` | `PublishOrDisableRequest` → `DynamicConfigRecordDto` | `ReferenceData.DynamicProperty.Disable` |
| POST | `/admin/dynamic-properties/configurations/{id}/clone` | `PublishOrDisableRequest` → 新 Draft | `ReferenceData.DynamicProperty.Create` |
| POST | `/admin/dynamic-properties/configurations/{id}/publish` | `PublishOrDisableRequest` → Published | `ReferenceData.DynamicProperty.Publish` |
| POST | `/admin/dynamic-properties/configurations/{id}/disable` | `PublishOrDisableRequest` → Disabled | `ReferenceData.DynamicProperty.Disable` |
| GET | `/admin/metadata-schemas` | Query → `PageResult<MetadataSchemaSummaryDto>` | `ReferenceData.Metadata.Read` |
| POST | `/admin/metadata-schemas` | `CreateMetadataSchemaRequest` → `MetadataSchemaDetailDto`，201 | `ReferenceData.Metadata.Create` |
| GET | `/admin/metadata-schemas/{id}` | → `MetadataSchemaDetailDto` | `ReferenceData.Metadata.Read` |
| PUT | `/admin/metadata-schemas/{id}` | `UpdateMetadataSchemaRequest` → `MetadataSchemaDetailDto` | `ReferenceData.Metadata.Update` |
| POST | `/admin/metadata-schemas/{id}/clone` | `PublishOrDisableRequest` → 新 Draft | `ReferenceData.Metadata.Create` |
| POST | `/admin/metadata-schemas/{id}/publish` | `PublishOrDisableRequest` → Published | `ReferenceData.Metadata.Publish` |
| POST | `/admin/metadata-schemas/{id}/disable` | `PublishOrDisableRequest` → Disabled | `ReferenceData.Metadata.Disable` |
| GET | `/admin/coding-rules` | Query → `PageResult<CodingRuleSummaryDto>` | `ReferenceData.CodingRule.Read` |
| POST | `/admin/coding-rules` | `CreateCodingRuleRequest` → `CodingRuleDetailDto`，201 | `ReferenceData.CodingRule.Create` |
| GET | `/admin/coding-rules/{id}` | → `CodingRuleDetailDto` | `ReferenceData.CodingRule.Read` |
| PUT | `/admin/coding-rules/{id}` | `UpdateCodingRuleRequest` → `CodingRuleDetailDto` | `ReferenceData.CodingRule.Update` |
| POST | `/admin/coding-rules/{id}/clone` | `PublishOrDisableRequest` → 新 Draft | `ReferenceData.CodingRule.Create` |
| POST | `/admin/coding-rules/{id}/publish` | `PublishOrDisableRequest` → Published | `ReferenceData.CodingRule.Publish` |
| POST | `/admin/coding-rules/{id}/disable` | `PublishOrDisableRequest` → Disabled | `ReferenceData.CodingRule.Disable` |
| POST | `/admin/coding-rules/{id}/preview` | `PreviewCodeRequest` → `CodePreviewDto` | `ReferenceData.CodingRule.Preview` |

创建/更新 DTO 必须显式包含作用域、NId、名称和领域字段，禁止接收任意 JSON 后由服务静默猜测类型。

核心写请求字段固定为：

```text
CreateDictionaryRequest
  scopeType, scopeId, nId, name, description,
  items[{nId, name, description, sort, enabled}]

UpdateDictionaryRequest
  name, description, items[],
  expectedOptimisticVersion, expectedConcurrencyVersion

CreateConfigurationAppDomainRequest / UpdateConfigurationAppDomainRequest
  scopeType, scopeId, nId, name, description, changeReason,
  Update额外包含 expectedOptimisticVersion, expectedConcurrencyVersion

CreateConfigurationKeyRequest / UpdateConfigurationKeyRequest
  nId, name, description, dataType, valueMode, value, defaultValue,
  isMandatory, isReadOnly, dictionaryNId, referenceTarget, status, sort,
  changeReason, expectedAppDomainOptimisticVersion, expectedAppDomainConcurrencyVersion

CreateConfigurationMultiValueRequest / UpdateConfigurationMultiValueRequest
  nId, name, value, sort, isDefault, enabled, changeReason,
  expectedAppDomainOptimisticVersion, expectedAppDomainConcurrencyVersion

ChangeConfigurationStateRequest
  changeReason, expectedAppDomainOptimisticVersion, expectedAppDomainConcurrencyVersion

CreateDynamicConfigRequest / UpdateDynamicConfigRequest
  scopeType, scopeId, nId, name, description,
  fields[{nId, name, dataType, required, enabled, sort, defaultValue,
          minLength, maxLength, minValue, maxValue, scale, pattern,
          dictionaryNId, referenceTarget, description}],
  Update额外包含 expectedOptimisticVersion, expectedConcurrencyVersion

CreateDynamicConfigRecordRequest / UpdateDynamicConfigRecordRequest
  nId, name, category, sort, enabled,
  values[{fieldNId, value}],
  Update额外包含 expectedOptimisticVersion, expectedConcurrencyVersion

CreateMetadataSchemaRequest / UpdateMetadataSchemaRequest
  scopeType, nId, name, description,
  attributes[{nId, name, dataType, required, isArray, enabled, sort,
              defaultValue, minLength, maxLength, minValue, maxValue,
              pattern, dictionaryNId, referenceTarget, description}],
  Update额外包含 expectedOptimisticVersion, expectedConcurrencyVersion

CreateCodingRuleRequest / UpdateCodingRuleRequest
  scopeType, nId, name, targetEntityNId, template, resetPolicy,
  Update额外包含 expectedOptimisticVersion, expectedConcurrencyVersion

PublishOrDisableRequest
  expectedOptimisticVersion, expectedConcurrencyVersion, changeReason
```

所有写响应必须返回 `id`、`status`、业务 Revision、`optimisticVersion`、`concurrencyVersion` 和 `lastUpdatedOn`；Create 返回 201，Update/Clone/Publish/Disable 返回 200。

## 15.3 运行时API

| Method | 服务内部路径 | Response | 权限/调用约束 |
| --- | --- | --- | --- |
| GET | `/dictionaries/{nId}` | `EffectiveDictionaryDto` | `ReferenceData.Dictionary.Read` |
| GET | `/configuration-domains/{appDomainNId}` | `EffectiveConfigurationDomainDto` | `ReferenceData.Parameter.Read`；最多500个Key |
| GET | `/configuration-domains/{appDomainNId}/keys/{keyNId}` | `EffectiveConfigurationDto` | `ReferenceData.Parameter.Read` |
| GET | `/dynamic-properties/configurations/{nId}/schema` | `EffectiveDynamicConfigSchemaDto` | `ReferenceData.DynamicProperty.Read` |
| GET | `/dynamic-properties/configurations/{nId}/records?revision={revision}` | `PageResult<DynamicConfigRecordDto>` | `ReferenceData.DynamicProperty.Read`；Revision 必填且必须分页 |
| GET | `/dynamic-properties/configurations/{nId}/records/{recordNId}?revision={revision}` | `DynamicConfigRecordDto` | `ReferenceData.DynamicProperty.Read`；Revision 必填 |
| GET | `/metadata-schemas/{nId}` | `EffectiveSchemaDto` | `ReferenceData.Metadata.Read` |
| POST | `/coding-rules/{nId}/preview` | `CodePreviewDto`，不推进序列 | `ReferenceData.CodingRule.Preview` |
| POST | `/coding-rules/{nId}/generate` | `GeneratedCodeDto` | `ReferenceData.CodingRule.Generate`；必须有 Idempotency-Key |

交互式请求沿用用户 JWT；后台服务身份和客户端凭据由 Identity 后续服务认证契约提供，在该契约落地前不得使用共享管理员 Token 替代。

## 15.4 核心响应字段

```text
EffectiveDictionaryDto
  nId, name, items[{nId, name, description, sort, enabled}], sourceScope, revision, publishedOn

EffectiveConfigurationDto
  appDomainNId, keyNId, fullNId, dataType, valueMode,
  value, multiValues[], sourceScope, revision, lastUpdatedOn

EffectiveConfigurationDomainDto
  appDomainNId, keys[EffectiveConfigurationDto], resolvedOn

EffectiveDynamicConfigSchemaDto
  nId, name, fields[], sourceScope, revision, publishedOn

DynamicConfigRecordDto
  nId, name, category, sort, enabled, values{fieldNId: typedValue}, revision

EffectiveSchemaDto
  nId, name, attributes[], sourceScope, revision, publishedOn

GeneratedCodeDto
  codingRuleNId, ruleRevision, code, sequence, periodKey, generatedOn
```

所有响应由统一 `ApiResult<T>` 包裹；分页响应 Data 为 `PageResult<T>`。运行时响应不得泄露内部表名、缓存键或配置历史前值。

---

# 16. 集成事件与Outbox设计

V1 事件：

```text
ReferenceDictionaryPublishedV1
ReferenceConfigurationChangedV1
ReferenceDynamicConfigurationPublishedV1
ReferenceMetadataPublishedV1
ReferenceCodingRulePublishedV1
```

Exchange 与路由键固定为：

```text
exchange: industrial.system

industrial.reference-data.dictionary.published.v1
industrial.reference-data.configuration.changed.v1
industrial.reference-data.dynamic-configuration.published.v1
industrial.reference-data.metadata.published.v1
industrial.reference-data.coding-rule.published.v1
```

共同字段：

```text
eventId: Guid
eventVersion: 1
createdTime: DateTimeOffset
tenantNId: string?
scopeType: string
scopeId: string?
aggregateId: Guid
subjectNId: string
revision: long
traceId: string?
```

`ReferenceConfigurationChangedV1` 额外包含 `appDomainNId`、`keyNId?`、`valueMode?` 和 `changeType`。AppDomain 级变化时 keyNId 为空；Key/MultiValue 变化时 subjectNId 固定为完整路径。事件不携带 Value、DefaultValue 或 MultiValue 内容，消费者收到后按 API 重新读取。

事件只包含定位、版本和缓存失效所需字段，不携带完整配置值、字典内容或敏感信息。消费者收到事件后按 API 拉取当前版本；重复事件按 EventId 去重，乱序事件按 Revision 丢弃旧版本。

五模块分别在自己的 Schema 中维护 Outbox、Inbox 和消费位点，宿主只复用发布、消费、重试与清理组件，不共用数据表。Outbox Worker 每秒轮询一次、每批最多 100 条；失败按 1、2、4、8、16、30、60、120、300 秒退避，累计 10 次失败后保留消息并触发告警，不自动丢弃。消息发布失败不回滚已提交业务事务。事件字段新增保持向后兼容，删除或改义必须发布 V2。

---

# 17. Redis缓存与一致性设计

缓存键：

```text
referencedata:v1:{tenantKey}:dictionary:{normalizedNId}
referencedata:v1:{tenantKey}:configuration:{factoryKey}:{normalizedAppDomainNId}:{normalizedKeyNId}
referencedata:v1:{tenantKey}:configuration-domain:{factoryKey}:{normalizedAppDomainNId}:{revision}
referencedata:dynamic-property:v1:{tenantKey}:dynamic-config:{normalizedNId}:{revision}:{pageKey}
referencedata:v1:{tenantKey}:metadata:{normalizedNId}
referencedata:v1:{tenantKey}:coding-rule:{normalizedNId}
```

五模块分别维护缓存命名空间与模块修订，不设置会造成跨模块联动失效的全局 `ReferenceDataRevision`。建议 TTL：字典 30 分钟、参数配置 Key/Domain 5 分钟、DynamicProperty Schema 15 分钟、动态配置记录页 5 分钟、元数据 15 分钟、编码规则 15 分钟；AppDomain/Key/MultiValue 修改提交成功后失效整个逻辑 AppDomain 的 Key 和 Domain 缓存，其他能力按 NId 失效。缓存值必须包含 Revision，禁止永久缓存“不存在”。

一致性策略：

- Cache Aside：先读 Redis，未命中读 PostgreSQL 并回填。
- Redis 不可用时降级直读 PostgreSQL并记录指标，不阻断正确读取。
- PostgreSQL 与 Redis 同时不可用时返回 503，不返回无法证明有效的陈旧配置。
- 写入不经过缓存；事务成功后失效，失败不得误删为成功状态。
- 编码序列和 Idempotency Record 永不以 Redis 为权威。

---

# 18. 跨服务消费与兼容性设计

MasterData 及后续服务可稳定依赖：

- DictionaryItem.NId、ConfigurationKey.NId、EntitySchema.NId、AttributeDefinition.NId 和 CodingRule.NId 一经发布不可原位改名。
- 动态配置消费者必须先读取 Schema Revision，再读取相同 Revision 的记录页；不得跨 Revision 拼装字段和值。
- 运行时 DTO 均携带 Revision；消费者可保存所用 Revision 以便追溯。
- 同步 API 是首次读取和恢复权威；事件是变化提示，不保证消费者已拥有完整内容。
- 调用方必须设置不超过 Gateway 10 秒上限的超时；读取可对网络瞬时错误有限重试，编码生成只可携带同一 Idempotency-Key 重试。
- 禁止消费者通过数据库、Redis Key 或内部实体结构耦合 ReferenceData。

Factory 作用域在 MasterData 提供权威工厂归属校验适配器或稳定 API 前保持不可写；门禁满足后才能从受限状态切换为可用。不得在 ReferenceData 数据库建立工厂外键。

---

# 19. 前端信息架构、路由与权限

PC 菜单：

```text
系统管理
└── 参考数据
    ├── 字典管理
    ├── 参数配置
    ├── 动态属性
    ├── 元数据定义
    └── 编码规则
```

| 路由 | 页面 | 路由权限 |
| --- | --- | --- |
| `/pc/system/reference-data/dictionaries` | 字典列表/详情 | `ReferenceData.Dictionary.Read` |
| `/pc/system/reference-data/configurations` | 配置应用域/键/多值 | `ReferenceData.Parameter.Read` |
| `/pc/system/reference-data/dynamic-properties` | DynamicProperty 模块；编辑 DynamicConfigDefinition 字段/记录/值 | `ReferenceData.DynamicProperty.Read` |
| `/pc/system/reference-data/metadata` | Schema 列表/编辑 | `ReferenceData.Metadata.Read` |
| `/pc/system/reference-data/coding-rules` | 编码规则列表/预览 | `ReferenceData.CodingRule.Read` |

按钮通过 PermissionGate 控制五模块各自的 Read/Create/Update/Publish/Disable 权限；CodingRule 另有 Preview/Generate，Parameter 另有 ReadSecretReference。前端隐藏按钮不替代服务端授权。五个页面使用 PCLayout，不增加 PDA/Mobile 管理路由，也不建设统一审计页面。

前端 API 层必须只暴露有类型的方法和统一错误对象，不允许页面直接创建 Axios 实例。MSW 仅用于组件测试和前置联调，联合验收必须切换真实 Gateway。

---

# 20. PC页面与交互详细设计

## 20.1 字典管理

- 列表字段：NId、名称、作用域、Revision、状态、启用项数、更新时间、发布人。
- 筛选：关键字、作用域、状态；默认显示当前 Draft 和 Published，不默认展开 Superseded。
- 编辑页包含基本信息和可排序字典项表格，支持新增、编辑、禁用，不允许删除已发布历史项。
- 发布前展示校验结果和与上一 Published 版本的差异；发布成功刷新 Revision。
- 遇到 409 并发冲突时保留本地输入，展示“重新加载”与“复制未保存内容”，禁止自动覆盖。

## 20.2 参数配置

- 左侧应用域列表显示 NId、名称、作用域、状态、Revision、Key 数和 LastUpdatedOn；右侧显示所选应用域的 Key 表格。
- Key 表格字段：NId、完整路径、Name、DataType、ValueMode、IsMandatory、IsReadOnly、状态、值摘要和排序。
- Single 编辑器按 DataType 显示 Value 与 DefaultValue；Multi 编辑器切换为明细表，字段为 NId、Name、Value、Sort、IsDefault、Enabled。
- ValueMode 是唯一选择项，页面只派生展示 isMultiValue；切换类型/模式前若已有值必须阻止并提示新增 Key。
- IsReadOnly Key 的编辑、启停和 MultiValue 操作均禁用，并说明只能由受信任启动同步/迁移更新。
- 提供“有效值查看”，分别展示完整路径、命中 Factory/Tenant/Platform、Single 实际值/默认值来源或 Multi 明细列表。
- 可选 Key 的显式 null/空数组必须显示“阻断下级继承”，不能与“未定义，继续回退”混淆。
- 新增/修改必须填写变更原因；历史抽屉显示 AppDomain Revision、对象层级、操作者、CreatedOn 和脱敏后的前后摘要。
- 对疑似敏感 NId/Value 显示拒绝原因和密钥管理提示，不允许强行保存。

## 20.3 DynamicProperty（动态属性）

- 列表字段：NId、名称、作用域、Revision、状态、字段数、记录数、发布时间。
- Draft 编辑页分为“字段定义”“配置记录”“发布校验”三个页签；Published/Superseded 只能查看。
- 字段定义页按 DataType 显示专属约束，Enum 选择已发布字典，Reference 填写目标实体 NId；字段已有值后禁止直接改变 DataType，必须新增字段并在新 Revision 禁用旧字段。
- 配置记录页使用服务端分页，支持 Record.NId/Name/Category 筛选；行编辑表单由当前 Draft 字段定义生成，但只用于本配置集管理，不输出通用低代码页面。
- Required、Decimal 精度、日期、Enum、Json 和 Reference 错误在保存前显示到具体字段；服务端仍执行同样校验。
- 发布校验显示字段、记录、缺失必填值、类型错误、字典引用和与上一 Published Revision 的差异统计；存在任何错误时禁用发布。
- Clone 生成完整 Draft 快照。页面必须提示租户配置集是整份替换，不与平台记录隐式合并。
- 一次页面请求最多加载 100 条记录；不得为了表格便利一次下载 10,000 条记录。

## 20.4 元数据定义

- 列表字段：NId、名称、作用域、Revision、状态、属性数、发布时间。
- 编辑页包含属性表格、类型专属校验项和只读 Schema 预览。
- Enum 选择器只加载当前有效字典；字典不存在或未发布时阻止 Schema 发布。
- 发布差异按新增、约束收紧、约束放宽、禁用分类；删除已发布属性必须以新版本中的停用语义表达。
- 本页面不生成业务表单、不保存实体实例值、不接受可执行 JavaScript。

## 20.5 编码规则

- 列表字段：NId、名称、TargetEntityNId、作用域、Revision、状态、模板、重置策略。
- 编辑器只允许插入第 12.1 节 Token，并即时显示语法错误。
- Preview 要求用户填写模板所需上下文，明确标注“不消耗正式序号”。
- 页面不默认提供正式 Generate 操作；正式编码由拥有 `ReferenceData.CodingRule.Generate` 的业务调用方生成。
- 发布前展示样例、长度和序列分区说明；并发冲突行为与字典一致。

## 20.6 通用页面状态与可访问性

- 首屏加载使用 Skeleton；空列表提供创建入口；保存成功使用明确消息并保留可追踪对象 ID。
- 400 显示字段级错误，401 进入统一重新认证，403 展示无权限，404 返回列表，409 进入冲突处理，503 提示服务暂不可用。
- 表单标签与控件关联，键盘可完成主要操作；错误不只依赖颜色；目标视口为 1366×768 和 1920×1080。
- 离开未保存页面前必须确认；页面销毁时取消未完成查询，避免旧响应覆盖新筛选结果。

---

# 21. 错误、安全、审计与可观测性

## 21.1 稳定错误码

| 错误码 | HTTP | 前端行为 |
| --- | --- | --- |
| `REF-VALIDATION-FAILED` | 400 | 字段级提示 |
| `REF-SCOPE-INVALID` | 400 | 作用域字段提示 |
| `REF-SCOPE-FACTORY-NOT-READY` | 409 | 说明需等待 MasterData 工厂校验契约 |
| `REF-DICT-NOT-FOUND` | 404 | 返回字典列表 |
| `REF-DICT-DUPLICATE-NID` | 409 | 标记 DictionaryDefinition/DictionaryItem NId 冲突 |
| `REF-CONFIG-DOMAIN-NOT-FOUND` | 404 | 返回应用域列表 |
| `REF-CONFIG-KEY-NOT-FOUND` | 404 | 定位完整配置路径 |
| `REF-CONFIG-DUPLICATE-NID` | 409 | 标记 AppDomain/Key/MultiValue NId 冲突 |
| `REF-CONFIG-VALUE-MODE-CONFLICT` | 422 | 修正 Single/Multi 字段或明细 |
| `REF-CONFIG-MANDATORY-VALUE-MISSING` | 422 | 补充单值/默认值或启用多值项 |
| `REF-CONFIG-READ-ONLY` | 409 | 说明只能由受信任同步/迁移更新 |
| `REF-CONFIG-MULTI-VALUE-LIMIT` | 422 | 提示单键1000条上限 |
| `REF-CONFIG-SENSITIVE-REJECTED` | 400 | 指向密钥管理，不回显原值 |
| `REF-DYNAMIC-CONFIG-NOT-FOUND` | 404 | 返回动态配置列表 |
| `REF-DYNAMIC-CONFIG-FIELD-INVALID` | 422 | 定位字段定义或值错误 |
| `REF-DYNAMIC-CONFIG-REVISION-REQUIRED` | 400 | 先读取 Schema 并携带 Revision |
| `REF-DYNAMIC-CONFIG-REVISION-MISMATCH` | 409 | 重新加载同一 Revision，禁止混用 |
| `REF-DYNAMIC-CONFIG-LIMIT-EXCEEDED` | 422 | 提示字段/记录/Json大小上限 |
| `REF-METADATA-NOT-FOUND` | 404 | 返回 Schema 列表 |
| `REF-METADATA-DICTIONARY-INVALID` | 422 | 标记对应 Enum 属性 |
| `REF-CODING-RULE-NOT-FOUND` | 404 | 返回规则列表 |
| `REF-CODING-TEMPLATE-INVALID` | 422 | 展示 Token/上下文错误 |
| `REF-IDEMPOTENCY-REQUIRED` | 400 | 调用方补充请求头 |
| `REF-IDEMPOTENCY-CONFLICT` | 409 | 禁止换参数重试同一键 |
| `REF-CONCURRENCY-CONFLICT` | 409 | 保留输入并要求重新加载 |
| `REF-INVALID-STATE` | 409 | 展示当前状态和允许动作 |

401、403、429、500、503 沿用平台统一错误码和 TraceId。错误响应不得包含 SQL、连接串、消息体或堆栈。

## 21.2 安全与授权

- JWT 验证、`TenantNId/UserNId` 和权限声明由 Identity 权威契约提供；缺少租户的租户级操作 fail-closed。当前代码中的旧租户属性名仅为前置契约偏移，不得固化到本服务模型。
- 所有查询在 Repository/Query 层强制租户过滤，不依赖 Controller 手工拼接。
- 五模块分别声明 `ReferenceData.{Module}.Read/Create/Update/Publish/Disable`；CodingRule 额外声明 Preview/Generate，Parameter 额外声明 ReadSecretReference。平台级维护使用独立高权限，禁止以一个“管理员”权限覆盖全部动作；Identity 负责权威判定和分配。
- JSON、Pattern、EAV 字段和值和 Template 按类型、数量、长度与语法白名单校验；不执行表达式、脚本或动态 SQL。
- 配置值、生成编码和事件负载记录时按字段规则脱敏；日志禁止输出 Authorization 和 Idempotency Record 完整响应。

## 21.3 审计与可观测性

ReferenceData 不建立 `ref_operation_audit`、统一审计查询 API、审计管理页面或合规保留策略。五模块只保留解释自身状态所需的领域历史和事务 Outbox；PF-04 Audit 是统一审计事实源。PF-04 契约未确认前，本阶段只定义标准审计事件适配门禁，不预设其 API。必要的宿主操作日志只作短期故障诊断，不承担合规审计，也不得保存敏感原值。

结构化日志字段至少包括 `ServiceName`、`TraceId`、`TenantNId`、`UserNId`、`Module`、`Operation`、`ObjectNId`、`Revision`、`DurationMs` 和结果。指标至少包括 API 延迟/错误率、模块缓存命中率、数据库降级次数、各模块 Outbox/Inbox 积压与重试、发布次数、DynamicProperty 发布记录数/校验失败数、并发冲突和编码生成冲突。liveness 与 readiness 分离；readiness 汇总 SystemData 握手和五模块迁移版本，任一必需模块失败时整个宿主返回 503 `NotReady`。

---

# 22. 自动化测试与验收设计

## 22.1 测试矩阵

| 层次 | 必测场景 |
| --- | --- |
| Domain/Unit | 字典定义/项 NId 唯一、AppDomain/Key NId、Single/Multi互斥、Mandatory/Default/ReadOnly、多值明细、动态配置字段/记录 NId 与强类型值、发布状态机、Schema 交叉校验、模板 Token、序列分区 |
| Application | 权限与租户、逐Key作用域回退、显式空值阻断继承、AppDomain双版本、配置历史、动态配置整份 Revision、发布事务、幂等请求哈希 |
| Infrastructure | 配置三级表唯一/部分唯一索引、Entity固定生命周期列、所有父表(Id,IsDeleted)可引用唯一键、子表父级影子列复合外键及ON UPDATE CASCADE、动态配置四表同Revision约束、类型列Check Constraint、迁移、原子序列、Outbox、Redis降级、双重软删除过滤 |
| API | Gateway/内部路径、ApiResult/PageResult、400/401/403/404/409/422/503、Idempotency-Key |
| Contract/Event | DTO JSON 快照、V1 事件字段、重复/乱序兼容、禁止引用内部项目 |
| Frontend Component | AppDomain/Key导航、Single/Multi编辑、ReadOnly禁用、显式空值提示、动态字段编辑、EAV记录分页、发布差异、权限按钮、冲突保留、错误映射 |
| E2E | 登录→字典发布→运行时读取；应用域创建→单值/多值键→作用域解析；动态配置定义/记录/发布；Schema 发布；编码预览；幂等正式生成 |

上述 Domain、Application、Infrastructure、API、Contract 五层测试必须按 Dictionary、Parameter、Metadata、DynamicProperty、CodingRule 分别建立边界，覆盖 `TenantNId` 隔离、权限拒绝、乐观并发、发布/覆盖或修订语义、独立 Schema/前缀、migration ledger、Outbox/Inbox 和缓存降级。SQLite 用于快速集成，真实 PostgreSQL 18 必须提供迁移、约束和并发证据；PC 关键路径必须经过真实 Gateway。

## 22.2 关键验收场景

1. 平台发布字典，租户未覆盖时读取平台版本；租户发布同 NId 字典后读取完整租户版本。
2. Platform 创建 Weighting 应用域及 RequireDoubleCheck Single Key；Tenant 只覆盖该 Key 后按完整路径命中 Tenant，未覆盖 Key 继续回退 Platform。
3. Single Key 按 Value→DefaultValue 解析；Mandatory 无值返回稳定错误；可选显式 null 阻断回退；ReadOnly 管理写入返回 409。
4. Multi Key 只能使用明细表，按 Sort/NId 稳定返回；重复 NId/规范化值、默认项禁用、超过1000条或 Mandatory 空集合均被拒绝。
5. AppDomain/Key/MultiValue 任一实际变化同时推进 AppDomain Revision、LastUpdatedOn、OptimisticVersion 和 ConcurrencyVersion，历史保存完整路径和 CreatedOn。
6. Factory 参数配置在 MasterData 权威工厂归属校验就绪前被明确拒绝；禁用高作用域 Domain/Key 后继续向下级作用域回退。
7. 动态配置 Draft 定义 Decimal、Boolean、Enum、DateTime 和 Json 字段并录入多行记录；发布后 Schema 与记录返回相同 Revision，跨 Revision 值写入被拒绝。
8. 动态配置 Required 缺值、类型列不匹配、未发布 Enum 字典、重复 Record.NId、超过字段/记录上限时发布失败且不产生半发布数据。
9. 租户动态配置发布后整份替换平台配置；禁用租户版本后回退平台版本，不发生隐式行合并。
10. Enum 属性引用未发布字典时 Schema 发布失败；字典发布后 Schema 可发布且带 Revision。
11. 同一编码请求使用相同 Idempotency-Key 返回相同编码；换参数复用同一键返回 409；并发请求不生成重复序号。
12. 两个浏览器编辑同一 Draft，后提交者收到并发冲突，前端不丢失本地输入。
13. Redis 停止时运行时读取回源 PostgreSQL；PostgreSQL 同时不可用时返回 503。
14. 无权限用户看不到管理按钮且直接调用 API 返回 403；跨租户 ID 返回 404 或统一拒绝，不泄露存在性。
15. 发布写库成功但 RabbitMQ 暂时不可用时 Outbox 保留待重试，恢复后只发布兼容的 V1 事件。
16. 子表写入不存在或 IsDeleted 不匹配的父表组合时数据库拒绝；主表软删除/恢复通过 ON UPDATE CASCADE 同步父级影子列，但不覆盖子表自身 IsDeleted，默认子表查询同时过滤两种删除状态。
17. Development Shared/PerService 和 Test/Staging/Production PerService 均按可信配置解析；Shared 只 provision 一次，五模块迁移/账本/readiness 独立，同物理目标 DDL 串行。
18. SystemData 不可用、目标错误、drift、任一模块迁移失败或版本不一致时宿主保持 NotReady，不使用管理员凭据、`EnsureCreated` 或错误数据库回退。

## 22.3 验证证据格式

每项任务必须回写：

```text
命令
退出码
通过 / 失败 / 跳过数量
覆盖率（适用时）
TRX、coverage、Playwright report 或截图路径
PostgreSQL / Redis / RabbitMQ / 浏览器等外部环境状态
```

本次文档调整没有执行后端或前端测试；现有骨架测试和 PF-02 尚未开发的 fixture 只能作为输入，不能作为 PF-03 新鲜验收证据。

---

# 23. 开发任务依赖

```text
PF-01 Gateway/前端基线 + Identity 可信身份/权限契约 + PF-02 TASK-SD-001～004
                    ↓
              TASK-RD-001
                    ↓
              TASK-RD-002
        ┌───────────┼───────────┬───────────┬───────────┐
        ↓           ↓           ↓           ↓           ↓
   TASK-RD-003 TASK-RD-004 TASK-RD-005 TASK-RD-006 TASK-RD-007
        └───────────┴───────────┴─────┬─────┴───────────┘
                                      ├────────→ TASK-RD-008
                                      ├────────→ TASK-RD-009
                                      └────────→ TASK-RD-010

TASK-RD-003 + TASK-RD-004 + TASK-RD-010 → TASK-RD-011
TASK-RD-005 + TASK-RD-010               → TASK-RD-012
TASK-RD-006 + TASK-RD-007 + TASK-RD-010 → TASK-RD-013
TASK-RD-008 + TASK-RD-009 + TASK-RD-011 + TASK-RD-012 + TASK-RD-013
                                      ↓
                                TASK-RD-014
```

TASK-RD-003～007 的领域与应用代码可分工并行，但都会影响迁移、DI 和 API 注册；若并行派遣，必须预先划分迁移编号并由 TASK-RD-002 锁定公共文件，禁止多人同时编辑同一迁移或 Program 装配文件。

---

# 24. ReferenceData开发任务拆分

## TASK-RD-001 对齐服务骨架、Contracts、鉴权与测试结构

**状态：** 待派遣（PF-03 详细设计已确认；尚未实际派遣）

**目标：** 在保留现有健康检查的基础上，将四层骨架扩展为母版规定的五层边界，固化 API 前缀、Identity 身份接入、配置和分层测试入口。

**输入文档：** 本文第 1、4～6、15、21 节；PF-01 Gateway/前端基线及 Identity 可信身份、TenantNId/UserNId、权限判定契约。

**依赖：** BuildingBlocks 可运行基线、PF-01 Gateway/前端基线和 Identity 可信身份/权限契约已达到可消费门禁；不绑定旧 TASK-ID 编号。

**允许修改范围：** ReferenceData 后端项目、ReferenceData 测试、解决方案注册、Gateway 的 ReferenceData 路由测试和 ReferenceData 开发配置；不得新增领域业务、修改 Identity 业务实现或其他服务数据库。

**预期输出：** Contracts 项目、五层引用约束、认证授权装配、`/api/v1/reference-data` 路由组、测试项目结构、配置校验及保留通过的 live/ready 健康检查。

**验证与证据：** 验证五层程序集引用、匿名/认证/无权限路径、Gateway 前缀剥离、配置绑定和健康检查；记录后端 build/test 命令、退出码和测试报告。

**结果回写：** 回写真实目录、引用、端口、配置键、权限注册方式和执行记录；若 Identity 未提供可消费授权契约，状态改为“设计待确认”。

**建议提交：** `feat(referencedata): align service contracts and security baseline`

---

## TASK-RD-002 实现公共作用域、发布版本与数据库基础

**状态：** 待派遣（PF-03 详细设计已确认；尚未实际派遣）

**目标：** 首先接入 SystemData 数据库编排控制面，再建立 ReferenceScope、发布状态、规范化 NId、五模块独立 Schema/前缀、迁移单元/账本、Outbox/Inbox、并发与软删除约束，并形成宿主汇总 readiness。

**输入文档：** 本文第 6、7、13、14、21、22 节；蓝图 07/33、实施 05 和 BuildingBlocks 当前 Entity/Repository 契约。

**依赖：** TASK-RD-001；PF-02 TASK-SD-001～004 数据库编排链通过书面契约与验收。

**允许修改范围：** ReferenceData Domain/Application/Infrastructure/Contracts 的公共模块、数据库映射、迁移和对应测试；不得实现具体字典、配置、元数据或编码用例。

**预期输出：** `ServiceKey=referencedata` 登记和宿主 Migration Bundle、Shared/PerService 目标解析、五模块独立迁移单元/账本、SystemData Operation 握手、五模块版本汇总 readiness，以及作用域值对象、NId 规范化器、TenantNId 过滤、Entity 列/复合外键和并发映射。

**验证与证据：** 覆盖 Development Shared/PerService、非 Development Shared 拒绝、错误目标/drift/迁移失败 NotReady、同物理目标 DDL 串行、五模块独立账本和无 `EnsureCreated`；同时覆盖 Platform/Tenant/Factory 门禁、跨 TenantNId 隔离、Entity 列、复合外键、双版本并发及迁移升级/恢复。

**结果回写：** 回写实际表字段、索引名、迁移名、Factory 受限开关和任何与 BuildingBlocks 契约的偏差。

**建议提交：** `feat(referencedata): add scoped versioning persistence foundation`

---

## TASK-RD-003 实现字典中心纵向闭环

**状态：** 待派遣（PF-03 详细设计已确认；尚未实际派遣）

**目标：** 完成 DictionaryDefinition/Item 聚合、持久化、管理 API、有效字典解析和发布版本行为。

**输入文档：** 本文第 7、8、13～16、21、22 节。

**依赖：** TASK-RD-002。

**允许修改范围：** ReferenceData 的 Dictionary 领域、应用、Contracts、Infrastructure、Api、迁移和对应测试；不得修改前端或保存业务实体值。

**预期输出：** 字典 Draft/Published/Superseded/Disabled 全流程、整本租户覆盖、版本 DTO、管理与运行时端点及发布 Outbox 记录。

**验证与证据：** 覆盖 DictionaryDefinition/DictionaryItem NId 唯一、Definition复合外键、发布前校验、克隆新版本、旧版替换、租户回退、并发冲突、权限和 API 信封；记录 Domain/Application/API/数据库测试结果。

**结果回写：** 回写最终 DTO、路由、表/索引、错误码、事件字段和任务状态。

**建议提交：** `feat(referencedata): implement versioned dictionary center`

---

## TASK-RD-004 实现配置应用域、单值键与多值键纵向闭环

**状态：** 待派遣（PF-03 详细设计已确认；尚未实际派遣）

**目标：** 完成 ConfigurationAppDomain 聚合、ConfigurationKey 单值/多值模式、MultiValue 明细、逐 Key 作用域覆盖、历史和有效值解析。

**输入文档：** 本文第 7、9、13～17、21、22 节。

**依赖：** TASK-RD-002。

**允许修改范围：** ReferenceData 的 Configuration 领域、应用、Contracts、Infrastructure、Api、迁移和对应测试；不得引入 Secret 存储或读取 MasterData 数据库。

**预期输出：** AppDomain/Key/MultiValue 三级模型与三张业务表、两级父表 Id+IsDeleted 复合外键、九种 DataType、ValueMode、Mandatory/Default/ReadOnly、稳定 NId/完整路径、历史表、逐 Key 作用域解析、管理/运行时 API、敏感值拒绝、ReferenceConfigurationChangedV1 Outbox 和审计。

**验证与证据：** 覆盖 AppDomain/Key/MultiValue NId 唯一、父级 Id+IsDeleted 组合约束、Single/Multi 互斥、Value→DefaultValue、Mandatory、ReadOnly、重复多值、显式null/空集合阻断回退、Tenant逐Key覆盖、Factory未就绪、聚合Revision与Entity双版本同步推进、历史只追加、敏感值拒绝、权限和三级表索引；记录测试与数据库证据。

**结果回写：** 回写 AppDomain/Key/MultiValue 字段与表、NId/完整路径规范、DTO/API、类型和模式、解析/回退语义、历史脱敏、事件、错误码和任务状态。

**建议提交：** `feat(referencedata): implement configuration domains and keys`

---

## TASK-RD-005 实现DynamicProperty模块及EAV动态配置集纵向闭环

**状态：** 待派遣（PF-03 详细设计已确认；尚未实际派遣）

**目标：** 以正式 `DynamicProperty` 模块边界实现 ReferenceData 自有完整 EAV，并保留 `DynamicConfigDefinition` 核心聚合及 Field/Record/Value 领域语义，交付完整修订发布和运行时分页读取。

**输入文档：** 本文第 7、10、13～18、21、22 节；TASK-RD-003 已发布字典契约用于 Enum 字段校验。

**依赖：** TASK-RD-002、TASK-RD-003。

**允许修改范围：** ReferenceData 的 DynamicProperty Domain/Application/Contracts/Infrastructure/Api、`reference_dynamic_property` Schema（SQLite 等价前缀）、独立迁移账本/Outbox/Inbox/缓存和对应测试；内部继续使用 DynamicConfigDefinition/Field/Record/Value，不得保存物料、设备、工单等业务实体值，不得加入脚本、公式、审批、低代码页面或通用分析引擎。

**预期输出：** 全部实体使用 NId 的 DynamicConfigDefinition 聚合、Field/Record/Value 子项、父级 Id+IsDeleted 复合外键、四表同 Revision 约束、强类型值列、Draft 克隆、整份发布、作用域替换、`/dynamic-properties/configurations` API、ReferenceDynamicConfigurationPublishedV1 和模块独立 Outbox/Inbox。

**验证与证据：** 覆盖 Definition/Field/Record NId 唯一、三组父级 Id+IsDeleted 组合、九种字段类型、Required/默认值/精度/字典/引用约束、跨 Revision 拒绝、类型列 Check Constraint、10,000 行分页、整份租户覆盖、发布事务、并发冲突、权限和事件快照；记录 Domain/Application/API/PostgreSQL 测试命令与报告。

**结果回写：** 回写最终四表字段/索引/Check Constraint、DTO、API、字段与记录上限、发布/覆盖语义、错误码、事件和任务状态。

**建议提交：** `feat(referencedata): add versioned dynamic configuration eav`

---

## TASK-RD-006 实现元数据定义纵向闭环

**状态：** 待派遣（PF-03 详细设计已确认；尚未实际派遣）

**目标：** 完成 EntitySchema/AttributeDefinition、类型专属约束、字典引用校验、发布版本和运行时读取。

**输入文档：** 本文第 7、11、13～16、18、21、22 节；TASK-RD-003 已发布字典契约。

**依赖：** TASK-RD-002、TASK-RD-003。

**允许修改范围：** ReferenceData 的 Metadata 领域、应用、Contracts、Infrastructure、Api、迁移和对应测试；不得创建业务实体 EAV 值表、页面生成器或业务实体仓储。

**预期输出：** EntitySchema/AttributeDefinition NId、父级 Id+IsDeleted 复合外键、Schema 版本生命周期、Attribute 类型/约束校验、Enum 字典交叉校验、管理/运行时 API、ReferenceMetadataPublishedV1 Outbox。

**验证与证据：** 覆盖 AttributeDefinition.NId 唯一、类型专属字段、危险 Pattern、未发布字典引用、版本克隆/发布、租户覆盖、权限和并发；记录测试和契约快照。

**结果回写：** 回写 Schema/Attribute DTO、支持类型、版本消费要求、与动态配置 EAV/业务实体值/低代码平台的边界和任务状态。

**建议提交：** `feat(referencedata): implement metadata schema definitions`

---

## TASK-RD-007 实现编码规则与幂等序列生成

**状态：** 待派遣（PF-03 详细设计已确认；尚未实际派遣）

**目标：** 完成模板解析、规则发布、预览、数据库原子序列、周期重置和幂等正式编码生成。

**输入文档：** 本文第 7、12～15、18、21、22 节。

**依赖：** TASK-RD-002。

**允许修改范围：** ReferenceData 的 CodingRule 领域、应用、Contracts、Infrastructure、Api、迁移和对应测试；不得在 Redis 中实现唯一序列或修改 MasterData/OperationalData。

**预期输出：** CodingRule.NId、白名单 Token 解析器、规则版本、Preview/Generate API、序列/幂等表到 CodingRule 的 Id+IsDeleted 复合外键、并发唯一性和审计。

**验证与证据：** 覆盖非法 Token、缺失上下文、长度、四种 ResetPolicy、Preview 不消耗、同键同结果、同键异请求冲突和高并发无重复；记录数据库集成测试与并发测试证据。

**结果回写：** 回写模板语法、分区键、Idempotency-Key 期限、DTO、错误码和任务状态。

**建议提交：** `feat(referencedata): add idempotent coding rule generation`

---

## TASK-RD-008 实现缓存、降级与有效读取优化

**状态：** 待派遣（PF-03 详细设计已确认；尚未实际派遣）

**目标：** 为字典、参数配置应用域（单值/多值）、动态配置、元数据和编码规则五类运行时读取建立 Revision 感知的 Cache Aside、提交后失效、Redis 降级和指标。

**输入文档：** 本文第 4、8～12、17、18、21、22 节。

**依赖：** TASK-RD-003～TASK-RD-007。

**允许修改范围：** ReferenceData Application/Infrastructure/Api 的查询、缓存、配置、指标和对应测试；不得缓存正式编码序列或绕过 PostgreSQL 权威。

**预期输出：** 统一缓存键生成器、TTL 配置、命中/未命中/失效、Redis 故障回源、双故障 503 和缓存指标。

**验证与证据：** 覆盖租户键隔离、Revision 更新、发布后失效、Redis 不可用回源、数据库同时不可用拒绝和禁止永久负缓存；记录 Redis/PostgreSQL 集成证据。

**结果回写：** 回写真实缓存键、TTL、降级行为、指标名和外部环境限制。

**建议提交：** `feat(referencedata): add revision-aware runtime caching`

---

## TASK-RD-009 实现模块Outbox/Inbox、审计适配与可观测性闭环

**状态：** 待派遣（PF-03 详细设计已确认；尚未实际派遣）

**目标：** 完成五模块独立 Outbox/Inbox/消费位点、五个 V1 事件、发布重试、PF-04 Audit 适配门禁、日志脱敏、指标与健康诊断。

**输入文档：** 本文第 13、16、21、22 节；RabbitMQ 事件总线规范和 BuildingBlocks EventBus。

**依赖：** TASK-RD-003～TASK-RD-007。

**允许修改范围：** ReferenceData 五模块的 Events、Outbox/Inbox、Logging、Metrics、Health 和测试，以及宿主共享技术组件；不得建立统一审计事实表、审计查询 API/页面或把完整配置值写入事件和日志。

**预期输出：** V1 事件、模块独立 Outbox/Inbox/位点、共享 Worker、退避/告警、重复与乱序契约测试、PF-04 Audit 契约适配门禁、结构化日志和指标。

**验证与证据：** 覆盖各模块业务事务与 Outbox 原子性、Inbox 幂等、RabbitMQ 中断后恢复、重复/乱序容忍、事件 JSON 快照、敏感值扫描、跨模块表隔离和积压健康状态；证明不存在竞争 PF-04 的统一审计表/API/页面。

**结果回写：** 回写事件名/字段/路由键、模块 Outbox/Inbox 表与重试参数、指标、PF-04 适配状态和任务状态。

**建议提交：** `feat(referencedata): publish reference changes through outbox`

---

## TASK-RD-010 接入前端API、类型、路由与权限框架

**状态：** 待派遣（PF-03 详细设计已确认；尚未实际派遣）

**目标：** 在统一前端内建立 ReferenceData 类型化 API、Store、五个 PC 路由、菜单和权限边界，为页面任务提供稳定基座。

**输入文档：** 本文第 3、15、19、21 节；02B 前端 API/路由/权限规范；03 的真实会话输出。

**依赖：** TASK-RD-001；API 契约评审通过，可先使用 MSW，真实联调依赖 TASK-RD-003～007。

**允许修改范围：** `src/frontend/src/api/referenceData/**`、`types/referenceData/**`、`stores/referenceData/**`、`router/referenceDataRoutes.ts`、PC 菜单与对应前端测试；不得新增 PDA/Mobile 页面或修改后端。

**预期输出：** DTO 类型、API Gateway 方法、统一错误映射、查询取消、ReferenceData 路由/菜单、PermissionGate 和 MSW 契约夹具。

**验证与证据：** 覆盖 Gateway 路径、序列化、401/403/409/422/503 映射、路由守卫、按钮权限、请求取消和 MSW/真实模式切换；记录 lint、typecheck、unit、build 结果。

**结果回写：** 回写最终目录、路由、权限码、API 方法、Mock 边界和任务状态。

**建议提交：** `feat(frontend): add reference data client and routes`

---

## TASK-RD-011 实现字典与参数配置PC页面

**状态：** 待派遣（PF-03 详细设计已确认；尚未实际派遣）

**目标：** 交付字典管理和参数配置两个可完成真实业务操作的 PC 页面。

**输入文档：** 本文第 8、9、15、19～22 节。

**依赖：** TASK-RD-003、TASK-RD-004、TASK-RD-010。

**允许修改范围：** `src/frontend/src/pages/pc/referenceData/dictionaries/**`、`configurations/**`、相关 Store/组件/测试；后端仅允许修正经确认的契约缺陷，不得扩展范围。

**预期输出：** 字典 Draft/发布管理；参数配置 AppDomain 主从导航、Single/Multi 编辑、Mandatory/Default/ReadOnly 状态、逐 Key 有效值来源、显式空值提示、历史抽屉、权限按钮和 AppDomain 并发冲突保留。

**验证与证据：** 组件测试覆盖加载/空/成功/失败/无权限/冲突、Single/Multi切换门禁、ReadOnly、Mandatory、默认值、多值排序/去重和显式空值；Playwright 覆盖字典发布及 AppDomain→Key→MultiValue→作用域解析关键路径；记录 1366×768、1920×1080 截图和前端质量命令。

**结果回写：** 回写页面字段、交互偏差、截图/报告路径、遗留限制和任务状态。

**建议提交：** `feat(frontend): add dictionary and configuration management`

---

## TASK-RD-012 实现DynamicProperty EAV动态属性PC页面

**状态：** 待派遣（PF-03 详细设计已确认；尚未实际派遣）

**目标：** 交付动态配置定义、字段设计、配置记录和值编辑、发布校验和版本查看的完整 PC 管理页面。

**输入文档：** 本文第 10、15、19～22 节；TASK-RD-005 的真实 API 与 TASK-RD-010 的前端基座。

**依赖：** TASK-RD-005、TASK-RD-010。

**允许修改范围：** `src/frontend/src/pages/pc/referenceData/dynamicProperties/**`、相关 API/Types/Store/组件/测试；页面入口与模块权限使用 DynamicProperty，内部表单继续呈现 DynamicConfigDefinition/Field/Record/Value；不得生成业务运行页面，不得一次加载全部记录，不得绕过服务端校验。

**预期输出：** 动态配置列表、字段定义页、服务端分页记录表、字段驱动记录编辑表单、Revision 克隆/差异/发布、整份替换提示、权限按钮和并发冲突保留。

**验证与证据：** 组件测试覆盖九种类型控件、Required/Enum/Decimal/Json 错误、分页与筛选、字段有值后改类型拒绝、发布校验、权限和冲突；Playwright 覆盖创建→字段→记录→发布→运行时读取，并记录两个目标视口截图。

**结果回写：** 回写路由、页面字段、分页参数、类型控件映射、发布差异、截图/报告路径、已知限制和任务状态。

**建议提交：** `feat(frontend): add dynamic configuration management`

---

## TASK-RD-013 实现元数据与编码规则PC页面

**状态：** 待派遣（PF-03 详细设计已确认；尚未实际派遣）

**目标：** 交付元数据定义和编码规则两个 PC 页面，并守住“不保存业务实体 EAV 值、不生成低代码页面”的边界。

**输入文档：** 本文第 11、12、15、19～22 节。

**依赖：** TASK-RD-006、TASK-RD-007、TASK-RD-010。

**允许修改范围：** `src/frontend/src/pages/pc/referenceData/metadata/**`、`codingRules/**`、相关 Store/组件/测试；不得加入动态业务表单运行时或默认正式编码按钮。

**预期输出：** Schema/属性编辑、Enum 字典选择、发布差异、规则 Token 编辑、上下文输入、无序号消耗 Preview、权限和冲突处理。

**验证与证据：** 覆盖类型专属字段、字典引用失败、危险 Pattern、非法 Token、Preview 不消耗提示、权限、未保存离开确认和两个目标视口截图。

**结果回写：** 回写页面/组件、支持的属性类型与 Token、明确未实现范围、报告路径和任务状态。

**建议提交：** `feat(frontend): add metadata and coding rule management`

---

## TASK-RD-014 完成契约、E2E与PF-03联合验收

**状态：** 待派遣（PF-03 详细设计已确认；尚未实际派遣）

**目标：** 使用真实 Gateway、Identity、SystemData 数据库编排、PostgreSQL、Redis、RabbitMQ 和浏览器验证 ReferenceData 五模块全纵向链路，并冻结供下游使用的 V1 契约。

**输入文档：** 本文全部章节；TASK-RD-001～TASK-RD-013 的输出和执行记录。

**依赖：** TASK-RD-008、TASK-RD-009、TASK-RD-011、TASK-RD-012、TASK-RD-013。

**允许修改范围：** ReferenceData 契约/测试/文档、前端 E2E、部署冒烟脚本的 ReferenceData 检查和本文执行记录；只允许修复验收发现的本阶段缺陷，不得增加新领域。

**预期输出：** 运行时 DTO/事件 V1 快照、全量测试报告、关键路径 E2E、故障降级证据、安全扫描结果、页面截图和 MasterData 输入契约。

**验证与证据：** 执行后端 build/test、前端 lint/typecheck/unit/build/e2e；验证 Shared/PerService、SystemData 握手、五模块 migration ledger/readiness、错误目标/drift/迁移失败 NotReady、真实 PostgreSQL 18 约束、Redis/RabbitMQ 故障和敏感信息扫描；证明无 `EnsureCreated` 且业务 API 不创建数据库。

**结果回写：** 更新本文完成标准、执行记录、最终 API/事件/权限/路由、已知限制和 MasterData 前置条件；任一外部环境未实测则保持“待验收”。

**建议提交：** `test(referencedata): complete pf03 acceptance`

---

# 25. ReferenceData完成标准

## 25.1 领域与数据

- 字典、参数配置应用域（单值/多值）、动态配置 EAV、元数据定义和编码规则均有明确聚合、不变量、状态和租户边界。
- 所有领域实体以 NId 作为稳定业务标识，引用字段使用 `{EntityName}NId`；除正式生成的编码结果外，实体定义、DTO、API 和页面不存在 Code 业务标识。
- 表定义只列业务字段；所有表统一具备 Entity 生命周期。每个同库父子关系均使用子表 `{ParentEntity}_Id + {ParentEntity}_IsDeleted` 引用父表 `(Id, IsDeleted)`，并验证级联同步及双重软删除过滤。
- `referencedata_db` 按 SystemData 配置解析 Shared/PerService；五模块独立迁移单元、账本和 readiness 均有真实 PostgreSQL 18 证据。
- 动态配置四表只允许同 Revision 关联，字段和值类型由领域与数据库约束双重保证，整份发布不产生半成品。
- 发布/配置变更与各模块 Outbox 原子，Inbox 幂等；正式编码在并发和重试下不重复。
- 动态配置值仅保存 ReferenceData 自有配置记录；未创建业务实体 EAV 值表、低代码页面运行时或跨服务外键。

## 25.2 API、事件与缓存

- Gateway 与内部路径、DTO、权限、错误码、Idempotency-Key 和 Revision 前后一致。
- 五个 V1 事件完成重复、乱序和兼容性验证。
- Redis 故障可回源 PostgreSQL，双故障明确返回 503，缓存不成为权威。
- MasterData 可只通过稳定 V1 契约消费字典、参数配置应用域、动态配置 Schema/记录、实体 Schema 和编码生成。

## 25.3 前端与用户路径

- 五个 PC 页面连接真实 Gateway，覆盖加载、空、成功、校验、冲突、无权限和服务不可用状态。
- 菜单、路由和按钮权限与服务端权限一致；没有 PDA/Mobile 管理页越界。
- 关键页面通过 1366×768、1920×1080 截图和键盘可操作性检查。

## 25.4 安全、审计与可观测性

- 跨 TenantNId 访问被拒绝；平台级管理、模块发布和编码生成分权。
- 配置、日志、事件和错误响应无密码、Token、私钥、连接串和堆栈泄漏。
- 关键变更可由模块领域历史和 Outbox 按 TenantNId/UserNId/ObjectNId/Revision/TraceId 解释；PF-04 Audit 承担统一审计事实，Outbox/Inbox、缓存和编码冲突可观测。

## 25.5 自动化与环境验收

- Domain、Application、Infrastructure、API、Contract/Event、Frontend Component 和 E2E 的适用场景均有新鲜证据。
- 后端 build/test 与前端 lint/typecheck/unit/build/e2e 的命令、退出码和数量已回写。
- PostgreSQL、Redis、RabbitMQ、Gateway、Identity 和浏览器任一环境缺失时，对应任务只能标记“待验收”。
- SystemData 控制面、Shared/PerService 拓扑、五模块版本汇总和 NotReady 故障门禁必须有新鲜证据；禁止 `EnsureCreated`、管理员自建库和错误数据库回退。

---

# 26. 执行记录

| 任务 | 状态 | 执行者/任务 | 提交 | 验证证据 | 结果回写 |
| --- | --- | --- | --- | --- | --- |
| TASK-RD-001 | 待派遣 | - | - | PF-03 详细设计已确认，尚未实际派遣 | - |
| TASK-RD-002 | 待派遣 | - | - | PF-03 详细设计已确认，尚未实际派遣 | - |
| TASK-RD-003 | 待派遣 | - | - | PF-03 详细设计已确认，尚未实际派遣 | - |
| TASK-RD-004 | 待派遣 | - | - | PF-03 详细设计已确认，尚未实际派遣 | - |
| TASK-RD-005 | 待派遣 | - | - | PF-03 详细设计已确认，尚未实际派遣 | - |
| TASK-RD-006 | 待派遣 | - | - | PF-03 详细设计已确认，尚未实际派遣 | - |
| TASK-RD-007 | 待派遣 | - | - | PF-03 详细设计已确认，尚未实际派遣 | - |
| TASK-RD-008 | 待派遣 | - | - | PF-03 详细设计已确认，尚未实际派遣 | - |
| TASK-RD-009 | 待派遣 | - | - | PF-03 详细设计已确认，尚未实际派遣 | - |
| TASK-RD-010 | 待派遣 | - | - | PF-03 详细设计已确认，尚未实际派遣 | - |
| TASK-RD-011 | 待派遣 | - | - | PF-03 详细设计已确认，尚未实际派遣 | - |
| TASK-RD-012 | 待派遣 | - | - | PF-03 详细设计已确认，尚未实际派遣 | - |
| TASK-RD-013 | 待派遣 | - | - | PF-03 详细设计已确认，尚未实际派遣 | - |
| TASK-RD-014 | 待派遣 | - | - | PF-03 详细设计已确认，尚未实际派遣 | - |

---

# 27. 下一阶段输入契约

ReferenceData 完成后，MasterData 及后续服务可以稳定依赖：

跨服务契约仅暴露实体 NId、Revision 和业务结果，不暴露数据库复合外键；下游引用字段按 `{EntityName}NId` 命名。

```text
Gateway路径
  /referencedata/api/v1/reference-data/**

运行时API
  GET  /api/v1/reference-data/dictionaries/{nId}
  GET  /api/v1/reference-data/configuration-domains/{appDomainNId}
  GET  /api/v1/reference-data/configuration-domains/{appDomainNId}/keys/{keyNId}
  GET  /api/v1/reference-data/dynamic-properties/configurations/{nId}/schema
  GET  /api/v1/reference-data/dynamic-properties/configurations/{nId}/records?revision={revision}
  GET  /api/v1/reference-data/dynamic-properties/configurations/{nId}/records/{recordNId}?revision={revision}
  GET  /api/v1/reference-data/metadata-schemas/{nId}
  POST /api/v1/reference-data/coding-rules/{nId}/preview
  POST /api/v1/reference-data/coding-rules/{nId}/generate

稳定DTO
  EffectiveDictionaryDto
  EffectiveConfigurationDto
  EffectiveDynamicConfigSchemaDto
  DynamicConfigRecordDto
  EffectiveSchemaDto
  CodePreviewDto
  GeneratedCodeDto

V1事件
  ReferenceDictionaryPublishedV1
  ReferenceConfigurationChangedV1
  ReferenceDynamicConfigurationPublishedV1
  ReferenceMetadataPublishedV1
  ReferenceCodingRulePublishedV1

权限码（各模块按实际支持动作取子集）
  ReferenceData.Dictionary.Read/Create/Update/Publish/Disable
  ReferenceData.Parameter.Read/Create/Update/Disable/ReadSecretReference
  ReferenceData.Metadata.Read/Create/Update/Publish/Disable
  ReferenceData.DynamicProperty.Read/Create/Update/Publish/Disable
  ReferenceData.CodingRule.Read/Create/Update/Publish/Disable/Preview/Generate

前端路由
  /pc/system/reference-data/dictionaries
  /pc/system/reference-data/configurations
  /pc/system/reference-data/dynamic-properties
  /pc/system/reference-data/metadata
  /pc/system/reference-data/coding-rules
```

已知限制：Factory 作用域要等待 MasterData 提供权威工厂归属校验后启用；后台服务身份等待 Identity 的服务认证契约。动态配置 EAV 只提供配置数据的强类型存储、版本和读取，不提供脚本、公式、任意分析查询或低代码运行时。后续服务必须自行设计业务实体动态属性值、Schema 迁移、业务审批和数据权限，不能从 ReferenceData 的动态配置或元数据推断这些能力已经存在。

---

# 28. 文档自审清单

- [x] 引用文件真实存在。
- [x] 当前代码/环境状态与文档一致。
- [x] 无待确定项、待办占位或模糊处理语句。
- [x] ReferenceData、Identity、MasterData、OperationalData 和低代码平台边界明确。
- [x] API、事件、类型、权限和路由前后一致。
- [x] 表字段清单未重复 Entity 生命周期字段，且所有表统一继承第 6 节公共生命周期。
- [x] 领域实体身份统一使用 NId；Code 仅表达正式生成的编码结果，跨实体业务引用使用 `{EntityName}NId`。
- [x] 所有同库父子表均定义 `{ParentEntity}_Id + {ParentEntity}_IsDeleted` 到父表 `(Id, IsDeleted)` 的复合外键及软删除同步/过滤规则。
- [x] 字典、参数配置应用域（单值/多值）、动态配置 EAV、元数据、编码规则及五个页面均有对应任务和验收。
- [x] 每个任务具备状态、目标、输入文档、依赖、允许修改范围、预期输出、验证与证据、结果回写、建议提交九字段。
- [x] 任务依赖图、任务卡和执行记录编号一一对应。
- [x] 现有测试代码与本轮新鲜验证证据严格区分。
- [x] `git diff --check` 通过。
