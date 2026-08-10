# Industrial Platform 统一前端

Vue 3 + TypeScript + Vite 单包工程,承载 **PC / PDA / Mobile** 三端统一前端。当前为第一批(统一前端基础壳),业务页面与真实 Identity 登录留待后续阶段。

## 技术栈

| 类别 | 选型 |
| --- | --- |
| 框架 | Vue 3(Composition API,`<script setup lang="ts">`) |
| 语言 | TypeScript(strict + `noUncheckedIndexedAccess` + `exactOptionalPropertyTypes`) |
| 构建 | Vite |
| 状态 | Pinia |
| 路由 | Vue Router |
| UI | Element Plus |
| HTTP | Axios(经统一客户端封装,禁止页面直接调用) |
| 单元/组件测试 | Vitest + Vue Test Utils |
| API Mock | MSW |
| E2E | Playwright(Chromium) |

## 工具链

工具链由仓库根 `.mise.toml` 钉定(mise 管理):

| 工具 | 版本 |
| --- | --- |
| Node.js | 24.18.0 |
| pnpm | 11.16.0 |

```bash
cd src/frontend
pnpm install --frozen-lockfile   # 严格按锁文件安装
```

## 稳定工程命令

| 命令 | 含义 |
| --- | --- |
| `pnpm dev` | Vite 开发服务器(`http://localhost:5173`) |
| `pnpm format:check` | Prettier 格式检查 |
| `pnpm lint` | ESLint 检查 |
| `pnpm typecheck` | vue-tsc 类型检查 |
| `pnpm test:unit` | Vitest 单元/组件/契约测试 |
| `pnpm test:unit:coverage` | 覆盖率(语句/分支/函数/行均不低于 70%) |
| `pnpm test:e2e` | Playwright E2E(基于 `pnpm preview`,需先 `pnpm build`) |
| `pnpm build` | 生产构建(含类型检查)到 `dist/` |
| `pnpm preview` | 预览构建产物(`http://localhost:4173`) |

命名与语义为稳定契约,后续任务不得随意改名。

## 运行配置

| 环境变量 | 默认值 | 说明 |
| --- | --- | --- |
| `VITE_API_BASE_URL` | `http://localhost:5080` | Gateway 统一入口(前端只走这里) |
| `VITE_AUTH_MODE` | `mock` | 认证适配器(仅开发/测试;生产构建禁止 mock) |
| `VITE_REQUEST_TIMEOUT_MS` | `10000` | HTTP 超时毫秒数 |

安全示例值见 `.env.example`;真实凭据不得提交。

## 目录结构

```text
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

## 质量门禁

```text
clean install → format:check → lint → typecheck → test:unit + coverage → build → preview E2E
```

每个命令要求退出码 0;覆盖率语句/分支/函数/行均不低于 70%。

## 状态

第一批(02B 实施方案)开发中,进度见 `docs/implementation/02B-Industrial Platform统一前端第一批开发实施方案.md` 执行记录。
