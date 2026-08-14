# 08-Industrial Platform Collaboration开发实施方案

# Industrial Platform Collaboration开发实施方案

> 当前里程碑范围：PF-05 创建 `Collaboration.Service`，交付登录用户之间的一对一文本、图片和文件聊天；本阶段只设计 Messaging、Presence、AttachmentIntegration，不实现 PF-06 RemoteAssistance、群聊、语音、音视频会议、机器人或外部联系人。详细设计已获确认，后续开发已获原则许可；但当前会话明确只完成设计文档定稿，不开发、不派遣、不构建、不测试、不提交。

版本：V1.0

日期：2026-08-13

确认日期：2026-08-14

阶段：PF-05 Collaboration

阶段状态：完整详细设计与九字段任务卡已于 2026-08-14 获用户全文最终确认，后续开发已获原则许可；当前会话明确不开发、不派遣、不构建、不测试、不提交。全部任务卡继续保持“待细化/任务待确认”，其含义是尚未完成前置依赖核验与实际派遣，不得据此宣称任务已派遣或开发已启动。

模块或服务：

```text
Collaboration.Service
├── Messaging
├── Presence
└── AttachmentIntegration
```

Service Host 与内部模块：

```text
PF-05 创建 Collaboration.Service
本阶段：Messaging / Presence / AttachmentIntegration
PF-06 后续加入：RemoteAssistance
```

阶段不等于微服务。三个内部模块独立建模、独立公开契约、独立权限资源、独立 Schema 或表前缀、独立迁移单元/账本和独立测试；禁止跨模块直读 Repository 或数据表，禁止建立跨模块数据库外键，并保留未来物理拆分能力。

数据库初始化与环境引导：

```text
ServiceKey：collaboration
Provider：Development 云端调试默认 PostgreSQL；显式关闭 RemoteDevelopment 时允许 SQLite 回退
LogicalDatabaseName：collaboration_db
MigrationUnit：collaboration.messaging / collaboration.presence / collaboration.attachment-integration
物理目标：仅由受信 DatabaseTopology 与 SystemData 控制面解析
Development：默认 Shared，可显式 PerService
Test / Staging / Production：仅 PerService
```

技术：

```text
.NET 10 WebAPI / Clean Architecture / DDD
SqlSugar / PostgreSQL 18 / SQLite Development fallback
SignalR / Redis（多实例背板与短期 Presence）
RabbitMQ / Outbox / Inbox
Vue 3 / TypeScript / Pinia / PC-PDA-Mobile 统一前端
```

规格与蓝图依据：

- `CLAUDE.md`
- `docs/blueprint/01-Industrial Platform 总体架构设计 V1.0.md`
- `docs/blueprint/04-Vue3 PCPDAMobile 三端统一架构设计.md`
- `docs/blueprint/05-Industrial Platform平台基础功能与独立模块设计.md`
- `docs/blueprint/07-PostgreSQL数据库规范及分库设计.md`
- `docs/blueprint/08-RabbitMQ事件总线设计规范.md`
- `docs/blueprint/09-Industrial Platform开发总TodoList.md`
- `docs/blueprint/20-Industrial Platform部署架构设计.md`
- `docs/blueprint/27-Industrial Platform API规范.md`
- `docs/blueprint/28-Industrial Platform前端工程规范.md`
- `docs/blueprint/29-Industrial Platform自动化测试体系.md`
- `docs/blueprint/30-Industrial Platform日志审计与可观测性平台设计.md`
- `docs/blueprint/31-Industrial Platform权限体系与安全架构设计.md`
- `docs/blueprint/32-Industrial Platform Service Host与内部模块边界.md`
- `docs/blueprint/33-Industrial Platform SystemData数据库编排与环境引导.md`
- `docs/implementation/01-Industrial Platform开发启动实施方案.md`
- `docs/implementation/03-Industrial Platform Identity Service开发实施方案.md`
- `docs/implementation/04-Industrial Platform视觉主题与平台外壳开发实施方案.md`
- `docs/implementation/05-Industrial Platform SystemData开发实施方案.md`
- `docs/implementation/06-Industrial Platform ReferenceData Service开发实施方案.md`
- `docs/implementation/07-Industrial Platform File Notification Audit开发实施方案.md`
- `docs/implementation/TEMPLATE-开发实施方案.md`

实施 07 当前为 `V1.0-draft` 且任务未确认、未开发，File 与 Audit 接口只作为动态依赖和适配端口，不作为已实现稳定契约。PF-05 进入开发前必须重新核验 File 与 Audit 的已批准契约和真实验收状态。

---

# 1. 文档说明

## 1.1 文档目的

本文是 PF-05 Collaboration 的开发详细设计、任务依赖、验收门禁和九字段任务卡维护源。当前按章节与用户逐项确认；只有已确认内容进入本文，未确认设计不得推断为开发契约。

本文只授权设计文档维护，不授权修改生产代码、测试代码、派遣开发任务、提交 Git 或执行实施任务。

## 1.2 当前输入状态

- 首次盘点分支为 `develop`，HEAD 为 `48c5374`，与 `origin/develop` 对齐。
- 当前仓库没有 `Collaboration.Service`、Messaging、Presence、AttachmentIntegration、SignalR Hub、SignalR Redis 背板或 `FileNId` 消费实现。
- 当前代码中的 `messaging` 仅表示云开发基础设施 RabbitMQ profile，不是 Collaboration Messaging 领域模块。
- Identity 当前代码已提供字符串 `UserNId` 和可信租户声明；现有 `ICurrentUser` 的租户属性仍名为 `TenantId`。PF-05 领域和公开契约统一使用 `TenantNId`，接入层负责适配现有声明命名，不将其误建模为新的租户实体。
- PF-01 平台外壳与 Identity 当前提交可作为现有代码基线；PF-02 SystemData 正在工作树中并行实现，未提交内容不作为稳定契约。
- 实施 07 已进入当前 HEAD，但仍明确为设计草案、任务待确认、未开发；PF-05 仅定义 File/Audit 适配端口和依赖门禁。
- 工作树中的 `CLAUDE.md`、`README.md`、`src/DEBUGGING.md`、SystemData/Gateway/解决方案/测试改动均属于并行工作，本文件不得覆盖、暂存、回退或提交这些改动。
- 本轮没有运行构建、测试或环境联调；历史测试数字不是 PF-05 的新鲜验证证据。

## 1.3 已确认设计记录

| 日期 | 设计范围 | 结论 |
| --- | --- | --- |
| 2026-08-13 | 宿主、三个模块迁移单元与分层 readiness | 已确认，写入第 4 章 |
| 2026-08-13 | 调试基础设施默认值 | 本地调试默认使用云端 Docker 基础设施；显式关闭 `RemoteDevelopment.Enabled` 时才使用 Development SQLite 回退 |
| 2026-08-13 | 一对一会话与发起资格 | 同租户有效用户默认互相可见；会话按规范化参与人对唯一，写入第 5 章 |
| 2026-08-13 | 消息、顺序、幂等与撤回 | 会话内服务端严格递增序列；普通用户发送后 2 分钟内可撤回，写入第 6 章 |
| 2026-08-13 | 未读、已读、个人隐藏与离线补拉 | 用户级单调已读游标，不逐消息写扩散；隐藏不删历史，新消息自动恢复，写入第 7 章 |
| 2026-08-13 | Presence 多设备与隐私投影 | 多连接聚合、20 秒心跳、60 秒租约、15 秒抖动宽限；离线只显示粗粒度最后活动，写入第 8 章 |
| 2026-08-13 | File 契约成熟度边界 | PF-05 只冻结 `IChatFileGateway` 所需语义；实施 07 的上传会话路由仅为候选映射，不作为稳定接口，写入第 9 章 |
| 2026-08-13 | REST、SignalR 与可靠消息 | SignalR 推送普通用户安全消息投影，REST 负责权威查询和缺口校正；Outbox/Inbox 至少一次且幂等，写入第 10 章 |
| 2026-08-13 | 合规访问与法律保全分级 | 单条/单会话查看需独立权限、原因和短时提权；批量导出与解除保全需双人审批；创建保全立即生效并复核，写入第 11 章 |
| 2026-08-13 | 三端页面与 PC 快捷抽屉 | PC 抽屉支持文本、图片、文件发送；搜索建会话、深度历史与合规管理留完整页/管理路由，写入第 12 章 |
| 2026-08-13 | 容量验收环境 | 以当前 2 核 4GB 云服务器的真实 Docker 组合为基准验收；不以脱离现状的高并发数字作为当前门禁，同时保留水平扩展能力 |
| 2026-08-13 | 2C4G 最低性能门禁 | 20 个在线连接、持续 2 msg/s、短时 5 msg/s；历史 2,000 条、Outbox 500 条恢复，并记录共享环境资源曲线 |

---

# 2. 定位、目标与职责边界

## 2.1 负责

- Messaging：登录用户一对一会话、文本/图片/文件消息、顺序游标、离线补拉、未读/已读、发送幂等、撤回墓碑、个人隐藏和消息 Outbox。
- Presence：用户多设备、多连接的在线派生状态、心跳、断开、抖动宽限和过期；在线状态不得作为身份或授权依据。
- AttachmentIntegration：Messaging 与 PF-04 File 公开契约之间的防腐适配、附件状态衔接、重新授权下载、引用/保留/法律保全协调和故障降级。
- PC 完整会话页与快捷抽屉，以及 PDA/Mobile 全屏聊天路径。
- 当前单租户完整可用，并在身份、数据、缓存、消息、SignalR 和审计边界中使用可信 `TenantNId` 与稳定 `UserNId`。

## 2.2 不负责

- Identity 拥有用户、角色、权限、登录、令牌和会话；Collaboration 不复制或修改 Identity 数据。
- SystemData 拥有行政组织；Collaboration 不建立自己的组织树。
- File 拥有文件二进制、隔离、扫描、对象存储和授权下载；Messaging 只保存 `FileNId` 与必要展示快照。
- Audit 拥有统一追加型合规事实；Messaging 保留业务消息事实并通过可靠 Outbox 提交审计事实，不直写 Audit 表。
- Notification 是系统到用户的单向通知/收件箱，不承担用户聊天。
- PF-06 RemoteAssistance 拥有屏幕协助会话、参与人授权、WebRTC/Screego/TURN 和共享审计；PF-05 只预留公开入口与消息卡片扩展边界。
- 首期不实现群聊、频道、语音消息、音视频会议、外部联系人、机器人、链接预览、任意 HTML、端到端加密或远程控制。

---

# 3. 前后端及跨服务协作目标

```text
Identity 可信 UserNId / TenantNId
    ↓
Messaging REST 命令与查询
    ↓
消息事实 + Messaging Outbox 同事务
    ↓
SignalR 低延迟提示 + 客户端游标补拉
    ↓
PC 抽屉/完整页、PDA/Mobile 全屏聊天

AttachmentIntegration ↔ File 公开契约
Messaging/Audit Outbox → Audit 公开接收契约
```

SignalR 不作为消息事实源。连接、推送失败或重复不得改变持久消息结果；客户端在初次进入、重连和检测到游标缺口时通过 REST 补拉。

---

# 4. 总体架构、数据库迁移单元与 readiness

## 4.1 宿主与模块边界

```text
Collaboration.Service
├── Collaboration.Api（宿主装配、认证、路由、Hub、健康汇总）
├── Messaging
│   ├── Domain / Application / Contracts / Infrastructure
│   ├── PostgreSQL schema: collaboration_messaging
│   ├── SQLite prefix: collaboration_messaging_
│   └── ledger: collaboration_messaging_schema_migrations
├── Presence
│   ├── Domain / Application / Contracts / Infrastructure
│   ├── 单实例内存状态；多实例 Redis 派生状态
│   └── 独立迁移单元与账本，首版允许没有领域表
└── AttachmentIntegration
    ├── Domain / Application / Contracts / Infrastructure
    ├── PostgreSQL schema: collaboration_attachment
    ├── SQLite prefix: collaboration_attachment_
    └── ledger: collaboration_attachment_schema_migrations
```

Presence 的 PostgreSQL Schema、SQLite 前缀和 migration ledger 必须保留独立声明，即使首版迁移产物只建立账本或空版本标记。这样可以独立报告版本、readiness 和未来持久化扩展，同时禁止为了“有表”而保存无业务价值的在线历史。

## 4.2 SystemData 登记与迁移编排

- 宿主稳定登记为 `ServiceKey=collaboration`、`LogicalDatabaseName=collaboration_db`。
- 宿主 Manifest 包含 `collaboration.messaging`、`collaboration.presence`、`collaboration.attachment-integration` 三个必需模块迁移单元；每个单元独立声明 MigrationAssembly/Bundle、不可变版本/checksum、Owner、DesiredState、AutoMigrate 请求、Schema/前缀和 ledger。
- Provider、Owner 和自动策略的实际配置值由未来执行任务依照已批准平台清单登记；Manifest 不包含物理服务器地址、密码、私钥、管理员连接串或可还原 Secret。
- `PhysicalDatabaseName` 只能由受信 `DatabaseTopology` 解析。Development 默认 Shared、可显式 PerService；Test、Staging、Production 只允许 PerService。
- Shared 只共享物理目标；三个模块的 Schema/表前缀、迁移产物、migration ledger、Repository、权限和数据所有权不得合并。
- 同一物理目标的 PostgreSQL DDL 使用 physical-target advisory lock 或等效锁串行执行；每个模块仍独立记录迁移和 readiness。
- 拓扑变化不得隐式 copy、rename、merge 或 split；已有数据时必须报告 drift，并走显式迁移/import。
- SystemData 不可用、目标错误、迁移失败、版本不一致或 drift 时保持 NotReady；禁止管理员凭据自行建库、`EnsureCreated` 和静默切换数据库。
- 生产执行 `plan → 审批 → 备份 → apply → verify`，并以 `OperationId` 贯穿计划、执行、日志、Trace、指标和审计。

## 4.3 调试环境默认值

- 本地调试默认使用云端 Docker 基础设施。`RemoteDevelopment.Enabled=true` 时，Collaboration 使用私有本地配置所提供的云端 PostgreSQL 18、Redis、RabbitMQ、对象存储/File 端点和可观测性端点；私有地址与凭据不得进入仓库、日志、Trace、审计或 API。
- 云端调试仍属于 Development 环境，默认 `DatabaseTopology.Mode=Shared`；显式验证时可以选择 PerService。是否远程连接与 Shared/PerService 是两个独立配置维度。
- 只有显式设置 `RemoteDevelopment.Enabled=false` 或私有配置不存在时才使用 Development SQLite 回退；回退仍使用显式版本化迁移、三个独立 ledger 和相同逻辑版本语义。
- 远程模式下 SystemData 或云端数据库不可用时，Collaboration 保持 NotReady，不得自动切换到 SQLite。Redis、RabbitMQ 或 File 的故障按第 4.4 节分层处理。

## 4.4 分层 readiness 与降级

| 状态 | 条件 | 对外行为 |
| --- | --- | --- |
| 宿主 NotReady | SystemData 握手失败、身份认证配置无效、物理目标错误、Messaging 迁移失败/drift/版本不符 | `/health/ready` 返回 503，禁止接受聊天业务命令 |
| Messaging Ready | 消息库、迁移、事务与 Outbox 可用 | REST 文本发送、历史、游标和离线补拉可用 |
| Attachment Degraded | File/扫描不可用或 AttachmentIntegration 迁移/适配失败 | 文本聊天继续；附件创建、发送或下载返回稳定降级错误，不把未扫描文件显示为可用 |
| Audit Buffered | 中央 Audit 暂不可用但 Messaging 本地 Audit Outbox 可可靠写入 | 普通聊天可完成并等待重投；合规查看、导出、法律保全等高风险操作失败关闭 |
| Single-instance Realtime Degraded | 单实例 Redis 不可用 | 可使用实例内 SignalR/Presence；REST 事实不受影响，明确报告 Redis 降级 |
| Multi-instance Realtime NotReady/Degraded | 部署声明多实例但 Redis 背板或全局 Presence 不可用 | 禁止把本地连接误报为全局在线；实时能力按发布门禁停止接流量或明确降级，REST 消息事实按独立健康结果继续 |

宿主对外保留标准 liveness/readiness，并提供受权限保护、脱敏的模块诊断。liveness 不检查下游业务依赖；readiness 汇总必需事实能力，模块诊断分别报告 Messaging、Presence、AttachmentIntegration、Outbox、Redis 背板、File 和 Audit 状态。

---

# 5. 一对一会话、成员关系与发起资格

## 5.1 Conversation 聚合

`Conversation` 是 Messaging 的一对一会话聚合根，稳定身份为 `ConversationNId`。核心业务字段：

```text
TenantNId
NId（对外为 ConversationNId）
ParticipantLowUserNId
ParticipantHighUserNId
Status
LastMessageSequence
LastMessageNId
LastMessageOn
```

`ParticipantLowUserNId` 与 `ParticipantHighUserNId` 是对两个规范化 `UserNId` 做确定性序后的结果，只用于唯一性和查询，不表达发起人、所有者、主次或权限高低。会话在同一 `TenantNId + ParticipantLowUserNId + ParticipantHighUserNId` 下全生命周期唯一；参与人顺序不同、重复请求或并发发起均返回同一会话，不创建第二条记录。

会话成员创建后不可替换。首期不允许将一对一会话升级为群聊，也不通过修改成员复用 ConversationNId；未来群聊必须使用独立模型和新会话身份。

## 5.2 ConversationMember

每个会话固定两条 `ConversationMember` 记录，主要业务字段：

```text
TenantNId
ConversationNId
UserNId
DisplayNameSnapshot
JoinedOn
VisibilityState
HiddenOn
LastReadSequence
LastReadOn
```

- `UserNId` 是 Identity 的跨服务稳定标识；Messaging 不保存 Identity 内部数据库键，不建立跨服务外键。
- `DisplayNameSnapshot` 仅用于历史展示和 Identity 短暂不可用时的降级，不是用户目录权威；正常列表和搜索优先使用 Identity 公开契约返回的当前显示信息。
- 两名成员必须属于同一个可信 `TenantNId`，且 `UserNId` 不得相同。
- `VisibilityState=Hidden` 只影响该成员自己的会话列表。新消息到达、本人再次发送或显式恢复时，会话重新显示；隐藏不删除消息、不影响对方视图、审计、保留或法律保全。
- 会话不使用成员“退出”语义；一对一成员不能退群、移除或转让。

## 5.3 用户搜索、可见性与发起资格

首期采用已确认的企业内部目录规则：同一可信租户内的有效登录用户默认可以互相搜索并发起会话。

发起会话必须同时满足：

1. 发起人已认证，可信身份上下文提供非空 `TenantNId` 与 `UserNId`。
2. 发起人拥有 `collaboration.messaging.conversation.start`。
3. 目标 `UserNId` 由 Identity 公开用户目录契约确认存在、属于同一租户且当前有效。
4. 目标不是发起人本人。
5. 未命中已有参与人对时才创建会话；命中时幂等返回已有 `ConversationNId`。

客户端不得提交或覆盖 `TenantNId`，不得用显示名、登录名、邮箱或组织名称替代 `UserNId`。用户搜索采用受限关键字、游标分页、最小字段投影和速率限制，禁止批量导出租户用户目录或通过错误差异枚举跨租户用户。

首期不引入好友关系、联系人审批、陌生人请求箱、拉黑、按行政组织限制可见范围或跨租户联系人。未来若出现合规隔离需求，应在 Identity/SystemData 的公开可见性策略与 Collaboration 发起策略之间增加显式契约，不得由 Messaging 自行读取组织表推断。

## 5.4 用户状态变化

- 发起时必须同步或基于有明确有效期的 Identity 用户状态投影确认双方有效；无法安全确认目标用户时拒绝新建会话，不使用陈旧数据放宽访问。
- 既有会话的历史事实不因用户停用而删除。被停用用户不能建立连接、发送、标记已读、隐藏或下载附件；另一名有效用户仍可按权限查看已有历史，但向停用用户发送新消息时返回稳定的“目标用户不可用”错误，避免制造无法到达的新消息。
- 用户重新启用后恢复原会话，不创建新会话；原 ConversationNId、消息序列、已读游标和保留状态不变。
- 用户显示名变化只影响当前目录投影；已保存快照可保留发送时语义，不批量改写历史消息。
- 用户被软删除或永久去标识化时，Messaging 按 Identity/Audit 已批准的公开事件和合规策略更新展示投影，不能直接读取 Identity Repository，也不能物理删除受保留或法律保全约束的聊天事实。

## 5.5 数据一致性与索引

- 会话创建事务同时写入 `Conversation`、两条 `ConversationMember`、本地审计 Outbox 和必要集成 Outbox。
- 数据库使用活动记录唯一约束保证规范化参与人对唯一；应用层先查后建只用于正常路径，数据库冲突后重新读取既有会话作为并发收敛结果。
- `ConversationMember` 是 Conversation 同库子实体，使用模板规定的 `(Conversation_Id, Conversation_IsDeleted) → Conversation(Id, IsDeleted)` 复合外键和双重软删除过滤。
- `UserNId` 是跨服务引用，不建立数据库外键，也不复制 Identity 实体生命周期字段。
- 会话列表按成员本地投影查询，索引至少覆盖 `TenantNId + UserNId + VisibilityState + LastMessageOn + ConversationNId`；最终索引必须以 PostgreSQL `EXPLAIN (ANALYZE, BUFFERS)` 和目标数据量验证，不能仅凭本设计机械创建。

## 5.6 稳定错误边界

| 场景 | HTTP | 错误码 |
| --- | ---: | --- |
| 与自己发起会话 | 400 | `COLLAB_CONVERSATION_SELF_NOT_ALLOWED` |
| 发起权限不足 | 403 | `COLLAB_CONVERSATION_START_FORBIDDEN` |
| 目标不存在、跨租户或调用方不可见 | 404 | `COLLAB_USER_NOT_FOUND` |
| 目标已停用 | 409 | `COLLAB_USER_INACTIVE` |
| Identity 目录无法安全确认 | 503 | `COLLAB_IDENTITY_DIRECTORY_UNAVAILABLE` |

跨租户、不可见和不存在统一返回 404，避免目录枚举。并发命中既有会话是成功幂等结果，不返回 409。

---

# 6. 消息模型、顺序、幂等与撤回

## 6.1 Message 聚合与类型

`Message` 是 Conversation 下的消息事实，稳定身份为 `MessageNId`。主要业务字段：

```text
TenantNId
NId（对外为 MessageNId）
ConversationNId
Sequence
SenderUserNId
ClientMessageNId
MessageType
TextContent
ReplyToMessageNId（可空）
AttachmentNId（可空，引用 AttachmentIntegration 本地绑定身份）
AcceptedOn
RetractedOn（可空）
RetractedByUserNId（可空）
RetractionReasonCode（可空）
```

首期消息类型固定为：

| 类型 | 语义 |
| --- | --- |
| `Text` | 纯文本消息，不接受 HTML |
| `Image` | 通过 AttachmentIntegration 绑定的已清洁图片 |
| `File` | 通过 AttachmentIntegration 绑定的已清洁文件 |
| `SystemTombstone` | 查询投影中的系统墓碑语义，不允许普通客户端直接创建 |

`SystemTombstone` 不是另行插入并改变会话顺序的新聊天消息；它是原消息在撤回或合规处置后的稳定投影类型，继续占用原 `Sequence`。数据库保留原始消息事实、撤回信息及受控合规内容，普通查询只返回墓碑 DTO。

PF-06 RemoteAssistance 后续可以通过 Messaging 公开扩展契约投递邀请卡片，但 PF-05 不预先实现远程会话类型、WebRTC 字段或可执行卡片逻辑。新增消息类型必须版本化并保证旧客户端安全降级为不可执行的系统提示。

## 6.2 文本内容规范

- 文本最大 4,000 个 Unicode 标量值；API 和前端使用同一计数语义，不能按 UTF-16 code unit 误截断代理对。
- 输入执行 Unicode NFC 规范化，统一 `CRLF/CR` 为 `LF`，保留用户可见换行；去除首尾不表达内容的空白后不得为空。
- 拒绝 NUL、双向文本控制字符、非必要 C0/C1 控制字符和不可安全展示的输入；允许普通制表符和换行时必须在渲染层按纯文本处理。
- 前端使用文本节点/安全转义展示，禁止 `v-html`、任意 HTML、脚本、Markdown 执行、链接预览和自动加载外部图片。
- 首期 URL 仅作为纯文本。若客户端提供“打开链接”，必须显式提示并应用安全协议 allowlist，不由服务端抓取目标内容。
- 日志、Trace、指标和普通审计摘要不记录消息正文；统一 Audit 只接收必要元数据和受控摘要。

## 6.3 会话内顺序

- 每个 Conversation 独立维护从 1 开始严格递增的 `Sequence`；服务端在消息事务内分配，客户端时间、接收时间和 `ClientMessageNId` 均不决定顺序。
- `Conversation.LastMessageSequence` 与消息写入在同一事务、同一并发控制下推进；同一会话并发发送必须串行分配不同 Sequence。
- 不要求不同 Conversation 之间存在全局顺序，也不使用数据库内部 `Id` 作为游标。
- 序列一经分配不复用。撤回、隐藏、保留清理或合规处置均不能重排后续消息。
- SignalR 事件携带 ConversationNId、MessageNId、Sequence 和最小提示字段。客户端发现 `Sequence > localSequence + 1` 时必须通过 REST 补拉缺口，不能把实时到达顺序当作最终顺序。

## 6.4 发送幂等

- 客户端每次新的发送意图生成稳定 `ClientMessageNId`；网络超时或不确定结果只能重试相同标识和相同语义。
- 服务端幂等键固定为 `TenantNId + SenderUserNId + ClientMessageNId`，并保存规范化后的请求指纹。该键跨会话唯一，防止客户端错误地把同一发送意图复用于另一会话。
- 重复请求的 Conversation、类型、规范化文本、回复目标和附件绑定完全一致时，返回原 `MessageNId`、Sequence 和 AcceptedOn，不重复写 Message、Outbox 或审计事实。
- 同一幂等键对应不同语义时返回 409 `COLLAB_MESSAGE_IDEMPOTENCY_CONFLICT`，不得覆盖原消息。
- 服务端另行支持标准 `Idempotency-Key` 请求头时，它只作为 HTTP 重放保护；消息领域幂等仍以 `ClientMessageNId` 为权威，两者冲突时拒绝请求。

## 6.5 附件消息占序时机

- 图片和文件必须先由 PF-04 File 完成隔离、校验和扫描，并由 AttachmentIntegration 确认达到可发送状态。
- 上传中、隔离中、扫描中、扫描未知或拒绝状态不创建 Message，也不占用 Sequence。前端把这些状态显示为本地附件草稿，不得显示为已发送消息。
- File 达到可发送状态后，客户端以原 `ClientMessageNId` 提交附件消息；Message、附件绑定快照和 Messaging Outbox 在同一 Messaging 事务中完成。
- File 在预检与提交之间失效、冻结或状态变化时，发送事务拒绝并返回稳定错误；不得先创建占位消息后异步替换为附件。
- 文本与附件首期不混合在同一消息。用户可以先发文本再发附件，二者拥有独立 MessageNId、ClientMessageNId 和 Sequence，从而避免附件失败造成文本语义不确定。

## 6.6 服务端与客户端状态

- 消息事务提交成功后，服务端事实状态为 `Accepted`。消息与 Messaging Outbox 同事务；SignalR 或 RabbitMQ 发布失败不回滚已接受消息，由 Outbox 重试。
- “编辑中、发送中、上传中、等待扫描、发送失败、待重试”是客户端本地状态或附件草稿状态，不写成双方共享的消息事实。
- 客户端只有收到成功响应，或超时后用相同 ClientMessageNId 查询/重试确认原结果，才能显示“已发送”。
- 不提供原地编辑。内容更正通过发送新消息表达，必要时可以引用原 MessageNId。

## 6.7 撤回墓碑与合规处置

- 普通发送者可以在 `AcceptedOn` 后 2 分钟内撤回自己的消息。服务端以 UTC 时间判断；客户端倒计时只作提示，不能作为授权依据。
- 撤回命令必须携带原始并发版本并保持幂等。重复撤回返回当前墓碑结果；窗口已过返回 409 `COLLAB_MESSAGE_RETRACTION_WINDOW_EXPIRED`。
- 撤回不物理删除、不释放 Sequence、不删除附件引用，也不改写 Outbox 历史。普通用户查询返回原 Sequence 上的 `SystemTombstone`，正文、文件名及附件下载入口不可见。
- 已被法律保全的消息仍可生成普通视图墓碑，但保全事实和合规副本不受影响。
- 合规管理员不能冒充发送者执行普通撤回。违规内容处置使用独立权限、必填原因、双版本校验和 `ComplianceDisposition` 事实，使普通视图显示墓碑，并向 Audit 可靠记录操作者、理由、范围和时间。
- 合规查看原始内容使用独立权限、必填理由、短时授权和再审计；撤回本身不保证收件人此前未看到内容。

## 6.8 历史分页与补拉游标

- 历史查询以 `Sequence` 为稳定锚点，默认倒序取得较旧页并在 DTO 中按显示顺序返回；不得使用 offset 分页。
- API 对外使用签名/完整性保护的不透明游标，至少绑定 TenantNId、UserNId、ConversationNId、方向、锚定 Sequence、查询投影版本和有效期，禁止跨用户或跨会话复用。
- 断线补拉使用 `afterSequence` 语义向前读取；响应返回连续消息、`NextAfterSequence` 和服务端当前 `HighWatermarkSequence`。
- 单页大小采用受控默认值和上限；具体数值在 API 契约章节统一冻结。客户端必须循环补拉直至本地游标达到 HighWatermark，不能假设一次响应覆盖全部离线消息。

## 6.9 稳定错误边界

| 场景 | HTTP | 错误码 |
| --- | ---: | --- |
| 文本为空、超长或含禁止控制字符 | 400 | `COLLAB_MESSAGE_CONTENT_INVALID` |
| 非会话成员或会话不可见 | 404 | `COLLAB_CONVERSATION_NOT_FOUND` |
| 目标用户当前不可接收新消息 | 409 | `COLLAB_RECIPIENT_INACTIVE` |
| ClientMessageNId 重用但语义不同 | 409 | `COLLAB_MESSAGE_IDEMPOTENCY_CONFLICT` |
| 回复目标不属于同一会话 | 400 | `COLLAB_REPLY_TARGET_INVALID` |
| 附件尚不可发送 | 409 | `COLLAB_ATTACHMENT_NOT_SENDABLE` |
| 撤回窗口已过 | 409 | `COLLAB_MESSAGE_RETRACTION_WINDOW_EXPIRED` |
| 消息并发版本冲突 | 409 | `COLLAB_MESSAGE_CONCURRENCY_CONFLICT` |

---

# 7. 未读、已读、个人隐藏与离线补拉

## 7.1 用户级已读游标

每条 `ConversationMember` 只维护本用户在本会话中的单调已读游标：

```text
LastReadSequence
LastReadOn
```

- `LastReadSequence` 初始为 0。更新到 Sequence=N 表示该成员已读完本会话中截至 N 的全部可见消息，而不是只读了第 N 条。
- 已读游标只能前进，重复提交相同或更小值按幂等成功处理，不得回退。
- 请求值不得超过服务端当前 `HighWatermarkSequence`，也不得越过调用者尚不可见的消息边界；非法值返回 400，不静默截断。
- 服务端应以“客户端实际呈现并确认进入可见范围的最高连续 Sequence”推进已读，不能仅因收到 SignalR 提示、预取页面或后台同步就自动标记已读。
- 已读是用户级、跨设备共享事实，不按连接或设备分别保存。任一设备成功推进后，其他设备通过实时提示或后续 REST 同步更新。
- 不建立逐消息 `MessageRead` 表，不为双方每条消息写回执，避免消息量乘成员数的写扩散。

## 7.2 未读计数

未读权威语义是“调用者 LastReadSequence 之后、对该调用者可见且计入未读的对方消息数量”。下列内容不计未读：

- 调用者自己发送的消息；
- 已投影为撤回/合规处置墓碑的消息；
- 因权限或保留策略不再向该用户可见的记录；
- 仅用于系统同步、游标或技术状态的内部事件。

由于存在本人消息和墓碑，未读数量不能简单等于 `HighWatermarkSequence - LastReadSequence`。实现应使用按成员维护的未读计数投影，或基于受索引支持的增量查询计算；数据库中的 Message/Member 游标与投影版本是权威，Redis 只允许作为可失效缓存。

写入对方消息时，在同一 Messaging 事务内推进会话高水位并更新接收方未读投影；发送方未读不增加。撤回未读消息时相应减少接收方未读投影，但不得使计数为负；投影异常通过可重建对账修复，不改写消息事实。

## 7.3 已读命令与回执范围

- 客户端使用幂等命令推进 `LastReadSequence`，提交 ConversationNId、目标 Sequence 和成员并发版本。
- 已读更新与成员游标、未读投影、本地审计 Outbox 在同一事务提交。
- 首期向对方仅展示会话级“已读至某条”的派生状态，不生成逐消息永久回执。发送方可根据对方 LastReadSequence 判断自己的某条消息是否已被越过。
- SignalR 的 `ReadCursorAdvanced` 只携带 ConversationNId、ReaderUserNId、LastReadSequence、ReadOn 和版本，不携带消息正文；丢失后可由会话详情查询恢复。
- 用户停用、会话非成员或并发版本冲突时拒绝推进；跨租户和非成员统一按会话不可见处理。

## 7.4 个人隐藏

成员本地视图增加：

```text
VisibilityState: Visible | Hidden
HiddenOn
HiddenThroughSequence
```

- 隐藏命令记录当时的 `HighWatermarkSequence` 为 `HiddenThroughSequence`，只将该会话从本人列表移除。
- 隐藏不改变 LastReadSequence，不自动把消息标为已读，不删除 Conversation/Member/Message，不影响对方视图、附件引用、审计、保留或法律保全。
- 隐藏后若收到 Sequence 大于 HiddenThroughSequence 的对方新消息，会话在同一消息事务内自动恢复为 Visible；本人显式打开、恢复或重新发送时也恢复显示。
- 隐藏会话仍可通过稳定 ConversationNId 访问，但必须经过成员身份和权限校验。普通列表默认排除 Hidden；可提供“已隐藏会话”筛选用于恢复，不实现回收站语义。
- 重复隐藏和重复恢复均幂等。隐藏与新消息并发时，以较高 Sequence 的新消息恢复规则为准，禁止新消息被竞争条件继续隐藏。

## 7.5 离线补拉

客户端对每个已知会话保存“已连续接收 Sequence”，该值只是本地同步位置，不等于服务端 LastReadSequence。同步流程：

```text
初次进入/重连/SignalR 缺口
→ 获取会话摘要与 HighWatermarkSequence
→ GET messages?after={localContiguousSequence}
→ 校验响应连续性并合并去重
→ 持续读取 NextAfterSequence
→ localContiguousSequence == HighWatermarkSequence
```

- SignalR 只发送变化提示；即使推送重复、乱序或丢失，REST 补拉仍能恢复完整持久消息。
- 客户端以 MessageNId 和 Sequence 双重去重。相同 Sequence 对应不同 MessageNId 或相同 MessageNId 对应不同内容时视为协议/完整性错误，停止推进并上报 TraceId。
- 游标必须绑定租户、用户、会话、方向和投影版本；签名无效、过期、跨用户/跨会话复用或锚点超范围时返回稳定错误，不静默重置到最新位置。
- 墓碑占用原 Sequence，因此撤回不会形成游标空洞。补拉取得的是当前授权投影；合规原文不通过普通消息 API 返回。
- 一个会话积压超过单页上限时分批补拉，并设置服务端页大小、请求速率和响应体上限，避免断线恢复造成突发负载。

## 7.6 保留边界后的同步

- 服务端响应提供 `EarliestAvailableSequence` 与 `HighWatermarkSequence`。正常情况下客户端请求锚点不得小于最早可用序列减一。
- 消息已按保留策略清理且客户端落后于 `EarliestAvailableSequence` 时，返回 410 `COLLAB_MESSAGE_HISTORY_EXPIRED`，并携带最早可用 Sequence、清理时间边界和脱敏恢复指引。
- 客户端必须显示“更早历史已按保留策略清理”的明确状态，并从 `EarliestAvailableSequence` 重新建立连续同步位置；不得伪装成从未存在消息。
- 法律保全可能让合规副本继续存在，但不自动延长普通用户历史可见期；普通 API 是否继续展示由消息保留/可见策略决定。

## 7.7 稳定错误边界

| 场景 | HTTP | 错误码 |
| --- | ---: | --- |
| 已读 Sequence 超出可见高水位 | 400 | `COLLAB_READ_CURSOR_INVALID` |
| 游标签名无效、过期或作用域不符 | 400 | `COLLAB_SYNC_CURSOR_INVALID` |
| 会话不存在、跨租户或非成员 | 404 | `COLLAB_CONVERSATION_NOT_FOUND` |
| 已读/隐藏成员并发版本冲突 | 409 | `COLLAB_MEMBER_CONCURRENCY_CONFLICT` |
| 请求位置早于普通历史保留边界 | 410 | `COLLAB_MESSAGE_HISTORY_EXPIRED` |

---

# 8. Presence 多设备、多连接与隐私

## 8.1 定位与权威边界

Presence 是短期派生状态，只表达“某个可信用户当前至少存在一个有效实时连接”。它不是身份、授权、用户启停、消息投递成功或设备可信度的权威来源。

- Identity 决定用户是否有效以及 JWT 是否可接受；Presence 不复制用户账号状态。
- Messaging 决定会话成员、消息和离线补拉；Presence 不保存消息、不修改 Messaging Repository，也不阻止向暂时离线的有效用户发送消息。
- SignalR 连接状态可能延迟、重复或短暂错误，页面必须把在线提示视为近似状态，不能据此执行高风险业务判断。
- Presence 不建立永久在线历史业务表。普通在线状态保存在进程内存或 Redis TTL 租约；合规所需连接元数据通过受控 Audit 事实记录。

## 8.2 连接身份与多设备模型

每个连接使用：

```text
TenantNId
UserNId
DeviceNId
ConnectionNId
ServerInstanceNId
TerminalType
ConnectedOn
LastHeartbeatOn
LeaseExpiresOn
```

- `TenantNId` 与 `UserNId` 只来自 SignalR 握手时验证通过的 JWT/可信身份上下文，客户端查询参数或 Hub 方法不得覆盖。
- `ConnectionNId` 由 SignalR 连接提供并在租约键中不可枚举地使用。
- `DeviceNId` 是客户端安装实例生成的随机稳定标识，用于诊断多设备，不是认证因素；删除本地数据后可以变化。
- `TerminalType` 只能从受控 PC/PDA/Mobile 枚举和服务端校验后的终端声明取得，不信任任意客户端字符串。
- 一个用户可以同时使用多个设备；同一设备可因多个标签页、前后台切换或重连拥有多个 ConnectionNId。在线聚合不能假设一用户一连接。

## 8.3 认证、分组与订阅

- SignalR 建立连接时验证 Access Token、租户、用户状态和必要的 `collaboration.presence.connect` 权限；认证失败不得创建租约。
- 连接建立后把身份固定到连接生命周期。Token 到期或安全版本失效时，按统一 Identity/SignalR 契约断开或要求重新认证，不能让旧连接无限存活。
- 服务端只将连接加入其自身用户组与经过成员校验的会话组。客户端不得传入任意 UserNId、TenantNId 或 group name 要求加入。
- Presence 查询只允许查看当前用户既有会话对方，以及用户搜索结果中当前有权看到的同租户用户；不提供普通用户全租户在线名单或批量导出。
- 用户停用/会话撤销事件到达后，相关连接应被主动断开；事件延迟期间，后续 Hub 方法仍必须重新执行必要授权，不能只信任初次分组。

## 8.4 心跳、租约与抖动

- 客户端正常情况下每 20 秒发送一次应用级心跳或触发等价连接活性续租。
- 每条连接租约 TTL 为 60 秒。续租必须原子校验租约所有者、ServerInstanceNId 和当前身份，禁止旧连接覆盖新连接租约。
- 正常客户端断开时立即移除连接租约，但用户级状态允许 15 秒抖动宽限；异常断开、进程崩溃或网络丢失依靠 TTL 自动过期。
- 心跳周期、TTL 和宽限是首期默认值，可由受信配置收紧，但必须满足 `Heartbeat < Grace < LeaseTtl` 的安全关系并在启动时校验；客户端不能覆盖。
- 服务端使用单调计时处理进程内超时，跨实例记录使用 UTC `DateTimeOffset`；不得用客户端时钟决定在线状态。

## 8.5 用户级在线聚合

```text
任一有效 Connection Lease 存在 → Online
最后一个连接消失但仍在 15 秒宽限内 → Online（抖动保护）
宽限结束且没有新连接 → Offline
```

- 仅在有效连接计数 `0 → 1` 时发布 Online，在宽限后 `1 → 0` 时发布 Offline；多标签页建立、续租或关闭不产生用户级状态风暴。
- 聚合更新必须按 `TenantNId + UserNId` 原子化或使用可证明等价的 Redis 脚本/事务；重复连接/断开事件幂等。
- Online/Offline 变化携带 PresenceRevision。消费者只接受更高 revision，重复或乱序事件不得使状态倒退。
- 在线推送只发给获授权的相关连接；不经 RabbitMQ 广播完整租户 Presence。跨实例实时传播使用 SignalR Redis 背板/受控 Redis channel，长期集成事件只在确有跨服务消费者时版本化发布。

## 8.6 单实例与多实例

| 部署 | 状态存储 | 故障行为 |
| --- | --- | --- |
| 单实例 | 进程内连接登记为主；Redis 可用于统一能力但不是首期强制 | Redis 不可用时继续报告本实例状态，模块标记 Degraded；进程重启后所有状态自然离线并由客户端重连恢复 |
| 多实例 | Redis TTL 租约、用户聚合和 SignalR 背板为必需 | Redis 不可用时禁止把本地连接声明为全局 Presence；实时能力停止接收新流量或明确 NotReady/Degraded，REST 消息事实按独立健康状态继续 |

部署模式由受信配置和实际副本门禁决定。若配置声明单实例但运行环境检测到多个副本，启动/发布验收必须失败，不得在多个局部内存状态上伪装全局在线。

## 8.7 最后活动与普通用户隐私投影

Presence 可以在用户从 Online 转为 Offline 时保留短期 `LastActiveOn`，用于粗粒度展示和诊断。普通聊天用户不得取得精确时间，服务端返回以下枚举投影而不是原始时间：

| 距当前时间 | 用户界面文案键 |
| --- | --- |
| 不超过 5 分钟 | `JustOnline`（刚刚在线） |
| 大于 5 分钟且不超过 1 小时 | `RecentlyOnline`（最近在线） |
| 大于 1 小时且不超过 24 小时 | `OnlineToday`（今天在线） |
| 超过 24 小时、无记录或策略不允许 | `Offline`（离线） |

- 分类由服务端使用当前 UTC 时间计算，客户端只做本地化文案，不接收精确 LastActiveOn。
- Online 用户只返回 `Online`，不返回连接数、设备数、IP、ServerInstanceNId 或心跳时间。
- 精确连接时间、终端、IP 和异常断开信息只进入受控诊断/Audit，按专用权限、保留和再审计规则访问。
- Presence 短期状态的保留期不得被误写成 365 天消息保留；Redis 键随租约过期，必要的最后活动投影采用最小化、独立保留策略。

## 8.8 故障、并发与安全

- 正常断开、TTL 扫描和主动踢出可能重复发生，移除必须幂等；过期扫描不得删除已由新连接续租的记录。
- Redis channel/背板消息至少一次或可能丢失，客户端和服务端均依赖 revision 与重新查询恢复，而不是假设每个变化只到达一次。
- 限制单用户、单设备、单 IP 的并发连接数和心跳速率；超过策略时拒绝最旧连接或新连接的具体规则由安全配置固定并审计，不允许无限占用租约。
- Hub 日志不记录 Access Token、完整 ConnectionNId、IP 明文或用户目录正文；指标标签不得包含 UserNId、DeviceNId、ConnectionNId 等高基数字段。
- Presence 不接受客户端设置“忙碌、离开、隐身”等自定义状态；首期只有 Online/Offline 与粗粒度离线活动投影。

## 8.9 稳定错误边界

| 场景 | 行为/错误码 |
| --- | --- |
| Hub 认证或用户状态无效 | 拒绝连接，使用统一 401/连接关闭语义 |
| Presence 连接权限不足 | 403 / `COLLAB_PRESENCE_CONNECT_FORBIDDEN` |
| 查询目标跨租户、不可见或不存在 | 404 / `COLLAB_PRESENCE_USER_NOT_FOUND` |
| 心跳租约已失效或不属于当前连接 | 409 / `COLLAB_PRESENCE_LEASE_INVALID` |
| 多实例 Redis/背板不可用 | 503 / `COLLAB_REALTIME_BACKPLANE_UNAVAILABLE` |
| 超过连接或心跳速率限制 | 429 / `COLLAB_PRESENCE_RATE_LIMITED` |

---

# 9. AttachmentIntegration 与 File 公开契约

## 9.1 契约成熟度与防腐层

PF-04 实施 07 当前为 `V1.0-draft`，其中 `UploadSession`、`POST /files/upload-sessions` 和完成上传端点是候选详细设计，不是已批准、已实现或已验收的稳定契约。PF-05 不冻结 File 的最终路由、DTO、传输方式或内部状态枚举。

AttachmentIntegration 在 Collaboration 内定义 `IChatFileGateway` 应用端口，只冻结聊天所需的业务语义：

```text
创建聊天用途的上传意图
查询 File 的可发送状态与安全展示元数据
登记/释放聊天业务引用
为当前会话成员请求一次性下载授权
请求消息保留/法律保全协调
消费 File 状态变化
```

未来基础设施适配器把该端口映射到 PF-04 验收后的 File Contracts/API/事件。若最终 File 不使用“上传会话”命名或路由，AttachmentIntegration 只替换适配器，不改变 Messaging 领域模型。PF-05 开发准入前必须核验 PF-04 已批准的 FileNId、状态、上传、引用、下载授权、保留和法律保全契约；未知项不得用猜测 DTO 落地。

## 9.2 上传与本地附件绑定

- 二进制上传由客户端使用 PF-04 File 最终公开上传契约完成。Collaboration 不代理文件流、不接触对象存储、不持有 Bucket、长期下载 URL、扫描器或对象存储凭据。
- 创建上传意图时，通过适配端口声明受控用途 `CollaborationMessageAttachment`、TenantNId、ConversationNId、SenderUserNId、期望文件名/大小/hash 和幂等键。File 是否接受并签发上传能力由 File 自身安全策略决定。
- AttachmentIntegration 创建本地 `ChatAttachment` 绑定，稳定身份为 `AttachmentNId`，主要业务字段：

```text
TenantNId
NId（对外为 AttachmentNId）
ConversationNId
UploaderUserNId
FileNId
Purpose
FileNameSnapshot
MediaTypeSnapshot
SizeBytesSnapshot
ContentHashSnapshot
FileStateProjection
FileStateVersion
ReferenceState
RetentionState
LastSynchronizedOn
```

- 快照只用于消息历史展示、幂等指纹和 File 短暂不可用时的安全降级，不是 File 状态、扫描结论或下载授权的权威来源。
- `FileNId` 是跨模块稳定引用，不建立跨 Schema/跨数据库外键，不复制 File 内部 Id、对象路径、预签名 URL、扫描报告或生命周期公共字段。
- AttachmentIntegration 与 Messaging 通过公开应用契约协作。Messaging 只保存 AttachmentNId；不得直接读取 AttachmentIntegration Repository 或表。

## 9.3 首期文件策略

- 默认单文件上限为 50MB；租户/环境安全策略只能收紧，不能由客户端或 Collaboration 放宽 File 的平台上限。
- 允许类型固定为 PNG、JPEG、WebP、PDF、TXT、CSV、DOCX、XLSX。
- 脚本、可执行文件、宏文档、压缩包、双扩展欺骗、MIME/魔数不一致和恶意内容由 File 做最终权威判断。Collaboration 的扩展名预检只用于即时反馈，不能替代 File 校验。
- 每条 `Image` 或 `File` 消息首期只绑定一个 AttachmentNId；文本与附件不混合。多文件发送表现为多条独立消息，各自拥有 ClientMessageNId、MessageNId 和 Sequence。
- 图片仅使用 File 经授权的安全响应进行受控展示；Collaboration 不生成缩略图、不做 OCR、转码、内容识别或自动加载外部图片。

## 9.4 可发送状态与原子绑定

只有同时满足以下条件才允许创建附件消息：

1. File 公开契约确认文件为可发送/可用且扫描结果为 Clean 或等价最终安全状态。
2. File 的 TenantNId、用途、上传者与 ChatAttachment 声明一致。
3. 当前发送者仍为 Conversation 成员、用户有效且具有发送附件权限。
4. File 大小、类型与 Collaboration 首期策略一致。
5. FileStateVersion 不早于本地已知版本，且文件未被冻结、隔离、拒绝、删除待定或处于法律限制下的不可发送状态。

满足条件后，在一个 Messaging 数据库事务内创建 Message、固化附件安全展示快照、把 ChatAttachment 绑定到 MessageNId、登记待发布的业务引用命令/Outbox，并写 Messaging Outbox。跨 File 的引用登记不能加入本地事务，采用 Outbox、幂等命令和对账实现最终一致性。

上传中、隔离中、扫描中、结果未知或被拒绝时不创建 Message、不占用 Sequence。File 在预检与事务提交之间发生状态变化时，后续 File 事件必须使附件投影立即不可下载；发送适配器还应使用 File 提供的状态版本/授权令牌降低检查与提交竞态。

## 9.5 下载重新授权

- 普通客户端不保存永久下载 URL。每次下载或图片加载前，都向 Collaboration 请求附件访问授权。
- Collaboration 校验可信 TenantNId/UserNId、会话成员、消息当前普通投影可见、消息未撤回/处置、附件用途和本地状态，再通过 `IChatFileGateway` 请求 File 最终授权。
- File 重新校验文件当前状态、租户、业务引用、冻结/保留策略和调用主体后，返回短期、最小权限、一次性或等价受限的下载能力。
- Collaboration 响应不得记录或长期缓存完整预签名 URL。下载失败不能把文件存在性、对象路径、扫描器详情或其他租户信息泄露给调用方。
- 个人隐藏不撤销成员的历史访问权；用户重新打开隐藏会话后仍按当前权限重新授权。消息撤回或合规处置后，普通用户不得取得原附件下载授权。

## 9.6 File 状态变化与 Inbox

AttachmentIntegration 通过版本化 File 状态事件或等价公开变更契约维护安全投影。每个事件至少具有 EventNId、FileNId、TenantNId、状态、FileStateVersion、OccurredOn、Producer、CorrelationNId 和 TraceId。

- Inbox 以 EventNId + ConsumerName 幂等；相同 FileNId 只接受更高 FileStateVersion，重复或乱序事件不得让状态倒退。
- File 变为 Quarantined、Rejected/Malicious、Frozen、DeletionPending、Deleted 或其他不可下载状态时，立即把 ChatAttachment 投影标记为不可用，并使后续授权失败。
- File 恢复 Available 时可以恢复普通下载，但必须保留所有状态历史和审计，且不得自动恢复已被消息撤回或合规处置屏蔽的访问。
- 永久失败进入死信/隔离与人工恢复流程并告警；定期对账主动查询近期活跃、状态不确定和事件积压涉及的 FileNId，不能只依赖事件永不丢失的假设。

## 9.7 保留、引用与法律保全

- 消息存在且在普通保留期内时，AttachmentIntegration 通过 File 公开引用契约维持聊天业务引用。引用键至少绑定 TenantNId、Consumer=`Collaboration`、MessageNId、AttachmentNId、FileNId 和引用版本。
- 消息和附件默认普通保留 365 天。到期流程先将消息普通视图转入过期/清理状态，再通过可靠 Outbox 请求释放普通 File 引用；File 根据自身其他引用、冻结和保留政策决定是否物理清理。
- 法律保全通过独立保全命令/引用协调，绑定 CaseNId、MessageNId/FileNId、范围、开始时间和授权主体。保全引用优先于普通引用释放与自动清理。
- 解除法律保全必须经过 Audit/PF-04 最终批准的权限、原因和审批契约。Collaboration 不自行物理删除 File，也不直接修改 File 保留表。
- 引用登记、释放、保全和解除均幂等、可重试、可对账；失败不得伪装成功。释放失败延迟清理但不影响消息过期投影，保全登记失败则高风险保全操作失败关闭。

## 9.8 故障降级

| 故障 | 行为 |
| --- | --- |
| File 上传能力不可用 | 禁止创建新的附件上传意图；文本聊天继续 |
| File 扫描未完成或扫描器不可用 | 附件保持本地草稿/隔离提示，不创建消息；文本聊天继续 |
| File 状态查询不可用 | 不允许附件发送；已有附件下载失败关闭，不使用陈旧 Available 投影放行 |
| File 下载授权不可用 | 显示“附件服务暂不可用”并允许稍后重试；消息正文/历史仍可查看 |
| File 事件消费积压 | AttachmentIntegration 标记 Degraded，依靠状态版本和授权时重检保持安全；超过阈值触发告警/NotReady 门禁 |
| AttachmentIntegration 自身迁移失败 | 关闭附件发送与下载，Messaging 文本事实保持可用并在模块诊断中明确降级 |

## 9.9 稳定错误边界

| 场景 | HTTP | 错误码 |
| --- | ---: | --- |
| 文件大小或类型不符合聊天策略 | 400 | `COLLAB_ATTACHMENT_POLICY_REJECTED` |
| FileNId、用途、租户、上传者或会话不匹配 | 404 | `COLLAB_ATTACHMENT_NOT_FOUND` |
| 文件仍在隔离/扫描中 | 409 | `COLLAB_ATTACHMENT_SCAN_PENDING` |
| 文件恶意或被安全策略拒绝 | 409 | `COLLAB_ATTACHMENT_REJECTED` |
| 文件已冻结、删除或当前不可下载 | 409 | `COLLAB_ATTACHMENT_NOT_AVAILABLE` |
| File/扫描/下载授权服务不可用 | 503 | `COLLAB_FILE_SERVICE_UNAVAILABLE` |
| 附件状态版本或绑定并发冲突 | 409 | `COLLAB_ATTACHMENT_CONCURRENCY_CONFLICT` |

---

# 10. REST、SignalR、事件与一致性

## 10.1 接口分工

```text
REST
├── 执行业务命令并返回数据库事务结果
├── 查询会话、消息、游标和附件访问授权
└── 初次加载、重连、缺口检测后的权威补拉

SignalR
├── 推送普通用户安全消息投影
├── 推送会话摘要、已读游标和 Presence 变化
└── 提示客户端存在变化或需要 REST 校正

RabbitMQ / Integration Events
├── 跨服务可靠事实与命令协调
├── File / Audit / Identity 等适配
└── 至少一次投递、Inbox 幂等和失败恢复
```

Hub 不直接创建会话、发送/撤回消息、推进已读、隐藏会话、登记附件或执行法律保全。所有改变业务事实的动作必须进入 Application 用例和数据库事务；SignalR 只传播已提交结果。

## 10.2 Gateway 与 REST 路由

Gateway 对外前缀固定为 `/collaboration`，转发时剥离；服务内部路由统一为 `/api/v1`。首期 REST 资源边界：

```text
GET  /api/v1/users?keyword=...                         搜索可发起会话的同租户有效用户

POST /api/v1/conversations                             幂等取得/创建一对一会话
GET  /api/v1/conversations                             会话列表、未读和对方 Presence 投影
GET  /api/v1/conversations/{conversationNId}           会话详情
POST /api/v1/conversations/{conversationNId}/hide      个人隐藏
POST /api/v1/conversations/{conversationNId}/restore   恢复显示

POST /api/v1/conversations/{conversationNId}/messages  发送文本/图片/文件消息
GET  /api/v1/conversations/{conversationNId}/messages  历史分页或 afterSequence 补拉
GET  /api/v1/messages/by-client/{clientMessageNId}      查询不确定发送结果
POST /api/v1/messages/{messageNId}/retract             发送者 2 分钟内撤回
PUT  /api/v1/conversations/{conversationNId}/read-cursor 推进用户级已读游标

POST /api/v1/attachments/intents                       创建聊天附件上传意图的适配入口
GET  /api/v1/attachments/{attachmentNId}               查询附件安全状态投影
POST /api/v1/attachments/{attachmentNId}/authorizations 请求一次性下载/展示授权
```

附件适配入口的最终请求/响应必须映射 PF-04 已验收 Contracts；上述 Collaboration 路由不冻结 File 内部路由。所有列表使用不透明游标、受控默认页大小与最大页大小；具体 DTO 字段、默认值和上限在最终契约表统一冻结。

所有普通请求从可信上下文取得 TenantNId/UserNId。会话、消息或附件不存在、跨租户、非成员或调用方不可见时统一返回 404，避免资源枚举。

## 10.3 命令幂等与并发

- 消息发送以 ClientMessageNId 为领域幂等权威，同时接受统一 `Idempotency-Key` 作为 HTTP 重放保护。
- 创建会话以规范化参与人对自然幂等；附件上传意图、引用、保留、法律保全等跨服务写操作必须带稳定幂等键。
- 已读、隐藏、恢复和撤回是幂等命令，并携带调用方读取到的双版本并发令牌。重复相同结果返回成功；语义冲突返回稳定 409。
- REST 成功响应只能在本地事务提交后返回。若提交成功但响应丢失，客户端使用相同幂等标识查询或重试恢复，不能生成新发送意图。
- API 使用统一 `ApiResult<T>`/`PageResult<T>` 信封、TraceId、稳定错误码和带偏移 ISO 8601 时间，不暴露数据库 Id 或内部堆栈。

## 10.4 SignalR Hub 与安全分组

Hub 候选内部路径为 `/hubs/collaboration-v1`，Gateway 映射后的公开路径由 Gateway 契约冻结。Hub 协议显式版本化；不兼容变更使用新协议版本/路径或协商版本，不能原位破坏旧客户端。

服务端分组：

```text
tenant-user:{TenantNId}:{UserNId}            仅当前用户的多连接
conversation:{TenantNId}:{ConversationNId}   仅经服务端验证的两名成员连接
```

实际 group name 必须使用不可预测/安全编码或内部散列，日志不输出完整值。客户端不得指定 TenantNId、UserNId、ConversationNId 或任意 group name 加组；服务端根据已认证主体和当前成员关系管理分组，并在用户停用、会话授权变化或连接恢复时重新校验。

## 10.5 SignalR 安全投影

已确认采用“推送安全消息投影 + REST 校正补拉”。`MessageAcceptedV1` 向普通会话成员推送：

```text
EventNId
ConversationNId
MessageNId
Sequence
SenderUserNId
ClientMessageNId（仅发送者自己的连接可见，或按专用回执事件返回）
MessageType
TextContent（仅规范化纯文本）
ReplyToMessageNId
AttachmentDisplaySnapshot（AttachmentNId、安全文件名、媒体类型、大小、当前可用提示）
AcceptedOn
ProjectionVersion
```

安全投影不得包含：

- 永久或短期下载 URL、对象路径、Bucket、File 内部 Id；
- 扫描报告明细、病毒特征、Secret 或基础设施地址；
- 已撤回/合规处置消息的原文和附件元数据；
- Audit 合规副本、精确 Presence 最后活动时间、IP 或设备明细。

其他 Hub 事件至少包括：

```text
ConversationChangedV1
MessageRetractedV1
ReadCursorAdvancedV1
AttachmentStateChangedV1
PresenceChangedV1
ResyncRequiredV1
```

每个事件携带 EventNId、对应业务版本/revision、OccurredOn 和最小 Trace/Correlation 信息。客户端按 EventNId、MessageNId/Sequence 或 revision 幂等合并；收到未知事件类型时安全忽略并触发兼容性遥测，不执行任意动作。

## 10.6 推送与 REST 校正流程

```text
REST SendMessage
→ Message + Conversation sequence + projections + Messaging Outbox 同事务提交
→ API 返回 Message DTO
→ Outbox dispatcher 发布 SignalR 安全投影/跨服务事件
→ 在线客户端按 Sequence 合并
→ 若 Sequence 连续：立即展示
→ 若重复：幂等忽略
→ 若缺口/乱序/未知投影版本：调用 REST afterSequence 补拉
```

- 客户端连接成功或重连后，先取得服务端会话摘要/HighWatermark，再从本地连续 Sequence 补拉；不能等待服务器重放所有 SignalR 事件。
- SignalR 可以重复、乱序或丢失。REST Message DTO 是当前普通授权投影的权威校正来源。
- 如果实时投影先到而发送 REST 响应后到，发送端用 MessageNId/ClientMessageNId 合并本地临时项，不能显示重复消息。
- `ResyncRequiredV1` 只提示哪些会话/资源需要校正，不携带缺失正文，也不能代替 REST 补拉。

## 10.7 Messaging Outbox

消息、会话、已读、隐藏、撤回、附件绑定等需要对外传播的事实，与所属业务数据及本地审计 Outbox 在同一 Messaging 事务提交。Outbox 主要字段：

```text
EventNId
ModuleKey
EventType
EventVersion
AggregateNId
AggregateVersion / Sequence
TenantNId
Payload
OccurredOn
AvailableOn
LeaseOwner
LeaseExpiresOn
AttemptCount
PublishedOn
LastErrorCode
TraceParent / CorrelationNId
```

- Dispatcher 使用短租约、Skip Locked/等价机制批量领取，避免多副本重复同时处理；租约过期后可恢复。
- 发布使用 Publisher Confirm 或目标通道的明确确认。只有确认成功才写 PublishedOn；进程在发布成功、落账失败之间崩溃时允许重复发布，由消费者幂等消化。
- 重试使用带抖动的指数退避，区分可重试基础设施错误与永久契约/安全错误。最大尝试次数和退避参数由受信配置冻结并纳入测试，不由业务请求控制。
- 超过阈值或永久失败进入模块独立隔离/DLQ 状态，保留脱敏错误、事件摘要和恢复操作，不删除原 Outbox 记录；积压年龄/数量超过门限使模块 Degraded/NotReady 并告警。
- SignalR 推送处理器消费已提交 Outbox 投影，不直接从 RabbitMQ consumer 或其他模块读取 Messaging Repository 重新拼装正文。

## 10.8 模块独立 Inbox

Messaging、Presence、AttachmentIntegration 分别拥有 Inbox/消费位点语义；即使共享宿主，也不得合并成可跨模块访问的 Repository 或账本。

```text
InboxEventNId
ConsumerName
EventType / EventVersion
Producer
AggregateNId / SourceVersion
ReceivedOn
ProcessedOn
Status
AttemptCount
LastErrorCode
```

- 同一 EventNId + ConsumerName 只产生一次业务效果；业务更新与 Inbox 成功标记在同一模块事务内提交。
- 对具有 SourceVersion/Revision 的投影，只接受更高版本；重复和乱序事件按幂等成功确认，不回退本地状态。
- 消费者使用 Manual ACK：本地事务提交后才 ACK。失败按分类重试，永久失败进入模块独立隔离队列并可人工重放。
- Identity 用户状态、File 状态、Audit 接收回执等事件均只通过公开版本化契约消费；不得以 Inbox 为理由跨模块读表补齐数据。

## 10.9 RabbitMQ 与同宿主协作

- RabbitMQ 用于跨 Service Host 的可靠集成事件，以及需要持久重试/解耦的跨模块协调；不用于替代一对一聊天的持久消息事实。
- 同宿主模块优先调用公开 Application Contract。若调用结果需要跨事务可靠完成，发起模块写 Outbox，目标模块通过自身 Inbox 处理；禁止分布式事务。
- Routing key 使用稳定领域/聚合/过去式事件语义，事件类型和 payload 版本化；旧版本在兼容窗口内不可原位修改。
- 队列、重试和 DLQ 按消费者模块隔离。Presence 的高频心跳不进入 RabbitMQ；只传播去抖后的必要用户级变化。

## 10.10 OperationId 使用边界

- 普通消息发送、撤回、已读和隐藏是短事务，使用 MessageNId/ClientMessageNId、幂等键和 TraceId，不创建长时 Operation。
- 批量保留协调、法律保全、合规导出、历史修复、大规模对账和人工 DLQ 重放等长时操作才使用 OperationId。
- OperationId 必须贯穿 API、Worker、日志、Trace、指标、Audit 和脱敏错误；失败可查询、可重试且不伪装完成。

## 10.11 稳定实时错误与恢复

| 场景 | 行为 |
| --- | --- |
| SignalR 暂不可用 | REST 发送/查询继续；客户端轮询/手动刷新并重连后补拉 |
| 推送重复或乱序 | 按 EventNId、Sequence/revision 幂等合并；缺口走 REST |
| RabbitMQ 暂不可用 | Message 已提交，Outbox 积压并重试；超过门限模块降级并告警 |
| Outbox 永久契约错误 | 隔离/DLQ、失败关闭相关跨服务效果；消息事实不丢失 |
| Inbox 重复 | 返回成功 ACK，不重复业务效果 |
| 客户端投影版本不支持 | 忽略事件并调用 REST；必要时提示客户端升级 |
| 多实例 Redis 背板不可用 | 使用第 8.6 节门禁，不把局部实时状态冒充全局状态 |

---

# 11. 权限、安全、审计、保留与法律保全

## 11.1 权限资源与数据授权

权限资源按模块和动作独立声明，首期至少包括：

```text
collaboration.messaging.conversation.start
collaboration.messaging.read
collaboration.messaging.send
collaboration.messaging.retract
collaboration.messaging.hide
collaboration.messaging.read-cursor.update

collaboration.attachment.send
collaboration.attachment.download

collaboration.presence.connect
collaboration.presence.read

collaboration.compliance.read
collaboration.compliance.read-original
collaboration.compliance.export
collaboration.compliance.dispose
collaboration.compliance.legal-hold.create
collaboration.compliance.legal-hold.release
collaboration.compliance.retention.manage
```

- 权限清单通过 SystemData/Identity 最终稳定的公开注册契约幂等登记。PF-05 不直写 Identity 权限表，不以菜单显示名作为权限。
- 普通消息读取、发送、撤回、隐藏、已读和附件访问必须同时满足对应 RBAC 权限、可信 TenantNId 和 ConversationMember 数据授权。拥有 `collaboration.messaging.read` 不代表可以读取任意会话。
- 合规权限与普通成员权限完全分离。合规查看可在受控范围内访问非本人会话，但不能因此发送、撤回、推进已读或修改成员视图。
- 前端隐藏按钮和路由只是用户体验；REST、Hub、后台任务与消息消费者均独立执行授权和租户校验。
- 跨租户、非成员、不可见与不存在的普通资源统一返回 404；合规查询必须显式指定受批准范围，禁止通过错误差异枚举其他租户。

## 11.2 认证与短时提权

- REST 与 SignalR 只接受 Identity 签发且通过 issuer、audience、签名、有效期、安全版本和撤销校验的令牌。
- 普通聊天使用平台正常认证会话。查看合规原文、单条/单会话合规详情、内容处置等高风险动作还必须通过短时提权认证，并验证最近认证时间/专用 assurance claim 或 Identity 最终等价契约。
- 短时提权凭据只绑定当前 UserNId、TenantNId、动作、范围和短有效期，不能作为通用 Access Token，不能经 URL、日志、前端持久存储或 SignalR payload 传播。
- 批量导出和解除法律保全除提权外还必须满足第 11.7 节双人或等价审批。
- 服务间 File/Audit 调用使用最小服务身份和受控权限，不转发管理员的完整用户 Token 作为跨服务万能凭据；需要保留操作者语义时使用受签名 actor context/最终批准契约。

## 11.3 内容与接口安全

- 消息正文按第 6.2 节规范化并只以纯文本编码输出；禁止任意 HTML、脚本、Markdown 执行、外部图片自动加载和服务端链接预览。
- 限制单用户/IP/租户的会话搜索、发送、撤回、已读、附件意图、下载授权、SignalR 连接和心跳速率。限流键包含可信租户/用户维度，不能只按可伪造请求字段。
- 对用户目录搜索、消息历史、合规查询和导出设置最小关键字长度、页大小、时间窗、总量和并发上限，禁止任意 SQL、任意 JSONPath 和无限导出。
- 响应设置安全缓存策略。聊天正文、附件授权、合规内容和导出状态默认 `no-store`；浏览器历史和 Service Worker 不得持久缓存合规原文。
- Content Security Policy、下载 Content-Disposition、协议 allowlist、XSS/CSV 公式注入防护和文件名规范化遵循 PF-01/PF-04 最终安全契约。
- 日志、Trace、指标、普通 Audit 摘要和异常不得包含消息正文、完整安全文件名、下载 URL、Access Token、ConnectionNId、IP 明文或 File 扫描报告。

## 11.4 普通业务审计

下列动作产生可靠 Audit 事实或本地 Audit Outbox：

- 会话创建/重复命中、消息发送接受或拒绝、撤回；
- 附件绑定、状态拒绝、下载授权请求与结果；
- 已读游标推进、个人隐藏/恢复（按隐私最小化记录）；
- Presence 连接异常、强制断开和连接限制触发；
- 用户状态投影变化、Outbox/Inbox 永久失败、人工重放和数据修复；
- 保留执行、内容处置、合规查看、导出、法律保全创建/复核/解除。

普通审计事实只记录 TenantNId、ActorUserNId/服务主体、动作、ConversationNId/MessageNId/AttachmentNId、结果、原因码、Sequence/版本、OperationId、TraceId、时间和必要终端摘要。正文不复制进统一 Audit；需要证明内容一致性时记录带版本和密钥治理的不可逆内容指纹，不能以可逆编码代替脱敏。

本地 Audit Outbox 写入失败时，对发送、撤回、附件绑定、处置、保留和合规操作失败关闭。中央 Audit 暂不可用但本地 Outbox 已可靠提交时，普通聊天可完成并等待重投；积压超过风险阈值后模块 Degraded/NotReady，不能静默丢失。

## 11.5 合规查看

单条消息或单个会话范围的合规查看流程：

```text
独立 compliance 权限
→ 短时提权认证
→ 必填结构化原因与案件/调查引用
→ 服务端限定租户、会话、时间窗和字段范围
→ 先可靠记录访问意图/事实
→ 返回最小必要内容
→ 再记录结果、数量和异常
```

- 单条/单会话查看不要求双人审批，以免日常调查流程过重；但任一权限、原因、提权或 Audit 写入失败都必须拒绝访问。
- 普通合规详情默认脱敏附件名、用户联系信息和非必要元数据；查看原始正文/附件使用更高的 `read-original` 权限。
- 每次访问单独审计，不能通过一次打开页面获得无限期会话。短时授权到期或查询范围改变时重新验证。
- 合规查看不推进普通用户 LastReadSequence，不改变未读、隐藏或 Presence，也不向会话参与人发送已读回执。

## 11.6 内容处置

- 内容处置与发送者 2 分钟撤回分离。合规人员使用 `collaboration.compliance.dispose`、短时提权、必填原因和双版本，对一条消息创建不可变 `ComplianceDisposition` 事实。
- 处置后普通投影在原 Sequence 显示墓碑；原始内容、内容指纹、处置前后状态和附件引用按保留/法律保全策略受控保存。
- 处置不得物理 UPDATE 清空原文、删除 Message、重排 Sequence 或冒充 SenderUserNId。
- 处置、撤销处置或更正只能追加新事实并提高 disposition version；恢复普通可见性若未来允许，必须有独立权限和审计，本阶段不提供普通 UI。

## 11.7 合规导出与审批

- 合规导出是异步 Operation：先冻结查询条件和授权范围，生成不可变 ExportRequest/checksum，返回 OperationId，由后台按游标读取并生成结果。
- 导出结果交给 PF-04 File 的安全存储契约，标记合规导出用途、短保留、禁止公开分享，下载每次重新授权并审计。
- 导出文件包含请求者、批准者、生成时间、租户、范围、查询摘要、内容指纹/校验信息和可见水印；CSV/Excel 内容执行公式注入防护。
- 批量导出必须双人或等价审批：请求者不得批准自己的请求，批准人具备独立权限，审批绑定请求 checksum、租户、范围和过期时间。任一变化使审批失效。
- 小范围在线查看不能通过分页循环规避导出审批；服务端按累计数量、时间窗、下载行为和风险策略自动要求导出流程。

## 11.8 普通保留策略

- 消息与聊天附件默认普通保留 365 天，从 Message.AcceptedOn 计算；租户政策可以在合规允许范围内延长，缩短必须经过保留策略权限、影响评估和 Audit，且不得追溯绕过既有法律保全。
- 普通用户消息历史到期后按第 7.6 节暴露明确保留边界，不伪装为消息从未存在。撤回、个人隐藏或用户停用不改变保留计时。
- 清理采用可恢复批次、租约、dry-run、候选标记、二次检查和 OperationId。先终止普通可见投影，再释放普通 File 引用；物理清理由拥有数据的模块按法律保全、其他引用和审计完成状态决定。
- 清理不得删除 Audit 事实、Outbox/Inbox 所需证明、内容指纹 checkpoint 或仍被保全的合规副本。失败可重试并告警，不能跳过未知步骤。
- 合规访问审计默认保留 3 年，由 Audit 模块权威执行；Collaboration 只声明分类和期望政策，不直接删除 Audit 数据。

## 11.9 法律保全

`LegalHoldCase`/适配投影至少包含：

```text
CaseNId
TenantNId
ExternalCaseReference
Reason
ScopeType: Conversation | TimeRange | MessageSet
ScopeSnapshot / ScopeChecksum
Status: ActivePendingReview | ActiveReviewed | ReleasePendingApproval | Released
CreatedByUserNId / CreatedOn
ReviewedByUserNId / ReviewedOn
ReleaseRequestedByUserNId / ReleaseApprovedByUserNId / ReleasedOn
```

- 创建法律保全只需专用权限、短时提权、必填案件依据和明确范围，提交后立即进入 `ActivePendingReview` 并阻止相关消息/附件清理，避免等待审批期间发生不可逆删除。
- 创建后必须在受控期限内由另一名合规主体复核，转为 `ActiveReviewed`。逾期未复核不自动解除保全，而是升级告警和管理处置。
- 保全范围支持整个会话、明确时间范围或消息集合；范围快照不可静默扩大/缩小，变更创建新版本并重新复核。
- Collaboration 通过可靠 Outbox/`IChatFileGateway` 为关联 FileNId 建立保全引用。任一附件保全登记失败时 Case 保持活动但 Operation 未完成、清理继续失败关闭并告警。
- 解除保全必须双人或等价审批，申请者不得批准自己的解除请求。审批绑定 Case version、范围 checksum 和当前保全状态；任何变化使批准失效。
- 释放只解除本 Case 的保全引用，不直接物理删除消息或文件；随后由普通保留流程重新评估其他 Case、引用和期限。

## 11.10 可观测性

结构化日志和 Trace 覆盖 REST、SignalR、数据库事务、Outbox/Inbox、Redis、RabbitMQ、File/Audit 适配和长时 Operation。至少提供：

- API：请求量、成功/失败、p50/p95/p99 延迟、429/409/503 分类；
- Messaging：接受/拒绝、序列分配冲突、幂等命中/冲突、撤回、未读投影对账差异；
- Realtime/Presence：连接数、重连、租约续期/过期、用户级状态变化、背板错误、推送成功/失败/延迟；
- Outbox/Inbox：积压数量、最老年龄、重试、永久失败、DLQ、人工重放；
- Attachment：上传意图、等待扫描、状态事件延迟、下载授权失败、File 故障和保全引用差异；
- Compliance：查看/拒绝、提权失败、导出排队/耗时/失败、保全待复核、解除待审批、清理阻塞；
- Retention：候选数、处理数、跳过保全、释放引用失败、最老待处理年龄。

UserNId、ConversationNId、MessageNId、AttachmentNId、CaseNId 和 OperationId 不作为默认指标标签；OperationId/TraceId 可进入日志和 Trace 关联字段。敏感正文、原因详情和案件引用不得进入指标。

## 11.11 健康与告警

- liveness 仅证明进程可响应，不检查数据库、Redis、RabbitMQ、File 或 Audit。
- readiness 按第 4.4 节汇总 Messaging 事实能力、数据库版本和部署所需实时能力；File/Audit/附件/后台积压分别报告 Degraded 或在风险阈值后 NotReady。
- 受保护模块诊断返回依赖类别、稳定错误码、OperationId/TraceId、积压年龄和版本，不返回连接串、目标地址、队列 payload 或 Secret。
- 告警至少覆盖消息事实不可写、序列/投影一致性失败、Outbox/Audit 积压、Redis 多实例背板故障、File 状态滞后、法律保全未完成、待复核逾期、解除审批异常和保留清理失败。

## 11.12 稳定合规错误边界

| 场景 | HTTP | 错误码 |
| --- | ---: | --- |
| 普通权限或会话成员校验失败 | 403/404 | `COLLAB_ACCESS_FORBIDDEN` / `COLLAB_CONVERSATION_NOT_FOUND` |
| 合规权限不足 | 403 | `COLLAB_COMPLIANCE_FORBIDDEN` |
| 短时提权缺失或过期 | 403 | `COLLAB_STEP_UP_REQUIRED` |
| 合规原因/案件引用无效 | 400 | `COLLAB_COMPLIANCE_REASON_INVALID` |
| Audit 无法可靠记录高风险访问 | 503 | `COLLAB_AUDIT_UNAVAILABLE` |
| 导出需要审批或审批失效 | 409 | `COLLAB_EXPORT_APPROVAL_REQUIRED` / `COLLAB_EXPORT_APPROVAL_STALE` |
| 法律保全范围/版本冲突 | 409 | `COLLAB_LEGAL_HOLD_CONFLICT` |
| 解除保全缺少双人审批 | 409 | `COLLAB_LEGAL_HOLD_RELEASE_APPROVAL_REQUIRED` |
| 保留/保全后台 Operation 失败 | 503 | `COLLAB_RETENTION_OPERATION_FAILED` |

---

# 12. PC、PDA、Mobile 页面与交互

## 12.1 前端模块边界

Collaboration 前端在统一 Vue 工程内形成独立模块，不复制平台壳、Identity 用户目录、File 上传实现或 Notification 收件箱：

```text
src/frontend/src
├── api/collaboration/**
├── stores/collaboration/**
├── realtime/collaboration/**
├── components/collaboration/**
├── pages/pc/collaboration/**
├── pages/pda/collaboration/**
├── pages/mobile/collaboration/**
└── router/collaborationRoutes.ts
```

- API/DTO、Pinia Store、SignalR Gateway 和消息合并算法跨三端复用；页面布局按终端分别组合。
- 只通过 PF-01 已验收的顶栏插槽、工作区标签、主题/密度、通用状态组件和路由元数据接入平台壳，不改写壳内部实现。
- File 上传使用 PF-04 最终公开前端契约/适配器；Collaboration 不复制预签名上传、扫描器或下载实现。
- 普通聊天页面与合规管理页面分离路由、权限、缓存和状态 Store，防止普通抽屉意外持有合规原文。

## 12.2 PC 顶栏入口与快捷抽屉

- 顶栏聊天入口展示图标、可访问名称和未读总数。未读超过展示上限时使用 `99+` 等受控文案，完整数值保留给可访问描述。
- 点击打开快捷抽屉；抽屉展示最近会话、当前会话消息和输入区，并提供“在完整页面打开”。关闭再打开时恢复最近会话，但不在持久存储中保存消息正文。
- 已确认抽屉支持发送纯文本、图片和文件，与完整页保持核心聊天能力一致。上传、等待扫描、发送确认和失败重试按第 9 章状态展示。
- 抽屉不提供用户目录搜索/新建会话、深度历史筛选、已隐藏会话管理、合规查看/导出/处置/法律保全。
- 抽屉宽度适应 PC 目标视口和紧凑/舒适密度；不得遮挡平台关键全局操作，较窄视口自动切换为覆盖式面板并提供明确关闭焦点返回。
- 打开/关闭遵循焦点陷阱、Esc、返回焦点和屏幕阅读器标题规则；新消息不抢夺当前键盘焦点。

## 12.3 PC 完整会话页

推荐路由：

```text
/pc/collaboration
/pc/collaboration/conversations/{conversationNId}
```

布局：

```text
┌────────────────┬────────────────────────────┬──────────────────┐
│ 会话列表       │ 消息历史与输入             │ 会话信息（可收起）│
│ 搜索/筛选/未读 │ 日期分隔/墓碑/新消息提示   │ 对方/Presence     │
│ 隐藏管理入口   │ 文本/图片/文件             │ 隐藏/附件摘要     │
└────────────────┴────────────────────────────┴──────────────────┘
```

- 完整页提供同租户用户搜索并发起会话、会话列表、未读筛选、已隐藏会话恢复、深度历史加载和会话级附件摘要。
- 用户搜索明确显示当前有效用户和最小目录字段，不展示全租户导出或精确在线时间。
- 直接访问 ConversationNId 时先校验成员/权限；不可见统一显示资源不可用状态，不泄露对方或会话是否存在。
- 右侧信息区只展示普通成员可见信息，不放合规原文、精确 Presence、连接设备或 Audit 详情。
- PC 工作区使用稳定 workspace identity，避免同一会话重复打开多个标签；抽屉“完整页打开”聚焦既有标签。

## 12.4 PDA 与 Mobile

推荐路由：

```text
/pda/collaboration
/pda/collaboration/conversations/{conversationNId}
/mobile/collaboration
/mobile/collaboration/conversations/{conversationNId}
```

- PDA/Mobile 使用会话列表 → 全屏消息页的分层导航，不复制 PC 三栏或侧抽屉。
- 输入区固定在安全可见区域，适配软键盘、横竖屏和 safe area；消息历史区域独立滚动，切换键盘不能丢失未发送草稿。
- PDA 触控目标不小于 48px，Mobile 不小于 44px；附件按钮、发送、撤回菜单和“新消息”按钮均满足目标尺寸和间距。
- 支持文本、图片、文件选择/上传、扫描等待、发送和授权下载；不提供合规导出、法律保全、内容批量处置或租户在线名单。
- 网络不稳定时允许保留当前内存草稿和同 ClientMessageNId 重试，但首期不建立可长期离线发送队列；未获服务端 Accepted 不能显示为已发送。
- Mobile/PDA 后退优先返回会话列表；深链接认证恢复后回到目标会话，失败则显示稳定不可用状态。

## 12.5 消息列表与滚动

- 消息按服务端 Sequence 展示，使用日期分隔只作视觉投影，不影响顺序。
- 初次进入默认定位最新连续消息；向上触顶或显式“加载更早消息”使用不透明历史游标。
- 用户正在阅读历史且新消息到达时，不强制滚到底部；显示带数量的“有新消息”按钮。只有用户已处于底部阈值内才平滑跟随。
- 列表可以窗口化/虚拟化，但必须保留可访问的加载更早入口、稳定焦点锚点、滚动位置和屏幕阅读器阅读顺序。减少动态效果偏好下禁用非必要平滑动画。
- 墓碑保留原 Sequence，显示“消息已撤回”或“内容已处置”；普通视图不显示原消息类型、正文、文件名、大小或下载入口。
- 历史早于 EarliestAvailableSequence 时显示“更早历史已按保留策略清理”，提供边界说明，不用空白或无限加载伪装。

## 12.6 发送、临时项与多设备合并

- 用户点击发送时创建以 ClientMessageNId 标识的本地临时项。纯文本立即调用 REST；附件先进入上传/扫描草稿，达到可发送状态后再创建 Message。
- REST 成功响应或 SignalR 自己的消息投影到达后，以 ClientMessageNId/MessageNId 合并临时项，替换为服务端 Sequence 和 AcceptedOn，不追加重复气泡。
- 请求超时但结果未知时显示“正在确认”，只能用相同 ClientMessageNId 查询/重试；用户选择“重新发送为新消息”时必须显式生成新标识。
- 发送失败展示稳定错误和可恢复动作。内容校验错误允许编辑；目标停用、权限和附件拒绝不提供盲目自动重试；基础设施暂时故障允许稍后重试。
- 多设备收到已读、隐藏恢复、撤回和消息事件时按业务版本合并。版本缺口触发 REST 校正，不以最后到达事件覆盖更高版本。

## 12.7 附件交互

- 选择文件前显示允许类型与 50MB 默认上限；客户端预检失败立即反馈，但明确最终结果以 File 校验/扫描为准。
- 状态文案区分：等待上传、上传中、校验中、隔离扫描中、可发送、已发送、被拒绝、服务不可用。不得把“上传完成”显示为“附件已发送”。
- 抽屉或完整页关闭时，进行中的上传/扫描草稿由 File/前端适配器按其最终契约恢复；无法可靠恢复时明确提示，不生成幽灵消息。
- 下载/图片展示每次请求短期授权。授权过期、文件冻结、状态变化或 File 故障显示明确占位和重试，不暴露 URL 或底层错误。
- 图片使用受控容器、替代文本和显式打开操作；不自动加载外部 URL。文件显示安全文件名、类型和格式化大小，不将文件名当 HTML。

## 12.8 Presence 与已读展示

- 在线状态同时使用文字、图标和可访问名称，不能只依赖颜色。离线活动按第 8.7 节显示 `刚刚在线/最近在线/今天在线/离线`，客户端不接收精确时间。
- Presence 未知或服务降级时显示“状态不可用”，不能误报离线。
- 会话级已读状态只在发送者自己的消息区域展示简洁状态，并以对方 LastReadSequence 派生；不为每条消息生成独立永久回执。
- 已读推进以实际连续可见的最高 Sequence 为准。后台标签页、抽屉关闭、预取或仅收到通知不能自动标记已读。

## 12.9 页面状态矩阵

| 状态 | 页面行为 |
| --- | --- |
| 首次加载 | 使用骨架/加载状态，禁止假会话或假消息 |
| 无会话 | 显示真实空态；完整页提供搜索用户发起会话，抽屉提供前往完整页 |
| 无消息 | 显示会话已建立的空态和输入区 |
| SignalR 断开 | 保留已加载历史，显示实时连接中断；REST 可用时仍允许发送/刷新，重连后补拉 |
| Messaging 不可用 | 禁止发送并显示服务不可用；不丢本地草稿 |
| File/扫描不可用 | 文本输入/发送继续；附件入口显示降级原因 |
| Presence 不可用 | 显示状态未知，不影响消息发送 |
| 权限不足 | 路由级/操作级无权限状态，不依赖隐藏按钮 |
| 会话不可见 | 统一资源不可用，不泄露对方身份 |
| 历史过期 | 展示保留边界和最早可用历史 |
| 并发/版本冲突 | REST 校正最新状态后提示用户；不静默覆盖 |

## 12.10 合规管理页面

合规能力只提供 PC 独立路由，候选边界：

```text
/pc/collaboration/compliance/search
/pc/collaboration/compliance/exports
/pc/collaboration/compliance/legal-holds
```

- 路由、Store、API client 和缓存与普通聊天分离；离开页面或提权过期后清除合规原文内存状态。
- 搜索页要求先输入原因/案件引用并完成短时提权，再显示限定范围结果；不以普通会话抽屉作为合规入口。
- 导出页显示请求 checksum、范围、申请人、批准人、状态、OperationId、到期和安全下载；请求者不能批准自己。
- 法律保全页显示 `ActivePendingReview/ActiveReviewed/ReleasePendingApproval/Released`、范围版本、待复核/待审批告警和 File 协调状态。
- 内容处置在受控详情中执行，明确“普通视图将显示墓碑，原始合规事实不会物理删除”。

## 12.11 可访问性、主题与视觉验收

- 使用 PF-01 语义 Token，覆盖工业青/科技蓝/中性灰、明亮/暗色/跟随系统和 PC 舒适/紧凑密度；不得写死品牌色表达状态。
- 文本与状态对比度、焦点可见性、键盘顺序、ARIA live 区域、错误关联、抽屉焦点陷阱和屏幕阅读器标签满足平台基线。
- 新消息提示使用节制的 `aria-live=polite` 摘要，不朗读完整正文或敏感文件名；连续消息批量合并播报，避免噪声。
- 消息气泡、墓碑、附件、错误和 Presence 不能只用颜色区分。动效遵守 `prefers-reduced-motion`。
- 截图/E2E 至少覆盖 PC 1280×720、1440×900，PDA 480×800 与 800×480，Mobile 360×800 与 390×844，以及主题/暗色、软键盘/safe-area 的适用真机待验收项。

---

# 13. 自动化测试与验收设计

## 13.1 测试层次

```text
Domain / Unit
    ↓
Application
    ↓
Infrastructure（SQLite + PostgreSQL 18 + Redis + RabbitMQ）
    ↓
API / SignalR / Contract / Event
    ↓
Frontend Unit / Component
    ↓
真实 Gateway + Identity + File + Audit + Browser E2E
    ↓
2C4G 云端 Docker 性能、故障与恢复验收
```

所有执行证据必须记录命令、退出码、通过/失败/跳过数、覆盖率、报告/截图路径、依赖提交、Provider、DatabaseTopology、容器组合、开始/结束时间和外部限制。历史证据必须标记日期与来源，不能写成本轮重新验证。

## 13.2 架构与数据库门禁

架构测试必须证明：

- 只有一个 `Collaboration.Service`，PF-05 不创建 Messaging/Presence/AttachmentIntegration 独立 Host，也不实现 RemoteAssistance。
- 三模块 Domain/Application/Contracts/Infrastructure 边界可识别；Contracts 不引用 Infrastructure，模块间无 Domain/Infrastructure/Repository/表直连。
- Messaging、Presence、AttachmentIntegration 使用独立 Schema/表前缀、迁移产物、ledger、权限和测试；无跨模块数据库外键。
- FileNId、UserNId 等跨服务引用只保存稳定 NId 和必要快照，无 Identity/File 内部 Id 或生命周期复制。
- 无 `EnsureCreated`、管理员自建库、静默 SQLite 回退、物理目标请求输入或隐式拓扑数据移动。

数据库测试覆盖：

1. Development Shared 默认和显式 PerService；Test/Staging/Production 拒绝 Shared。
2. `ServiceKey=collaboration`、`LogicalDatabaseName=collaboration_db` 与三个 MigrationUnit 独立版本/readiness。
3. Shared 物理目标只 provision 一次，三 ledger 独立，DDL 按物理目标锁串行。
4. SystemData 不可用、错误目标、drift、任一必要迁移失败时正确 NotReady；Attachment 单元失败只关闭附件能力的分层门禁按第 4.4 节验证。
5. PostgreSQL 与 SQLite 显式迁移从空库、逐版本升级、失败回滚/恢复、幂等重跑结果一致。
6. 会话参与人对唯一、会话内 Sequence 并发严格递增、ClientMessageNId 唯一、父子复合外键和软删除双重过滤真实生效。
7. 消息、Outbox、未读投影、成员游标和本地 Audit Outbox 的事务原子性。

## 13.3 Messaging 测试矩阵

- 会话：同租户用户、反向参与人顺序、重复/并发发起、自聊拒绝、跨租户 404、目标停用、Identity 不可用。
- 文本：Unicode NFC、Unicode 标量 4,000 上限、换行规范化、空白、控制字符、双向控制、HTML/XSS 和日志正文扫描。
- 幂等：响应丢失重试、同键同语义、同键不同会话/文本/附件冲突、REST 回执与 SignalR 先后竞态。
- 顺序：同会话高并发、跨会话独立、事务失败不复用 Sequence、SignalR 乱序/重复/缺口与 REST 校正。
- 撤回：2 分钟边界前后、服务端 UTC、重复撤回、非发送者、并发版本、墓碑原 Sequence、附件下载失效和法律保全不受影响。
- 已读：只能前进、不超过高水位、多设备同步、后台预取不推进、会话级回执、不逐消息写扩散。
- 未读：本人消息、对方消息、撤回未读消息、投影重建、计数不为负、Redis 缓存失效后数据库一致。
- 隐藏：隐藏不已读、不删除、新消息/本人发送自动恢复、隐藏与新消息并发、恢复幂等。
- 补拉：游标签名/过期/跨用户、分页连续性、重复合并、ProjectionVersion 不支持、2,000 条历史、410 保留边界。

## 13.4 Presence 测试矩阵

- 同用户多设备、多标签页、重复连接/断开、旧租约续租拒绝、主动踢出和 Token/安全版本失效。
- 20 秒心跳、60 秒 TTL、15 秒宽限的边界与受信配置关系校验；测试使用可控时钟，不真实等待完整时长。
- 只在 0→1、1→0 发布用户级变化；重复/乱序 revision 不倒退。
- 单实例进程内恢复、进程重启、Redis 故障降级；多实例 Redis 背板中断时不误报全局在线。
- Presence 查询仅限会话对方/可见用户；无全租户枚举；精确 LastActiveOn、IP、连接数和设备数不进入普通 DTO。
- `Online/JustOnline/RecentlyOnline/OnlineToday/Offline` 时间边界、服务端分类与客户端本地化。
- 连接/心跳/用户/IP 限流，日志和指标高基数字段扫描。

## 13.5 AttachmentIntegration 测试矩阵

- `IChatFileGateway` 使用 PF-04 最终 Contract fixture 验证，不把实施 07 草案 DTO 直接固化。
- 50MB 边界、八类允许格式、双扩展、MIME/魔数冲突、宏文档、脚本、可执行文件、压缩包和恶意结果。
- Uploading/Scanning/Unknown 不占 Sequence；Available/Clean 才发送；预检后状态改变拒绝或立即阻止下载。
- FileNId 的租户、用途、上传者、会话匹配；跨模块无外键；每消息单附件、文本附件不混合。
- 下载每次重新授权；撤回/处置后拒绝；个人隐藏仍可重新授权；URL 不持久化、不进日志。
- File 状态事件重复/乱序/版本倒退、Inbox 原子性、事件积压和主动对账。
- 普通引用、365 天释放、法律保全引用、多个 Case、解除单个 Case 后仍有其他引用、失败关闭与恢复。
- File/扫描/下载授权不可用时文本聊天持续，页面展示明确降级。

## 13.6 REST、SignalR 与事件契约测试

- OpenAPI 快照锁定 Gateway/内部路径、Method、DTO、ApiResult/PageResult、NId、ISO 8601 时间、状态码、错误码、幂等键和并发版本。
- SignalR protocol fixture 锁定 Hub 路径/版本和 `MessageAcceptedV1` 等安全投影；验证不包含下载 URL、扫描详情、精确 Presence 或合规原文。
- REST 成功后推送失败、推送先于 REST 回执、重连、重复、乱序、缺口、未知事件和 `ResyncRequiredV1`。
- Outbox 多副本领取、租约过期、Publisher Confirm、发布后落账前崩溃、退避、DLQ、人工重放和积压门禁。
- 三模块 Inbox 独立、Manual ACK、业务事务原子、重复/乱序/永久失败隔离。
- RabbitMQ 中断恢复、Redis 背板中断恢复、File/Audit/Identity 超时与熔断；错误响应和 Trace 不泄露凭据。

## 13.7 权限、合规与保留测试

- 每个 REST/Hub/Worker 权限正负例、ConversationMember 数据授权、跨租户 404、服务身份最小权限和前端隐藏不能绕过后端。
- 普通 Audit 不含正文；本地 Audit Outbox 失败使关键写操作回滚；中央 Audit 暂不可用后的积压、重投和阈值门禁。
- 合规查看权限、必填原因、短时提权、范围限制、访问前/后再审计；失败时不返回正文。
- 在线查看累计阈值不能规避导出审批；批量导出请求者不能自批、checksum 变化使批准失效、结果 File 短时授权和 CSV 公式注入防护。
- 内容处置不冒充发送者、不物理删除、原 Sequence 墓碑和追加更正。
- 法律保全创建立即阻止清理、待复核逾期告警、范围版本、File 保全失败关闭、解除双人审批、其他 Case 仍有效。
- 365 天普通保留、3 年 Audit 声明、撤回/隐藏/停用不缩短保留、410 历史边界和可恢复清理 Operation。

## 13.8 前端与 E2E

- API/Store/SignalR 合并：本地临时项、ClientMessageNId、响应/推送竞态、缺口补拉、多设备已读和版本冲突。
- PC 抽屉发送文本/图片/文件、完整页用户搜索与新建、历史加载、隐藏/恢复、墓碑、Presence 和附件降级。
- PDA/Mobile 全屏会话、软键盘、横竖屏、safe-area、48px/44px 触控、断线重连和深链接认证恢复。
- 合规 PC 路由与普通 Store 隔离、提权过期清内存、导出审批、法律保全和处置确认。
- 401、403、404、409、410、429、503、Loading、Empty、Offline、Degraded、HistoryExpired 和 Permission 状态。
- 三主题、明暗/跟随系统、PC 双密度、键盘、焦点、ARIA、减少动画、屏幕阅读器摘要和目标视口截图。
- Mock 只用于开发隔离和契约先行；阶段完成必须使用真实 Gateway、Identity、Collaboration、SystemData 已稳定门禁以及 PF-04 File/Audit 已验收契约。

## 13.9 2 核 4GB 云端 Docker 最低性能门禁

当前性能基准不是 Collaboration 独占服务器，而是 2 核 4GB 云服务器上与平台所需基础设施共同运行的真实 Docker 组合。测试报告必须列出当时启动的 Collaboration、Gateway、Identity/SystemData 依赖、PostgreSQL、Redis、RabbitMQ、File/Audit 适配及可观测容器；缺失依赖时不能声称完成共享环境验收。

最低硬门禁：

| 场景 | 目标 |
| --- | --- |
| 同时在线 SignalR 连接 | 20 个，包含多设备/多标签页分布 |
| 持续文本发送 | 2 msg/s，连续 10 分钟 |
| 短时文本峰值 | 5 msg/s，连续 1 分钟 |
| 历史数据 | 单会话 2,000 条，使用游标分页补拉 |
| Outbox 恢复 | 预置 500 条积压，依赖恢复后持续下降并清空；记录实际耗时，不预设脱离环境的 5 分钟承诺 |
| REST 接受延迟 | p95 ≤ 1 秒 |
| 在线推送延迟 | p95 ≤ 2 秒 |
| 历史单页查询 | p95 ≤ 1.5 秒 |
| 稳定性 | 无 OOM、容器重启、持续 swap 抖动或无法恢复的队列增长 |

- 测试同时运行文本发送、Presence 心跳/变化和少量附件状态事件。50MB 附件只验证编排、状态、授权和资源上限，不把外部上传带宽计入消息吞吐。
- 记录整机与每容器 CPU、RSS/working set、内存限制、swap、GC、线程池、数据库连接/慢查询、Redis 延迟、RabbitMQ 队列/confirm、Outbox/Inbox 年龄和错误率的时间序列。
- 负载停止后，资源、连接、租约和积压必须回归稳定；测试报告记录峰值、稳态、恢复时间和瓶颈。
- 在最低门禁通过后可以逐级增加连接和速率，记录首个违反延迟、错误率或资源安全余量的拐点，形成“已验证安全容量”，但探索上限失败不反向否定最低门禁。
- 两个 Collaboration 实例仅在隔离的短时窗口验证 Redis 背板、Presence 全局聚合、Outbox 竞争领取、无业务重复和 Sequence 可恢复；不要求两实例长期与全部容器共驻 2C4G，也不把功能验证结果宣传为容量翻倍。

---

# 14. 开发任务依赖

```text
PF-02 数据库编排稳定契约 + Identity/PF-01 已验收基线
    → TASK-PF05-001 宿主、模块边界与三迁移单元
        ├→ TASK-PF05-002 Messaging 领域、数据与应用用例
        ├→ TASK-PF05-003 Presence 与 SignalR 连接治理
        └→ TASK-PF05-004 AttachmentIntegration（依赖 PF-04 File 稳定契约）

TASK-PF05-002 + 003 + 004 + PF-04 Audit 稳定契约
    → TASK-PF05-005 REST/SignalR/事件/权限与可靠集成
        ├→ TASK-PF05-006 PC/PDA/Mobile 前端
        └→ TASK-PF05-007 合规、保留、安全与可观测性

TASK-PF05-001～007
    → TASK-PF05-008 契约、E2E、2C4G 与阶段验收
```

- TASK-PF05-002、003 可在 001 后并行；004 必须等待 PF-04 File 契约批准，不能用实施 07 草案 DTO 直接开发。
- 005 的 Audit 适配必须等待 PF-04 Audit 稳定契约；前置未满足时只能完成端口/fixture，不能标记任务完成。
- 006 与 007 可在 005 契约冻结后并行，但共享路由、权限清单、错误码和前端合规 Store 的文件必须由单一任务负责或明确协调，避免覆盖并行改动。
- 008 只修复 PF-05 验收阻塞缺陷，不扩张群聊、RemoteAssistance、语音、会议或机器人。

---

# 15. 开发任务拆分

> 以下全部是九字段设计卡，状态统一为“待细化/任务待确认”。建议提交仅表达未来原子提交意图，不授权派遣、开发、测试、提交或发布。开发前必须重新核验工作树、PF-02/PF-04/Identity 的真实提交和验收状态。

## TASK-PF05-001 创建 Collaboration 宿主与三个独立迁移单元

**状态：** 待细化/任务待确认

**目标：** 创建 `Collaboration.Service` 五层宿主骨架，接入 SystemData registration/manifest、三个独立 MigrationUnit/ledger、分层 readiness、云端 Docker 默认调试和 SQLite 显式回退。

**输入文档：** 本文第 1～4、13.2 节；蓝图 07/32/33；PF-02 已验收数据库编排契约。

**依赖：** PF-02 数据库控制面、消费者握手和模块迁移单元扩展契约已稳定；Identity JWT 校验基线可用。

**允许修改范围：** 新建 Collaboration 后端/测试项目、解决方案登记、经协调的 Gateway 路由、Collaboration 配置和测试；禁止修改 PF-02 控制面实现、PF-04 模块、Identity 领域和其他并行文件。

**预期输出：** Api/Application/Contracts/Domain/Infrastructure、三模块边界、`collaboration_db` Manifest、PostgreSQL Schema/SQLite 前缀、三个 ledger、Operation/readiness、Secret 隔离和架构测试。

**验证与证据：** 第 13.2 节数据库/架构门禁，SQLite/PostgreSQL 18 空库与升级、Shared/PerService、错误目标/drift/SystemData 不可用、`diff --check` 和敏感信息扫描。

**结果回写：** 回写实际项目路径、ServiceKey/UnitKey、Schema/前缀/ledger、Manifest DTO、配置键、健康标签、测试数、提交和待验收项。

**建议提交：** `feat(collaboration): scaffold host and migration units`

## TASK-PF05-002 实现 Messaging 领域、持久化与应用用例

**状态：** 待细化/任务待确认

**目标：** 实现一对一会话、成员、消息、严格 Sequence、ClientMessageNId 幂等、未读/已读、撤回墓碑、个人隐藏、游标补拉和本地 Outbox/Audit 原子事务。

**输入文档：** 本文第 5～7、10.3、10.7、11.4、13.3 节。

**依赖：** TASK-PF05-001；Identity 用户目录/状态公开契约稳定或以批准 fixture 隔离。

**允许修改范围：** Messaging Domain/Application/Contracts/Infrastructure、迁移和对应测试；禁止实现 Presence、File 二进制、统一 Audit、前端或 RemoteAssistance。

**预期输出：** Conversation/Member/Message/Disposition 模型、Repository、事务 Sequence、幂等指纹、未读投影、已读/隐藏/撤回用例、opaque cursor、Messaging Outbox 和对账修复端口。

**验证与证据：** 第 13.3 节全矩阵，尤其并发发起、并发 Sequence、幂等冲突、2 分钟边界、投影重建、历史 410 和正文敏感扫描。

**结果回写：** 回写最终字段/表/索引、状态、API 输入端口、错误码、事务算法、测试/性能数据、提交和偏差。

**建议提交：** `feat(collaboration): implement one-to-one messaging core`

## TASK-PF05-003 实现 Presence 与 SignalR 连接治理

**状态：** 待细化/任务待确认

**目标：** 实现可信 SignalR 连接、多设备/多标签页租约、用户级 Online/Offline 聚合、粗粒度最后活动、单实例内存和多实例 Redis 背板边界。

**输入文档：** 本文第 4.4、8、10.4～10.6、13.4 节。

**依赖：** TASK-PF05-001；Identity 令牌/撤销/用户停用契约；多实例验证依赖 Redis。

**允许修改范围：** Presence 模块、Collaboration Hub 连接层、Redis 适配、配置、健康/指标和测试；禁止创建消息事实、修改 Messaging Repository 或实现 RemoteAssistance 信令。

**预期输出：** ConnectionLease、20s/60s/15s 配置校验、服务端安全分组、revision 聚合、隐私 DTO、单/多实例策略、限流与故障降级。

**验证与证据：** 第 13.4 节矩阵、可控时钟、多设备、Token 失效、Redis 故障、两副本短时功能验证、日志/指标高基数扫描。

**结果回写：** 回写 Hub 路径/协议版本、租约键、默认时长、分组算法、隐私投影、指标/健康、测试数和待扩展限制。

**建议提交：** `feat(collaboration): add presence and realtime connection governance`

## TASK-PF05-004 实现 AttachmentIntegration 防腐层

**状态：** 待细化/任务待确认

**目标：** 基于 PF-04 已验收 File Contracts 实现 `IChatFileGateway` 适配、ChatAttachment 投影、可发送校验、状态 Inbox、下载重新授权、引用/保留/法律保全协调和文本聊天降级。

**输入文档：** 本文第 6.5、9、13.5 节；PF-04 File 最终批准的 Contract、状态与错误语义。

**依赖：** TASK-PF05-001、002；PF-04 File 稳定契约和真实验收。实施 07 草案接口本身不满足依赖。

**允许修改范围：** AttachmentIntegration Domain/Application/Contracts/Infrastructure、迁移、File adapter 和对应测试；禁止修改 File 内部实现、代理二进制、持有对象存储凭据或读取 File Repository。

**预期输出：** `IChatFileGateway`、ChatAttachment、允许类型/50MB 策略、File 状态版本 Inbox、一次性授权、引用/保全 Outbox、对账和稳定降级错误。

**验证与证据：** 第 13.5 节矩阵、PF-04 Contract fixture、扫描竞态、事件乱序、撤回下载、多个保全 Case、File 故障下文本聊天和 URL/Secret 扫描。

**结果回写：** 回写真实 PF-04 Contract 版本/路径映射、状态映射、字段/表、API 端口、事件、测试数、外部依赖和偏差。

**建议提交：** `feat(collaboration): integrate secure chat attachments`

## TASK-PF05-005 冻结 REST、SignalR、事件、权限与可靠集成

**状态：** 待细化/任务待确认

**目标：** 装配三个模块，冻结 v1 REST/Hub/事件/错误/权限，完成安全消息投影、Outbox/Inbox、Identity/File/Audit 适配和 Gateway 路由。

**输入文档：** 本文第 3、10～11、13.6～13.7 节；TASK-PF05-002～004 输出；PF-04 Audit 稳定契约。

**依赖：** TASK-PF05-002、003、004；Identity、File、Audit 已批准公开契约。

**允许修改范围：** Collaboration Api/Contracts/组合根、Gateway 协调文件、三个模块集成适配、权限清单、OpenAPI/Hub/Event/Contract 测试；禁止跨模块 Repository、PF-06 字段和其他服务内部改造。

**预期输出：** `/collaboration` 路由、REST v1 DTO、Hub v1、安全投影、模块独立 Outbox/Inbox、retry/DLQ、服务身份、权限/提权端口、稳定错误和降级门禁。

**验证与证据：** 第 13.6～13.7 节契约/权限/故障矩阵，OpenAPI/JSON/Hub snapshots、Publisher Confirm、重复/乱序/重连、跨租户和敏感载荷扫描。

**结果回写：** 回写最终路由、DTO、事件名/版本/routing key、权限、错误码、重试参数、健康状态、测试数和前端消费契约。

**建议提交：** `feat(collaboration): expose reliable messaging contracts`

## TASK-PF05-006 实现 PC/PDA/Mobile Collaboration 页面

**状态：** 待细化/任务待确认

**目标：** 在 PF-01 统一壳上实现 PC 顶栏/快捷抽屉/完整页、PDA/Mobile 全屏聊天、SignalR 合并、附件交互和普通用户完整状态体验。

**输入文档：** 本文第 7～10、12、13.8 节；TASK-PF05-005 前端契约；PF-01 已验收组件/主题/工作区契约。

**依赖：** TASK-PF05-005；PF-01、Identity、File 前端公开适配稳定。

**允许修改范围：** Collaboration 前端 api/stores/realtime/components/pages/routes/tests 及经协调的 PF-01 顶栏插槽装配；禁止重写平台壳、Identity/File 页面、合规后端或创建独立前端项目。

**预期输出：** PC 抽屉和完整页、三端会话页、用户搜索、消息/游标 Store、临时项合并、附件状态/下载、Presence、墓碑、降级、可访问性和截图基线。

**验证与证据：** 第 13.8 节 unit/component/E2E、三主题/暗色/密度、目标视口、键盘/ARIA/触控、断线补拉、权限负例、控制台和敏感缓存扫描。

**结果回写：** 回写最终路由、组件/Store、终端差异、状态映射、截图/报告、覆盖率、测试数、真机待验收和偏差。

**建议提交：** `feat(frontend): add collaboration chat experience`

## TASK-PF05-007 实现合规、保留、安全与可观测性闭环

**状态：** 待细化/任务待确认

**目标：** 实现合规查看/处置、异步导出、法律保全、365 天清理协调、3 年 Audit 分类、短时提权、双人审批、指标/Trace/告警和 PC 合规页面。

**输入文档：** 本文第 11～13.8 节；PF-04 File/Audit 稳定契约；TASK-PF05-005/006 输出。

**依赖：** TASK-PF05-005；前端部分依赖 006；PF-04 File/Audit 和 Identity 提权/服务身份契约稳定。

**允许修改范围：** Collaboration Compliance/Retention/Operations、后台 Worker、适配器、PC 合规页面和对应测试/运行手册；禁止修改 Audit/File 内部表、创建 Scheduler 领域或扩张普通聊天范围。

**预期输出：** ComplianceDisposition、Export Operation、LegalHoldCase、审批 checksum、File 保全/导出适配、可恢复清理、审计/指标/告警、受保护诊断和合规 UI。

**验证与证据：** 第 13.7～13.8 节全部场景、权限/提权、再审计失败关闭、双人审批、多个 Case、保留边界、故障注入、Secret/正文扫描和恢复演练。

**结果回写：** 回写状态机、默认/可配期限、Operation/审批契约、指标/告警、运行步骤、测试数、外部待验收和合规偏差。

**建议提交：** `feat(collaboration): add compliance retention and observability`

## TASK-PF05-008 完成契约、E2E、2C4G 与阶段验收

**状态：** 待细化/任务待确认

**目标：** 在真实云端 Docker、Gateway、Identity、SystemData、PostgreSQL、Redis、RabbitMQ、File、Audit 和浏览器环境验证 PF-05 全链，冻结 PF-06 可消费契约。

**输入文档：** 本文全部章节；TASK-PF05-001～007 输出；各前置阶段真实验收记录。

**依赖：** TASK-PF05-001～007 全部完成；PF-02 数据库门禁、PF-04 File/Audit 稳定契约和真实环境可用。

**允许修改范围：** PF-05 验收测试、fixture、性能/故障脚本、报告、运行说明和本文执行记录；只修复验收阻塞缺陷，不扩张业务范围，不修改其他实施文档的设计结论。

**预期输出：** 全量测试/覆盖率、数据库拓扑、OpenAPI/Hub/Event snapshots、真实两用户/多设备/附件/合规 E2E、2C4G 最低门禁、两实例短时扩展正确性、故障恢复、截图与阶段报告。

**验证与证据：** 第 13 章全部适用矩阵；记录真实命令、退出码、数量、覆盖率、容器/提交、资源曲线、拐点、报告路径和任何跳过/外部限制。

**结果回写：** 仅在用户明确授权的未来执行阶段回写本文第 16～18 节及总 Todo；未满足项保持阻塞/待验收，不伪装完成。

**建议提交：** `test(collaboration): verify pf05 collaboration stage`

---

# 16. 完成标准

## 16.1 宿主、模块与数据

- `Collaboration.Service` 创建且只包含 PF-05 三模块；RemoteAssistance 仍未实现。
- 三模块独立公开契约、权限、Schema/前缀、迁移产物、ledger、readiness 和测试，无跨模块 Repository/表/外键。
- SystemData logical-to-physical、Shared/PerService、OperationId、锁、drift、NotReady、生产 plan/审批/备份/apply/verify 和 SQLite 显式回退通过。
- 会话参与人对唯一、严格 Sequence、ClientMessageNId 幂等、消息/Outbox/Audit 原子、已读游标、未读投影、墓碑、隐藏和补拉符合本文。

## 16.2 Presence、附件与可靠通信

- 多设备/多连接、20s/60s/15s、隐私投影、单实例内存和多实例 Redis 背板门禁通过。
- File 只有最终安全状态可发送/下载；File 不可用时文本聊天继续；FileNId 只经公开契约引用。
- REST 是事实入口，SignalR 安全投影可丢失后由 REST 恢复；Outbox/Inbox、重试、DLQ、人工恢复和对账通过。

## 16.3 安全、合规与保留

- TenantNId/UserNId 来自可信上下文；普通权限与会话成员授权并用，跨租户不泄露。
- 正文纯文本、无 HTML/链接预览/外部图片自动加载，日志/Trace/指标/Audit 无正文和凭据。
- 单条/单会话合规查看执行权限、原因、短时提权和再审计；批量导出与解除保全执行双人审批；创建保全立即生效并复核。
- 消息/附件 365 天普通保留、Audit 3 年分类、法律保全优先、可恢复清理和 File 引用协调通过。

## 16.4 前端与体验

- 两名有效登录用户可在 PC 抽屉/完整页及 PDA/Mobile 全屏路径完成文本、图片、文件聊天、断线补拉、已读、撤回、隐藏和恢复。
- PC 抽屉支持文本/图片/文件；用户搜索建会话、深历史和合规管理在完整页/独立路由。
- 三主题、明暗、密度、目标视口、键盘/ARIA/触控、减少动画、错误/空/降级/保留边界通过。

## 16.5 自动化与环境

- Domain、Application、Infrastructure、API、SignalR、Contract/Event、Frontend Component 和 E2E 有新鲜证据。
- 2 核 4GB 共享云端 Docker 环境通过第 13.9 节最低门禁，记录资源曲线、恢复和已验证容量拐点。
- PostgreSQL 18、Redis、RabbitMQ、真实 Identity/SystemData/File/Audit/Gateway 或浏览器缺失时，对应项只能待验收，阶段不得标记完成。
- 未越界实现群聊、频道、语音、会议、外部联系人、机器人、HTML、E2EE、RemoteAssistance 或文件二进制存储。

---

# 17. 执行记录

| 任务 | 状态 | 执行者/任务 | 提交 | 验证证据 | 结果回写 |
| --- | --- | --- | --- | --- | --- |
| TASK-PF05-001 | 待细化/任务待确认 | - | - | - | - |
| TASK-PF05-002 | 待细化/任务待确认 | - | - | - | - |
| TASK-PF05-003 | 待细化/任务待确认 | - | - | - | - |
| TASK-PF05-004 | 待细化/任务待确认 | - | - | - | - |
| TASK-PF05-005 | 待细化/任务待确认 | - | - | - | - |
| TASK-PF05-006 | 待细化/任务待确认 | - | - | - | - |
| TASK-PF05-007 | 待细化/任务待确认 | - | - | - | - |
| TASK-PF05-008 | 待细化/任务待确认 | - | - | - | - |

截至 2026-08-14，已完成只读证据盘点、用户逐节确认、全文最终确认和实施文档 08 定稿。用户允许后续进入开发，但明确要求当前会话不做开发；因此本会话未派遣任务，未修改生产代码或测试代码，未运行构建/测试/性能验收，未提交 Git。工作树中的 SystemData、Gateway、配置、测试和其他并行改动均保持原状。

---

# 18. 下一阶段输入契约

PF-06 RemoteAssistance 只能在 PF-05 真实验收后依赖以下版本化公开语义：

```text
身份与租户
  TenantNId / UserNId / 服务身份与短时提权边界

Messaging
  ConversationNId / MessageNId / Sequence
  只读会话成员验证端口
  版本化系统卡片/邀请投递扩展端口
  MessageAccepted/MessageRetracted/ReadCursor 事件语义

Realtime / Presence
  已认证 Hub 连接与服务端安全分组端口
  Presence 安全投影和 revision
  多实例 Redis 背板运行门禁

AttachmentIntegration
  AttachmentNId / FileNId 安全展示和重新授权语义
  引用、保留与法律保全协调端口

Compliance
  普通聊天邀请事实审计端口
  合规查看/保留/法律保全的独立权限边界
```

PF-06 必须自行设计 RemoteAssistanceSession、参与人白名单、一次性凭证、邀请接受/拒绝、WebRTC/Screego/TURN、超时/终止和屏幕共享审计。PF-05 不承诺 WebRTC 房间、远程控制、录屏、共享媒体保存或 RemoteAssistance 数据表。

PF-04 File/Audit 契约在 PF-05 开发前仍需按实际批准版本替换候选适配；本文的 `IChatFileGateway` 与 Audit 端口表达所需语义，不证明实施 07 草案路由已经稳定或实现。

---

# 19. 文档自审清单

- [x] 指定蓝图、总 Todo、模板、实施 01/03/04/05/06/07、Git 和当前代码结构已核对。
- [x] PF-02 并行未提交实现和 PF-04 draft 未写成稳定实现。
- [x] 一个 Collaboration Host、三个独立模块；PF-06 RemoteAssistance 未越界实现。
- [x] Shared/PerService、Manifest、三迁移单元/账本、OperationId、锁、drift、NotReady 和云端 Docker 默认调试完整。
- [x] 会话、消息、Sequence、幂等、已读/未读、撤回、隐藏、Presence 和附件边界一致。
- [x] REST、SignalR 安全投影、Outbox/Inbox、重试/DLQ 和 REST 校正恢复前后一致。
- [x] File/Audit 仅通过动态公开契约适配，无跨模块 Repository、表或外键。
- [x] 权限、安全、合规查看、双人审批、365 天/3 年、法律保全和可观测性完整。
- [x] PC 抽屉/完整页、PDA/Mobile 全屏、主题、可访问性和状态矩阵完整。
- [x] 2C4G 性能门禁按共享微服务环境设置，未把扩展能力冒充当前单机容量。
- [x] 八张任务卡均且只包含统一九字段，并保持“待细化/任务待确认”。
- [x] 依赖图、任务卡和执行记录一一对应。
- [x] 文档明确记录后续开发已获原则许可，同时当前会话未派遣、未提交、未实施。
- [x] 用户已于 2026-08-14 完成全文最终审阅并确认实施文档 08。
- [x] 引用、禁用占位表达、契约一致性、九字段与 `git diff --check` 自审已执行。
