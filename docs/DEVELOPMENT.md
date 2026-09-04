# Industrial Platform 开发与接手指南

本文面向未来直接维护本仓库的人，回答两件事：如何沿真实代码路径增加一个前后端完整功能，以及在统一/分布式部署下如何选择组件、组织跨服务协作并验证结果。权威服务边界仍以蓝图 32、33 为准；本文不替代具体 PF 实施方案。

## 1. 先判断功能归属

动手前依次确认：

1. 哪个 Service Host 拥有数据和业务不变量；不要按页面位置或数据库物理位置判断所有权。
2. 它是现有逻辑模块的新能力，还是确实具有独立持久化生命周期的新初始化单元。
3. 是简单查询/CRUD，还是需要聚合、不变量和领域事件的复杂行为。
4. 是否需要权限、菜单、页面、Migration、RequiredSeed 或跨服务契约。

`Service Host != Domain Module != Initialization Unit != Deployment Unit`。Shared 物理数据库也不允许跨服务 Repository、外键或直接写表。ReferenceData 默认仍是一个 Host、七个逻辑模块、服务级基础设施；新增状态机定义与通用计量单位，不接管业务实例状态、事务或物料专属换算。

## 2. 一项前后端完整功能的固定路径

```text
功能归属
→ 权限和 API 契约
→ 后端 Domain / Application / Infrastructure / API
→ Migration 与 Seed
→ 前端 types / API / page / router / menu / permission
→ 单元、接口与必要集成测试
→ UnifiedHost 与 Gateway 双入口验证
→ 更新所属组件 README
```

### 2.1 从哪一层开始

| 改动类型 | 首先进入 | 随后补齐 |
| --- | --- | --- |
| 只读查询 | Application 查询用例和读端口 | Infrastructure 查询、Contract、Controller、前端与接口测试 |
| 简单 CRUD | Application 用例/校验 | 持久化、Contract、Controller；有明确不变量时再引入 Domain 模型 |
| 复杂领域行为 | Domain 聚合/值对象/不变量 | Application 编排、Repository 端口、持久化、API |
| 新数据库表/列 | 服务自己的版本化 Migration | 映射、Repository、兼容窗口、Migration 测试；禁止 `EnsureCreated` |
| 权限 | 后端权限目录和授权策略 | 前端 `PERMISSIONS`、路由 meta、菜单与操作级 `PermissionGate` |
| 页面/菜单 | API 契约稳定后从 types/API 开始 | Page、route、navigation、permission、Vitest/Playwright |
| 跨服务需求 | 消费方 Application 定义 Port | 版本化同步契约或 Integration Event 适配器 |

### 2.2 后端四层调用链

以当前 Identity 用户管理为路径参考，不复制其业务内容：

```text
src/backend/src/Services/Identity/
  IndustrialPlatform.Identity.Domain/Users/...
  IndustrialPlatform.Identity.Application/Management/UserManagementService.cs
  IndustrialPlatform.Identity.Application/Management/ManagementStoreContracts.cs
  IndustrialPlatform.Identity.Infrastructure/Persistence/...
  IndustrialPlatform.Identity.Contracts/Management/UserManagementContracts.cs
  IndustrialPlatform.Identity.Api/Controllers/UsersController.cs
```

调用方向固定为：

```text
Controller
→ Application 用例
→ Domain 聚合/规则（需要时）
→ Application 定义的 Repository/Store/外部 Port
→ Infrastructure 实现
→ 服务自有数据库或外部适配器
```

- Controller 负责 HTTP 绑定、授权、状态码和调用用例，不写业务事务。
- Application 负责编排用例、事务边界、幂等和把 Domain Event 映射为 Integration Event/Outbox。
- Domain 只表达真正的业务不变量；简单技术记录不必强行使用完整聚合生命周期。
- Infrastructure 实现 Application 端口，不能反向定义业务规则。
- Contracts 保存对外 DTO/事件契约；不要直接暴露数据表 Entity。

新增 API 时复用 `AddIndustrialApi`、`ApiResult`、异常中间件、当前用户和授权扩展。外部路径保持 `/<service>/api/v1/**`；独立 Host 内 Controller 仍只关心 `/api/v1/**`。

### 2.3 Migration 与 Seed

数据库变更由目标服务拥有：

```text
Migration → RequiredSeed → Bootstrap → Verify → 本地 Ledger/readiness
```

- 所有环境使用显式版本化 Migration；禁止用 `EnsureCreated`、自动删库重建代替。
- 系统必需目录使用 `SystemBaseline`；租户默认项按需使用 `TenantBaseline`。
- 演示数据只能是显式启用的 `EnvironmentSample`，Staging/Production 拒绝。
- SecretBootstrap 只声明需求，Secret 由目标服务自己的 Secret Provider 解析。
- 同版本 checksum 不同必须报 drift；修正历史数据使用显式 DataPatch。
- SystemData 负责目标和编排，只保存脱敏 Observation；不能承载业务 Migration/Seed 内容。

### 2.4 前端调用链

Identity 用户管理的真实路径是：

```text
src/frontend/src/api/identity/management/types.ts
→ src/frontend/src/api/identity/management/managementApi.ts
→ src/frontend/src/api/identity/managementRegistry.ts
→ src/frontend/src/pages/pc/identity/IdentityUsersPage.vue
→ src/frontend/src/router/routes.ts
→ src/frontend/src/components/navigation/navigation.ts
→ src/frontend/src/permissions/catalog.ts
```

应用启动时 `src/frontend/src/app/createIndustrialApp.ts` 创建共享 `httpClient` 并注册 API。`httpClient` 负责统一响应信封、认证头、401 刷新和错误映射，页面不能另建 Axios 实例或重复实现刷新逻辑。

新增功能按以下顺序：

1. 在所属 API 目录定义 request/response types，不把后端 Entity 直接搬到前端。
2. 在 API factory 中使用共享 `HttpClient`，服务前缀与版本写在 API 层。
3. 页面从 Registry/组合入口取得 API；通用交互优先复用 `components/base/`、`components/management/`。
4. 在 `router/routes.ts` 增加页面路由及 `requiresAuth`、`permission`、terminal/workspace 元数据。
5. 在 `navigation.ts` 增加真实存在的菜单；菜单隐藏不能替代 Router Guard。
6. 在 `permissions/catalog.ts` 使用与后端一致的权限常量；按钮用 `PermissionGate`，路由由 `router/guards.ts` 再校验。

不要只加菜单而没有路由/权限，也不要只在前端隐藏操作而省略后端授权。

## 3. 组件和中间件何时使用

| 组件 | 使用场景 | 不使用/注意事项 | 优先入口 |
| --- | --- | --- | --- |
| PostgreSQL/SQLite | 服务权威业务事实、事务、Ledger、Outbox | Shared 物理库不等于共享所有权；SQLite 只是在 Development profile 下的 Provider | `DatabaseTopologyResolver`、服务 Migration/Repository |
| Redis | 缓存、分布式锁、短期会话/撤销、OAuth state、限流状态 | 不作为不可恢复的唯一业务事实；明确 fail-open/fail-closed | `ICacheService`、`IDistributedLock`、`AddRedis` |
| RabbitMQ | 跨服务状态变化、可重放异步协作 | 不用于必须立即返回结果的查询/命令；消费者必须幂等 | `IndustrialPlatform.EventBus`、服务 Outbox |
| SignalR | 浏览器需要低延迟进度/通知推送时 | 当前没有通用业务 Hub；不得用它替代持久事件或最终状态查询 | 在拥有通知语义的模块设计 Hub，仍以数据库/事件为事实 |
| Domain Event | 一个服务/聚合内部解耦副作用 | 不跨进程、不作为外部稳定契约 | `IndustrialPlatform.SharedKernel/Events/IDomainEvent.cs` |
| Integration Event | 服务间状态变化通知 | 必须版本化，经 Outbox 原子发布，消费方 Inbox/幂等 | `IndustrialPlatform.EventBus`、`*/Contracts/Events/` |
| Outbox | 数据库写入与待发布事件需原子提交 | 不能省略消费者幂等；不要把同步查询塞入 Outbox | Identity 的 `OutboxEnvelope` 与 Infrastructure Outbox 实现 |
| Module Contract / 进程内事件 | 同一 Service Host 内逻辑模块解耦 | 不直接访问其他模块 Repository/表；需保持可拆分语义 | 模块公开 Application Contract/Port |

## 4. 两种部署入口

### UnifiedHost：当前默认统一部署

```text
Browser → UnifiedHost (:5041) → 进程内 Identity/SystemData/ReferenceData
```

UnifiedHost 组合模块、统一中间件、协调模块自己的初始化、聚合模块本地 readiness、托管生产 SPA。它不运行 YARP、不代理下游、不拥有业务流程。

```powershell
./deploy/scripts/dev.ps1 start
./deploy/scripts/dev.ps1 status
Invoke-RestMethod http://localhost:5041/health/ready
```

### Gateway：分布式边界验证/未来部署

```text
Browser → Gateway (:5080) → 独立 Identity/SystemData/ReferenceData API Host
```

Gateway 只做 YARP、前缀、CORS、下游健康聚合和代理错误；不加载业务模块、不托管 SPA、不迁移数据。

```powershell
./deploy/scripts/dev.ps1 start -IndependentServices
./deploy/scripts/dev.ps1 status
Invoke-RestMethod http://localhost:5080/health/ready
```

两种入口必须保持 `/identity`、`/systemdata`、`/referencedata` 外部路径兼容。前端只切换 API Base URL，不直连内部服务端口。Gateway 与 UnifiedHost 不互相代理。

## 5. 跨服务同步调用、快照与事件协作

外部访问入口与服务内部协作分开：

```text
外部客户端 → Gateway 或 UnifiedHost
服务内部 → consumer-owned port + 同步适配器，或 Outbox + Integration Event
```

### 5.1 选择规则

```text
同步查询/命令 = consumer-owned port + versioned contract
状态变化 = Outbox + Integration Event
同宿主模块 = module contract / in-process event
长流程 = owning business domain process manager
多服务查询 = projection/read model
Shared physical database != shared ownership
```

- 必须立即得到结果的跨服务查询/命令：消费方 Application 定义 Port，使用版本化同步契约。
- 同宿主模块：用 Module Contract 或进程内事件适配 Port。
- 分布式部署：同一 Port 由 Typed HTTP Client 或消息适配器实现，业务用例不感知部署模式。
- 状态变化通知：本地事务写 Outbox，再由 RabbitMQ 发布 Integration Event。
- 多服务长流程：由拥有业务结果的领域实现 Process Manager/Saga，不放到 SystemData。
- 多服务查询：建立 projection/read model 或只读副本，不跨服务直接联表。

同步调用最低要求：明确超时、有限重试（只对瞬时且幂等的操作）、契约版本、TraceId、错误映射和失败补偿。禁止无上限重试和同步调用环。

异步消费最低要求：事件 ID/业务幂等键、Inbox 或等价去重、版本兼容、TraceId/CorrelationId、失败重试与死信/人工恢复路径。发布成功只表示事件已持久交付，不表示所有下游立刻一致。

### 5.2 未来业务链示例（只说明交互，不代表已实现）

```text
MasterData 发布物料
→ WorkOrder 在创建/下达时同步校验当前物料，并保存必要快照
→ WorkOrder 下达事件经 Outbox 发布
→ Weighting 幂等建立称量任务
→ Weighting 完成事件经 Outbox 发布
→ Trace / BatchRecord 幂等更新各自业务投影
```

关键点：

- WorkOrder 只保存物料稳定 ID 和下单时必须冻结的名称、规格、单位、版本等快照，不复制 MasterData 整库。
- “物料是否当前可用”需要立即答案时，通过 WorkOrder 自己的 `IMaterialValidationPort`（命名以未来设计为准）同步调用版本化 MasterData 契约；统一部署用进程内适配器，分布式用 Typed HTTP Client。
- 同步校验超时不能伪造成“物料不存在”；按用例决定拒绝、稍后重试或保存待确认状态，并记录 TraceId。
- WorkOrder 下达与 Outbox 写入同一事务；Weighting 用 WorkOrderNId/事件 ID 幂等创建一次任务。
- Weighting 失败不能回滚已下达的 WorkOrder 本地事务；由 WorkOrder 所属业务域的 Process Manager 执行补偿、重试或人工介入。
- Trace/BatchRecord 接受 Weighting 完成事件后构建自己的投影。RabbitMQ 延迟期间允许短暂不一致，API/页面应展示处理中或最后更新时间，并提供重放/修复入口。
- 契约升级采用并行兼容或消费者先升级，禁止直接改旧事件语义。

## 6. 最小测试集合

| 改动 | 最小验证 |
| --- | --- |
| Domain 不变量 | 所属服务 Domain/常规测试中的正反例、并发/边界值 |
| Application 用例 | 成功、授权/校验、幂等、端口失败与补偿语义 |
| Repository/Migration/Seed | SQLite/可重复测试；真实 PostgreSQL 进入 IntegrationTests；checksum/ledger/drift |
| API/权限 | Controller 合同、401/403/404、统一信封、路由前缀 |
| 前端 API/页面 | Vitest：types 映射、请求、加载/空/错误/权限状态与关键交互 |
| 路由/菜单/权限 | Router Guard、导航过滤、操作级 PermissionGate |
| 跨服务事件 | Outbox 原子性、契约版本、消费幂等、重试/失败恢复 |
| 入口/宿主 | UnifiedHost 组合与前缀；Gateway 路由、健康、503/504 |

日常门禁：

```powershell
dotnet build src/backend/IndustrialPlatform.slnx --configuration Release
dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build
pnpm --dir src/frontend test:unit
```

源码变化后必须先 fresh Release Build，再运行 `--no-build` 测试。外部依赖集成测试和 real-login E2E 在环境可用时执行；环境不可用要明确命名阻塞，不能当作代码通过。

## 7. 端到端文件路径模板

以下只是路径模板，不创建示例业务代码：

```text
src/backend/src/Services/<Service>/IndustrialPlatform.<Service>.Domain/<Capability>/
src/backend/src/Services/<Service>/IndustrialPlatform.<Service>.Application/<Capability>/
src/backend/src/Services/<Service>/IndustrialPlatform.<Service>.Contracts/<Capability>/
src/backend/src/Services/<Service>/IndustrialPlatform.<Service>.Infrastructure/<Capability>/
src/backend/src/Services/<Service>/IndustrialPlatform.<Service>.Api/Controllers/<Capability>Controller.cs
tests/<Service>/IndustrialPlatform.<Service>.Tests/<Layer-or-Capability>/

src/frontend/src/api/<service>/<capability>/types.ts
src/frontend/src/api/<service>/<capability>/<capability>Api.ts
src/frontend/src/pages/<terminal>/<service>/<Capability>Page.vue
src/frontend/src/router/routes.ts
src/frontend/src/components/navigation/navigation.ts
src/frontend/src/permissions/catalog.ts
src/frontend/tests/<matching capability tests>
```

完成后更新所属组件 README 的结构、运行/测试入口和排障，不为每个服务继续拆出多份子文档。特殊工程陷阱才按需写入 `docs/agents/ENGINEERING-NOTES.md`。

## 8. 常见开发排障

### 后端改动后测试结果可疑

- 现象 → `dotnet test --no-build` 很快通过，但改动似乎未生效。
- 首先检查 → 是否先对当前源码执行了 fresh Release Build，以及 `bin/obj` 是否被锁。
- 执行命令 → `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release; dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build`
- 正常结果 → Build 退出码 0，测试输出明确 0 failed。
- 异常时下一步 → 若 CS2012/access denied，先检查 `dotnet`、`testhost`、`VBCSCompiler` 进程，锁释放后重新 Build；不要用旧产物下结论。
相关代码入口 → `src/backend/IndustrialPlatform.slnx`、`tests/`。

### 页面可见但接口 403

- 现象 → 菜单/页面能打开，操作或 API 返回 403。
- 首先检查 → 后端权限目录、Token claims、路由 meta、导航权限和按钮 `PermissionGate` 是否同一权限码。
- 执行命令 → `rg -n "identity\..*\." src/backend/src/Services/Identity src/frontend/src/permissions src/frontend/src/router src/frontend/src/components/navigation`
- 正常结果 → 同一能力的后端策略和前端三处门禁引用一致常量/权限码。
- 异常时下一步 → 修正权限目录/Seed 与映射并补授权测试；不要取消后端授权。
相关代码入口 → `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Domain/Permissions/PermissionCatalog.cs`、`src/frontend/src/permissions/catalog.ts`、`src/frontend/src/router/guards.ts`。

### 统一入口正常、分布式入口失败

- 现象 → `5041` 可用而 `5080` 返回 503/504/404。
- 首先检查 → 是否使用 `-IndependentServices` 启动独立 Host、Gateway 目标地址和服务前缀。
- 执行命令 → `./deploy/scripts/dev.ps1 status; Invoke-RestMethod http://localhost:5080/health/ready`
- 正常结果 → Gateway 下游逐项 Ready，外部前缀与 UnifiedHost 相同。
- 异常时下一步 → 直接检查各独立 Host readiness，再检查 YARP 配置和代理错误；不要让 Gateway 加载模块。
相关代码入口 → `src/backend/src/Gateway/IndustrialPlatform.Gateway/`、`deploy/scripts/dev.ps1`。
