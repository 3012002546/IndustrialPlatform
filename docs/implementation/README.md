# Industrial Platform 实施文档索引

本目录是 Industrial Platform 开发实施方案和可派遣开发 TODO 的唯一维护源。蓝图决策来自 `docs/blueprint`；实际开发在其他任务或被派遣的协作任务中完成，当前任务只负责拆分、派遣、跟踪、验收信息汇总和结果回写。

总体开发优先级和阶段门禁统一读取 `docs/blueprint/09-Industrial Platform开发总TodoList.md`。PF-01～PF-10 对应本目录 04～13；新增 PF-06A 使用 09A，PF-10A 使用 13A，新增 PF-10B 使用 13B，PF-11 使用 14；MES 文档从 15 开始。后缀编号插入执行顺序，不改旧编号。

所有PF阶段还必须读取 `docs/blueprint/32-Industrial Platform Service Host与内部模块边界.md`。阶段编号不等于Service Host；同宿主模块可共用物理Schema和服务级迁移/账本，但必须保留明确模块表命名空间、契约、权限与测试边界，不得跨模块直读Repository。只有独立持久化生命周期才拆初始化单元。平台原生优先、最小依赖与外部宿主装配前置见该蓝图§2.1～2.2。

PF-02 及其后的新服务必须以 `docs/blueprint/07-PostgreSQL数据库规范及分库设计.md` 与蓝图 33 为权威初始化来源，并以实施 05 为 PF-02 控制面计划；引用文件与章节，不绑定文档修订号。SystemData 提供通用 Service Initialization Pipeline；后续服务必须交付 ServiceKey/ModuleKey、InitializationManifest/SeedSets、服务自有 migration/seed/initializer 产物、服务级或真实独立持久化单元的 schema/seed 双账本、本地 readiness、最小角色、备份登记和可观测 `OperationId`。不得自行持有管理员凭据建库、向 SystemData 传 Secret、使用 `EnsureCreated`，或让共享宿主使用模糊初始化大包。

## 协作边界

PF05起分开维护设计就绪度（待细化/待前置核验/已就绪）与派遣状态（待派遣→已派遣→开发中→待验收→已完成）。只有本次批准范围全部就绪才派遣；“待派遣”不代表前置已通过。设计冲突回总控补齐规格后再执行，不能把细化留给编码任务。

所有新增或重构的实施方案必须从 [`TEMPLATE-开发实施方案.md`](TEMPLATE-开发实施方案.md) 开始，并保持“详细设计正文 → 任务依赖 → 九字段任务卡 → 完成标准 → 执行记录 → 下一阶段输入契约”的顺序。每个 PF 阶段只保留一个阶段管理会话；该会话负责详细设计、任务派遣、跟踪、验收和结果回写，实际编码由被派遣任务执行。

每个可派遣任务或阶段内步骤统一包含：状态、目标、输入文档、依赖、允许修改范围、预期输出、验证与证据、结果回写、提交策略。提交策略服从仓库当前执行协议；阶段整体派遣时，内部步骤只需独立验证和回写，不机械拆成独立派遣或提交。不得只写实现摘要或笼统的“测试通过”。

## PF05起派遣前必读

- [详细设计、线框图与验收规则](STANDARD-派遣前详细设计与页面验收.md)：字段/状态/契约/样例/页面/协作，含所有后续PF/MES的细化门禁。
- [PF05细化规格](details/PF05-数据接口与页面规格.md)、[PF06细化规格](details/PF06-数据接口与页面规格.md)、[PF06A细化规格](details/PF06A-运行时字段与页面规格.md)、[PF10B细化规格](details/PF10B-数据接口与页面规格.md)：具体数据字典、接口和已绘线框图。
- [待派遣工作包索引](../tasks/pending/README.md)：只记录待派遣范围和就绪缺口，不是已发出的任务。后续实施10/11/12/13/13A/14未创建，必须先按共同规则完成阶段详细设计，不直接派编码。

## 时间类型约束

- .NET 时间值使用 `DateTimeOffset` / `DateTimeOffset?`，当前 UTC 时间使用 `DateTimeOffset.UtcNow`。
- API 时间使用包含 `Z` 或明确偏移量的 ISO 8601 / RFC 3339 格式。
- PostgreSQL 瞬时时间使用 `timestamp with time zone`（`timestamptz`），以 UTC 保存。
- 本目录后续新增或修订的实施方案不得把 `DateTime` 用作业务时间类型。

| 编号 | 文档 | 当前范围 |
| --- | --- | --- |
| TEMPLATE | [统一开发实施方案模板](TEMPLATE-开发实施方案.md) | 后续开发设计和任务派遣的强制母版 |
| 01 | [启动实施方案](01-Industrial%20Platform开发启动实施方案.md) | 总体路线：可运行基线优先 |
| 02 | [BuildingBlocks基础组件实施方案](02-Industrial%20Platform%20BuildingBlocks基础组件开发实施方案.md) | 原基础搭建已完成；`TASK-BB-010` Entity 生命周期/并发/软删除调整已完成 |
| 02A | [可运行基线开发 TODO](02A-Industrial%20Platform可运行基线开发实施方案.md) | `TASK-BASE-001～006` 已完成（`BASE-002/003` 真实依赖与容器验收留待 Docker 环境，`BASE-005/006` 无 Docker 实测通过）；Phase 2 前端输入契约已登记 |
| 02B | [统一前端第一批开发 TODO](02B-Industrial%20Platform统一前端第一批开发实施方案.md) | `TASK-FE-001～010` 已完成 |
| 03 | [Identity Service实施方案](03-Industrial%20Platform%20Identity%20Service开发实施方案.md) | `TASK-ID-001～023` 当前范围已完成并合入；真实 PostgreSQL/Redis 联合登录链路保留为外部验收项 |
| 04 | [PF-01 视觉主题与平台外壳实施方案](04-Industrial%20Platform视觉主题与平台外壳开发实施方案.md) | `TASK-PF01-001～007` 已完成；真实 Identity 联合验收 real E2E 19/19；外部真机 safe-area 待验收 |
| 05 | [PF-02 SystemData实施方案](05-Industrial%20Platform%20SystemData开发实施方案.md) | 已有控制面及管理能力，PF-02 仍 active；真实菜单发布、七页/三端及外部门禁按 CURRENT/阶段 evidence 收束。PF-04 只核验所消费的稳定契约，不重做已完成前置 |
| 06 | [PF-03 ReferenceData实施方案](06-Industrial%20Platform%20ReferenceData%20Service开发实施方案.md) | 七模块已于 2026-09-05 完成并经独立验收 PASS，当前后续 WIP 不包含在历史验收内，证据见 docs/evidence/PF-03.md |
| 07 | [PF-04 File / Notification / Audit实施方案](07-Industrial%20Platform%20File%20Notification%20Audit开发实施方案.md) | Core已交付；历史自测及真实环境缺口见 PF-04 evidence，不能仅凭提交标记整阶段验收通过 |
| 08 | [PF-05 Collaboration实施方案](08-Industrial%20Platform%20Collaboration开发实施方案.md) | 字段/接口/线框图已细化；001～008 待派遣、待前置核验 |
| 09 | [PF-06 RemoteAssistance实施方案](09-Industrial%20Platform%20RemoteAssistance开发实施方案.md) | 字段/接口/线框已细化；双PoC待派遣，生产002～008保持门禁阻塞；未开发、未测试 |
| 09A | [PF-06A 终端运行时与客户端打包](09A-Industrial%20Platform终端运行时与客户端打包开发实施方案.md) | PF-05/06 完成后执行；共用字段/线框已细化，待派遣/待目标设备核验，无原生工程 |
| 10 | PF-07 Scheduler / Platform Health实施方案（待 PF-07 会话创建） | 两个模块分开建模，在同一阶段管理会话协调和派遣 |
| 11 | PF-08 Low Code实施方案（待 PF-08 会话创建） | 待阶段管理会话设计和派遣 |
| 12 | PF-09 Dashboard & Report实施方案（待 PF-09 会话创建） | 两个产品边界分开建模，在同一阶段管理会话协调和派遣 |
| 13 | PF-10 ServerMonitor实施方案（待 PF-10 会话创建） | 创建 `OperationsCenter.Service`，本阶段只处理 ServerMonitor |
| 13A | PF-10A Operations Center Knowledge & Assistant实施方案（待 PF-10A 会话创建） | 设计待确认；先补齐 IssueTracking 与 KnowledgeBase 完整数据闭环，不提前创建文档 |
| 13B | [PF-10B 标签管理平台](13B-Industrial%20Platform标签管理平台开发实施方案.md) | PF-11前；先平台原生兼容/打印闭环，再外部MES与无宿主装配；核心字段/线框已细化，待派遣/待平台契约、客户设备及外部适配核验 |
| 14 | PF-11 IoT Collector实施方案（待 PF-11 会话创建） | 待阶段管理会话复核蓝图并派遣 |
| 15 | [MES-01 MasterData实施方案](15-Industrial%20Platform%20MasterData%20Service开发实施方案.md) | 暂缓；保留现有设计和未提交调整，恢复前复核 |
| 16 | [MES-02 OperationalData实施方案](16-Industrial%20Platform%20OperationalData%20Service开发实施方案.md) | 暂缓；恢复前按母版重构和复核 |

## 当前开发顺序

```text
BuildingBlocks / 可运行基线 / 统一前端第一批（已完成，Docker 实机项除外）
→ PF-00 Identity（TASK-ID-001～023 当前范围已完成）
→ PF-01 视觉、主题与平台外壳（TASK-PF01-001～007 已完成）
→ PF-02 SystemData + PF-03 ReferenceData
→ PF-04 File / Notification / Audit
→ PF-05 Collaboration
→ PF-06 RemoteAssistance
→ PF-06A 终端运行时与客户端打包
→ PF-07 Scheduler / Platform Health
→ PF-08 Low Code
→ PF-09 Dashboard / Report
→ PF-10 ServerMonitor
→ PF-10A Operations Center Knowledge & Assistant（设计待确认）
→ PF-10B 标签管理平台
→ PF-11 IoT Collector
→ MES-01 MasterData
→ MES-02 OperationalData
→ MES生产闭环
```

BuildingBlocks 原基础搭建和 Entity 调整不重复派遣。详细任务状态在对应实施文档中维护；`docs/blueprint/09-Industrial Platform开发总TodoList.md` 只维护总体阶段、门禁、阶段管理会话入口和结果索引。

每个 PF 阶段（包括 PF-10A）只开一个阶段管理会话。该会话根据蓝图、母版、项目记忆和当前代码反复完善详细设计，确认后直接在对应编号实施文档中生成九字段任务卡并负责派遣、跟踪和验收，不再拆成规格会话、计划会话和开发会话。被派遣任务继续按“后端用例与契约 → 对应页面 → 契约测试与关键路径 E2E → 阶段验收”纵向交付。
