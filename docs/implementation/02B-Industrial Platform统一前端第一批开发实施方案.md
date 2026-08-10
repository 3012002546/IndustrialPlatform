# 02B-Industrial Platform统一前端第一批开发实施方案

# Industrial Platform 统一前端第一批开发实施方案

> 当前里程碑范围：建立 PC、PDA、Mobile 三端统一前端基础壳；业务页面和真实 Identity 登录留待后续阶段。

版本：V2.1

阶段：Phase 2（可运行基线之后、Identity 业务功能之前）

模块：

```text
Industrial Platform Unified Frontend
```

技术：

```text
Vue 3
TypeScript
Vite
Pinia
Vue Router
Element Plus
```

规格与蓝图依据：

- `docs/superpowers/specs/2026-08-09-runnable-baseline-first-development-sequence-design.md`

- `docs/blueprint/04-Vue3 PCPDAMobile 三端统一架构设计.md`
- `docs/blueprint/27-Industrial Platform API规范.md`
- `docs/blueprint/28-Industrial Platform前端工程规范.md`
- `docs/blueprint/29-Industrial Platform自动化测试体系.md`
- `docs/blueprint/30-Industrial Platform日志审计与可观测性平台设计.md`

- `README.md`“前端 API 契约（Phase 2 输入）”
- `docs/implementation/02A-Industrial Platform可运行基线开发实施方案.md`

---

# 1. 文档说明

## 1.1 文档目的

本文档定义 Industrial Platform 第一批统一前端的：

- 建设范围与明确边界
- 技术选型与工程结构
- 应用装配、API、认证、路由和终端适配设计
- PC、PDA、Mobile 三端基础壳设计
- 错误、安全、可访问性和测试要求
- 可独立派遣的开发任务及验收标准

目标不是一次完成全部 MES 前端，而是先建立后续业务页面共同依赖的稳定前端基础。

## 1.2 当前输入状态

- `src/frontend` 已完成 Vue 3 + TypeScript + Vite 单包工程以及 PC、PDA、Mobile 三端基础壳。
- Gateway 统一入口为 `http://localhost:5080`。
- Gateway 已允许前端开发地址 `http://localhost:5173` 和预览地址 `http://localhost:4173`。
- Identity 当前只有服务骨架和健康检查，尚未提供真实登录 API。
- 第一批使用显式 Mock 登录；TASK-FE-001～010 均已完成，最新历史证据为 2026-08-10 的 unit 212/212、E2E 35/35。
- 本轮仅整理文档，不将既有构建、测试和截图结果表述为重新验证；真实 Identity、Docker 联调和真机 safe-area 保持后续阶段或外部环境待验收。

## 1.3 文档使用方式

本文件前半部分是统一前端开发设计，后半部分是开发任务拆分。

任务执行者必须同时遵守：

```text
全局设计章节
+
被派遣任务的目标与范围
+
被派遣任务的验证与证据要求
```

发现设计冲突或需要扩大范围时，不得自行改变稳定契约；任务状态改为“设计待确认”并回写冲突。

---

# 2. 定位、目标与职责边界

## 2.1 建设目标

建立一个可安装、可启动、可测试、可生产构建的 Vue 3 前端工程，形成以下演示闭环：

```text
访问应用
    ↓
显式 Mock 登录
    ↓
进入 PC / PDA / Mobile 对应首页
    ↓
验证受保护路由、403、404、刷新与退出
```

第一批完成后，Phase 3 应只需新增 `HttpAuthGateway` 并切换装配，不重写登录页、AuthStore、路由守卫或三端布局。

## 2.2 包含范围

- Vue 3、TypeScript、Vite 单包工程。
- Pinia、Vue Router、Element Plus。
- ESLint、Prettier、Vitest、Vue Test Utils、MSW、Playwright。
- 应用配置、HTTP 客户端、统一 API 错误模型和 TraceId 展示。
- `AuthGateway`、`MockAuthGateway`、AuthStore 和开发会话恢复。
- 公共路由、受保护路由、权限守卫和终端识别。
- PC 管理框架、登录页、Mock 首页、403、404。
- PDA 基础布局和占位首页。
- Mobile 基础布局和占位首页。
- 三端单元、组件、契约和关键路径 E2E 测试。

## 2.3 不包含范围

- Identity 真实登录、JWT 刷新、服务端注销和用户/角色/权限管理。
- ReferenceData、MasterData、OperationalData、工单、称量、设备和追溯业务页面。
- 离线队列、IndexedDB、扫码、RFID、打印、蓝牙、NFC。
- SignalR、SSE、Capacitor、Vant、Tailwind CSS、ECharts。
- 复杂看板、低代码渲染器、深色主题和前端容器化部署。
- 后端代码、Gateway 路由和基础设施修改。

---

# 3. 前后端及跨服务协作目标与前端设计原则

## 3.1 单一工程

第一批采用：

```text
一个 Vue 应用
+
共享核心层
+
PC / PDA / Mobile 三套布局
```

不创建三个独立前端工程，也不在第一批引入 pnpm workspace。

蓝图 28 的 Monorepo 结构作为后期出现独立发布、独立团队或独立依赖需求时的候选方案，不作为当前前置条件。

## 3.2 分层访问

页面访问数据必须遵循：

```text
Page / Component
        ↓
Store / Use Case
        ↓
Gateway / API Client
        ↓
Backend API 或 Mock Adapter
```

禁止：

- 页面直接调用 Axios。
- 页面直接读取或写入 token。
- Mock 数据散落在页面组件。
- Store 判断 HTTP 状态字符串。
- 三端布局复制认证和错误处理逻辑。

## 3.3 共享与隔离

共享：

- API、认证、状态、类型、基础组件和设计 Token。

隔离：

- PC、PDA、Mobile 的布局、导航和终端专属页面。

业务逻辑可以共享，终端交互不得为了复用而强行使用同一页面结构。

---

# 4. 总体架构、技术选型与运行基线

## 4.1 工具链

| 项目 | 约定 |
| --- | --- |
| Node.js | `>=24.18.0 <25` |
| 包管理器 | pnpm `11.16.0` |
| UI 框架 | Vue 3 + Element Plus |
| 构建 | Vite |
| 类型系统 | TypeScript strict |
| 状态 | Pinia |
| 路由 | Vue Router |
| HTTP | Axios，经统一客户端封装 |
| 单元/组件测试 | Vitest + Vue Test Utils |
| API Mock | MSW |
| E2E | Playwright Chromium |

依赖实际版本由 `package.json` 声明并通过 `pnpm-lock.yaml` 固定。禁止同时提交 npm 或 yarn 锁文件。

## 4.2 TypeScript规范

必须开启：

```json
{
  "strict": true,
  "noUncheckedIndexedAccess": true,
  "exactOptionalPropertyTypes": true
}
```

生产源码禁止：

```text
any
@ts-ignore
无理由 ESLint disable
```

页面和组件统一使用 Composition API 与：

```vue
<script setup lang="ts">
```

## 4.3 稳定工程命令

```text
pnpm format:check
pnpm lint
pnpm typecheck
pnpm test:unit
pnpm test:unit:coverage
pnpm test:e2e
pnpm build
pnpm preview
```

后续任务不得随意改名。覆盖率按语句、分支、函数和行分别不低于 70%。

---

# 5. 工程结构

位置：

```text
src/frontend
```

目标结构：

```text
src/frontend
├── .env.example
├── index.html
├── package.json
├── pnpm-lock.yaml
├── playwright.config.ts
├── vite.config.ts
├── vitest.config.ts
├── tsconfig*.json
├── eslint.config.js
├── README.md
├── src
│   ├── main.ts
│   ├── App.vue
│   ├── app
│   ├── api
│   ├── auth
│   ├── components
│   │   ├── base
│   │   └── navigation
│   ├── config
│   ├── device
│   ├── layouts
│   ├── pages
│   │   ├── public
│   │   ├── pc
│   │   ├── pda
│   │   └── mobile
│   ├── router
│   ├── stores
│   ├── styles
│   └── types
└── tests
    ├── fixtures
    ├── unit
    ├── components
    ├── contract
    └── e2e
```

职责：

| 目录 | 职责 |
| --- | --- |
| `app` | 应用创建和依赖装配 |
| `api` | HTTP 传输、信封解包和错误分类 |
| `auth` | 认证端口、Mock 实现和会话存储 |
| `components/base` | 无业务语义的公共组件 |
| `device` | 终端识别和开发覆盖 |
| `layouts` | PC、PDA、Mobile 布局 |
| `pages` | 公共页面和终端页面 |
| `router` | 路由定义、meta 和守卫 |
| `stores` | 应用状态，不包含 HTTP 细节 |
| `styles` | Token、基础样式和终端样式 |

---

# 6. 全局技术约束与应用装配设计

应用入口：

```text
main.ts
    ↓
loadRuntimeConfig()
    ↓
createIndustrialApp()
    ↓
Pinia + Router + Gateway + Element Plus
    ↓
mount
```

`main.ts` 只负责读取配置、创建和挂载应用。

生产入口和测试必须使用同一个 `createIndustrialApp()`，避免测试装配与真实运行不一致。

基础组件第一批包括：

```text
AppPage
AppEmptyState
AppErrorAlert
MockModeBanner
```

这些组件不得包含 MES 业务语义。

---

# 7. 运行配置设计

## 7.1 环境变量

只允许：

| 环境变量 | 默认值 | 说明 |
| --- | --- | --- |
| `VITE_API_BASE_URL` | `http://localhost:5080` | Gateway 统一入口 |
| `VITE_AUTH_MODE` | `mock`（仅开发/测试） | 认证适配器选择 |
| `VITE_REQUEST_TIMEOUT_MS` | `10000` | HTTP 超时毫秒数 |

配置必须由 `loadRuntimeConfig()` 统一解析，业务代码不得直接读取 `import.meta.env`。

## 7.2 配置校验

- Base URL 必须是合法的 HTTP/HTTPS URL。
- 超时必须是正整数。
- 生产构建启用 `mock` 必须失败，不得静默切换。
- `.env.example` 只能包含安全示例值，不提交真实凭据。

---

# 8. API层设计

## 8.1 统一返回模型

```typescript
export interface ApiResult<T> {
    success: boolean;
    code: string;
    message: string;
    data: T | null;
}
```

规则：

- 2xx 且 `success=true` 才返回类型化 `data`。
- 2xx 但信封非法时按 `invalidResponse` 处理。
- 非 2xx 业务信封保留 `code` 和 `message`。
- 页面和 Store 不接触 Axios 错误对象。

## 8.2 统一错误模型

```typescript
export type ApiErrorKind =
    | 'network'
    | 'timeout'
    | 'business'
    | 'unauthorized'
    | 'forbidden'
    | 'notFound'
    | 'server'
    | 'invalidResponse'
    | 'cancelled'
    | 'unknown';

export interface ApiErrorDetails {
    kind: ApiErrorKind;
    message: string;
    status?: number;
    code?: string;
    traceId?: string;
    correlationId: string;
}
```

映射：

| 场景 | 分类 |
| --- | --- |
| 无响应 | `network` |
| 超时 | `timeout` |
| 主动取消 | `cancelled` |
| 401 | `unauthorized` |
| 403 | `forbidden` |
| 404 | `notFound` |
| 5xx | `server` |
| `success=false` | `business` |
| 非法信封 | `invalidResponse` |

## 8.3 请求关联

- 每次请求生成 UUID `correlationId`。
- 请求头使用 `X-Correlation-Id`。
- 可用时从响应体、`X-Trace-Id` 或 `traceparent` 提取 TraceId。
- 错误页和错误提示可展示 TraceId，便于前后端排查。
- 日志不得输出 Authorization、password、accessToken 或 refreshToken。

---

# 9. 认证边界设计

登录数据流：

```text
LoginPage
    ↓
AuthStore
    ↓
AuthGateway
    ├── MockAuthGateway（Phase 2）
    └── HttpAuthGateway（Phase 3）
```

## 9.1 认证模型

```typescript
export interface LoginCommand {
    username: string;
    password: string;
}

export interface AuthUser {
    userId: string;
    username: string;
    displayName: string;
    tenantId: string;
    roles: string[];
    permissions: string[];
}

export interface AuthSession {
    accessToken: string;
    refreshToken: string;
    expiresAt: string;
    user: AuthUser;
}
```

`expiresAt` 使用带 `Z` 或明确偏移的 ISO 8601 字符串。解析失败视为无效会话。

## 9.2 AuthGateway

```typescript
export interface AuthGateway {
    login(command: LoginCommand): Promise<AuthSession>;
    refresh(refreshToken: string): Promise<AuthSession>;
    logout(): Promise<void>;
    getCurrentUser(): Promise<AuthUser>;
}
```

Phase 3 的 `HttpAuthGateway` 必须通过与 Mock 相同的契约测试，不得改变页面和 Store 的消费模型。

## 9.3 Mock登录

开发演示账号：

```text
用户名：mock.admin
密码：Mock@123456
```

权限：

```text
platform.home.view
platform.pda.view
platform.mobile.view
```

界面必须显示“开发 Mock 模式”和“仅本地开发演示账号”，不得将其描述为生产管理员。

---

# 10. 会话与权限设计

## 10.1 会话存储

Phase 2 使用：

```text
sessionStorage
```

键：

```text
industrial-platform.auth.mock.v1
```

损坏 JSON、未知版本、缺少字段、非法过期时间和已过期会话必须清除。

password 不得进入 Store 或 Storage。退出时即使 Gateway 调用失败，也必须清理本地会话。

## 10.2 AuthStore

AuthStore 对外提供：

```text
session
user
isAuthenticated
restore()
login()
refresh()
logout()
hasPermission()
```

并发恢复或刷新只能执行一次，避免重复请求和会话覆盖。

## 10.3 权限边界

第一批实现：

- 路由权限判断。
- 菜单项权限过滤。

第一批不实现：

- 完整按钮权限指令。
- 后端权限配置页面。

前端权限仅用于交互控制，不能替代后端授权。

---

# 11. 终端识别设计

终端类型：

```typescript
export type TerminalType = 'pc' | 'pda' | 'mobile';
```

## 11.1 自动识别

| 条件 | 终端 |
| --- | --- |
| 宽度 `>=1200px` | PC |
| 宽度 `<768px` | Mobile |
| 宽度 `768–1199px` 且支持触控 | PDA |
| 宽度 `768–1199px` 且不支持触控 | PC |

## 11.2 识别优先级

```text
显式 /pc、/pda、/mobile 路由
    ↓
开发期手动覆盖
    ↓
自动识别
```

显式访问三端路由时不得自动改写到其他终端。

手动覆盖键：

```text
industrial-platform.terminal.override.v1
```

允许值：

```text
pc
pda
mobile
auto
```

视口变化只更新建议终端，不强制中断当前操作并跳转。

---

# 12. 路由设计

## 12.1 路由表

| 路径 | 路由名 | 布局 | 鉴权 | 权限 |
| --- | --- | --- | --- | --- |
| `/` | `root` | 无 | 按会话 | 无 |
| `/login` | `login` | 无 | 公共 | 无 |
| `/403` | `forbidden` | 无 | 公共 | 无 |
| `/pc/home` | `pc-home` | PC | 是 | `platform.home.view` |
| `/pda/home` | `pda-home` | PDA | 是 | `platform.pda.view` |
| `/mobile/home` | `mobile-home` | Mobile | 是 | `platform.mobile.view` |
| `/:pathMatch(.*)*` | `not-found` | 无 | 公共 | 无 |

## 12.2 Route Meta

```typescript
interface RouteMeta {
    title: string;
    requiresAuth?: boolean;
    permission?: string;
    terminal?: TerminalType;
}
```

## 12.3 路由守卫

守卫顺序：

```text
恢复会话
    ↓
判断公共或受保护路由
    ↓
判断权限
    ↓
确认目标终端
    ↓
设置页面标题
```

无会话访问受保护路由时，可携带相对 `redirect`。登录后只允许跳转站内路径，禁止开放重定向。

---

# 13. 样式与可访问性设计

## 13.1 Design Token

统一定义：

```text
颜色
文字字号与行高
间距
圆角
阴影
焦点环
PC侧栏尺寸
PDA/Mobile触控尺寸
安全区域
```

页面不得重复散落同类魔法值。

## 13.2 可访问性

按 WCAG 2.2 AA 可适用条目验收：

- 键盘可完成登录、菜单和退出操作。
- 焦点始终可见。
- 表单控件具有 label。
- 页面具有语义标题和 landmark。
- 错误信息与输入项关联。
- 颜色对比度满足要求。
- `prefers-reduced-motion` 时减少非必要动画。

---

# 14. PC管理框架设计

目标用户：

```text
管理人员
配置人员
分析人员
```

布局：

```text
+------------------------------------------------+
| Header                                         |
+------------+-----------------------------------+
| Sidebar    | Breadcrumb / Page Title           |
|            |                                   |
|            | Main Content                      |
+------------+-----------------------------------+
```

尺寸：

| 项目 | 值 |
| --- | ---: |
| 顶栏 | `56px` |
| 侧栏展开 | `240px` |
| 侧栏折叠 | `64px` |
| 目标视口 | `1280×720`、`1440×900` |

第一批菜单只有：

```text
首页
```

导航模型：

```typescript
export interface NavigationItem {
    id: string;
    label: string;
    routeName: string;
    icon?: string;
    permission?: string;
    children?: NavigationItem[];
}
```

侧栏折叠键：

```text
industrial-platform.pc.sidebar.collapsed.v1
```

PC Layout 必须包含跳到主内容入口、路由高亮、用户菜单、终端信息、Mock 横幅和退出入口。

---

# 15. 公共页面与PC首页设计

## 15.1 登录页

页面状态：

```text
初始
校验失败
提交中
登录失败
登录成功
```

功能：

- 用户名和密码必填。
- 密码显示/隐藏。
- 提交中防重复点击。
- 登录失败显示统一错误。
- 支持安全的 redirect。
- 显示 Mock 模式和演示账号说明。

第一批不包含注册、忘记密码、验证码和真实 Identity 请求。

## 15.2 PC首页

只显示：

- 当前用户欢迎信息。
- 当前终端与开发环境状态。
- “业务指标将在后续阶段接入”的空状态。

禁止伪造产量、OEE、告警数或其他真实生产指标。

## 15.3 403页面

提供：

- 返回有权限首页。
- 重新登录。
- 可用时显示 TraceId。

## 15.4 404页面

提供：

- 返回首页。
- 返回上一页。
- 以纯文本展示原始路径。

原始路径必须进行 HTML 转义。

---

# 16. PDA基础壳设计

目标：

```text
现场触控
大按钮
少导航
横竖屏可用
```

目标视口：

```text
480×800
800×480
```

交互目标最小：

```text
48×48px
```

PDA Layout 包含：

- 顶栏。
- 首页、返回和退出入口。
- 当前用户、终端和 Mock 模式标识。
- 可滚动主内容区。

PDA 首页只显示“现场任务将在业务阶段接入”的空状态，不出现扫码、称量、工单等不可用按钮。

---

# 17. Mobile基础壳设计

目标视口：

```text
360×800
390×844
```

交互目标最小：

```text
44×44px
```

布局：

```text
顶部栏
    ↓
主内容区
    ↓
底部导航 + safe-area
```

第一批底部导航只有：

```text
首页
我的
```

“我的”展示当前用户和退出入口。任务、消息、审批和 AI 不得作为可点击假入口出现。

底部导航必须适配：

```css
env(safe-area-inset-bottom)
```

---

# 18. 错误与可观测性设计

## 18.1 用户错误反馈

| 错误 | 页面行为 |
| --- | --- |
| 网络错误 | 显示网络不可用和重试建议 |
| 超时 | 显示请求超时，不推断成功 |
| 401 | 清理无效会话并回登录 |
| 403 | 进入 403 页面 |
| 404 | 资源请求显示未找到，路由进入 404 |
| 5xx | 显示服务错误和 TraceId |
| 取消请求 | 静默或保持当前状态，不显示网络故障 |

页面不得通过字符串包含关系识别错误。

## 18.2 控制台要求

验收时不得出现：

- 浏览器 console error。
- Vue warning。
- 未处理 Promise rejection。
- 非预期失败网络请求。
- token、password 或真实凭据日志。

---

# 19. 前端安全设计

必须覆盖：

- 生产构建禁止 Mock 认证。
- redirect 只允许站内相对路径。
- token 和 password 不进入日志。
- Mock token 只保存在 `sessionStorage`。
- 404 原始路径作为文本展示。
- 不使用 `v-html` 展示服务端错误信息。
- 环境变量不包含真实密钥。

Phase 2 的会话存储只用于开发 Mock。Phase 3 必须结合 Identity API 单独确认真实令牌的存储、刷新和撤销策略。

---

# 20. 自动化测试设计

## 20.1 测试层次

| 类型 | 重点 |
| --- | --- |
| Unit | 配置解析、错误映射、终端识别、会话校验 |
| Component | 登录、基础组件、菜单、三端布局 |
| Contract | `AuthGateway`、ApiResult、ApiError、路由 meta |
| E2E | 登录、三端首页、403、404、刷新、退出 |

## 20.2 必测场景

认证：

- 登录成功与失败。
- 会话恢复、过期和损坏数据清理。
- 刷新成功、失败和并发控制。
- 退出始终清理本地会话。

路由：

- 未登录访问受保护页面。
- 已登录访问登录页。
- 有权限和无权限。
- 安全与恶意 redirect。
- 直接输入 URL、刷新、前进和后退。

三端：

- PC `1280×720`、`1440×900`。
- PDA `480×800`、`800×480`。
- Mobile `360×800`、`390×844`。
- PDA 48px、Mobile 44px 触控目标。
- Mobile safe-area。

API：

- 成功、业务失败、网络、超时、取消。
- 401、403、404、5xx。
- 非法响应信封和 TraceId。
- 敏感信息脱敏。

## 20.3 质量门禁

```text
clean install
    ↓
format check
    ↓
lint
    ↓
typecheck
    ↓
unit + coverage
    ↓
build
    ↓
preview E2E
```

每个命令必须记录退出码、测试数量和报告位置。

---

# 21. 开发任务依赖

```text
FE-001 → FE-002 → FE-003 → FE-004 → FE-005
                                      ├→ FE-006 → FE-007
                                      ├→ FE-008
                                      └→ FE-009

FE-007 + FE-008 + FE-009 → FE-010
```

说明：

- FE-006 完成后，FE-007 可独立执行。
- FE-005 完成后，FE-006、FE-008、FE-009 可以并行。
- 并行任务不得同时修改同一布局文件；路由最终集成由 FE-010 完成。
- FE-010 是阶段验收，不替代前置任务自身验证。

---

# 22. 开发任务拆分

## TASK-FE-001 创建Vue工程与质量门禁

**状态：** 可派遣

**目标：** 将 `src/frontend` 从占位目录初始化为 Vue 3、TypeScript、Vite 单包工程，并建立可复现的质量命令。

**输入文档：** 本文第 4、5、20 节；TASK-BASE-006 的端口与 Gateway 契约。

**依赖：** TASK-BASE-006 已完成；Node.js 和 pnpm 满足第 4.1 节。

**允许修改范围：** `src/frontend/**`、仅与前端产物有关的根 `.gitignore`；不得修改后端和部署脚本。

**预期输出：** Vue/Vite 工程、严格 TypeScript、Pinia、Router、Element Plus、Axios、Vitest、MSW、Playwright、锁文件、八个稳定命令和更新后的 README。

**验证与证据：** 执行 `pnpm install --frozen-lockfile`、Playwright Chromium 安装、format、lint、typecheck、unit、coverage 和 build；记录 Node/pnpm/依赖版本、退出码、测试数、覆盖率和 `dist` 摘要。

**结果回写：** 更新任务状态、实际依赖版本、scripts 和目录偏差。

**建议提交：** `feat(frontend): initialize unified vue application`

---

## TASK-FE-002 实现应用装配、Design Token与基础组件

**状态：** 可派遣

**目标：** 建立统一应用创建入口、全局样式和无业务语义的公共组件。

**输入文档：** 本文第 6、13 节。

**依赖：** TASK-FE-001。

**允许修改范围：** `src/frontend/src/app/**`、`components/base/**`、`styles/**`、`main.ts`、`App.vue` 和对应测试。

**预期输出：** `createIndustrialApp()`、全局 Token、焦点与 reduced-motion 基线、`AppPage`、`AppEmptyState`、`AppErrorAlert`、`MockModeBanner`。

**验证与证据：** 验证应用只装配一次、组件语义、键盘焦点、TraceId 展示、Mock 横幅和三端 Token；运行组件测试和全量质量门禁。

**结果回写：** 回写 Token 名称、组件接口和可访问性偏差。

**建议提交：** `feat(frontend): add application foundation and design tokens`

---

## TASK-FE-003 实现运行配置、HTTP与统一错误层

**状态：** 可派遣

**目标：** 建立类型安全运行配置和唯一 HTTP 入口，统一响应解包、错误分类与请求关联。

**输入文档：** 本文第 7、8、18、19 节；Gateway API 契约。

**依赖：** TASK-FE-002。

**允许修改范围：** `src/frontend/src/config/**`、`api/**`、`types/api.ts`、`.env.example` 和对应测试。

**预期输出：** `RuntimeConfig`、`loadRuntimeConfig()`、`HttpClient`、`ApiError`、correlationId、TraceId 提取和敏感日志脱敏。

**验证与证据：** 覆盖合法/非法配置、成功信封、业务失败、网络、超时、取消、401/403/404/5xx、非法信封和 TraceId；证明日志不包含 token、password 和 Authorization。

**结果回写：** 回写环境变量、超时、错误模型、请求头和 TraceId 优先级。

**建议提交：** `feat(frontend): add typed runtime and api client`

---

## TASK-FE-004 实现认证边界、Mock登录与会话状态

**状态：** 可派遣

**目标：** 实现稳定 `AuthGateway`、Mock 适配器、AuthStore 和版本化会话存储。

**输入文档：** 本文第 9、10、19 节；Identity Phase 3 边界。

**依赖：** TASK-FE-003。

**允许修改范围：** `src/frontend/src/auth/**`、`stores/authStore.ts`、认证契约测试和 Mock fixtures。

**预期输出：** 登录、刷新、退出、当前用户、权限判断、会话恢复/过期/损坏清理和可复用 AuthGateway 契约测试。

**验证与证据：** 覆盖正确/错误登录、刷新成功/失败/并发、恢复、过期、损坏数据、退出和权限判断；证明 password 不进入 Store/Storage，生产配置不能启用 Mock。

**结果回写：** 回写 AuthGateway 签名、AuthStore 公共 API、会话键、版本和 Phase 3 替换点。

**建议提交：** `feat(frontend): add mock authentication boundary`

---

## TASK-FE-005 实现终端识别、路由与访问守卫

**状态：** 可派遣

**目标：** 实现 PC/PDA/Mobile 识别、开发覆盖、稳定路由表和鉴权/权限守卫。

**输入文档：** 本文第 11、12 节。

**依赖：** TASK-FE-004。

**允许修改范围：** `src/frontend/src/device/**`、`router/**`、`stores/deviceStore.ts` 和对应测试；页面只允许最小测试桩。

**预期输出：** `TerminalType`、`detectTerminal()`、覆盖存储、六类稳定路由、Route Meta 和唯一全局守卫。

**验证与证据：** 覆盖三档宽度、触控组合、显式路由、四类覆盖值、登录状态、权限、redirect、刷新、前进/后退和重定向循环。

**结果回写：** 回写断点、优先级、覆盖键、路由名称和守卫决策。

**建议提交：** `feat(frontend): add terminal routing and guards`

---

## TASK-FE-006 实现PC管理框架

**状态：** 可派遣

**目标：** 实现 PC 侧栏、顶栏、页面标题、内容区、折叠和用户菜单。

**输入文档：** 本文第 13、14 节；TASK-FE-005 路由和权限状态。

**依赖：** TASK-FE-005。

**允许修改范围：** `layouts/PcLayout.vue`、`components/navigation/**`、PC 样式、PC route record 和对应测试。

**预期输出：** PC Layout、首页导航模型、折叠持久化、路由高亮、用户/终端信息、Mock 横幅和退出入口。

**验证与证据：** 覆盖展开/折叠、刷新保持、权限过滤、键盘导航、退出和两个 PC 目标视口；保存关键截图。

**结果回写：** 回写 PC 尺寸、导航模型、折叠键和可访问性结论。

**建议提交：** `feat(frontend): add pc administration shell`

---

## TASK-FE-007 实现登录、PC首页、403与404页面

**状态：** ✅ 已完成(2026-08-10)

**目标：** 完成公共页面和 PC Mock 首页，形成从登录到退出的 PC 演示闭环。

**输入文档：** 本文第 9、12、15、18 节。

**依赖：** TASK-FE-006。

**允许修改范围：** `pages/public/**`、`pages/pc/PcHomePage.vue`、页面组件、样式和测试。

**预期输出：** 登录表单、提交/错误状态、无伪指标 PC 首页、403、404 和 PC 关键路径 E2E。

**验证与证据：** 覆盖必填、密码显隐、重复提交、错误账号、安全 redirect、刷新保持、403、404、退出和键盘登录；保存 PC 截图和 E2E 报告。

**结果回写：** 回写页面状态、文案、路由行为、数据来源标识和可访问性结果。

**建议提交：** `feat(frontend): add initial pc pages`

---

## TASK-FE-008 实现PDA基础壳

**状态：** 可派遣

**目标：** 实现适合现场触控、横竖屏可用的 PDA 基础布局和占位首页。

**输入文档：** 本文第 11、13、16 节。

**依赖：** TASK-FE-005。

**允许修改范围：** `layouts/PdaLayout.vue`、`pages/pda/**`、PDA 专属组件、样式、route record 和测试。

**预期输出：** PDA 顶栏、首页/返回/退出、Mock 标识、48px 触控基线和无伪业务入口的占位首页。

**验证与证据：** 覆盖鉴权、权限、显式 PDA 路由、480×800、800×480、触控尺寸、横竖屏、键盘和退出；保存两类截图。

**结果回写：** 回写 PDA 视口、导航、方向适配、触控结果和暂缓能力。

**建议提交：** `feat(frontend): add pda application shell`

---

## TASK-FE-009 实现Mobile基础壳

**状态：** 可派遣

**目标：** 实现安全区域适配的 Mobile 顶栏、底部导航和占位首页。

**输入文档：** 本文第 11、13、17 节。

**依赖：** TASK-FE-005。

**允许修改范围：** `layouts/MobileLayout.vue`、`pages/mobile/**`、Mobile 专属组件、样式、route record 和测试。

**预期输出：** Mobile Layout、首页/我的、用户和退出入口、44px 触控基线、safe-area 和无虚假任务/消息/审批入口的占位首页。

**验证与证据：** 覆盖鉴权、权限、Tab 高亮、360×800、390×844、触控尺寸、安全区域、键盘和退出；保存两类截图。

**结果回写：** 回写 Mobile 视口、Tab 模型、安全区域、触控结果和暂缓能力。

**建议提交：** `feat(frontend): add mobile application shell`

---

## TASK-FE-010 完成三端集成与第一批验收

**状态：** 可派遣

**目标：** 集成三端路由与布局，执行全量质量验收并形成 Phase 3 Identity 接入清单。

**输入文档：** TASK-FE-001 至 TASK-FE-009 全部输出；本文第 18 至 20 节。

**依赖：** TASK-FE-007、TASK-FE-008、TASK-FE-009。

**允许修改范围：** 前端集成点、自动化测试、前端 CI、`src/frontend/README.md` 和本文执行记录；不得增加新功能或修改后端。

**预期输出：** 三端入口、全量测试报告、六类目标视口截图、最新质量基线和 HttpAuthGateway/Identity 接入清单。

**验证与证据：** 执行 clean install、format、lint、typecheck、unit、coverage、build 和 preview E2E；验证登录成功/失败、三端首页、刷新、403、404、终端覆盖、退出、生产禁 Mock、控制台无错误及敏感日志；记录命令、退出码、耗时、测试数、覆盖率和报告路径。

**结果回写：** 更新全部 FE 任务状态、验证基线、Phase 3 接入点、外部环境待验收项和全部设计偏差。

**建议提交：** `feat(frontend): complete initial unified shell`

---

# 23. 完成标准

完成后必须达到：

- `src/frontend` 可用固定 Node/pnpm 版本完成 clean install。
- format、lint、typecheck、unit、coverage 和 build 全部通过。
- PC 登录页、管理框架、Mock 首页、403 和 404 可用。
- PDA、Mobile 基础壳可以通过明确路由访问。
- 三端目标视口无关键遮挡、裁切和非预期横向滚动。
- PDA/Mobile 触控尺寸和 safe-area 符合设计。
- Mock 登录只经 `AuthGateway` 使用，生产构建禁止 Mock。
- API、错误、认证、会话、路由和终端契约测试通过。
- 键盘、焦点、表单标签、语义结构和错误关联通过验收。
- 页面不显示伪造生产指标，不提前实现业务、扫码、离线或原生能力。
- Phase 3 可以通过新增 `HttpAuthGateway` 接入真实 Identity，不重写现有页面与布局。

若因浏览器、Gateway 或外部环境无法完成某项验证，只能标记为“待验收”，不得直接标记“已完成”。

---

# 24. 执行记录

| 任务 | 状态 | 执行者/任务 | 提交 | 验证证据 | 结果回写 |
| --- | --- | --- | --- | --- | --- |
| TASK-FE-001 | ✅ 完成(2026-08-10) | Claude Code | 待提交 `feat(frontend): initialize unified vue application` | Node 24.18.0 / pnpm 11.16.0(mise `.mise.toml` 钉定);`install --frozen-lockfile`/format:check/lint/typecheck/unit(4/4)/coverage(100%)/build/E2E(1 passed)全部退出码 0;Playwright Chromium 已装;dist 0.40k html + 967.50k js(gzip 311.68k) | 见下 |
| TASK-FE-002 | ✅ 完成(2026-08-10) | Claude Code | 待提交 `feat(frontend): add application foundation and design tokens` | format/lint/typecheck 通过;unit 20/20(新增 16);coverage 语句 95/分支 100/函数 100/行 94.7(阈值 70);build + E2E(1 passed)通过 | 见下 |
| TASK-FE-003 | ✅ 完成(2026-08-10) | Claude Code | 待提交 `feat(frontend): add typed runtime and api client` | format/lint/typecheck 通过;unit 71/71(12 文件,新增 runtimeConfig/correlation/redact/errors 单元 + httpClient 契约);coverage 语句 93.71 / 分支 89.61 / 函数 87.09 / 行 93.83(阈值 70);build + E2E(1 passed)通过 | 见下 |
| TASK-FE-004 | ✅ 完成(2026-08-10) | Claude Code | 待提交 `feat(frontend): add mock authentication boundary` | format/lint/typecheck 通过;unit 112/112(新增 41:authGateway 契约 7 + sessionStore 15 + authStore 15 + 其余);coverage 语句 95.18 / 分支 90.94 / 函数 92.18 / 行 96.29(阈值 70);build + E2E(1 passed)通过 | 见下 |
| TASK-FE-005 | ✅ 完成(2026-08-10) | Claude Code | 待提交 `feat(frontend): add terminal routing and guards` | format/lint/typecheck 通过;unit 141/141(18 文件,新增 device 8 + deviceStore 7 + routerGuards 15);coverage 语句 93.35 / 分支 90.03 / 函数 90.24 / 行 94.64(阈值 70);build + E2E(1 passed)通过 | 见下 |
| TASK-FE-006 | ✅ 完成(2026-08-10) | Claude Code | `b965dd1` `feat(frontend): add pc administration shell` | format/lint/typecheck 通过;unit 158/158(20 文件,新增 PcLayout 10 + PcNavMenu 6);coverage 语句 93.91 / 分支 90.79 / 函数 92.78 / 行 95.3(阈值 70);build + E2E(1 passed)通过 | 见下 |
| TASK-FE-007 | ✅ 完成(2026-08-10) | Claude Code | `da6d126` `feat(frontend): add initial pc pages` | format/lint/typecheck 通过;unit 182/182(24 文件,新增 LoginPage 9 + ForbiddenPage 6 + NotFoundPage 5 + PcHomePage 4);coverage 语句 95.71 / 分支 92.21 / 函数 94.53 / 行 96.58(阈值 70);build 通过;E2E 12/12(smoke 1 + pc 11,含两张 PC 首页截图 1280×720/1440×900) | 见下 |
| TASK-FE-008 | ✅ 完成(2026-08-10) | Claude Code | `feat(frontend): add pda application shell` | format/lint/typecheck 通过;unit 195/195(26 文件,新增 PdaLayout 8 + PdaHomePage 5);coverage 语句 96.02 / 分支 92.05 / 函数 94.92 / 行 96.81(阈值 70);build 通过;E2E 19/19(smoke 1 + pc 12 + pda 7,含两张 PDA 截图 480×800/800×480) | 见下 |
| TASK-FE-009 | ✅ 完成(2026-08-10) | Claude Code | `feat(frontend): add mobile application shell` | format/lint/typecheck 通过;unit 212/212(29 文件,新增 MobileLayout 8 + MobileHomePage 5 + MobileMyPage 4);coverage 语句 96.32 / 分支 91.81 / 函数 95.36 / 行 97.04(阈值 70);build 通过;E2E 27/27(smoke 1 + pc 12 + pda 7 + mobile 8,含两张 Mobile 截图 360×800/390×844) | 见下 |
| TASK-FE-010 | ✅ 完成(2026-08-10) | Claude Code | `feat(frontend): complete initial unified shell` | clean install(node 24.18.0/pnpm 11.16.0,frozen-lockfile)✅;format:check/lint/typecheck 退出码 0;unit 212/212(29 文件);coverage 语句 96.32/分支 91.81/函数 95.36/行 97.04(阈值 70);build 通过(dist js 1,027.38k gzip 332.29k);E2E 35/35(smoke 1+pc 12+pda 7+mobile 8+screens 7+console 1) | 见下 |

### FE-001 结果回写(2026-08-10)

- **工具链**:仓库根新增 `.mise.toml`,钉定 node 24.18.0 / pnpm 11.16.0(方案 §4.1 合规;原环境 node 22 已由 mise 项目级覆盖)。
- **实际依赖版本**:vue 3.5.41 / vue-router 4.6.4 / pinia 3.0.4 / element-plus 2.14.4 / axios 1.19.0;vite 8.2.1 / typescript 5.9.3 / vue-tsc 3.3.9 / vitest 4.1.10 / @vue/test-utils 2.4.11 / jsdom 30.0.1 / @playwright/test 1.62.1 / msw 2.15.0 / eslint 10.8.1 / typescript-eslint 8.66.0 / eslint-plugin-vue 10.10.0 / @vue/eslint-config-typescript 14.9.0 / prettier 3.9.6。锁文件 `pnpm-lock.yaml` 已生成,未提交 npm/yarn 锁。
- **版本选择说明**:typescript-eslint 8.66.0 的 peer 上限为 `typescript <6.1.0`,故不采用 registry 最新的 TS 7,而用 TS 5.9.3;vue-router/pinia 采用稳定 4.x/3.x 主版本,避免激进主版本破坏稳定基础。
- **scripts**:八个稳定命令全部就绪(`dev/format:check/lint/typecheck/test:unit/test:unit:coverage/test:e2e/build/preview`,另含 `format/test:unit:watch/build:app` 辅助命令)。
- **目录偏差**:`tests/unit|components|contract|e2e|fixtures` 均建立;`tests/e2e` 由 vitest include 排除(Playwright 专属),ESLint 覆盖 `**/*.{ts,mts,tsx,vue}`。
- **pnpm 11 构建脚本策略**:`pnpm-workspace.yaml` 用 `allowBuilds: { msw: true }`(msw postinstall 在未配置 `msw.workerDirectory` 时为空操作)。
- **已知注意项**:Element Plus 全量导入使主 chunk 967.50k(gzip 311.68k),build 有 chunk>500k 警告,属第一批可接受,留待后续按需引入/代码分割。

### FE-002 结果回写(2026-08-10)

- **装配**:`src/app/createIndustrialApp.ts` 提供唯一应用工厂(默认根组件 App.vue,可选 rootComponent/plugins 注入),装配 Pinia + Element Plus + 全局样式;`main.ts` 只调工厂并 mount。生产与测试同一装配入口。
- **Design Token**:`src/styles/tokens.css` 定义 `--ip-*` Token:品牌/中性色、字号与行高、间距、圆角、阴影、焦点环(`--ip-focus-ring-*`)、PC 顶栏 56px/侧栏 240px/折叠 64px、PDA 48px / Mobile 44px 触控最小尺寸、safe-area。`base.css` 增加 `:focus-visible` 焦点环与 `prefers-reduced-motion` 减少动画基线。
- **基础组件**(`components/base/`,无 MES 业务语义):`AppPage`(语义标题 + `aria-labelledby` 关联,useId)、`AppEmptyState`(role=status + 图标插槽 + 操作插槽)、`AppErrorAlert`(role=alert + TraceId 展示 + 操作插槽)、`MockModeBanner`(role=status,默认文案「开发 Mock 模式 · 仅本地开发演示账号」)。
- **测试**:20/20(新增 16);覆盖率语句 95 / 分支 100 / 函数 100 / 行 94.7,阈值 70 达标。覆盖率已确认计入 .vue 组件(v8 provider 默认包含,text reporter 仅显示未满覆盖文件属正常行为)。
- **已知偏差**:覆盖率报告中 createIndustrialApp.ts 第 31 行(for 插件循环体)未覆盖——测试未注入 plugins;属正常未覆盖分支,不影响门禁。

### FE-003 结果回写(2026-08-10)

- **运行配置**(`src/config/runtimeConfig.ts`):唯一解析入口,业务代码不读 `import.meta.env`。允许 `VITE_API_BASE_URL`(http/https 校验,默认 `http://localhost:5080`)/ `VITE_AUTH_MODE`(mock 默认|http)/ `VITE_REQUEST_TIMEOUT_MS`(正整数,默认 10000)。**生产构建启用 mock 抛 `RuntimeConfigError`**,不静默切换。非法输入抛带字段名与当前值的 `RuntimeConfigError`。
- **HTTP 层**(`src/api/httpClient.ts`):`createHttpClient` 是唯一网络入口,页面/Store 不得直接使用 Axios。每次请求注入 `X-Correlation-Id`(`headers` 优先 → 注入的 `getCorrelationId` → `crypto.randomUUID`);可选 `getToken` 注入 `Authorization: Bearer`(FE-004 接入)。2xx 且信封合法且 success 才解包 data;2xx 但信封非法 → `invalidResponse`;非 2xx 经 `normalizeError` 分类。
- **错误模型**(`src/api/errors.ts` + `src/types/api.ts`):`ApiError`(含 details:kind/message/status?/code?/traceId?/correlationId)是页面唯一接触的错误类型。`normalizeError` 映射:`ERR_CANCELED`→cancelled、`ETIMEDOUT`/`ECONNABORTED`→timeout、无 response→network、401/403/404→unauthorized/forbidden/notFound、5xx→server、非 2xx 业务信封→business(保留 code/message)、其余→invalidResponse/unknown。axios 1.19 的 `isAxiosError` 用独立导出的类型守卫(静态方法未在该版本暴露)。
- **TraceId 优先级**(`src/api/correlation.ts` + `envelope.ts`):响应体 `traceId` 字段 → `X-Trace-Id`/`x-trace-id` 响应头 → `traceparent` W3C 格式第 2 段,全部缺失为 undefined(不注入 undefined 以满足 exactOptionalPropertyTypes)。
- **敏感脱敏**(`src/api/redact.ts`):`redactHeaders`/`redactSensitive` 递归替换 `authorization`/`password`/`accessToken`/`refreshToken`/`token`/`cookie`/`x-api-key` 等为 `[REDACTED]`(键不区分大小写);契约测试证明日志不含 token、Authorization、Bearer。
- **测试稳定性**:MSW mock 传输层下 axios 的真实 socket 超时不触发(axios 仅对原生传输启用 connect-phase 定时器,否则退化为 socket idle 事件),故 timeout/cancelled 分类由 `tests/unit/errors.spec.ts` 用真实 `AxiosError` 实例确定性覆盖;`tests/contract/httpClient.spec.ts` 经 MSW 覆盖完整 HTTP 路径(成功信封、业务失败、401/403/404/5xx、非法信封、网络错误、取消、TraceId、敏感日志)。

### FE-004 结果回写(2026-08-10)

- **AuthGateway 契约**(`src/auth/types.ts`):`AuthGateway { login(LoginCommand); refresh(refreshToken); logout(); getCurrentUser() }`;`AuthUser` 含 userId/username/displayName/tenantId/roles/permissions;`AuthSession` 含 accessToken/refreshToken/expiresAt(ISO 8601,解析失败视为无效)/user。契约测试封装为 `runAuthGatewayContractSuite(factory)`,Phase 3 HttpAuthGateway 追加一次调用即可复用,不改变页面/Store 消费模型。
- **MockAuthGateway**(`src/auth/mockAuthGateway.ts`):固定演示账号 `mock.admin / Mock@123456`,displayName「Mock 演示账号」,权限 `platform.home.view` / `platform.pda.view` / `platform.mobile.view`;登录失败/刷新失败抛 `ApiError(business)`,错误码 `AUTH_1001`(凭据错误,通用文案不泄露具体哪项错误)/ `AUTH_1002`(刷新令牌无效);token 为 `mock.at./mock.rt.` 前缀的防猜测格式;`delayMs`(默认 0,应用装配时可调)/`now`/`sessionDurationMs`(默认 1h)可注入,测试传 0 保证确定性。
- **版本化会话存储**(`src/auth/sessionStore.ts`):键 `industrial-platform.auth.mock.v1`,载荷含 `version: 1`。损坏 JSON、非对象、未知版本、缺少字段、permissions 非字符串数组、非法/已过期时间一律判无效(返回 null)。Store 恢复时对无效数据物理清理(removeItem)。
- **AuthStore**(`src/stores/authStore.ts`,Pinia setup store):公共 API 为 `session` / `user` / `isAuthenticated` / `restore()` / `login()` / `refresh()` / `logout()` / `hasPermission()`。**单飞**:并发 restore 只读一次存储,并发 refresh 只调一次网关(`restorePromise`/`refreshPromise` 闭包变量,Per-instance 不跨 Store 泄漏)。刷新失败视为会话不可续,清理本地会话后抛出;无会话刷新抛 `unauthorized`。退出在 `finally` 清理,即使 Gateway 失败也保证清空。
- **password 边界**:password 只进入 `AuthGateway.login(command)` 参数,不进入 Store 状态、不进入 sessionStorage(测试断言 `JSON.stringify` 与存储内容均不含密码)。
- **Phase 3 替换点**:`src/auth/gateway.ts` 提供 `setAuthGateway/getAuthGateway`(装配点在应用工厂按 authMode 选择,HttpAuthGateway 实现后替换)与 `setCurrentSession/getCurrentSession` 令牌镜像——HTTP 层 `createHttpClient` 的 `getToken` 注入点(FE-003 已预留)后续从此读取,Store 无需改动。
- **生产禁用 Mock**:由运行配置层保证(FE-003 `parseRuntimeConfig` 生产 + mock 抛 `RuntimeConfigError`,已测试);应用工厂按 authMode 装配网关的计划在 FE-005 落地。

### FE-005 结果回写(2026-08-10)

- **断点**(`src/device/detect.ts`):纯函数 `detectTerminal(width, hasTouch)` — `>=1200`→PC、`<768`→Mobile、`768–1199` 触控→PDA / 无触控→PC。视口读取统一走 `getViewportInfo()`(SSR/matchMedia 缺失安全降级 1280/无触控)。
- **覆盖**(`src/device/override.ts`):键 `industrial-platform.terminal.override.v1`(localStorage),允许值 `pc|pda|mobile|auto`,非法值按 auto;`resolveTerminal(automatic, override)` — auto 用自动识别,其余用显式覆盖。**优先级:显式路由 > 手动覆盖 > 自动识别**。
- **设备 Store**(`src/stores/deviceStore.ts`):`suggested`(自动识别建议)/`override`/`terminal`(生效)/`ready`;`init()` 惰性初始化(守卫首次导航调用),`updateViewport()` 只更新建议与生效终端不触发导航(§11.2 不强制中断),`setOverride()` 写存储并立即重算。
- **路由表**(`src/router/routes.ts`):`ROUTE_NAMES` 常量,导航一律用 name。七条稳定路由:`/`(root,按会话)→ 按生效终端分流、`/login`、`/403`、`/pc/home`(requiresAuth + `platform.home.view` + terminal pc)、`/pda/home`(platform.pda.view + pda)、`/mobile/home`(platform.mobile.view + mobile)、`/:pathMatch(.*)*`(not-found)。页面组件为最小测试桩(FE-007 替换真实页面)。
- **Route Meta**(`src/router/meta.ts`):`AppRouteMeta { title(必填); requiresAuth?; permission?; terminal? }`,经 `declare module 'vue-router'` 模块增强扩展 `RouteMeta`;`no-empty-object-type` 空接口为 vue-router 增强的标准形式,已加 eslint-disable 注释。
- **守卫决策**(`src/router/guards.ts`,唯一全局守卫,顺序 §12.3):恢复会话(authStore.restore 幂等单飞)→ 受保护路由无会话 → `/login?redirect=<站内相对 fullPath>`;已登录访问 `/login` → 生效终端首页;权限不足 → `/403`;根路由按生效终端分流 `{name: '${terminal}-home'}`,**显式三端路由不改写**(§11.2);最后 `setDocumentTitle(`${title} · Industrial Platform`)`。重定向只使用路由名,无开放重定向,重复根导航无死循环。
- **已知偏差**:前进/后退测试用 `router.go()`(返回 void 不返回导航 promise),测试侧用 afterEach 监听一次导航完成再断言;根路由分流目标 home 为受保护路由,测试需先登录(此前失败正是此原因)。

### FE-006 结果回写(2026-08-10)

- **布局壳**(`src/layouts/PcLayout.vue`):三段式 grid/flex 骨架 —— 56px 顶栏(`--ip-pc-header-height`)、240/64px 侧栏(`--ip-pc-sidebar-width(-collapsed)`,折叠宽度过渡 200ms)、主内容区(`main#main-content`,可聚焦,`overflow: auto`)。顶栏左侧 = 折叠切换按钮(`aria-expanded`/`aria-controls` + 视觉隐藏文案)+ 品牌名;右侧 = 终端信息(生效终端映射 PC/PDA/Mobile)+ `MockModeBanner` + 用户菜单。侧栏为 `<aside aria-label="侧边导航">`。
- **§14.1 必需元素**:跳到主内容入口(`.ip-pc-skip-link`,视觉隐藏聚焦出现,`href="#main-content"`,`main` 带 `tabindex="-1"`)、路由高亮(PcNavMenu 依 `route.name`)、用户菜单(`ElDropdown trigger=click`,显示 displayName,命令仅「退出登录」,退出后总是回登录页)、终端信息、Mock 横幅、退出入口 —— 全部落地并有组件测试覆盖。
- **导航模型**(`src/components/navigation/`):`types.ts` 定义 `NavigationItem { id; label; routeName; icon?; permission?; children? }`;`navigation.ts` 导出第一批菜单 `pcNavigationItems`(仅「首页」→ pc-home,`platform.home.view`);`PcNavMenu.vue` 渲染 `<nav aria-label="主导航">` + 原生 `<a href>`(RouterLink,键盘 Tab+Enter 可达),权限过滤(未声明视为公开,声明但未持有则隐藏),折叠态隐藏文字并经 `title` 提示全名。
- **折叠持久化**:键 `industrial-platform.pc.sidebar.collapsed.v1`,`'1'` 折叠 / `'0'` 展开,跨实例/刷新保持(localStorage;存储不可用不阻塞交互)。测试覆盖 展开/折叠/持久化恢复/aria-expanded。
- **路由接线**(`src/router/routes.ts`):`/pc/home` 重构为父路由 `/pc`(component = PcLayout)+ 子路由 `home`(name `pc-home` 不变,meta 不变),为后续 PC 子页面预留挂载点;导航/守卫仍只用 name,不受路径变更影响。页面桩改为 `<div>`(页面渲染在布局的 `<main>` 内,避免嵌套 `<main>` 语义错误)。
- **测试**:新增 16 项(`PcLayout.spec.ts` 10:骨架/跳到主内容/品牌/终端信息/Mock 横幅/用户菜单/展开折叠持久化/aria-expanded/路由高亮/logout 跳转;`PcNavMenu.spec.ts` 6:权限过滤两态/高亮与非高亮/链接语义/折叠态 title)。logout 组件级经 `ElDropdown` 的 `command` 事件断言会话清理与跳转 `login`。
- **已知偏差**:任务卡验证点中「两个 PC 目标视口(1280×720 / 1440×900)+ 关键截图」属 E2E 截图验收,需先有登录→首页完整链路(FE-007 落地后再补,FE-010 三端集成阶段统一执行);本任务以组件测试覆盖布局功能行为。

### FE-007 结果回写(2026-08-10)

- **登录页**(`src/pages/public/LoginPage.vue`):字段 id `ip-login-username`/`ip-login-password`,标签与 `for` 关联,`autocomplete="username"/"current-password"`;必填错误经 `aria-invalid` + `aria-describedby` 关联到输入项,错误信息 `role="alert"` 呈现;密码显隐切换按钮 `aria-pressed` 反映状态;提交防重(`submitting` 守卫);统一错误文案「用户名或密码错误」(`ApiError` → message,否则默认错误文案,不泄露具体错误项);`redirect` 仅接受站内相对路径(拒绝 `//` 开头的协议相对 URL,回落根路由);密码只进入 `login()` 参数,不进入 Store/Storage(sessionStorage 断言不含密码);Mock 横幅 + 演示账号提示 `mock.admin / Mock@123456`;表单 `novalidate @submit.prevent`。
- **403 页**(`ForbiddenPage.vue`):「返回有权限首页」优先当前生效终端首页,当前终端无权限时回落任一有权限终端首页,全部无权限时隐藏返回入口(无 `go-home` 按钮);`relogin` 调 `authStore.logout()` 清理会话后跳登录;`traceId` 仅当查询参数为 string 时条件展示(exactOptionalPropertyTypes 用 `v-bind` 展开)。
- **404 页**(`NotFoundPage.vue`):原始路径经 Vue 插值纯文本展示(无 `v-html`,组件测试断言 `childElementCount === 0` 且含 `<` 的路径不解析);「返回上一页」用 `canGoBack() = history.state.back != null` 判断,有历史 `router.back()`、无历史回落首页(`!=` 同时覆盖 web history 的 null 与 memory history 的 undefined;memory history 的 back() 会导航到初始 `""` entry 而失效,故生产逻辑以 web history 为准,测试用 jsdom 真实 web history + `vi.waitFor` 轮询 popstate 宏任务)。
- **PC 首页**(`PcHomePage.vue`):欢迎「欢迎,{{ displayName }}」;dl 展示当前终端(PDA/Mobile 覆盖键下显示对应生效终端)、认证模式(Mock/HTTP 由 `loadRuntimeConfig().authMode` 映射)、数据来源(Mock 演示数据);空状态 `AppEmptyState` 明确「业务指标将在后续阶段接入」,免责文案提及指标名称但**不伪造任何数值**(测试断言不出现百分比数字)。
- **路由接线**(`src/router/routes.ts`):login/forbidden/notFound 由桩替换为真实页面,`/pc/home` 子路由组件替换为 PcHomePage;PDA/Mobile 首页仍为桩(FE-008/FE-009 替换)。
- **装配**(`src/app/createIndustrialApp.ts`):`installAuthGateway()` 按 `authMode` 装配 —— http 抛 `RuntimeConfigError`(生产禁 Mock),mock 注入 `createMockAuthGateway({delayMs:200})`;装配顺序 Pinia+ElementPlus → authGateway → `createAppRouter()` → 插件。`App.vue` 收敛为仅 `<RouterView/>`。
- **E2E**(`tests/e2e/pc.spec.ts`,11 用例 + smoke 1):未登录访问受保护路径 → `/login?redirect=` 且登录后回原路径、空提交必填、错误账号统一错误、密码显隐、键盘登录(密码回车)、刷新保持、退出(用户菜单 → 登录页)、403 返回有权限首页、404 纯文本路径 + 返回首页、协议相对 redirect 拒绝后落站内、两个 PC 视口(1280×720 / 1440×900)首页截图到 `tests/e2e/screenshots/pc-home-*.png`。Playwright 经 Vite dev server 提供应用(Phase 2 mock 认证下 preview 不可用,Phase 3 后按 §20.3 改回 preview)。
- **已知偏差**:`getByLabel('密码')` 与切换按钮 `aria-label="显示密码"` 子串冲突(strict mode violation),E2E 改用 `{ exact: true }` 精确匹配;PC 目标视口截图的整体验收(六视口统一)仍在 FE-010 执行。

### FE-008 结果回写(2026-08-10)

- **PDA 布局壳**(`src/layouts/PdaLayout.vue`):flex 列骨架 —— 48px 顶栏(`--ip-pda-header-height`,与 `--ip-touch-min-size` 一致)+ 可滚动主内容区(`main#main-content`,可聚焦)。顶栏左侧 = 返回 + 首页两个 48×48 触控按钮;中间 = 当前用户 displayName(ellipsis 截断);右侧 = 终端标识 + 紧凑 `MockModeBanner(label="Mock")` + 退出按钮(48×48)。横竖屏均由 flex 列布局自适应,无横向滚动(E2E 480×800 / 800×480 断言 `scrollWidth <= innerWidth`)。
- **导航与退出**:返回用 `canGoBack() = router.options.history.state.back != null`(与 NotFoundPage 同一模式)—— 有历史 `router.back()`,无历史回落 PDA 首页,避免误退出应用;首页按钮 `router.push({name: pdaHome})`;退出 `authStore.logout()`(finally 保证清理)+ 回登录页。全部走路由名,不硬编码路径。
- **触控与可访问性**:返回/首页/退出按钮宽高 = `--ip-touch-min-size`(48px),E2E 用 `boundingBox()` 断言几何尺寸 ≥48×48;跳到主内容入口(`.ip-pda-skip-link` href 锚点)是布局第一个可聚焦元素,键盘 Tab 可达,E2E 覆盖 Tab 焦点顺序与 Enter 触发;48px 顶栏高度即触控目标高度。
- **PDA 首页**(`src/pages/pda/PdaHomePage.vue`):欢迎「欢迎,{{ displayName }}」;dl 展示当前终端/认证模式/数据来源(与 PcHomePage 一致);空状态 `AppEmptyState` 标题「现场任务将在业务阶段接入」,免责文案提及扫码/称量/工单等暂缓能力但**不渲染为任何按钮/链接**(组件测试断言 button/a 计数 0,E2E 断言 `getByRole('button'|'link')` 计数 0),不伪造任何数值。
- **路由接线**(`src/router/routes.ts`):`/pda/home` 由最小测试桩重构为父路由 `/pda`(component = PdaLayout)+ 子路由 `home`(name `pda-home`、meta `platform.pda.view` + terminal pda 不变),为后续 PDA 子页面预留挂载点;守卫/导航只用 name 不受影响;Mobile 首页仍为桩(FE-009 替换)。
- **终端识别已知点**:真实 480×800 现场设备宽度 `<768` 属 Mobile 断点(§11.1),PDA 现场环境需经手动覆盖键 `industrial-platform.terminal.override.v1=pda` 或显式 `/pda` 路由(§11.2 显式路由 > 手动覆盖 > 自动识别)保证;单元测试用覆盖键模拟 PDA 环境,`PdaLayout` 测试需 `installRouterGuards`(设备 Store 由守卫惰性初始化)。
- **测试**:新增 13 项(`PdaLayout.spec.ts` 8:骨架/跳到主内容/返回无历史回落/返回有历史(web history + `vi.waitFor`)/首页导航/退出/用户终端 Mock 标识/48px 结构;`PdaHomePage.spec.ts` 5:欢迎/终端/空状态/无伪业务按钮/无伪造数值);E2E 7 项 + 两张截图 `tests/e2e/screenshots/pda-home-480x800.png`/`pda-home-800x480.png`。
- **已知偏差**:空状态标题与描述文案子串重叠(strict mode violation),E2E 改用 `getByRole('heading', { name: … })` 精确定位;PDA 顶栏用户名为静态文本(不设菜单),退出为独立 48px 按钮,符合现场触控「大按钮、少导航」。

### FE-009 结果回写(2026-08-10)

- **Mobile 布局壳**(`src/layouts/MobileLayout.vue`):flex 列骨架 —— 44px 顶栏(`--ip-mobile-header-height`,与 `--ip-touch-min-size-mobile` 一致)+ 可滚动主内容区(`main#main-content`,可聚焦)+ 底部导航 + safe-area。顶栏左侧品牌名(ellipsis),右侧终端标识 + 紧凑 `MockModeBanner(label="Mock")`。底部导航为 `<nav aria-label="底部导航">`,只含「首页」「我的」两个 Tab(§17,不出现任务/消息/审批假入口)。
- **底部导航与 Tab 模型**:`tabs` 常量 `[{首页, mobileHome}, {我的, mobileMy}]`,用 `RouterLink`(语义链接,键盘 Tab+Enter 可达),高亮按 `route.name` 精确匹配(`ip-mobile-nav-item--active`),`RouterLink` active 自动带 `aria-current="page"`。切换经 RouterLink 原生 `:to`,无重复导航。safe-area 适配:`.ip-mobile-nav { padding-bottom: var(--ip-safe-area-bottom) }`(env(safe-area-inset-bottom),§17 强制要求),触控目标 `min-height: var(--ip-touch-min-size-mobile)`(44px)。
- **Mobile 首页**(`src/pages/mobile/MobileHomePage.vue`):欢迎 + 终端/认证模式/数据来源(与 Pc/Pda 首页一致);空状态 `AppEmptyState` 标题「业务功能将在后续阶段接入」,免责文案提及任务/消息/审批等暂缓能力但**不渲染为任何按钮/链接**,不伪造数值。
- **「我的」页**(`src/pages/mobile/MobileMyPage.vue`):展示当前用户 displayName / username / roles(join),44px 退出按钮(全宽 `min-height: var(--ip-touch-min-size-mobile)`),点击 `authStore.logout()`(finally 保证清理)+ 回登录页;无任何伪造数据。
- **路由接线**(`src/router/routes.ts`):`/mobile/home` 由最小测试桩重构为父路由 `/mobile`(component = MobileLayout)+ 子路由 `home`(name/meta 不变)+ 新增 `my`(name `mobile-my`,`platform.mobile.view` + terminal mobile)。根路由占位改名 `rootStub()`(守卫对 root 总是按生效终端分流,组件不实际渲染,保留最小组件满足路由类型要求);`defineComponent/h` 仅剩 root 占位使用,其余页面桩全部移除。
- **终端识别已知点**:真实 360×800/390×844 设备宽度 `<768` 自动识别为 Mobile(§11.1),单元测试用覆盖键 `industrial-platform.terminal.override.v1=mobile` 显式保证;MobileLayout 测试需 `installRouterGuards`(设备 Store 由守卫惰性初始化)。
- **测试**:新增 17 项(`MobileLayout.spec.ts` 8:骨架/skip-link/双 Tab/首页高亮 aria-current/切换后高亮更新/链接点击路由切换/safe-area Token 声明/品牌终端 Mock 标识;`MobileHomePage.spec.ts` 5:欢迎/终端认证数据来源/空状态无伪入口/无伪造数值/h1;`MobileMyPage.spec.ts` 4:用户信息/h1/无伪造数值/退出跳转);E2E 8 项 + 两张截图 `tests/e2e/screenshots/mobile-home-360x800.png`/`mobile-home-390x844.png`。
- **已知偏差**:jsdom 不解析 `env()` 且不计算 scoped min-height,安全区域与 44px 的几何验证由 E2E bounding box 承担(底部导航 Tab/退出按钮 height ≥44 已断言),`env(safe-area-inset-bottom)` 真实 inset 需真机/浏览器设备模拟验收(留 FE-010 统一截图与真机项);键盘 Tab 焦点顺序依赖「首页无其他可聚焦元素」,若后续首页加入按钮需同步调整 E2E。

### FE-010 结果回写(2026-08-10)

- **三端集成确认**:PC(`/pc` 父路由,PcLayout)/ PDA(`/pda`,PdaLayout)/ Mobile(`/mobile`,MobileLayout)三套布局壳全部接入,`/mobile/my` 新增;守卫/导航/权限/终端分流在 FE-010 全量回归中保持 212 单元 + 35 E2E 全绿,无跨终端串台。
- **六类目标视口统一截图**:新增 `tests/e2e/screens.spec.ts` —— 登录后对 PC 1280×720/1440×900、PDA 480×800/800×480、Mobile 360×800/390×844 六视口逐一断言关键内容(heading)可见(无关键遮挡/裁切)与无横向滚动,并输出到规范文件名 `tests/e2e/screenshots/{pc,pda,mobile}-home-*.png`(与各终端任务 E2E 同一批文件名,共 6 张)。
- **safe-area Token 消费验证**(§20.2「Mobile safe-area」):`screens.spec.ts` 注入 `:root { --ip-safe-area-bottom: 34px }` 覆盖后断言底部导航 `padding-bottom` 由 0px 变为 34px,证明布局消费 Token 而非魔法值;`env(safe-area-inset-bottom)` 真实 inset 仍需真机/浏览器设备模拟验收(列入外部环境待验收项)。
- **§18.2 控制台验收**:新增 `tests/e2e/console.spec.ts` —— 登录 → PC → PDA → Mobile → 我的 → 退出全程监听 `console error`/`pageerror` 与敏感日志(`token/password/authorization/Bearer` 正则),全部为零。**为此补齐集成点**:项目原无 favicon,浏览器请求 `/favicon.ico` 产生 404 console error,新增 `public/favicon.svg` + `index.html` link 消除该噪声。
- **clean install 门禁**(§20.3):`rm -rf node_modules` 后经 `mise exec`(node 24.18.0 / pnpm 11.16.0,`.mise.toml` 钉定)执行 `pnpm install --frozen-lockfile` 通过(4.5s);注:当前 shell 默认 node 为系统 22.23.2,门禁必须经 `mise exec` 走钉定工具链。
- **最新质量基线**:format:check/lint/typecheck 退出码 0;unit 212/212(29 文件);coverage 语句 96.32/分支 91.81/函数 95.36/行 97.04(阈值 70);build 通过(dist html 0.47k + js 1,027.38k gzip 332.29k + css 376.61k gzip 50.53k,chunk>500k 为已知偏差);E2E 35/35(9.6s)。
- **Phase 3 接入点与清单**:`src/frontend/README.md` 新增「Phase 3 Identity 接入清单(HttpAuthGateway)」——① `HttpAuthGateway` 实现 `AuthGateway` 契约并过 `runAuthGatewayContractSuite`;② `createHttpClient` 的 `getToken` 注入点改读 `getCurrentSession()` 令牌镜像;③ 真实令牌存储/刷新/撤销策略按 Identity API 单独确认(§19,现有 mock 会话键仅供 Mock);④ API 信封与错误码对齐 Identity 实施方案;⑤ 权限映射到 `platform.home.view/pda.view/mobile.view`;⑥ E2E 改回 preview 并加真实登录用例。`installAuthGateway()` 当前 `authMode=http` 抛 `RuntimeConfigError` 即替换点。
- **外部环境待验收项**(不标记已完成):真实 `env(safe-area-inset-bottom)` 与真机 PDA/Mobile 操作;Docker 环境(联动 TASK-BASE-002);真实 Identity 服务联调(Phase 3);§20.3 的 preview E2E 待 Phase 3 认证就绪后切换。
- **设计偏差收口**:六类目标视口截图此前分属各终端任务,E2E 现由 `screens.spec.ts` 统一产出,规范文件名不变;`src/frontend/README.md` 的 `test:e2e` 描述由「基于 preview」更正为「Phase 2 经 dev server,Phase 3 改回 preview」(与 `playwright.config.ts` 注释一致);Element Plus 全量导入 chunk>500k 警告与 xunit Legacy(后端)维持既有待办,不在本批强行处理。

---

# 25. 下一阶段输入契约

```text
应用工厂: createIndustrialApp()
认证边界: AuthGateway、runAuthGatewayContractSuite()
会话镜像: setCurrentSession() / getCurrentSession()
HTTP 入口: createHttpClient()
稳定路由: login、forbidden、not-found、pc-home、pda-home、mobile-home、mobile-my
权限码: platform.home.view、platform.pda.view、platform.mobile.view
运行配置: VITE_API_BASE_URL、VITE_AUTH_MODE、VITE_REQUEST_TIMEOUT_MS
```

Phase 3 只在既有替换点新增 `HttpAuthGateway`、真实令牌策略和 Identity 契约，不重写三端页面与布局。真实业务菜单、指标、扫码、离线和原生能力必须由各自实施方案单独设计。

# 26. 文档自审清单

- [x] 引用文件真实存在，顶层顺序与统一母版的职责一致。
- [x] 当前输入状态已由“工程占位”更新为 TASK-FE-001～010 开发完成。
- [x] API、认证、会话、权限、路由、三端页面和错误契约前后一致。
- [x] 十个任务均使用统一九字段，依赖图与执行记录编号一致。
- [x] 历史验证均保留日期和数量，本轮未声称重新运行构建或测试。
- [x] 真实 Identity、Docker 联调、真机 safe-area 与 preview E2E 的后续边界明确。
- [x] 本方案不新增后端实体或数据库表，统一数据建模约束不适用。
- [x] 未保留未决项或模糊处理等占位表达。
