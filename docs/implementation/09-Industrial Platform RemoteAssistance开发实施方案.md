# 09-Industrial Platform RemoteAssistance开发实施方案

# Industrial Platform RemoteAssistance开发实施方案

> 当前里程碑范围：PF-06 在 `Collaboration.Service` 中加入独立 RemoteAssistance 模块。先完成现场网络、浏览器、许可和双 PoC 决策门禁；门禁通过后才允许进入领域、契约、适配、页面、部署和验收开发。首期只做一名共享者与一至三名白名单观看者的浏览器屏幕共享，不录屏、不保存画面、不远程控制、不无人值守、不默认启用音视频会议。

版本：V1.0

日期：2026-08-14

确认日期：2026-08-14

阶段：PF-06 RemoteAssistance

阶段状态：详细设计和九字段任务卡已确认；开发、任务派遣、构建、测试和提交均未获本轮授权。`TASK-PF06-001` 为唯一首发任务，只有其决策门禁通过并由用户再次批准后，后续任务才可转为可派遣。

模块或服务：

```text
Collaboration.Service
└── RemoteAssistance
    ├── Domain
    ├── Application
    ├── Contracts
    └── Infrastructure
```

Service Host 与内部模块：

```text
PF-05 创建 Collaboration.Service（尚未开发）
├── Messaging（PF-05）
├── Presence（PF-05）
├── AttachmentIntegration（PF-05）
└── RemoteAssistance（PF-06，本阶段）

辅助部署单元：Screego PoC、TURN
```

RemoteAssistance 独立拥有领域、公开契约、权限、Schema/表前缀、迁移/种子账本、readiness 和测试；禁止直读 Messaging、Presence、Identity、Audit、File 或 SystemData Repository/表，不建立跨模块数据库外键，并保留未来物理拆分能力。Screego、TURN 不计入核心 Service Host，不拥有平台会话事实。

服务初始化与环境引导：

```text
ServiceKey: collaboration
ModuleKey: remote-assistance
LogicalDatabaseName: collaboration_db
PostgreSQL schema: collaboration_remote_assistance
SQLite prefix: collaboration_remote_assistance_
Schema ledger: remote_assistance_schema_migrations
Seed ledger: remote_assistance_seed_ledger
Provider: PostgreSQL 18 / SQLite Development fallback
```

- `SystemBaseline`：RemoteAssistance 权限目录和必要系统策略目录，`RequiredForReadiness=true`。
- `TenantBaseline`：只有最终确认由本模块拥有的默认租户策略；默认功能关闭，不复制 SystemData 功能开关事实。
- `EnvironmentSample`：仅 Development/Test 显式启用的演示策略，registration、plan、apply 三层禁止 Staging/Production。
- 本模块不声明 `SecretBootstrap`。TURN shared secret、Screego cookie secret、TLS 私钥由部署 Secret Provider 注入，不进入种子、SystemData、数据库、API、日志、Trace 或审计。
- 模块拥有签名 migration/seed/initializer 产物及本地双账本；SystemData 只保存 registration、plan、Operation 和脱敏 observation。
- SystemData 不可用、迁移/RequiredSeed 未完成、checksum drift、目标错误或环境策略拒绝时 RemoteAssistance 保持 NotReady 且功能开关关闭；Messaging/Presence 不因此整体 NotReady。

技术：

```text
.NET 10 / Clean Architecture / DDD / SqlSugar
PostgreSQL 18 / SQLite Development fallback
REST / SignalR or dedicated WSS signaling / WebRTC
Redis（短期票据消费与多实例信令协调，持久授权事实仍在数据库）
RabbitMQ / Outbox / Inbox
独立 TURN（生产候选）/ Screego（未修改基准 PoC）
Vue 3 / TypeScript / Pinia / Edge / Chrome
```

规格与蓝图依据：

- `docs/blueprint/05-Industrial Platform平台基础功能与独立模块设计.md`
- `docs/blueprint/09-Industrial Platform开发总TodoList.md`
- `docs/blueprint/20-Industrial Platform部署架构设计.md`
- `docs/blueprint/27-Industrial Platform API规范.md`
- `docs/blueprint/28-Industrial Platform前端工程规范.md`
- `docs/blueprint/29-Industrial Platform自动化测试体系.md`
- `docs/blueprint/30-Industrial Platform日志审计与可观测性平台设计.md`
- `docs/blueprint/31-Industrial Platform权限体系与安全架构设计.md`
- `docs/blueprint/32-Industrial Platform Service Host与内部模块边界.md`
- `docs/blueprint/33-Industrial Platform SystemData数据库编排与环境引导.md`
- `docs/implementation/03-Industrial Platform Identity Service开发实施方案.md`
- `docs/implementation/05-Industrial Platform SystemData开发实施方案.md`
- `docs/implementation/07-Industrial Platform File Notification Audit开发实施方案.md`
- `docs/implementation/08-Industrial Platform Collaboration开发实施方案.md`
- `docs/implementation/TEMPLATE-开发实施方案.md`
- Screego 官方仓库、`screego.config.example`、`auth/auth.go` 与 GPL-3.0 LICENSE。
- W3C Screen Capture、W3C WebRTC 与目标 Edge/Chrome 官方兼容资料。

---

# 1. 文档说明

## 1.1 文档目的

本文是 PF-06 详细设计、验证门禁、任务依赖、九字段任务卡、验收证据和结果回写的唯一维护源。它先把尚未验证的媒体引擎选择隔离在 PoC 决策门禁，再定义不随引擎变化的平台会话、授权、安全和审计边界。

## 1.2 当前输入状态

- 当前分支 `develop` 的 HEAD 为 `61753dc`，相对 `origin/develop` 领先一个提交。
- 当前仓库没有 `Collaboration.Service`、Messaging、Presence、AttachmentIntegration 或 RemoteAssistance 生产代码。
- PF-05 实施 08 是已确认详细设计，但任务未派遣、未开发；其公开契约只能作为前置形状，PF-06 开发前必须按真实实现重新核验。
- PF-04 实施 07 已形成设计，但 File/Audit 尚未开发；PF-06 只声明所需适配语义。
- Identity 已实现可信 `sub=UserNId`、`tenant_id=TenantNId`、权限求值和会话撤销基础；没有现成的一次性 RemoteAssistance JoinTicket。
- SystemData migration-only Runner 已提交；Seed/Bootstrap 通用扩展处于并行未提交开发，不能当作已验收能力。
- Screego 官方当前认证为静态 bcrypt 用户文件和自身 Cookie Session；配置只提供 `all/turn/none` 登录模式，没有平台 Identity、一次性票据或会话参与人白名单扩展契约。
- 本轮只完成只读证据核对和文档设计，未运行构建、测试、PoC 或现场网络验证。

## 1.3 已确认设计记录

| 日期 | 决策 | 结论 |
| --- | --- | --- |
| 2026-08-14 | 生产候选方向 | 平台原生控制面与最小原生信令为推荐候选；Screego 仅作未修改独立基准 PoC |
| 2026-08-14 | 决策顺序 | 先双 PoC、现场网络和许可门禁，证据通过后才允许后续开发 |
| 2026-08-14 | 失败策略 | 任一关键门禁失败时功能开关保持关闭，不阻塞 Collaboration 文本与附件能力 |
| 2026-08-14 | 媒体边界 | 不录屏、不截帧、不转码、不落盘、不默认音频、不远程控制、不无人值守 |
| 2026-08-14 | 页面边界 | 流程由聊天发起；接受后进入平台独立路由或安全新窗口，不以跨域 iframe 为生产基线 |

## 1.4 执行前置

```text
PF-05 真实契约与 Collaboration.Service 可用
PF-04 Audit 稳定接收契约可用
Identity 用户/权限/会话契约稳定
SystemData 通用初始化链可用
内部 HTTPS/WSS、可信证书与可配置现场网络
    ↓
TASK-PF06-001 双 PoC、现场网络与许可门禁
    ↓ 用户确认生产路线并批准后续开发
TASK-PF06-002～008
    ↓
PF-07 Platform Health 消费脱敏健康摘要
```

---

# 2. 定位、目标与职责边界

## 2.1 负责

- 独立远程协助会话、邀请、共享者与观看者白名单、接受/拒绝/撤销、超时和终止。
- 一次性短期 JoinTicket、兑换、信令/ICE 会话授权、重放防护和服务端撤销。
- 一名共享者、1～3 名观看者；PC 主要共享，PDA/Mobile 主要观看。
- WebRTC 引擎防腐层、信令、ICE/TURN 策略和断线恢复编排。
- 只记录人员、原因、时间、终端、IP 摘要、共享范围、网络路径、中继、异常和终止原因等元数据审计。
- PC/PDA/Mobile 独立页面、版本化聊天邀请卡片及旧客户端安全降级。

## 2.2 不负责

- Identity 用户、登录、权限和平台会话；不复制 Identity 用户表。
- Messaging 消息事实、Presence 在线事实或 AttachmentIntegration 文件事实；不直读其 Repository。
- Audit 追加事实存储；本模块只在本地事务写 Audit Outbox。
- 录屏、截图保存、远程鼠标键盘、无人值守、语音/视频会议、外部联系人和跨租户协助。
- 浏览器授权绕过；每次捕获必须由共享者主动操作并在浏览器选择屏幕、窗口或标签。
- TURN/Screego 辅助单元的运维数据不得反向成为平台领域权威。

## 2.3 引擎路线

| 路线 | 用途 | 优点 | 风险/结论 |
| --- | --- | --- | --- |
| 平台原生控制面 + 最小 WebRTC 信令 + 独立 TURN | 推荐生产候选 | 直接实现平台票据、白名单、权限和审计；无 GPL fork | 需通过画质、资源和网络 PoC |
| 未修改 Screego 独立部署 | 基准 PoC | 快速验证 WebRTC、画质、多观看者和 TURN | 认证/房间授权不足，不直接作为生产方案 |
| Screego fork | 仅条件候选 | 可复用其媒体实现并补平台授权 | GPL 交付、源码提供、长期 rebase 和安全维护成本；未经单独许可评审不得采用 |

反向代理加 `SCREEGO_AUTH_MODE=none` 不能替代平台会话级白名单；知道房间标识或持有泛化入口的用户不能因此获得访问权。若双 PoC 证明原生方案不满足门禁，而 Screego 明显满足，必须重新提交“fork/独立适配/放弃首期功能”的决策，不自动转向 fork。

---

# 3. 前后端及跨服务协作目标

```text
Messaging 版本化邀请卡片
    ↓ 接受邀请
RemoteAssistance 权威会话/参与人授权
    ↓ JoinTicket 单次兑换
EngineAdapter → Signaling/WSS → WebRTC → TURN（按网络策略）
    ↓
独立平台路由/安全新窗口

Identity：用户有效性、TenantNId、UserNId、权限、会话撤销
Presence：仅用于邀请提示，不作为授权依据
Audit：本地 Outbox → 公开 AuditFactV1
Platform Health：只消费脱敏 readiness/指标摘要
```

Messaging 卡片采用公开 `RemoteAssistanceInvitationCardV1`，至少携带 `SchemaVersion`、`SessionNId`、`InvitationNId`、发起人安全展示、观看者数量、过期时间和平台路由目标类型；不携带 JoinTicket、TURN 凭据、房间 Secret 或可直接加入的外部 URL。未知版本客户端只显示“收到远程协助邀请，请升级或在平台中打开”，不得渲染任意 HTML，也不得自动打开外部站点。

---

# 4. 总体架构、部署与数据流

## 4.1 模块结构

```text
Collaboration.Api
└── Modules/RemoteAssistance
    ├── Domain
    ├── Application
    ├── Contracts
    └── Infrastructure
        ├── Persistence
        ├── Initialization
        ├── MessagingAdapter
        ├── IdentityAdapter
        ├── AuditOutbox
        ├── EngineAdapter
        └── Signaling

tests/Collaboration/RemoteAssistance
src/frontend/src/modules/remote-assistance
```

Domain 不引用 Messaging/Presence/Identity/Audit/具体媒体引擎。Application 只引用本模块 Contracts 和抽象端口。Infrastructure 适配公开契约。宿主仅负责认证、路由、Hub/WSS 装配、DI 和模块健康汇总。

## 4.2 邀请和加入数据流

```text
发起人从一对一会话选择观看者与原因
  → Messaging 公开端口验证会话成员
  → Identity 公开端口验证同租户有效用户
  → RemoteAssistance 事务创建 Session/Invitation/ParticipantGrant/Outbox
  → Messaging 投递 CardV1
  → 观看者接受
  → 服务端再次验证身份、白名单、状态、期限和权限
  → 签发仅存哈希的一次性 JoinTicket
  → 前端以 POST body 兑换；原票据原子标记 Consumed
  → 返回短期 EngineAccess + ICE 配置
  → WSS 信令建立 WebRTC
```

共享者也必须通过绑定 `Role=Sharer` 的票据进入。客户端不得提交 TenantNId、共享者身份或擅自增加观看者。

## 4.3 部署拓扑

```text
Browser ─ HTTPS ─ Gateway/Collaboration.Service
   └──── WSS ─── Signaling
   └──── WebRTC direct（允许时）
   └──── TURN UDP/TCP/TLS（跨网段/受限/强制 relay）

Screego PoC：独立域名/证书/容器与版本固定，不并入平台程序集
TURN：独立地址、端口范围、证书、Secret 和容量监控
```

生产必须支持内部 DNS、HTTPS/WSS、受信证书链、明确 Origin allowlist 和防火墙规则。代理必须正确处理 WebSocket Upgrade、空闲超时、客户端 IP 信任链和请求大小；只有明确的受信代理地址可提供转发头。

## 4.4 失败和降级

| 状态 | 行为 |
| --- | --- |
| RemoteAssistance 初始化未完成/SystemData 不可用 | 模块 NotReady、功能开关关闭；Collaboration 其他模块继续 |
| Identity 无法确认目标用户 | 新邀请失败关闭；已有媒体连接按会话撤销策略终止 |
| Messaging 不可用 | 不能从聊天发起；不得绕过为公开房间链接 |
| Audit 中央不可用且本地 Outbox 可写 | 普通会话可继续并重投；高风险管理员操作失败关闭 |
| Signaling 不可用 | 新连接/恢复失败，已有 P2P 连接按浏览器状态运行；状态可见 |
| TURN 不可用 | 允许 direct 的环境可尝试直连；强制 relay 环境立即失败，不降级泄露地址 |
| 浏览器拒绝/停止共享 | 结束媒体轨；会话进入暂停或终止，不伪报仍在共享 |

---

# 5. 领域模型与状态机

统一应用模板第 6.1 节的 Entity 生命周期字段、NId、`DateTimeOffset/timestamptz`、软删除、并发和跨模块 NId 约束。本章只列业务字段。

## 5.1 RemoteAssistanceSession

主要业务字段：`TenantNId`、`NId`、`ConversationNId`、`SharerUserNId`、`Reason`、`State`、`EngineKind`、`NetworkPolicy`、`ExpiresOn`、`StartedOn`、`EndedOn`、`EndReason`、`ReconnectDeadlineOn`、`LastActivityOn`、`OptimisticVersion`。

不变量：

- 创建时只有一名共享者和 1～3 名唯一观看者，全部属于同一可信租户且不能与共享者相同。
- `ConversationNId` 只保存跨模块稳定标识，不建立数据库外键。
- 原因必填、去除首尾空格、最大 500 字符；审计使用脱敏摘要。
- 同一共享者默认只允许一个 Active/Sharing 会话；并发策略由服务端原子约束。
- 任何终态禁止重新开放；重新协助创建新 SessionNId。

状态机：

```text
Draft → Invited → Accepted → Connecting → Sharing
  └→ Cancelled   └→ Declined/Expired
Accepted/Connecting/Sharing → Suspended → Connecting/Sharing
任意非终态 → Terminated
```

`Suspended` 只允许在短暂断线窗口内恢复信令；捕获轨已结束时共享者必须再次主动授权。终态为 `Declined/Expired/Cancelled/Terminated`。

## 5.2 Invitation

字段：`TenantNId`、`NId`、`SessionNId`、`InviteeUserNId`、`Role`、`State`、`IssuedOn`、`ExpiresOn`、`RespondedOn`、`ResponseReason`、`CardContractVersion`、`MessageNId`。

状态：`Pending → Accepted/Declined/Revoked/Expired`。接受、拒绝和撤销均幂等；过期后不得签发票据。MessageNId 是 Messaging 返回的跨模块标识，不建立外键。

## 5.3 ParticipantGrant

字段：`TenantNId`、`NId`、`SessionNId`、`UserNId`、`Role`、`GrantState`、`GrantedOn`、`RevokedOn`、`RevocationReason`、`LastJoinedOn`、`LastLeftOn`。

角色只有 `Sharer`、`Viewer`。授权不能从 Presence 推导。被撤销、Identity 会话失效、用户禁用或跨租户时不得建立或恢复媒体连接。

## 5.4 JoinTicket

字段：`TenantNId`、`NId`、`SessionNId`、`ParticipantGrantNId`、`UserNId`、`Role`、`TokenHash`、`EngineAudience`、`IssuedOn`、`ExpiresOn`、`ConsumedOn`、`RevokedOn`、`ClientNonceHash`、`IpBindingMode`。

- 原始票据使用加密安全随机值，只返回一次；数据库仅保存 SHA-256/HMAC 结果。
- 建议有效期 60 秒，只允许兑换一次；兑换使用原子条件更新。
- 绑定 Session、User、Role、Tenant、EngineAudience 和客户端 nonce；IP 只作可配置风险信号，不作为移动网络下的唯一身份。
- 票据只能在 HTTPS POST body 或受保护 header 中提交，不进入 URL、Referer、日志、Trace、指标或审计。
- 同键不同上下文、过期、已消费、撤销和重放均拒绝并产生安全审计。

## 5.5 EngineLease 与连接观察

`EngineLease` 保存平台签发的短期引擎访问授权摘要：`NId`、`SessionNId`、`ParticipantGrantNId`、`EngineKind`、`EngineSessionReferenceHash`、`ExpiresOn`、`RevokedOn`。不保存 TURN 明文凭据、SDP 或 ICE candidate。

连接观察只保存元数据：连接/断开时间、候选类别 `host/srflx/relay`、传输 `udp/tcp/tls`、浏览器家族/版本、共享范围类别、分辨率档位、匿名化网络摘要、错误类别、RTT/丢包/码率聚合。禁止保存画面、音频、SDP 全文、私网地址或完整 User-Agent/IP。

---

# 6. 数据、事务与初始化设计

## 6.1 主要表

| 表 | 主要业务字段/约束 |
| --- | --- |
| `remote_assistance_session` | TenantNId+NId 全历史唯一；共享者活动会话部分唯一；状态/过期索引 |
| `remote_assistance_invitation` | SessionNId+InviteeUserNId 唯一；Pending+ExpiresOn 索引 |
| `remote_assistance_participant_grant` | SessionNId+UserNId 唯一；角色/撤销索引 |
| `remote_assistance_join_ticket` | TokenHash 全局唯一；ExpiresOn/ConsumedOn 索引 |
| `remote_assistance_engine_lease` | SessionNId+ParticipantGrantNId+活动状态索引 |
| `remote_assistance_connection_observation` | SessionNId+CreatedOn、候选类别和错误类别索引 |
| `remote_assistance_outbox` | EventId 唯一；PublishedOn+CreatedOn 索引 |
| `remote_assistance_inbox` | Consumer+EventId 唯一；处理状态/重试索引 |

Session 是父聚合；本模块内部子表按 `(Session_Id, Session_IsDeleted) → (Id, IsDeleted)` 复合外键和双重删除过滤。Identity、Messaging、Audit、SystemData 只保存 NId，不建外键。

## 6.2 事务与 Outbox

- 创建 Session、Invitation、ParticipantGrant、邀请卡片命令 Outbox 和 Audit Outbox 同事务提交。
- 接受邀请、创建/消费 JoinTicket、撤销参与人和终止会话分别使用原子事务与乐观并发。
- Messaging 卡片投递、Audit 接收和 Identity 状态事件采用至少一次投递；Inbox 按 EventId 幂等。
- 投递失败不得删除领域事实；达到重试上限进入可观测失败队列并由受权操作恢复。
- 不跨数据库事务，不直接写 Messaging/Audit 表。

## 6.3 初始化清单

InitializationManifest 至少声明 ModuleKey、逻辑库、表前缀、迁移/种子产物 version/checksum/signature、Owner、DesiredState、数据库最小角色、锁范围、备份策略和环境策略。验证覆盖首次 apply、重复 apply、并发多副本、升级、drift、部分失败、Shared 多 ModuleKey 隔离、管理员维护策略不被覆盖和 SystemData 不可用 NotReady。

---

# 7. API、Hub、事件、权限与错误契约

## 7.1 REST 路由

Gateway 前缀：`/api/remote-assistance/v1`；服务内部前缀保持稳定并由 Gateway 映射。

| Method/Path | 用途 | 权限/幂等 |
| --- | --- | --- |
| `POST /sessions` | 从 Conversation 创建邀请 | `remote-assistance.session.create`；`Idempotency-Key` |
| `GET /sessions/{sessionNId}` | 获取安全会话投影 | 参与人授权或管理权限 |
| `POST /sessions/{sessionNId}/accept` | 接受邀请并生成票据 | 被邀请人；幂等 |
| `POST /sessions/{sessionNId}/decline` | 拒绝邀请 | 被邀请人；幂等 |
| `POST /sessions/{sessionNId}/cancel` | 发起人取消 | `remote-assistance.session.cancel`；并发版本 |
| `POST /sessions/{sessionNId}/terminate` | 终止会话 | 参与人或管理权限；并发版本 |
| `POST /sessions/{sessionNId}/participants/{userNId}/revoke` | 撤销观看者 | `remote-assistance.participant.manage` |
| `POST /join-tickets/exchange` | 单次兑换 EngineAccess/ICE | 已认证且绑定用户；原子单次 |
| `POST /sessions/{sessionNId}/heartbeat` | 会话/共享端活动摘要 | 参与人绑定；限流 |
| `GET /sessions/{sessionNId}/diagnostics` | 脱敏诊断 | `remote-assistance.diagnostics.read` |

所有请求从可信上下文取得 TenantNId/UserNId；响应使用统一 ApiResult/TraceId、RFC 3339 时间和版本化 DTO。创建/接受响应不得返回其他参与人的敏感网络信息。

## 7.2 信令契约

生产候选使用独立 `/hubs/remote-assistance` 或 `/ws/remote-assistance/v1`，最终选择由 PoC 决策记录冻结。握手必须携带短期 EngineAccess，不接受长期平台 Access Token 作为房间万能凭据。每个信令消息含 `ContractVersion`、`SessionNId`、`SenderGrantNId`、`MessageNId`、`Sequence`、`Type`、`Payload`；服务端验证角色和目标白名单后转发。

允许消息类型：Offer、Answer、IceCandidate、Renegotiate、ParticipantReady、TrackEnded、KeepAlive、Terminate。Payload 有大小/频率限制；不记录 SDP/ICE 正文。未知版本返回稳定不支持错误并关闭连接。

## 7.3 公开事件

- `RemoteAssistanceInvitationCreatedV1`
- `RemoteAssistanceInvitationRespondedV1`
- `RemoteAssistanceSessionStartedV1`
- `RemoteAssistanceParticipantJoinedV1`
- `RemoteAssistanceParticipantLeftV1`
- `RemoteAssistanceSessionSuspendedV1`
- `RemoteAssistanceSessionEndedV1`
- `RemoteAssistanceSecurityViolationDetectedV1`

事件只含稳定 NId、角色、状态、UTC 时间、原因类别、TraceId 和必要安全快照；不含票据、Secret、SDP、ICE、IP 明文、画面或音频。

## 7.4 权限码

```text
remote-assistance.session.create
remote-assistance.session.view
remote-assistance.session.share
remote-assistance.session.join
remote-assistance.session.cancel
remote-assistance.session.terminate
remote-assistance.participant.manage
remote-assistance.diagnostics.read
remote-assistance.audit.read
remote-assistance.policy.manage
```

权限只给出能力上限，端点还必须验证同租户、会话参与人、邀请状态、ParticipantGrant、角色和 Identity 当前有效性。

## 7.5 稳定错误码

```text
REMOTE_ASSISTANCE_DISABLED
REMOTE_ASSISTANCE_NOT_READY
REMOTE_ASSISTANCE_DEPENDENCY_UNAVAILABLE
REMOTE_ASSISTANCE_CONVERSATION_NOT_ELIGIBLE
REMOTE_ASSISTANCE_PARTICIPANT_INVALID
REMOTE_ASSISTANCE_PARTICIPANT_NOT_ALLOWED
REMOTE_ASSISTANCE_VIEWER_LIMIT_EXCEEDED
REMOTE_ASSISTANCE_SESSION_CONFLICT
REMOTE_ASSISTANCE_SESSION_EXPIRED
REMOTE_ASSISTANCE_SESSION_ENDED
REMOTE_ASSISTANCE_INVITATION_EXPIRED
REMOTE_ASSISTANCE_TICKET_INVALID
REMOTE_ASSISTANCE_TICKET_EXPIRED
REMOTE_ASSISTANCE_TICKET_REPLAYED
REMOTE_ASSISTANCE_SIGNALING_UNAVAILABLE
REMOTE_ASSISTANCE_TURN_UNAVAILABLE
REMOTE_ASSISTANCE_BROWSER_UNSUPPORTED
REMOTE_ASSISTANCE_CAPTURE_DENIED
REMOTE_ASSISTANCE_CAPTURE_ENDED
REMOTE_ASSISTANCE_NETWORK_POLICY_BLOCKED
REMOTE_ASSISTANCE_ENGINE_UNAVAILABLE
```

401 表示未认证，403 表示权限/参与人授权失败，404 对无权主体统一隐藏资源存在性，409 表示状态/并发/重放冲突，410 表示已过期/终止，422 表示可安全披露的业务校验，429 表示限流，503 表示初始化或依赖不可用。

---

# 8. 页面与交互设计

## 8.1 聊天邀请卡片

- 一对一聊天操作菜单提供“发起远程协助”；仅 PC 默认显示共享入口，PDA/Mobile 主要显示观看入口。
- 发起抽屉展示观看者、原因、30 分钟默认超时、媒体不录制提示和网络/浏览器前置检查。
- `RemoteAssistanceInvitationCardV1` 显示发起人、状态、到期时间和接受/拒绝操作；旧客户端安全降级为纯文本提示和平台内部路由。
- 卡片状态通过事件/安全查询更新，不把 Message 状态当作 Session 权威状态。

## 8.2 独立页面

推荐路由：

```text
/pc/remote-assistance/:sessionNId
/pda/remote-assistance/:sessionNId
/mobile/remote-assistance/:sessionNId
```

PC 支持当前平台标签或用户主动打开安全新窗口；`window.open` 使用 `noopener,noreferrer`，新窗口通过正常平台会话和 POST 兑换进入，不在 URL 传票据。PDA/Mobile 默认只观看，不展示共享按钮，除非后续独立验收明确开放。

## 8.3 状态与操作

- 进入前检查 HTTPS、安全上下文、浏览器版本、WSS、网络策略和功能开关。
- 共享必须由清晰的“选择共享内容”按钮触发 `getDisplayMedia({video:true,audio:false})`。
- 页面明确展示共享屏幕/窗口/标签类型、观看者名单、连接路径 direct/relay、共享中指示器和“停止共享”。
- 浏览器拒绝、系统取消、轨结束、断线、TURN 不可用、会话过期、权限撤销分别显示稳定状态，不自动重复弹出授权。
- 1～3 观看者布局优先保证主画面文字可读，不自动生成视频会议网格。
- 页面不提供下载、录制、截图或远程控制入口。

## 8.4 安全响应头与可访问性

- CSP 使用显式 `default-src/script-src/connect-src/frame-src` allowlist；生产不允许任意 `*`、`unsafe-eval` 或不受控外域。
- CORS 与 WebSocket Origin 只允许平台及批准的独立域名；Cookie 场景使用严格 SameSite/Secure/HttpOnly 和 CSRF Token，Bearer/票据仍防止跨 Origin 滥用。
- `Permissions-Policy: display-capture=(self)`；若未来 iframe 必须单独安全评审并显式 allow，不作为本阶段默认。
- 键盘、焦点、ARIA live、对比度、200% 缩放、PDA 48px/Mobile 44px 触控和减少动态效果纳入验收。

---

# 9. 安全、审计与可观测性

## 9.1 安全控制

- 会话级最小权限、显式白名单和服务端角色校验；客户端 UI 不能构成授权。
- JoinTicket 单次、短期、绑定、哈希保存、原子消费、撤销和重放告警。
- EngineAccess 与 TURN 临时凭据最小寿命；长期 TURN secret 只在服务端 Secret Provider。
- ICE 策略可配置 `all/relay`；强制 relay 环境不能悄悄回退 direct。
- 限制邀请、接受、票据兑换、信令消息、ICE candidate 和心跳速率；异常关闭连接并审计。
- 不保存媒体，不启用 MediaRecorder，不在服务端转发/解码/截帧。
- Screego PoC 固定镜像 digest/版本、独立网络和域名；不得使用默认 Secret，不向公网开放未认证房间。

## 9.2 审计

必须审计：创建/接受/拒绝/撤销/过期、参与人增删、票据签发/消费/重放、连接/断线/恢复、开始/停止共享、direct/relay 类别、强制终止、策略/配置变更和诊断访问。

审计禁止：画面、音频、SDP、ICE candidate、TURN 密码、JoinTicket、EngineAccess、Authorization、Cookie、完整 IP/User-Agent、屏幕标题和窗口内容。共享范围只记录 `monitor/window/browser` 类别；IP、终端和 User-Agent 按批准的散列/粗粒度策略处理。

## 9.3 指标和健康

指标至少包括：邀请/接受/拒绝/过期、建连成功率与 P50/P95、direct/relay 比例、ICE 失败类别、重连、活跃会话/观看者、共享端码率/RTT/丢包聚合、TURN 入出带宽、信令连接/消息拒绝、票据重放、Outbox/Inbox 积压和初始化版本。

健康分层：liveness 不探测下游；module readiness 检查初始化、必要数据库和信令；TURN、Audit、Messaging、Identity 分别报告依赖状态。Platform Health 只消费脱敏摘要、错误码、TraceId 和时间，不获取参与人名单或网络细节。

---

# 10. PoC 与现场验证决策门禁

## 10.1 PoC A：未修改 Screego 基准

- 固定官方版本/镜像 digest，记录配置、许可证、SBOM/漏洞扫描和部署拓扑。
- 使用独立 HTTPS/WSS 域名，验证内置与外部 TURN、1～3 观看者、1080p 文字和 30 分钟稳定性。
- 证明其静态用户/Cookie 认证和房间授权与平台 Identity/白名单的差距；不得以 `AUTH_MODE=none` 结果证明生产安全可用。
- 不修改源码；若验证必须修改，立即停止并登记为 fork 路线许可决策输入。

## 10.2 PoC B：平台原生最小信令

- 只实现临时验证用的会话白名单、单次票据、Offer/Answer/ICE 转发和外部 TURN；不提前实现完整业务模块。
- 与 PoC A 使用同一浏览器、终端、网络、分辨率、观看人数和观测方法。
- 验证信令可替换性、票据/白名单安全和资源曲线，不将 PoC 代码直接视为生产完成代码。

## 10.3 验证矩阵

| 维度 | 场景 | 必须记录 |
| --- | --- | --- |
| 网络 | 同网段、跨 VLAN、无互联网 | 候选类别、建连耗时、实际路径、失败原因 |
| 受限 | UDP 禁止、仅 TCP/TLS、强制 TURN | relay 证明、端口/代理/防火墙证据 |
| 浏览器 | 受管 Edge/Chrome 当前现场版本 | 屏幕/窗口/标签、权限、停止、重授权 |
| 质量 | 1080p 文字，1/2/3 观看者 | 可读性截图、帧率、码率、RTT、丢包 |
| 稳定 | 连续 30 分钟、网络短断 | 断流、恢复、内存增长、异常 |
| 资源 | PC 共享、PDA/Mobile 观看 | CPU、内存、共享端上行、终端发热/耗电摘要 |
| TURN | direct 与 relay | 服务端入/出带宽、并发容量和告警阈值 |
| 安全 | 非白名单、错用户、过期、重复兑换、Origin 伪造 | 全部拒绝、稳定错误、脱敏审计 |
| 故障 | Signaling/TURN/Audit/SystemData 故障 | fail-open/closed、功能开关和聊天可用性 |

## 10.4 决策规则

- 原生 PoC 满足安全、1080p、30 分钟、1～3 观看者和目标网络门禁：采用平台原生信令 + 独立 TURN。
- 原生 PoC 不满足而未修改 Screego 明显满足：暂停后续任务，提交 Screego 独立适配或 fork 的许可、安全、维护和交付评审，不自动采用。
- 两者均不满足：功能开关保持关闭，记录网络/浏览器/容量阻塞，不阻塞 Collaboration。
- 现场网络、许可证、证书或浏览器证据缺失：阶段只能标记待验收，不能进入生产开发结论。

---

# 11. 自动化测试与阶段验收设计

## 11.1 测试层次

- Domain：会话/邀请/授权/票据状态机、人数、并发、过期和终态不变量。
- Application：同租户、Identity 有效性、Messaging 成员、权限、幂等、Outbox 和失败策略。
- Infrastructure：PostgreSQL/SQLite、双账本、Redis 原子消费、SignalR/WSS、TURN 凭据、防腐层。
- API/Contract/Event：OpenAPI、CardV1、事件版本、错误码、Origin/CORS/CSRF/CSP 和敏感字段扫描。
- Frontend：三端状态、浏览器拒绝/停止、旧卡片降级、安全新窗口和可访问性。
- E2E：邀请→接受→主动授权→共享→1～3 观看→断线恢复→终止；非白名单和重放负例。
- 环境：矩阵第 10.3 节与容量/故障注入。

## 11.2 初始化门禁

覆盖首次初始化、重复 apply、并发副本、迁移/种子升级、checksum drift、部分失败、生产未审批/未备份、EnvironmentSample 环境拒绝、Shared 多 ModuleKey 隔离、管理员维护策略不被覆盖、SystemData 不可用 NotReady 和 SQLite/PostgreSQL 等价语义。本模块不声明 SecretBootstrap，并验证 TURN/TLS Secret 不进入 SystemData。

## 11.3 安全门禁

- 非参与人、跨租户、已禁用用户、被撤销用户、错角色和会话终态全部拒绝。
- JoinTicket 过期、重复、并发兑换、上下文替换和日志泄漏扫描通过。
- WebSocket Origin、CORS、CSRF、CSP、Permissions Policy 和新窗口 opener 隔离通过。
- 日志、Trace、审计、事件、错误和指标中无票据、凭据、SDP/ICE、画面、完整 IP/User-Agent。
- 强制 TURN 不产生 host/srflx 媒体路径；TURN 不可用时失败关闭。

## 11.4 验收证据格式

每项记录命令/操作步骤、退出码、通过/失败/跳过数量、报告/截图/抓包或指标路径、浏览器与 OS 版本、网络拓扑、候选类型、资源曲线、外部限制和 TraceId。历史证据与本轮新鲜证据分开。现场截图必须避免业务敏感内容。

---

# 12. 开发任务依赖

```text
TASK-PF06-001 双 PoC/网络/许可门禁
    ↓ 用户确认生产路线并批准继续
TASK-PF06-002 模块骨架/初始化/领域
    ↓
TASK-PF06-003 API/票据/事务/Outbox
    ├── TASK-PF06-004 引擎适配/信令/TURN
    └── TASK-PF06-005 Messaging/Identity/Audit/Health 契约适配
                ↓
TASK-PF06-006 PC/PDA/Mobile 页面
                ↓
TASK-PF06-007 安全/部署/运维/升级回滚
                ↓
TASK-PF06-008 全矩阵与阶段验收
```

`TASK-PF06-001` 失败或证据不足时，002～008 保持“待细化”，并在依赖中记录门禁未满足。任何任务都不得修改 PF-00、PF-02/SystemData、PF-04/05 模块内部实现；跨模块差异通过公开契约协商和结果回写解决。

---

# 13. 开发任务拆分

## TASK-PF06-001 完成双 PoC、现场网络与许可决策门禁

**状态：** 可派遣（仅在用户另行明确派遣后执行）

**目标：** 在同一验证矩阵中比较未修改 Screego 与平台原生最小信令，形成带现场网络、画质、资源、安全和 GPL 交付证据的生产路线决策。

**输入文档：** 本文第 2.3、4.3、9、10、11.4 节；Screego 官方仓库/配置/auth/LICENSE；W3C Screen Capture/WebRTC。

**依赖：** 内部 HTTPS/WSS、可信证书、可控 VLAN/防火墙/代理、目标 Edge/Chrome、TURN 测试主机；不依赖 PF-05 业务实现完成。

**允许修改范围：** 独立 PoC、部署样例、验证脚本和证据目录；禁止修改/合并 Screego 源码，禁止改业务生产代码、CLAUDE.md、PF-00、PF-02/SystemData 和 PF-05。

**预期输出：** 两套可重复 PoC、固定版本/digest/SBOM/配置清单、网络/浏览器/质量/资源/安全矩阵、GPL 交付边界、采用/暂停/放弃决策报告。

**验证与证据：** 第 10.3、10.4 与 11.4 节；1080p 文字、30 分钟、1～3 观看者、direct/relay、UDP 受限、无互联网、重放/非白名单、CPU/内存/上行/TURN 带宽全部有新鲜证据。

**结果回写：** 本文引擎路线、配置、端口、容量、已知限制、任务 002～008 状态、执行记录和总 Todo PF-06；失败时明确功能开关关闭且不阻塞 Collaboration。

**建议提交：** `docs(pf-06): record remote assistance poc decision`

## TASK-PF06-002 建立独立模块、初始化单元与领域模型

**状态：** 待细化

**目标：** 在已实现 Collaboration.Service 中创建独立 RemoteAssistance 四层模块、Schema/前缀、双账本、Manifest/SeedSets、领域聚合和 readiness。

**输入文档：** 本文第 1～6、11.2 节；TASK-PF06-001 已批准路线；PF-05 与 SystemData 真实稳定契约。

**依赖：** TASK-PF06-001 通过；PF-05 宿主和 SystemData 初始化通用链已验收。

**允许修改范围：** Collaboration.Service RemoteAssistance 模块、所属迁移/初始化和独立测试；禁止修改 SystemData、Messaging/Presence Repository、Identity/File/Audit 内部实现。

**预期输出：** Domain/Application/Contracts/Infrastructure、六类核心表、migration/seed 双账本、SystemBaseline、健康与架构边界测试。

**验证与证据：** 状态机/不变量、PostgreSQL/SQLite、初始化十三项门禁、ModuleKey/表前缀隔离、禁止跨模块引用和 SystemData 不可用局部 NotReady。

**结果回写：** 实际目录、表/索引、manifest/seed 版本、OperationId、readiness、偏差和执行记录。

**建议提交：** `feat(remote-assistance): add module domain and initialization`

## TASK-PF06-003 实现会话、邀请、白名单、票据和可靠事务

**状态：** 待细化

**目标：** 实现 Session/Invitation/ParticipantGrant/JoinTicket/EngineLease 用例、单次兑换、超时/终止、Outbox/Inbox 和稳定错误。

**输入文档：** 本文第 3、5～7、9 节。

**依赖：** TASK-PF06-002；Identity 与 Messaging 稳定查询/命令端口。

**允许修改范围：** RemoteAssistance Domain/Application/Contracts/Infrastructure 和所属测试；禁止直读跨模块表或保存媒体/Secret。

**预期输出：** REST 用例、原子票据消费、幂等/并发、计时过期、可靠事件/Audit Outbox、权限与错误码。

**验证与证据：** 同租户/白名单/人数、并发创建、重复接受、票据过期/重放/上下文替换、撤销/会话终态、Outbox 重投和敏感数据扫描。

**结果回写：** DTO/API/事件/权限/错误、状态转换、TTL、哈希与并发策略、执行记录。

**建议提交：** `feat(remote-assistance): add secure session lifecycle`

## TASK-PF06-004 实现引擎适配、信令与 TURN 策略

**状态：** 待细化

**目标：** 按 TASK-PF06-001 已批准路线实现可替换 EngineAdapter、授权信令、ICE/TURN 配置、断线恢复和故障降级。

**输入文档：** 本文第 2.3、4、5.5、7.2、9、10 节及 PoC 决策报告。

**依赖：** TASK-PF06-003；目标 TURN、证书、DNS、代理和防火墙策略可用。

**允许修改范围：** RemoteAssistance Engine/Signaling Infrastructure、独立辅助部署清单和所属测试；禁止把 Screego 源码并入闭源模块，禁止记录 SDP/ICE 正文。

**预期输出：** EngineAdapter、SignalR/WSS 契约、临时 EngineAccess/ICE 凭据、direct/relay/forced-relay、恢复/终止、辅助单元健康和版本固定。

**验证与证据：** 消息授权/顺序/限流、未知版本、TURN UDP/TCP/TLS、强制 relay、代理/WSS、断线恢复、Secret/日志扫描和故障注入。

**结果回写：** 引擎版本、端口、Origin、ICE 策略、超时、容量、升级兼容和执行记录。

**建议提交：** `feat(remote-assistance): add signaling and turn adapter`

## TASK-PF06-005 冻结 Messaging、Identity、Audit 与 Health 公开适配

**状态：** 待细化

**目标：** 以真实稳定契约完成聊天卡片、会话成员、用户有效性/撤销、审计 Outbox 和 Platform Health 脱敏摘要适配。

**输入文档：** 本文第 2、3、4.4、7.3、9 节；PF-00/04/05 实际已验收契约。

**依赖：** TASK-PF06-003；PF-04 Audit 与 PF-05 Messaging/Presence 相关契约稳定。

**允许修改范围：** RemoteAssistance adapters/contracts、必要的跨模块公开 Contracts 增量及契约测试；禁止读取/修改相邻模块 Repository/表，禁止用 Presence 授权。

**预期输出：** CardV1 与旧客户端降级、Identity 目录/状态适配、Messaging 命令/事件、AuditFact 投影、Health 摘要和 Inbox 幂等。

**验证与证据：** 契约快照、旧/未知版本、跨租户/停用/撤销、Presence 伪造不授权、Audit 不可用策略、重复/乱序事件和架构测试。

**结果回写：** 最终公开契约版本、责任矩阵、降级、兼容窗口、偏差和执行记录。

**建议提交：** `feat(remote-assistance): integrate platform contracts`

## TASK-PF06-006 实现 PC/PDA/Mobile 邀请与独立页面

**状态：** 待细化

**目标：** 实现聊天发起、CardV1、平台独立路由/安全新窗口、PC 共享和三端观看的完整安全交互。

**输入文档：** 本文第 3、7、8、11 节；PF-01 主题/外壳与 PF-05 页面真实实现。

**依赖：** TASK-PF06-003～005。

**允许修改范围：** 前端 remote-assistance 模块、PF-05 公开扩展点、路由和所属 unit/component/E2E；禁止实现录制、远控、无人值守或任意 iframe。

**预期输出：** 三端路由、邀请/接受/拒绝、浏览器前置检查、主动捕获、观看者名单、连接/故障状态、安全新窗口、主题与可访问性。

**验证与证据：** Edge/Chrome、屏幕/窗口/标签、拒绝/停止/重授权、旧卡片、noopener、CSP/Origin、键盘/ARIA/缩放/触控、三主题明暗截图和关键 E2E。

**结果回写：** 路由、组件、状态矩阵、浏览器最低版本、视觉证据、限制和执行记录。

**建议提交：** `feat(frontend): add remote assistance experience`

## TASK-PF06-007 完成安全、部署、可观测性与升级回滚

**状态：** 待细化

**目标：** 固化 Secret/证书/Origin/代理/防火墙、指标告警、容量、版本升级、数据库回滚和辅助单元恢复手册。

**输入文档：** 本文第 4、6、8.4、9～11 节和 PoC 证据。

**依赖：** TASK-PF06-004～006。

**允许修改范围：** RemoteAssistance 配置/部署/运维文档、辅助单元清单、监控告警和安全/恢复测试；禁止写入真实 Secret，禁止修改 SystemData 实现。

**预期输出：** 环境配置矩阵、TLS/WSS/TURN 端口清单、Secret 轮换、容量/告警、Screego GPL 材料（如最终适用）、expand/contract、备份/恢复/回滚和功能开关 runbook。

**验证与证据：** 证书过期/轮换、错误 Origin、代理超时、TURN 宕机/耗尽、版本滚动升级、数据库回滚/恢复、日志脱敏和 Collaboration 不受阻塞。

**结果回写：** 配置键、Secret 引用、端口、阈值、升级/回滚步骤、许可交付清单和执行记录。

**建议提交：** `ops(remote-assistance): add secure deployment and recovery`

## TASK-PF06-008 完成自动化、现场矩阵与阶段验收

**状态：** 待细化

**目标：** 汇总全部契约、安全、三端、网络、30 分钟、1～3 观看者、资源、故障和初始化证据，判定 PF-06 是否可启用。

**输入文档：** 本文全部章节；TASK-PF06-001～007 执行记录。

**依赖：** TASK-PF06-002～007 完成；现场网络、终端、证书和监控环境可用。

**允许修改范围：** RemoteAssistance 测试/证据、验收修复和本文/总 Todo/实施 README 精确回写；禁止扩大首期功能或改受保护并行文件。

**预期输出：** 全层自动化、真实 Edge/Chrome 与现场网络矩阵、资源/带宽报告、安全报告、已知限制、功能开关结论和下一阶段稳定契约。

**验证与证据：** 第 10.3、11.1～11.4 节全部可核验；任何外部缺失项标记待验收，关键门禁失败时保持关闭且不伪报完成。

**结果回写：** 任务/阶段状态、提交与命令证据、版本/容量/限制、总 Todo、实施 README、PF-07 Health 输入契约。

**建议提交：** `test(remote-assistance): complete pf-06 acceptance`

---

# 14. 完成标准

- 双 PoC 和现场矩阵先于生产开发，生产路线由证据和用户确认冻结。
- RemoteAssistance 在 Collaboration.Service 内保持独立四层、Schema/前缀、双账本、权限、迁移/种子和测试。
- 一名共享者、1～3 名白名单观看者，会话/邀请/授权/票据状态机和并发不变量通过验证。
- CardV1、REST、信令、事件、错误和兼容策略具备契约测试；旧客户端安全降级。
- Edge/Chrome 的 1080p 文字、30 分钟、1～3 观看者、同网段/跨 VLAN/无互联网/UDP 受限/强制 TURN 全部有现场证据。
- PC 共享、PDA/Mobile 观看、主动授权、安全新窗口、三主题/明暗、可访问性和错误状态通过。
- JoinTicket、EngineAccess、TURN Secret、Origin/CORS/CSRF/CSP、重放、限流和脱敏门禁通过。
- 无录屏、画面/音频/SDP/ICE 落盘，无远控、无人值守和默认音视频会议。
- SystemData/RemoteAssistance/引擎/TURN 故障不会阻塞 Collaboration；关键门禁失败时功能开关关闭。
- Screego 若进入最终交付，必须完成独立 GPL-3.0 法务/源码交付/升级维护评审；否则只保留未修改 PoC 证据。
- 所有外部环境缺失项保持待验收，不伪报完成。

---

# 15. 执行记录

| 任务 | 状态 | 执行者/任务 | 提交 | 验证证据 | 结果回写 |
| --- | --- | --- | --- | --- | --- |
| TASK-PF06-001 | 可派遣（待用户另行派遣） | - | - | - | - |
| TASK-PF06-002 | 待细化（PoC 门禁未满足） | - | - | - | - |
| TASK-PF06-003 | 待细化（PoC 门禁未满足） | - | - | - | - |
| TASK-PF06-004 | 待细化（PoC 门禁未满足） | - | - | - | - |
| TASK-PF06-005 | 待细化（PoC 门禁未满足） | - | - | - | - |
| TASK-PF06-006 | 待细化（PoC 门禁未满足） | - | - | - | - |
| TASK-PF06-007 | 待细化（PoC 门禁未满足） | - | - | - | - |
| TASK-PF06-008 | 待细化（PoC 门禁未满足） | - | - | - | - |

截至 2026-08-14，只完成仓库/官方证据核对、推荐方向确认、详细设计和九字段任务卡。未开发、未派遣、未构建、未测试、未提交。工作树中的 CLAUDE.md、PF-00、PF-02/SystemData、PF-05 及其他并行改动均保持原状。

---

# 16. 下一阶段输入契约

PF-07 Platform Health 在 PF-06 真实验收后只能依赖：

```text
ModuleKey/readiness/OperationId
RemoteAssistance 模块可用/降级/关闭状态
Signaling/TURN 脱敏依赖摘要
活跃会话数量和建连/失败/relay 聚合指标
稳定错误码与 TraceId
```

Platform Health 不得获取参与人名单、原因、JoinTicket、EngineAccess、TURN 凭据、SDP/ICE、IP 明文或媒体内容。其他阶段不得从本文推断录屏、远控、无人值守、音视频会议或跨租户协助能力。

---

# 17. 文档自审清单

- [x] 目标文件创建前已确认不存在。
- [x] 当前仓库、最新提交、工作树保护边界和真实未实现状态已记录。
- [x] 蓝图 05/09/32/33、模板、PF-04/05、Identity 和 SystemData 最新输入已核对。
- [x] Screego 官方仓库、配置、认证源码和 GPL-3.0 已核对，未把蓝图预设写成采用结论。
- [x] 双 PoC/现场网络/许可先于领域开发，失败时功能开关关闭且不阻塞 Collaboration。
- [x] 独立模块、Schema/前缀、ModuleKey、双账本、四类种子、readiness 和 Secret 边界完整。
- [x] 会话、邀请、参与人、票据、信令、ICE/TURN、状态机、事务和 Outbox/Inbox 完整。
- [x] Identity、Messaging/Presence、Audit、Health 只通过公开契约，无跨模块 Repository/表/外键。
- [x] API/Hub/事件/权限/错误、三端页面、安全、审计、可观测性、部署、升级回滚和验收完整。
- [x] 八张任务卡均且只使用九字段；任务依赖与执行记录一致。
- [x] 无模糊占位表达。
- [ ] 用户完成实施文档 09 全文最终审阅。
- [ ] 如用户另行授权提交，仅精确暂存本任务文档并执行 `git diff --check`。
