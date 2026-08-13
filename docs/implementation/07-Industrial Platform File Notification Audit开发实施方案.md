# 07-Industrial Platform File Notification Audit开发实施方案

# Industrial Platform File / Notification / Audit 开发实施方案

> 当前里程碑范围：PF-04 仍是一个阶段、一个详细设计任务；File、Notification、Audit 是加入 PF-02 所创建 `SystemData.Service` 的三个独立内部模块，不创建独立 Service Host。本文件完成详细设计、依赖与九字段任务卡；详细设计完成不等于允许开发。

版本：V1.0-draft
日期：2026-08-12
阶段：PF-04 File / Notification / Audit
阶段状态：详细设计形成中；全部任务卡均为“待细化/任务待确认”，未派遣、未开发、未构建、未测试。未经本任务用户后续明确书面允许，不得创建开发子任务、修改生产/测试代码或执行任务卡。

模块与宿主：

```text
SystemData.Service（PF-02 创建）
├── SystemData Core（PF-02）
├── File Module（PF-04）
├── Notification Module（PF-04）
└── Audit Module（PF-04）
```

设计依据：`CLAUDE.md`、蓝图 01/05/07/09/20/27/29/30/31/32/33、实施 01、README、实施模板及实施 03/04/05/06。发生冲突时，以当前 Git/代码证据、蓝图 32/33、总 Todo 的阶段边界以及本文件明确冻结的规则为准；实施 06 的 `TenantId`、DynamicConfig、本地审计与自迁移设计不是 PF-04 稳定契约。

---

# 1. 文档说明

## 1.1 文档目的

本文是 PF-04 的详细设计、开发依赖、验收门禁和九字段任务卡维护源。它定义三个模块的独立数据所有权、公开契约、权限、迁移单元、测试和未来拆分边界，但不授权实施。

## 1.2 当前输入状态

- 基线分支为 `develop`，首次盘点 HEAD 为 `4180d71`；当时与 `origin/develop` 无领先或落后。
- 首次盘点只有实施 05 的并行修改；设计期间工作树继续出现 `CLAUDE.md`、实施 06、Identity 管理/审计代码及测试等并行改动。PF-04 不修改、暂存、回退或提交这些内容，也不把未验收实现当作稳定契约。
- 当前仓库尚无 `SystemData.Service`、File、Notification 或统一 Audit 生产实现；Gateway 也没有 `/systemdata` 路由。
- 当前 Identity 已存在本地登录/操作审计概念，ReferenceData 文档存在 `ref_operation_audit`，二者均不是统一 Audit 权威事实源；其迁移只能在各阶段明确兼容方案后实施。
- PF-02 实施 05 已形成数据库编排设计与任务卡，但控制面尚未实现；`TASK-SD-001～004` 是 PF-04 阻塞前置。
- PF-01 只有已批准设计、没有稳定实现；PF-03 只有骨架且实施 06 待复核。

## 1.3 执行前置

```text
PF-02 TASK-SD-001 → SD-002 → SD-003 → SD-004 通过
    → SystemData.Service core 可运行
    → 支持宿主 Manifest + 子 MigrationUnit + 父子 Operation
    → PF-04 数据库门禁验收
    → Audit → File → Notification 纵切
    → 三模块集成、安全、三端页面与阶段验收
```

PF-02 若未提供本文件要求的子迁移单元、宿主聚合 readiness 和内部协调扩展点，PF-04 必须保持阻塞，不得在 PF-04 重复实现数据库控制面。

---

# 2. 定位、目标与职责边界

## 2.1 负责

- Audit：平台追加型合规事实的统一接收、完整性保护、保留、冻结、受权查询和导出；高风险审计访问本身也产生审计事实。
- File：上传会话、隔离区、扩展名/MIME/魔数校验、病毒扫描适配、对象存储、受权下载、冻结、保留与清理。
- Notification：公告、系统通知、个人收件箱、受众解析、投递状态、已读、失效、跳转目标、SignalR 在线提示与离线补拉。
- 三模块各自的公开 Contracts、权限资源、Schema/表前缀、迁移产物与账本、API/事件、可观测性和测试。
- 当前单租户完整可用，所有领域、持久化与消息边界使用可信身份上下文的 `TenantNId`。

## 2.2 不负责

- 不创建 File/Audit/Notification 独立 Service Host。
- 不覆盖业务模块自己的领域历史、业务事件或 Outbox；Audit 只保存合规事实投影。
- 不实现用户聊天、群聊、会话和消息漫游；这些属于 Collaboration Messaging。
- 不设计 PF-10 ServerMonitor，也不实现 PF-10A 知识、问题与助手；File 只提供未来消费者所需公开契约。
- 不实现对象存储、杀毒引擎、邮件/短信厂商本身，只实现可替换适配器和安全门禁。
- 不允许跨模块 Repository、DbContext、表、外键、可写视图或领域实现引用。

## 2.3 模块依赖原则

- 每个模块独立 Domain、Application、Contracts、Infrastructure 与组合根。
- 同步协作只走公开 Application Contract/API；异步协作只走版本化事件。
- 宿主只负责装配、路由、身份上下文、技术中间件和健康汇总，不承载跨模块业务规则。
- Audit 不回查 File/Notification 表补齐事实；生产者必须提交足够、脱敏且可验证的上下文。

---

# 3. 前后端及跨服务协作目标

| 消费方 | 稳定输入 | PF-04 输出 |
| --- | --- | --- |
| Identity | JWT/可信 `TenantNId`、`UserNId`、权限集合 | Audit 写入/查询；File/Notification 授权上下文 |
| PF-02 | DatabaseTopology、Manifest、Operation/readiness、权限资源目录 | 三个 MigrationUnit 声明与模块健康 |
| 业务模块 | 自有事务与 Outbox、业务资源授权结果 | Audit 接收；FileNId；通知发布/收件箱契约 |
| Collaboration/KnowledgeBase | 未来公开消费契约 | 仅 File 元数据、授权上传/下载，不开放 Repository |
| PC/PDA/Mobile | AuthUser 与平台壳能力 | 管理页面、收件箱、上传/下载交互；按终端能力降级 |

所有写接口支持 `Idempotency-Key`，所有响应与事件使用稳定 NId、UTC 时间、版本字段和稳定错误码；API 不暴露数据库键、存储物理路径、Bucket、连接串、Secret 或杀毒引擎内部信息。

---

# 4. 总体架构与数据流

## 4.1 数据库启动门禁

```text
基础设施最小引导 systemdata.core
  → CoreControlReady
  → 读取并验签 SystemData Host Manifest
  → DatabaseTopologyOptions 解析 logical/physical target
  → 创建父 Reconcile OperationId
  → File / Notification / Audit UnitOperation inspect-plan-apply-verify
  → 汇总版本、drift 与健康
  → BusinessReady → /health/ready = 200
```

- `ServiceKey=systemdata` 是唯一宿主登记；PF-04 不伪装成三个服务。
- 一个签名宿主 Manifest 包含 `systemdata.core`、`systemdata.file`、`systemdata.notification`、`systemdata.audit` 四个必需迁移单元。
- 宿主字段拥有 Provider、稳定 `LogicalDatabaseName=systemdata_db`、Owner、DesiredState、ManifestVersion/checksum；模块单元拥有 UnitKey、Schema/前缀、MigrationAssembly/Bundle、目标版本、checksum/signature、ledger、Owner、DesiredState 和 `AutoMigrateRequested`。
- `PhysicalDatabaseName` 由可信 `DatabaseTopologyOptions` 解析，API/Manifest 不接受物理地址、路径或凭据。
- Development 默认 `Shared`、可显式 `PerService`；Test/Staging/Production 只允许 `PerService`。Shared 仅共享物理目标，不合并所有权、迁移产物或账本。
- PostgreSQL 使用 `system_file`、`system_notification`、`system_audit` 独立 Schema；SQLite 使用对应 `system_file_*`、`system_notification_*`、`system_audit_*` 表前缀和独立 migration ledger。
- Shared 物理目标以 `EnvironmentNId + Provider + PhysicalDatabaseName` 的 advisory lock/等效锁串行 DDL；每个 Unit 仍独立迁移与报告。
- 任一必需 Unit 未登记、版本不符、drift、迁移失败、目标错误或 SystemData 控制面不可用，标准 `/health/ready` 返回 503；liveness 与脱敏模块诊断保持可用。
- `CoreControlReady` 只开放受限管理通道以完成生产 plan、审批、备份、apply、查询和恢复；普通业务路由只在 `BusinessReady` 后开放。
- 生产生成一个绑定 Manifest、TopologyRevision、物理目标指纹和全部 Unit 状态的不可变宿主 Plan；审批与备份证据绑定 PlanChecksum。执行与账本仍按 Unit 独立。
- `AutoMigrateRequested` 只能收紧策略；环境政策计算 `EffectiveAutoMigrate`。Production 永远走 `plan → 审批 → 备份 → apply → verify`。
- SystemData 自身数据库仅允许 PostgreSQL 18 Compose/init 或部署步骤最小 bootstrap；PF-04 单元不进入 init 脚本，由 core-ready 后的宿主内部协调器复用 PF-02 能力，且不调用公开编排 API。
- 禁止 `EnsureCreated`、管理员凭据自行建库、静默切换数据库及隐式 copy/rename/merge/split；有数据的拓扑变化必须报告 drift 并走显式迁移/import。

## 4.2 Audit 数据流

```text
业务事务 → 业务变更 + 本模块 Outbox(AuditFactV1) 原子提交
        → 幂等投递 → Audit Ingress → 验证/脱敏/完整性链 → 追加事实
        → 查询投影/导出任务
```

默认策略是“可靠 Outbox、中央异步接收”：Outbox 无法写入则业务事务回滚；中央 Audit 暂时不可用但 Outbox 已落库时业务可完成。高风险审计查看、导出、解密查看、保留策略修改、法律冻结和完整性操作必须先可靠追加访问事实，失败时关闭操作。

## 4.3 File 数据流

```text
创建上传会话 → 受限隔离上传 → 完成上传
→ 大小/扩展名/MIME/魔数/hash 校验
→ 病毒扫描 → Clean
→ 提升为 Available 对象
→ 授权下载/冻结/保留/清理
```

扫描不可用、超时或结果未知时文件停留隔离区，禁止业务下载、预览或下游消费。

## 4.4 Notification 数据流

```text
发布草稿/系统事件 → 固化内容与受众快照 → 生成 Inbox Delivery
→ Outbox 通知 SignalR → 客户端收到提示 → 按游标补拉持久收件箱
→ 已读/失效/跳转
```

数据库收件箱是事实源；SignalR 只做低延迟提示，不承诺恰好一次，也不承载聊天正文权威存储。

---

# 5. 项目结构与引用关系

建议在 PF-02 实际项目结构稳定后落位：

```text
src/backend/src/Services/SystemData/
├── ...SystemData.Contracts/
│   ├── File/
│   ├── Notification/
│   └── Audit/
├── ...SystemData.Domain/
│   ├── File/
│   ├── Notification/
│   └── Audit/
├── ...SystemData.Application/
│   ├── File/
│   ├── Notification/
│   └── Audit/
├── ...SystemData.Infrastructure/
│   ├── File/
│   ├── Notification/
│   └── Audit/
└── ...SystemData.Api/
    └── Modules/{File,Notification,Audit}/
```

Contracts 保持零基础设施引用；模块间只能引用对方 Contracts，优先通过宿主内部端口适配。架构测试必须阻止跨模块 Domain/Infrastructure 引用和跨模块 DbContext/Repository 注入。

---

# 6. 全局技术与实施约束

- 所有聚合使用 `Guid` 内部键与稳定字符串 NId；外部仅暴露 `FileNId`、`NotificationNId`、`AuditEventNId` 等 NId。
- `TenantNId` 来自可信身份/服务上下文，不接受普通请求体覆盖；平台级系统事实也必须使用明确的受控平台租户/作用域，不使用 null 绕过隔离。
- 时间使用 UTC `timestamptz`/等价类型；状态变更使用 double concurrency token 或等价乐观并发。
- 软删除不适用于 Audit 事实；File/Notification 的删除是状态与保留策略，不进行不可审计的物理删除。
- 公开契约版本化；消费者不得依赖数据库 Schema、枚举整数或内部对象键。
- 写入幂等、Outbox/Inbox、重试、死信和恢复均必须记录 OperationId/TraceId，但不得记录 Secret、原始文件内容或通知敏感正文。
- 三模块的运行角色仅拥有自身 Schema/前缀所需 DML；迁移角色仅拥有自身 DDL；禁止跨模块授权。

---

# 7. 领域模型或核心组件详细设计

## 7.1 Audit

### 7.1.1 聚合与事实模型

`AuditFact` 为不可变追加事实，核心字段：

- `AuditEventNId`、`TenantNId`、`OccurredOn`、`ReceivedOn`。
- `ProducerServiceKey`、`ProducerModuleKey`、`EventType`、`EventVersion`。
- `ActorType`、`ActorUserNId`、`ActorServiceNId`、`ImpersonatorUserNId`。
- `Action`、`ResourceType`、`ResourceNId`、`Outcome`、`RiskLevel`。
- `OperationId`、`TraceId`、`CorrelationNId`、`SourceIpMasked`、`UserAgentSummary`。
- `Summary`、经过字段级策略处理的 `MetadataJson`、`PayloadClassification`。
- `PreviousHash`、`ContentHash`、`ChainPartition`、`IntegrityVersion`。
- `RetentionPolicyNId`、`RetainUntil`、`LegalHold`。

生产者不能提交 `ReceivedOn`、哈希链字段、最终保留期限或服务端身份字段。Audit Ingress 从受信调用身份补全生产者并校验声明。

### 7.1.2 写入与失败策略

- 普通业务操作必须在自身事务内写 Audit Outbox；Outbox 写入失败则业务回滚，禁止捕获后忽略。
- `AuditFactV1` 以 `TenantNId + ProducerServiceKey + AuditEventNId` 幂等；同键不同内容返回冲突并告警。
- 接收流程先做 Schema/大小/枚举/时间窗/身份校验，再执行服务端脱敏和追加。
- 永久失败进入可查询隔离队列，保留原事件的加密受控副本和脱敏诊断；不得丢弃或无限盲重试。
- Outbox 年龄、积压量或永久失败超过风险策略阈值时，相关生产者进入 Degraded/NotReady；阈值由环境和风险等级配置，不能由调用方放宽。
- 高风险 Audit 管理操作同步写“访问事实”后才返回数据；写失败返回 503，不能 fail-open。

### 7.1.3 完整性、保留与查询

- 事实行禁止 UPDATE/DELETE；更正以关联原事实的 `AuditCorrectionRecorded` 新事实表达。
- 按 `TenantNId + 时间分区` 建立哈希链；定期生成签名 checkpoint，并将摘要写入独立受控介质/外部锚点适配器。
- 保留策略版本化；延长立即生效，缩短不得追溯删除，必须经过审批与下一清理周期。
- Legal Hold 覆盖普通保留清理，解除冻结必须双人或等价审批并产生审计。
- 查询默认最大时间窗、分页上限和字段投影；禁止任意 SQL、任意 JSONPath 全表扫描和未授权跨租户查询。
- 敏感元数据默认脱敏；“查看敏感值”使用独立权限、理由、短时授权和二次审计。导出异步生成、加密、短期有效且下载也审计。

## 7.2 File

### 7.2.1 聚合

- `UploadSession`：上传意图、租户、拥有者、用途、期望大小/hash、允许类型策略、到期时间、分片状态和幂等键。
- `FileObject`：`FileNId`、原始/安全展示文件名、声明/探测 MIME、扩展名、大小、hash、存储对象引用、状态、扫描结果、保留与冻结。
- `FileReferenceGrant`：消费者模块、业务资源 NId、用途、授权动作、有效期；不保存业务表外键。
- `FileRetentionCase`：保留策略、清理候选时间、Legal Hold/业务冻结及原因。

### 7.2.2 状态机

```text
UploadInitiated → Uploading → Uploaded → Validating → Quarantined → Scanning
Scanning → Available（仅 Clean）
Scanning → Rejected（Malicious/PolicyRejected）
Scanning → Quarantined（Unavailable/Timeout/Unknown）
Available → Frozen → Available
Available/Frozen/Rejected/Expired → DeletionPending → Deleted
```

- 只有 `Available` 可签发普通下载；Frozen 是否允许合规只读取决于冻结类型和独立权限。
- 状态转换以并发版本和幂等命令保护；客户端不能直接指定状态。
- 重新扫描产生新 ScanAttempt，不覆盖历史结果；策略或引擎版本变化可以触发受控复扫。

### 7.2.3 上传、扫描、存储与下载

- 创建上传会话先校验用途、配额、最大大小、扩展名白名单和调用者权限。
- 上传只写隔离区；预签名凭证最小权限、短时有效、绑定对象键/大小/content hash 条件，不暴露长期 Secret。
- 完成上传后由服务端重新读取对象并校验实际大小、扩展名、规范化 MIME、魔数和 SHA-256/等价 hash；客户端声明不作为信任依据。
- 文件名去路径化、控制字符清理和 Unicode 规范化；下载使用安全 `Content-Disposition`，禁止基于原名构造物理路径。
- 病毒扫描通过 `IVirusScanner` 适配器；扫描引擎不可用时保持 Quarantined，并指数退避重试、告警，不允许人工直接改为 Clean。
- 对象存储通过 `IObjectStorage`；数据库只存不可逆/不含凭据的 StorageObjectKey 和版本，不存本地绝对路径或公开 URL。
- 下载先校验可信 TenantNId、调用者、消费者资源授权、File 状态和一次性 token；服务端流式代理或签发短期受限 URL，并记录下载审计。
- 业务模块只保存 `FileNId`；跨模块不建立数据库外键。引用检查通过显式 Contract，删除采用延迟清理与重复确认。
- 清理任务对过期上传、隔离对象和无引用文件分阶段标记；冻结、Legal Hold、活跃引用或未完成审计均阻止物理删除。

## 7.3 Notification

### 7.3.1 聚合

- `NotificationDefinition`：类型、模板版本、默认严重级别、允许跳转类型和渠道策略。
- `Announcement`：标题、摘要、正文、发布者、发布时间窗、优先级、状态和受众规则。
- `NotificationMessage`：不可变内容快照、来源、业务关联、跳转目标、失效时间、幂等键。
- `AudienceSnapshot`：发布时解析的用户/角色/组织/租户受众摘要与规则版本；大受众可批次展开。
- `InboxDelivery`：`NotificationNId + RecipientUserNId`、投递状态、首次投递、已读、失效、置顶/确认状态和版本。

### 7.3.2 语义与状态

- 公告支持 Draft → Scheduled → Published → Expired/Revoked；发布后正文变化必须产生新版本，不原地篡改已投递快照。
- 系统通知由受信服务契约或事件创建；个人收件箱只读投递事实，不允许用户伪造来源。
- 投递至少一次、Inbox 幂等去重；SignalR 重复/丢失不影响数据库事实。
- 已读为用户级幂等状态，记录首次已读时间；重复标记不改变时间。批量已读必须有数量上限和明确过滤条件。
- 失效后默认不出现在未读计数，但历史查询按权限/保留策略可见；撤回产生状态事件，不能假装从未投递。
- 跳转目标使用受控 `TargetType + TargetNId + RouteParameters`，由资源注册表解析；禁止服务端保存任意外部 URL、脚本或前端路由片段。外链仅允许显式白名单类型。
- Notification 不是聊天：无会话、回复、输入状态、消息编辑、端到端私聊或群组成员模型。

### 7.3.3 实时与离线

- SignalR 事件只含 `NotificationNId`、类别、严重级别、发生时间和提示摘要，不发送高敏正文。
- 客户端连接/重连后使用游标查询 `/inbox?after=...` 补拉；服务端游标不可伪造并绑定用户/租户。
- 未读计数由持久数据计算并可短时缓存；缓存失效不影响查询正确性。
- 在线推送失败只影响延迟，不把持久 Delivery 标记为失败；渠道投递状态与收件箱创建状态分开。

---

# 8. 数据与持久化设计

## 8.1 数据库与账本

| MigrationUnit | PostgreSQL Schema | SQLite 前缀 | 独立账本 |
| --- | --- | --- | --- |
| `systemdata.file` | `system_file` | `system_file_` | `system_file_schema_migrations` |
| `systemdata.notification` | `system_notification` | `system_notification_` | `system_notification_schema_migrations` |
| `systemdata.audit` | `system_audit` | `system_audit_` | `system_audit_schema_migrations` |

三者继承 `systemdata_db` 的规范化目标。Development Shared PostgreSQL 可物理落到 `industrial_platform_dev`；PerService 落到配置映射的 SystemData 物理库。模块不能自行选择物理库。

## 8.2 主要表

File：`upload_session`、`upload_part`、`file_object`、`file_scan_attempt`、`file_reference_grant`、`file_retention_case`、`file_outbox`。
Notification：`notification_definition`、`announcement`、`notification_message`、`audience_snapshot`、`audience_batch`、`inbox_delivery`、`notification_outbox`。
Audit：`audit_fact`、`audit_integrity_checkpoint`、`audit_retention_policy`、`audit_legal_hold`、`audit_export_job`、`audit_ingress_failure`。

每张租户业务表以 `TenantNId` 作为唯一键/索引前导；跨租户唯一性必须显式说明。跨模块只保存外部 NId 字符串，不建立外键。Outbox/Inbox 与本模块业务事务同库提交。

## 8.3 分区、索引与生命周期

- Audit 按时间和租户策略分区；核心索引覆盖 TenantNId、OccurredOn、Actor、Action、Resource、Outcome、OperationId/TraceId。任意高基数 JSON 字段不默认建通用索引。
- File 以 TenantNId+FileNId、状态+清理时间、hash（按策略去重但不跨租户推断存在性）索引；对象存储生命周期不能早于数据库清理批准。
- Notification 以 TenantNId+RecipientUserNId+状态+时间、NotificationNId、失效时间索引；大受众批次生成，禁止单事务展开无限收件箱记录。
- 清理作业均采用 lease、批次、并发版本、dry-run 指标和可恢复游标；删除前二次检查冻结/引用/保留。

---

# 9. API、事件与外部集成契约

## 9.1 路由边界

```text
/api/systemdata/files/...
/api/systemdata/notifications/...
/api/systemdata/audits/...
```

### File

- `POST /files/upload-sessions`
- `POST /files/upload-sessions/{nId}/complete`
- `GET /files/{fileNId}`
- `POST /files/{fileNId}/download-authorizations`
- `POST /files/{fileNId}/freeze|unfreeze`
- `DELETE /files/{fileNId}`（仅请求清理）
- 内部 Contract：引用登记/释放、批量元数据查询、受控复扫。

### Notification

- `POST /notifications/announcements`、`.../{nId}/publish|revoke`
- `POST /notifications/system-messages`（仅受信服务）
- `GET /notifications/inbox`
- `PUT /notifications/inbox/{notificationNId}/read`
- `POST /notifications/inbox/read-batch`
- `GET /notifications/unread-count`
- SignalR Hub 仅发布变更提示。

### Audit

- `POST /audits/facts:ingest`（受信服务/内部 Contract）
- `GET /audits/facts`
- `GET /audits/facts/{auditEventNId}`
- `POST /audits/exports`、`GET /audits/exports/{jobNId}`
- `POST /audits/integrity-verifications`
- `POST /audits/legal-holds`、`.../{nId}/release`
- 保留策略管理接口只允许平台合规管理员。

所有列表采用稳定排序和 opaque cursor；敏感查询限制时间窗、页大小、导出量与速率。错误响应包含稳定 code、TraceId，异步操作包含 OperationId，不泄露内部存储信息。

## 9.2 事件

输出事件包括：

- File：`FileAvailableV1`、`FileRejectedV1`、`FileQuarantinedV1`、`FileDeletedV1`。
- Notification：`NotificationPublishedV1`、`InboxDeliveryCreatedV1`、`NotificationReadV1`、`NotificationRevokedV1`。
- Audit：`AuditFactAcceptedV1`、`AuditIngressFailedV1`、`AuditIntegrityViolationDetectedV1`、`AuditExportCompletedV1`。

输入事件必须经 Inbox 幂等；事件 Envelope 包含 EventNId、EventType/version、OccurredOn、Producer、TenantNId、CorrelationNId、TraceId。事件不携带文件正文、Secret、任意 URL、完整敏感审计载荷或数据库物理信息。

## 9.3 权限资源

建议资源前缀：

- `systemdata.file.upload/read/download/manage/scan/freeze/delete`
- `systemdata.notification.inbox.read/announcement.read/manage/publish/system.send`
- `systemdata.audit.write/read/read-sensitive/export/integrity.verify/retention.manage/legal-hold.manage`

权限清单由模块声明，经 PF-02/Identity 稳定注册流程核验；服务端每个端点独立授权，前端隐藏按钮不构成安全控制。

---

# 10. 页面与交互设计

## 10.1 PC

- File 管理：筛选状态/类型/时间/拥有者，查看扫描与引用摘要，执行复扫、冻结和清理申请；不展示对象路径和扫描引擎 Secret。
- Notification 管理：公告草稿、受众预估、排期、发布确认、撤回和投递统计；发布前展示不可变内容/受众快照摘要。
- 个人通知中心：未读计数、分类、严重级别、已读、失效提示和受控跳转。
- Audit 控制台：固定条件查询、详情脱敏、敏感查看理由、异步导出、完整性验证、保留策略和 Legal Hold；所有高风险动作二次确认并审计。

## 10.2 PDA/Mobile

- 支持上传进度、隔离/扫描状态、受权下载和失败重试提示；不提供复杂存储管理。
- 支持个人收件箱、未读、已读和受控跳转；离线后按游标补拉。
- Audit 默认不开放管理页面；若未来开放，仅提供权限严格限制的只读摘要，敏感查看/导出保留 PC。

## 10.3 可访问性与安全交互

- 状态不能只依赖颜色；键盘导航、焦点顺序、ARIA、触控尺寸、错误关联和超时提示符合 PF-01 规范。
- 文件上传明确显示“上传完成不等于可用”；扫描未知不得显示成功。
- Notification 跳转前验证目标可见性；无权限时显示稳定提示，不泄露目标是否存在。
- Audit 导出和敏感查看显示用途、保留期和水印/下载到期信息。

---

# 11. 错误、安全、审计与可观测性

## 11.1 稳定错误类别

- 数据库：`PF04_DB_NOT_READY`、`PF04_DB_UNIT_DRIFT`、`PF04_DB_MANIFEST_INVALID`。
- File：`FILE_UPLOAD_EXPIRED`、`FILE_TYPE_REJECTED`、`FILE_SCAN_PENDING`、`FILE_MALICIOUS`、`FILE_NOT_AVAILABLE`、`FILE_REFERENCE_EXISTS`。
- Notification：`NOTIFICATION_AUDIENCE_INVALID`、`NOTIFICATION_ALREADY_PUBLISHED`、`NOTIFICATION_EXPIRED`、`NOTIFICATION_TARGET_INVALID`。
- Audit：`AUDIT_FACT_INVALID`、`AUDIT_FACT_CONFLICT`、`AUDIT_WRITE_UNAVAILABLE`、`AUDIT_QUERY_SCOPE_INVALID`、`AUDIT_INTEGRITY_FAILED`。

## 11.2 安全控制

- 上传防 zip bomb/解析炸弹、路径穿越、双扩展名、MIME 欺骗、超大文件和恶意内容；预览转换若引入必须独立沙箱，本阶段不默认实现。
- 对象存储 Bucket/容器隔离 quarantine 与 available，凭据最小权限并轮换；日志禁止记录预签名 URL。
- Notification 正文输出编码并限制富文本白名单；禁止任意 HTML/脚本和开放重定向。
- Audit Metadata 采用 allowlist、字段分类和服务端脱敏；禁止密码、token、连接串、文件内容和完整 Authorization header。
- 租户、用户、服务身份、权限与业务资源授权分别校验；不能仅凭知道 NId 访问资源。

## 11.3 可观测性

- 指标：各 MigrationUnit 版本/readiness、Operation 时长/失败、Audit Outbox 年龄/积压/拒绝/完整性、File 隔离数/扫描延迟/恶意率/存储清理、Notification 投递延迟/未读/SignalR 连接与补拉。
- Trace 跨 API、Outbox/Inbox、扫描器、对象存储和 SignalR；高基数 NId 不作为默认指标标签。
- 日志结构化、脱敏，包含 TenantNId 的受控散列/安全维度、OperationId、TraceId、ModuleKey 和稳定错误码。
- 告警区分安全事件、合规缺口、容量、外部适配器故障与数据库门禁，不用单一“服务异常”掩盖根因。

---

# 12. 自动化测试与验收设计

## 12.1 测试层次

- 架构测试：模块引用、Contracts 零基础设施依赖、无跨 Schema Repository/外键、无独立 Host。
- 单元测试：三模块状态机、权限、幂等、脱敏、保留、受众、跳转和失败策略。
- 集成测试：SQLite 与 PostgreSQL 18 显式迁移、账本独立、Outbox/Inbox、并发、对象存储/扫描适配器故障。
- 契约测试：OpenAPI、事件版本、错误码、NId/TenantNId、游标和敏感字段扫描。
- E2E：真实 Identity/PF-01/PF-02 稳定实现具备后验证 PC/PDA/Mobile 关键路径。

## 12.2 数据库十项门禁

1. Development Shared 默认及显式 PerService。
2. Test/Staging/Production 拒绝 Shared。
3. 宿主一个登记、四个独立 MigrationUnit。
4. PostgreSQL Schema 与 SQLite 前缀/账本等价隔离。
5. Shared 只 provision 一次，DDL 按物理目标串行。
6. 父子 Operation、幂等恢复和单元诊断。
7. 任一必需 Unit 异常则 Business NotReady。
8. CoreControlReady 仅开放受限管理通道。
9. Production Plan/审批/备份绑定完整快照。
10. 无 `EnsureCreated`、Secret/物理目标输入和隐式拓扑数据移动。

## 12.3 模块关键矩阵

- Audit：事务 Outbox 写失败、中央暂时不可用、重复/冲突事件、脱敏、跨租户、哈希链篡改、checkpoint、保留/冻结、敏感查询失败关闭、导出过期。
- File：扩展名/MIME/魔数不一致、零字节/超限、hash 不符、分片重放、扫描 clean/malicious/unavailable/timeout、下载竞态、冻结、活跃引用、清理恢复、预签名泄露扫描。
- Notification：大受众分批、重复发布、排期竞态、撤回、过期、重复 SignalR、断线补拉、游标篡改、已读并发、未读缓存失效、非法跳转和富文本 XSS。

## 12.4 验收证据

执行任务必须记录命令、退出码、测试通过/失败/跳过数、覆盖率、报告/截图路径、依赖提交、Provider/拓扑和外部限制。缺少 PF-02、PostgreSQL、对象存储、扫描器或真实 Identity 时只能标记“待验收”，不得以 Mock 证明生产门禁完成。

---

# 13. 开发任务依赖

```text
PF-02 TASK-SD-001～004 + PF-04 数据库扩展契约
  → TASK-PF04-001 宿主集成与三个迁移单元
      → TASK-PF04-002 Audit 核心
          ├→ TASK-PF04-003 File 核心
          └→ TASK-PF04-004 Notification 核心
              → TASK-PF04-005 API/事件/权限与宿主集成
                  ├→ TASK-PF04-006 PC/PDA/Mobile 页面
                  └→ TASK-PF04-007 安全、可观测性与生命周期作业
                      → TASK-PF04-008 契约/E2E/阶段验收
```

Audit 先行是为了给 File/Notification 的高风险操作和平台事实提供统一接收面；它不允许跨库同步事务。任务只能在用户书面批准派遣后执行，且应按实际稳定契约再次细化。

---

# 14. 开发任务拆分

> 以下均为九字段设计卡，统一状态“待细化/任务待确认”。建议提交仅是未来原子提交意图，不授权提交。

## TASK-PF04-001 接入宿主 Manifest 与三个独立迁移单元

**状态：** 待细化/任务待确认
**目标：** 在 PF-02 稳定实现上接入 File/Notification/Audit MigrationUnit、父子 Operation、双层 readiness 与 Provider 等价命名空间。
**输入文档：** 本文第 1～6、8.1、12.2 节；蓝图 32/33；实施 05 的已验收输出。
**依赖：** TASK-SD-001～004 完成并书面验收；子 MigrationUnit/内部协调扩展点稳定。
**允许修改范围：** 经协调后的 SystemData 宿主装配、PF-04 模块骨架、三个迁移产物及测试；禁止重写 PF-02 控制面、修改其他阶段代码或部署并行文件。
**预期输出：** 签名宿主 Manifest、四 Unit 快照、三个 Schema/前缀及账本、父子 Operation、CoreControlReady/BusinessReady。
**验证与证据：** 数据库十项门禁、架构引用测试、SQLite/PostgreSQL 18 空库与升级链、Secret 扫描。
**结果回写：** 实际项目/UnitKey、表/账本、Manifest/Operation DTO、测试数、偏差和未验收项回写第 16/17 节。
**建议提交：** `feat(systemdata): add pf04 migration units`

## TASK-PF04-002 实现统一 Audit 事实源

**状态：** 待细化/任务待确认
**目标：** 实现不可变 AuditFact、可靠接收、完整性链、保留/冻结、受权查询与高风险失败关闭。
**输入文档：** 本文 7.1、8～9、11～12 节；蓝图 30/31。
**依赖：** TASK-PF04-001；Identity/服务身份与权限稳定契约。
**允许修改范围：** Audit 模块及所属测试/迁移；禁止改写业务模块领域历史、直接接管 Identity/ReferenceData 本地表。
**预期输出：** Audit Contracts、Ingress、事实/哈希/checkpoint、查询/导出、Retention/LegalHold、Outbox/Inbox 适配。
**验证与证据：** 幂等冲突、脱敏、篡改检测、跨租户、积压恢复、敏感操作失败关闭及 PostgreSQL 分区验证。
**结果回写：** 字段、算法版本、保留政策、API/事件/权限、性能和迁移兼容边界。
**建议提交：** `feat(systemdata): add append-only audit module`

## TASK-PF04-003 实现安全 File 生命周期

**状态：** 待细化/任务待确认
**目标：** 实现上传会话、隔离验证、病毒扫描、对象存储、授权下载、引用、冻结与清理。
**输入文档：** 本文 7.2、8～12 节。
**依赖：** TASK-PF04-001、002；对象存储与扫描器技术选型/Secret 方案书面确认。
**允许修改范围：** File 模块、适配器、所属测试/迁移及经协调的配置；禁止设计 Operations Center/Collaboration 领域。
**预期输出：** File Contracts/状态机、IObjectStorage/IVirusScanner、隔离与 available 边界、授权下载、引用与清理作业。
**验证与证据：** 恶意样本安全测试、类型欺骗、扫描不可用、并发、预签名限制、冻结/引用清理和 Secret 扫描。
**结果回写：** 存储/扫描产品与版本、大小/超时策略、状态/API/事件、测试报告和外部限制。
**建议提交：** `feat(systemdata): add secure file module`

## TASK-PF04-004 实现持久 Notification 收件箱

**状态：** 待细化/任务待确认
**目标：** 实现公告、系统通知、受众快照、个人收件箱、已读/失效、受控跳转及 SignalR 提示。
**输入文档：** 本文 7.3、8～12 节。
**依赖：** TASK-PF04-001、002；Identity 用户/角色与 PF-02 组织/资源目录稳定契约。
**允许修改范围：** Notification 模块、SignalR 适配、所属测试/迁移；禁止实现聊天或修改 Collaboration。
**预期输出：** 发布/受众/Delivery 模型、持久收件箱 API、游标补拉、未读计数、SignalR 和安全跳转解析。
**验证与证据：** 大受众、幂等投递、断线补拉、已读并发、XSS/开放重定向、权限与缓存一致性。
**结果回写：** 受众解析边界、容量批次、Hub/事件/API、页面消费契约及性能数据。
**建议提交：** `feat(systemdata): add notification inbox module`

## TASK-PF04-005 冻结跨模块 API、事件、权限与宿主装配

**状态：** 待细化/任务待确认
**目标：** 冻结三个公开 Contracts、权限清单、Outbox/Inbox 和宿主路由，证明无跨模块数据访问。
**输入文档：** 本文第 2～6、9、11 节；TASK-PF04-002～004 输出。
**依赖：** TASK-PF04-002～004；Identity/PF-02 稳定集成契约。
**允许修改范围：** PF-04 Contracts、宿主组合根、Gateway/OpenAPI、契约/架构测试；禁止跨模块 Repository 和非 PF-04 业务改造。
**预期输出：** v1 DTO/events/errors/permissions、路由、可信身份/租户映射、审计接入和未来物理拆分适配点。
**验证与证据：** OpenAPI/event snapshot、架构测试、权限负例、跨租户、幂等与敏感字段扫描。
**结果回写：** 最终契约版本、资源名、路由、兼容策略、测试数和联合验收缺口。
**建议提交：** `feat(systemdata): integrate pf04 module contracts`

## TASK-PF04-006 实现 PC/PDA/Mobile 页面与交互

**状态：** 待细化/任务待确认
**目标：** 在 PF-01 稳定壳上实现 File/Audit 管理、公告管理和三端个人通知/文件交互。
**输入文档：** 本文第 10～12 节；PF-01 已验收输出。
**依赖：** TASK-PF04-005；PF-01、Identity 真实授权与 API 稳定。
**允许修改范围：** PF-04 前端 routes/pages/stores/components/tests；禁止重做平台壳、Mock Auth 或其他阶段页面。
**预期输出：** 权限路由、状态/错误/空态、上传与扫描反馈、收件箱补拉、高风险审计交互和三端适配。
**验证与证据：** unit/component/E2E、键盘/ARIA/触控、XSS、断线、权限负例、截图与覆盖率。
**结果回写：** 路由、组件、终端差异、截图/报告、测试数及外部待验收项。
**建议提交：** `feat(frontend): add file notification audit pages`

## TASK-PF04-007 完成安全、生命周期作业与可观测性

**状态：** 待细化/任务待确认
**目标：** 完成保留/冻结/清理、重试/死信、容量保护、指标/Trace/告警和安全加固。
**输入文档：** 本文第 6～8、11～12 节；前三模块实现输出。
**依赖：** TASK-PF04-002～005。
**允许修改范围：** PF-04 后台作业、模块策略/指标/告警及测试；禁止创建独立 Scheduler Service 或扩大基础设施范围。
**预期输出：** 可恢复批作业、积压策略、完整性锚定适配、存储/扫描保护、Dashboard/告警定义和运行手册。
**验证与证据：** 故障注入、时钟/并发、容量、恢复、Legal Hold、日志/Trace/Secret 扫描和告警演练。
**结果回写：** 阈值、SLO、作业租约/批次、告警、恢复步骤、测试与演练证据。
**建议提交：** `feat(systemdata): harden pf04 operations`

## TASK-PF04-008 完成 PF-04 契约、E2E 与阶段验收

**状态：** 待细化/任务待确认
**目标：** 从空环境和升级环境验证数据库门禁、三模块纵切、真实授权、三端页面、故障恢复与未来消费者契约。
**输入文档：** 本文全部章节；TASK-PF04-001～007 输出；稳定 PF-01/PF-02/Identity。
**依赖：** TASK-PF04-001～007 全部完成。
**允许修改范围：** PF-04 验收测试、fixture、报告与本文执行记录；只修复阻塞缺陷，不扩张业务范围。
**预期输出：** SQLite/PostgreSQL、Shared/PerService、对象存储/扫描、SignalR、Audit 完整性及 PC/PDA/Mobile 全链证据。
**验证与证据：** 第 12 节全部矩阵、构建/覆盖率/E2E/安全扫描/性能/恢复演练，记录真实退出码与依赖提交。
**结果回写：** 第 15～17 节、总 Todo 与实际稳定契约；未满足项明确阻塞，不伪装完成。
**建议提交：** `test(systemdata): verify pf04 modules`

---

# 15. 完成标准

- PF-02 数据库控制面前置真实通过，三 MigrationUnit 独立且宿主 BusinessReady 聚合正确。
- Audit 成为统一追加合规事实源，但不覆盖业务历史/Outbox；高风险查看也可靠审计。
- File 只有 Clean 才可用，扫描不可用保持隔离，消费者只保存 FileNId。
- Notification 以持久收件箱为事实源，SignalR 可丢失后补拉，且不包含聊天能力。
- TenantNId、权限、幂等、Outbox/Inbox、脱敏、保留、并发、安全和可观测性通过测试。
- 三端边界、API/事件版本、未来物理拆分接口和运行手册形成稳定输入。
- 用户完成书面验收并明确是否允许后续开发；在此之前阶段仍不是开发授权状态。

---

# 16. 执行记录

截至 2026-08-12：仅完成仓库/蓝图盘点及详细设计草案；未派遣任务，未修改生产/测试代码，未执行构建或测试，未提交 Git。并行工作树改动均保持原状。

---

# 17. 下一阶段输入契约

未来消费者只能依赖经验收的：

- `FileMetadataV1`、上传会话、受权下载、引用登记/释放与 File 状态事件；不得直读 File Repository。
- Notification 个人收件箱、系统通知发布、受控跳转和 SignalR 提示契约；不得将其当作聊天。
- `AuditFactV1` 写入、查询/导出权限、完整性与保留语义；不得把本地审计表视为中央权威。
- 三个 MigrationUnit 的稳定 UnitKey、版本/readiness 和 SystemData Host Manifest 聚合契约。

PF-10 仅消费 ServerMonitor 范围；PF-10A KnowledgeBase 若使用文件，只消费 File 公开契约。

---

# 18. 文档自审清单

- [x] 一个阶段、一个宿主、三个独立内部模块；未创建微服务。
- [x] 数据库拓扑先行，Shared/PerService、Manifest、父子 Operation、readiness 与 bootstrap 边界完整。
- [x] PostgreSQL Schema/SQLite 前缀、迁移产物与账本独立。
- [x] 无跨模块 Repository/表/外键；未来拆分只走契约。
- [x] TenantNId 来自可信上下文，未回退为领域 TenantId。
- [x] Audit、File、Notification 领域、API、事件、权限、页面、安全、测试均覆盖。
- [x] 明确无 EnsureCreated、无静默数据库切换、无隐式拓扑数据移动。
- [x] PF-02/PF-01/PF-03 未完成内容未写成当前稳定实现。
- [x] 九字段任务卡全部为“待细化/任务待确认”，没有派遣授权。
- [x] 未修改实施 03/04/05/06、生产代码或测试代码，未提交 Git。
