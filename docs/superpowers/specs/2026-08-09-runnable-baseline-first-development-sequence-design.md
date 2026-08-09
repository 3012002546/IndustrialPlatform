# 可运行基线优先的开发顺序设计

## 1. 背景与当前状态

`CLAUDE.md` 记录的实际协作进度是本次顺序调整的依据：

- BuildingBlocks 已完成 7 个共享组件及 Security、Web 补充能力。
- BuildingBlocks 测试 64 个通过、0 个失败；记录时全解决方案 74 个通过、0 个失败。
- Identity 和 ReferenceData 当前只有 Clean Architecture 服务骨架与 `/health`。
- `src/frontend` 只有目录占位与 README，尚未创建 Vue/Vite 工程。
- Docker、数据库、消息队列、日志服务与部署目录仍为占位。

现有实施路线把前端放在业务服务之后，无法尽早形成可启动、可访问、可演示的项目基础。因此开发目标由“按后端服务顺序推进”调整为“先建立可运行产品骨架，再逐个加入业务服务和对应页面”。

## 2. 目标

优先交付一条可重复启动的最小产品基线：

```text
基础设施启动
→ 后端服务与健康检查可访问
→ 前端三端基础壳可访问
→ Mock 登录可进入 PC 管理框架
→ Identity 完成后切换真实登录与权限
→ 后续服务与其业务页面同步交付
```

完成基线后，新开发环境应能按照仓库文档启动依赖、后端和前端，并从浏览器访问登录页、首页、错误页以及 PDA/Mobile 基础壳。

## 3. 调整后的实施顺序

### Phase 0：BuildingBlocks（已完成）

不再把已完成组件列为待开发任务。实施文档只保留完成状态、关键技术决策、验证证据和后续组件复用约束。

### Phase 1：项目可运行基线

交付内容：

- 统一后端启动方式与开发环境配置。
- Identity、ReferenceData 健康检查可访问。
- Gateway 或开发期统一入口能够转发/聚合健康状态。
- Docker Compose 启动 PostgreSQL、Redis、RabbitMQ 和 Seq。
- 配置样例不包含真实密钥；启动失败时能明确定位缺失依赖。
- 后端 restore、build、test 和启动命令写入可复现说明。

Phase 1 不实现 Identity 登录、ReferenceData 业务能力、MasterData 或 OperationalData。

### Phase 2：第一批统一前端

创建一个 Vue 3 单体前端工程，业务逻辑共享，布局按终端适配。第一批固定范围：

- Vue 3、TypeScript、Vite。
- Pinia、Vue Router、Element Plus。
- 应用配置、HTTP 客户端、统一 API 错误模型。
- PC 管理框架：侧边菜单、顶部栏、内容区、折叠与基础响应式行为。
- 登录页。
- 首页仪表盘；第一批使用本地静态数据或显式 Mock 数据，不伪装真实生产指标。
- 403、404 页面。
- PDA 基础壳。
- Mobile 基础壳。
- 终端识别和可手动覆盖的调试入口。
- Mock 登录适配器，以及与未来真实 Identity API 相同的前端接口边界。

第一批不实现物料、库存、工单、称量、设备或追溯业务页面；不实现离线同步、扫码、打印、蓝牙、Capacitor 或复杂看板设计器。

## 4. 前端边界与数据流

登录调用通过稳定端口隔离：

```text
LoginPage
  → AuthStore
  → AuthGateway
      ├─ MockAuthGateway（Phase 2）
      └─ HttpAuthGateway（Phase 3）
```

Phase 2 的 Mock 返回模型必须与 Phase 3 Identity 登录响应的前端领域模型一致，包括用户、访问令牌、刷新令牌、权限和菜单所需字段。Mock 数据集中存放，并在界面显式显示开发模式，禁止散落在页面组件中。

路由分为：

- 公共路由：登录、403、404。
- PC 受保护路由：首页和后续管理页面。
- PDA 受保护路由：第一批仅基础壳首页。
- Mobile 受保护路由：第一批仅基础壳首页。

Phase 2 使用本地会话模拟路由守卫；Phase 3 替换为真实令牌、刷新和权限判断，不重写页面与布局。

## 5. Phase 3：Identity 登录闭环

Identity 在前端壳完成后开发真实认证授权：

- 用户、角色、权限和用户角色关系。
- 密码哈希、登录、JWT、RefreshToken、注销与令牌撤销。
- 登录失败、令牌过期、刷新失败和未授权的统一错误契约。
- 前端 `HttpAuthGateway`、令牌存储、自动刷新、路由守卫、菜单和按钮权限。
- 端到端验证从登录页进入 PC 首页，并覆盖 401、403 和刷新失败。

切换完成后删除运行时 Mock 开关的生产默认值；Mock 只能保留为开发和前端自动化测试适配器。

## 6. 后续业务服务与页面的交付节奏

后续阶段不再采用“全部后端完成后统一做前端”，而是每个服务纵向交付：

```text
服务领域与应用用例
→ API / 事件契约
→ 对应 PC/PDA/Mobile 页面
→ 契约测试与关键流程 E2E
→ 阶段验收
```

具体顺序：

1. ReferenceData：字典、配置、元数据、编码规则及对应管理页面。
2. MasterData：执行 `TASK-MD-001` 至 `TASK-MD-010`，同步增加物料、单位、组织、仓库/库位、设备、BOM、工艺路线页面。
3. OperationalData：执行 `TASK-OD-001` 至 `TASK-OD-009`，同步增加库存查询、批次、收发退、调拨、盘点页面。
4. WorkOrder、Weighting、IoT Collector、Trace、BatchRecord 按相同纵向方式推进。

业务页面不得先于稳定 API 契约绑定真实后端；允许先以严格契约 Mock 开发，但同一阶段必须完成真实 API 切换和契约测试。

## 7. 错误处理与可观测性

- Docker 依赖不可用时，后端健康检查返回依赖级别状态，启动说明提供诊断命令。
- 前端网络错误、业务错误、401、403 和未知错误采用统一分类，不在页面散落字符串判断。
- Phase 2 Mock 模式在页面可见，避免误认为已连接真实服务。
- 请求携带或接收 TraceId，前后端错误页面显示可供排查的关联标识。
- 真实凭据、数据库密码和 JWT 密钥不提交仓库，只提交安全的示例配置。

## 8. 测试与验收

Phase 1 至少验证：

- 全解决方案 restore、build、test。
- Docker Compose 配置解析和四个基础容器健康状态。
- Identity、ReferenceData 和统一入口健康检查。
- 新开发环境启动说明可复现。

Phase 2 至少验证：

- 前端依赖安装、类型检查、生产构建和单元测试。
- 登录成功/失败、受保护路由、403、404。
- PC、PDA、Mobile 三类布局路由可访问。
- Mock 与真实 AuthGateway 的契约测试夹具一致。
- 基础响应式和键盘可访问性检查。

Phase 3 至少验证：

- Identity 领域、应用、基础设施与 API 测试。
- 密码、JWT、刷新令牌、撤销、并发刷新与权限测试。
- 前后端登录、刷新、401、403 的契约和 E2E 测试。

## 9. 文档调整范围

实施计划阶段应：

- 更新 `docs/implementation/01-Industrial Platform开发启动实施方案.md` 的阶段顺序和 Phase 0 状态。
- 新增项目可运行基线的可派遣 TODO 文档。
- 新增统一前端第一批的可派遣 TODO 文档。
- 更新 `docs/implementation/README.md` 的索引、当前状态和依赖顺序。
- 更新 `docs/blueprint/09-MES MVP第一阶段开发TodoList.md`，使 Sprint 顺序与新基线一致。
- 更新 MasterData、OperationalData TODO 的阶段依赖，明确在 Identity 登录闭环和对应前置服务之后执行。
- 不修改 `CLAUDE.md`，其进度由负责代码实现的协作方维护。

## 10. 完成标准

- 实施文档不再把 BuildingBlocks 列为待开发。
- 前端第一批范围固定且没有提前混入业务页面。
- Identity 真实登录明确晚于前端壳、早于 ReferenceData/MasterData/OperationalData。
- 每个后续服务都要求同阶段交付相应业务页面和契约/E2E 验证。
- 文档中的状态与 `CLAUDE.md` 当前进度一致。
- 新 TODO 可以独立派遣，包含目标、输入、依赖、范围、输出、验证、证据和回写位置。
