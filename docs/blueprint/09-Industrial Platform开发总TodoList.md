# Industrial Platform 开发总 TodoList

版本：V2.2
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
09A PF-06A 终端运行时与客户端打包
10  PF-07 Scheduler / Platform Health
11  PF-08 Low Code
12  PF-09 Dashboard & Report
13  PF-10 ServerMonitor
13A PF-10A Operations Center Knowledge & Assistant
13B PF-10B 标签管理平台
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

阶段不等于微服务。PF-02～PF-11（含 PF-10A）的宿主创建/扩展映射固定读取蓝图 32：PF-02/04/07 共用 `SystemData.Service`，PF-05/06 共用 `Collaboration.Service`，PF-08/09 共用 `PlatformStudio.Service`；PF-03 使用 `ReferenceData.Service`；PF-10 创建 `OperationsCenter.Service` 并只处理 ServerMonitor，PF-10A 再加入知识、问题与助手模块；PF-10B 创建 `Label.Service`，然后 PF-11 创建 `IoTCollector.Service`；PF-06A 是客户端/运行时专项，不增加核心 Host。`Service Host != Domain Module != Initialization Unit != Deployment Unit`；同宿主模块必须独立建模、使用明确的 Schema/模块表前缀逻辑命名空间及契约/权限/测试边界，但只有独立持久化生命周期才拆分初始化单元和物理治理设施。

# 3. 当前真实基线

截至 2026-09-07（本轮只核对文档、提交和代码，历史验收不重跑）：

| 范围 | 状态 | 证据与说明 |
| --- | --- | --- |
| BuildingBlocks | 已完成 | 原基础能力和 `TASK-BB-010` 已完成 |
| 可运行基线 | 基本完成，Docker 实机待验收 | `TASK-BASE-001/003/004/005/006` 完成，`TASK-BASE-002` 待 Docker 环境验收 |
| 统一前端第一批 | 已完成 | `TASK-FE-001～010` 执行记录均已完成 |
| Identity | 当前范围已完成 | `TASK-ID-001～023` 已完成并合入 `develop`；本地可验证门禁全绿，真实 PostgreSQL/Redis 联合登录链路保留为外部验收项 |
| PF-01 视觉主题与平台外壳 | 已完成（外部真机项待验收） | 实施 04 `TASK-PF01-001～007` 已完成；真实 Identity 联合验收 real E2E 19/19 |
| PF-02 SystemData | 收束验收中 / 26项证据状态已回写 | 2026-09-04已知整改缺陷复验关闭；24项部分、ORG-02/03隔离通过/真实HTTP待补；014完整UI与200%、015真实链、016外部门禁保留；017本轮回写完成，013/PF-02继续active，不进入PF-03 |
| ReferenceData | 已完成并合入 | PF-03 七模块、共享治理、七个 PC 页面与真实链路验收已完成；见 `docs/evidence/PF-03.md` |
| 架构收敛整改 | 已完成 | 四个已批准工作包完成，结果已纳入当前架构基线 |
| PF-04 Core | 已有代码提交，真实验收待补 | HEAD `8625efb`；`docs/evidence/PF-04.md` 有历史自测及外部缺口，不能当作本轮新鲜验证 |
| PF-05/PF-06 | 字段/接口/线框已细化，待派遣/待前置核验 | 实施08/09 V1.2；原批准范围和PoC门禁保留 |
| PF-06A/PF-10B | 核心规格已细化，待派遣/待实际目标核验 | 实施09A/13B V1.1；没有原生或Label工程 |
| MasterData | 暂缓 | 实施方案存在，本轮不进入开发 |
| OperationalData | 暂缓 | 实施方案存在，本轮不进入开发 |

代码进度以实施文档执行记录、提交和新鲜验证证据为准；`CLAUDE.md` 可记录协作过程，但不能代替任务验收表。

# 4. 总体执行路线

```text
已完成基础
  BuildingBlocks / Runnable Baseline / Frontend First Batch
        ↓
PF-00 Identity（TASK-ID-001～023 当前范围已完成）
        ↓
PF-01 视觉、主题与平台外壳（TASK-PF01-001～007 已完成）
        ↓
PF-02 SystemData
        ↓
架构收敛整改（四个工作包，不新增 PF 编号）
        ↓
PF-03 ReferenceData
        ↓
PF-04 File / Notification / Audit
                    ↓
PF-05 Collaboration
                    ↓
PF-06 RemoteAssistance 验证与试点
                    ↓
PF-06A 终端运行时与客户端打包
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
PF-10B 标签管理平台
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
- PF-04 使用一个阶段管理会话，File、Notification、Audit 分开建模，整个 PF 统一派遣、内部任务连续执行；Collaboration 文本可基于可信身份/目录/本地持久审计先闭环，附件集成再消费 File 稳定契约；完整阶段验收仍落实扫描/保留/法律保全。
- PF-07 使用一个阶段管理会话，Scheduler 与 Platform Health 分开建模和派遣。
- PF-09 使用一个阶段管理会话，Dashboard 与 Report 共享数据集契约但保持产品边界。
- PF-10 先创建 `OperationsCenter.Service` 并独立交付 ServerMonitor；PF-10A 才向同一宿主加入项目知识、问题与助手模块。
- Operations Center、Label 与 IoT Collector 不互为领域数据依赖；排期上 PF-10B 的独立标签/设备闭环先于 PF-11。不得把 PF-10A 的知识闭环未完成误报为已设计。
- PF-06A 在 PF-05/06 三端 Web 验收后实施原生包；PF-10B 在 IoTCollector 前实施标签服务和 Device Agent，既有 PF-07～PF-11 不重编号。

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
待启动 → 设计中 → 待前置核验 → 待派遣（已就绪） → 派遣中 → 待验收 → 已完成
```

发现跨阶段冲突时：

```text
任意状态 → 设计待确认
```

任务状态继续使用实施规范：

```text
设计就绪度：待细化 → 待前置核验 → 已就绪
派遣状态：待派遣 → 已派遣 → 开发中 → 待验收 → 已完成
```

# 7. PF-00 Identity

**状态：** 当前范围已完成
**现有实施文档：** `docs/implementation/03-Industrial Platform Identity Service开发实施方案.md`
**目标：** 完成用户、角色、权限、本地登录、令牌、会话、企业 SSO 边界和三端真实登录闭环。
**当前进度：** `TASK-ID-001～023` 已完成并合入 `develop`。PF-00 本地门禁包括后端 Release 构建、Identity 五工程测试、SystemData Contract 测试以及前端 typecheck/lint/unit/build；真实 PostgreSQL/Redis 联合登录 E2E 仍作为外部验收项保留。
**前置：** BuildingBlocks、可运行基线、统一前端第一批。
**禁止范围：** SystemData 行政组织、菜单所有权、聊天、业务数据权限模型扩张。

**完成门禁：**

- `TASK-ID-001～016` 保留历史完成记录，`TASK-ID-017～023` 有完整新鲜执行记录；
- 登录、刷新、注销、撤销、401、403、菜单/按钮权限契约和关键 E2E 通过；
- 前端切换真实 `HttpAuthGateway`；
- 输出 PF-01/02/03 可消费的用户、权限、会话和身份上下文契约；
- Docker 缺失造成的外部验收项明确保留为待验收，不伪报完成。

**后续：** 当前补强范围不再派遣；后续契约变更另立范围。

**证据：** `docs/evidence/PF-00.md`；集成提交 `9f48d89`，状态回写提交 `8dc8b91`。

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

**状态：** 收束验收中（2026-09-03）；`TASK-SD-001～010` 已完成，011～012 待收束，014/016 已交付并关闭已报告缺陷，015 真实矩阵受环境限制，017 已回写证据，013/PF-02 未关闭；不自动进入 PF-03
**Service Host：** 创建 `SystemData.Service`；本阶段只交付 SystemData 模块。
**建议会话标题：** `PF-02 SystemData阶段管理`
**输入：** 蓝图 05、07、13、20、23、27、30、31、32、33；PF-00 身份契约；PF-01 页面规范；PostgreSQL 18 与当前 `deploy/cloud-dev` 最小引导现状。
**目标：** SystemData 只负责 Topology、Orchestration、Policy 与脱敏 Observation；各服务负责自己的 Migration、Seed、Bootstrap、Verify、Ledger 和本地 readiness。现有行政组织、岗位、任职范围保持不变，后续菜单导航、功能开关、服务目录和主题默认值在整改完成后复核。
**依赖：** PF-00；页面依赖 PF-01。
**禁止范围：** 制造组织、字典参数、物料设备、租户运营后台。

**设计会话必须解决：**

- **当前结果：** 001～010 已完成；011～012 已完成功能开发，但 runtime、页面可用性及真实业务操作仍需收束验收；013 重新打开，统筹 014～017 的阶段关闭门禁。

- 行政组织树、岗位和任职关系的不变量；
- 菜单、路由、按钮资源与 Identity 权限的所有权和同步；
- PC/PDA/Mobile 可见性和功能开关覆盖层级；
- 服务目录、所有者、入口和健康地址；
- 默认租户与用户主题覆盖规则；
- 数据迁移、缓存、事件、审计和前端页面。
- `ServiceKey + ModuleKey`、InitializationManifest/SeedSets、dry-run/plan、异步 initialize/apply、Operation 状态和受信查询 API；
- `SystemBaseline/TenantBaseline/EnvironmentSample/SecretBootstrap` 四类种子、RequiredForReadiness、环境允许列表与依赖图；
- 每个服务或独立持久化初始化单元自有 migration/seed ledger、checksum drift、DataPatch 和管理员维护数据保护；逻辑模块不机械拆分账本；
- SystemData 普通连接与 provisioning 管理凭据隔离，最小业务角色、审批、备份、审计、脱敏、限流和幂等；
- PostgreSQL advisory lock、迁移/种子历史、失败恢复、expand/contract、禁止多副本重复初始化和 readiness 门禁；
- Development/Test 必要迁移/种子自动策略、EnvironmentSample 显式启用、生产 `plan → 审批 → 备份 → apply` 且禁止启动时播种，以及 SQLite 显式迁移/双账本语义；
- 服务自有签名 migration/seed/initializer 产物与 Secret Provider；SystemData 不理解业务表、不直写 Repository、不接收或透传 Secret 值；
- SystemData 自身数据库由基础设施最小引导的 bootstrap 例外，不得形成调用自身 API 的循环依赖。

**已交付：** 当前 Git 已包含 SystemData 骨架、Runner、组织/岗位/任职 API，资源导航、Feature、服务目录、主题策略、缓存、审计、Outbox、Identity 对账，以及运行端和七个 PC 管理页的功能代码；已有 build、单元/契约测试、Mock E2E、七页可达性和真实组织/岗位 CRUD 证据继续有效。
**收束门禁：** 完成七页页面一致性与关键状态验收、真实业务浏览器矩阵、Service Initialization V2 Advanced 闭环和蓝图 33 §12 十三项门禁证据矩阵；逐项记录真实/夹具证据、报告/截图路径和限制。全部 P0 项关闭前 PF-02 不得标记“已完成”。

# 9A. 架构收敛整改（不新增 PF 编号）

**状态：** 执行中；完成后直接继续 PF-03。

**唯一实施计划：** `docs/superpowers/plans/2026-08-20-industrial-platform-architecture-consolidation.md`

内部顺序固定为四个工作包，不增加子计划或额外 PF：

1. 对齐蓝图与开发 Todo。
2. 补齐可接手架构文档与代码交接。
3. 收敛测试项目与门禁。
4. 对齐当前服务初始化与 readiness。

冻结规则为：SystemData = Topology + Orchestration + Policy + Observation；Service = Migration + Seed + Bootstrap + Verify + Ledger；runtime readiness 只取本地核心数据库事实。Gateway 与 UnifiedHost 分别承担分布式反向代理和统一进程组合宿主，不互相替代。ReferenceData 保持一个宿主、七个逻辑模块、一个 `reference_data` Schema、模块表前缀、一个服务级 Migration/Ledger 和一个带 `ModuleKey` 的服务级 Outbox；没有真实入站事件消费者时不预建 Inbox/Checkpoint。

# 10. PF-03 ReferenceData

**状态：** 已完成并合入 `develop`（2026-09-05，独立验收 PASS）
**Service Host：** 继续利用现有 `ReferenceData.Service` 骨架；内部模块为 Dictionary、Parameter、Metadata、DynamicProperty、CodingRule、StateMachine、UnitOfMeasure。
**建议会话标题：** `PF-03 ReferenceData阶段管理`
**现有实施文档：** `docs/implementation/06-Industrial Platform ReferenceData Service开发实施方案.md`
**输入：** 蓝图 05、07、08、21、26、27、31；现有 06 实施方案；PF-00/01/02 契约。
**目标：** 在一个 `ReferenceData.Service` 内按 Dictionary、Parameter、DynamicProperty、UnitOfMeasure、Metadata、CodingRule、StateMachine 七个纵向切片顺序交付，复用一个数据库 Schema、服务级迁移/Outbox 与现有前端平台，不按模块复制微服务治理设施。
**依赖：** PF-00；与 PF-02 对菜单、主题默认值和系统参数所有权达成明确契约。
**禁止范围：** SystemData、MasterData 业务实体、业务当前状态/事务、物料专属换算、Low Code 运行时与通用工作流/脚本引擎。

**文档收敛结果：**

- 原五模块保留，新增 StateMachine 与 UnitOfMeasure；实施 06 V2.7 调整为 `TASK-RD-001～010` 十个顺序步骤，计量单位先于 Metadata，仍不独立派遣或提交；
- StateMachine 只拥有定义、版本和路径合法性，业务实例/权限/前置条件/事务仍归业务服务；通用单位与换算由 ReferenceData 唯一维护，MasterData 保留物料单位选用及专属比例；
- SystemData 与 ReferenceData 参数所有权边界保持不变；
- 七模块领域、应用、基础设施、API、权限、PC 页面、统一初始化/readiness、缓存与服务级 Outbox 已完成；
- 每个模块按后端、前端和测试同一步纵向交付并复用 PF-01 平台能力；
- 依赖、状态、任务卡和验收证据已同步到实施 06。

**完成门禁：** 七类能力纵向交付并连接真实 Gateway；缓存、Outbox、审计、页面、契约和 E2E 完成；固定定义修订/单位换算/业务事务失败不推进状态有验证；不越界实现业务实体属性值或业务状态执行。

# 11. PF-04 File / Notification / Audit

**状态：** 2026-09-06 完成开发就绪复评并整包派遣 PF-04 Core（001～009）；截至 2026-09-07，Core 已提交 `8625efb`，真实验收待补。功能开发任务 `01a076d2-a8d3-7163-8721-8cae51393d92`，独立验收任务 `01a076d3-3abf-7921-a367-9b70749d0780` 保持不变。实施入口：[07 V1.1](../implementation/07-Industrial%20Platform%20File%20Notification%20Audit开发实施方案.md)。010 为后续增强待细化。

**Service Host：** 三个逻辑模块加入现有 SystemData.Service，沿用服务级迁移/账本与可靠设施，数据命名空间/公开契约独立；不新增三个 Host、四 Unit 硬前置或重复 PF-02 数据库治理。当前 PF-02 仍 active，PF-03 已完成合入；前置按所消费的具体契约和证据核验，不沿用旧“全部尚不存在”。

## 11.1 Audit Core

**输入：** 蓝图 05 第 7.3 节、30 第 8 章、31；PF-00 身份与现有服务本地审计/Outbox。
**目标：** 可信统一事件、脱敏、追加事实、幂等冲突、可靠恢复、授权查询、基础保留与必要导出/访问审计。
**完成门禁：** 成功关键变更与本地 Audit Outbox 原子提交；失败/拒绝/回滚经独立可靠路径记录；中央不可用后补投，同键不同内容冲突；跨租户与敏感字段不泄漏。Identity、SystemData 和另一平台服务代表动作通过增量适配接入，保留原本地审计/历史。事实与可变生命周期分离，Core 导出不依赖 File，避免循环依赖。

## 11.2 File 核心

**输入：** 蓝图 05/20/27/31；Audit Core、可信身份/用途授权；单条上传路线候选及存储/扫描条件。
**目标：** 手机中断后电脑重新选择同二进制文件，发现本人有权会话、强确认、接管并从服务端持久断点续传；隔离/校验/扫描、授权下载、引用保护和延迟删除。
**完成门禁：** 指纹只找候选，完整 SHA-256 与授权分离；首期写入前登记预期完整 hash，无 hash 旧会话拒绝续传；同名不同内容/采样相同但未采样区不同不混传；服务重启恢复、双设备在途写入与接管互斥、过期/取消/重复完成均验证。扫描失败/未知不可用，受保护文件禁止下载，引用/保留阻止物理删除。
**选型边界：** tusdotnet + tus-js-client 为优先候选，版本/许可/持久 store/双入口/多实例仍待实际集成验证；不同时建设 Multipart 或重复 upload_part 状态机。全局秒传、跨用户去重和复杂复扫后置。

## 11.3 Notification 核心

**输入：** 蓝图 05；PF-00/01/02 公开契约与 Audit Core。
**目标：** Announcement、NotificationMessage、InboxDelivery 三个核心模型；模板简单配置，发布受众固化且容量受限，持久收件箱、已读/撤回/失效/未读数和安全跳转。
**完成门禁：** 离线不丢、重复投递不重复记录；电脑已读后手机重连能同步旧记录；撤回/过期与列表、未读数一致。SignalR 只提示，重连/前台/变更提示后刷新当前列表和计数，丢失时有界刷新收敛。打开业务目标继续检查当前权限，已读不代表签收或业务完成，不引入聊天/复杂增量回放。

## 11.4 内部任务、后续增强与验收

内部顺序：001 宿主/契约接入 → 002 Audit Core →（003 上传会话/跨设备续传 → 009 File 安全生命周期；004 Notification）→ 005 联合契约/生产者接入 → 006 三端页面及 007 安全恢复 → 008 阶段验收。模块契约在 001/对应模块任务先定义，005 做联合验证。

保留旧 TASK-PF04-001～008 并调整范围；原 003 的安全/下载/引用/清理拆至新增 009，原 002/007 的高级合规移至新增 010。九字段卡、旧 ID 映射和 F1～F7/N1～N4/A1～A5/U1 验收矩阵只在实施 07 第 12～14 章维护，不新建平行任务体系；整个 PF 才是派遣单位，内部步骤连续执行，不逐卡派遣/提交。

Audit Advanced（010）包含哈希链、签名 checkpoint、外部锚点、完整 Legal Hold 审批、解密和复杂合规导出，按明确需求再决策，不是 Core 验收或 File/Notification 前置。蓝图 05 第 8.1 节已确定的扫描、保留和法律保全要求继续有效，PF-05 启用相关内容前落实保全保护，不能因审批 UI 后置而允许删除。

001～009 的设计与九字段说明已具备开发条件。组件/扫描/存储/服务身份等事项按实施 07 第 1.3、17 章分别纳入任务内核验和功能验收，不作为整个 Core 的设计阻塞。2026-09-06 已落实专用工作线、开发/验收负责人和共享文件边界；原执行安排从 001 按依赖连续执行，003 内先做最小技术验证。截至 2026-09-07，已有 Core 提交与历史自测记录，仍缺真实浏览器、ClamAV、外部中间件和多实例等验收证据；提交不代表功能验收完成。

# 12. PF-05 Collaboration

**状态：** 2026-09-07 已补充 V1.2 字段/接口/线框图规格；任务待派遣、就绪度待前置核验，未实施。
**实施文档：** [实施 08](../implementation/08-Industrial%20Platform%20Collaboration开发实施方案.md)。
**Service Host：** 一个 Collaboration.Service，Messaging/Presence/AttachmentIntegration 分责；默认服务级初始化，Presence 不建空账本。
**目标：** 平台原生优先、外部 MES 可信身份嵌入；三端 Web 一对一文本/图片/文件、在线、已读未读、撤回、隐藏恢复与可靠同步。
**依赖：** 身份/目录与必要持久审计先支持文本；File 按实际契约后接，完整真实集成仍验收。PF-04 代码存在不代表所有消费契约已稳定。
**内部顺序：** 001 两模式宿主/身份 → 002 文本用例与 005/006 对应 API/页面 → 003 在线/可靠同步 → 004 附件与 005/006 三端联动 → 007 基础及平台已批准合规 → 008 两模式完整验收。
**关键修订：** 已提交 Sequence 连续且失败回滚不占号；新消息补拉与旧窗口状态刷新分开；MessageStateVersion 合并；发布前安全投影；Messaging 自有附件绑定；已读原子 max；Presence 20/60/15 秒语义明确并展示 Unknown。
**完成门禁：** 平台登录未打开聊天也接收提醒、抽屉/页签共享状态；外部不启动整套平台完成参考宿主认证/目录/退出/裁剪闭环；三端 Web、多设备、故障恢复、保留/合规与已批准 2C4G 门禁。客户/真机缺口如实记录。
**边界：** 不引入完整 IM 引擎，不做群聊/会议/独立人员主数据，不提前打包；平台已批准合规增强未决定延期前仍保留为未完成范围。

# 13. PF-06 RemoteAssistance

**状态：** 详细设计已确认，PoC 决策门禁待派遣
**Service Host：** RemoteAssistance 作为独立内部模块加入 `Collaboration.Service`，并保留未来物理拆分能力。
**建议会话标题：** `PF-06 RemoteAssistance阶段管理`
**实施文档：** `docs/implementation/09-Industrial Platform RemoteAssistance开发实施方案.md`
**输入：** 蓝图 05；PF-05 会话契约；Screego 官方仓库和部署配置。
**目标：** 先验证现场网络中的 WebRTC 屏幕共享，再决定 Screego 适配或自研轻量信令路线。
**依赖：** 技术 PoC 只需实验网络/浏览器/证书等；产品集成依赖 PF-05 真实契约、可信身份与必要 Audit；客户上线另需现场证据，详见实施 09 §1.4。
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
**产品完成门禁：** 三端 Web 从聊天发起、逐人邀请/授权/拒绝、共享、离开/终止和元数据审计闭环；第 2/3 人无原聊天历史权限；数据库票据唯一消费、响应丢失补发、真实撤权断流及强制 TURN 通过。外部嵌入复用宿主适配，协助故障不影响聊天；不含原生安装包。

# 13A. PF-06A 终端运行时与客户端打包

**状态：** 核心字段/接口/页面线框图已细化；待派遣、待实际目标核验，未实施。
**设计/实施：** [蓝图 34](34-Industrial%20Platform终端运行时与客户端架构.md)、[实施 09A](../implementation/09A-Industrial%20Platform终端运行时与客户端打包开发实施方案.md)。
**前置/顺序：** PF-05/PF-06 Web 完成后、PF-07 前；不占用已有 PF-07 编号。
**目标：** 同一 Vue3 业务层 → Web/Electron Windows/Capacitor Android PDA；扫码/相机/BLE、受限桥接、协作生命周期、完整升级与 PDA Bundle 热更新。
**任务：** TASK-PF06A-001～008：能力矩阵→Runtime→PC/PDA容器→更新→协作→设备交接→真机验收。
**完成门禁：** 一台真实 PDA、一种广播、相机扫码、一种 BLE；PC/Bundle/APK 各一次升级、兼容/签名/回退与业务保护；原生聊天/共享/换人恢复。Mobile 原生平台范围在 001 明确，未知不当完成。
**边界：** 不重写消息核心、不创建标签队列、不做全品牌/MDM/永久后台；具体版本/许可/硬件与发布环境必须核验。

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

# 17B. PF-10B 标签管理平台

**状态：** 核心数据/接口/页面线框已细化，待派遣/待客户设备与独立适配核验，未实施。
**设计/实施：** [蓝图 35](35-Industrial%20Platform标签管理平台设计.md)、[实施 13B](../implementation/13B-Industrial%20Platform标签管理平台开发实施方案.md)。
**前置/顺序：** PF-06A 提供容器能力；排在 PF-10A 后、PF-11 IoTCollector 前，并先于正式 MES 业务。外部项目数据契约可独立接入，不等待 MasterData 开发。
**宿主：** 独立 Label.Service + 必要数据库/文件存储 + 按需 Device Agent；完全独立、外部 MES 接入、平台集成三模式。Agent 为辅助部署单元，Label 是新增第八个规划核心 Host。
**任务：** TASK-PF10B-001～010：样本/设备→独立基础→数据契约/绑定/快照→模板→任务调度→Windows/PDA执行→页面/客户接入→恢复试点→验收。
**完成门禁：** 物料/容器/设备及自定义模板；底稿/指令；客户保密映射、明细/份数、固定预览快照；真实 Windows 工位与 PDA 蓝牙直连；历史/受控重打、未知结果不自动重打、恢复对账、一个真实项目交付。
**边界：** 不宣称全打印机兼容，候选组件未定；不让称量/IoT 依赖标签任务，不建设通用规则引擎/完整离线业务。

# 18. PF-11 IoT Collector

**状态：** 待启动
**Service Host：** 创建 `IoTCollector.Service`；内部模块为 Driver、DeviceConnection、Point、CollectionTask、EdgeManagement。
**建议会话标题：** `PF-11 IoT Collector阶段管理`
**输入：** 蓝图 05、08、17、20、30；PF-07/10 可观测契约。
**执行前置：** 按路线先完成 PF-10B 标签平台；消费其设备连接/诊断公开语义，不依赖 Label 任务表。原 PF-11 编号保持。
**目标：** 复核并实施驱动、连接、点位、采集任务、边缘缓存、断线续传和数据质量。
**边界：** 不承担 MasterData、报表和 MES 规则；设备业务档案仍归 MasterData。
**完成门禁：** 选择一个首期协议完成连接、采集、缓存、断线恢复、幂等写入和监控闭环；其余协议按适配器任务追加。

# 19. MES 阶段恢复条件

## 19.1 MES-01 MasterData

**状态：** 暂缓
**现有实施文档：** `docs/implementation/15-Industrial Platform MasterData Service开发实施方案.md`
**恢复门禁：** 按第 4 章先收束平台基础至 PF-11，包含 PF-06A 终端化和 PF-10B 标签平台；未经新的排期决定不提前进入 MES；必须新开会话复核现有设计与 SystemData、ReferenceData、File、Audit 和主题契约。

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
| PF-00 Identity | 当前范围已完成 | PF-00 固定工作线 | 蓝图 13、31、33 | 实施 03 | TASK-ID-001～023 已完成；PF-00 集成提交 `9f48d89` | `docs/evidence/PF-00.md`；本地门禁全绿，真实 PostgreSQL/Redis 联合链路为外部验收项 |
| PF-01 视觉主题 | 已完成（外部真机项待验收） | 现有 PF-01 会话继续 | 已批准 PF-01 规格 | `docs/implementation/04-Industrial Platform视觉主题与平台外壳开发实施方案.md` | 设计提交 `e2d24a4`、`d7ef889`、`efb3b35`；开发未提交(按协作约定) | TASK-PF01-001～007 完成；静态门禁全绿、mock E2E 102/102、真实 Identity E2E 19/19 |
| PF-02 SystemData | 收束验收中 / 真实矩阵受环境限制 | PF-02 主工作区顺序交接 | 蓝图 05、07、33 V3.1 | `docs/implementation/05-Industrial Platform SystemData开发实施方案.md` | 001～010 已完成；011～012 待收束；014/016 已交付并关闭已报告缺陷；015 待真实验收；017 已回写；013 未关闭 | `docs/evidence/PF-02.md` 第五轮；后端1378通过/3跳过、独立探针8项达到预期；七页/三端/十三门禁真实矩阵仍待验收，不进入 PF-03 |
| 架构收敛整改 | 已完成 | 当前计划 | 已批准整改设计 | 已批准四工作包计划 | WP1～WP4 已完成 | 结果已纳入当前架构基线 |
| PF-03 ReferenceData | 已完成并合入 | 开发/验收任务已归档；原专用工作树与分支已清理 | 蓝图 07、21、26、32、33；七模块与单位/状态机所有权已确认 | 实施 06 V2.7；`docs/tasks/archive/PF-03.md` | `969ee156`；合并 `e9452b47` | `docs/evidence/PF-03.md`；独立验收 PASS，主工作树门禁通过 |
| PF-04 File / Notification / Audit | Core 已提交，真实验收待补 | 原开发/验收任务保留 | 蓝图 05/26/30/32/33 | 实施 07 V1.1 | `8625efb`，001～009 已有实现；010 后续待细化 | `docs/evidence/PF-04.md` 历史自测；真实浏览器/ClamAV/中间件/多实例仍待验收 |
| PF-05 Collaboration | V1.2 规格细化，待派遣/待前置核验 | 未新增任务 | 蓝图 04/05/32/33 | [实施 08](../implementation/08-Industrial%20Platform%20Collaboration开发实施方案.md) | 001～008 待派遣 | 仅文档检查，无功能验收 |
| PF-06 RemoteAssistance | 详细设计已确认，PoC 门禁待派遣 | 当前 PF-06 阶段管理会话 | 蓝图 05、32、33；Screego/W3C 官方证据 | `docs/implementation/09-Industrial Platform RemoteAssistance开发实施方案.md` | 推荐平台原生控制面/最小信令，Screego 仅未修改基准 PoC | TASK-PF06-001 待另行派遣；002～008 门禁阻塞，未开发/未测试 |
| PF-06A 终端运行时与客户端打包 | 待派遣/待设备核验 | 未新增任务 | 蓝图 34 | [实施 09A](../implementation/09A-Industrial%20Platform终端运行时与客户端打包开发实施方案.md) | 001～008 未派遣 | 无原生/真机验收 |
| PF-07 Scheduler / Platform Health | 待启动 | 待创建 | 蓝图 05、30 | 实施 10 待创建 | - | - |
| PF-08 Low Code | 待启动 | 待创建 | 蓝图 21 待复核 | 实施 11 待创建 | - | - |
| PF-09 Dashboard & Report | 待启动 | 待创建 | 蓝图 22 待复核 | 实施 12 待创建 | - | - |
| PF-10 ServerMonitor | 待启动 | 待创建 | 蓝图 02、32 待复核 | 实施 13 待创建 | - | - |
| PF-10A Operations Center Knowledge & Assistant | 设计待确认 | 待创建 | 蓝图 32 第 5.4 节 | 实施 13A 待创建 | - | - |
| PF-10B 标签管理平台 | 待派遣/待客户设备核验 | 未新增任务 | 蓝图 35 | [实施 13B](../implementation/13B-Industrial%20Platform标签管理平台开发实施方案.md) | 001～010 未派遣 | 无客户/设备验收 |
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
- 在 PF-03 前插入一个不新增 PF 编号的“架构收敛整改”阶段，仅按已批准计划执行四个工作包；不增加额外设计范围。

## 23.1 2026-09-07 路线整改

PF-05/06 本次只改设计和九字段任务；新增 PF-06A/09A 与 PF-10B/13B，不重编号已有阶段。需求—设计—任务—验收映射、来源、变更分类与证据缺口见 `docs/evidence/2026-09-07-platform-roadmap-docs.md`。原八张父卡分别保留，内部纵向步骤连续执行，任务排期不是本轮开发/派遣授权。


## PF05起任务细化与派遣输入（2026-09-07增量）

统一遵守[派遣前详细设计与页面验收](../implementation/STANDARD-派遣前详细设计与页面验收.md)。PF05/PF06/PF06A/PF10B的完整字段字典、接口样例、页面线框与任务断言已经写入各实施方案链接的details文件；[待派遣索引](../tasks/pending/README.md)记录待派遣包与前置缺口。生产范围全部D01～D08已就绪后才能派遣，不能在开发任务中临时设计字段或布局。原34张内部卡保持，按已定规格纵向连续实现，不逐卡新增会话或提交。

尚无实施方案的PF07/08/09/10/10A/11按共同规则§7逐阶段补齐全部设计后才进入待派遣；MES原暂缓不变。本轮未创建或发送外部任务，PF02/04现有工作线不受此文档更新影响。
