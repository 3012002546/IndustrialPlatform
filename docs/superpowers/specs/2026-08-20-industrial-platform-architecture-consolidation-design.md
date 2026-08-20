# Industrial Platform 架构收敛整改设计

## 1. 目标

本次整改用于重新对齐 Industrial Platform 的权威蓝图、开发 Todo、服务接手文档、测试门禁和当前实现。整改不重新设计产品，不改变“平台基础能力优先”的路线，也不提前实现未来业务服务。

本设计服务于两个目标：

1. 后续执行者能够按清晰边界完成整改，不自行扩展范围。
2. 项目未来由维护者本人接手时，能够快速理解架构、增加前后端功能并定位常见故障。

## 2. 本会话与实施边界

本会话只产出设计规格和实施计划，不修改正式蓝图、开发 Todo、测试结构或生产代码。后续实施固定为四个工作包，不再拆成大量独立任务：

1. 蓝图与开发 Todo 对齐。
2. 全服务架构文档与代码整改交接。
3. 测试体系收敛。
4. 当前代码调整。

实施范围只覆盖当前已实现的 BuildingBlocks、Identity、SystemData、ReferenceData 骨架、Gateway、UnifiedHost 和与其直接相关的前端入口。MasterData、OperationalData、WorkOrder、Weighting、Trace、BatchRecord 等未来服务只调整蓝图边界，不实现业务代码。

以下内容明确不在范围内：

- 不推翻七个 Service Host 的长期规划。
- 不将当前服务合并为模块化单体。
- 不重写已完成的 Identity 和 SystemData 业务功能。
- 不批量重构现有 Entity 继承体系。
- 不创建新的跨服务通用框架。
- 不处理当前工作区已有的前端样式、调试配置等无关未提交改动。

## 3. 蓝图与 Todo 收敛

### 3.1 权威文档

实施时优先调整以下权威文档：

- `docs/blueprint/09-Industrial Platform开发总TodoList.md`
- `docs/blueprint/29-Industrial Platform自动化测试体系.md`
- `docs/blueprint/32-Industrial Platform Service Host与内部模块边界.md`
- `docs/blueprint/33-Industrial Platform SystemData数据库编排与环境引导.md`
- `docs/blueprint/README.md`
- `docs/implementation/05-Industrial Platform SystemData开发实施方案.md`
- `docs/implementation/06-Industrial Platform ReferenceData Service开发实施方案.md`
- `docs/implementation/TEMPLATE-开发实施方案.md`
- `docs/status/CURRENT.md`
- 根目录 `README.md`

其他文档只有在存在直接冲突引用时才做局部修正，不进行批量重写。

### 3.2 冻结规则

- Service Host、领域模块、初始化单元和独立部署单元是四个不同概念。
- SystemData 负责数据库拓扑、初始化编排、执行策略和脱敏观察，即 Where、When、Policy、Observation。
- 每个服务负责自己的 Migration、Seed、Bootstrap、Verify 和 Ledger，即 What、How、Fact。
- 服务日常启动与 readiness 只依赖自己的数据库事实，不依赖 SystemData 在线。
- ReferenceData 保留 Dictionary、Parameter、Metadata、DynamicProperty、CodingRule 五个内部模块，但默认共享服务级 Migration、Outbox、Inbox 和基础设施。
- 只有具备独立持久化生命周期的模块才成为独立初始化单元。
- 初始化分为 Standard 与 Advanced 两档。普通功能不强制使用审批、备份证据、签名和漂移恢复等高级流程。
- DDD 按业务复杂度使用。简单 CRUD 和技术记录不强制具备完整聚合、领域事件和通用业务实体生命周期。
- 测试按风险和运行成本分层，不再要求每个生产技术层拥有独立测试项目。

开发 Todo 不新增多个阶段编号，只增加一个“架构收敛整改”阶段，内部顺序对应四个工作包；完成后再继续 PF-03。

## 4. 服务架构文档与可接手性

### 4.1 文档集合

文档数量保持最小，只新增或完善：

- 根目录 `README.md`
- `docs/DEVELOPMENT.md`
- `src/backend/src/BuildingBlocks/README.md`
- `src/backend/src/Services/Identity/README.md`
- `src/backend/src/Services/SystemData/README.md`
- `src/backend/src/Services/ReferenceData/README.md`
- `src/backend/src/Gateway/README.md`
- `src/backend/src/Hosts/IndustrialPlatform.UnifiedHost/README.md`
- `docs/agents/ARCHITECTURE-REMEDIATION-HANDOFF.md`

每个组件 README 固定回答：职责、非职责、项目结构、运行入口、依赖与配置、数据初始化、测试入口、常见排障。不为每个服务继续创建多份子文档。

排障说明使用以下固定结构：

```text
现象 → 首先检查 → 执行命令 → 正常结果 → 异常时下一步 → 相关代码入口
```

### 4.2 Gateway 与 UnifiedHost

Gateway 和 UnifiedHost 必须作为两种不同部署角色描述，不能互相替代或混写。

| 项目 | Gateway | UnifiedHost |
| --- | --- | --- |
| 使用模式 | 多进程、未来分布式部署 | 当前统一进程部署 |
| 核心职责 | YARP 路由、服务前缀、CORS、下游健康聚合、代理错误 | 组合当前模块、统一中间件、模块启动迁移、托管 SPA |
| 业务模块 | 不加载 | 加载 Identity、SystemData、ReferenceData |
| 前端静态文件 | 不托管 | 生产环境托管 |
| 数据迁移 | 不执行 | 协调模块自己的迁移 |
| 下游代理 | 执行 | 不执行 YARP 代理 |
| 外部路径 | `/identity`、`/systemdata`、`/referencedata` | 保持相同路径兼容 |

正式部署路径只有两种：

```text
统一部署：Browser → UnifiedHost → 内置模块
分布式部署：Browser → Gateway → 独立 API Host
```

Gateway 不作为服务间调用总线，UnifiedHost 不作为业务编排器，SystemData 不作为业务服务中介。

### 4.3 开发者上手指南

`docs/DEVELOPMENT.md` 面向具有整体产品和架构经验、但对部分前后端组件及中间件不熟悉的维护者。它按以下真实路径组织：

```text
明确功能归属
→ 定义权限和 API 契约
→ 后端 Domain/Application/Infrastructure/API
→ Migration 与 Seed
→ 前端类型/API/页面/路由/菜单/权限
→ 单元与接口测试
→ UnifiedHost 和 Gateway 两种模式验证
→ 更新服务 README
```

指南必须说明：

- 新增查询、CRUD、复杂领域行为、数据库表、权限、菜单和页面分别从哪里开始。
- 后端 Controller、Application、Domain、Repository 的调用链。
- 前端 API 类型、`httpClient`、页面、组件、路由、菜单和权限控制的调用链。
- PostgreSQL、Redis、RabbitMQ、SignalR、Outbox、Domain Event、Integration Event 的适用场景。
- 当前项目已经封装的组件、优先复用入口和禁止重复实现的基础能力。
- 两种部署模式的启动、访问和验证方式。
- 每类改动所需的最小测试集合。
- 一个不引入示例业务代码的端到端文件路径模板。

`docs/agents/ENGINEERING-NOTES.md` 继续只保存按需工程陷阱，不与正常开发路径混用。

## 5. 未来业务服务交互

外部访问与服务内部协作分开：

```text
外部客户端 → Gateway 或 UnifiedHost
服务内部协作 → 服务契约、同步接口或集成事件
```

交互选择规则：

- 聚合内部变化使用 Domain Event，仅限服务内部。
- 同一 Service Host 内模块解耦使用 Module Contract 或进程内事件。
- 必须立即得到结果的跨服务查询或命令使用版本化同步契约。
- 状态变化通知使用 Outbox 与 RabbitMQ Integration Event。
- 多服务长流程由所属业务域的 Process Manager 或 Saga 编排，不放入 SystemData。
- 多服务查询使用查询投影或只读副本，不跨服务直接联表。
- Shared 物理数据库模式仍禁止跨服务 Repository、外键和直接写表。

消费方 Application 依赖自己定义的 Port。统一部署使用进程内模块适配器，分布式部署使用 Typed HTTP Client 或消息适配器，业务代码不感知部署模式。

开发指南使用以下业务链解释同步校验、快照和事件协作：

```text
MasterData 发布物料 → WorkOrder 保存必要快照
WorkOrder 下达 → Weighting 建立称量任务
Weighting 完成 → Trace / BatchRecord 构建业务投影
```

跨服务交互最低要求包括超时、有限重试、幂等、契约版本、TraceId、失败补偿和最终一致性说明。

## 6. 测试体系收敛

### 6.1 目标结构

后端测试原则上收敛为以下七个项目：

- `IndustrialPlatform.BuildingBlocks.Tests`
- `IndustrialPlatform.Identity.Tests`
- `IndustrialPlatform.SystemData.Tests`
- `IndustrialPlatform.ReferenceData.Tests`
- `IndustrialPlatform.Gateway.Tests`
- `IndustrialPlatform.UnifiedHost.Tests`
- `IndustrialPlatform.IntegrationTests`

Identity 的 Domain、Application、Contract、Infrastructure、API 测试项目合并为一个。SystemData 的五层测试项目和 Testing 辅助项目合并为一个。真实 PostgreSQL、Redis、RabbitMQ 和跨服务链路进入统一 IntegrationTests。

SQLite、内存数据库、Fake 和 `WebApplicationFactory` 等可重复运行测试保留在服务常规测试项目。前端继续使用 Vitest 与 Playwright 两层，不新增其他测试层。

### 6.2 保留与删除标准

优先保留：

- 领域不变量和安全规则。
- 登录、权限和管理员保护。
- Migration、Seed 和 Ledger。
- SystemData 拓扑、编排和 readiness。
- 核心 API 契约。
- Gateway 路由与代理错误语义。
- UnifiedHost 模块组合、路径兼容和启动迁移顺序。

允许删除：

- 仅验证属性赋值或框架默认行为的测试。
- 各层重复的程序集边界测试。
- 内容相同的配置绑定、健康端点和依赖注入测试。
- 已被更高层真实链路覆盖的低价值 Mock 测试。
- 只为未来功能预留、当前没有实现对象的测试。
- 合并后不再需要的测试项目和辅助项目。

测试删除前必须记录原覆盖意图。只有合并后的高价值行为测试通过，才能删除对应旧文件。

### 6.3 门禁

```text
日常：Release Build + 常规后端测试 + 前端单元测试
集成：真实数据库和中间件测试
发布：关键登录与统一入口 E2E
```

测试数量不是验收目标。验收关注关键行为覆盖、执行时间、失败定位能力和维护成本。

## 7. 当前代码整改

### 7.1 初始化契约

BuildingBlocks 的中立契约层提供最小初始化协议：

```text
Inspect → Plan → Apply → Verify
```

初始化上下文只包含环境、OperationId、ServiceKey、ModuleKey、解析后的数据库目标、期望版本和策略，不包含原始 SQL、文件路径、管理员密码或业务 Seed 内容。

- Identity、SystemData、ReferenceData 分别实现自己的初始化器。
- SystemData 调用初始化器并保存脱敏 Observation，不拥有其他服务的迁移实现。
- UnifiedHost 使用进程内调用。
- 分布式部署使用受信内部 HTTP 调用，并要求内部鉴权、幂等 OperationId 和脱敏响应。
- SystemData 通过统一调用端口使用两种适配器，不感知实际传输方式。

### 7.2 Readiness

- 每个服务从自己的数据库、Migration Ledger 和 Required Seed Ledger 计算本地 readiness。
- SystemData 不在线时，已初始化服务仍可 Ready。
- SystemData Observation 不是运行事实的唯一来源。
- Gateway 聚合独立 API Host readiness。
- UnifiedHost 聚合进程内模块本地 readiness。
- Gateway 与 UnifiedHost 不互相代理，也不混用错误语义。

### 7.3 组件调整范围

- BuildingBlocks：只增加最小公共初始化契约，不整体重写 `Entity`。
- Identity：复用现有 Migration、Seed、Bootstrap 与 readiness，封装为服务初始化器。
- SystemData：将占位式 `ServiceInitializerExecutor` 调整为服务初始化调用端口，保留拓扑、计划、审批、备份、Operation 和 Observation。
- ReferenceData：只补服务级初始化与本地 readiness 骨架，不实现五个业务模块。
- Gateway：保持纯 YARP 边界，只修正文档、配置或测试发现的实际偏差。
- UnifiedHost：通过模块初始化器协调启动顺序，不直接依赖各服务迁移实现类。
- 前端：只修改与入口、健康状态或初始化状态展示直接相关的代码，不新增管理功能。

不实现未来业务服务的客户端、事件消费者和 Saga，也不因测试合并改变生产程序集边界。

## 8. 防回归与停止条件

后续实施必须保护现有功能：

1. 开始前记录当前工作区、当前 Release 构建结果、测试项目及关键行为基线。
2. 文档调整不触碰生产代码。
3. 测试合并先迁移并运行保留测试，再删除旧项目。
4. 初始化契约调整先增加刻画现有行为的测试，再修改生产实现。
5. 源码变化后先执行全新 Release Build，再执行对应测试，禁止使用陈旧 `--no-build` 产物证明通过。
6. Identity 登录、权限、管理员保护，SystemData 编排，Gateway 路由和 UnifiedHost 登录入口出现回归时立即停止，不进入下一工作包。
7. 外部基础设施不可用必须标记为环境阻塞，不能伪造成代码通过或失败。
8. 保留当前工作区既存未提交改动，不覆盖、不清理、不顺带提交。

四个工作包完成后的最终验收至少包含：

- Release 构建成功。
- 常规后端测试全部通过。
- 前端单元测试通过。
- 可用环境下的集成测试通过。
- Gateway 分布式入口与 UnifiedHost 统一入口分别完成关键登录/路由冒烟验证。
- 文档链接、命令和代码路径与实际仓库一致。

## 9. 最终交付

实施完成后应交付：

- 已对齐的权威蓝图、开发 Todo 和当前状态。
- 当前组件的接手级 README。
- 面向维护者的前后端与跨服务开发指南。
- 供执行者使用的代码整改交接说明。
- 收敛后的测试项目和分层门禁。
- 当前服务初始化与 readiness 边界一致的生产代码。
- 精简的验证记录和仍需外部环境确认的事项。

任何超出上述范围的发现只记录为后续事项，不在本次整改中顺带实现。
