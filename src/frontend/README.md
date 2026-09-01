# Industrial Platform 统一前端

Vue 3 + TypeScript + Vite 单包工程,承载 **PC / PDA / Mobile** 三端统一前端。当前为第一批(统一前端基础壳),业务页面与真实 Identity 登录留待后续阶段。

## 技术栈

| 类别          | 选型                                                                           |
| ------------- | ------------------------------------------------------------------------------ |
| 框架          | Vue 3(Composition API,`<script setup lang="ts">`)                              |
| 语言          | TypeScript(strict + `noUncheckedIndexedAccess` + `exactOptionalPropertyTypes`) |
| 构建          | Vite                                                                           |
| 状态          | Pinia                                                                          |
| 路由          | Vue Router                                                                     |
| UI            | Element Plus                                                                   |
| HTTP          | Axios(经统一客户端封装,禁止页面直接调用)                                       |
| 单元/组件测试 | Vitest + Vue Test Utils                                                        |
| API Mock      | MSW                                                                            |
| E2E           | Playwright(Chromium)                                                           |

## 工具链

工具链由仓库根 `.mise.toml` 钉定(mise 管理):

| 工具    | 版本    |
| ------- | ------- |
| Node.js | 24.18.0 |
| pnpm    | 11.16.0 |

```bash
cd src/frontend
pnpm install --frozen-lockfile   # 严格按锁文件安装
```

## 稳定工程命令

| 命令                      | 含义                                                                                   |
| ------------------------- | -------------------------------------------------------------------------------------- |
| `pnpm dev`                | Vite 开发服务器(`http://localhost:5173`)                                               |
| `pnpm format:check`       | Prettier 格式检查                                                                      |
| `pnpm lint`               | ESLint 检查                                                                            |
| `pnpm typecheck`          | vue-tsc 类型检查                                                                       |
| `pnpm test:unit`          | Vitest 单元/组件/契约测试                                                              |
| `pnpm test:unit:coverage` | 覆盖率(语句/分支/函数/行均不低于 70%)                                                  |
| `pnpm test:e2e`           | Playwright E2E(Phase 2 经 Vite dev server;Phase 3 接入真实认证后按 §20.3 改回 preview) |
| `pnpm build`              | 生产构建(含类型检查)到 `dist/`                                                         |
| `pnpm preview`            | 预览构建产物(`http://localhost:4173`)                                                  |

命名与语义为稳定契约,后续任务不得随意改名。

## 运行配置

| 环境变量                  | 默认值                  | 说明                                                                       |
| ------------------------- | ----------------------- | -------------------------------------------------------------------------- |
| `VITE_API_BASE_URL`       | `http://localhost:5041` | 默认 UnifiedHost 入口；独立服务模式显式覆盖为 Gateway `:5080`              |
| `VITE_AUTH_MODE`          | `http`                  | 认证适配器(http=真实 Identity,默认;mock 仅测试/显式配置;生产构建禁止 mock) |
| `VITE_REQUEST_TIMEOUT_MS` | `10000`                 | HTTP 超时毫秒数                                                            |

安全示例值见 `.env.example`;真实凭据不得提交。

## 目录结构

```text
public             # 静态资源(favicon.svg)
src
├── app           # 应用创建与依赖装配
├── api           # HTTP 传输、信封解包与错误分类
├── auth          # 认证端口、Mock 实现与会话存储
├── components    # base(无业务语义)/ navigation 公共组件
├── config        # 类型安全运行配置
├── device        # 终端识别与开发覆盖
├── layouts       # PC / PDA / Mobile 三端布局
├── pages         # public / pc / pda / mobile 页面
├── router        # 路由定义、meta 与守卫
├── stores        # 应用状态(不包含 HTTP 细节)
├── styles        # Design Token、基础样式与终端样式
└── types         # 共享类型
tests
├── fixtures      # 测试夹具
├── unit          # 单元测试
├── components    # 组件测试
├── contract      # 契约测试
└── e2e           # Playwright E2E
```

分层访问纪律:`Page/Component → Store/Use Case → Gateway/API Client → Backend API 或 Mock`。页面禁止直接调用 Axios、读写 token 或在组件内散落 Mock 数据。

## 三端入口与目标视口

| 终端   | 路由入口                                                                        | 布局                       | 目标视口           |
| ------ | ------------------------------------------------------------------------------- | -------------------------- | ------------------ |
| PC     | `/pc/home`(`pc-home`,`platform.home.view`)                                      | `layouts/PcLayout.vue`     | 1280×720、1440×900 |
| PDA    | `/pda/home`(`pda-home`,`platform.pda.view`)                                     | `layouts/PdaLayout.vue`    | 480×800、800×480   |
| Mobile | `/mobile/home`(`mobile-home`)、`/mobile/my`(`mobile-my`,`platform.mobile.view`) | `layouts/MobileLayout.vue` | 360×800、390×844   |

- 终端识别 §11.1:宽度 `>=1200`→PC、`<768`→Mobile、`768–1199` 触控→PDA;优先级「显式路由 > 开发覆盖键 `industrial-platform.terminal.override.v1` > 自动识别」。
- 六类目标视口截图统一由 `tests/e2e/screens.spec.ts` 产出到 `tests/e2e/screenshots/`(PC 1280×720/1440×900、PDA 480×800/800×480、Mobile 360×800/390×844)。
- PDA 触控目标 ≥48px、Mobile ≥44px、Mobile 底部导航适配 `env(safe-area-inset-bottom)`(经 `--ip-safe-area-bottom` Token,真实 inset 需真机/设备模拟验收)。

## 质量基线(2026-08-10,第一批验收)

| 门禁                            | 结果                                                                                                |
| ------------------------------- | --------------------------------------------------------------------------------------------------- |
| clean install                   | ✅ node 24.18.0 / pnpm 11.16.0(mise 钉定)`pnpm install --frozen-lockfile`                           |
| format:check / lint / typecheck | ✅ 退出码 0                                                                                         |
| unit                            | ✅ 212/212(29 文件)                                                                                 |
| coverage                        | ✅ 语句 96.32 / 分支 91.81 / 函数 95.36 / 行 97.04(阈值 70)                                         |
| build                           | ✅ dist 0.47k html + js 1,027.38k(gzip 332.29k)+ css 376.61k(gzip 50.53k);chunk>500k 警告为已知偏差 |
| E2E                             | ✅ 35/35(smoke 1 + pc 12 + pda 7 + mobile 8 + screens 7 + console 1)                                |

## Phase 3 Identity 接入清单(HttpAuthGateway)

> **已完成(2026-08-13):** HttpAuthGateway、真实令牌/刷新/撤销、权限映射、真实登录 E2E 均已落地(见 PF-01 实施 04 §16/§17、Identity 实施 03)。以下清单保留为历史实现记录。

第一批为 Mock 认证边界;接入真实 Identity 时按以下清单,不重写现有页面与布局:

1. **HttpAuthGateway**:实现 `AuthGateway` 契约(`src/auth/types.ts`)——`login/refresh/logout/getCurrentUser`,须通过可复用契约测试套件 `runAuthGatewayContractSuite`(`tests/contract/authGateway.spec.ts`)。装配点在 `src/app/createIndustrialApp.ts` `installAuthGateway()`(现 `authMode=http` 为本地默认,装配真实实现;`authMode=mock` 仅测试与显式配置使用)。
2. **HTTP 鉴权注入**:`createHttpClient`(`src/api/httpClient.ts`)的 `getToken` 注入点从 `getCurrentSession()`(`src/auth/gateway.ts` 令牌镜像)读取 Bearer;`X-Correlation-Id` 已就绪。
3. **真实令牌策略**:按后端 Identity API 确定 access/refresh token 的存储(新版本化会话键,现有 `industrial-platform.auth.mock.v1` 仅供 Mock)、过期刷新、刷新失败清理与退出撤销(§19:Phase 3 须单独确认真实令牌的存储/刷新/撤销策略)。
4. **API 契约对齐**:登录/刷新/登出端点与 ApiResult 信封以 Identity Service 实施方案(`docs/implementation/03`)定稿的契约为准;错误码映射到现有 `ApiError` 分类。
5. **权限模型**:真实 Identity 角色/权限到 `platform.home.view`、`platform.pda.view`、`platform.mobile.view` 的映射,经守卫与导航过滤验证。
6. **E2E 切换**:接入后 `playwright.config.ts` webServer 由 dev server 改回 `pnpm preview`(§20.3),新增真实登录 E2E(seed Identity 用户),并复跑六视口与全量门禁。

## 质量门禁

```text
clean install → format:check → lint → typecheck → test:unit + coverage → build → preview E2E
```

每个命令要求退出码 0;覆盖率语句/分支/函数/行均不低于 70%。

## 状态

第一批(02B 实施方案)三端基础壳已完成(FE-001~FE-010),进度与执行记录见 `docs/implementation/02B-Industrial Platform统一前端第一批开发实施方案.md` 第 24 节。真实 Identity 登录(Phase 3)已接入并经真实 E2E 验证(见 PF-01 实施 04 §16);业务页面留待后续阶段。
