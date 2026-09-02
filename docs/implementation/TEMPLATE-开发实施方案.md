# NN-Industrial Platform【模块或服务】开发实施方案

# Industrial Platform【模块或服务】开发实施方案

> 当前里程碑范围：【一句话说明本阶段交付内容和明确暂缓内容】

版本：V1.0

阶段：【Phase/Sprint/里程碑及前后阶段关系】

模块或服务：

```text
【模块/服务正式名称】
```

Service Host 与内部模块：

```text
【读取蓝图 32，列出本阶段创建/扩展的 Service Host 和本阶段内部模块；明确阶段不等于 Service Host】
```

服务初始化与环境引导：

```text
【PF-02 及后续新服务读取蓝图 07、33；先区分 Service Host、Domain Module、Initialization Unit、Deployment Unit，再列出 ServiceKey、InitializationUnitKey、Provider、LogicalDatabaseName、表前缀、服务自有 Migration/Seed/Bootstrap/Verify/Ledger、DesiredVersion、Owner、DatabaseTopology 和 Standard|Advanced 策略。SystemData 只负责 Topology、Orchestration、Policy、Observation，通过进程内或受信 HTTP 端口调用服务初始化器；服务不得向 SystemData 传 SQL、路径、命令或 Secret。只有独立持久化生命周期才拆分初始化单元；普通功能默认 Standard，审批、备份证据、签名和漂移恢复仅在 Advanced 或环境强制时要求。runtime readiness 只读取本地数据库事实，不依赖 SystemData 在线；不适用时说明原因】
```

技术：

```text
【主要框架、语言、数据库、中间件和前端技术】
```

规格与蓝图依据：

- `docs/blueprint/【真实存在的文件】.md`
- `docs/superpowers/specs/【已批准规格；没有时删除本行并说明设计来源】.md`
- `docs/implementation/【前置实施方案】.md`

---

# 1. 文档说明

## 1.1 文档目的

说明本实施方案解决的问题、目标读者，以及本文同时承担“开发详细设计”和“任务派遣唯一维护源”的职责。

## 1.2 当前输入状态

- 当前代码、项目骨架、数据库、前端和基础设施的真实状态。
- 已完成能力及其历史验证来源。
- 尚未实现或仅为占位的能力。
- 本轮是设计、开发还是历史文档整理；不得混淆。

## 1.3 执行前置

```text
【前置任务】
    ↓
【本方案任务】
    ↓
【后续阶段】
```

---

# 2. 定位、目标与职责边界

## 2.1 负责

- 【本模块拥有的能力、数据和业务决策】

## 2.2 不负责

- 【相邻模块拥有的能力】
- 【本阶段明确暂缓能力】

必须用正反示例或边界表消除容易混淆的职责。

---

# 3. 前后端及跨服务协作目标

说明完整纵向交付链：

```text
领域/组件设计
    ↓
应用用例
    ↓
API/事件契约
    ↓
对应页面或调用方
    ↓
契约测试与关键路径E2E
    ↓
阶段验收
```

没有前端或跨服务消费者时，明确写明“不适用”及原因，不得静默省略协作边界。

---

# 4. 总体架构与数据流

使用文本图说明入口、层次、数据库、缓存、消息、外部系统和前端的关系。

必须明确：

- 服务/模块边界。
- 调用方向。
- 数据权威来源。
- 事务边界。
- 同步 API 与异步事件的分工。

当多个模块共享 Service Host 时，还必须明确逻辑表命名空间（独立 Schema 或模块表前缀）、公开应用契约、权限资源和测试边界；同宿主协作优先使用进程内 Application 契约，禁止跨模块直读 Repository/数据表，并说明未来物理拆分路径。不得因为逻辑模块数量机械拆分 Migration、Outbox、Inbox、基础设施或初始化单元。

PF-02 及后续新服务还必须明确 Service Initialization Pipeline：SystemData 是 Topology/Orchestration/Policy/Observation 控制面；当前服务的初始化器负责 Inspect/Plan/Apply/Verify，并拥有 Migration、Seed、Bootstrap、Ledger 与本地 readiness。必须写清 SystemData 不可用时已初始化服务仍按本地事实 Ready、同物理目标锁、checksum drift、Development/Test 自动策略、EnvironmentSample 禁入 Staging/Production、幂等和 Secret 隔离。禁止独立 Migrator/Seeder Service、业务 API 管理员建库、SystemData 直写业务 Repository、`EnsureCreated` 和向控制面传任意 SQL/路径/命令/Secret。

---

# 5. 项目结构与引用关系

列出真实或目标目录：

```text
src/...
tests/...
```

明确每层职责、允许引用和禁止引用。现有项目必须以当前代码结构为基线，不能复制过时蓝图。

---

# 6. 全局技术与实施约束

至少覆盖：

- 标识与时间类型。
- 数据库名和表前缀。
- 多租户、权限、并发和审计。
- API 信封、错误码和时间序列化。
- 配置、密钥和敏感信息。
- TDD、验证证据和任务状态流转。

## 6.1 统一数据建模约束

- 领域实体的字段定义和“主要字段”列表只展示当前对象拥有的业务字段，不逐表重复 `Entity` 生命周期字段。继承 `Entity` 的领域实体表必须包含 `Id`、`IsFrozen`、`IsLocked`、`IsDeleted`、`EntityType`、`CreatedOn`、`LastUpdatedOn`、`OptimisticVersion`、`ConcurrencyVersion`；统一类型、默认值、并发和软删除语义在本节集中定义。Migration/Seed Ledger、Outbox、Inbox/Checkpoint、幂等、序列和纯技术历史等技术记录只保留其用途所需字段，不机械继承 `Entity`、软删除或双版本并发。
- 领域实体自身的稳定业务标识统一命名为 `NId`，例如 `Material(NId, Name, ...)`；其他业务表引用该业务标识时统一命名为 `{EntityName}NId`，例如 `MaterialNId`。`Code` 只允许表示规则生成的编码结果等非实体身份语义，不得作为实体稳定身份字段的通用名称。
- 同库聚合内父子关系默认使用普通 `ParentId` 外键和聚合仓储维护一致性。只有领域确实要求数据库层传播父实体软删除/恢复状态，并且收益大于额外列、唯一键和级联复杂度时，才选择 `Id + IsDeleted` 复合外键；选择后，子表分别保存 `{ParentEntity}_Id` 和 `{ParentEntity}_IsDeleted`，且子表自身仍有独立生命周期字段 `IsDeleted`。技术记录不得为套用模板而引入该复合外键。
- PostgreSQL 物理列统一使用 `snake_case`：`NId → n_id`、`MaterialNId → material_n_id`；选择复合软删除外键时，`Material_Id → material_id`、`Material_IsDeleted → material_is_deleted`，被引用主表声明条件性 `unique (id, is_deleted)`。未选择该策略时不得额外创建影子删除列或复合唯一键。
- 选择复合软删除外键时，父表软删除或恢复由 `ON UPDATE CASCADE` 或同一事务内的等价机制同步子表 `{ParentEntity}_IsDeleted`，但不得改写子表自身 `IsDeleted`；有效子记录查询同时过滤两种删除状态。未选择时，由聚合用例和普通外键保证父子一致性，不得伪造同等语义。
- 跨服务、跨数据库只保存对方业务标识 `{EntityName}NId` 及必要快照，通过 API/事件维护最终一致性，不建立数据库外键，也不复制对方实体生命周期字段作为本地实体生命周期。

状态统一为：

```text
待细化 → 可派遣 → 已派遣 → 开发中 → 待验收 → 已完成
```

设计冲突统一改为：

```text
设计待确认
```

---

# 7. 领域模型或核心组件详细设计

按聚合、实体、值对象或组件逐章说明：

- 字段和类型。
- 不变量。
- 状态机和允许/禁止转换。
- 公共接口。
- 依赖。
- 错误和边界场景。

实体字段表只列本实体业务字段；统一生命周期字段引用第 6.1 节，不在每个实体章节重复展开。

复杂模块可以从本章继续增加编号章节，不受模板章节数量限制。

---

# 8. 数据与持久化设计

至少说明：

- 数据库和表。
- 字段、主键、外键。
- 唯一约束和索引。
- 时间、精度和状态字段。
- 乐观并发、软删除和迁移。
- 服务初始化器、Migration/Seed/Bootstrap/Verify、服务或独立初始化单元 Ledger、SystemData OperationId、本地 readiness。
- Standard/Advanced 策略、四类种子、Observation、DataPatch 与管理员维护数据保护边界。
- 事务、Outbox、存在真实入站消费者时的 Inbox/Checkpoint，以及数据修复边界。

领域实体表字段清单只列当前表业务字段，完整建表和迁移仍须按第 6.1 节判断并应用统一生命周期字段；技术记录必须显式说明其最小字段和保留策略。每个父子关系必须说明使用普通外键还是复合软删除外键；只有选择后者时才要求父引用快照、同步方式和双重查询过滤。跨服务引用必须明确不建立数据库外键。

不使用数据库时，明确写明“不适用”和状态存储位置。

---

# 9. API、事件与外部集成契约

API 必须明确：

- Gateway 路径和服务内部路径。
- Method、Request、Response。
- 权限。
- 幂等键和并发版本。
- HTTP 状态和稳定错误码。

事件必须明确：

- 事件名和版本。
- 字段。
- 发布时机和事务。
- 重复、乱序和兼容策略。

外部系统必须说明防腐层、超时、重试、回执和对账。

---

# 10. 页面与交互设计

适用时说明：

- 路由、布局和权限。
- 页面字段、状态和交互流程。
- 加载、空、成功、失败、冲突和无权限状态。
- PC/PDA/Mobile 或独立页面差异。
- API 映射、Mock/真实数据边界。
- 可访问性、目标视口和截图验收。

禁止只写“实现对应页面”。

---

# 11. 错误、安全、审计与可观测性

至少覆盖：

- 业务错误与 HTTP/前端行为映射。
- 认证、授权、租户和数据权限。
- 密钥、Token、密码和日志脱敏。
- 审计场景和前后值边界。
- TraceId、结构化日志、指标和健康检查。
- fail-open/fail-closed 决策。
- 初始化器 Secret Provider 边界；SystemData 只能传非敏感上下文并接收脱敏 version/checksum/status/TraceId。

---

# 12. 自动化测试与验收设计

测试层次：

```text
Domain/Unit
Application
Infrastructure
API
Contract/Event
Frontend Component
E2E
```

根据风险和业务复杂度选择适用层次，并给出具体场景矩阵。简单 CRUD 和技术记录不强制完整 DDD 或每层独立测试项目；常规测试默认收敛在服务级项目，真实数据库/中间件和跨服务链路进入统一 IntegrationTests。不得只写“测试通过”。

PF-02 及后续服务至少覆盖首次初始化、重复 apply、并发多副本、版本升级、同版本 checksum drift、部分失败重试、缺 Secret、EnvironmentSample 环境拒绝、管理员维护数据不被覆盖、SystemData 不可用时已初始化服务仍 Ready、本地事实失败时 NotReady 和 SystemData 自身无循环自举。生产未审批/未备份、签名和漂移恢复只在 Advanced 策略适用时强制测试。

所有验证证据至少记录：

```text
命令
退出码
通过/失败/跳过数量
覆盖率（适用时）
报告或截图路径
外部环境限制
```

历史证据必须标注日期和来源，不能表述为本轮重新验证。

---

# 13. 开发任务依赖

```text
TASK-XXX-001 → TASK-XXX-002
                    ├→ TASK-XXX-003
                    └→ TASK-XXX-004

TASK-XXX-003 + TASK-XXX-004 → TASK-XXX-005
```

说明可并行任务、共享文件冲突和最终验收任务。

---

# 14. 开发任务拆分

阶段任务按仓库当前执行协议定义。若阶段整体派遣、内部步骤顺序执行，则步骤必须可独立验证和回写，但不得伪装成独立派遣或强制独立提交；只有明确作为独立任务管理时，才要求独立派遣、提交和验收。详细设计放在前置章节，任务卡引用章节，不重复整篇设计，也不得退化成一句摘要。

## TASK-XXX-001【任务名称】

**状态：** 可派遣

**目标：** 【一个独立、可验收的交付目标】

**输入文档：** 【本文对应章节、前置规格和契约】

**依赖：** 【具体任务编号或“无”】

**允许修改范围：** 【精确目录/项目；同时列出禁止范围】

**预期输出：** 【代码、契约、迁移、页面、报告等明确交付物】

**验证与证据：** 【具体测试场景、命令类型和必须记录的证据】

**结果回写：** 【状态、字段、路由、事件、偏差和回写文档】

**提交策略：** 【阶段整体提交、独立任务提交或不提交；必须与仓库当前执行协议一致】

---

# 15. 完成标准

分别从以下角度列出可核验条件：

- 领域/组件。
- 数据与事务。
- API/事件/外部集成。
- 前端与用户路径。
- 安全、审计和可观测性。
- 自动化测试和环境验收。
- 明确未越界实现暂缓能力。

外部环境缺失时只能标记“待验收”，不得直接标记“已完成”。

---

# 16. 执行记录

| 任务 | 状态 | 执行者/任务 | 提交 | 验证证据 | 结果回写 |
| --- | --- | --- | --- | --- | --- |
| TASK-XXX-001 | 可派遣 | - | - | - | - |

---

# 17. 下一阶段输入契约

列出后续服务、页面或任务可以稳定依赖的：

```text
类型和接口
API路径与DTO
数据库/事件版本
权限码
配置键
页面/路由边界
已知限制
```

同时再次说明后续阶段必须自行设计、不能从本模块推断的业务能力。

---

# 18. 文档自审清单

- [ ] 引用文件真实存在。
- [ ] 当前代码/环境状态与文档一致。
- [ ] 无 `TBD`、`TODO`、“适当处理”等模糊占位。
- [ ] 职责边界和数据权威明确。
- [ ] 已区分 Service Host、Domain Module、Initialization Unit、Deployment Unit。
- [ ] runtime readiness 只依赖本地数据库事实；SystemData 仅保存脱敏 Observation。
- [ ] 实体稳定业务标识使用 `NId`，`Code` 未被误用为实体身份。
- [ ] 领域实体字段列表未逐表重复生命周期字段；技术记录未机械继承领域生命周期、软删除或双版本并发。
- [ ] 同库父子关系已说明普通外键或复合软删除外键的选择依据；采用复合策略时删除状态同步和双重查询过滤完整；跨服务未建立数据库外键。
- [ ] API、事件、类型和路由前后一致。
- [ ] 每个需求都有对应任务和验收。
- [ ] 每个任务具备统一九字段。
- [ ] 任务依赖与执行记录编号一致。
- [ ] 历史证据与本轮验证严格区分。
- [ ] `git diff --check` 通过。

<!--
使用本模板时删除所有方括号说明和本注释。
章节数量按复杂度扩展，但“设计正文在前、任务拆分在后、完成标准与执行记录收尾”的顺序不得改变。
-->
