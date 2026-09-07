# 07-Industrial Platform File / Notification / Audit 开发实施方案

版本：V1.3（Core 独立离线验收回写）
核验日期：2026-09-07（Asia/Taipei）
阶段：PF-04 Core 001～009 开发完成并通过独立离线验收；真实环境项待复验。010 为后续增强。
依据：[评估上传设计](chatgpt-conversation://6a9d0e0a-ea6c-83e8-a243-072e8022bf65)、仓库现状及现行蓝图。2026-08-13 的设计批准仅作历史记录，不自动授权修改后方案的开发。

# 1. 文档说明

## 1.1 目的与权威来源

本文维护 PF-04 的详细设计、开发依赖、九字段内部任务、验收与结果回写。2026-09-06 Core 001～009 已获开发授权并整包派遣；PF-02/PF-03 提交后，用户改定执行者在主工作树 `develop` 实施、自测且不提交，独立验收在稳定交接后执行，最终提交与集成仍由总控负责。

- 宿主与数据所有权：[蓝图 32 第 2～4 章](../blueprint/32-Industrial%20Platform%20Service%20Host与内部模块边界.md)。
- 初始化、环境策略与本地事实：[蓝图 33 第 3～8、12 章](../blueprint/33-Industrial%20Platform%20SystemData数据库编排与环境引导.md)、[实施 05](05-Industrial%20Platform%20SystemData开发实施方案.md)。
- 模块范围：[蓝图 05 第 7.3、8.1 节](../blueprint/05-Industrial%20Platform平台基础功能与独立模块设计.md)、[蓝图 30 第 8 章](../blueprint/30-Industrial%20Platform日志审计与可观测性平台设计.md)。
- 阶段状态：[CURRENT](../status/CURRENT.md)、[总 Todo 第 11、21 章](../blueprint/09-Industrial%20Platform开发总TodoList.md)。
- 本文沿用[实施模板](TEMPLATE-开发实施方案.md)；整个 PF 是派遣单位，内部任务不是独立派遣或提交门。

## 1.2 当前输入状态与证据

核验基线为 `develop@4eefeed044f9fcda6d67274b2a3c0908fa68a261`，读取时间为 2026-09-07。主工作树保留 PF-04 Core 的未提交前端改动；本节只引用本轮新鲜命令，不把历史验收或未运行的真实环境当作通过。下列代码路径均相对仓库根目录。

| 分类 | 核验结论 | 证据位置与限制 |
| --- | --- | --- |
| 已实现且有历史验收证据 | SystemData 宿主、初始化器、组织/导航、Outbox 等已存在；PF-01 已交付；PF-03 七模块已完成并合入 | `docs/evidence/PF-02.md`、`docs/evidence/PF-03.md`、实施 04 执行记录、`docs/status/CURRENT.md`；PF-03 功能提交 `969ee156`、合并 `e9452b47` |
| 已完成并归档 | PF-02 当前功能范围已完成并合入，真实云 Docker/UnifiedHost 与完整视觉矩阵已由用户验证 | `docs/tasks/archive/PF-02.md`、PF-02 evidence 最终收束记录；PF-04 只消费稳定公开契约 |
| 已有能力，保留并增量接入 | Identity 本地登录/操作审计、SystemData 本地审计与 Outbox、ReferenceData Outbox 均存在；它们不等于中央 Audit Core | `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Authentication/{LoginAuditSink,OperationAuditSink}.cs`；SystemData Infrastructure 的 `Reliability/{SqlLocalAuditCommand,SqlControlPlaneOutbox,ControlPlaneOutboxDispatcher}.cs`；ReferenceData Infrastructure 的 `Outbox/` |
| 已有宿主与初始化契约 | UnifiedHost 已组合 SystemData，Gateway 有服务前缀转发；初始化协议是 IServiceInitializer 的 Inspect/Plan/Apply/Verify 与本地状态 | `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Api/Modules/SystemDataUnifiedHostModule.cs`；`src/backend/src/Gateway/IndustrialPlatform.Gateway/Configuration/GatewayRouteFactory.cs`；`src/backend/src/BuildingBlocks/IndustrialPlatform.Application.Abstractions/Initialization/ServiceInitializationContracts.cs` |
| 已实现且通过独立离线验收 | PF-04 File/Notification/Audit 后端能力、权限/迁移/导航和 PC/PDA/Mobile 前端入口已在代码中；本轮完成 Shell 通知铃、系统消息、PDA 通知路由、管理页权限门禁/错误/服务端分页/确认动作与 Audit lifecycle UI 收口 | 后端 fresh Release build/test、前端 124 文件/926 测试、类型检查及生产构建均通过，详见 `docs/evidence/PF-04.md` |
| Core 契约已闭合 | 公告列表返回 page/pageSize/total；FileObjectV1 返回用途、上传者、引用数量/摘要并支持对应服务端筛选；真实浏览器/服务链仍待验收 | 保持现有 DTO 与服务端分页实现；外部环境限制见 evidence |
| 当前无法核验 | 真实 UnifiedHost/Gateway 登录、权限、菜单发布、浏览器交互、实际 PostgreSQL/Redis/RabbitMQ/ClamAV、多实例文件锁、真实手机/跨设备链路 | 当前机器无所需监听服务和容器运行时；对应矩阵保持待验收，不以 Mock/fixture 替代 |

当前 SystemData 初始化器位于 `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/DatabaseOrchestration/Initialization/SystemDataServiceInitializer.cs`，`ServiceKey=systemdata`、初始化 `ModuleKey=systemdata`。本轮采用服务级迁移接入，不能为迎合旧文档把既有控制面、账本或数据迁移重建。

## 1.3 开发就绪评估与启动条件

2026-09-06 按用户“重新评估调整，后续准备任务开发”的要求复评：001～009 的需求、所有权、状态/契约、允许范围、依赖、输出与验收已足够支撑实现，调整为 **可派遣**。上一轮将接入/集成尚未验证也计为“待细化”，混淆了设计就绪与功能验收；本次纠正分类，不要求先完成实现才能获得开发状态。

| 事项 | 分类 | 执行安排及影响 |
| --- | --- | --- |
| Core 模型、强哈希续传、通知刷新、审计事务语义 | 已确定设计 | 按本文实施，不再普遍重开需求或选型讨论 |
| PF-00/01/02 输入及现有 WIP | 001 的启动核验 | 开工时记录稳定交付标识、核对必要契约；具体缺口只阻塞相关路径，不要求 PF-02 全包重新验收 |
| tus 版本/store/许可、代理头与并发写入 | 003 的任务内技术验证 | 以 tus 主路线开展最小持久上传/重启/接管验证，再扩展完整路径；版本与适配器核验是该任务工作，不是任务可派遣前必须已有的成果 |
| 事务适配、扫描器/存储、容量与终端性能 | 002/003/004/009 的实现细节 | 在既定契约内由执行者选择最小可用配置并回写依据；不能满足安全边界时报告具体阻塞，不把全阶段退回待细化 |
| 真扫描、真实多实例、Gateway、实际手机、跨服务身份 | 对应功能验收条件 | 能先完成契约/适配与隔离验证；缺失环境只能使对应真实验收待完成，不伪造通过 |
| 工作线、负责人、共享文件边界 | 总控派遣准备 | 使用当时稳定基线和获准工作区，排除现有 WIP 冲突；属于工作包配置，不是产品设计缺口 |
| 010 高级合规 | 后续需求待定 | 保持待细化，不包含在 Core 工作包，不阻塞 001～009 |

“可派遣”表示设计与执行说明已就绪，不表示九项可无视依赖同时开工，也不表示功能已开发/验收。本轮已将整个 PF-04 Core 一次派遣，从 001 按第 13 章连续推进，内部步骤不逐项等待确认。首次接入检查与最小技术验证归对应任务，常规版本/参数/适配选择在已定范围内直接处理；只有改变主路线、增加基础设施或降低安全要求才回总控裁决。

服务身份未稳定前可完成经验证的进程内公开契约和接收适配，不开放不可信外部服务写入口、不伪造信任头；分布式真实链仍是对应验收项。开发任务须记录实际接入证据，无法满足的真实链路保持对应验收项未通过，不扩大信任边界。

# 2. 定位、目标与职责边界

一个 `SystemData.Service`，内部包括 SystemData Core、File、Notification、Audit；统一部署嵌入 UnifiedHost，分布式部署由现有 SystemData API Host 承载。三个模块的数据所有权与公开契约独立，默认共用服务级持久化生命周期，不创建三个微服务或内部 HTTP 总线。

| 模块 | 当前必须交付 | 已有能力的处理 | 简化/后续增强 |
| --- | --- | --- | --- |
| File | 跨设备断点续传、强内容确认、隔离/校验/扫描、受权下载、引用保护、到期清理和延迟删除 | 没有找到成熟现有上传实现；消费当前身份、宿主、前端和可靠消息，保留既有用途安全政策 | 一条上传主路线；全局秒传、跨用户去重、复杂复扫、多扫描引擎、完整法律冻结流程后置 |
| Notification | 公告、系统通知、持久收件箱、幂等投递、已读/撤回/失效、未读数、跨端刷新与安全跳转 | 复用身份/组织公开契约及前端壳；Shell 入口不算已实现通知 | 三个核心模型；模板用代码/简单配置；不强制独立 AudienceSnapshot/AudienceBatch；复杂编排、多渠道、偏好、摘要、可视化模板后置 |
| Audit Core | 可信事件、可靠接收、幂等冲突检测、脱敏、追加事实、授权查询、必要导出/访问审计、基础保留清理与恢复 | 保留各服务本地审计、事务及 Outbox；通过生产者公开适配增量投递，不接管或删除旧表 | Audit Advanced 单列：哈希链、签名 checkpoint、外部锚点、Legal Hold 审批、敏感值解密、复杂合规导出 |

不在 PF-04 范围：聊天/会话/回复、MES 业务状态或历史、PF-07 通用调度平台、PF-10/10A 业务、对象存储和杀毒引擎自身、第二套数据库编排或事件总线。

**既有约束不延期：** 蓝图 05 第 8.1 节已规定 Collaboration 附件强制扫描、默认 50MB/类型白名单、内容 365 天、合规访问审计 3 年和法律保全优先。PF-04 保留用途政策与禁止删除/访问限制接入；完整审批 UI 可后置，但 PF-05 启用受保全内容前必须具有可执行的保全登记、阻止清理和可靠审计路径。若客户另有明确合规要求，记录来源和适用范围后纳入相应阶段，不靠延期标签豁免。未找到 PF-04 全场景立即交付哈希链/外部锚点的已确认要求。

# 3. 前后端及跨模块协作目标

| 消费方 | 输入/职责 | PF-04 输出 |
| --- | --- | --- |
| Identity | 可信 TenantNId、用户/服务身份、权限 | Audit 写入适配、资源授权；不能依赖任意请求头自报身份 |
| SystemData | 自有初始化器、权限目录、组织公开查询与宿主组合 | 三个逻辑模块的表命名空间、权限、迁移增量和局部健康 |
| 业务生产者 | 自有事务/Outbox、用途与当前资源授权 | AuditFactV1、FileNId/引用契约、通知发布契约 |
| PC/PDA/Mobile | 用户重新选择本地原文件、当前认证、统一组件 | 标准上传组件、跨端会话发现/接管、通知中心及管理页 |

稳定 NId、UTC 时间、版本化契约与错误码贯穿边界。租户来自可信上下文；文件指纹、FileNId 和上传 URL 均不证明权限。模块之间只调用公开应用契约或版本化事件，不注入对方 Repository、直读表或创建跨模块外键。

# 4. 总体架构与数据流

## 4.1 初始化与故障边界

PF-04 只接入蓝图 33 的初始化/环境/迁移契约：同一 `systemdata_db` 目标、现有服务级迁移和 migration/seed ledger、必要种子/权限登记、本地 Inspect/Verify。当前无独立持久化生命周期证据，不新增三个初始化单元、签名宿主 Manifest 或父子编排体系。Standard/Advanced 初始化策略按平台已确认环境要求执行，PF-04 不另写规范。

核心数据库身份、Schema/必要 Seed/Bootstrap 不兼容必须阻断相应核心启动。运行时扫描器故障隔离文件处理，SignalR 故障只影响提示，导出故障只影响导出；消息暂不可用但本地 Outbox 可靠保存时可积压。现有宿主仍可能因 Identity/SystemData 的 Redis/RabbitMQ 必需检查报告 503（PF-03 evidence 已记录），本设计不宣称运行代码已改为局部降级，也不在本轮改动这些安全契约。

## 4.2 Audit Core

成功关键变更：业务变更 + 本地审计 Outbox 同一事务提交 → 重试投递 → Audit 验证/脱敏/幂等接收 → 追加事实。

失败/拒绝/回滚：终止或回滚业务事务 → 独立可靠事务/持久通道记录失败事实 → 原事件标识重试投递。记录不能依赖已经回滚的事务。无跨库原子提交要求。

## 4.3 File

手机选文件 → 采样发现候选/创建会话 → 完整哈希登记 → 隔离上传 → 中断。
电脑重新选择原二进制文件 → 查本人有权候选 → 完整哈希确认 → 原子接管 → 查询服务端持久断点 → 续传 → 幂等完成 → 服务端全量校验 → 扫描 → 可用。

电脑必须能读取原文件，系统不会凭指纹取得本地不存在的文件；压缩、转码或修改后的文件重新上传。

## 4.4 Notification

发布/可信系统意图 → 内容与发布时受众固化 → 持久 InboxDelivery → Outbox 提示 → 客户端查询数据库事实。
重连及收到变更提示时刷新当前列表和未读数，同时反映旧通知已读/撤回/过期；不只拉“新通知”。

# 5. 项目结构与引用关系

沿用 `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.{Contracts,Domain,Application,Infrastructure,Api}/`，各层按 File/Notification/Audit 子目录落位；复用现有 API 组合根，不增加空 Host/工程。Contracts 不引用存储/扫描/tus 类型；测试落现有 SystemData、UnifiedHost/Gateway 测试项目，真实外部依赖使用现有 IntegrationTests。

前端复用 `src/frontend/src/` 中 api、pages、components、router、locales 的既有结构；标准上传组件跨 PC/PDA/Mobile 共用。共享壳、管理表格与 PF-02 正在编辑的文件，后续派遣前由总控明确最小修改范围与顺序。

# 6. 全局技术与实施约束

- 领域实体遵守平台 NId/生命周期规则，不逐表复制字段。UploadSession、Delivery、Outbox 等技术记录只采用所需键/状态/CAS；不机械继承软删除或双并发令牌。
- .NET 时间用 DateTimeOffset，UTC 持久化，API 使用带时区 ISO 8601。
- 可重试操作明确幂等语义：创建/发布以调用者作用域的请求键去重并比较内容；完成绑定会话；引用绑定消费者/资源/用途；已读取首次时间。幂等记录与业务变化同事务；无需每个接口独建幂等表。
- 可变记录采用单一版本 CAS 或自然状态条件更新；接管写入 fencing epoch 负责废止旧写入者，是上传专用语义，不推广成全平台双令牌。
- 普通管理列表复用受控查询的页码/页大小；收件箱滚动和审计大时间范围可采用稳定排序游标。分页游标不代表跨端变更同步。
- 复用现有 Outbox/Dispatcher、数据库事务与重试设施，真实入站消费者才增加持久去重。现有实现的事务参与、同键冲突、确认与死信恢复须按 002 核验，不能仅因类存在就声称满足 Audit 可靠性。
- 不新增第二套 Outbox 平台、事件总线、通用 Scheduler 或健康平台；模块作业复用现有托管服务模式。

# 7. 领域模型或核心组件详细设计

## 7.1 Audit Core

### 7.1.1 不可变事实与生命周期

AuditFactV1 输入：AuditEventNId、EventType/EventVersion、OccurredOn、ProducerServiceKey/ProducerModuleKey、ActorType/UserNId/ServiceNId、Action、ResourceType/ResourceNId、Outcome、RiskLevel、OperationId/TraceId/CorrelationNId、Summary 及白名单 Metadata。Actor/租户与生产者声明必须按认证来源验证，拒绝冒充；ReceivedOn 由中央服务填写。

事实只追加，更正生成关联原事实的新事件；不提供普通 UPDATE/DELETE 接口。TenantNId、动作、主体、资源、结果和时间不可改。接收时的 RetentionPolicyVersion 可作不可变政策快照；当前 RetainUntil、DeletionBlocked、归档/清理进度放独立 lifecycle 数据，变更追加审计。达到合法保留条件后的受控清理是明确例外，先可靠记录清理范围/数量/依据并可恢复，不把“追加型”误写成无限保留。

### 7.1.2 必须审计的动作与可靠路径

| 动作目录 | 类型/路径 | 保存失败时行为 |
| --- | --- | --- |
| 权限/用途/引用变更，公告发布撤回，关键资源写入 | 成功业务审计与本地 Outbox 同事务 | Outbox 失败回滚业务；中央不可用但已落库可继续并告警 |
| 登录/权限拒绝、上传接管拒绝、关键业务执行回滚 | 安全/失败事实，经回滚后独立可靠路径 | 拒绝仍拒绝；可靠记录失败必须告警/暴露稳定诊断，相关高风险后续动作按策略关闭，禁止吞异常 |
| 受保护下载授权、Audit 查询/导出、删除/保留政策变更 | 必要访问审计先可靠落库再放行；实际流访问另记结果 | 不能可靠保存则不放行高风险数据或破坏性动作 |
| 健康探测、普通性能诊断、无安全意义的读操作 | 技术日志/指标 | 不机械写成同强度业务审计，不用技术日志替代以上事实 |

以 `TenantNId + ProducerServiceKey + AuditEventNId` 唯一去重。同键同规范化内容返回原接收结果；同键不同内容冲突并告警。内容比较使用固定版本的规范化输入摘要（排除 ReceivedOn 等接收端生成值），它只用于幂等，不是 Advanced 哈希链。首次接收的脱敏版本固化，后续重试不因脱敏政策更新生成重复事实。

接收失败分瞬态重试与永久隔离；保留最小安全载荷和原因，敏感原文不进入普通日志或任意死信。必要保留原件时走受控加密存储及明确期限。失败审计用服务拥有的独立事务落库，不依赖请求取消令牌立刻终止持久保存。生产者事故中尚未可靠落库的事件不能声称绝对不丢失；002/008 必须验证风险门禁和恢复边界。

### 7.1.3 查询、保留与必要导出

查询必须有租户/权限/字段白名单、最大时间窗和页大小；索引按实际 Actor/Action/Resource/Outcome/OccurredOn 场景选择。前后值只保留获准字段的脱敏摘要，禁止整对象、密码/token、连接串和请求正文自动落库。

Core 先提供有行数/时间/频率上限的授权流式导出，转义表格公式注入并记录访问；不依赖 File 完成，避免 Audit → File → Audit 循环。大批量异步合规导出、解密及证据验证属于 Advanced。基础保留清理必须服从可信服务端期限和禁止删除标记，缩短期限不自动追溯清理。

## 7.2 File

### 7.2.1 标识、采样与内容确认

| 标识 | 语义 |
| --- | --- |
| SampleFingerprint | 仅快速查找候选，不唯一标识文件，不作授权 |
| ExpectedContentHash / ContentHash | 客户端所选完整文件的 SHA-256 预期值 / 服务端完整内容实测值，完成时必须相等 |
| UploadSessionNId | 随机、独立的上传任务业务标识 |
| WriterEpoch + 上传授权 | 绑定可信身份、会话、用途、有效期；接管后旧写入者失效，不能从内容推导 |
| FileNId | 业务文件标识，独立于内容、上传任务及存储键 |

采样规则 `sample-v1`：L 为字节数，W=65536；L≤3W 时采整个文件一次，否则采 `[0,W)`、`[floor((L-W)/2),floor((L-W)/2)+W)`、`[L-W,L)`。哈希输入依次为 ASCII `IPF:sample-v1\n`、L 的无符号 64 位大端编码、每段 offset 与 length 的同编码及原始字节，输出小写 SHA-256 hex。不同版本不得混合匹配；001/003/006 共享边界长度/Unicode 文件名无关的固定向量。文件名和修改时间仅展示，不参与强内容确认。

**首期强校验只走完整哈希路线：** 允许先创建会话，但首个字节写入前必须登记完整 ExpectedContentHash（精确 64 位 hex），与大小一并锁定；客户端分块读取全文件计算，明确展示“正在确认文件”，不以一次性读入全部内存为前提。跨设备接管前新设备计算完整哈希并比对。原会话缺少完整哈希时返回 `FILE_UPLOAD_PROOF_REQUIRED`，不能确认则创建新会话重传；首期不新增已上传前缀证明协议，也不能补写一个新预期值后直接继承旧字节。

采样相同但未采样区不同、同名不同内容或大小不符时拒绝接续（`FILE_UPLOAD_CONTENT_MISMATCH`），用户可新建任务。最后服务端从隔离对象完整读取，与锁定预期大小/hash 比较；只计算拼接结果的 hash 而无预期比较不可验收。完整 hash 只证明内容一致性，不证明拥有/访问权限。

### 7.2.2 持久会话、权威断点与接管

UploadSession 保存 TenantNId、UploaderUserNId、Purpose/ResourceNId、采样版本与指纹、ExpectedSize/ExpectedContentHash、状态、ExpiresOn、底层 TransportUploadId、WriterEpoch、RecordVersion、最终 FileNId/处理 OperationId。对同租户同上传者且当前仍满足用途/资源权限的会话提供有上限的候选发现；只返回必要元数据，不泄漏他人文件、路径或 URL。跨用户接续、全局去重不开放。

1. 发现候选不获得写权限；证明接口比较锁定完整 hash/大小并生成短期、绑定 session/version/hash 的证明票据。
2. 接管必须重新授权，并与当前写入使用同一会话排他锁/CAS 边界。先阻止新旧写入、等待或中止在途写入至可确认持久边界，再增加 WriterEpoch，保存新写入者并返回短期授权。
3. 所有 PATCH、完成、暂停、取消及底层写入入口校验当前 epoch；权限校验与实际持久写入在相同互斥范围，不能仅在请求开始验一次后允许旧长请求继续写。进程重启/多实例时互斥必须仍成立，单进程内存锁不能证明多实例安全。
4. 从底层 store 查询已可靠保存的 Upload-Offset；客户端显示和业务库缓存不是权威，未确认尾部允许重传。接管成功后再 HEAD 获取实际断点。
5. 接管后旧请求返回 `FILE_UPLOAD_WRITER_REPLACED`；版本/断点冲突返回冲突并重新查询，不能盲重试写入。过期/取消在锁内禁止后续写入、接管与完成。
6. 底层资源创建与业务会话不跨存储伪造事务：创建请求幂等绑定 TransportUploadId；崩溃后补记或回收无主临时对象。存储已写而业务进度未更新时读取底层事实恢复，不能推进到客户端自报位置。
7. 完成以会话唯一键和条件状态更新固定 FileNId/OperationId；重复返回相同结果。处理作业可恢复，崩溃后继续校验/扫描，不重复发布 Available。

### 7.2.3 传输主路线与组件边界

设计优先候选为 tusdotnet + tus-js-client，单条顺序偏移上传路线，不同时建设 S3 Multipart。tus 负责传输与偏移协议，File 模块负责会话发现、授权/接管、内容证明和生命周期。底层 store 的持久字节与偏移是传输权威，业务 upload_session 只保留映射及可重建观察值，**不自建 upload_part 表或平行分片状态机**。

官方资料核验于 2026-09-06：[tus 协议](https://tus.io/protocols/resumable-upload)定义 HEAD/PATCH 偏移及冲突，认证留给应用；[tusdotnet](https://github.com/tusdotnet/tusdotnet)是 .NET 实现；[客户端 API](https://github.com/tus/tus-js-client/blob/main/docs/api.md)支持已有 uploadUrl，本地 URL 存储不提供跨设备发现。[服务端许可证](https://github.com/tusdotnet/tusdotnet/blob/master/LICENSE)与[客户端许可证](https://github.com/tus/tus-js-client/blob/main/LICENSE)为 MIT，实际锁定版本及第三方存储适配器须重新登记许可证。

**尚未集成验证：** .NET 10/当前 Vue 构建、移动端全量 hash 的内存/耗时、持久卷重启、接管与 store 锁结合、Gateway 请求体/超时/CORS/Location/上传头、多实例持久存储和清理。003 在获准开发后验证这些条件并记录版本/证据；验证失败不自研双传输栈。只有已确认部署更适合对象存储直传时，才改选 S3 Multipart 并替换本节的传输权威与验收，不叠加第二条首期路线。没有验证结果前只称候选，不宣称 tus 自带业务接管。

### 7.2.4 分离生命周期维度

| 维度 | 状态/规则 |
| --- | --- |
| 会话 | Created → Uploading ↔ Paused → Completed；非终态可 Cancelled/Expired。断网仅客户端中断，不自动丢弃会话；Completed 表示字节接收封闭，不代表文件可用 |
| 处理 | PendingValidation → PendingScan → Available；校验/恶意失败为 Rejected，扫描不可用保留 PendingScan 并显示故障；删除流程独立 |
| 扫描 | NotScanned、Pending、Clean、Malicious、Error/Unknown；重试保留 ScanAttempt，不把失败覆盖为 Clean |
| 保留/访问/删除 | RetainUntil、DeletionBlocked、ReadBlocked、DeleteRequestedOn、DeletedOn；保留不自动禁止读，禁止读不自动允许删除 |

默认及已有用途继续强制扫描；只有未来明确批准的可信服务端用途策略可声明不要求扫描，此时结果只能标 NotRequired，不能标 Clean，客户端无权设置。当前必交付文件须验证完成且扫描 Clean、无 ReadBlocked 才可用。扫描器不可用/超时/未知保持隔离并重试，不开放下载或预览。

文件名去路径化、控制字符和 Unicode 规范化；用途配额/大小/扩展名/MIME/魔数及恶意内容防护仍必需，文件名不构造路径。对象先存隔离区，校验通过再受控提升；存储键不含凭据、不向 API 暴露物理路径。

下载逐次校验身份、当前资源权限、状态和访问限制，Core 默认授权流式代理，支持断点下载的 Range 请求亦重新授权并记录访问结果。若后续选择短期预签名 URL，签发审计不等于实际下载审计，URL 有效期内可重复使用（[AWS 官方说明](https://docs.aws.amazon.com/AmazonS3/latest/userguide/using-presigned-url.html)）；一次性兑换只约束兑换动作，不能承诺兑换后 URL 只下载一次。需要即时撤销或严格访问记录的用途继续走受控代理。

引用以 `TenantNId + FileNId + ConsumerModule + ResourceNId + Purpose` 幂等登记/释放，无跨模块外键。延迟删除先标申请，执行前在同一并发边界再检查活跃引用、保留、禁止删除和必要审计；进入不可逆删除阶段后拒绝新引用。冻结期间可以登记申请，不能物理删除。过期/取消会话、无主隔离对象及无引用文件分批可恢复清理，不引入完整 FileRetentionCase 聚合。

## 7.3 Notification

核心模型为 Announcement（草稿/排期/发布/撤回）、NotificationMessage（不可变内容、来源、失效与跳转快照）、InboxDelivery（租户/接收人/通知唯一键、首次投递、首次已读、撤回/失效可见状态）。模板使用简单配置/代码；受众规则版本及解析时点放发布记录，实际收件人由持久 Delivery 表达，不强制独立 AudienceSnapshot 聚合。

- 发布前校验容量上限，通过公开用户/角色/已稳定组织查询固化收件人集合；重试不能按变化后的角色重新解析并改写原受众。首期有界受众可在单个受控事务写消息/Delivery/Outbox；超过上限拒绝并说明范围。只有真实容量或独立恢复需求成立后才引入 AudienceBatch 及分批快照，不能无界展开。
- 发布键和 `TenantNId + NotificationNId + RecipientUserNId` 唯一约束防重复；投递重试以已有收件人为事实。已读是幂等首次时间，批量已读设数量上限；已读不等于确认收到，确认不等于业务完成，首期不增加签收动作或第二套业务状态。
- 撤回保留历史投递事实；过期按服务端时间判断，未读数与列表使用同一过滤规则，即使过期作业延迟也不计入有效未读。
- SignalR 只发有权限的变更提示，不发敏感正文。客户端首连/重连、切回前台、提示到达时重新查询未读数与当前列表；连接失效时采用有界定期刷新（首期默认 60 秒，可按服务端策略收紧），列表保持可手动刷新，不构建变更日志或事件回放。
- 提示包含新投递、已读、撤回等类别，丢失/重复只影响刷新时机；不改变持久 Delivery。分页 after 只用于浏览更旧/更新记录，不用于恢复旧记录状态。
- TargetType + TargetNId + 白名单参数由注册资源解析；正文编码/富文本白名单、拒绝脚本和任意 URL。受众按发布时间固化，打开业务目标仍校验当前权限，无权时统一提示，不泄漏资源存在性。

# 8. 数据与持久化设计

## 8.1 归属与迁移

沿用 SystemData 自有服务级迁移/ledger/连接与配置映射的 `systemdata_db`，逻辑前缀建议 `system_file_`、`system_notification_`、`system_audit_`，在 001 按现行命名落定。不搬迁已有 `system_data_*` 表，不为每个逻辑模块新建物理 Schema/迁移账本。SQLite 本地测试、PostgreSQL 18 及 Shared/PerService 的适用环境沿用蓝图 07/33；不新增 Provider/拓扑组合。

Outbox 可以共享服务级技术存储，但必须具有可判定的生产模块归属和业务事务参与；现有实现缺少字段/能力时按公开适配增量补齐，不让模块直接操作另一模块的业务表。中央 Audit 接收和 Notification 输入确有去重需求，可用唯一事实键/投递键或最小 ingress 记录；不机械建三套 Inbox。

## 8.2 当前表与增强表

| 范围 | 当前逻辑表/记录 | 不纳入当前必选 |
| --- | --- | --- |
| File | upload_session、file_object、file_scan_attempt、file_reference_grant；保留/限制字段归 file_object | upload_part（tus 路线不建）、file_retention_case、全局内容去重表 |
| Notification | announcement、notification_message、inbox_delivery；受众规则固化在发布记录 | notification_definition 独立聚合、audience_snapshot、audience_batch |
| Audit Core | audit_fact、audit_lifecycle、audit_ingress_failure；幂等摘要可在事实行保存；保留政策可用简单受控配置 | integrity_checkpoint、legal_hold 审批、敏感值密文库、复杂 export_job |
| 公共技术 | 现有服务 Outbox/Dispatcher、所需持久入站去重、作业进度 | 第二套事件总线/调度器/数据库控制面 |

租户业务唯一键/查询以前导 TenantNId 隔离；技术全局键的例外须明确不能跨租户泄漏。存储表名在逻辑名上加所属前缀。

## 8.3 索引、并发与清理

Audit 优先租户+时间/主体/资源索引，不以强制分区、哈希链换取首期查询能力。File 候选索引为租户+上传者+用途+采样版本/指纹+会话状态；hash 不提供跨用户存在性查询。Notification 索引服务接收人+状态+时间的列表/未读查询。

清理和重试采用现有有界批处理、租约或条件抢占、重试上限和恢复位置，禁止每模块新建通用调度平台。审计先记录删除意图，物理处理后追加结果；重启可依据意图和外部对象存在性收敛。

# 9. API、事件与外部集成契约

## 9.1 路由与幂等语义

外部均以 `/systemdata` 为服务前缀，符合现有 UnifiedHost/Gateway 入口；独立 API Host 内部使用 `/files`、`/notifications`、`/audits`。以下是设计契约，尚未存在生产接口，001 与各模块实现时先固定 DTO/错误/授权，005 只做联合兼容验证。

| 方法与内部路径 | 语义 |
| --- | --- |
| POST /files/upload-sessions | 创建会话；请求键绑定调用者/用途与输入摘要，返回同一 session |
| POST /files/upload-sessions/discover | 仅搜索有权候选，输入采样版本/指纹/大小/用途，不返回上传授权 |
| GET /files/upload-sessions/{nId} | 会话、服务端进度观察、到期与处理结果；重新授权 |
| PUT /files/upload-sessions/{nId}/content-hash | 首次写入前固定完整预期 hash/大小；重复相同值幂等，已有字节/不同值拒绝 |
| POST /files/upload-sessions/{nId}/resume-proof | 比对完整 hash/大小，签发短时会话版本证明；缺依据拒绝 |
| POST /files/upload-sessions/{nId}/takeover | 证明+版本 CAS 接管，返回新 epoch 与受限上传入口；重复请求不能反复增加 epoch |
| POST /files/upload-sessions/{nId}/pause | 当前写入者暂停，封闭在途写入并保留可靠进度；同设备恢复也经 resume-proof/takeover 恢复 Uploading、刷新授权并重新 HEAD |
| POST /files/upload-sessions/{nId}/cancel | 幂等终止与延迟清理，已完成则拒绝取消、走文件删除规则 |
| POST /files/upload-sessions/{nId}/complete | 幂等封闭字节接收，返回固定 FileNId/OperationId，异步处理不等于 Available |
| HEAD/PATCH /files/uploads/{transportId} | 选定 tus adapter 的偏移传输；每次授权+epoch，底层 Location 由宿主适配，不能绕过业务会话创建裸资源 |
| GET /files/{fileNId}、GET /files/{fileNId}/content | 元数据/受权内容访问，状态与资源权限逐次判定 |
| PUT/DELETE /files/{fileNId}/references/{referenceNId} | 消费者权限与自然键幂等登记/释放 |
| POST /files/{fileNId}/deletion-requests | 登记删除申请，绝非即时物理删除 |
| PUT /files/{fileNId}/restrictions | 可信策略管理员 CAS 修改保留/读/删限制，理由与审计必需；不提供完整法律审批流程 |
| POST /notifications/announcements；PUT /notifications/announcements/{nId} | 草稿创建/编辑，发布后正文不可原地改写 |
| POST /notifications/announcements/{nId}/publish 或 /revoke | 原子固化/撤回与幂等请求键；排期由模块作业执行 |
| POST /notifications/system-messages | 受信服务输入；服务身份未稳定时只开放经验证的进程内契约 |
| GET /notifications/inbox、GET /notifications/unread-count | 用户/租户受限、统一有效性过滤、当前状态查询 |
| PUT /notifications/inbox/{nId}/read；POST /notifications/inbox/read-batch | 首次已读幂等，批量数量受控 |
| POST /audits/facts:ingest | 受信应用契约/服务输入，唯一键+摘要幂等与冲突 |
| GET /audits/facts、GET /audits/facts/{nId} | 授权/脱敏/限窗查询并记录必要访问 |
| GET /audits/exports | Core 有界流式导出与访问审计；超限拒绝，不暗中创建高级导出作业 |

File 的进度权威由授权 HEAD 获取，业务查询可附观察时间避免冒充实时断点。证明和会话操作用平台响应信封；tus HEAD/PATCH 保留协议头/状态码，不把二进制强包 ApiResult。敏感值、凭据、hash 请求载荷与上传授权不进 URL query/普通日志。无权限查询采用一致拒绝行为；普通错误带稳定 code/TraceId，异步任务带 OperationId。

Advanced 的 integrity-verifications、legal-holds 审批、decrypt、复杂 exports API 当前不注册、不生成权限菜单。

## 9.2 事件与权限

事件 Envelope 复用平台 EventNId/类型版本/OccurredOn/Producer/TenantNId/CorrelationNId/TraceId 约定；生产者已有命名风格通过兼容适配映射，不批量改名。

- File：FileAvailableV1、FileRejectedV1、FileDeletedV1；上传完成不是可用事件。
- Notification：NotificationPublishedV1、InboxDeliveryCreatedV1、NotificationReadV1、NotificationRevokedV1；过期仍以服务端时间查询为准。
- Audit：AuditFactV1 输入，必要的接收/失败反馈按调用契约返回；不向所有业务广播每条审计，也不引入审计自身无限循环。完整性链事件仅属 Advanced。

权限前缀：`systemdata.file.upload/read/download/manage/delete`、`systemdata.notification.inbox.read/announcement.read/manage/publish/system.send`、`systemdata.audit.write/read/export/retention.manage`。接管还须同上传者/用途资源权限，manage 不默认允许跨用户续传。scan/integrity/decrypt/legal-hold 审批权限不作为当前通用必选；访问限制管理属于 file.manage 的高风险子操作并在目录登记。服务端每个动作独立授权，前端隐藏不构成权限。

## 9.3 组件采用边界

tus 候选与证据见 7.2.3。Audit.NET 仅作采集/Provider 抽象参考，未确认集成、版本和许可前不引入；不能证明 SqlSugar 原子事务。CAP 仅在现有可靠消息缺口无法增量补齐时重新评估，本阶段不叠加。Notification 不整体引入通知编排平台。未验证的组件不写成已经安装或稳定依赖。

# 10. 页面与交互设计

- 三端标准上传：文件选择、内容确认进度、候选会话、接管提示、服务端确认进度、暂停/重试/取消、到期和旧设备失效、校验/扫描状态；明示上传完成尚不可用。本地文件不可读时要求重新选择，不伪造自动恢复。
- PC File 管理：用途/上传者/处理/扫描/访问限制筛选、元数据、引用摘要、受控限制与删除申请；没有复杂复扫/法律审批入口。
- PC 公告管理：草稿、受众范围/数量上限、发布时间、发布/撤回；个人通知中心三端共用已读/未读/失效/安全跳转，重连刷新旧通知。
- PC Audit Core：授权条件查询、脱敏详情、有界导出和必要保留配置；不出现解密、哈希验证、checkpoint、Legal Hold 审批页面。PDA/Mobile 不提供审计管理。
- 管理页复用用户管理黄金页、现有管理组件及权限路由；遵循既有主题/中英语言、键盘/ARIA、触控/焦点与错误关联，状态不用单一颜色表达。大量上传字节不进入前端状态持久化。

# 11. 错误、安全、审计与可观测性

当前错误包括 FILE_UPLOAD_PROOF_REQUIRED、FILE_UPLOAD_CONTENT_MISMATCH、FILE_UPLOAD_WRITER_REPLACED、FILE_UPLOAD_EXPIRED、FILE_UPLOAD_CANCELLED、FILE_SCAN_PENDING、FILE_MALICIOUS、FILE_REFERENCE_EXISTS、NOTIFICATION_AUDIENCE_LIMIT、NOTIFICATION_EXPIRED、NOTIFICATION_TARGET_INVALID、AUDIT_FACT_CONFLICT、AUDIT_WRITE_UNAVAILABLE、AUDIT_QUERY_SCOPE_INVALID。数据库就绪错误沿用平台，不再维护 PF04_DB_* 平行集合。

安全边界：防路径穿越/双扩展名/MIME 欺骗/超限/解析炸弹；默认不引入预览转换，若引入须沙箱。HTML 和表格导出安全处理，租户/用户/服务/资源权限分层校验。存储/扫描凭据由配置 Secret 管理，不写入业务 DTO 或日志。

观测按能力区分：上传确认/断点/接管冲突/过期量、校验扫描延迟与隔离积压、投递延迟、未读查询、Outbox 年龄/永久失败、失败审计保存失败、清理/导出失败。指标不用高基数 NId 标签；TraceId/OperationId 贯穿可靠处理。必要审计无法落库的高风险门禁不能被“局部降级”绕过。

# 12. 自动化测试与验收设计

## 12.1 接入门禁

001/008 引用蓝图 29 与 33 第 12 章的适用门禁，不另设“四 Unit 数据库十项门禁”。覆盖现有 SQLite 测试、PostgreSQL 18 空库/升级、本地 ledger/必要种子、目标身份、幂等初始化、权限与服务前缀；Shared/PerService 仅按已确认环境使用。架构测试验证模块所有权、无跨模块 Repository/外键和无独立 Host。tus 需在 UnifiedHost 与 Gateway→SystemData 两种入口验证协议，不推断某入口可用即另一入口可用。

## 12.2 必须进入任务的场景

| 编号 | 验收场景与预期 | 责任任务 |
| --- | --- | --- |
| F1 | 手机上传中断，电脑重新选同二进制文件，完整 hash 比对后从持久断点接续；大文件 hash 内存/时延可接受 | 003、006、008 |
| F2 | 服务重启、在途请求中断、底层已写但业务库未更新，按 store 恢复且未确认尾部可重传 | 003、008 |
| F3 | 同名不同内容、采样相同而未采样区不同、大小错误、无预期 hash 均拒绝接续/明确重传 | 003、008 |
| F4 | 跨租户/跨用户/无用途权限不能发现或接管；两设备和在途 PATCH 与接管竞态，旧 epoch 不再写入 | 003、005、008 |
| F5 | 暂停恢复、取消/过期与 PATCH/complete 竞态、重复完成、处理期间重启不重复文件/可用事件 | 003、009、008 |
| F6 | hash/大小/类型校验失败；扫描 Clean/Malicious/Error/Unknown/timeout；受保护文件禁下载 | 009、008 |
| F7 | 下载权限撤销/Range、引用新增与删除竞态、禁止删除/保留、过期对象清理重启可恢复 | 009、007、008 |
| N1 | 离线通知持久存在，重复发布/消费不重复生成 Delivery，受众超限拒绝，规则发布后不漂移 | 004、008 |
| N2 | 电脑已读后手机重连同步旧记录；撤回/过期与未读数、列表一致，过期作业延迟也正确 | 004、006、008 |
| N3 | SignalR 丢失/重复/故障不改变持久事实，重连/前台/定期刷新收敛 | 004、006、008 |
| N4 | XSS/非法跳转/已撤销资源权限被拒绝，通知已读不能替代业务办理完成 | 004、005、008 |
| A1 | 成功关键业务与本地 Audit Outbox 原子提交，Outbox 故障必须回滚业务 | 002、005、008 |
| A2 | 事务回滚、权限拒绝后失败事实独立可靠保存；保存失败可见并按风险关闭必要操作 | 002、008 |
| A3 | 中央暂不可用后补投、重复事件与同键不同内容冲突、永久失败及恢复 | 002、007、008 |
| A4 | 跨租户访问拒绝、脱敏/敏感字段扫描、授权查询/导出限制、访问审计保存失败不放行 | 002、005、008 |
| A5 | lifecycle 变化不改原事实、清理不越过保留/禁止删除，Core 验收不依赖 Advanced | 002、007、008 |
| U1 | 三端/亮暗/中英/键盘/触控/错误与空态；真实身份下两端上传和收件箱交互 | 006、008 |

## 12.3 证据要求

Core 离线验收已按上述矩阵执行并记录命令、退出码、通过/失败/跳过数和真实依赖限制。Mock/fixture、浏览器设备模拟、真实手机分别标注；没有真实扫描/存储/服务身份或 Gateway 链路只能对应标待验收，不能用 Mock 证明完成。最终源码发生变更时先新鲜 Release build，再 `dotnet test ... --configuration Release --no-build`；不拿旧编译产物或历史 evidence 冒充当轮通过。

# 13. 开发任务依赖

```text
现有宿主/必要契约核验
  → 001 宿主、契约与迁移接入
    → 002 Audit Core（契约随实现固定）
      → 003 File 会话/传输/跨设备续传 → 009 File 安全生命周期
      → 004 Notification 持久投递/跨端同步
    002 + 003 + 009 + 004 → 005 联合契约/权限/生产者接入
      → 006 三端组件和页面
      → 007 安全、恢复作业与观测
    005 + 006 + 007 → 008 阶段集成验收

010 Audit Advanced：另行范围确认；依赖 002，不是 003～009 的前置。
```

这是同一 PF 的内部执行序列；001 先固定接入约定，002/003/004/009 先提供对应契约再实现，005 不承担开发结束后才首次定义 API。独立分支显示逻辑依赖，不代表本轮派遣或允许共享文件并行编辑。

| 旧 ID | 本轮对应与状态解释 |
| --- | --- |
| TASK-PF04-001 | 保留 ID，四个独立迁移单元改为现有服务级接入，不重新实施 PF-02 |
| TASK-PF04-002 | 保留为 Audit Core；完整性/高级合规移至新增 010 |
| TASK-PF04-003 | 保留为上传协议/会话/跨设备续传；安全/下载/引用/清理拆至新增 009 |
| TASK-PF04-004 | 保留持久通知，减少独立聚合并补旧通知跨端同步 |
| TASK-PF04-005 | 保留联合接入/契约验证；基础契约前移至 001 和模块任务 |
| TASK-PF04-006 | 保留三端页面，增加标准跨设备上传，去除 Advanced 菜单 |
| TASK-PF04-007 | 保留 Core 安全/恢复/观测，去除锚定/完整法律审批依赖 |
| TASK-PF04-008 | 保留阶段验收，按第 12 章 Core 矩阵收束 |
| TASK-PF04-009 / 010 | 新增的 003 安全拆分已完成并通过离线验收；002 高级后续范围未实现 |

原八项在 2026-09-06 复评时没有开发完成证据，历史状态与本次结果分开记录。TASK-PF04-001～009 随后作为一个 Core 工作包按依赖连续开发，并于 2026-09-07 通过独立离线验收；010 保持“待细化（后续增强）”。不改变任何已完成的 PF-00/01/02/03 工作。

# 14. 开发任务拆分

以下每项九字段；001～009 已纳入本轮 Core 开发授权，具体共享文件边界以 `docs/tasks/active/PF-04.md` 为准。所有任务回写本文第 16/17 章，并统一使用 `docs/evidence/PF-04.md`；010 不在本轮授权内。

## TASK-PF04-001 宿主、基础契约与迁移接入

**状态：** 开发与独立离线验收通过（首个内部步骤，接入核验已回写）

**目标：** 在现有 SystemData 服务级生命周期接入三个逻辑模块，冻结基础身份/初始化/路由约定。

**输入文档：** 本文 1～6、8.1、9、12.1；蓝图 32 第 2～4 章、33 第 3～8/12 章；实施 05/evidence 的对应现状。

**依赖：** 总控给定无共享文件冲突的工作基线；本任务启动时核验所消费的 PF-00/01/02 契约并记录交付标识，不重做 TASK-SD-001～004。

**允许修改范围：** 后续获准的 SystemData Contracts/组合根/迁移接入与 SystemData/UnifiedHost/Gateway 契约测试；不改 PF-02 控制面规则或新增 Host。

**预期输出：** 初始化/表前缀/权限声明映射、迁移增量、共用可靠设施复用清单及缺口、服务身份边界；逐项明确可复用与待验收。

**验证与证据：** 第 12.1 节适用初始化/架构/入口检查；记录路径、版本、命令及依赖缺口，不能只看代码存在。

**结果回写：** 第 1.2、5、8、9、16/17 章的实际接口、Unit/表/账本和阻塞。

**提交策略：** PF 整体交付，总控按仓库门禁统一提交，执行者默认不提交。

## TASK-PF04-002 Audit Core 与可靠记录

**状态：** 开发与独立离线验收通过

**目标：** 实现事实/生命周期分离、成功与失败可靠路径、幂等冲突、脱敏查询和 Core 导出。

**输入文档：** 本文 4.2、6、7.1、8～9、11～12；蓝图 30 第 8 章。

**依赖：** TASK-PF04-001；可信服务身份/进程内调用及本地事务能力核验。

**允许修改范围：** SystemData 各层 Audit 子目录及服务内可靠设施最小适配、所属迁移/测试；不接管其他服务审计表，不实现 Advanced。

**预期输出：** AuditFactV1、接收幂等/隔离恢复、失败独立落库契约、动作目录、追加事实/lifecycle、授权查询/有界导出。

**验证与证据：** A1～A5，明确独立失败事务、同键异载荷与审计保存失败门禁；历史 Outbox 不直接作为原子性证明。

**结果回写：** 第 7.1、8、9、16/17 章的字段/摘要规范/政策/API 与证据。

**提交策略：** PF 整体交付，总控统一提交，执行者默认不提交。

## TASK-PF04-003 File 上传会话与跨设备续传

**状态：** 开发与独立离线验收通过（真实 tus/store、多实例链路仍受环境限制）

**目标：** 在唯一传输路线实现持久会话、强证明、可靠断点和接管旧写入者失效。

**输入文档：** 本文 7.2.1～7.2.3、8～9、12 的 F1～F5；官方组件/许可证链接。

**依赖：** TASK-PF04-001、TASK-PF04-002；本任务内先核验并锁定 tus/store 版本、许可和部署边界，不同时做 tus 和 Multipart。

**允许修改范围：** SystemData File 契约/会话/传输适配和所属迁移/测试；依赖及入口配置仅后续明确派遣范围内，不能修改用户运行环境。

**预期输出：** 采样固定向量、完整 hash 路径、会话 API、底层进度映射、接管互斥/epoch、重启恢复与幂等完成。

**验证与证据：** F1～F5，实际持久 store 重启、在途旧请求竞争、两种入口及移动端 hash 容量；记录版本/许可/失败假设，无条件则待验收。

**结果回写：** 第 7.2、8、9、16/17 章的主路线、协议头/Location、限制和验收报告。

**提交策略：** PF 整体交付，总控统一提交，执行者默认不提交。

## TASK-PF04-004 Notification 持久投递与跨端同步

**状态：** 开发与独立离线验收通过

**目标：** 以三个核心模型交付公告/系统通知/收件箱与旧通知状态刷新。

**输入文档：** 本文 7.3、8～12；现有 Identity/组织公开查询契约。

**依赖：** TASK-PF04-001、TASK-PF04-002；可信接收人来源，不依赖 File。

**允许修改范围：** SystemData Notification 模块、SignalR 适配、所属迁移/测试；不新增聊天、复杂通知编排或通用受众作业平台。

**预期输出：** 固化受众、幂等 Delivery、已读/撤回/过期/未读一致性、重连/提示/轮询刷新契约与安全跳转。

**验证与证据：** N1～N4，受众上限、已读首次时间、旧记录同步、提示丢失/重复与 XSS；记录真实依赖与 fixture 区别。

**结果回写：** 第 7.3、9、16/17 章的容量上限、消息/Hub/查询与刷新规则。

**提交策略：** PF 整体交付，总控统一提交，执行者默认不提交。

## TASK-PF04-005 联合契约、权限及现有生产者接入

**状态：** 开发与独立离线验收通过

**目标：** 验证前置已定义的契约与两种宿主入口一致，并增量接入现有审计生产者。

**输入文档：** 本文 3、5、9、12；001/002/003/004/009 输出。

**依赖：** TASK-PF04-002、TASK-PF04-003、TASK-PF04-004、TASK-PF04-009。

**允许修改范围：** PF-04 Contracts/宿主组合/权限注册与契约测试；Identity/SystemData/ReferenceData 生产者仅总控明确批准的公开适配文件，不改领域行为或历史表。

**预期输出：** v1 契约快照、可信租户/身份链、现有审计保留兼容方案；至少 Identity/SystemData/另一平台服务代表动作的可靠接入。

**验证与证据：** A1/A4、F4、N4、两入口协议/错误/权限负例和无跨表访问；验证生产者本地事务，不伪造跨库原子性。

**结果回写：** 第 3、9、16/17 章实际路由、版本、兼容/历史映射与缺口。

**提交策略：** PF 整体交付，总控统一提交，执行者默认不提交。

## TASK-PF04-006 三端上传组件、收件箱与管理页

**状态：** 开发与独立离线验收通过（真实浏览器/三端链路仍受环境限制）

**目标：** 交付标准跨设备上传和通知交互，以及 PC 必要 File/公告/Audit Core 页面。

**输入文档：** 本文 9～12；PF-01 稳定外壳与用户管理黄金页规范。

**依赖：** TASK-PF04-005；001 已核验前端/身份契约。

**允许修改范围：** PF-04 前端 api/pages/components/routes/locales/tests；共享文件仅批准最小接入，不重做 AppDataTable 或平台壳。

**预期输出：** 三端内容确认/续传/接管/失败恢复、通知列表刷新、权限路由/空态/错误及 Core 管理 UI。

**验证与证据：** F1、N2/N3、U1；单元/组件/E2E、亮暗/中英/键盘/触控、两设备场景；真实手机与模拟设备证据分开。

**结果回写：** 第 10、16/17 章路由、组件、截图/报告及终端限制。

**提交策略：** PF 整体交付，总控统一提交，执行者默认不提交。

## TASK-PF04-007 Core 作业恢复、安全与可观测性

**状态：** 开发与独立离线验收通过

**目标：** 完成过期/清理/补投/扫描重试、容量保护与局部降级。

**输入文档：** 本文 4.1、7～8、11～12；模块已实现输出。

**依赖：** TASK-PF04-005。

**允许修改范围：** PF-04 模块托管作业/策略/指标及测试；不创建 Scheduler 平台、不静默改变 Identity/宿主既有强制健康门禁。

**预期输出：** 可恢复 Core 作业、受控保留/禁止删除保护、告警与恢复步骤；Advanced 故障不引入整宿主依赖。

**验证与证据：** F7、A3/A5、扫描器与 SignalR 故障局部影响，清理中断重启、积压门禁与日志 Secret 扫描。

**结果回写：** 第 11、12、16/17 章阈值、恢复步骤、实际 health 限制和演练证据。

**提交策略：** PF 整体交付，总控统一提交，执行者默认不提交。

## TASK-PF04-008 集成、安全与阶段验收

**状态：** 开发与独立离线验收通过（真实服务与三端链路仍受环境限制）

**目标：** 以真实接入证明 Core 三模块、跨设备用户路径及安全/恢复边界。

**输入文档：** 本文第 12/15 章；001～007 和 009 输出；现有服务稳定交付标识。

**依赖：** TASK-PF04-005、TASK-PF04-006、TASK-PF04-007（传递覆盖 001～004、009）；不依赖 010。

**允许修改范围：** PF-04 验收测试/fixture/报告与执行记录；发现生产缺陷回原模块范围修复，不扩张产品能力。

**预期输出：** F1～F7/N1～N4/A1～A5/U1、适用初始化与两种入口的分层证据；未验证项明确待验收。

**验证与证据：** 新鲜 Release build 后测试，前端检查与真实 E2E/外部故障恢复；逐项记录命令/退出码/通过失败跳过/环境，不用历史 PASS。

**结果回写：** 第 15～17 章、总 Todo 与 CURRENT 的实际完成范围和风险；功能未通过不写已完成。

**提交策略：** PF 整体完成后总控按当前仓库门禁统一提交；本轮文档整改不触发提交。

## TASK-PF04-009 File 校验、下载、引用与清理

**状态：** 开发与独立离线验收通过（真实扫描/存储链仍受环境限制）

**目标：** 将完成的上传安全处理为可用文件，并实现授权使用/引用/保留删除。

**输入文档：** 本文 7.2.4、8～12；003 会话/完成契约；蓝图 05 第 8.1 节既有用途政策。

**依赖：** TASK-PF04-002、TASK-PF04-003；继承已有服务端安全政策，在本任务内确定最小扫描/存储适配并验证；真实依赖缺失列对应验收限制。

**允许修改范围：** SystemData File 处理/扫描/存储/授权/引用子范围及迁移/测试，不改其他模块业务或完整法律审批流程。

**预期输出：** 分离的处理/扫描/保留状态、最终 hash 对比、受控代理下载、幂等引用、延迟删除和恢复。

**验证与证据：** F5～F7，扫描未知/不可用隔离、恶意内容安全 fixture、权限撤销/Range、引用竞态和保全保护；无真实扫描则该项待验收。

**结果回写：** 第 7.2、8～9、16/17 章实际策略/产品版本/限制及测试证据。

**提交策略：** PF 整体交付，总控统一提交，执行者默认不提交。

## TASK-PF04-010 Audit Advanced 后续增强

**状态：** 待细化（后续增强；不纳入当前阶段验收）

**目标：** 仅在明确合规需求成立时提供完整性链、锚点、法律审批、解密或复杂证据导出。

**输入文档：** 本文 2、7.1、9、15 章；需求方确认的合规来源/适用范围与 Audit Core 稳定输出。

**依赖：** TASK-PF04-002；后续单独范围决策。涉及 File 异步导出时再声明其稳定契约，不能反向阻塞 Core。

**允许修改范围：** 本轮无实现范围；后续由总控按选定增强项精确定界，不一次包揽所有能力。

**预期输出：** 需求触发条件、成本/运维/许可与生命周期兼容方案、对应 API/表/权限/页面及专项验收；无需求保持后置。

**验证与证据：** 仅对选入范围执行篡改/签名/锚定/保全/解密授权或证据验证，不污染 Core 必选矩阵。

**结果回写：** 第 2、7.1、9、16/17 章与总 Todo 的后续增强范围，保留 002/007 的旧 ID 来源。

**提交策略：** 不随当前 PF-04 Core 提交；后续另行工作包授权与验收。

# 15. 完成标准

本文整改和任务派遣不等于下列功能标准已经通过。PF-04 Core 的完成条件：

- 现有宿主、服务级初始化/表命名空间及必要契约接入正确；没有重建 PF-02 或扩展 Provider/部署矩阵。
- Audit Core 成功原子写入、回滚后失败独立记录、幂等冲突/补投、脱敏授权、基础保留/必要导出符合 A1～A5。
- File 完成手机→电脑强确认续传、服务端持久断点、旧设备失效、最终预期 hash 对比、扫描隔离、下载/引用/清理 F1～F7。
- Notification 数据库为事实源，离线/重复投递/旧通知已读撤回过期同步与权限跳转通过 N1～N4。
- 三端 U1、两种入口、适用数据库/真实依赖与故障恢复有分层证据；跳过和 Mock 不充当真实通过。
- Advanced、双上传栈、全局去重/秒传及通知编排不作为当前必选；已有明确保留/扫描/法律保全要求仍受保护。
- 对外契约与已实现/已验收范围明确，未完成外部项保持待验收；后续实际开发派遣须明确授权。

# 16. 执行记录

## 16.1 历史快照（不作为当前事实）

2026-08-13 原记录：已完成仓库/蓝图盘点、详细设计和用户书面确认；用户允许后续进入开发，但明确要求当时会话不做开发。未派遣任务，未修改生产/测试代码，未执行构建或测试，未提交 Git；并行工作树改动保持原状。

原首次盘点 `develop@4180d71`，当时记录与 origin 无领先落后，PF-02 控制面、PF-01 与 PF-03 状态为当时输入；原 TASK-PF04-001～008 全为“待派遣”。原四 MigrationUnit、哈希链/Legal Hold 首期门禁等设计已由本版替代，不能继续作为当前前置。历史全文可从整改前 Git 版本追溯。

## 16.2 首次文档整改快照（2026-09-06；任务状态已由 16.3 更新）

| 范围 | 状态 | 执行者/任务 | 提交 | 验证证据 | 结果回写 |
| --- | --- | --- | --- | --- | --- |
| PF-04 蓝图与开发 TODO | 文档整改完成，Core 开发中 | 总控 `01a0759d-fc1c-7452-8a78-1031774dbb45` | 未提交 | 文档结构、链接、依赖和 diff 检查已通过；功能证据等待开发与独立验收 | 本文、相关蓝图、索引、CURRENT 与 `docs/tasks/active/PF-04.md` |
| TASK-PF04-001～009 | 已整包派遣，开发中；尚未验收 | 开发 `01a076d2-a8d3-7163-8721-8cae51393d92`；验收 `01a076d3-3abf-7921-a367-9b70749d0780` | 未提交 | 开发按第 12～14 章验证，独立验收等待稳定交接 | 第 14 章及后续 `docs/evidence/PF-04.md` |
| TASK-PF04-010 | 后续增强，未派遣 | - | - | 无功能证据，不阻塞 Core | 第 2/14 章 |

## 16.3 开发就绪复评（2026-09-06）

用户要求重新评估状态并为后续开发做准备。复核结论：Core 无需继续普遍细化设计，原“待细化”中多数事项实际属于任务内验证和最终验收，现将其分类并明确执行责任。

| 范围 | 当前状态 | 开发安排 | 证据/限制 |
| --- | --- | --- | --- |
| TASK-PF04-001～009 | 已派遣，开发中；未验收 | 一个 Core 工作包按依赖连续执行；001 起步，003 内先技术验证 | 已落实工作树、模型、负责人和验收对端；功能测试结果等待回写 |
| TASK-PF04-010 | 待细化（后续增强） | 当前 Core 不执行 | 需具体合规需求，保持非阻塞 |

总 Todo、实施/蓝图索引、CURRENT 和实施前门禁同步此状态。首次整改历史保留；未创建执行任务、工作树或开发工作包，未修改代码、未提交 Git。

## 16.4 Core 开发与独立离线验收回写（2026-09-07）

本轮在 `develop` 主工作树完成 Core 001～009 的实现、前端收口和独立离线验收，结论为 PASS；未提交、未推送、未启动或重启服务。当前 `HEAD=4eefeed044f9fcda6d67274b2a3c0908fa68a261`；未提交改动覆盖 PF-04 后端契约/API/服务/存储、前端 API 与 Shell/PDA/Mobile/PC 页面、路由、语言包及对应测试。

| 层级 | 新鲜结果 | 结论边界 |
| --- | --- | --- |
| 后端 | `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release`：0 warning、0 error；随后 `dotnet test ... --configuration Release --no-build`：1,684 通过、0 失败、3 跳过、总计 1,687 | 跳过项是 PostgreSQL/Redis/RabbitMQ 外部依赖条件；不等同真实服务链通过 |
| 前端定向 | Shell/PDA/navigation/notificationHub 与 PF-04 页面组件回归通过 | 覆盖通知权限、未读/批量已读、安全跳转、PDA 路由、服务端分页、确认动作、消息收件人和幂等键 |
| 前端全量 | `vitest run --configLoader native`：124 文件、926 测试通过；`vue-tsc --noEmit --pretty false` 通过 | 使用本轮工作树 |
| 前端构建 | `cmd.exe /d /c pnpm.cmd build` 通过，Vite 2,330 modules；保留既有大 chunk warning | 构建产物为验证输出，不纳入提交 |
| 差异卫生 | `git diff --check` 通过 | 仅有 Git 报告的 LF/CRLF 转换提示 |

仍待真实环境复验的路径：真实浏览器登录/权限/动态菜单、UnifiedHost/Gateway、PostgreSQL/Redis/RabbitMQ/ClamAV、多实例文件锁、真实手机与跨设备上传。Core 代码契约已明确：公告列表为 page/pageSize/total 服务端分页，FileObjectV1 返回用途/上传者/引用数量/摘要并支持服务端筛选。

# 17. 下一阶段输入契约与待核验事项

以下未验证项是开发任务内的工作或功能验收条件，不是 001～009 继续保持“待细化”的理由；只有实际发现无法满足契约的证据才标记对应路径阻塞。

以下 Core 契约已经实现并通过独立离线验收，可作为后续开发输入：AuditFactV1 可靠写入/授权查询；FileMetadataV1、上传会话/发现/证明/接管/完成、受权内容与引用；Notification 发布/收件箱/未读/刷新；服务级初始化增量与模块权限。消费者只保存 FileNId 等外部标识，不依赖 Repository、存储键、分页游标的隐藏同步语义或 Advanced 能力；涉及真实基础设施和终端联动的运行保证仍以完成外部环境复验为准。

| 待核验事项 | 责任/关闭条件 |
| --- | --- |
| PF-02 所用契约/当前 WIP、服务身份/权限注册 | 001/005：稳定交付标识、公开适配与对应真实验证；不因本轮文档刷新判整包完成 |
| 现有 Outbox 的原子事务、冲突/死信与失败独立路径 | 002：A1～A3 的实际故障证据，缺口最小补齐，默认不引入 CAP |
| tus 版本/第三方 store 许可、.NET 10、双入口、多实例锁/持久卷 | 003：固定版本及 F1～F5；不能验证时收紧支持范围并明确限制，未经决策不另建上传栈 |
| 大文件全量 hash 的终端耗时/内存 | 003/006：PC/实际 PDA/Mobile 文件规模证据；无 hash 的旧会话不冒险续写 |
| 扫描引擎/用途政策/存储故障与受保护下载 | 009：实际产品与 F6/F7；扫描不可用保持隔离 |
| 合规强制项 | 后续总控：需求来源/阶段/作用域明确；保留蓝图 05 Collaboration 已定要求，高级审批后置不豁免保全 |

当前实现与验收交接的具体缺口：

| 项目 | 当前事实 | 处理 |
| --- | --- | --- |
| 公告管理列表 | `GET /notifications/announcements` 返回 `NotificationAnnouncementPageV1`，服务端执行 `search/page/pageSize/total` 查询 | 页面通过 AppDataTable loader 消费服务端分页；保持 200 条单页上限 |
| 文件管理字段 | `FileObjectV1` 返回用途、上传者、活动引用数量/摘要；文件列表支持用途、上传人、扫描状态、限制状态服务端筛选 | 页面展示真实返回字段；引用摘要只含业务 NId、授权人、用途和创建时间 |
| 真实环境 | 本机当前无 Docker/Podman 与 3310/5432/5672/6379 监听 | 相关 F/N/A/U 真实链路保持待验收，见 `docs/evidence/PF-04.md` |

PF-05/10A 可消费基础 File/Audit/Notification 契约，但各自的业务状态、聊天、知识生命周期及客户合规专项仍自行设计。PF-05 的已定法律保全边界应在启用前落实；不能把 Core 完成解释成完整合规平台已完成。

# 18. 文档自审清单

- [x] 当前状态按代码/Git/历史证据分层，旧 HEAD 和批准记录仅作历史。
- [x] 一个宿主、三个逻辑模块；默认服务级迁移/可靠设施复用，控制面规范引用蓝图 33。
- [x] 完整 hash 与采样/授权/会话/FileNId 分离；明确无预期 hash 拒绝续传。
- [x] 权威进度、持久恢复、接管在途竞争、幂等完成及扫描/保留状态均有任务和验收。
- [x] 持久收件箱、旧记录跨端刷新、受众容量与当前资源权限一致。
- [x] Audit 成功/失败路径及事实/lifecycle 分离；Advanced 未混入 Core 表/API/页面/依赖/验收。
- [x] 原八任务保留 ID，对应新增 009/010；任务具备九字段与阶段整体提交策略。
- [x] 既有扫描、保留及法律保全要求未静默降低，未知外部依赖有关闭责任。
- [x] 本轮 Core 开发、自测、独立离线验收、证据与契约缺口已回写；未提交 Git，真实环境项保留待复验。
