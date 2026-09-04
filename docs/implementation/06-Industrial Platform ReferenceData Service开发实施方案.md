# 06-Industrial Platform ReferenceData Service开发实施方案

# Industrial Platform ReferenceData Service开发实施方案

> 当前里程碑范围：在现有四层服务骨架和健康检查基础上，完成字典、参数配置应用域（单值/多值）、EAV 动态配置集、元数据定义、编码规则、状态机定义、计量单位、缓存与变更事件，并同步交付对应 PC 管理页面、契约测试和关键路径 E2E；物料、设备、工单等业务实体的 EAV 属性值、业务状态执行、物料专属换算、低代码页面运行时和通用规则引擎不在本阶段实现。

版本：V2.7（七模块参考定义增补版）

所属项目开发路线阶段：PF-03「ReferenceData」。当前代码只有服务骨架；本文沿用 V2.6 的纵向交付与共享基础设施决策，新增状态机、计量单位后形成 `TASK-RD-001～010` 十个顺序执行步骤。原五个业务模块全部保留，但不复制七套微服务级治理设施。文档收敛不等于 PF-03 已启动；内部步骤不独立派遣、默认不独立提交，任何开发和状态推进仍需 PF-03 整体授权。阶段定义见 `docs/blueprint/09-Industrial Platform开发总TodoList.md`。

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

本文同时承担 ReferenceData 的开发详细设计、前后端协作契约和 PF-03 实施步骤唯一维护源。PF-03 的整体派遣与状态仍以仓库当前执行协议为准；开发人员不应再依据 V1.0 中“创建四层项目”或“实现对应页面”等摘要描述自行推断领域边界、API、状态机和验收条件。

目标读者包括后端、统一前端、测试、集成和任务验收人员。任何实现偏差必须先回写本文，再继续开发或验收。

## 1.2 当前输入状态

截至本次文档设计核对：

- 后端已存在 Api、Application、Domain、Infrastructure 四个项目，并已加入解决方案。
- Api 已提供 `/health`、`/health/live`、`/health/ready`，Infrastructure 已注册 PostgreSQL、Redis 和 RabbitMQ 基础能力。
- 现有单一测试项目覆盖程序集边界、开发配置绑定和健康检查；本次只核对源文件，不把这些测试表述为本轮重新执行的证据。
- 当前 initializer 仍以 CodeFirst 创建 `reference_data_schema_migrations`/`reference_data_seed_ledger` 占位表，ready 端点也会把 Redis/RabbitMQ/Seq 全部作为阻断项；两者都是 TASK-RD-001 必须替换的 PF-03 前骨架差距，不是目标架构。
- 尚无 ReferenceData 业务聚合、数据库迁移、业务 API、Contracts 项目、缓存策略、Outbox、权限和管理页面。
- 统一前端已具备 `httpClient`、真实路由与守卫、权限目录/`PermissionGate`、导航注册、统一页面/查询/表格/抽屉组件、Pinia 和真实 E2E 基线；ReferenceData API、路由、权限项和页面尚不存在。
- V1.0 同时写有“仅创建项目骨架”和“完成业务能力”的冲突表述；V2.7 以 PF-03 七模块逐个纵向交付和当前真实骨架为准，骨架不再作为待创建目标。

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
TASK-RD-001～TASK-RD-010（顺序执行）
    ↓
MasterData 等下游按公开契约消费
```

前置环境缺失不影响本文复核结论，但会阻止对应开发任务从“待派遣”进入开发或验收。不得再以旧 Identity 任务编号替代权威契约门禁。

---

## 1.4 本轮增补依据

2026-09-04 按用户提供的《OP ReferenceData：实体字段与功能增补参考》及用户确认的边界吸收设计理念：增加 StateMachine、UnitOfMeasure 两个逻辑模块；通用单位定义与换算从 MasterData 规划移入本服务。该参考是研究材料，不是逐字段复制清单。本轮只改文档，既有 Entity 九字段、冻结/锁定规则和平台底座不重建；OP 的共享模板、行为引擎、任意公式、批量导入体系不自动进入 PF-03。

# 2. 服务定位、目标与职责边界

## 2.1 ReferenceData负责

- 字典定义、字典项、发布版本和租户覆盖。
- 非敏感参数配置应用域及其单值键、多值键的类型约束、作用域覆盖、有效值解析和变更历史。
- 由 ReferenceData 自身拥有的多行、异构字段动态配置集，包括字段定义、配置记录、强类型字段值和整份修订发布。
- 动态实体的元数据定义、属性约束、发布版本和读取契约。
- 编码模板、序列分区、预览、幂等生成和规则版本。
- 状态机定义、节点、动作与转换路径、修订发布及定义合法性判断；不持有业务实例状态。
- 通用计量维度、单位、基准换算、精度/舍入、预置保护和历史修订；不接管物料专属比例。
- 上述能力的 PC 管理页面、权限、审计、缓存、变更事件和消费契约。

ReferenceData 是定义和规则的权威来源，不是所有“基础数据”的统称。

正式宿主固定为现有 `ReferenceData.Service`，不新建核心 Service Host。宿主内固定划分七个逻辑模块：`Dictionary`、`Parameter`、`Metadata`、`DynamicProperty`、`CodingRule`、`StateMachine`、`UnitOfMeasure`。七模块独立建模，使用分层项目内的模块目录/命名空间、独立公开契约、权限、缓存键前缀和测试目录；物理上共享一个 `referencedata_db`、一个 PostgreSQL Schema `reference_data`、一个服务级 Migration/Ledger、一个带 `ModuleKey` 的服务级 Outbox、连接和宿主基础设施。当前没有真实入站事件消费者，因此不预建 Inbox/Checkpoint。禁止跨模块直读 Repository/数据表或建立数据库外键；同宿主协作使用进程内 Application 契约，不引入内部 HTTP 或 RabbitMQ。

## 2.2 ReferenceData不负责

| 不属于本服务的内容 | 权威归属 | 本服务允许保存的引用 |
| --- | --- | --- |
| 用户、角色、登录、SSO | Identity | `TenantNId`、`UserNId` 和权限上下文 |
| 物料、设备、制造组织、工厂、仓库、库位、BOM、工艺路线及物料专属单位比例 | MasterData | 对应实体 NId 或不透明外部标识，不建立跨库外键 |
| 库存批次、余额、预留、收发退、调拨、盘点单据 | OperationalData | CodingRuleNId 和已生成编码 |
| 工单、称量、追溯、批记录业务事实，以及业务当前状态、业务守卫和状态历史 | 对应业务服务 | 字典/配置/元数据、状态机/单位的固定来源与 Revision、编码结果 |
| 随物料变化的箱/件比例、包装规则、密度条件换算 | MasterData/对应业务服务 | 通用单位 NId/Revision；ReferenceData 不存业务换算上下文 |
| 密码、连接串、API Key、证书和私钥 | 受信 Secret Provider | Parameter 只允许保存 `SecretRef`、版本标识和脱敏状态，不保存或解析秘密本体 |
| 物料、设备、工单等实体的动态属性实际值 | 拥有该实体的业务服务 | 元数据定义和 `schemaRevision` |
| 低代码页面生成、任意脚本和规则执行 | 后续低代码平台 | 稳定的元数据读取 API |

正例：ReferenceData 定义 `Equipment` 可扩展属性 `Voltage` 为 Decimal，并发布 schema revision 3。

反例：ReferenceData 不保存设备 `MIX-001` 的 `Voltage=380`；该值由 MasterData 保存并记录使用的 schema revision。

## 2.3 本阶段取舍

本阶段采用“核心 ReferenceData”边界：完整交付字典、参数配置应用域（单值/多值）、ReferenceData 自有动态配置集、元数据定义、编码规则、状态机定义和计量单位。动态配置集允许保存配置记录和值，但只用于规则矩阵、映射表、阈值表等配置数据；不创建 `ref_entity_attribute_value`，不保存业务实体实例，不实现表单页面 JSON、动态列表运行时或通用规则引擎。

`DynamicProperty` 是正式的大模块边界，覆盖动态属性及 EAV 能力；现有完整 `DynamicConfigDefinition` 并非临时模型或待机械改名对象，而是该模块当前的核心聚合根。其 `DynamicConfigFieldDefinition`、`DynamicConfigRecord`、`DynamicConfigFieldValue` 领域命名和版本化四表语义继续保留；模块 API 前缀、表前缀、权限、契约命名空间、迁移目录和测试统一归属 `DynamicProperty`，但仍共用服务级 Schema、迁移流和基础设施。`Metadata` 负责实体 Schema/属性结构和兼容性差异，`DynamicProperty` 负责动态字段、记录和值；两者只通过 `EntitySchemaNId/schemaRevision` 等公开契约引用。

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
  │       ├── Domain：字典、参数配置应用域（单值/多值）、动态配置 EAV、元数据、编码规则、状态机定义、计量单位
  │       └── Contracts：DTO、错误码、集成事件 V1
  │
  └── Infrastructure
          ├── referencedata_db：权威数据、迁移、Outbox、审计
          ├── Redis：已发布读取模型缓存
          └── RabbitMQ：发布变更事件

MasterData / OperationalData / 业务服务
  ├── 同步读取有效字典、参数配置应用域、动态配置和元数据
  ├── 固定来源/修订读取状态机、单位；定义校验与数量换算不代替业务事务
  ├── 同步幂等生成编码
  └── 异步消费发布/变更事件并失效本地缓存
```

数据权威与事务规则：

- PostgreSQL 是唯一权威来源；Redis 不保存不可恢复状态。
- 字典、动态配置、元数据、编码规则、状态机和计量单位的发布，与对应 Outbox 消息写入同一数据库事务。
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

目标后端结构（每个分层项目内部按七模块组织，不创建七套项目）：

```text
src/backend/src/Services/ReferenceData
├── IndustrialPlatform.ReferenceData.Api
│   └── Modules/{Dictionary,Parameter,DynamicProperty,Metadata,CodingRule,StateMachine,UnitOfMeasure}
├── IndustrialPlatform.ReferenceData.Application
│   └── {Dictionary,Parameter,DynamicProperty,Metadata,CodingRule,StateMachine,UnitOfMeasure}
├── IndustrialPlatform.ReferenceData.Contracts
│   └── {Dictionary,Parameter,DynamicProperty,Metadata,CodingRule,StateMachine,UnitOfMeasure}
├── IndustrialPlatform.ReferenceData.Domain
│   └── {Dictionary,Parameter,DynamicProperty,Metadata,CodingRule,StateMachine,UnitOfMeasure}
└── IndustrialPlatform.ReferenceData.Infrastructure
    ├── Persistence
    ├── Messaging
    └── {Dictionary,Parameter,DynamicProperty,Metadata,CodingRule,StateMachine,UnitOfMeasure}

tests/ReferenceData
└── IndustrialPlatform.ReferenceData.Tests
    ├── Domain
    ├── Application
    ├── Infrastructure
    ├── Api
    └── Contracts
```

前端目标结构（接入现有平台入口，不另建第二套路由、权限、HTTP 或全局状态框架）：

```text
src/frontend/src
├── api/referenceData
├── pages/pc/referenceData
│   ├── dictionaries
│   ├── configurations
│   ├── dynamicProperties
│   ├── metadata
│   ├── codingRules
│   ├── stateMachines
│   └── unitsOfMeasure
├── router/routes.ts                         # 注册 ReferenceData 子路由
├── components/navigation/navigation.ts     # 注册菜单
└── permissions/catalog.ts                  # 注册权限常量
```

API DTO 类型优先与 `api/referenceData` 共置；页面状态默认保持局部或使用小型 composable，只有真实跨路由共享状态才增加 Pinia store。禁止建立七模块大 Store、第二个 Axios 实例、第二套路由/错误处理/权限系统或通用 CRUD、版本管理、动态表单引擎。

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

- 聚合和内部实体标识使用 `Guid`；租户与用户主体统一使用可信身份上下文的 `TenantNId`、`UserNId`。当前 BuildingBlocks/Identity 的旧租户属性命名偏移只能在宿主组合根临时适配，不得进入七模块公开契约、领域模型或持久化模型；平台级记录允许 `tenant_nid` 为空。
- 所有业务时间使用 `DateTimeOffset`，PostgreSQL 使用 `timestamptz` 并以 UTC 保存。
- 聚合继承 BuildingBlocks 当前 Entity 生命周期、软删除和 `OptimisticVersion + ConcurrencyVersion` 双版本并发模型。
- `referencedata_db` 是稳定的 `LogicalDatabaseName`，物理目标由 SystemData 的可信 `DatabaseTopology` 配置解析，业务 API 和 manifest 不接受地址、路径或凭据。Development 默认 `Shared`、可显式 `PerService`；Test/Staging/Production 只允许 `PerService`。
- Shared 只共享物理数据库，不合并服务或模块的数据所有权。PostgreSQL 固定使用一个 `reference_data` Schema，并以 `dictionary_*`、`parameter_*`、`dynamic_property_*`、`metadata_*`、`coding_rule_*`、`state_machine_*`、`unit_of_measure_*` 表前缀表达模块归属；SQLite 使用 `reference_data_dictionary_*` 等等价全名。七模块共享服务级 Migration/Ledger、带 `ModuleKey` 的 Outbox、连接与基础设施组件，Repository 和领域事务仍按模块隔离。
- DDD 按复杂度使用：发布版本、编码幂等和动态配置等复杂行为保留聚合与领域测试；简单 CRUD 与技术记录不强制完整聚合、领域事件或独立测试项目。
- 领域实体自身的稳定业务标识统一命名为 `NId`，禁止以 `Code` 表示实体业务标识；其他实体引用该业务标识时使用 `{EntityName}NId`，例如 `Material.NId` 与业务表中的 `MaterialNId`。
- `Code` 只保留给“已经生成的业务编码值”等确实表达编码结果、而非实体身份的字段；NId 保存规范化比较值，展示名称保留原始大小写，同一作用域内大小写不敏感唯一。
- 写请求不能从 Body 指定当前租户；`TenantNId/UserNId` 必须来自已验证 JWT/可信服务身份。平台级写入使用独立高权限声明，不与普通租户管理权限复用。
- 统一返回 `ApiResult<T>` / `PageResult<T>`；时间使用含 `Z` 或偏移量的 ISO 8601 字符串。
- 所有写接口执行模块独立权限校验和乐观并发；编码生成额外要求 `Idempotency-Key`。
- Parameter 可保存普通非敏感值；密码、Token、私钥、连接串或客户 IdP Secret 只保存受信 Secret Provider 的 `SecretRef`、版本标识和脱敏状态，普通查询不得解析或返回秘密本体。
- 每项任务执行 TDD，并记录命令、退出码、通过/失败/跳过数量、报告路径和外部环境限制。
- PF 级状态流转统一为 `设计待确认 → 待派遣 → 已派遣 → 开发中 → 待验收 → 已完成`。本文十个编号只表示 PF-03 内部进度步骤，当前均为“未开始”；它们不独立进入派遣、验收或提交状态，未经 PF-03 整体授权不得执行。

所有需要领域生命周期、软删除和双版本并发的领域实体直接继承 02 第 8 节定义的 BuildingBlocks Entity，公共字段和 PostgreSQL 列名固定为：

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

领域实体和领域表的“字段定义”“主要字段”只列当前对象的业务字段，不逐表重复上述九个 Entity 生命周期字段；完整映射由本节补齐。Migration/Seed Ledger、Outbox、配置历史、幂等记录和序列等技术记录只保留用途所需的标识、状态、时间、版本、重试或载荷字段，不机械继承 `Entity`、软删除或双版本并发，也不建立同义生命周期字段。

ReferenceData 聚合内父子表默认使用普通 `ParentId → Parent.Id` 外键，由聚合仓储从聚合根加载和保存子项，不为子项建立独立 Repository。只有某个聚合经实现证据证明必须由数据库传播父实体软删除/恢复状态时，才按蓝图 07 的条件性规则选择 `(Id, IsDeleted)` 复合外键；当前设计不预设该例外。技术记录不得使用复合软删除外键。

```text
DictionaryItem(DictionaryDefinitionId)
    → DictionaryDefinition(Id)

PostgreSQL:
dictionary_item(dictionary_definition_id)
    → dictionary_definition(id)
```

聚合查询先过滤有效聚合根，再随聚合加载子项；禁止绕过聚合根单独恢复或更新子项。跨模块只保存稳定 NId/Revision 并通过进程内公开契约校验，跨服务、跨数据库只保存 `{EntityName}NId` 和必要快照，不建立数据库外键。

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
- 现有字典/参数/动态配置/元数据/编码规则有效值解析沿用 `Factory → Tenant → Platform`，命中第一条已发布/启用记录即停止。StateMachine/UnitOfMeasure 的当前选择和固定来源读取按第 12A、12B 节，不对固定引用隐式回退或混合修订。
- PF-03 完整交付 Platform 和 Tenant。Factory 只保留契约扩展点，在 MasterData 提供权威工厂归属校验前，创建、更新和解析均保持禁用并返回 `REF-SCOPE-FACTORY-NOT-READY`；不得以旧 Phase 编号代替该依赖门禁。

## 7.2 发布型聚合

Dictionary、DynamicProperty、Metadata、CodingRule、StateMachine 和 UnitOfMeasure 的发布聚合统一使用：

```text
Draft → Published → Superseded
  │          └────→ Disabled
  └───────────────→ Disabled
```

- 只有 Draft 可编辑。
- 发布后内容不可原位覆盖；修改必须从已发布版本克隆新 Draft。
- 同一作用域、同一 NId 最多一个 Published 版本。
- 发布新版本时，旧 Published 在同一事务变为 Superseded。
- Disabled 不参与当前可用定义选择；曾发布的固定 Revision 仍可按明确来源读取，用于既有业务和回放。未曾发布的 Draft/Disabled 版本不向运行时开放。停用不是紧急禁止业务执行；后者由业务服务自己的控制负责。
- StateMachine、UnitOfMeasure 消费引用必须固定 `sourceScope + sourceTenantNId + NId + Revision`，不得只存 NId 后再按当前作用域回退；新发布或停用不自动迁移已有引用。首版不额外提供 IsHidden 开关。
- 发布状态不是 Entity.IsFrozen/IsLocked：Unfreeze/Unlock 不允许原位编辑 Published/Superseded 内容。配置发布生命周期采用固定领域代码，不能依赖用户配置的状态机启动自身。

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
DictionaryDefinitionId: Guid
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
- DictionaryItem 使用普通 `DictionaryDefinitionId → DictionaryDefinition.Id` 外键并作为 DictionaryDefinition 聚合内子项保存，不提供独立 Repository 或绕过聚合根的写入口。

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
│ ConfigurationAppDomainId、NId、         │
│ DataType、ValueMode、Value、             │
│ DefaultValue、IsMandatory、IsReadOnly   │
└─────────────────────┬────────────────────┘
                      │ 1:N，仅Multi
                      ▼
┌──────────────────────────────────────────┐
│ ConfigurationKeyMultiValue              │
│ 多值键明细                               │
│ ConfigurationKeyId、NId、               │
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
ConfigurationAppDomainId: Guid
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
ConfigurationKeyId: Guid
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
- 同一 Key 下启用明细的规范化 Value 不得重复；持久化 `canonical_value_hash` 并建立 `configuration_key_id + canonical_value_hash` 部分唯一索引。
- IsDefault 可有多条，表达多选默认集合；默认项必须同时 Enabled。
- 第一阶段每个 Multi Key 最多 1,000 条明细，读取按 Sort、NId 稳定排序。
- MultiValue 变更使用 AppDomain 聚合双版本令牌，不提供绕过聚合根的独立更新入口。
- ConfigurationKey 使用普通 `ConfigurationAppDomainId → ConfigurationAppDomain.Id` 外键；MultiValue 使用普通 `ConfigurationKeyId → ConfigurationKey.Id` 外键。两者均为 ConfigurationAppDomain 聚合内子项，不提供绕过聚合根的独立 Repository 或写入口。

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
reference_data.dynamic_property_definition
  ├── dynamic_property_field
  │     DynamicConfigDefinitionId、NId、DataType
  ├── dynamic_property_record
  │     DynamicConfigDefinitionId、NId、Sort、Enabled
  └── dynamic_property_value
        DynamicConfigDefinitionId、DynamicConfigRecordId、
        DynamicConfigFieldDefinitionId、对应类型值列
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
DynamicConfigDefinitionId: Guid
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
- Field 使用普通 `DynamicConfigDefinitionId → DynamicConfigDefinition.Id` 外键，并由 Definition 聚合统一维护。

## 10.5 DynamicConfigRecord

```text
DynamicConfigDefinitionId: Guid
NId: string
Name: string?
Category: string?
Sort: int
Enabled: bool
Values: DynamicConfigFieldValue[]
```

NId 是配置行的稳定业务标识，同一 Definition 修订内大小写不敏感唯一。Category 只用于分组和筛选，不决定字段结构；若不同类别需要完全不同的字段集合，应拆成不同 DynamicConfigDefinition，避免形成无法验证的稀疏万能表。

Record 使用普通 `DynamicConfigDefinitionId → DynamicConfigDefinition.Id` 外键，并由 Definition 聚合统一维护。

第一阶段单个 Revision 最多 10,000 条记录。运行时 API 必须分页，禁止默认一次返回完整数据集。字段或记录的任何变更都使用 Definition 聚合的双版本令牌并推进其版本，不为子项建立绕过聚合的独立并发入口。

## 10.6 DynamicConfigFieldValue强类型存储

```text
DynamicConfigDefinitionId: Guid
DynamicConfigRecordId: Guid
DynamicConfigFieldDefinitionId: Guid
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

- `(DynamicConfigRecordId, DynamicConfigFieldDefinitionId)` 唯一。
- Value 分别通过普通外键引用 Record 和 Field；同时通过包含 `DynamicConfigDefinitionId` 的领域复合唯一键/外键保证 Record 与 Field 属于同一 Definition 行及其 Revision，禁止跨修订拼接。该约束用于 Revision 一致性，不携带软删除影子列。
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
EntitySchemaId: Guid
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
Precision / Scale: int?
UnitDimensionNId: string?
DefaultUnitNId: string?
UnitRevision: int?
UnitSourceScope: Platform | Tenant?
UnitSourceTenantNId: string?
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
- AttributeDefinition 使用普通 `EntitySchemaId → EntitySchema.Id` 外键，并由 EntitySchema 聚合统一维护。
- Precision/Scale 仅适用 Decimal（Precision 1～28，Scale 0～12 且不超过 Precision）；字符串长度/Pattern 仅适用 String，数值上下界仅适用 Integer/Decimal；拒绝不适用类型的约束组合，不能静默忽略。
- 单位引用仅适用 Decimal：UnitDimensionNId、UnitRevision、UnitSourceScope 必须同时有值或同时为空；Tenant 来源还须携带当前租户，Platform 来源租户为空。未声明维度时 DefaultUnitNId/UnitSourceTenantNId 也必须为空。DefaultUnitNId 可省略，若填写必须属于该固定维度修订且可选。发布时经 UnitOfMeasure 公开进程内契约验证曾发布且当前可用于新绑定，不跨模块读表/建外键。
- 新增必填、收紧长度/范围/精度、改变类型或单位维度为不兼容变更，发布差异必须明确列出，调用方自行迁移业务值；不自动用 0/空串/日期填补必填，也不替下游完成数量换算。null、空串、0、false、空集合保持不同语义。

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

- 序列表以 `CodingRuleId` 和规则 Revision 关联 CodingRule；它是原子计数技术记录，不继承领域实体生命周期，也不携带软删除影子列。序列分区键由 CodingRuleId、规则 Revision、ResetPolicy 周期和上下文哈希组成。
- Preview 只校验并展示样例，不推进序列，也不创建幂等记录。
- Generate 必须携带 `Idempotency-Key`；同一租户、规则、键和相同请求重复调用返回同一编码。
- 相同 Idempotency-Key 对应不同请求摘要时返回 `REF-IDEMPOTENCY-CONFLICT`。
- 序列通过数据库原子更新/行锁保证唯一；Redis 分布式锁只可减少竞争，不作为正确性前提。
- 已生成编码不回收、不复用；规则发布新 Revision 后使用独立序列分区。
- Idempotency Record 在线保留 7 天；过期清理不影响编码审计和序列连续性。

---

# 12A. StateMachine 状态机定义详细设计

## 12A.1 最小聚合与边界

`StateMachineDefinition` 是聚合根，私有维护 `StateNode`、`StateTransition`；状态/动作先在本状态机修订内定义，不建设独立 StatusDefinition、ActionDefinition、BehaviorDefinition 模板中心。真实跨状态机语义复用需求出现后再评估提取，不能用 Dictionary 代替转换路径约束。

| 对象 | 业务字段 | 约束 |
| --- | --- | --- |
| StateMachineDefinition | `NId`、`Name`、`Description`、`ScopeType`、`TenantNId`、`Revision`、`Status`、`SourceRevision?`、`PublishedOn?`、`Nodes[]`、`Transitions[]` | 复用第 7 节发布模型；SourceRevision 仅记录同逻辑定义克隆来源 |
| StateNode | `StateMachineDefinitionId`、`NId`、`Name`、`Description?`、`IsInitial`、`IsTerminal`、`Outcome`、`Color?`、`Sort` | NId 表达状态身份，业务的 CurrentStatusNId 引用它；Outcome 为 None/Success/Failure/Skipped，仅作语义标记 |
| StateTransition | `StateMachineDefinitionId`、`FromStatusNId`、`ActionNId`、`ActionName`、`ToStatusNId`、`Description?` | 表达“在当前状态执行某动作后可到达的目标”，不携带脚本、业务守卫或可执行回调 |

发布不变量：

- 1～200 个节点、最多 1000 条转换；节点 NId 大小写不敏感唯一，恰好一个初始节点；所有节点应从初始节点可达。允许业务上的回路，不把状态机误当 DAG。
- 转换两端必须属于本聚合同一修订；`(FromStatusNId, ActionNId)` 唯一，首版一个状态下一个动作只有一个目标。禁止悬空转换；终态可以为 0～多个，但终态不得有出边（只有初始节点要求唯一）；同一 ActionNId 的显示名在本修订内保持一致。
- Color 只允许 `#RRGGBB` 或为空；Outcome、Color、Name 均不是权限、库存可用性或业务成功事实。
- 所有节点/转换写入经聚合根检查 Draft、冻结/锁定/软删除和根的预期双版本。冻结根后不能修改子项，不传播到引用该定义的业务实例。
- 发布内容不可原位变更，克隆产生新 Id/并发令牌并递增业务 Revision；新版本不自动迁移在途单据。首次无已发布来源的 Draft 不能被业务绑定。

## 12A.2 读取、校验和业务执行

当前选择支持 Platform 和 Tenant，返回两种来源的可选定义并明确标识来源，不默默混合节点或覆盖已绑定定义。新实例只选择 Published；按固定 `sourceScope + sourceTenantNId + StateMachineNId + stateMachineRevision` 读取曾发布版本，允许 Superseded/Disabled；租户来源必须等于可信当前租户。来源不存在、修订不存在或无权限时明确失败，不回退其他作用域/新版。

定义校验输入：固定来源与 Revision、`fromStatusNId`、`actionNId`。输出：`allowedByDefinition`、`toStatusNId?`、`reasonCode?` 和原定义引用。合法请求但无对应转换返回 200/false；缺失版本返回 404，非法字段返回 400。该查询只依据调用方提供的当前状态，不证明业务实例真的处于此状态，更不代表动作已被授权或执行成功。

业务服务持有 `StateMachineNId + stateMachineRevision + sourceScope + sourceTenantNId + CurrentStatusNId`，负责自己的权限、业务前置条件、并发检查、数量/流水/状态历史及事务事件。需要多个独立状态维度时，由业务模型定义具名关联，不给所有 Entity 强塞单个 Status。禁止跨服务通用 `SetStatus` 和把工单/库存实例放进 ReferenceData。

同宿主可经进程内公开契约查询；跨服务可预取固定修订快照，在业务事务中依据该快照验证路径并推进状态。只读快照不是运行状态权威，也不要求业务数据库事务中同步等待远程校验；业务服务最终须在同一事务内重检自身当前状态、执行业务写入与状态推进。业务回滚不得提前留下成功状态。

## 12A.3 首版不做

不建设通用工作流/审批/调度引擎、任意表达式守卫、动作插件、状态行为脚本、跨服务事务或自动在途迁移。不增加可以关闭必要审计/业务事件的 DoRaiseEvent 开关；业务事件由拥有该事实的业务服务决定。配置自身的发布状态与业务状态机独立。

---

# 12B. UnitOfMeasure 计量单位详细设计

## 12B.1 维度聚合与单位

`UnitDimension` 是发布型聚合根，整份修订包含本维度的 `UnitDefinition` 子项。每个单位只定义到唯一基准单位的参数；不建任意两两 UnitConversion 图、独立 UoMFactor 聚合或公式引擎。

| 对象 | 业务字段 | 约束 |
| --- | --- | --- |
| UnitDimension | `NId`、`Name`、`Description`、`ScopeType`、`TenantNId`、`Revision`、`Status`、`SourceRevision?`、`PublishedOn?`、`IsSystemDefined`、`ConversionKind`、`BaseUnitNId`、`Units[]` | ConversionKind 为 Ratio/AbsoluteTemperature；每修订唯一基准单位；整个维度原子发布 |
| UnitDefinition | `UnitDimensionId`、`NId`、`Name`、`Symbol`、`FactorToBase`、`OffsetToBase`、`DecimalPlaces`、`RoundingMode`、`Enabled`、`Sort` | 本维度内 NId 大小写不敏感唯一；Symbol 只作显示；DecimalPlaces 为 0～12；RoundingMode 为 ToEven/AwayFromZero |

不变量与来源：

- 因子必须大于 0；Ratio 的偏移必须为 0，只有 AbsoluteTemperature 允许偏移。基准单位必须存在、启用且 FactorToBase=1、OffsetToBase=0；每维度最多 200 个单位。
- Platform 预置质量、长度、时间、体积、计数、绝对温度及必要单位（如 kg/g、m/mm、s/min、L/mL、piece、K/degC/degF）。可信幂等种子设置 IsSystemDefined；普通请求不接收该字段。首版系统预置整体只读，普通管理 API 不允许修改显示字段（含 Name/Description）、结构、单位子项、CloneRevision/Publish/Disable/Delete；不论持有何种普通管理权限均执行保护。预置修订仅由显式版本化 seed/migration 升级，不覆盖已发布历史。
- Tenant 可维护自己的维度及单位；当前选择列表标注 Platform/Tenant 来源，不允许租户同名定义隐式替换平台标准。引用必须含来源、UnitDimensionNId、UnitNId、unitRevision；unitRevision 指整份 UnitDimension.Revision，而非子项独立版本。
- 修改单位结构、换算参数、精度或启用状态须克隆维度新 Draft 后整份发布。需要基于标准扩展时，显式创建新的租户定义 NId 并复制所需单位值，IsSystemDefined=false；这不是修改平台定义或同名覆盖，也不复制旧 Id/并发令牌。CloneRevision 只处理同一来源/逻辑定义的修订。
- 新业务选择 Published 且 Enabled 的单位；固定来源/Revision 可读取曾发布的 Superseded/Disabled 定义。新修订中停用单位不影响旧修订下的历史显示和解释；已发布/被引用定义不提供物理删除入口。

## 12B.2 换算与精度

同一来源、维度、修订内采用唯一基准路径：

```text
baseValue = sourceValue × source.FactorToBase + source.OffsetToBase
targetValue = (baseValue - target.OffsetToBase) / target.FactorToBase
```

服务端使用 decimal/numeric 运算，因子/偏移按可表达精度保存；禁止二进制浮点承担数量权威。新单位契约中的数量、因子、偏移、换算结果均用十进制字符串传输，前端不先转 JavaScript Number。正整数小数位和 Revision 仍为 JSON 整数。

中间计算不按展示精度逐段舍入，只在最终目标单位按 DecimalPlaces/RoundingMode 舍入；返回原数量、结果、固定定义引用、实际因子/偏移/舍入快照和 `wasRounded`。除不尽的因子/除法遵循 decimal 的有限精度，不承诺任意数学实数的无误差表示；数值越界明确返回错误，不截断溢出。业务若要求法规级更高精度，须另行扩展契约，不能静默降精度。

首版提供同维度比例换算及绝对温度换算，不支持温差与绝对温度混用、任意指数或符号公式。不能从 kg 直接转 L；密度、浓度、包装等额外上下文属于业务规则。库存/BOM/称量消费方还须限制适用量纲和数量语义，不能把任意可换算的量用于库存累加。

## 12B.3 与 MasterData、Metadata 的分工

- ReferenceData 是通用维度、单位和换算定义的唯一权威；MasterData 不再维护另一份 Unit/UnitConversion CRUD、权威表或换算中心。
- MasterData 保存物料的基础/采购等单位选择，以及因物料而异的箱/件比例、包装规则和上下文换算；引用通用定义和必要修订/计算快照。全球不存在一个通用的“1 箱=若干件”。
- OperationalData/Weighting 等持有实际数量、输入单位及换算快照，历史流水不可因新单位版本重算；业务余额、库存冻结与过账不归本模块。
- Metadata 可在 Decimal 属性上声明 `UnitDimensionNId`、`DefaultUnitNId?`、`UnitRevision`、`UnitSourceScope`、`UnitSourceTenantNId?`；整组引用校验见第 11 节。实际数量/单位仍保存在业务实例。

---
# 13. 数据与持久化设计

稳定逻辑数据库（物理目标由 SystemData 的可信 `DatabaseTopology` 解析）：

```text
referencedata_db
```

PostgreSQL 固定使用一个 `reference_data` Schema。下表只列主要职责；领域实体是否应用 Entity 生命周期、技术记录采用哪些最小字段，均按第 6 节判断，不机械补齐：

| 表 | 主要内容 | 关键约束/索引 |
| --- | --- | --- |
| `reference_data.dictionary_definition`、`reference_data.dictionary_item` | 字典定义、修订、项与发布 | 普通聚合内外键；作用域+规范化 NId+Revision 唯一 |
| `reference_data.parameter_app_domain`、`reference_data.parameter_key`、`reference_data.parameter_multi_value`、`reference_data.parameter_history` | 参数应用域、键、单值/多值和领域历史 | 按 Key 整体覆盖；历史只追加；Secret 仅保存 SecretRef |
| `reference_data.dynamic_property_definition`、`reference_data.dynamic_property_field`、`reference_data.dynamic_property_record`、`reference_data.dynamic_property_value` | `DynamicConfigDefinition` 完整 EAV 聚合 | 同 Revision 约束；强类型值 Check Constraint；整份原子发布 |
| `reference_data.metadata_entity_schema`、`reference_data.metadata_attribute_definition` | EntitySchema 完整快照与属性定义 | 属性只属于同一 Schema 修订；发布生成兼容性差异摘要 |
| `reference_data.coding_rule_definition`、`reference_data.coding_rule_sequence`、`reference_data.coding_rule_idempotency_record` | 规则、原子序列与幂等响应 | `TenantNId+RuleNId+IdempotencyKey` 幂等；Preview 不占号 |
| `reference_data.state_machine_definition`、`reference_data.state_machine_node`、`reference_data.state_machine_transition` | StateMachineDefinition 修订与私有节点/转换 | 作用域+NId+Revision 唯一；节点唯一；根内 FromStatusNId+ActionNId 唯一；所有子项随根写入 |
| `reference_data.unit_of_measure_dimension`、`reference_data.unit_of_measure_unit` | UnitDimension 修订及到基准单位的换算参数 | 作用域+维度NId+Revision 唯一；修订内单位 NId 唯一；正因子/精度范围；唯一基准由聚合发布校验 |
| `reference_data.outbox_message` | ReferenceData 事件发布 | 单一服务级 Outbox；`ModuleKey` 标识逻辑模块归属 |
| `reference_data.schema_migrations`、`reference_data.seed_ledger` | ReferenceData 初始化事实 | 单一服务级迁移流与账本；七模块不是独立初始化单元 |

上述命名中点号左侧是唯一 PostgreSQL Schema，点号右侧以模块前缀表达逻辑所有权；SQLite 使用 `reference_data_dictionary_*` 等等价全名。领域聚合根和有独立生命周期的领域实体遵守第 6 节公共字段；聚合子项使用必要的普通外键且只能随聚合根写入。技术记录使用最小字段，不软删除、不冻结、不套用双版本。模块之间不得建立外键，跨模块引用只保存稳定 NId/Revision 并通过进程内公开 Application 契约校验。

迁移规则：

- 迁移文件按任务顺序追加，禁止修改已在共享环境执行的迁移。
- 全服务只有一个迁移流、一个 migration ledger 和一个 initializer/readiness 汇总；迁移内容可按模块目录组织，但不得建立七套 ledger、编号空间或初始化器。
- PostgreSQL 大小写不敏感唯一使用规范化列或函数索引，不依赖默认排序规则。
- 平台级空租户的唯一约束使用明确的部分唯一索引，避免 `NULL` 导致重复。
- 发布切换、动态配置四表快照、配置历史、编码生成和 Outbox 均使用本地事务；禁止分布式事务。
- 本阶段创建的 EAV 值表只保存 ReferenceData 自有配置记录；不创建业务实体属性值表、低代码页面表或跨服务外键。

## 13.1 SystemData 数据库编排消费与 readiness

ReferenceData 只消费 SystemData 的拓扑与编排契约，不重复实现控制面。宿主登记 `ServiceKey=referencedata`、`InitializationUnitKey=referencedata`、`Provider`、稳定 `LogicalDatabaseName=referencedata_db`、Owner、DesiredVersion 和 `Standard|Advanced` 策略；七个逻辑模块默认组成一个服务级初始化单元。物理目标由可信 `DatabaseTopology` 解析。

初始化 Operation 固定为：`SystemData 解析拓扑/策略 → ReferenceData initializer Inspect → Plan → Apply → Verify → 脱敏 Observation`。ReferenceData initializer 自己执行服务级 Migration、Seed、Bootstrap 与 Verify，并维护服务级 Ledger。

日常启动与 readiness 只读取 `referencedata_db` 身份、`reference_data.schema_migrations`、`reference_data.seed_ledger` 和必要 Bootstrap 本地事实。SystemData 不可用时，已经初始化且本地事实有效的 ReferenceData 仍可 Ready；目标错误、迁移失败或版本不一致时整个宿主保持 `NotReady`，但 liveness 仍可 Healthy。Redis 故障时运行时读取回源 PostgreSQL，RabbitMQ 故障时 Outbox 保留待发布消息，Seq/追踪后端故障只降低可观测性；这些依赖进入 capability health 并报告 `Degraded`，不机械阻断整个宿主 Ready。

Shared 物理目标只解析一次；ReferenceData initializer 对服务级 DDL 使用 PostgreSQL advisory lock 或等效锁串行化。ReferenceData 不持有建库管理员凭据、不使用 `EnsureCreated`，也不在远程失败时回退 SQLite、旧 Schema 或错误数据库。拓扑变化不隐式 copy、rename、merge 或 split；Advanced 策略下的 drift 走显式迁移/import。

---

# 14. Application用例设计

| 能力 | Commands | Queries |
| --- | --- | --- |
| 字典 | CreateDraft、UpdateDraft、Add/Update/DisableItem、CloneRevision、Publish、Disable | GetAdminDetail、SearchAdmin、GetEffectiveDictionary |
| 参数配置 | Create/Update/Enable/DisableAppDomain、Add/Update/Enable/DisableKey、Add/Update/Enable/DisableMultiValue | SearchAppDomains、GetAppDomainDetail、GetKeyHistory、ResolveConfigurationKey、ResolveConfigurationDomain |
| 动态配置 | CreateDraft、UpdateFields、Add/Update/DisableRecord、CloneRevision、Publish、Disable | SearchAdmin、GetAdminDetail、GetEffectiveDynamicConfigSchema、GetEffectiveRecords、GetEffectiveRecord |
| 元数据 | CreateDraft、UpdateDraft、Add/Update/RemoveAttribute、CloneRevision、Publish、Disable | SearchAdmin、GetAdminDetail、GetEffectiveSchema |
| 编码规则 | CreateDraft、UpdateDraft、CloneRevision、Publish、Disable、GenerateCode | SearchAdmin、GetAdminDetail、PreviewCode、GetEffectiveRule |
| 状态机 | CreateDraft、UpdateDraft（节点/转换整体保存）、CloneRevision、Publish、Disable | SearchAdmin、GetAdminDetail、ListAvailable、GetCurrent、GetRevision、EvaluateTransition |
| 计量单位 | CreateDraft、UpdateDraft（维度/单位整体保存）、CloneRevision、Publish、Disable | SearchAdmin、GetAdminDetail、ListAvailable、GetCurrent、GetRevision、ConvertQuantity |

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

查询默认排除软删除；Admin 查询可查看 Draft、Superseded 和 Disabled。运行时当前选择只返回 Published/Active；状态机、单位按固定来源读取曾发布 Revision 的例外见第 7、12A、12B 节。EvaluateTransition/ConvertQuantity 都是无副作用查询，不能通过调用它们创建业务状态或历史记录。

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
| GET | `/admin/dictionaries` | Query → `PageResult<DictionarySummaryDto>` | `referencedata.dictionary.view` |
| POST | `/admin/dictionaries` | `CreateDictionaryRequest` → `DictionaryDetailDto`，201 | `referencedata.dictionary.create` |
| GET | `/admin/dictionaries/{id}` | → `DictionaryDetailDto` | `referencedata.dictionary.view` |
| PUT | `/admin/dictionaries/{id}` | `UpdateDictionaryRequest` → `DictionaryDetailDto` | `referencedata.dictionary.update` |
| POST | `/admin/dictionaries/{id}/clone` | 并发版本 → 新 Draft | `referencedata.dictionary.create` |
| POST | `/admin/dictionaries/{id}/publish` | 并发版本 → Published | `referencedata.dictionary.publish` |
| POST | `/admin/dictionaries/{id}/disable` | 并发版本+原因 → Disabled | `referencedata.dictionary.disable` |
| GET | `/admin/configuration-domains` | Query → `PageResult<ConfigurationAppDomainSummaryDto>` | `referencedata.parameter.view` |
| POST | `/admin/configuration-domains` | `CreateConfigurationAppDomainRequest` → `ConfigurationAppDomainDetailDto`，201 | `referencedata.parameter.create` |
| GET | `/admin/configuration-domains/{id}` | → `ConfigurationAppDomainDetailDto` | `referencedata.parameter.view` |
| PUT | `/admin/configuration-domains/{id}` | `UpdateConfigurationAppDomainRequest` → `ConfigurationAppDomainDetailDto` | `referencedata.parameter.update` |
| POST | `/admin/configuration-domains/{id}/enable` | `ChangeConfigurationStateRequest` → Domain DTO | `referencedata.parameter.update` |
| POST | `/admin/configuration-domains/{id}/disable` | `ChangeConfigurationStateRequest` → Domain DTO | `referencedata.parameter.disable` |
| POST | `/admin/configuration-domains/{id}/keys` | `CreateConfigurationKeyRequest` → `ConfigurationKeyDto`，201 | `referencedata.parameter.create` |
| PUT | `/admin/configuration-domains/{id}/keys/{keyId}` | `UpdateConfigurationKeyRequest` → `ConfigurationKeyDto` | `referencedata.parameter.update` |
| POST | `/admin/configuration-domains/{id}/keys/{keyId}/enable` | `ChangeConfigurationStateRequest` → Key DTO | `referencedata.parameter.update` |
| POST | `/admin/configuration-domains/{id}/keys/{keyId}/disable` | `ChangeConfigurationStateRequest` → Key DTO | `referencedata.parameter.disable` |
| POST | `/admin/configuration-domains/{id}/keys/{keyId}/values` | `CreateConfigurationMultiValueRequest` → MultiValue DTO，201 | `referencedata.parameter.create` |
| PUT | `/admin/configuration-domains/{id}/keys/{keyId}/values/{valueId}` | `UpdateConfigurationMultiValueRequest` → MultiValue DTO | `referencedata.parameter.update` |
| POST | `/admin/configuration-domains/{id}/keys/{keyId}/values/{valueId}/enable` | `ChangeConfigurationStateRequest` → MultiValue DTO | `referencedata.parameter.update` |
| POST | `/admin/configuration-domains/{id}/keys/{keyId}/values/{valueId}/disable` | `ChangeConfigurationStateRequest` → MultiValue DTO | `referencedata.parameter.disable` |
| GET | `/admin/configuration-domains/{id}/keys/{keyId}/history` | → `PageResult<ConfigurationHistoryDto>` | `referencedata.parameter.view` |
| GET | `/admin/dynamic-properties/configurations` | Query → `PageResult<DynamicConfigSummaryDto>` | `referencedata.dynamic-property.view` |
| POST | `/admin/dynamic-properties/configurations` | `CreateDynamicConfigRequest` → `DynamicConfigDetailDto`，201 | `referencedata.dynamic-property.create` |
| GET | `/admin/dynamic-properties/configurations/{id}` | → `DynamicConfigDetailDto` | `referencedata.dynamic-property.view` |
| PUT | `/admin/dynamic-properties/configurations/{id}` | `UpdateDynamicConfigRequest` → `DynamicConfigDetailDto` | `referencedata.dynamic-property.update` |
| POST | `/admin/dynamic-properties/configurations/{id}/records` | `CreateDynamicConfigRecordRequest` → `DynamicConfigRecordDto`，201 | `referencedata.dynamic-property.update` |
| PUT | `/admin/dynamic-properties/configurations/{id}/records/{recordId}` | `UpdateDynamicConfigRecordRequest` → `DynamicConfigRecordDto` | `referencedata.dynamic-property.update` |
| POST | `/admin/dynamic-properties/configurations/{id}/records/{recordId}/disable` | `PublishOrDisableRequest` → `DynamicConfigRecordDto` | `referencedata.dynamic-property.disable` |
| POST | `/admin/dynamic-properties/configurations/{id}/clone` | `PublishOrDisableRequest` → 新 Draft | `referencedata.dynamic-property.create` |
| POST | `/admin/dynamic-properties/configurations/{id}/publish` | `PublishOrDisableRequest` → Published | `referencedata.dynamic-property.publish` |
| POST | `/admin/dynamic-properties/configurations/{id}/disable` | `PublishOrDisableRequest` → Disabled | `referencedata.dynamic-property.disable` |
| GET | `/admin/metadata-schemas` | Query → `PageResult<MetadataSchemaSummaryDto>` | `referencedata.metadata.view` |
| POST | `/admin/metadata-schemas` | `CreateMetadataSchemaRequest` → `MetadataSchemaDetailDto`，201 | `referencedata.metadata.create` |
| GET | `/admin/metadata-schemas/{id}` | → `MetadataSchemaDetailDto` | `referencedata.metadata.view` |
| PUT | `/admin/metadata-schemas/{id}` | `UpdateMetadataSchemaRequest` → `MetadataSchemaDetailDto` | `referencedata.metadata.update` |
| POST | `/admin/metadata-schemas/{id}/clone` | `PublishOrDisableRequest` → 新 Draft | `referencedata.metadata.create` |
| POST | `/admin/metadata-schemas/{id}/publish` | `PublishOrDisableRequest` → Published | `referencedata.metadata.publish` |
| POST | `/admin/metadata-schemas/{id}/disable` | `PublishOrDisableRequest` → Disabled | `referencedata.metadata.disable` |
| GET | `/admin/coding-rules` | Query → `PageResult<CodingRuleSummaryDto>` | `referencedata.coding-rule.view` |
| POST | `/admin/coding-rules` | `CreateCodingRuleRequest` → `CodingRuleDetailDto`，201 | `referencedata.coding-rule.create` |
| GET | `/admin/coding-rules/{id}` | → `CodingRuleDetailDto` | `referencedata.coding-rule.view` |
| PUT | `/admin/coding-rules/{id}` | `UpdateCodingRuleRequest` → `CodingRuleDetailDto` | `referencedata.coding-rule.update` |
| POST | `/admin/coding-rules/{id}/clone` | `PublishOrDisableRequest` → 新 Draft | `referencedata.coding-rule.create` |
| POST | `/admin/coding-rules/{id}/publish` | `PublishOrDisableRequest` → Published | `referencedata.coding-rule.publish` |
| POST | `/admin/coding-rules/{id}/disable` | `PublishOrDisableRequest` → Disabled | `referencedata.coding-rule.disable` |
| POST | `/admin/coding-rules/{id}/preview` | `PreviewCodeRequest` → `CodePreviewDto` | `referencedata.coding-rule.preview` |

新增两模块管理 API（均在上述服务内部前缀下）：

| Method | 路径 | 请求/响应 | 权限 |
| --- | --- | --- | --- |
| GET | `/admin/state-machines` | Query → `PageResult<StateMachineSummaryDto>` | `referencedata.state-machine.view` |
| POST | `/admin/state-machines` | `CreateStateMachineRequest` → `StateMachineDetailDto`，201 | `referencedata.state-machine.create` |
| GET | `/admin/state-machines/{id}` | → `StateMachineDetailDto` | `referencedata.state-machine.view` |
| PUT | `/admin/state-machines/{id}` | `UpdateStateMachineRequest` → Detail DTO | `referencedata.state-machine.update` |
| POST | `/admin/state-machines/{id}/clone` | `PublishOrDisableRequest` → 新 Draft | `referencedata.state-machine.create` |
| POST | `/admin/state-machines/{id}/publish` | `PublishOrDisableRequest` → Published | `referencedata.state-machine.publish` |
| POST | `/admin/state-machines/{id}/disable` | `PublishOrDisableRequest` → Disabled | `referencedata.state-machine.disable` |
| GET | `/admin/units-of-measure/dimensions` | Query → `PageResult<UnitDimensionSummaryDto>` | `referencedata.unit-of-measure.view` |
| POST | `/admin/units-of-measure/dimensions` | `CreateUnitDimensionRequest` → `UnitDimensionDetailDto`，201 | `referencedata.unit-of-measure.create` |
| GET | `/admin/units-of-measure/dimensions/{id}` | → `UnitDimensionDetailDto` | `referencedata.unit-of-measure.view` |
| PUT | `/admin/units-of-measure/dimensions/{id}` | `UpdateUnitDimensionRequest` → Detail DTO | `referencedata.unit-of-measure.update` |
| POST | `/admin/units-of-measure/dimensions/{id}/clone` | `PublishOrDisableRequest` → 新 Draft | `referencedata.unit-of-measure.create` |
| POST | `/admin/units-of-measure/dimensions/{id}/publish` | `PublishOrDisableRequest` → Published | `referencedata.unit-of-measure.publish` |
| POST | `/admin/units-of-measure/dimensions/{id}/disable` | `PublishOrDisableRequest` → Disabled | `referencedata.unit-of-measure.disable` |

两个新模块均只经根保存子项；没有独立节点/转换/换算 Repository 或绕过根的管理 API。单位平台预置保护在应用和领域写入口执行，持有普通 update/disable 权限也不能绕过。

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
              pattern, dictionaryNId, referenceTarget, precision, scale,
              unitDimensionNId, defaultUnitNId, unitRevision, unitSourceScope,
              unitSourceTenantNId, description}],
  Update额外包含 expectedOptimisticVersion, expectedConcurrencyVersion

CreateCodingRuleRequest / UpdateCodingRuleRequest
  scopeType, nId, name, targetEntityNId, template, resetPolicy,
  Update额外包含 expectedOptimisticVersion, expectedConcurrencyVersion

CreateStateMachineRequest / UpdateStateMachineRequest
  Create: scopeType, nId, name, description,
  nodes[{nId, name, description, isInitial, isTerminal, outcome, color, sort}],
  transitions[{fromStatusNId, actionNId, actionName, toStatusNId, description}]
  Update: name, description, nodes[], transitions[],
          expectedOptimisticVersion, expectedConcurrencyVersion

CreateUnitDimensionRequest / UpdateUnitDimensionRequest
  Create: scopeType, nId, name, description, conversionKind, baseUnitNId,
  units[{nId, name, symbol, factorToBase, offsetToBase, decimalPlaces,
         roundingMode, enabled, sort}]
  Update: name, description, conversionKind, baseUnitNId, units[],
          expectedOptimisticVersion, expectedConcurrencyVersion
  不接收 isSystemDefined、tenantNId、revision、status 或任意服务端保护字段

PublishOrDisableRequest
  expectedOptimisticVersion, expectedConcurrencyVersion, changeReason
```

所有写响应必须返回 `id`、`status`、业务 Revision、`optimisticVersion`、`concurrencyVersion` 和 `lastUpdatedOn`；Create 返回 201，Update/Clone/Publish/Disable 返回 200。

## 15.3 运行时API

| Method | 服务内部路径 | Response | 权限/调用约束 |
| --- | --- | --- | --- |
| GET | `/dictionaries/{nId}` | `EffectiveDictionaryDto` | `referencedata.dictionary.view` |
| GET | `/configuration-domains/{appDomainNId}` | `EffectiveConfigurationDomainDto` | `referencedata.parameter.view`；最多500个Key |
| GET | `/configuration-domains/{appDomainNId}/keys/{keyNId}` | `EffectiveConfigurationDto` | `referencedata.parameter.view` |
| GET | `/dynamic-properties/configurations/{nId}/schema` | `EffectiveDynamicConfigSchemaDto` | `referencedata.dynamic-property.view` |
| GET | `/dynamic-properties/configurations/{nId}/records?revision={revision}` | `PageResult<DynamicConfigRecordDto>` | `referencedata.dynamic-property.view`；Revision 必填且必须分页 |
| GET | `/dynamic-properties/configurations/{nId}/records/{recordNId}?revision={revision}` | `DynamicConfigRecordDto` | `referencedata.dynamic-property.view`；Revision 必填 |
| GET | `/metadata-schemas/{nId}` | `EffectiveSchemaDto` | `referencedata.metadata.view` |
| POST | `/coding-rules/{nId}/preview` | `CodePreviewDto`，不推进序列 | `referencedata.coding-rule.preview` |
| POST | `/coding-rules/{nId}/generate` | `GeneratedCodeDto` | `referencedata.coding-rule.generate`；必须有 Idempotency-Key |

状态机/单位的运行时 API：

| Method | 服务内部路径 | Response | 权限/调用约束 |
| --- | --- | --- | --- |
| GET | `/state-machines` | `PageResult<StateMachineSummaryDto>`，可选列表，标明来源 | `referencedata.state-machine.view` |
| GET | `/state-machines/{nId}` | `StateMachineDefinitionDto`，当前已发布 | `referencedata.state-machine.view`；sourceScope 必填 |
| GET | `/state-machines/{nId}/revisions/{revision}` | `StateMachineDefinitionDto`，固定修订 | `referencedata.state-machine.view`；sourceScope 必填 |
| POST | `/state-machines/{nId}/evaluate` | `TransitionEvaluationDto`，仅判定义路径 | `referencedata.state-machine.view`；revision/sourceScope/fromStatusNId/actionNId 必填 |
| GET | `/units-of-measure/dimensions` | `PageResult<UnitDimensionSummaryDto>`，可选列表，标明来源 | `referencedata.unit-of-measure.view` |
| GET | `/units-of-measure/dimensions/{nId}` | `UnitDimensionDto`，当前已发布 | `referencedata.unit-of-measure.view`；sourceScope 必填 |
| GET | `/units-of-measure/dimensions/{nId}/revisions/{revision}` | `UnitDimensionDto`，固定修订 | `referencedata.unit-of-measure.view`；sourceScope 必填 |
| POST | `/units-of-measure/convert` | `UnitConversionResultDto`，无副作用 | `referencedata.unit-of-measure.view`；固定来源、unitDimensionNId/unitRevision/fromUnitNId/toUnitNId/value 必填 |

固定来源请求的 GET 参数放 Query，POST 放 Body：sourceScope 必填；sourceScope=Platform 时 sourceTenantNId 必须省略或为 null，sourceScope=Tenant 时 sourceTenantNId 必须显式填写且与可信当前租户一致，缺少返回 400，不一致按租户隔离拒绝；服务端不替调用方猜测来源。可选列表可不指定来源，但只能列平台及当前租户可见项。状态机/单位的当前列表不暴露 Draft，历史端点及两个计算查询只能使用曾发布的固定修订。量值 value 使用十进制字符串。两个 POST 是有权限的只读计算，不产生事务状态、占号或写入幂等记录。

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

StateMachineDefinitionDto
  nId, name, sourceScope, sourceTenantNId, revision, status, publishedOn,
  nodes[], transitions[]
TransitionEvaluationDto
  stateMachineNId, stateMachineRevision, sourceScope, sourceTenantNId,
  fromStatusNId, actionNId, allowedByDefinition, toStatusNId?, reasonCode?

UnitDimensionDto
  nId, name, sourceScope, sourceTenantNId, revision, status, publishedOn,
  isSystemDefined, conversionKind, baseUnitNId, units[]
UnitConversionResultDto
  unitDimensionNId, unitRevision, sourceScope, sourceTenantNId,
  fromUnitNId, toUnitNId, inputValue, resultValue, wasRounded,
  conversionSnapshot{sourceFactorToBase, sourceOffsetToBase, targetFactorToBase,
                     targetOffsetToBase, decimalPlaces, roundingMode}
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
ReferenceStateMachineChangedV1
ReferenceUnitDimensionChangedV1
```

Exchange 与路由键固定为：

```text
exchange: industrial.system

industrial.reference-data.dictionary.published.v1
industrial.reference-data.configuration.changed.v1
industrial.reference-data.dynamic-configuration.published.v1
industrial.reference-data.metadata.published.v1
industrial.reference-data.coding-rule.published.v1
industrial.reference-data.state-machine.changed.v1
industrial.reference-data.unit-of-measure.changed.v1
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

新增 `ReferenceStateMachineChangedV1`、`ReferenceUnitDimensionChangedV1` 还包含 `changeType=Published|Disabled`、`optimisticVersion`；停用与发布都写入同一服务级 Outbox，使当前选择缓存失效。相同业务 Revision 内仍可能先发布后停用，因此这两个 Changed 事件按来源+subjectNId+Revision+optimisticVersion 判断新旧，不能仅因 Revision 相同就丢弃停用事件；旧 Revision 的状态变更最多失效对应固定修订状态缓存，不能覆盖新 current 内容。它们不等于业务实例状态变更或实际数量换算事件，不能驱动消费者自动迁移绑定版本。

事件只包含定位、版本和缓存失效所需字段，不携带完整配置值、字典内容或敏感信息。消费者收到事件后按 API 拉取对应定义；重复事件按 EventId 去重，原发布型事件按 Revision 判序，新增两个 Changed 事件使用上述 Revision+optimisticVersion 规则。事件 tenantNId/scopeType 表达定义所属来源，不是消费者业务记录的租户。

全服务只维护 `reference_data.outbox_message` 一张 Outbox 表和一个 Dispatcher，记录 `ModuleKey`、事件名、聚合标识、Revision、载荷、状态、尝试次数和下次重试时间。当前 ReferenceData 没有真实入站事件消费者，不创建 Inbox 或消费位点；未来出现真实消费者时，先补充消费契约与幂等需求，再增加一套服务级 Inbox/Checkpoint。Dispatcher 每秒轮询一次、每批最多 100 条；失败按 1、2、4、8、16、30、60、120、300 秒退避，累计 10 次失败后保留消息并触发告警，不自动丢弃。消息发布失败不回滚已提交业务事务。事件字段新增保持向后兼容，删除或改义必须发布 V2。

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
referencedata:v1:{sourceScope}:{sourceTenantKey}:state-machine:{normalizedNId}:{currentOrRevision}
referencedata:v1:{sourceScope}:{sourceTenantKey}:unit-of-measure:{dimensionNId}:{currentOrRevision}
```

全服务复用一个 Redis 连接与一套 Cache Aside 适配器，七模块通过逻辑键前缀和模块 Revision 隔离，不设置会造成跨模块联动失效的全局 `ReferenceDataRevision`。建议 TTL：字典 30 分钟、参数配置 Key/Domain 5 分钟、DynamicProperty Schema 15 分钟、动态配置记录页 5 分钟、元数据 15 分钟、编码规则 15 分钟；AppDomain/Key/MultiValue 修改提交成功后失效整个逻辑 AppDomain 的 Key 和 Domain 缓存，其他能力按 NId 失效。缓存值必须包含 Revision，禁止永久缓存“不存在”。状态机/单位当前选择缓存 TTL 为 5 分钟、固定修订 30 分钟；发布/停用清理对应 current/列表/详情状态缓存，已发布内容的 Revision 快照不被新版本替换。缓存键必须区分 Platform/Tenant 与实际来源租户；计算结果无须另建全局缓存。

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
- 状态机与单位固定来源和 Revision 后才可被业务绑定；通用定义归 ReferenceData，业务实例/事务和物料专属比例分别留在对应业务服务/MasterData。Metadata 的 Decimal 单位约束通过 UnitOfMeasure 公开契约验证，禁止跨模块外键。
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
    ├── 编码规则
    ├── 状态机定义
    └── 计量单位
```

“参考数据”按现有 navigation group/section/item 模型实现为一个 section 和七个 item；不改造导航树、折叠父菜单或动态菜单框架。

| 路由 | 页面 | 路由权限 |
| --- | --- | --- |
| `/pc/system/reference-data/dictionaries` | 字典列表/详情 | `referencedata.dictionary.view` |
| `/pc/system/reference-data/configurations` | 配置应用域/键/多值 | `referencedata.parameter.view` |
| `/pc/system/reference-data/dynamic-properties` | DynamicProperty 模块；编辑 DynamicConfigDefinition 字段/记录/值 | `referencedata.dynamic-property.view` |
| `/pc/system/reference-data/metadata` | Schema 列表/编辑 | `referencedata.metadata.view` |
| `/pc/system/reference-data/coding-rules` | 编码规则列表/预览 | `referencedata.coding-rule.view` |
| `/pc/system/reference-data/state-machines` | 状态机、节点、转换与定义校验 | `referencedata.state-machine.view` |
| `/pc/system/reference-data/units-of-measure` | 维度、单位与换算试算 | `referencedata.unit-of-measure.view` |

按钮通过现有 `PermissionGate` 与权限目录常量控制七模块各自的 view/create/update/publish/disable 权限；CodingRule 另有 preview/generate，Parameter 另有 read-secret-reference。前端隐藏按钮不替代服务端授权。七个页面使用现有 PC 布局和页面组件，不增加 PDA/Mobile 管理路由，也不建设统一审计页面。

前端 API 层必须通过现有 `httpClient` 暴露有类型的方法和统一错误对象，不允许页面直接创建 Axios 实例。路由注册到现有 `router/routes.ts`，导航注册到现有导航模型；页面优先复用 `AppPage`、`AppQueryPanel`、`AppDataTable`、`AppFormDrawer` 等共享组件。普通筛选采用明确 Query 参数，不引入 OData。MSW 仅用于组件测试和前置联调，联合验收必须切换真实 Gateway。

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
- 配置记录页使用服务端分页，支持 Record.NId/Name/Category 筛选；行编辑表单在页面内对九种 DataType 显式映射控件，只用于本配置集管理，不抽象为通用动态表单或低代码引擎。
- Required、Decimal 精度、日期、Enum、Json 和 Reference 错误在保存前显示到具体字段；服务端仍执行同样校验。
- 发布校验显示字段、记录、缺失必填值、类型错误、字典引用和与上一 Published Revision 的差异统计；存在任何错误时禁用发布。
- Clone 生成完整 Draft 快照。页面必须提示租户配置集是整份替换，不与平台记录隐式合并。
- 一次页面请求最多加载 100 条记录；不得为了表格便利一次下载 10,000 条记录。

## 20.4 元数据定义

- 列表字段：NId、名称、作用域、Revision、状态、属性数、发布时间。
- 编辑页包含属性表格、类型专属校验项和只读 Schema 预览。
- Enum 选择器只加载当前有效字典；字典不存在或未发布时阻止 Schema 发布。
- 发布差异按新增、约束收紧、约束放宽、禁用分类；删除已发布属性必须以新版本中的停用语义表达。
- Decimal 的精度/小数位和单位维度/默认单位显示专属控件；选择器保留来源及修订，默认单位限该维度，差异摘要标记量纲变化/精度收紧等不兼容项。
- 本页面不生成业务表单、不保存实体实例值、不接受可执行 JavaScript。

## 20.5 编码规则

- 列表字段：NId、名称、TargetEntityNId、作用域、Revision、状态、模板、重置策略。
- 编辑器只允许插入第 12.1 节 Token，并即时显示语法错误。
- Preview 要求用户填写模板所需上下文，明确标注“不消耗正式序号”。
- 页面不默认提供正式 Generate 操作；正式编码由拥有 `referencedata.coding-rule.generate` 的业务调用方生成。
- 发布前展示样例、长度和序列分区说明；并发冲突行为与字典一致。

## 20.6 状态机定义

- 普通列表默认不启用行选择；字段含 NId、名称、来源作用域、Revision、发布状态、节点数/转换数。行内详情/编辑，多余业务动作进入“更多”，与导出/列配置等表格工具分区。
- 结构化创建/编辑复用 AppFormDrawer，保留居中弹窗/右抽屉偏好；页签为基本信息、节点、转换及发布差异。每个子表有自己的上下文标题；不新增拖拽画布、图编辑依赖或通用工作流 UI。
- 节点编辑包含初始/终态、显示语义、颜色；转换编辑使用源状态、动作 NId/名称、目标状态。前端可预检，发布仍由服务端校验，错误定位到节点/转换。
- 对已发布固定修订提供“当前状态＋动作”的只读校验，展示定义上是否允许/目标状态；始终提示“未执行业务操作”。不能把校验成功显示为业务已流转。
- 已发布内容只读，可克隆新 Draft；显示“新发布只影响新绑定，既有业务保持原版本”。停用采用确认对话框并说明不取消历史读取。

## 20.7 计量单位

- 普通维度列表＋AppFormDrawer 内单位子表，不为无批量操作的场景启用行选择。展示维度 NId、来源、基准单位、Revision、发布状态和系统预置标志。
- 单位编辑展示 NId、符号、到基准倍率/偏移、小数位、舍入方式和启用状态；Ratio 隐藏/禁用偏移编辑且提交值固定为 0，基准单位参数受控。
- 平台预置记录显示保护原因，不提供可误导的编辑/停用入口；服务端守卫仍是权威。租户扩展使用新的租户定义，不能冒充标准。
- 换算试算输入固定维度/修订、源/目标单位和十进制字符串，调用服务端返回结果及舍入说明，不在浏览器另写换算算法。
- 历史详情明确版本与来源；新建业务选择只显示当前可用定义。数量错误显示到字段，不能因结果为 0 而误判为空。

## 20.8 通用页面状态与可访问性

- 首屏加载使用 Skeleton；空列表提供创建入口；保存成功使用明确消息并保留可追踪对象 ID。
- 400 显示字段级错误，401 进入统一重新认证，403 展示无权限，404 返回列表，409 进入冲突处理，503 提示服务暂不可用。
- 表单标签与控件关联，键盘可完成主要操作；错误不只依赖颜色；目标视口为 1366×768 和 1920×1080。
- 七模块页面沿用用户管理黄金页：AppPage/AppQueryPanel/AppDataTable/AppFormDrawer、PermissionGate、主题 Token 与中英文资源；查询模式切换语义一致，保留 aria-label，不硬编码中文。新增两页无批量需求，默认 selection=none；结构化表单与纯确认对话框保持区分。
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
| `REF-STATE-MACHINE-INVALID` | 422 | 定位初始节点/可达性/悬空转换/重复动作目标 |
| `REF-STATE-MACHINE-NOT-FOUND` | 404 | 提示指定来源/曾发布修订不存在，不回退新版 |
| `REF-UNIT-DIMENSION-NOT-FOUND` | 404 | 提示固定单位定义不存在 |
| `REF-UNIT-CONVERSION-INVALID` | 422 | 提示不同维度/来源/修订、非法因子或单位 |
| `REF-UNIT-NUMERIC-OVERFLOW` | 422 | 提示数值超出允许范围，不截断结果 |
| `REF-UNIT-SYSTEM-DEFINED` | 409 | 说明系统预置结构不可普通修改/停用 |
| `REF-METADATA-UNIT-INVALID` | 422 | 定位 Decimal 单位来源/修订/默认单位错误 |
| `REF-CONCURRENCY-CONFLICT` | 409 | 保留输入并要求重新加载 |
| `REF-INVALID-STATE` | 409 | 展示当前状态和允许动作 |

401、403、429、500、503 沿用平台统一错误码和 TraceId。错误响应不得包含 SQL、连接串、消息体或堆栈。

## 21.2 安全与授权

- JWT 验证、`TenantNId/UserNId` 和权限声明由 Identity 权威契约提供；缺少租户的租户级操作 fail-closed。当前代码中的旧租户属性名仅为前置契约偏移，不得固化到本服务模型。
- 所有查询在 Repository/Query 层强制租户过滤，不依赖 Controller 手工拼接。
- 权限 NId 沿用现有全小写点分格式：七模块分别声明 `referencedata.{module}.view/create/update/publish/disable` 的实际动作子集；CodingRule 额外声明 `preview/generate`，Parameter 额外声明 `read-secret-reference`。路由、导航和 `PermissionGate` 只引用统一权限目录常量。平台级维护使用独立高权限，禁止以一个“管理员”权限覆盖全部动作；Identity 负责权威判定和分配。
- JSON、Pattern、EAV 字段和值和 Template 按类型、数量、长度与语法白名单校验；不执行表达式、脚本或动态 SQL。
- 配置值、生成编码和事件负载记录时按字段规则脱敏；日志禁止输出 Authorization 和 Idempotency Record 完整响应。

## 21.3 审计与可观测性

ReferenceData 不建立 `ref_operation_audit`、统一审计查询 API、审计管理页面或合规保留策略。七模块只保留解释自身状态所需的领域历史和事务 Outbox；PF-04 Audit 是统一审计事实源。PF-04 契约未确认前，本阶段只定义标准审计事件适配门禁，不预设其 API。必要的宿主操作日志只作短期故障诊断，不承担合规审计，也不得保存敏感原值。

结构化日志字段至少包括 `ServiceName`、`TraceId`、`TenantNId`、`UserNId`、`Module`、`Operation`、`ObjectNId`、`Revision`、`DurationMs` 和结果。指标至少包括 API 延迟/错误率、模块缓存命中率、数据库降级次数、服务级 Outbox 按 ModuleKey 的积压与重试、发布次数、DynamicProperty 发布记录数/校验失败数、并发冲突和编码生成冲突。liveness、core readiness 与 capability health 分离；readiness 只汇总服务级本地初始化事实，Redis/RabbitMQ/Seq（及未来 tracing 后端）故障按能力降级状态观测。

---

# 22. 自动化测试与验收设计

## 22.1 测试矩阵

| 层次 | 必测场景 |
| --- | --- |
| Domain/Unit | 字典定义/项 NId 唯一、AppDomain/Key NId、Single/Multi互斥、Mandatory/Default/ReadOnly、多值明细、动态配置字段/记录 NId 与强类型值、发布状态机、Schema 交叉校验、模板 Token、序列分区；状态机初始/终态/可达性/确定性转换；基准单位、同维度换算/偏移/舍入/溢出 |
| Application | 权限与租户、逐Key作用域回退、显式空值阻断继承、AppDomain双版本、配置历史、动态配置整份 Revision、发布事务、幂等请求哈希；固定来源/修订、无隐式回退、系统预置保护；业务消费者事务失败不推进状态 |
| Infrastructure | 配置三级表唯一/部分唯一索引、领域实体生命周期列、技术记录最小字段、普通聚合内外键、动态配置同 Revision 约束、类型列 Check Constraint、单一迁移流/Ledger、原子序列、服务级 Outbox、Redis 降级 |
| API | Gateway/内部路径、ApiResult/PageResult、400/401/403/404/409/422/503、Idempotency-Key |
| Contract/Event | DTO JSON 快照、V1 事件字段、重复/乱序兼容、禁止引用内部项目 |
| Frontend Component | AppDomain/Key导航、Single/Multi编辑、ReadOnly禁用、显式空值提示、动态字段编辑、EAV记录分页、发布差异、权限按钮、冲突保留、错误映射 |
| E2E | 七页路由/权限 smoke；字典与 DynamicProperty 原关键路径；状态机“定义→发布→动作校验”；单位“维度/单位→发布→换算试算”；编码规则 Preview |

上述适用层次收敛在 `IndustrialPlatform.ReferenceData.Tests` 服务级项目，并按七个逻辑模块组织目录；真实 PostgreSQL、Redis、RabbitMQ 和跨入口链路进入统一 `IndustrialPlatform.IntegrationTests`。测试覆盖 `TenantNId` 隔离、权限拒绝、乐观并发、发布/覆盖或修订语义、单一 Schema/模块表前缀、服务级 Ledger/Outbox 和缓存降级；当前无入站消费者，不编写假想 Inbox 测试，也不为每个生产技术层创建独立测试项目。

新两模块额外覆盖冻结根后所有子项写入口拒绝、Unfreeze/Unlock 不能修改已发布内容、克隆不复制 Id/并发令牌、平台预置保护不可 mass assignment 绕过、定义发布后旧业务快照稳定。状态机消费以测试项目内的最小工单/库存样例夹具证明：业务前置条件或事务失败时当前状态、业务写入和历史均不前进；夹具不等于正式 WorkOrder/OperationalData 已实现，不扩张 PF-03 生产代码范围。

Parameter、Metadata 和 CodingRule 的领域语义主要由 Domain/Application/API/Integration 测试覆盖；CodingRule Generate 的 `Idempotency-Key`、同键同结果、换参冲突和并发唯一性不得依赖浏览器 E2E。最终浏览器验收不复制全量后端测试矩阵。

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
16. 聚合子项写入不存在的父表时数据库拒绝；子项只能通过聚合根用例写入，聚合根软删除后不再加载有效子项，且不存在绕过聚合根单独恢复子项的 API/Repository。
17. Development Shared/PerService 和 Test/Staging/Production PerService 均按可信配置解析；ReferenceData 使用服务级 Migration/Ledger/readiness，同物理目标 DDL 串行。
18. SystemData 不可用时已初始化宿主仍按本地事实 Ready；目标错误、服务级 Migration/Ledger 失败或版本不一致时宿主 NotReady，不使用管理员凭据、`EnsureCreated`、CodeFirst 自动建表或错误数据库回退。Redis/RabbitMQ/Seq 中断分别验证回源、Outbox 积压和可观测性降级，capability health 为 Degraded 而不是无条件阻断 Ready。

19. 状态机零个/多个初始节点、悬空转换、同状态同动作多个目标、不可达节点、终态出边均拒绝发布；合法回路、零终态或多个无出边终态可发布。
20. 工单/库存样例分别绑定状态机修订，按当前状态＋动作求目标；定义允许但业务守卫失败或业务事务回滚时，业务状态与历史不前进。平台/租户同 NId 不混用。
21. 发布状态机 revision 2 或停用 revision 1 后，既有实例仍读取其固定 revision 1；新选择不再使用停用项；未知来源/版本明确失败，无回退。
22. 质量 kg→g、体积 L→mL、绝对温度 degC→degF 按预置参数和目标舍入验证；kg→L、异修订/来源拼接、非正因子、非法小数位和溢出被拒绝。测试 ToEven/AwayFromZero 边界和有符号量值，不依赖 JavaScript 浮点。
23. 普通调用修改 IsSystemDefined、预置显示字段/倍率/基准/精度，或 CloneRevision/Publish/Disable 系统定义均失败；新增租户定义不能覆盖平台 NId 的既有绑定。旧单位修订/换算快照在新版本发布后保持可解释。
24. Metadata 非 Decimal 携带单位约束、无效默认单位/修订、跨租户来源均拒绝；改量纲、精度收紧和新增必填产生明确不兼容差异，不自动迁移业务值。
25. 状态机/单位同一 Revision 的 Published、Disabled 事件即使重复或乱序也按 optimisticVersion 正确失效；旧修订事件不得把 current 缓存回写旧版。Tenant 固定引用缺少 sourceTenantNId 返回 400，跨租户来源拒绝而不回退。

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

~~~text
PF-01 Gateway/前端基线 + Identity 可信身份/权限契约 + PF-02 数据库编排契约
                              ↓
                         TASK-RD-001
                              ↓
                         TASK-RD-002  Dictionary 纵向切片
                              ↓
                         TASK-RD-003  Parameter 纵向切片
                              ↓
                         TASK-RD-004  DynamicProperty 纵向切片
                              ↓
                         TASK-RD-005  UnitOfMeasure 纵向切片
                              ↓
                         TASK-RD-006  Metadata 纵向切片
                              ↓
                         TASK-RD-007  CodingRule 纵向切片
                              ↓
                         TASK-RD-008  StateMachine 纵向切片
                              ↓
                         TASK-RD-009  共享缓存、Outbox 与可观测性
                              ↓
                         TASK-RD-010  PF-03 联合验收
~~~

执行顺序固定为：薄基础 → Dictionary → Parameter → DynamicProperty → UnitOfMeasure → Metadata → CodingRule → StateMachine → 共享能力 → 联合验收。UnitOfMeasure 前置于 Metadata 以便验证单位约束；这是执行顺序，不代表七模块之间存在全面领域依赖。

十个编号是 PF-03 管理任务内的顺序执行步骤，不是十个独立派遣任务或十次强制提交。每个业务模块必须在同一步内完成 Domain、Application、Contracts、Infrastructure、API、前端页面和适用测试，再进入下一模块；禁止先平铺七个后端模块、最后集中补前端，也禁止多人并行修改同一迁移流、DI、路由或权限目录。

---

# 24. ReferenceData开发任务拆分

## TASK-RD-001 收敛服务薄基础

**状态：** 未开始（PF-03 未整体授权；内部顺序步骤）

**目标：** 在现有骨架上建立五层项目引用、七模块目录边界、可信身份/权限接入、单一数据库初始化单元、单一 Schema/迁移流和 core readiness/capability health 分层；不提前实现业务模块或自建框架。

**输入文档：** 本文第 1～7、13、15、21～23 节；蓝图 07/32/33；现有 BuildingBlocks、Gateway、Identity、SystemData 和统一前端真实代码。

**依赖：** BuildingBlocks/PF-01/PF-02 已有可消费契约；不绑定历史内部任务编号。

**允许修改范围：** ReferenceData 后端项目与服务级测试、解决方案/UnifiedHost/Gateway 的必要注册和 ReferenceData 配置；仅为本服务权限目录允许同步修改 Identity 权威 `PermissionCatalog`/seed/必要 policy 注册、前端 `permissions/catalog.generated.ts`、`catalog.ts` 及一致性测试，不得修改其他 Identity/SystemData 业务逻辑；不得实现七模块业务用例，不得新增通用 Repository、Mediator、事件总线、动态表单或代码生成框架。

**预期输出：** Contracts 项目与引用门禁；Domain/Application/Contracts/Infrastructure/Api 内七模块目录；`ServiceKey=referencedata`、`InitializationUnitKey=referencedata`、`referencedata_db`、`reference_data` Schema、单一 Migration/Seed Ledger/initializer；模块表名前缀规则；Identity 权威权限种子、前端生成/手写权限目录与契约测试同步；可信身份/权限接入；core readiness 与 Redis/RabbitMQ/Seq capability health 分离。

**验证与证据：** 覆盖程序集引用、权限目录一致性、配置绑定、Gateway/UnifiedHost 路由、匿名/认证/无权限路径、Development Shared/PerService、非 Development Shared 拒绝、SystemData 离线本地 Ready、错误目标/迁移失败 NotReady、Redis/RabbitMQ/Seq 故障不无条件阻断 Ready、迁移幂等和无 `EnsureCreated`/CodeFirst 自动建表；记录新鲜 build/test 证据。

**结果回写：** 回写真实目录、引用、端口、配置键、Schema/ledger/迁移名、健康检查标签和与现有平台契约的偏差。

**提交策略：** 作为 PF-03 整体工作的一部分回写；默认不为该内部步骤单独提交。

---

## TASK-RD-002 完成 Dictionary 纵向切片

**状态：** 未开始（PF-03 授权后随 TASK-RD-001 顺序执行）

**目标：** 完成 DictionaryDefinition/Item 聚合、持久化、管理与运行时 API、权限、PC 页面、测试和发布版本行为。

**输入文档：** 本文第 7、8、13～16、19～22 节；现有前端 `httpClient`、路由、导航、权限和共享页面组件。

**依赖：** TASK-RD-001。

**允许修改范围：** 五层项目中的 Dictionary 模块目录、`reference_data.dictionary_*` 迁移、ReferenceData API/权限/路由/导航/页面和适用测试；不得读取其他模块 Repository/表，不得建立通用 CRUD/版本引擎。

**预期输出：** DictionaryDefinition 聚合及普通子项外键；Draft/Published/Superseded/Disabled；整本租户覆盖；管理/运行时 DTO 与端点；`referencedata.dictionary.*` 权限；字典页面完整真实操作链路。发布事件只定义领域事实，统一持久化发布由 TASK-RD-009 接入服务级 Outbox。

**验证与证据：** 覆盖 NId 唯一、发布校验、克隆版本、租户回退、并发冲突、权限/API 信封、普通父子外键、页面加载/空/失败/无权限/冲突及“创建→发布→运行时读取”关键路径。

**结果回写：** 回写 DTO、路由、表/索引、权限、错误码、页面交互、测试报告和遗留限制。

**提交策略：** 作为 PF-03 整体工作的一部分回写；默认不为该内部步骤单独提交。

---

## TASK-RD-003 完成 Parameter 纵向切片

**状态：** 未开始（PF-03 授权后随 TASK-RD-002 顺序执行）

**目标：** 完成 ConfigurationAppDomain 聚合、Single/Multi Key、逐 Key 作用域覆盖、历史、有效值解析以及对应管理页面。

**输入文档：** 本文第 7、9、13～17、19～22 节；TASK-RD-002 已验证的纵向切片模式。

**依赖：** TASK-RD-002。

**允许修改范围：** Parameter 模块目录、`reference_data.parameter_*` 迁移、ReferenceData API/权限/路由/导航/页面和适用测试；不得存储秘密本体、读取 MasterData 数据库或抽象七模块共享 CRUD。

**预期输出：** AppDomain/Key/MultiValue 聚合内模型与普通外键；九种 DataType、ValueMode、Mandatory/Default/ReadOnly、显式空值、逐 Key 覆盖、脱敏历史、管理/运行时 API、`referencedata.parameter.*` 权限和参数配置页面。

**验证与证据：** 覆盖 Single/Multi 互斥、值/默认值、Mandatory、ReadOnly、多值排序/去重、显式 null/空集合、Tenant 回退、Factory 门禁、聚合 Revision/并发、敏感值拒绝、权限、页面状态和 AppDomain→Key→MultiValue→解析关键路径。

**结果回写：** 回写表/索引、完整路径、DTO/API、解析语义、历史脱敏、权限、页面交互、错误码和测试证据。

**提交策略：** 作为 PF-03 整体工作的一部分回写；默认不为该内部步骤单独提交。

---

## TASK-RD-004 完成 DynamicProperty/EAV 纵向切片

**状态：** 未开始（PF-03 授权后随 TASK-RD-003 顺序执行）

**目标：** 在 DynamicProperty 模块内完成 ReferenceData 自有 DynamicConfigDefinition/Field/Record/Value EAV、整份 Revision 发布、分页运行时读取和 PC 管理页面。

**输入文档：** 本文第 7、10、13～19、21、22 节；Dictionary 已发布读取契约用于 Enum 校验。

**依赖：** TASK-RD-003。

**允许修改范围：** DynamicProperty 模块目录、`reference_data.dynamic_property_*` 迁移、API/权限/路由/导航/页面和适用测试；不得保存物料/设备/工单等业务实体值，不得加入脚本、公式、审批、通用动态表单、低代码页面或分析引擎。

**预期输出：** Definition 聚合及 Field/Record/Value 子项；同 Revision 约束和强类型值列；Draft 克隆、整份发布/覆盖、分页 API；页面内九种 DataType 显式控件映射；`referencedata.dynamic-property.*` 权限和完整管理链路。

**验证与证据：** 覆盖 NId 唯一、九种类型、Required/默认值/精度/字典引用、跨 Revision 拒绝、Check Constraint、记录上限与分页、整份租户覆盖、并发/权限，以及“创建→字段→记录→发布→运行时读取”关键路径；证明没有业务实体 EAV 表或通用表单引擎。

**结果回写：** 回写四类表、约束、DTO/API、分页、类型控件映射、发布语义、权限、页面交互和测试报告。

**提交策略：** 作为 PF-03 整体工作的一部分回写；默认不为该内部步骤单独提交。

---

## TASK-RD-005 完成 UnitOfMeasure 纵向切片

**状态：** 未开始（PF-03 授权后随 TASK-RD-004 顺序执行）

**目标：** 完成通用维度、单位、基准换算、精度舍入、系统预置保护、修订读取及 PC 管理/试算。

**输入文档：** 本文第 7、12B、13～22 节；MasterData/OperationalData 已调整的单位所有权边界。

**依赖：** TASK-RD-004。

**允许修改范围：** UnitOfMeasure 模块目录、`reference_data.unit_of_measure_*` 迁移/受控幂等种子、API/权限/路由/导航/页面和适用测试；不得修改基础 Entity，不建设物料包装比例、跨维度密度换算或公式引擎。

**预期输出：** UnitDimension/UnitDefinition 原子修订；来源+NId+unitRevision 契约；系统标准种子与租户自定义；管理/历史读取/Convert API、十进制字符串契约、`referencedata.unit-of-measure.*`、维度/单位编辑和服务端试算页。

**验证与证据：** 覆盖唯一基准、正因子/偏移限制、同维度同来源同修订、目标舍入/溢出、预置保护防绕过、历史可读、跨租户拒绝、冻结根/并发/发布不可变、真实 decimal/numeric 映射，以及“定义→发布→试算→历史读取”浏览器路径。

**结果回写：** 回写维度/单位字段、decimal 精度、API/DTO、源引用、种子保护、错误码、页面和测试证据；明确物料专属换算由下游负责。

**提交策略：** 作为 PF-03 整体工作的一部分回写；默认不为该内部步骤单独提交。

---

## TASK-RD-006 完成 Metadata 纵向切片

**状态：** 未开始（PF-03 授权后随 TASK-RD-005 顺序执行）

**目标：** 完成 EntitySchema/AttributeDefinition、类型专属约束、字典/固定修订单位引用校验、发布版本、运行时读取和 PC 管理页面。

**输入文档：** 本文第 7、11、13～16、18～22 节；Dictionary、UnitOfMeasure 已发布读取契约。

**依赖：** TASK-RD-005。

**允许修改范围：** Metadata 模块目录、`reference_data.metadata_*` 迁移、API/权限/路由/导航/页面和适用测试；不得创建业务实体属性值表、页面生成器或业务实体仓储。

**预期输出：** EntitySchema 聚合及 AttributeDefinition 普通外键；版本生命周期、类型/精度约束、Enum 字典与 Decimal 单位引用交叉校验；管理/运行时 API；`referencedata.metadata.*` 权限；Schema/属性编辑及发布差异页面。

**验证与证据：** 覆盖属性 NId 唯一、类型专属字段、危险 Pattern、未发布字典/单位引用、不兼容量纲/类型/必填/精度差异、版本克隆/发布、租户覆盖、并发、权限、页面状态和“定义→校验→发布→运行时读取”关键路径。

**结果回写：** 回写表/索引、DTO/API、支持类型、版本消费要求、权限、页面交互、测试证据以及与 EAV/业务实体值/低代码的边界。

**提交策略：** 作为 PF-03 整体工作的一部分回写；默认不为该内部步骤单独提交。

---

## TASK-RD-007 完成 CodingRule 纵向切片

**状态：** 未开始（PF-03 授权后随 TASK-RD-006 顺序执行）

**目标：** 完成模板解析、规则发布、Preview、数据库原子序列、周期重置、幂等 Generate 和 PC 管理页面。

**输入文档：** 本文第 7、12～15、18～22 节。

**依赖：** TASK-RD-006。

**允许修改范围：** CodingRule 模块目录、`reference_data.coding_rule_*` 迁移、API/权限/路由/导航/页面和适用测试；不得用 Redis 作为唯一序列，不得修改 MasterData/OperationalData，不得引入通用规则引擎。

**预期输出：** CodingRule 聚合、白名单 Token 解析、规则版本；最小字段的 sequence/idempotency 技术记录；Preview/Generate API；`referencedata.coding-rule.*` 权限；Token 编辑、上下文输入和不占号 Preview 页面。

**验证与证据：** 覆盖非法 Token、缺失上下文、长度、ResetPolicy、Preview 不消耗、同键同结果、同键异请求冲突、高并发无重复、权限、页面提示和未保存冲突处理。

**结果回写：** 回写模板语法、分区键、幂等保留期、表字段、DTO/API、权限、错误码、页面交互和并发测试证据。

**提交策略：** 作为 PF-03 整体工作的一部分回写；默认不为该内部步骤单独提交。

---

## TASK-RD-008 完成 StateMachine 纵向切片

**状态：** 未开始（PF-03 授权后随 TASK-RD-007 顺序执行）

**目标：** 完成状态机定义、节点/转换、发布修订、固定版本读取、定义路径校验和 PC 管理页面。

**输入文档：** 本文第 7、12A、13～22 节；已有发布型模块的纵向交付模式。

**依赖：** TASK-RD-007。

**允许修改范围：** StateMachine 模块目录、`reference_data.state_machine_*` 迁移、API/权限/路由/导航/页面及测试项目内消费夹具；不得新建 WorkOrder/库存生产实现、共享 Entity.Status、远程 SetStatus 或通用工作流/脚本引擎。

**预期输出：** StateMachineDefinition 根及普通子项外键；唯一初始、0～多个无出边终态、可达性和确定性动作规则；来源+StateMachineNId+stateMachineRevision；管理/读取/Evaluate API、`referencedata.state-machine.*`、节点/转换编辑和无副作用动作校验页。

**验证与证据：** 覆盖非法图/合法回路、冻结根、并发、发布不可变、旧修订可读、新版不迁移、权限/来源隔离；工单/库存最小夹具证明业务守卫/事务失败时状态及历史不前进；浏览器“定义→发布→动作校验”仅证明定义能力，不宣称业务模块已实现。

**结果回写：** 回写图约束、引用语义、DTO/API、false原因码、权限、页面、测试证据与业务事务边界；事件由 TASK-RD-009 统一接入。

**提交策略：** 作为 PF-03 整体工作的一部分回写；默认不为该内部步骤单独提交。

---

## TASK-RD-009 接入共享缓存、Outbox 与可观测性

**状态：** 未开始（PF-03 授权后随 TASK-RD-008 顺序执行）

**目标：** 在七个已完成纵向模块上接入共享 Redis Cache Aside、单一服务级 Outbox/Dispatcher、V1 事件、降级策略、日志、指标和 Redis/RabbitMQ/Seq capability health；不预建无消费者的 Inbox。

**输入文档：** 本文第 13、16～18、21、22 节；RabbitMQ 规范和 BuildingBlocks 现有 EventBus/Logging/Health 能力。

**依赖：** TASK-RD-008。

**允许修改范围：** ReferenceData 服务级 Persistence/Messaging/Caching/Logging/Metrics/Health 及七模块的薄适配和测试；不得新建模块级 Outbox/连接/Worker，不得建立统一审计事实表/API/页面，不得把完整配置值写入事件或日志。

**预期输出：** 一个 Redis 连接和 Cache Aside 适配器；逻辑模块缓存键；`reference_data.outbox_message`、`ModuleKey`、一个 Dispatcher；七个 V1 事件；RabbitMQ 中断积压/恢复；PF-04 Audit 适配门禁；结构化日志、指标、core readiness 与 Redis/RabbitMQ/Seq capability health 分层。

**验证与证据：** 覆盖租户缓存隔离、Revision/失效、Redis 回源、双故障 503、业务事务与 Outbox 原子性、RabbitMQ 恢复、重复/乱序事件的 Outbox 载荷与 Revision 兼容性、事件快照、敏感值扫描、按 ModuleKey 积压指标；证明 ReferenceData 未注册假想消费者、没有七套 Outbox/Inbox 或竞争 PF-04 的审计中心。

**结果回写：** 回写缓存键/TTL、Outbox 表和重试、事件/路由键、降级行为、健康标签、指标、审计适配状态和测试证据。

**提交策略：** 作为 PF-03 整体工作的一部分回写；默认不为该内部步骤单独提交。

---

## TASK-RD-010 完成契约、E2E 与 PF-03 联合验收

**状态：** 未开始（PF-03 授权后随 TASK-RD-009 顺序执行）

**目标：** 使用真实 Gateway/UnifiedHost、Identity、SystemData 初始化编排、PostgreSQL、Redis、RabbitMQ 和浏览器验证七模块全纵向链路，并冻结供下游使用的 V1 契约。SystemData 只参与初始化 Operation 与离线验证，不成为 ReferenceData 日常运行依赖。

**输入文档：** 本文全部章节和 TASK-RD-001～009 的输出/执行记录。

**依赖：** TASK-RD-009。

**允许修改范围：** ReferenceData 契约/测试/文档、前端 E2E、部署冒烟检查及验收发现的本阶段缺陷；不得增加新领域或借验收建设通用平台。

**预期输出：** 运行时 DTO/事件 V1 快照、全量测试报告、七页路由/权限 smoke、Dictionary、DynamicProperty、StateMachine、UnitOfMeasure 四条浏览器关键路径、其余模块 API/Integration 证据、故障降级证据、安全扫描、页面截图和 MasterData 输入契约。

**验证与证据：** 执行新鲜后端 build/test 与前端 lint/typecheck/unit/build/e2e；验证单一 Schema/Migration/Ledger/Outbox、Shared/PerService、SystemData 离线、错误目标 NotReady、Redis/RabbitMQ/Seq Degraded、真实 PostgreSQL 约束、权限/租户、安全扫描和七个页面；Generate 幂等由 API/Integration 覆盖；证明无 `EnsureCreated`/CodeFirst 自动建表、七套基础设施、业务实体 EAV 或第二套前端框架。

**结果回写：** 更新完成标准、执行记录、最终 API/事件/权限/路由、已知限制和 MasterData 前置条件；任一必需外部环境未实测则保持“待验收”。

**提交策略：** PF-03 全部步骤验收通过后再按仓库工作流形成整体提交；文档中不预设分步提交。

---

# 25. ReferenceData完成标准

## 25.1 领域与数据

- 字典、参数配置应用域（单值/多值）、动态配置 EAV、元数据定义、编码规则、状态机定义和计量单位均有明确聚合、不变量、状态和租户边界。
- 所有领域实体以 NId 作为稳定业务标识，引用字段使用 `{EntityName}NId`；除正式生成的编码结果外，实体定义、DTO、API 和页面不存在 Code 业务标识。
- 领域表定义只列业务字段，具有独立领域生命周期的实体应用 Entity 公共字段；Ledger、Outbox、历史、序列和幂等记录使用最小技术字段。聚合子项使用普通外键并只能随聚合根写入，没有机械增加软删除影子列或复合唯一键。
- `referencedata_db` 按 SystemData 配置解析 Shared/PerService；服务级初始化器、Migration/Ledger 和本地 readiness 均有真实 PostgreSQL 18 证据。
- 动态配置四表只允许同 Revision 关联，字段和值类型由领域与数据库约束双重保证，整份发布不产生半成品。
- 发布/配置变更与单一服务级 Outbox 原子，Outbox 行以 ModuleKey 保留模块归属；当前无入站消费者和 Inbox。正式编码在并发和重试下不重复。
- 动态配置值仅保存 ReferenceData 自有配置记录；未创建业务实体 EAV 值表、低代码页面运行时或跨服务外键。

## 25.2 API、事件与缓存

- Gateway 与内部路径、DTO、权限、错误码、Idempotency-Key 和 Revision 前后一致。
- 七个 V1 事件完成重复、乱序和兼容性验证。
- Redis 故障可回源 PostgreSQL，双故障明确返回 503，缓存不成为权威。
- MasterData 可只通过稳定 V1 契约消费字典、参数配置应用域、动态配置 Schema/记录、实体 Schema、编码生成、状态机定义及通用单位/换算；不再定义另一套通用单位权威源。
- 状态机不持有业务当前状态、不提供通用 SetStatus；单位不包含全球箱/件比例、密度跨维度换算或任意公式。固定修订历史读取与新业务选择分离，变更定义不会静默改变在途业务。

## 25.3 前端与用户路径

- 七个 PC 页面连接真实 Gateway，覆盖加载、空、成功、校验、冲突、无权限和服务不可用状态。
- 菜单、路由和按钮权限与服务端权限一致；没有 PDA/Mobile 管理页越界。
- 关键页面通过 1366×768、1920×1080 截图和键盘可操作性检查。

## 25.4 安全、审计与可观测性

- 跨 TenantNId 访问被拒绝；平台级管理、模块发布和编码生成分权。
- 配置、日志、事件和错误响应无密码、Token、私钥、连接串和堆栈泄漏。
- 关键变更可由模块领域历史和 Outbox 按 TenantNId/UserNId/ObjectNId/Revision/TraceId 解释；PF-04 Audit 承担统一审计事实，Outbox、缓存、capability health 和编码冲突可观测。

## 25.5 自动化与环境验收

- Domain、Application、Infrastructure、API、Contract/Event、Frontend Component 和 E2E 的适用场景均有新鲜证据。
- 后端 build/test 与前端 lint/typecheck/unit/build/e2e 的命令、退出码和数量已回写。
- PostgreSQL、Redis、RabbitMQ、Seq、Gateway、Identity 和浏览器任一必需验收环境缺失时，对应能力只能标记“待验收”；这不改变运行时 capability health 的降级规则。
- SystemData 控制面、Shared/PerService 拓扑、七模块版本汇总和 NotReady 故障门禁必须有新鲜证据；禁止 `EnsureCreated`、管理员自建库和错误数据库回退。
- Redis/RabbitMQ/Seq 故障必须分别证明安全回源、Outbox 积压或可观测性降级，报告 capability Degraded 而不是机械使整个宿主 NotReady。

---

# 26. 执行记录

| 内部步骤 | 进度 | PF 执行者 | PF Evidence | 结果回写 |
| --- | --- | --- | --- | --- |
| TASK-RD-001 | PF-03 未授权（内部顺序步骤） | - | - | - |
| TASK-RD-002 | PF-03 未授权（内部顺序步骤） | - | - | - |
| TASK-RD-003 | PF-03 未授权（内部顺序步骤） | - | - | - |
| TASK-RD-004 | PF-03 未授权（内部顺序步骤） | - | - | - |
| TASK-RD-005 | PF-03 未授权（内部顺序步骤） | - | - | - |
| TASK-RD-006 | PF-03 未授权（内部顺序步骤） | - | - | - |
| TASK-RD-007 | PF-03 未授权（内部顺序步骤） | - | - | - |
| TASK-RD-008 | PF-03 未授权（内部顺序步骤） | - | - | - |
| TASK-RD-009 | PF-03 未授权（内部顺序步骤） | - | - | - |
| TASK-RD-010 | PF-03 未授权（内部顺序步骤） | - | - | - |

---

# 27. 下一阶段输入契约

ReferenceData 完成后，MasterData 及后续服务可以稳定依赖：

跨服务契约仅暴露实体 NId、Revision 和业务结果，不暴露数据库内部 Id、外键或表结构；下游引用字段按 `{EntityName}NId` 命名。

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
  GET  /api/v1/reference-data/state-machines
  GET  /api/v1/reference-data/state-machines/{nId}?sourceScope={scope}
  GET  /api/v1/reference-data/state-machines/{nId}/revisions/{revision}?sourceScope={scope}
  POST /api/v1/reference-data/state-machines/{nId}/evaluate
  GET  /api/v1/reference-data/units-of-measure/dimensions
  GET  /api/v1/reference-data/units-of-measure/dimensions/{nId}?sourceScope={scope}
  GET  /api/v1/reference-data/units-of-measure/dimensions/{nId}/revisions/{revision}?sourceScope={scope}
  POST /api/v1/reference-data/units-of-measure/convert

稳定DTO
  EffectiveDictionaryDto
  EffectiveConfigurationDto
  EffectiveDynamicConfigSchemaDto
  DynamicConfigRecordDto
  EffectiveSchemaDto
  CodePreviewDto
  GeneratedCodeDto
  StateMachineDefinitionDto
  TransitionEvaluationDto
  UnitDimensionDto
  UnitConversionResultDto

V1事件
  ReferenceDictionaryPublishedV1
  ReferenceConfigurationChangedV1
  ReferenceDynamicConfigurationPublishedV1
  ReferenceMetadataPublishedV1
  ReferenceCodingRulePublishedV1
  ReferenceStateMachineChangedV1
  ReferenceUnitDimensionChangedV1

权限码（各模块按实际支持动作取子集）
  Dictionary: referencedata.dictionary.view, referencedata.dictionary.create,
              referencedata.dictionary.update, referencedata.dictionary.publish,
              referencedata.dictionary.disable
  Parameter:  referencedata.parameter.view, referencedata.parameter.create,
              referencedata.parameter.update, referencedata.parameter.disable,
              referencedata.parameter.read-secret-reference
  Metadata:   referencedata.metadata.view, referencedata.metadata.create,
              referencedata.metadata.update, referencedata.metadata.publish,
              referencedata.metadata.disable
  DynamicProperty: referencedata.dynamic-property.view,
                   referencedata.dynamic-property.create,
                   referencedata.dynamic-property.update,
                   referencedata.dynamic-property.publish,
                   referencedata.dynamic-property.disable
  CodingRule: referencedata.coding-rule.view, referencedata.coding-rule.create,
              referencedata.coding-rule.update, referencedata.coding-rule.publish,
              referencedata.coding-rule.disable, referencedata.coding-rule.preview,
              referencedata.coding-rule.generate

  StateMachine: referencedata.state-machine.view, referencedata.state-machine.create,
                referencedata.state-machine.update, referencedata.state-machine.publish,
                referencedata.state-machine.disable
  UnitOfMeasure: referencedata.unit-of-measure.view, referencedata.unit-of-measure.create,
                 referencedata.unit-of-measure.update, referencedata.unit-of-measure.publish,
                 referencedata.unit-of-measure.disable

前端路由
  /pc/system/reference-data/dictionaries
  /pc/system/reference-data/configurations
  /pc/system/reference-data/dynamic-properties
  /pc/system/reference-data/metadata
  /pc/system/reference-data/coding-rules
  /pc/system/reference-data/state-machines
  /pc/system/reference-data/units-of-measure
```

状态机/单位引用固定来源与修订；上面的 GET 示例只展示共同参数，Tenant 来源必须额外传入 `sourceTenantNId` Query，POST 则放 Body，并与可信身份一致；Platform 来源省略或为 null（第 15.3 节）。单位数量/因子/偏移采用十进制字符串。新单位/状态机契约不要求下游重写已有业务状态机；只有明确绑定的业务类型才消费这些定义，旧业务不自动迁移。

已知限制：Factory 作用域要等待 MasterData 提供权威工厂归属校验后启用；后台服务身份等待 Identity 的服务认证契约。动态配置 EAV 只提供配置数据的强类型存储、版本和读取，不提供脚本、公式、任意分析查询或低代码运行时。后续服务必须自行设计业务实体动态属性值、Schema 迁移、业务审批和数据权限，不能从 ReferenceData 的动态配置或元数据推断这些能力已经存在。

---

# 28. 文档自审清单

- [x] 引用文件真实存在。
- [x] 当前代码/环境状态已如实记录；CodeFirst baseline 与全依赖阻断 readiness 是 TASK-RD-001 待消除的已知骨架差距。
- [x] 无待确定项、待办占位或模糊处理语句。
- [x] ReferenceData、Identity、MasterData、OperationalData 和低代码平台边界明确。
- [x] API、事件、类型、权限和路由前后一致。
- [x] 领域实体与技术记录已区分；技术记录未机械继承 Entity 生命周期、软删除或双版本并发。
- [x] 领域实体身份统一使用 NId；Code 仅表达正式生成的编码结果，跨实体业务引用使用 `{EntityName}NId`。
- [x] 聚合内父子表默认使用普通外键并由聚合根维护；复合软删除外键仅作为有证据时的条件性例外。
- [x] 字典、参数配置应用域（单值/多值）、动态配置 EAV、元数据、编码规则、状态机定义、计量单位及七个页面均有对应任务和验收。
- [x] 每个步骤具备状态、目标、输入文档、依赖、允许修改范围、预期输出、验证与证据、结果回写、提交策略九字段。
- [x] 任务依赖图、任务卡和执行记录编号一一对应。
- [x] 现有测试代码与本轮新鲜验证证据严格区分。
- [x] `git diff --check` 通过。
