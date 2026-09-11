# Industrial Platform Service Host 与内部模块边界

版本：V1.4
状态：已确认，平台微服务母版
生效日期：2026-09-07

---

# 1. 文档定位与优先级

本文固定 PF-02～PF-11 共用的当前部署宿主、内部模块边界和未来拆分规则，是平台基础层 Service Host 的权威母版。各阶段管理任务必须同时读取本文、`33-Industrial Platform SystemData数据库编排与环境引导.md`、`09-Industrial Platform开发总TodoList.md`、`TEMPLATE-开发实施方案.md`、项目记忆和当前代码。

旧蓝图中按领域列出的 `File Service`、`Audit Service`、`Dashboard Service`、`Server Monitor Service` 等名称，保留为领域设计或未来物理拆分目标；凡与本文的当前部署宿主清单冲突，以本文为准。

# 2. 核心原则

- `Service Host`、`Domain Module`、`Initialization Unit`、`Deployment Unit` 是四个不同概念：宿主决定进程组合，领域模块决定业务所有权，初始化单元决定持久化生命周期，部署单元决定运行和扩缩容边界，四者不得互相推导。
- 当前单租户必须完整可用；所有领域边界预留可信身份上下文提供的 `TenantNId`。
- 阶段不等于微服务。合并只表示共用部署宿主，不表示合并领域模型或数据所有权。
- 同一 Service Host 内的模块必须独立建模，使用明确的逻辑表命名空间（独立 Schema 或模块表前缀）、公开契约、权限资源和测试边界；默认按服务持久化生命周期共享物理 Schema、迁移流、连接和技术基础设施。模块只有具备独立持久化生命周期时才升级为独立初始化单元，不因逻辑模块数量机械拆分迁移、Outbox、Inbox 或基础设施。
- 禁止跨模块直读或写入其他模块的 Repository 或数据表；同宿主协作优先使用进程内公开 Application 契约，跨宿主才使用 API 或事件，禁止为了模块边界引入内部 HTTP 或内部消息总线。
- 模块间不建立数据库级跨模块外键。跨模块只保存稳定业务标识和必要快照，并按契约维护一致性。
- 每个模块都必须能够在不改变外部语义的前提下迁移到独立进程和数据库，为未来物理拆分保留边界。
- SystemData 只负责数据库拓扑、初始化编排、执行策略和脱敏 Observation，即 `Where + When + Policy + Observation`；每个服务负责自己的 Migration、Seed、Bootstrap、Verify 和 Ledger，即 `What + How + Fact`。SystemData 调用服务初始化器，不拥有或执行其他服务的领域迁移实现。
- 服务日常启动与 runtime readiness 只依赖本服务核心数据库身份、本地 ledger 和必需 bootstrap 事实；SystemData 是否在线不改变已经初始化服务的本地 Ready 结论。Redis、消息代理和日志/可观测性后端默认属于 capability health，故障时报告 `Degraded` 并按既定降级策略运行，不机械把整个宿主判为 `NotReady`；只有公开能力契约明确要求该依赖不可替代时才可成为 readiness 门禁。
- 初始化策略分为 `Standard` 与 `Advanced`。普通功能默认采用 Standard；审批、备份证据、签名和漂移恢复只在环境或风险要求时进入 Advanced。

## 2.1 平台原生优先与相对独立交付

PF-05、PF-06、PF-08、PF-09、PF-10、PF-10A、PF-10B、PF-11都属于Industrial Platform自身能力，首先满足平台兼容和原生集成，再将同一业务核心按需独立部署并接入其他MES。外部适配边界在设计时一起明确；实现先打通平台纵向业务链，再验外部宿主和无宿主场景，不能先复制一套独立产品、最后返接平台。

“相对独立”要求明确业务/数据所有权、公开接口、最小依赖、可选择的功能集合和交付方式；不代表零依赖、不代表每个PF一个进程，也不要求每个功能自建用户库或支持所有部署组合。当前八个Host及PF排期保持；产品装配清单定义要加载的模块，未选模块不注册页面/后台任务/种子，必要依赖不能假禁用。同一共享初始化单元暂不能安全裁剪时，明确整单元是当前最小安装边界，先设计裁剪/迁移再宣称可单装，不能复制或改写已应用迁移来“拆包”。

| 运行形态 | 身份与公共能力 | 页面与交付 |
| --- | --- | --- |
| 平台原生（默认、首要验收） | 复用平台可信身份/租户、权限、File/Audit、菜单、初始化与可观测公开契约 | 使用平台共享UI、路由/菜单、主题/语言和Runtime；可按当前拓扑统一或独立Host运行 |
| 独立部署 + 外部MES嵌入 | 信任已配置外部身份发行者，适配目录/租户映射和业务上下文；必要公共能力由精简复用部署或等价适配提供 | 使用同一页面/应用核心的嵌入入口或独立入口；明确SSO/退出/导航契约，不强制启动整套Industrial Platform |
| 无外部宿主的独立使用 | 只在该产品明确范围内提供最小登录/管理员引导；复用成熟身份能力，不给每模块新建账号域 | 精简外壳/独立入口；Label已含此范围，其他PF须各自明确，不因“独立”一词自动扩张 |

身份、租户、权限、文件引用/保全、审计、初始化和业务数据源通过消费方公开端口接入。平台装配优先复用现有实现；同进程用公开Application契约，跨进程用API/事件。外部适配允许替换实现，但保留同等授权、数据隔离、幂等和恢复语义；缺少必要能力就阻止该操作并报告，不用空审计、跳扫描或浏览器自报权限冒充兼容。可选通知/定时/助手等功能须显式标注启用依赖与禁用行为。

外部嵌入是完整集成契约，不能只证明iframe能显示：身份须服务端验证issuer/audience/时效并映射稳定租户/主体；页面参数不得作为授权依据；认证失效不能降级成同名本地账号。导航、主题/语言、退出、令牌续期与跨域/CSP/嵌入权限需按目标宿主验证。可选iframe或路由入口，具体方式在阶段设计中确定，不承诺任意MES即插即用。

## 2.2 各功能的最小边界与兼容前置

以下是必须细化的交付边界，不表示安装包或外部适配已实现；精确字段、接口和装配清单由所属阶段在派遣前补齐。

| 阶段 | 平台内优先兼容 | 相对独立边界/最小依赖 |
| --- | --- | --- |
| PF-05 Collaboration | 平台登录/目录、全局连接、聊天入口、文件与审计 | 聊天核心+可信身份/目录+必要持久审计/文件能力；外部宿主适配，不复制人员主数据 |
| PF-06 RemoteAssistance | 现有一对一聊天内屏幕共享与独立语音，分别邀请/结束、可同时使用；复用身份/成员/SignalR/审计 | 首版随Collaboration交付，原生WebRTC＋按需TURN；逻辑模块边界保留，复用服务级初始化/账本。外部MES与脱离聊天另验，不要求全量聊天历史 |
| PF-08 Low Code | 平台权限、主题、路由、受控数据源、发布 | PlatformStudio选定的DataSource/Dataset/LowCode/Publishing；设计器与运行入口范围明确，不依赖Dashboard/Report自动启用 |
| PF-09 Dashboard & Report | 平台受控Dataset、行列授权、File、通知/定时能力 | Dashboard/Report及所需Dataset运行能力；不强制低代码设计器。报告导出、定时报表分别声明存储/调度/通知的必需或可选适配 |
| PF-10 ServerMonitor | 平台节点权限、告警/通知、审计和健康摘要 | ServerMonitor+Agent+指标/告警存储及必要公共适配；不依赖知识库、问题和模型运行时 |
| PF-10A Operations Center | 平台项目/用户范围、文件/知识/问题闭环及受控Dataset | 项目/问题/知识核心与按需助手组合；声明索引、模型和数据源依赖，关闭助手不应阻断基本问题/知识管理；不强制安装ServerMonitor |
| PF-10B Label | 平台身份/权限、菜单/黄金页、File/Audit、SystemData初始化、Runtime及业务来源端口 | Label核心+数据库+必要文件/审计/身份能力+按需Agent/PDA；先平台闭环，再外部MES与无宿主模式；不依赖IoT或MES领域表 |
| PF-11 IoTCollector | 平台设备引用/权限、采集质量/时标、健康与事件契约 | 采集核心+已选驱动/边缘执行与持久缓存；通过API/事件接目标MES，不强制Label、报表或完整MES。PF10B是排期前置，不是运行依赖 |

每阶段派遣输入必须给：①平台装配及真实兼容链；②支持的独立/嵌入形态和明确暂缓项；③必需/可选依赖、缺失行为、最小模块/进程/数据库清单；④身份/数据/公共能力适配和页面入口；⑤同一数据模型/迁移/权限/API的兼容测试；⑥启停、升级、备份恢复与支持矩阵。平台原生链先验，已批准的外部交付范围另验，不能互相替代；阶段排期前置与产品运行依赖分栏记录。

# 3. 当前核心 Service Host

平台基础层规划共八个核心 Service Host：

| Service Host | 当前内部模块 |
| --- | --- |
| `Identity.Service` | Identity |
| `SystemData.Service` | SystemData、File、Notification、Audit、Scheduler、PlatformHealth |
| `ReferenceData.Service` | Dictionary、Parameter、DynamicProperty、Metadata、CodingRule、StateMachine、UnitOfMeasure |
| `Collaboration.Service` | Messaging、Presence、AttachmentIntegration、RemoteAssistance |
| `PlatformStudio.Service` | DataSource、Dataset、LowCode、Dashboard、Report、Publishing |
| `OperationsCenter.Service` | ServerMonitor、ProjectWorkspace、KnowledgeBase、IssueTracking、KnowledgeAssistant、DataAssistant、ModelGateway |
| `Label.Service` | Template、DataPreparation、Rendering、PrintJob、History；PF-10B 新增规划 |
| `IoTCollector.Service` | Driver、DeviceConnection、Point、CollectionTask、EdgeManagement |

Worker、Agent、Screego、TURN 和本地模型运行时是辅助部署单元，不计入八个核心 Service Host。它们不得反向拥有核心领域数据；其生命周期、密钥、网络和升级策略在对应阶段详细设计。

`ReferenceData.Service` 是一个 Service Host，包含 Dictionary、Parameter、DynamicProperty、Metadata、CodingRule、StateMachine、UnitOfMeasure 七个逻辑领域模块。当前固定使用一个逻辑数据库 `referencedata_db`、一个 PostgreSQL Schema `reference_data`、模块表前缀、一个服务级 Migration/Ledger、一个带 `ModuleKey` 的服务级 Outbox 和共享基础设施；没有真实入站事件消费者，不创建 Inbox/Checkpoint。只有某个模块以后形成独立持久化生命周期并完成边界评审，才可成为独立初始化单元；这不改变七个领域模块的契约与数据所有权隔离。

PF-04 于 2026-09-06 完成文档范围收束：File、Notification、Audit Core 加入现有 SystemData.Service，沿用服务级初始化/账本和可靠设施，逻辑数据使用模块表命名空间，不搬迁既有 SystemData 表。旧实施 07 要求“四个必需 MigrationUnit、各自独立 Outbox、控制面在线才能 Ready”的描述已失效。File 核心包含强内容确认的跨设备续传；Notification 包含旧通知状态跨端刷新；Audit Advanced 后置且不阻塞 Core。详细范围与待核验接入见[实施 07](../implementation/07-Industrial%20Platform%20File%20Notification%20Audit开发实施方案.md)，本次未实施模块或更改运行健康规则。

StateMachine 管理 `StateMachineDefinition`、其 `StateNode` 和 `StateTransition` 的版本化定义，只提供定义读取和转换合法性判断；业务服务拥有实例当前状态、权限、业务前置条件、事务和状态历史，不建立通用 `SetStatus`。UnitOfMeasure 管理 `UnitDimension` 的整份 Revision 快照及其 `UnitDefinition` 子项；物料专属包装比例等依赖具体物料的换算仍归 MasterData。

当前对外入口存在两种部署角色且不得混写：

| 项目 | Gateway | UnifiedHost |
| --- | --- | --- |
| 使用模式 | 多进程、未来分布式部署 | 当前统一进程部署 |
| 职责 | YARP、服务前缀、CORS、下游健康聚合、代理错误 | 组合当前模块、统一中间件、协调模块自己的初始化、托管生产 SPA |
| 业务模块 | 不加载 | 加载 Identity、SystemData、ReferenceData |
| 迁移 | 不执行 | 调用模块自己的初始化器 |
| 下游代理 | 执行 | 不执行 YARP 代理 |

正式路径固定为 `Browser → UnifiedHost → 内置模块` 或 `Browser → Gateway → 独立 API Host`。Gateway 不是服务间调用总线，UnifiedHost 不是业务编排器，SystemData 不是业务服务中介。

# 4. 阶段到 Service Host 的正式映射

阶段编号和“一阶段一个管理任务”的工作流保持不变。前一阶段可以创建宿主，后续阶段向同一宿主增加独立模块。

| 文档/阶段 | 阶段名称 | Service Host 动作 | 本阶段宿主内模块范围 |
| --- | --- | --- | --- |
| 03 / PF-00 | Identity | 使用 `Identity.Service` | Identity |
| 05 / PF-02 | SystemData | 创建 `SystemData.Service` | SystemData；包含数据库编排/环境引导能力 |
| 06 / PF-03 | ReferenceData | 使用 `ReferenceData.Service` | Dictionary、Parameter、DynamicProperty、Metadata、CodingRule、StateMachine、UnitOfMeasure |
| 07 / PF-04 | File / Notification / Audit | 加入 `SystemData.Service` | File、Notification、Audit |
| 08 / PF-05 | Collaboration | 创建 `Collaboration.Service` | Messaging、Presence、AttachmentIntegration |
| 09 / PF-06 | RemoteAssistance | 加入 `Collaboration.Service` | RemoteAssistance |
| 09A / PF-06A | 终端运行时与客户端打包 | 不新增 Host；客户端辅助部署单元 | Web/Electron/Capacitor Runtime、设备桥接与更新 |
| 10 / PF-07 | Scheduler / Platform Health | 加入 `SystemData.Service` | Scheduler、PlatformHealth |
| 11 / PF-08 | Low Code | 创建 `PlatformStudio.Service` | DataSource、Dataset、LowCode、Publishing 的首期范围 |
| 12 / PF-09 | Dashboard & Report | 加入 `PlatformStudio.Service` | Dashboard、Report，并复用受控 Dataset 契约 |
| 13 / PF-10 | ServerMonitor | 创建 `OperationsCenter.Service` | 只交付 ServerMonitor；与知识、问题和助手模块保持隔离 |
| 13A / PF-10A | Operations Center Knowledge & Assistant | 加入 `OperationsCenter.Service` | ProjectWorkspace、KnowledgeBase、IssueTracking、KnowledgeAssistant、DataAssistant、ModelGateway；进入实施前先补齐第 5.4 节的设计缺口 |
| 13B / PF-10B | 标签管理平台 | 创建 `Label.Service`，先于 PF-11 | Template、DataPreparation、Rendering、PrintJob、History；先平台原生，再外部MES/无宿主同核心装配 |
| 14 / PF-11 | IoT Collector | 创建 `IoTCollector.Service` | Driver、DeviceConnection、Point、CollectionTask、EdgeManagement |

# 5. Operations Center 母版边界

本章只记录已经逐项确认的模块定位和安全边界，不代表 Operations Center 已完成详细设计。上一个设计会话确认到 DataAssistant 为止，尚未完成 IssueTracking 与 KnowledgeBase 的端到端数据闭环；PF-10A 必须先补齐该闭环并经用户确认，才能生成可派遣任务卡。

## 5.1 ProjectWorkspace 与知识治理

- `ProjectWorkspace` 是客户/工厂实施交付项目的知识与数据作用域，只包含现场、系统版本、成员权限、知识空间、模型配置和受控数据源引用；不做计划、里程碑、预算或工时。
- `KnowledgeBase` 是一等领域模块，覆盖项目文档、系统配置说明、实施手册、FAQ、问题解决方案、附件、分类、标签、版本、发布状态以及全文/向量索引。
- 知识状态固定遵循“草稿 → 审核 → 发布 → 索引”。`IssueTracking` 的解决方案只能由人工转为知识草稿。
- 助手默认只检索当前项目内、当前用户有权访问的已发布知识。
- 首期内容包括富文本、系统配置记录、问题方案、Markdown/TXT、PDF/Word、Excel、图片和附件。图片保存预览与人工说明，OCR 后续；压缩包和 EXE 只作附件、不索引。
- 文件上传复用 `SystemData.Service` 的 File 模块，只保存其公开文件标识，不跨模块直读文件 Repository。

## 5.2 助手与模型

- `KnowledgeAssistant` 使用 RAG，答案展示引用和适用版本；没有可靠知识时必须明确说明，不得编造。
- `ModelGateway` 默认使用本地模型，并兼容本地 DeepSeek、DeepSeek 官方 API 和 Generic OpenAI-Compatible。
- 外部模型按项目启用且默认关闭；密钥加密保存，模型调用可审计。
- `DataAssistant` 第一阶段只查询已注册的受控 Dataset 或只读视图，模型只输出结构化查询计划。
- 第二阶段可按项目开放受限 Text-to-SQL，但必须经过白名单、AST、安全和权限校验。模型永远不得自由访问生产库。
- 查询预览、Excel 导出、行列权限和审计是不可删除的安全边界。

## 5.3 ServerMonitor

`ServerMonitor` 是 `OperationsCenter.Service` 内部独立模块，由 PF-10 单独完成阶段设计、派遣和验收。PF-10 不得顺带实现 ProjectWorkspace、KnowledgeBase、IssueTracking 或助手模块。ServerMonitor 第一阶段不自动创建问题、不自动关联 KnowledgeBase，也不主动介入问题闭环；只预留未来通过公开契约扩展的能力。

## 5.4 尚待 PF-10A 确认的完整闭环

下列内容尚未完成详细设计，不得把第 5.1～5.2 节的原则性结论当作实现规格：

- IssueTracking 从登记、分派、处理、验证到关闭的状态、不变量、权限和审计；
- 问题附件、处理记录、解决方案与 ProjectWorkspace 的数据归属；
- 解决方案人工转为知识草稿时的字段映射、来源追踪、幂等和版本关系；
- KnowledgeBase 草稿、审核、发布、索引、停用、重发与回滚的完整状态机；
- 知识版本、适用系统版本、附件、全文索引和向量索引的一致性及失败恢复；
- IssueTracking、KnowledgeBase、File、KnowledgeAssistant 之间的 API/事件、事务和 Outbox/Inbox 边界；
- 从问题解决到知识发布、再到助手带引用检索的端到端验收场景。

PF-10A 的第一个设计门禁是逐项完成并确认以上闭环；在此之前该阶段状态统一为“设计待确认”。

# 6. 阶段管理工作流

每个 PF 阶段（包括 PF-10A）仍只创建一个阶段管理任务。阶段管理任务负责读取母版、蓝图、项目记忆与当前代码，反复完成详细设计、九字段任务卡、派遣、跟踪和验收，但不直接开发业务代码。

本母版不定义各模块的表、字段、API、事件或页面。对应 PF 阶段必须在其唯一管理任务中完成详细设计，不得从本文摘要推断实现细节，也不得提前创建空的阶段实施方案。

# 7. 数据库编排边界

- `SystemData.Service` 内的数据库编排/环境引导是控制面能力，内部 Worker/Runner 只是辅助执行单元，不增加核心 Service Host 数量。
- SystemData 管理其他服务的登记、拓扑解析、plan、provision 策略、Operation 状态和脱敏 Observation；各服务通过自己的初始化器管理 Migration、Seed、Bootstrap、Verify、Ledger 和 readiness 事实。
- SystemData 自身数据库是唯一 bootstrap 例外，由 PostgreSQL 18 基础设施最小引导创建；不得调用自身 API 创建自身数据库。
- 新服务的 manifest、启动握手、readiness、环境策略、安全和验收统一读取蓝图 33。

## 7.1 PF-05/06 与独立标签产品增量（2026-09-07）

PF05的Messaging/Presence/AttachmentIntegration采用同一服务级初始化、seed ledger、Outbox和必要消费者Inbox；Messaging 拥有 ChatAttachment 绑定，AttachmentIntegration 只适配 File；Presence 无持久化不建空 Schema/账本。2026-09-10用户将RemoteAssistance明确收敛为现有聊天的双人屏幕共享增量：屏幕和新增一对一语音保持必要业务状态，均随Collaboration安装/升级，当前没有独立于该服务的持久化生命周期。因此保留RemoteAssistance逻辑数据/契约/权限/测试边界，表使用collaboration_remote_assistance_前缀，媒体数据迁移追加到Collaboration服务级流，权限种子沿Identity已有目录升级，复用现有账本/Outbox，不再拆独立初始化单元；这项调整依据本次明确范围，未改其他模块生命周期。详见实施09及PF06细化规格。

平台原生与外部 MES 嵌入共用聊天核心，平台原生优先；外部复用可信身份/目录、本地持久审计和本地受控初始化器，不依赖整套平台在线，安全和可靠性不裁剪。Label.Service先兼容平台身份/文件/审计/初始化，再以同一核心支持外部宿主和无宿主的最小装配，默认服务级治理；设备注册/绑定是Label服务的受控配置能力，Agent只消费绑定并拥有本地连接/执行账本；详见蓝图35。Runtime/Agent 只拥有终端技术能力和执行记录，不拥有 MES/Label 领域事实。
