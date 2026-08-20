# Industrial Platform Service Host 与内部模块边界

版本：V1.1
状态：已确认，平台微服务母版
生效日期：2026-08-11

---

# 1. 文档定位与优先级

本文固定 PF-02～PF-11 共用的当前部署宿主、内部模块边界和未来拆分规则，是平台基础层 Service Host 的权威母版。各阶段管理任务必须同时读取本文、`33-Industrial Platform SystemData数据库编排与环境引导.md`、`09-Industrial Platform开发总TodoList.md`、`TEMPLATE-开发实施方案.md`、项目记忆和当前代码。

旧蓝图中按领域列出的 `File Service`、`Audit Service`、`Dashboard Service`、`Server Monitor Service` 等名称，保留为领域设计或未来物理拆分目标；凡与本文的当前部署宿主清单冲突，以本文为准。

# 2. 核心原则

- `Service Host`、`Domain Module`、`Initialization Unit`、`Deployment Unit` 是四个不同概念：宿主决定进程组合，领域模块决定业务所有权，初始化单元决定持久化生命周期，部署单元决定运行和扩缩容边界，四者不得互相推导。
- 当前单租户必须完整可用；所有领域边界预留可信身份上下文提供的 `TenantNId`。
- 阶段不等于微服务。合并只表示共用部署宿主，不表示合并领域模型或数据所有权。
- 同一 Service Host 内的模块必须独立建模，使用独立 Schema 或表前缀、公开契约、权限资源和测试边界；模块只有具备独立持久化生命周期时才升级为独立初始化单元，不因逻辑模块数量机械拆分迁移、Outbox、Inbox 或基础设施。
- 禁止跨模块直读或写入其他模块的 Repository 或数据表；协作使用公开应用契约、API 或事件。
- 模块间不建立数据库级跨模块外键。跨模块只保存稳定业务标识和必要快照，并按契约维护一致性。
- 每个模块都必须能够在不改变外部语义的前提下迁移到独立进程和数据库，为未来物理拆分保留边界。
- SystemData 只负责数据库拓扑、初始化编排、执行策略和脱敏 Observation，即 `Where + When + Policy + Observation`；每个服务负责自己的 Migration、Seed、Bootstrap、Verify 和 Ledger，即 `What + How + Fact`。SystemData 调用服务初始化器，不拥有或执行其他服务的领域迁移实现。
- 服务日常启动与 runtime readiness 只依赖本服务数据库事实和本地 ledger；SystemData 是否在线不改变已经初始化服务的本地 Ready 结论。
- 初始化策略分为 `Standard` 与 `Advanced`。普通功能默认采用 Standard；审批、备份证据、签名和漂移恢复只在环境或风险要求时进入 Advanced。

# 3. 当前核心 Service Host

平台基础层当前共七个核心 Service Host：

| Service Host | 当前内部模块 |
| --- | --- |
| `Identity.Service` | Identity |
| `SystemData.Service` | SystemData、File、Notification、Audit、Scheduler、PlatformHealth |
| `ReferenceData.Service` | Dictionary、Parameter、Metadata、DynamicProperty、CodingRule |
| `Collaboration.Service` | Messaging、Presence、AttachmentIntegration、RemoteAssistance |
| `PlatformStudio.Service` | DataSource、Dataset、LowCode、Dashboard、Report、Publishing |
| `OperationsCenter.Service` | ServerMonitor、ProjectWorkspace、KnowledgeBase、IssueTracking、KnowledgeAssistant、DataAssistant、ModelGateway |
| `IoTCollector.Service` | Driver、DeviceConnection、Point、CollectionTask、EdgeManagement |

Worker、Agent、Screego、TURN 和本地模型运行时是辅助部署单元，不计入七个核心 Service Host。它们不得反向拥有核心领域数据；其生命周期、密钥、网络和升级策略在对应阶段详细设计。

`ReferenceData.Service` 是一个 Service Host，包含五个逻辑领域模块，但默认共享服务级 Migration、Outbox、Inbox 和基础设施。只有某个模块以后形成独立持久化生命周期并完成边界评审，才可成为独立初始化单元；这不改变五个领域模块的契约与数据所有权隔离。

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
| 03 / PF-00 | Identity | 正在开发 `Identity.Service` | Identity |
| 05 / PF-02 | SystemData | 创建 `SystemData.Service` | SystemData；包含数据库编排/环境引导能力 |
| 06 / PF-03 | ReferenceData | 继续利用现有 `ReferenceData.Service` 骨架 | Dictionary、Parameter、Metadata、DynamicProperty、CodingRule |
| 07 / PF-04 | File / Notification / Audit | 加入 `SystemData.Service` | File、Notification、Audit |
| 08 / PF-05 | Collaboration | 创建 `Collaboration.Service` | Messaging、Presence、AttachmentIntegration |
| 09 / PF-06 | RemoteAssistance | 加入 `Collaboration.Service` | RemoteAssistance |
| 10 / PF-07 | Scheduler / Platform Health | 加入 `SystemData.Service` | Scheduler、PlatformHealth |
| 11 / PF-08 | Low Code | 创建 `PlatformStudio.Service` | DataSource、Dataset、LowCode、Publishing 的首期范围 |
| 12 / PF-09 | Dashboard & Report | 加入 `PlatformStudio.Service` | Dashboard、Report，并复用受控 Dataset 契约 |
| 13 / PF-10 | ServerMonitor | 创建 `OperationsCenter.Service` | 只交付 ServerMonitor；与知识、问题和助手模块保持隔离 |
| 13A / PF-10A | Operations Center Knowledge & Assistant | 加入 `OperationsCenter.Service` | ProjectWorkspace、KnowledgeBase、IssueTracking、KnowledgeAssistant、DataAssistant、ModelGateway；进入实施前先补齐第 5.4 节的设计缺口 |
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
