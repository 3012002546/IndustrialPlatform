# Industrial Platform 开发总 TodoList

版本：V2.0
状态：持续维护
用途：总体阶段编排、独立会话派遣、阶段门禁和结果回写
蓝图依据：`01-Industrial Platform 总体架构设计 V1.0.md`、`05-Industrial Platform平台基础功能与独立模块设计.md`、`32-Industrial Platform Service Host与内部模块边界.md`

---

# 1. 文档职责

本文是 Industrial Platform 开发顺序和阶段状态的唯一总体维护源，不替代各模块详细设计和实施方案。

本文负责：

- 记录当前真实进度；
- 定义 PF/MES 执行阶段编号；
- 固定阶段依赖、准入条件和完成门禁；
- 为每个阶段提供独立会话的输入、目标和交付物；
- 链接阶段会话产生的蓝图、规格和实施方案；
- 在阶段完成后回写状态和证据。

本文不负责：

- 提前定义尚未开会讨论的字段、API、事件和页面细节；
- 在一个会话中完成多个独立模块的详细设计；
- 直接执行开发；
- 以文件编号代替真实执行优先级。

# 2. 编号规则

## 2.1 文档编号

实施文档编号与 PF/MES 阶段统一：

```text
03  PF-00 Identity（保持）
04  PF-01 视觉、主题与平台外壳
05  PF-02 SystemData
06  PF-03 ReferenceData（原04）
07  PF-04 File / Notification / Audit
08  PF-05 Collaboration
09  PF-06 RemoteAssistance
10  PF-07 Scheduler / Platform Health
11  PF-08 Low Code
12  PF-09 Dashboard & Report
13  PF-10 ServerMonitor
13A PF-10A Operations Center Knowledge & Assistant
14  PF-11 IoT Collector
15  MES-01 MasterData（原05）
16  MES-02 OperationalData（原06）
```

04、05、07～14 及 13A 在对应阶段管理会话中根据 `docs/implementation/TEMPLATE-开发实施方案.md` 创建，不提前生成空骨架。

## 2.2 执行阶段编号

- `PF-*`：平台基础、平台服务和独立产品优先阶段。
- `MES-*`：平台基础稳定后恢复的制造业务阶段。
- PF-04、PF-07、PF-09 各使用一个阶段管理会话，但会话内仍要求相邻模块分开建模和拆分任务。

## 2.3 阶段与 Service Host

阶段不等于微服务。PF-02～PF-11（含 PF-10A）的宿主创建/扩展映射固定读取蓝图 32：PF-02/04/07 共用 `SystemData.Service`，PF-05/06 共用 `Collaboration.Service`，PF-08/09 共用 `PlatformStudio.Service`；PF-03 使用 `ReferenceData.Service`；PF-10 创建 `OperationsCenter.Service` 并只处理 ServerMonitor，PF-10A 再加入知识、问题与助手模块；PF-11 创建 `IoTCollector.Service`。同宿主模块仍必须独立建模、独立 Schema 或表前缀、独立契约/权限/测试，禁止跨模块直读 Repository。

# 3. 当前真实基线

截至 2026-08-11：

| 范围 | 状态 | 证据与说明 |
| --- | --- | --- |
| BuildingBlocks | 已完成 | 原基础能力和 `TASK-BB-010` 已完成 |
| 可运行基线 | 基本完成，Docker 实机待验收 | `TASK-BASE-001/003/004/005/006` 完成，`TASK-BASE-002` 待 Docker 环境验收 |
| 统一前端第一批 | 已完成 | `TASK-FE-001～010` 执行记录均已完成 |
| Identity | 补强设计已确认 | 历史 `TASK-ID-001～016` 已完成；用户组、安全删除、正式 admin 引导和管理闭环为新增 `TASK-ID-017～023`，尚未开发 |
| PF-01 视觉主题与平台外壳 | 已完成（外部真机项待验收） | 实施 04 `TASK-PF01-001～007` 已完成；真实 Identity 联合验收 real E2E 19/19 |
| PF-02 SystemData | 部分实现，通用初始化设计已确认 | `961cad4` 已提交骨架/控制面，`61753dc` 已提交 migration-only Runner/PG/SQLite adapter/tests；通用 Seed/Bootstrap 扩展尚未开发 |
| ReferenceData | 仅骨架 | 已有健康检查、测试入口和详细实施方案，业务能力尚未开发 |
| MasterData | 暂缓 | 实施方案存在，本轮不进入开发 |
| OperationalData | 暂缓 | 实施方案存在，本轮不进入开发 |

代码进度以实施文档执行记录、提交和新鲜验证证据为准；`CLAUDE.md` 可记录协作过程，但不能代替任务验收表。

# 4. 总体执行路线

```text
已完成基础
  BuildingBlocks / Runnable Baseline / Frontend First Batch
        ↓
PF-00 Identity（历史 TASK-ID-001～016 已完成；补强 TASK-ID-017～023 尚未开发）
        ↓
PF-01 视觉、主题与平台外壳（TASK-PF01-001～007 已完成）
  ├──────────────┐
  ↓              ↓
PF-02 SystemData  PF-03 ReferenceData
  └──────┬───────┘
         ↓
PF-04 File / Notification / Audit
                    ↓
PF-05 Collaboration
                    ↓
PF-06 RemoteAssistance 验证与试点
                    ↓
PF-07 Scheduler / Platform Health
                    ↓
PF-08 Low Code
                    ↓
PF-09 Dashboard & Report
                    ↓
PF-10 ServerMonitor
                    ↓
PF-10A Operations Center Knowledge & Assistant
                    ↓
PF-11 IoT Collector
                    ↓
MES-01 MasterData
                    ↓
MES-02 OperationalData
                    ↓
MES-03+ WorkOrder / Weighting / Trace / BatchRecord / 生产闭环
```

执行顺序允许在阶段门禁内调整：

- PF-01 的设计会话可以在 PF-00 后半段开始，但真实 Identity 契约未稳定前不得完成集成验收。
- PF-02 与 PF-03 的后端设计可以并行，页面必须共同遵循 PF-01。
- PF-04 使用一个阶段管理会话，File、Notification、Audit 分开建模和派遣；Collaboration 开发前至少需要 Audit 与 File 稳定契约。
- PF-07 使用一个阶段管理会话，Scheduler 与 Platform Health 分开建模和派遣。
- PF-09 使用一个阶段管理会话，Dashboard 与 Report 共享数据集契约但保持产品边界。
- PF-10 先创建 `OperationsCenter.Service` 并独立交付 ServerMonitor；PF-10A 才向同一宿主加入项目知识、问题与助手模块。
- Operations Center 与 IoT Collector 不互为领域依赖；PF-11 的实际启动条件由阶段管理任务根据 PF-10A 的设计进度复核，不得把知识闭环未完成误报为已设计。

# 5. 单阶段单管理会话工作流

每个 PF 阶段（包括 PF-10A）只创建一个阶段管理会话。该会话只负责详细设计和任务派遣，不直接开发业务代码；实际编码由它派遣的执行任务完成。

```text
读取总体蓝图、阶段任务卡和项目记忆
→ 完整读取 TEMPLATE-开发实施方案.md
→ 检查当前代码、现有实施文档和真实状态
→ 与用户反复确认详细设计
→ 直接编写对应编号的 docs/implementation 实施方案
→ 按母版生成任务依赖和九字段任务卡
→ 派遣实际开发任务
→ 跟踪提交、测试和环境证据
→ 组织阶段验收
→ 回写执行记录、下一阶段契约和总 TodoList
```

不得为同一阶段再要求用户切换“规格会话”“计划会话”或“开发会话”。阶段管理会话可以调用执行任务，但用户始终在该阶段会话中完成设计确认和验收。

# 6. 全局状态流转

阶段状态：

```text
待启动 → 设计中 → 任务待确认 → 可派遣 → 派遣中 → 待验收 → 已完成
```

发现跨阶段冲突时：

```text
任意状态 → 设计待确认
```

任务状态继续使用实施规范：

```text
待细化 → 可派遣 → 已派遣 → 开发中 → 待验收 → 已完成
```

# 7. PF-00 Identity

**状态：** 补强设计已确认，尚未派遣
**现有实施文档：** `docs/implementation/03-Industrial Platform Identity Service开发实施方案.md`
**目标：** 完成用户、角色、权限、本地登录、令牌、会话、企业 SSO 边界和三端真实登录闭环。
**当前进度：** 历史 `TASK-ID-001～016` 已由提交 `48c5374` 完成；新增 `TASK-ID-017～023` 覆盖用户组、安全删除、SystemData 协同 admin 引导、前端管理闭环和真实登录验收，设计已确认但尚未开发。
**前置：** BuildingBlocks、可运行基线、统一前端第一批。
**禁止范围：** SystemData 行政组织、菜单所有权、聊天、业务数据权限模型扩张。

**完成门禁：**

- `TASK-ID-001～016` 保留历史完成记录，`TASK-ID-017～023` 有完整新鲜执行记录；
- 登录、刷新、注销、撤销、401、403、菜单/按钮权限契约和关键 E2E 通过；
- 前端切换真实 `HttpAuthGateway`；
- 输出 PF-01/02/03 可消费的用户、权限、会话和身份上下文契约；
- Docker 缺失造成的外部验收项明确保留为待验收，不伪报完成。

**补强起点：** 历史 `TASK-ID-001～016` 不重复派遣；下一项为 `TASK-ID-017` 用户组领域、持久化与授权求值。

**下一会话：** 用户恢复 PF-00 后继续现有 Identity 开发任务，不在其他阶段代做 Identity 剩余范围。

# 8. PF-01 视觉、主题与平台外壳

**状态：** 已完成（`TASK-PF01-001～007`；外部真机 safe-area 项待验收）
**建议会话标题：** `PF-01 视觉主题与平台外壳阶段管理`
**输入：** 蓝图 04、05、28；已完成统一前端第一批；Identity 稳定前端契约。
**目标：** 把已批准的工业视觉方向落实为可测试的 Design Token、主题恢复、PC/PDA/Mobile 外壳和通用管理组件规范。
**依赖：** PF-00 的接口稳定；设计可以提前，最终集成验收必须等待 PF-00。
**禁止范围：** SystemData 领域、业务表单设计器、看板产品功能。

**设计会话必须解决：**

- 主题 Token 分层、明暗和系统模式；
- 工业青/科技蓝/中性灰的完整语义映射；
- 顶栏、工具轨、功能树、内容区和 12 标签治理；
- 树表、查询区、表单、抽屉、状态、空态和错误态；
- PC 密度、PDA 48px、Mobile 44px 触控约束；
- 主题持久化、首屏恢复、可访问性和视觉回归策略；
- 对当前统一前端第一批的迁移范围和兼容策略。

**设计交付：** 已批准阶段规格、视觉验收基线、实施文档 04、任务依赖和七张九字段任务卡均已编写完成，且 `TASK-PF01-001～007` 已全部实现并通过验收（静态门禁全绿、mock E2E 102/102、真实 Identity E2E 19/19）；开发提交未执行（按协作约定），任务状态以实施 04 §16 为准。
**完成门禁：** Identity 页面和三端外壳接入主题；自动化和截图验收通过；未引入业务假数据。

# 9. PF-02 SystemData

**状态：** 待派遣（设计已批准、存量部分实现）；migration-only Runner 已提交，通用扩展待派遣
**Service Host：** 创建 `SystemData.Service`；本阶段只交付 SystemData 模块。
**建议会话标题：** `PF-02 SystemData阶段管理`
**输入：** 蓝图 05、07、13、20、23、27、30、31、32、33；PF-00 身份契约；PF-01 页面规范；PostgreSQL 18 与当前 `deploy/cloud-dev` 最小引导现状。
**目标：** 以 SystemData 通用服务初始化控制面为最高优先级，统一 registration、plan、审批/备份、provision、迁移、RequiredSeed、按需 SecretBootstrap、Operation 和 readiness，再交付行政组织、岗位、任职关系、菜单导航、功能开关、服务目录和主题默认值。
**依赖：** PF-00；页面依赖 PF-01。
**禁止范围：** 制造组织、字典参数、物料设备、租户运营后台。

**设计会话必须解决：**

- **最高优先级：** `TASK-SD-001～004` 是阻塞链。`961cad4` 已实现 001～002 并待验收；`61753dc` 已提交 003 的 migration-only Runner，但 migration/seed/bootstrap 通用扩展仍待派遣；004 完成 consumer handshake/readiness 与验收。在该链满足初始化门禁前，不得开始 `TASK-SD-005+`。

- 行政组织树、岗位和任职关系的不变量；
- 菜单、路由、按钮资源与 Identity 权限的所有权和同步；
- PC/PDA/Mobile 可见性和功能开关覆盖层级；
- 服务目录、所有者、入口和健康地址；
- 默认租户与用户主题覆盖规则；
- 数据迁移、缓存、事件、审计和前端页面。
- `ServiceKey + ModuleKey`、InitializationManifest/SeedSets、dry-run/plan、异步 initialize/apply、Operation 状态和受信查询 API；
- `SystemBaseline/TenantBaseline/EnvironmentSample/SecretBootstrap` 四类种子、RequiredForReadiness、环境允许列表与依赖图；
- 每模块 `<module>_schema_migrations + <module>_seed_ledger` 双账本、SeedObservation、checksum drift、DataPatch 和管理员维护数据保护；
- SystemData 普通连接与 provisioning 管理凭据隔离，最小业务角色、审批、备份、审计、脱敏、限流和幂等；
- PostgreSQL advisory lock、迁移/种子历史、失败恢复、expand/contract、禁止多副本重复初始化和 readiness 门禁；
- Development/Test 必要迁移/种子自动策略、EnvironmentSample 显式启用、生产 `plan → 审批 → 备份 → apply` 且禁止启动时播种，以及 SQLite 显式迁移/双账本语义；
- 服务自有签名 migration/seed/initializer 产物与 Secret Provider；SystemData 不理解业务表、不直写 Repository、不接收或透传 Secret 值；
- SystemData 自身数据库由基础设施最小引导的 bootstrap 例外，不得形成调用自身 API 的循环依赖。

**交付：** SystemData 蓝图/实施方案/任务卡、Service Initialization API/manifest/双账本/readiness 母版和 PF-03+ 可消费契约。当前 `961cad4` 已提交骨架与控制面，`61753dc` 已提交 migration-only Runner；通用 Seed/Bootstrap 扩展和验收尚未完成，其他 SystemData 业务未开发。
**完成门禁：** 覆盖首次初始化、重复 apply、并发多副本、迁移/种子版本升级、同版本 checksum drift、部分失败重试、缺 Secret、生产未审批/未备份、EnvironmentSample 环境拒绝、共享物理库多个 ModuleKey 隔离、管理员维护数据不被覆盖、SystemData 不可用消费者 NotReady 和 SystemData 自身无循环自举。所有服务/模块拥有自己的 Schema、初始化产物和双账本，SystemData 只保存脱敏 observation；随后管理员可完成组织、岗位、导航、开关、服务和主题管理。不得使用 `EnsureCreated`，权限、审计、契约和关键 E2E 通过。

# 10. PF-03 ReferenceData

**状态：** 仅骨架，待独立会话复核
**Service Host：** 继续利用现有 `ReferenceData.Service` 骨架；内部模块为 Dictionary、Parameter、Metadata、DynamicProperty、CodingRule。
**建议会话标题：** `PF-03 ReferenceData阶段管理`
**现有实施文档：** `docs/implementation/06-Industrial Platform ReferenceData Service开发实施方案.md`
**输入：** 蓝图 05、07、08、21、26、27、31；现有 06 实施方案；PF-00/01/02 契约。
**目标：** 在不丢弃现有骨架和详细设计的前提下，复核字典、参数、元数据、动态配置和编码规则与新平台边界是否一致，再决定任务调整。
**依赖：** PF-00；与 PF-02 对菜单、主题默认值和系统参数所有权达成明确契约。
**禁止范围：** SystemData、MasterData 业务实体、Low Code 运行时。

**复核会话必须输出：**

- 保留、修改、删除的现有 `TASK-RD-001～014` 清单；
- SystemData 与 ReferenceData 参数所有权矩阵；
- 当前代码骨架与实施文档差异；
- PF-01 页面规范迁移；
- 修订后的依赖、状态、任务卡和验收证据要求。

**完成门禁：** 五类能力纵向交付并连接真实 Gateway；缓存、Outbox、审计、页面、契约和 E2E 完成；不越界实现业务实体属性值。

# 11. PF-04 File / Notification / Audit

PF-04 只使用一个阶段管理会话，输出实施文档 07。File、Notification、Audit 必须分开建模、分开数据归属并拆成可独立派遣的任务。

**Service Host：** 三个模块加入现有 `SystemData.Service`，不得创建当前独立 File/Audit/Notification Service Host。

## 11.1 Audit

**状态：** 待启动
**建议会话标题：** `PF-04 File Notification Audit详细设计与任务派遣`
**输入：** 蓝图 05、30、31；BuildingBlocks 日志和事件能力；PF-00 身份上下文。
**目标：** 建立跨模块追加型审计事实源、查询权限、合规查看和可靠写入。
**关键问题：** 事件契约、Outbox 接入、前后值边界、敏感字段、保留策略、查询索引、合规访问再审计、故障降级。
**完成门禁：** Identity、SystemData 和至少一个平台服务可以可靠写入并查询审计；失败可监控且不静默丢失。

## 11.2 File

**状态：** 待启动
**输入：** 蓝图 05、20、27、31；对象存储和安全规范；PF-00 与 Audit 契约。
**目标：** 建立上传会话、隔离、校验、扫描、授权下载、冻结、保留和清理。
**关键问题：** `FileNId`、对象路径租户隔离、MIME/魔数、扫描适配、签名下载、配额、幂等、清理、审计和页面。
**完成门禁：** 文件未经扫描不可被业务下载；扫描故障可见；授权、审计、保留和清理测试通过。

## 11.3 Notification

**状态：** 待启动
**输入：** 蓝图 05；PF-00/01/02 与 Audit 契约；Realtime 可复用基础。
**目标：** 建立公告、系统通知、个人收件箱、投递、已读和跳转目标。
**关键问题：** 目标范围、优先级、发布时间、失效、幂等投递、离线读取、SignalR 推送、通知模板、权限、审计和三端页面。
**完成门禁：** 系统到用户通知闭环完成，且与用户聊天边界明确分离。

# 12. PF-05 Collaboration

**状态：** 待启动
**Service Host：** 创建 `Collaboration.Service`；内部模块至少保持 Messaging、Presence、AttachmentIntegration 分界。
**建议会话标题：** `PF-05 Collaboration阶段管理`
**输入：** 蓝图 05；PF-00、PF-01、PF-04 稳定契约。
**目标：** 交付登录用户之间的一对一文本、图片和文件聊天。
**依赖：** Identity、Audit、File；可消费 Notification/Realtime 基础。
**禁止范围：** 群聊、语音、视频会议、外部联系人、机器人和远程控制。

**设计会话必须解决：**

- 会话、消息、参与人、游标、已读和撤回模型；
- REST 与 SignalR 分工、幂等和离线补拉；
- Outbox、顺序、重复、断线和多实例；
- 附件隔离和扫描状态；
- 365 天内容、3 年合规访问审计和法律保全；
- 合规查看权限、原因和再审计；
- PC 抽屉/完整页、PDA/Mobile 全屏页面。

**完成门禁：** 两名登录用户可跨断线完成文字和合规附件聊天；权限、保留、审计、契约和关键 E2E 通过。

# 13. PF-06 RemoteAssistance

**状态：** 详细设计已确认，PoC 决策门禁待派遣
**Service Host：** RemoteAssistance 作为独立内部模块加入 `Collaboration.Service`，并保留未来物理拆分能力。
**建议会话标题：** `PF-06 RemoteAssistance阶段管理`
**实施文档：** `docs/implementation/09-Industrial Platform RemoteAssistance开发实施方案.md`
**输入：** 蓝图 05；PF-05 会话契约；Screego 官方仓库和部署配置。
**目标：** 先验证现场网络中的 WebRTC 屏幕共享，再决定 Screego 适配或自研轻量信令路线。
**依赖：** PF-05；验证环境需具备内部 HTTPS、WebSocket 和可配置网络策略。
**禁止范围：** 远程鼠标键盘、无人值守、默认录屏。

**验证会话必须解决：**

- 同网段、跨 VLAN、无互联网、UDP 受限和强制 TURN；
- Edge、Chrome 和现场实际浏览器；
- 1080p 文字可读、连续 30 分钟、断线恢复；
- CPU、内存、共享端上行带宽和 1～3 观看者；
- Screego 登录不足、平台一次性凭证、参与人白名单；
- GPL-3.0 独立部署、修改和交付边界；
- 独立路由/新窗口与可选 iframe 的权限兼容性。

**决策门禁：** 形成带证据的采用、适配或自研结论。验证失败时功能开关保持关闭，不阻塞 Collaboration。
**已确认方向（2026-08-14）：** 平台原生 RemoteAssistance 控制面与最小原生 WebRTC 信令作为推荐生产候选，未修改 Screego 仅作独立基准 PoC；先执行 `TASK-PF06-001` 双 PoC、现场网络与 GPL-3.0 交付门禁，证据和用户确认通过后才允许 `TASK-PF06-002～008` 进入领域、契约、适配、页面、部署与验收开发。当前未派遣、未开发、未构建、未测试。
**产品完成门禁：** 从聊天发起、邀请、接受、共享、终止和元数据审计闭环通过。

# 14. PF-07 Scheduler / Platform Health

PF-07 只使用一个阶段管理会话，输出实施文档 10。Scheduler 与 Platform Health 分开建模并拆成独立任务。

**Service Host：** Scheduler 与 PlatformHealth 模块加入 `SystemData.Service`。

## 14.1 Scheduler

**状态：** 待启动
**建议会话标题：** `PF-07 Scheduler Platform Health详细设计与任务派遣`
**目标：** 定义任务、Cron、启停、互斥、超时、重试、执行记录和人工触发。
**边界：** Scheduler 不包含所属模块业务规则；处理器通过公开契约注册。
**完成门禁：** 单实例和多实例互斥、失败重试、人工触发、审计、指标和管理页面通过。

## 14.2 Platform Health

**状态：** 待启动
**目标：** 聚合 API、数据库、缓存、消息、文件扫描、Realtime 和 RemoteAssistance 依赖摘要。
**边界：** 不采集完整主机、进程、磁盘和日志告警。
**完成门禁：** 工作台和运行治理页显示真实服务状态、降级原因和 TraceId，不展示伪造健康数据。

# 15. PF-08 Low Code

**状态：** 待启动
**Service Host：** 创建 `PlatformStudio.Service`；内部模块边界读取蓝图 32。
**建议会话标题：** `PF-08 Low Code阶段管理`
**输入：** 蓝图 05、21、27、28、31；PF-01/02/03/04/07 稳定契约。
**目标：** 交付受治理的数据模型、表单、列表、页面、权限、发布、版本和回滚。
**禁止范围：** 任意 SQL、任意脚本、绕过权限、复杂工作流和插件市场。

**设计门禁：** 明确设计态、发布快照和运行数据分离；与 ReferenceData、SystemData、File、Audit 的契约；三端渲染和版本回滚。
**完成门禁：** 一个示例应用可从建模、设计、授权、发布到运行和回滚，全程有版本和审计。

# 16. PF-09 Dashboard & Report

PF-09 只使用一个阶段管理会话，输出实施文档 12。Dashboard 与 Report 共享受控数据集契约，但保持产品边界并拆成独立任务。

**Service Host：** Dashboard 与 Report 模块加入 `PlatformStudio.Service`，不得跨模块直读 DataSource/Dataset Repository。

## 16.1 Dashboard

**状态：** 待启动
**建议会话标题：** `PF-09 Dashboard Report详细设计与任务派遣`
**目标：** 设计数据集、指标、维度、图表、看板编排和大屏运行模式。
**边界：** 不承担固定版式导出和业务录入。

## 16.2 Report

**状态：** 待启动
**目标：** 设计参数查询、固定版式、Excel/PDF 导出、定时生成和通知。
**边界：** 长任务通过 Scheduler，结果通过 File，完成通过 Notification；前端不得提交任意 SQL。

**共同门禁：** 在同一阶段管理会话中确认共享数据源/数据集契约，并在实施文档 12 中分别建立 Dashboard 与 Report 任务依赖。

# 17. PF-10 ServerMonitor

**状态：** 待启动
**建议会话标题：** `PF-10 ServerMonitor阶段管理`
**Service Host：** 创建 `OperationsCenter.Service`；本阶段只加入并交付 ServerMonitor 模块。
**输入：** 蓝图 02、05、20、30、32；PF-04/07 稳定契约。
**目标：** 独立复核并实施主机、CPU、内存、磁盘、网络、进程、服务、端口、日志、告警、Agent 和运维看板。
**边界：** 与 Platform Health 分层；不实现 ProjectWorkspace、KnowledgeBase、IssueTracking、KnowledgeAssistant、DataAssistant 或 ModelGateway；首期不自动创建问题、不自动关联知识库、不主动介入问题闭环，只保留公开契约扩展点。
**完成门禁：** 至少一个受管节点完成注册、采集、状态、告警、通知和处置记录闭环，且未越界实现 PF-10A 模块。

# 17A. PF-10A Operations Center Knowledge & Assistant

**状态：** 设计待确认
**建议会话标题：** `PF-10A Operations Center知识问题与助手阶段管理`
**Service Host：** 向既有 `OperationsCenter.Service` 加入 ProjectWorkspace、KnowledgeBase、IssueTracking、KnowledgeAssistant、DataAssistant、ModelGateway。
**输入：** 蓝图 05、24、32；PF-04/09/10 稳定契约；上一个 Operations Center 设计会话的已确认记录。
**已确认边界：** ProjectWorkspace 的最小知识/数据作用域；知识“草稿→审核→发布→索引”原则；知识内容类型；KnowledgeAssistant 的带引用 RAG；ModelGateway 的本地优先、DeepSeek 与 OpenAI-Compatible 适配；DataAssistant 的受控 Dataset、结构化查询计划和受限 Text-to-SQL 安全边界。
**设计缺口：** 上一个会话只推进到 DataAssistant，尚未完成 IssueTracking 与 KnowledgeBase 的完整数据闭环。不得把现有原则性结论表述为完整数据模型、状态机或可派遣实施方案。
**首要设计门禁：** 完成蓝图 32 第 5.4 节，包括问题登记到关闭、解决方案人工转知识草稿、知识审核发布与索引、失败恢复、模块契约/事件/事务，以及助手带引用检索的端到端闭环，并逐项取得用户确认。
**禁止范围：** 在闭环确认前生成开发任务卡或派遣实现；ServerMonitor 的既有领域模型不得被知识/问题模块直接读取。
**完成门禁：** 在阶段管理会话中补齐详细设计后另行定义，不得在本总 Todo 中预设表、API 或页面。

# 18. PF-11 IoT Collector

**状态：** 待启动
**Service Host：** 创建 `IoTCollector.Service`；内部模块为 Driver、DeviceConnection、Point、CollectionTask、EdgeManagement。
**建议会话标题：** `PF-11 IoT Collector阶段管理`
**输入：** 蓝图 05、08、17、20、30；PF-07/10 可观测契约。
**目标：** 复核并实施驱动、连接、点位、采集任务、边缘缓存、断线续传和数据质量。
**边界：** 不承担 MasterData、报表和 MES 规则；设备业务档案仍归 MasterData。
**完成门禁：** 选择一个首期协议完成连接、采集、缓存、断线恢复、幂等写入和监控闭环；其余协议按适配器任务追加。

# 19. MES 阶段恢复条件

## 19.1 MES-01 MasterData

**状态：** 暂缓
**现有实施文档：** `docs/implementation/15-Industrial Platform MasterData Service开发实施方案.md`
**恢复门禁：** PF-00～PF-07 完成；PF-08/09 是否前置由产品需要决定；必须新开会话复核现有设计与 SystemData、ReferenceData、File、Audit 和主题契约。

## 19.2 MES-02 OperationalData

**状态：** 暂缓
**现有实施文档：** `docs/implementation/16-Industrial Platform OperationalData Service开发实施方案.md`
**恢复门禁：** MES-01 稳定契约完成，并在独立会话复核库存、WMS、追溯和现有任务卡。

## 19.3 MES-03 以后

WorkOrder、Weighting、Trace、BatchRecord 和生产闭环分别开会话设计。原蓝图 09 中的固定 Sprint 和未验证性能指标不再作为直接开发依据。

# 20. 总体阶段门禁

任一阶段只有同时满足以下条件才能标记“已完成”：

- 阶段书面规格经用户评审并提交；
- 独立实施方案和可派遣任务卡已提交；
- 所有任务执行记录完整；
- 代码、迁移、契约、页面和文档与实现一致；
- 单元、集成、契约和关键路径 E2E 有新鲜证据；
- 外部环境缺失项明确标记“待验收”；
- 默认租户可运行且多租户边界未被破坏；
- 权限、审计、错误、TraceId、健康和指标满足蓝图；
- 下一阶段输入契约已写清；
- 本文状态、链接和证据摘要已回写。

# 21. 阶段跟踪表

| 阶段 | 状态 | 阶段管理会话 | 设计依据 | 实施方案 | 派遣/提交 | 验收证据 |
| --- | --- | --- | --- | --- | --- | --- |
| PF-00 Identity | 补强设计已确认，尚未派遣 | 现有 Identity 会话 | 蓝图 13、31、33 | 实施 03 | 历史提交 `48c5374` 完成 TASK-ID-001～016；TASK-ID-017～023 尚无实现提交 | 见实施 03；本轮仅完成设计，补强联合验收尚未执行 |
| PF-01 视觉主题 | 已完成（外部真机项待验收） | 现有 PF-01 会话继续 | 已批准 PF-01 规格 | `docs/implementation/04-Industrial Platform视觉主题与平台外壳开发实施方案.md` | 设计提交 `e2d24a4`、`d7ef889`、`efb3b35`；开发未提交(按协作约定) | TASK-PF01-001～007 完成；静态门禁全绿、mock E2E 102/102、真实 Identity E2E 19/19 |
| PF-02 SystemData | 部分实现；通用初始化设计已确认 | 现有 PF-02 会话 | 蓝图 05、07、33 V2.0 | `docs/implementation/05-Industrial Platform SystemData开发实施方案.md` | `961cad4` 已实现 001～002；`61753dc` 已提交 003 migration-only 基线 | 001～002 待验收；003 通用扩展待派遣；004+ 未实现，本轮未重跑测试 |
| PF-03 ReferenceData | 仅骨架 | 待创建 | 蓝图及现有设计待复核 | 实施 06 待修订 | - | - |
| PF-04 File / Notification / Audit | 待启动 | 待创建 | 蓝图 05、30、31 | 实施 07 待创建 | - | - |
| PF-05 Collaboration | 待启动 | 待创建 | 蓝图 05 | 实施 08 待创建 | - | - |
| PF-06 RemoteAssistance | 详细设计已确认，PoC 门禁待派遣 | 当前 PF-06 阶段管理会话 | 蓝图 05、32、33；Screego/W3C 官方证据 | `docs/implementation/09-Industrial Platform RemoteAssistance开发实施方案.md` | 推荐平台原生控制面/最小信令，Screego 仅未修改基准 PoC | TASK-PF06-001 待另行派遣；002～008 门禁阻塞，未开发/未测试 |
| PF-07 Scheduler / Platform Health | 待启动 | 待创建 | 蓝图 05、30 | 实施 10 待创建 | - | - |
| PF-08 Low Code | 待启动 | 待创建 | 蓝图 21 待复核 | 实施 11 待创建 | - | - |
| PF-09 Dashboard & Report | 待启动 | 待创建 | 蓝图 22 待复核 | 实施 12 待创建 | - | - |
| PF-10 ServerMonitor | 待启动 | 待创建 | 蓝图 02、32 待复核 | 实施 13 待创建 | - | - |
| PF-10A Operations Center Knowledge & Assistant | 设计待确认 | 待创建 | 蓝图 32 第 5.4 节 | 实施 13A 待创建 | - | - |
| PF-11 IoT Collector | 待启动 | 待创建 | 蓝图 17 待复核 | 实施 14 待创建 | - | - |
| MES-01 MasterData | 暂缓 | 待恢复时创建 | 蓝图 14 待复核 | 实施 15 暂缓 | - | - |
| MES-02 OperationalData | 暂缓 | 待恢复时创建 | 蓝图 14A 待复核 | 实施 16 暂缓 | - | - |

# 22. 新阶段管理会话启动模板

开始某个阶段时只创建一个管理会话。以下是 PF-02 的完整启动示例；其他阶段使用同一结构，并复制真实阶段编号、实施文档编号和输入蓝图：

```text
开始 PF-02 SystemData 阶段管理会话。

先读取：
1. docs/blueprint/01-Industrial Platform 总体架构设计 V1.0.md
2. docs/blueprint/05-Industrial Platform平台基础功能与独立模块设计.md
3. docs/blueprint/09-Industrial Platform开发总TodoList.md 第 9 节 PF-02 任务卡
4. docs/implementation/TEMPLATE-开发实施方案.md（必须完整读取）
5. CLAUDE.md、相关项目记忆、Identity/PF-01稳定契约和当前代码

这个会话只负责详细设计、任务派遣、跟踪和验收，不直接开发业务代码。先核对真实状态，再逐个问题讨论；设计确认后直接按母版创建 docs/implementation/05-Industrial Platform SystemData开发实施方案.md，生成九字段任务卡并派遣实际开发任务。开发结果持续回写同一实施文档和总TodoList，不再要求我切换规格会话、计划会话或开发会话。
```

# 23. 本轮调整记录

- 平台基础和独立模块优先于 MasterData、OperationalData 和 MES 业务扩张。
- 实施文档按 PF/MES 阶段重排为 03～16；原 ReferenceData、MasterData、OperationalData 调整为 06、15、16。
- Identity 历史实现证据校准为 `TASK-ID-001～016` 已完成；用户组、安全删除、正式 admin 引导和管理闭环登记为新增 `TASK-ID-017～023`，不把设计完成写成开发完成。
- MasterData、OperationalData 改为暂缓。
- 删除旧的固定 MES Sprint 路线，改为阶段门禁和单阶段单管理会话。
- 每个 PF 阶段（含 PF-10A）一个管理会话；PF-04、PF-07、PF-09 在同一阶段会话内保持模块分开建模和任务拆分。
