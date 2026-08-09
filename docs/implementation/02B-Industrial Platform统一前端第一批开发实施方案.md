# Industrial Platform 统一前端第一批开发 TODO

版本：v1.0
当前阶段：可运行基线之后、Identity 业务功能之前
规格依据：`docs/superpowers/specs/2026-08-09-runnable-baseline-first-development-sequence-design.md`

## 1. 目标与范围

建立一个可运行的 Vue 3 统一前端工程，交付登录页、PC 管理框架、首页仪表盘、403/404、PDA 基础壳和 Mobile 基础壳。Phase 2 使用显式 Mock 登录，Phase 3 通过相同 `AuthGateway` 边界切换真实 Identity API。

第一批明确暂缓：物料、库存、工单、称量、设备、追溯等业务页面；离线同步；扫码、打印、蓝牙；Capacitor；复杂看板设计器。

## 2. 全局约束

- 单一 Vue 工程共享 api、stores、composables、components 和 types；不创建三套前端。
- TypeScript 禁止 `any`；生产构建必须通过类型检查和 lint。
- 页面不直接调用 HTTP；通过 Store、Use Case 或 Gateway 边界访问。
- Mock 数据集中存放并在界面显示“开发 Mock 模式”；生产默认不得启用 Mock。
- PC、PDA、Mobile 由路由和布局适配，终端识别允许开发期手动覆盖。
- 网络、业务、401、403 和未知错误使用统一分类；可用时显示 TraceId。
- 可访问性最低要求：键盘可操作、可见焦点、表单标签、语义标题和足够对比度。
- 状态流转：`可派遣 → 已派遣 → 开发中 → 待验收 → 已完成`。

## 3. 稳定前端契约

```typescript
interface AuthGateway {
    login(command: LoginCommand): Promise<AuthSession>;
    refresh(refreshToken: string): Promise<AuthSession>;
    logout(): Promise<void>;
    getCurrentUser(): Promise<CurrentUser>;
}
```

`MockAuthGateway` 与 Phase 3 的 `HttpAuthGateway` 必须实现相同接口。`AuthSession` 至少包含 accessToken、refreshToken、expiresAt、user 和 permissions；时间使用带偏移的 ISO 8601 字符串。

## 4. 任务依赖图

```text
FE-001 → FE-002 → FE-003 → FE-004
                    ├→ FE-005
                    ├→ FE-006
                    └→ FE-007
FE-004..FE-007 → FE-008
```

## TASK-FE-001 初始化 Vue 3 工程与质量门禁

**状态：** 可派遣

**目标：** 在 `src/frontend` 创建 Vue 3、TypeScript、Vite 工程并建立 package scripts、格式、lint、类型检查、单测和生产构建门禁。

**输入文档：** 蓝图 04、28、29；TASK-BASE-006 的端口与 API Base URL。

**依赖：** TASK-BASE-006。

**允许修改范围：** `src/frontend/**`、前端 CI 配置和前端测试目录；不得修改后端业务代码。

**预期输出：** 可安装、启动和生产构建的单一工程；Vue、TypeScript、Vite、Pinia、Vue Router、Element Plus、Vitest 及测试环境配置。

**验证与证据：** 记录包管理器与 Node 版本，执行 clean install、lint、typecheck、unit test、build；记录退出码、测试数量和产物目录。

**结果回写：** 回写确定的包管理器、版本、scripts 和目录结构。

**建议提交：** `feat(frontend): initialize unified vue application`

## TASK-FE-002 实现应用配置、HTTP 与统一错误层

**状态：** 可派遣

**目标：** 建立类型安全的运行配置、HTTP 客户端、请求关联标识和统一错误分类。

**输入文档：** TASK-BASE-006 API 契约；蓝图 27、30。

**依赖：** TASK-FE-001。

**允许修改范围：** `src/frontend/src/api/**`、`utils/**`、`types/**`、环境变量示例和测试。

**预期输出：** API Base URL 配置、请求/响应拦截、超时、取消、ApiError 分类、TraceId 提取和安全日志规则。

**验证与证据：** 提供成功、网络失败、超时、业务错误、401、403、未知响应和 TraceId 单元测试；证明日志不输出令牌。

**结果回写：** 回写环境变量名、错误类型、超时和 TraceId 约定。

**建议提交：** `feat(frontend): add typed api client and errors`

## TASK-FE-003 实现认证边界与 Mock 登录

**状态：** 可派遣

**目标：** 实现 `AuthGateway`、`MockAuthGateway`、AuthStore、会话持久化和路由认证状态，保持未来真实 API 可替换。

**输入文档：** 本文件稳定前端契约；Identity 蓝图 13 与实施文档 03。

**依赖：** TASK-FE-002。

**允许修改范围：** 前端 auth api/store/types/mock、路由守卫和测试。

**预期输出：** 登录、刷新、退出、当前用户与权限接口；集中 Mock 数据；开发模式标识；生产构建默认关闭 Mock。

**验证与证据：** 提供登录成功/失败、会话恢复、过期、退出、受保护路由、生产禁用 Mock 和契约夹具测试。

**结果回写：** 回写 AuthGateway 签名、AuthSession 字段、存储策略和 Phase 3 替换点。

**建议提交：** `feat(frontend): add mock authentication boundary`

## TASK-FE-004 实现 PC 管理框架

**状态：** 可派遣

**目标：** 实现 PC 端侧边菜单、顶部栏、面包屑/标题、内容区、折叠和基础响应式布局。

**输入文档：** 蓝图 04 第 6、7 节；TASK-FE-003 用户与权限状态。

**依赖：** TASK-FE-003。

**允许修改范围：** 前端 layouts、components、router、styles、assets 和测试。

**预期输出：** `PcLayout`、导航模型、用户菜单、退出入口、Mock 模式横幅和可扩展业务菜单槽位。

**验证与证据：** 提供菜单展开/折叠、路由高亮、键盘导航、退出、窄屏布局和权限隐藏测试；保存关键视口截图。

**结果回写：** 回写布局断点、菜单模型、CSS 变量和可访问性结论。

**建议提交：** `feat(frontend): add pc administration shell`

## TASK-FE-005 实现登录、首页、403 与 404 页面

**状态：** 可派遣

**目标：** 完成第一批 PC 页面，并明确首页数据为静态或 Mock。

**输入文档：** TASK-FE-003、TASK-FE-004；产品视觉约定。

**依赖：** TASK-FE-004。

**允许修改范围：** 前端 views/pages、dashboard 组件、路由和页面测试。

**预期输出：** 登录表单、加载/错误状态、首页欢迎与基础卡片、403 返回入口、404 导航入口；不展示伪造的真实生产指标。

**验证与证据：** 提供必填/密码显示、重复提交保护、登录跳转、403/404、刷新保持和键盘操作测试；保存 PC 截图。

**结果回写：** 回写页面路由、文案、状态和首页数据来源标识。

**建议提交：** `feat(frontend): add initial platform pages`

## TASK-FE-006 实现 PDA 基础壳

**状态：** 可派遣

**目标：** 建立适合现场触控的大尺寸控件、精简导航和占位首页，不实现扫码或业务操作。

**输入文档：** 蓝图 04 第 6.2、7、10 节；TASK-FE-003 会话状态。

**依赖：** TASK-FE-003。

**允许修改范围：** 前端 pda、layouts、device、router、styles 和测试。

**预期输出：** `PdaLayout`、PDA 首页、返回/退出、Mock 标识、手动终端切换和触控尺寸变量。

**验证与证据：** 提供 PDA 路由、受保护访问、终端覆盖、触控目标尺寸、横竖屏和键盘可用性测试；保存目标视口截图。

**结果回写：** 回写 PDA 断点、目标视口、导航和触控变量。

**建议提交：** `feat(frontend): add pda application shell`

## TASK-FE-007 实现 Mobile 基础壳

**状态：** 可派遣

**目标：** 建立移动端顶部栏、底部 Tab 和占位首页，不实现审批、消息或原生能力。

**输入文档：** 蓝图 04 第 6.3、7、12、13 节；TASK-FE-003 会话状态。

**依赖：** TASK-FE-003。

**允许修改范围：** 前端 mobile、layouts、device、router、styles 和测试。

**预期输出：** `MobileLayout`、Mobile 首页、基础 Tab、退出入口、Mock 标识和安全区域样式。

**验证与证据：** 提供 Mobile 路由、受保护访问、Tab 高亮、安全区域、窄屏和键盘可用性测试；保存目标视口截图。

**结果回写：** 回写 Mobile 断点、目标视口、Tab 模型和安全区域策略。

**建议提交：** `feat(frontend): add mobile application shell`

## TASK-FE-008 完成三端集成与第一批验收

**状态：** 可派遣

**目标：** 集成 PC/PDA/Mobile 路由、终端识别和运行基线，完成第一批质量与可访问性验收。

**输入文档：** TASK-FE-001 至 TASK-FE-007；TASK-BASE-006 启动说明。

**依赖：** TASK-FE-004、TASK-FE-005、TASK-FE-006、TASK-FE-007。

**允许修改范围：** 前端集成、自动化测试、CI、README 和本文件执行记录；不得加入业务页面。

**预期输出：** 三端入口、自动/手动终端选择、Mock 登录端到端路径、生产构建、截图基线和 Phase 3 Identity 接入清单。

**验证与证据：** 执行 clean install、lint、typecheck、unit、build 和 Playwright 冒烟；验证登录到三端首页、403/404、刷新、退出和生产禁用 Mock，记录测试总数。

**结果回写：** 将第一批状态改为已完成，登记提交、截图、验证证据和 Identity 接入阻塞项。

**建议提交：** `feat(frontend): complete initial unified shell`

## 5. 完成标准

- PC 登录页、管理框架、首页、403、404 可用。
- PDA 和 Mobile 基础壳可通过明确路由访问。
- Mock 登录只经 `AuthGateway` 使用，生产默认关闭并有显式开发标识。
- lint、typecheck、unit、build 和三端关键路径冒烟测试全部通过。
- 未提前实现任何业务页面或设备/离线能力。

## 6. 执行记录

| 任务 | 状态 | 执行者/任务 | 提交 | 验证证据 | 结果回写 |
| --- | --- | --- | --- | --- | --- |
| TASK-FE-001 | 可派遣 | - | - | - | - |
| TASK-FE-002 | 可派遣 | - | - | - | - |
| TASK-FE-003 | 可派遣 | - | - | - | - |
| TASK-FE-004 | 可派遣 | - | - | - | - |
| TASK-FE-005 | 可派遣 | - | - | - | - |
| TASK-FE-006 | 可派遣 | - | - | - | - |
| TASK-FE-007 | 可派遣 | - | - | - | - |
| TASK-FE-008 | 可派遣 | - | - | - | - |
