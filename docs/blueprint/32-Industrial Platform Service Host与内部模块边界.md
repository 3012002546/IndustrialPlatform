# Industrial Platform Service Host 与内部模块边界

版本：V1.0
状态：已确认，平台微服务母版
生效日期：2026-08-11

---

# 1. 文档定位与优先级

本文固定 PF-02～PF-11 共用的当前部署宿主、内部模块边界和未来拆分规则，是平台基础层 Service Host 的权威母版。各阶段管理任务必须同时读取本文、`09-Industrial Platform开发总TodoList.md`、`TEMPLATE-开发实施方案.md`、项目记忆和当前代码。

旧蓝图中按领域列出的 `File Service`、`Audit Service`、`Dashboard Service`、`Server Monitor Service` 等名称，保留为领域设计或未来物理拆分目标；凡与本文的当前部署宿主清单冲突，以本文为准。

# 2. 核心原则

- 当前单租户必须完整可用；所有领域边界预留可信身份上下文提供的 `TenantNId`。
- 阶段不等于微服务。合并只表示共用部署宿主，不表示合并领域模型或数据所有权。
- 同一 Service Host 内的模块必须独立建模，使用独立 Schema 或表前缀、独立公开契约、权限资源、迁移和测试。
- 禁止跨模块直读或写入其他模块的 Repository 或数据表；协作使用公开应用契约、API 或事件。
- 模块间不建立数据库级跨模块外键。跨模块只保存稳定业务标识和必要快照，并按契约维护一致性。
- 每个模块都必须能够在不改变外部语义的前提下迁移到独立进程和数据库，为未来物理拆分保留边界。

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

# 4. 阶段到 Service Host 的正式映射

阶段编号和“一阶段一个管理任务”的工作流保持不变。前一阶段可以创建宿主，后续阶段向同一宿主增加独立模块。

| 文档/阶段 | 阶段名称 | Service Host 动作 | 本阶段宿主内模块范围 |
| --- | --- | --- | --- |
| 03 / PF-00 | Identity | 正在开发 `Identity.Service` | Identity |
| 05 / PF-02 | SystemData | 创建 `SystemData.Service` | SystemData |
| 06 / PF-03 | ReferenceData | 继续利用现有 `ReferenceData.Service` 骨架 | Dictionary、Parameter、Metadata、DynamicProperty、CodingRule |
| 07 / PF-04 | File / Notification / Audit | 加入 `SystemData.Service` | File、Notification、Audit |
| 08 / PF-05 | Collaboration | 创建 `Collaboration.Service` | Messaging、Presence、AttachmentIntegration |
| 09 / PF-06 | RemoteAssistance | 加入 `Collaboration.Service` | RemoteAssistance |
| 10 / PF-07 | Scheduler / Platform Health | 加入 `SystemData.Service` | Scheduler、PlatformHealth |
| 11 / PF-08 | Low Code | 创建 `PlatformStudio.Service` | DataSource、Dataset、LowCode、Publishing 的首期范围 |
| 12 / PF-09 | Dashboard & Report | 加入 `PlatformStudio.Service` | Dashboard、Report，并复用受控 Dataset 契约 |
| 13 / PF-10 | Operations Center | 创建 `OperationsCenter.Service` | 本文第 5 节所列模块；详细范围由 PF-10 管理任务确认 |
| 14 / PF-11 | IoT Collector | 创建 `IoTCollector.Service` | Driver、DeviceConnection、Point、CollectionTask、EdgeManagement |

# 5. Operations Center 母版边界

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

`ServerMonitor` 是 `OperationsCenter.Service` 内部独立模块。第一阶段不自动创建问题、不自动关联 KnowledgeBase，也不主动介入问题闭环；只预留未来通过公开契约扩展的能力。

# 6. 阶段管理工作流

PF-01～PF-11 每阶段仍只创建一个阶段管理任务。阶段管理任务负责读取母版、蓝图、项目记忆与当前代码，反复完成详细设计、九字段任务卡、派遣、跟踪和验收，但不直接开发业务代码。

本母版不定义各模块的表、字段、API、事件或页面。对应 PF 阶段必须在其唯一管理任务中完成详细设计，不得从本文摘要推断实现细节，也不得提前创建空的阶段实施方案。
