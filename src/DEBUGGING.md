# 本地调试指南

> 适用环境:Windows 11 / 无 Docker(基础设施不可用)。记录于 2026-08-10,配合第一批(FE-001~FE-010)三端统一前端 + 后端三服务验证结论。

## 目录

1. [已验证的运行状态](#一已验证的运行状态)
2. [后端调试(VS2026)](#二后端调试vs2026)
3. [前端调试(VS Code)](#三前端调试vs-code)
4. [质量门禁命令](#四质量门禁命令)
5. [无 Docker 环境的已知预期](#五无-docker-环境的已知预期)

---

## 一、已验证的运行状态

无 Docker 环境下,手工启动后端三服务并逐项探测(探测后已停止,端口已释放):

| 探测项 | 结果 | 说明 |
| --- | --- | --- |
| Identity 直接访问 `:5041/health` | ✅ HTTP 200 | 服务正常启动 |
| ReferenceData 直接访问 `:62311/health` | ✅ HTTP 200 | 同上 |
| Gateway `/health` | ✅ HTTP 200 | 统一入口存活 |
| 网关转发 `/identity/health` | ✅ `{"status":"Healthy","service":"Identity"}` | YARP 前缀剥离转发正常 |
| 网关转发 `/referencedata/health` | ✅ `{"status":"Healthy","service":"ReferenceData"}` | 同上 |
| `/health/live` | ✅ HTTP 200 | 存活探针 |
| `/health/ready` | ⚠️ HTTP 503 | 预期:Postgres/Redis/RabbitMQ/Seq 未启动,依赖检查 Unhealthy,聚合 503 |
| `/unknown` | ✅ HTTP 404 信封 | `{"success":false,"code":"404","message":"路由不存在","data":null}` |

日志说明:

- 启动无异常;依赖健康检查报 `postgres/redis/rabbitmq Unhealthy` 属设计行为(TASK-BASE-003/004),非程序错误。
- 中文日志在 Windows 控制台显示为乱码(GBK 编码),为已知显示问题,不影响运行与调试。

前端独立可跑全流程:第一批 E2E **35/35**(mock 登录 → PC/PDA/Mobile 三端首页 → 403/404 → 退出)已通过 Vite dev server 验证。

---

## 二、后端调试(VS2026)

### 1. 打开解决方案

```text
src/backend/IndustrialPlatform.slnx
```

### 2. 启动顺序与端口

多启动项目:Identity、ReferenceData、Gateway 三个项目右键 → 设为启动项目(或配置多个启动项目)。

端口已钉在各自 `launchSettings.json`,与 Gateway 路由一致:

| 项目 | 端口 | 说明 |
| --- | --- | --- |
| `IndustrialPlatform.Identity.Api` | `http://localhost:5041` | 认证服务 |
| `IndustrialPlatform.ReferenceData.Api` | `http://localhost:62311` | 基础数据服务(launchSettings 默认 `launchBrowser: true` 会弹浏览器,可忽略) |
| `IndustrialPlatform.Gateway` | `http://localhost:5080` | YARP 统一入口 |

### 3. 调试入口

- 统一入口:**`http://localhost:5080`**(前端 `VITE_API_BASE_URL` 默认即此)。
- 绕过网关直达服务:`http://localhost:5041`、`http://localhost:62311`。
- 断点位置建议:各服务 `Health/` 目录下的依赖健康检查、Gateway 的 YARP 中间件、`IndustrialPlatform.Web.Middleware.RequestLoggingMiddleware`。

### 4. 环境准备

- **NuGet 恢复异常时**:Windows 需设置本地 CLI home,在 VS 环境变量或命令行设 `DOTNET_CLI_HOME=<仓库根>\.dotnet_cli_home`(见 CLAUDE.md 提示)。
- **环境变量**:运行配置选 `http` Profile 时即为 `Development` 环境。
- **无 Docker 时**:依赖不可用属预期(见第五节),业务代码未触碰这些依赖时不阻塞调试;完整联调需先起 Docker Compose(`docker compose up -d`,见 `deploy/scripts/README.md`)。

---

## 三、前端调试(VS Code)

### 1. 打开与工具链

- 打开目录:`src/frontend`。
- 仓库根 `.mise.toml` 钉定 **node 24.18.0 / pnpm 11.16.0**。集成终端建议经 mise 生效 PATH;若 shell node 版本不对(如系统自带 node 22),一律用 `mise exec -- pnpm ...` 运行。

```bash
cd src/frontend
pnpm install --frozen-lockfile   # 严格按锁文件
pnpm dev                          # → http://localhost:5173
```

### 2. 登录与三端访问

演示账号 `mock.admin` / `Mock@123456`(Mock 模式,无需后端)。

三端入口:

| 终端 | 直达路由 | 目标视口 |
| --- | --- | --- |
| PC | `/pc/home` | 1280×720、1440×900 |
| PDA | `/pda/home` | 480×800、800×480 |
| Mobile | `/mobile/home`、`/mobile/my` | 360×800、390×844 |

模拟终端(优先级高于自动识别):

```js
localStorage.setItem('industrial-platform.terminal.override.v1', 'pc')    // PC
localStorage.setItem('industrial-platform.terminal.override.v1', 'pda')   // PDA
localStorage.setItem('industrial-platform.terminal.override.v1', 'mobile')// Mobile
localStorage.setItem('industrial-platform.terminal.override.v1', 'auto')  // 恢复自动识别
```

注意:自动识别按 §11.1(宽度 `>=1200`→PC、`<768`→Mobile、`768–1199` 触控→PDA)。DevTools 设备模拟宽度小于 768 会识别为 **Mobile**,要调 PDA 必须用覆盖键或拉宽到 768–1199 + 触控。

### 3. 插件与调试配置

- 安装 **Vue - Official(Volar)**,获得 `.vue` 智能提示与类型检查;若类型报错为陈旧状态,命令面板执行 `Vue: Restart Vue Server`。
- 可选 `.vscode/launch.json`:Edge/Chrome 调试连 `http://localhost:5173`,断点打在 `.ts` / `.vue` `<script setup>` 内。

### 4. 接入后端说明

- 当前 `VITE_AUTH_MODE=mock`,前端只走 Mock 认证;`VITE_AUTH_MODE=http` 会抛 `RuntimeConfigError`(Phase 3 实现 HttpAuthGateway 后可用)。
- `VITE_API_BASE_URL` 默认 `http://localhost:5080`(Gateway 统一入口)。
- 生产构建禁止 mock(`VITE_AUTH_MODE=mock` 构建会失败,属设计约束)。

---

## 四、质量门禁命令

每个命令要求退出码 0;覆盖率语句/分支/函数/行均不低于 70%。

```bash
# 前端(src/frontend)
pnpm format:check
pnpm lint
pnpm typecheck
pnpm test:unit
pnpm test:unit:coverage
pnpm build
pnpm test:e2e

# 后端(src/backend)
dotnet build IndustrialPlatform.slnx
dotnet test IndustrialPlatform.slnx
```

---

## 五、无 Docker 环境的已知预期

| 现象 | 说明 |
| --- | --- |
| `/health/ready` 返回 503,耗时约 6~15s | 依赖健康检查 Postgres/Redis/RabbitMQ 均 Unhealthy(连接超时),聚合 503;**预期行为**,`/health` 与 `/health/live` 仍 200 |
| 日志出现 `PostgreSQL 无法连接:SqlSugarException`、`Redis 无法连接`、`RabbitMQ 连接失败:localhost:5672` | 均为依赖缺失,**非程序错误**;ReferenceData 启动时还会提示「未注册任何消费事件,跳过 RabbitMQ 消费者」 |
| 控制台中文乱码 | Windows GBK 编码显示问题,不影响运行 |
| `launchSettings` 启动 ReferenceData 弹浏览器 | 正常,`launchBrowser: true` 所致 |

要完整联调(真实数据库/缓存/消息队列),需安装 Docker 后执行 `docker compose up -d`(见 `deploy/scripts/README.md`),届时 `/health/ready` 应转 200。
