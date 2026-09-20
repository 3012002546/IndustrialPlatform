# 平台前端与独立协作宿主部署边界

## 平台用户管理单页入口（第一阶段）

普通 `/pc/identity/users` 仍走平台外壳。使用真实 Identity HTTP 认证时，以下地址只装配原用户管理页及其原有权限和业务操作：

```text
/pc/identity/users?mode=single&token=<平台签发的有效访问令牌>
/pc/identity/users?mode=single&ticket=<受信任流程签发的一次性 SSO 票据>
```

`token` 由 Identity `/auth/me` 验证，须有 `identity.user.view` 权限；单独的访问令牌没有刷新凭据，过期后需重新从受信任入口打开。`ticket` 通过既有 `/sso/exchange` 兑换完整会话，沿现有刷新接口续期。两种凭证不能同时传，票据的目标地址必须指向用户管理页。凭证进入页面后会从地址栏移除；单页会话保存在当前标签页独立的 `sessionStorage` 键，普通平台会话不会被覆盖。首次改密用户先在平台完成改密。当前只开放用户管理页，其他页面尚未接入 single 模式。

独立协作构建已提供专用产物；实际 MES 用户接口和现场媒体验收仍需接入方完成。

## 构建和脚本边界

```json
"dev": "vite",
"dev:lan": "vite --mode lan",
"dev:lan:https": "vite --mode lan-https",
"dev:lan:https:collaboration": "vite --mode lan-https-collaboration"
```

`dev` 是 HTTP localhost；`dev:lan` 是 HTTP 所有 LAN 地址；`dev:lan:https` 和 `dev:lan:https:collaboration` 才启用 HTTPS。普通 `dev:lan` 不改变原有 HTTP 行为。

- `pnpm build` 仍构建完整平台 PC/PDA/Mobile 前端到 `dist/`。
- `pnpm build:collaboration` 构建独立协作前端到 `dist-collaboration/`，只装配协作路由和 MES 嵌入会话。
- `VITE_AUTH_MODE=embedded` 只决定认证方式；独立构建目标由构建命令设置，URL `mode=standalone` 是入口展示合同。

## 本地联调

后端宿主启动方式见 `src/backend/src/Hosts/IndustrialPlatform.Collaboration.EmbeddedHost/README.md`。前端代理从同源 `/_backend/...` 转发到独立宿主 HTTP 56365，浏览器页面使用 HTTPS 5173：

```powershell
Set-Location -LiteralPath 'D:\Code\Industrial Platform\IndustrialPlatform\src\frontend'
pnpm dev:lan:https:collaboration
```

打开：

```text
https://localhost:5173/pc/collaboration?mode=standalone&account=operator-1
https://localhost:5173/pc/collaboration?mode=standalone&account=operator-2
```

独立宿主用 `account` 精确查找 MES 用户列表中的 `userId`；未传、重复、格式错误或查无此人会被拒绝。当前 [MesUserDirectoryAdapter.cs](../backend/src/Hosts/IndustrialPlatform.Collaboration.EmbeddedHost/Adapters/MesUserDirectoryAdapter.cs) 暂用三个 `userId/userName` 对象，接入方只需替换 `LoadMesUsersAsync` 的方法体。前端没有账号白名单。

`lan-https-collaboration` 模式由 `src/frontend/vite.config.ts` 设置 embedded 认证、同源 `/_backend` 代理和 HTTPS；运行时由 `src/frontend/src/config/runtimeConfig.ts` 解析 URL。普通 `lan` 模式不受影响。MES 只需用当前登录账户拼接独立聊天窗 URL，用户接口返回 `userId`、`userName`；独立服务负责会话和固定协作能力。

## 页面会话、实时连接和媒体边界

`mode=standalone` 首次打开时，`embeddedAuthGateway.ts` 调用 `POST /embedded/standalone/session?account=...`。后端核对用户列表后返回 `pageSession.token/binding`，只保存在当前页面内存；HTTP 与 SignalR 共用同一页凭据。凭据不写 localStorage、URL 或普通 bearer Authorization。平台原有 `mode=single` 与普通嵌入入口不调用此端点。

因此同源打开不同账号的多页时，页面身份由页级内存凭据区分；关闭一个页面不会调用其他页面的 logout。页面连接、60 秒嵌入会话租约、20 秒 Presence 续租、媒体邀请不是同一个生命周期。页面停止后不再发送 heartbeat，服务端在租约到期后拒绝请求；媒体邀请仍由服务端判断目标在线和权限，首个有效接受者获胜。

后端会话路径：`POST /api/v1/embedded/session/heartbeat`。服务端 `EmbeddedSessionAuthenticationMiddleware` 与 `EmbeddedHostHandshakeService.TryReadSessionCredential` 优先读取页级请求头，再读取 SignalR `access_token=embedded-session:<token>:<binding>` 和 cookie。JWT bearer 不会把该页级凭据误判成 JWT。

## Standalone 适配验证

后端使用 `EmbeddedCollaboration:Mode=Standalone`。临时人员列表可验证入口、目录 Search/Get、文本消息、Presence、语音及屏幕共享组件；真实 MES 接口与现场媒体效果仍需浏览器验收。

最小验证顺序：

1. 启动 EmbeddedHost 和 `pnpm dev:lan:https:collaboration`。
2. 分别用临时用户列表中的两个 `userId` 打开 `mode=standalone` 页面，确认页面身份不串页；不存在的 `account` 应被拒绝。
3. 一个账号搜索另一个账号，创建会话并发送文本；离线目标的媒体邀请必须由服务端拒绝。
4. 关闭其中一页，确认其他页面仍保持自己的会话；停止页面后等待会话租约失效。

## 正式发布边界

独立后端可单独发布：

```powershell
Set-Location -LiteralPath 'D:\Code\Industrial Platform\IndustrialPlatform'
dotnet publish 'src/backend/src/Hosts/IndustrialPlatform.Collaboration.EmbeddedHost/IndustrialPlatform.Collaboration.EmbeddedHost.csproj' -c Release -o 'D:\Code\Industrial Platform\开发辅助文件\发布包\Collaboration\backend'
```

发布包不包含任何 `appsettings*.json`；部署服务器需单独放置宿主 `appsettings.json`，并用 `INDUSTRIAL_PLATFORM_STANDALONE_CONFIG` 指向独立数据库配置文件的绝对路径，提供持久化数据库目录。生产不使用 `launchSettings.json`。Standalone 入口不需要 MES 签名密钥、`Sources` 或 `SourceSessions`，在线状态与实时投递由宿主进程处理，当前限单实例部署。

独立协作前端发布：

```powershell
Set-Location -LiteralPath 'D:\Code\Industrial Platform\IndustrialPlatform\src\frontend'
$env:VITE_DEPLOYMENT_ENVIRONMENT = 'TEST'
pnpm build:collaboration
```

反向代理必须提供 SPA 回退、`/_backend` API 和 WebSocket 转发；只发布 `dist-collaboration/`。后端保持 `Mode=Standalone`，接入方把 `MesUserDirectoryAdapter.LoadMesUsersAsync` 换成返回 `userId/userName` 的 MES 人员接口调用。其余身份映射和会话由独立服务完成；MES 通过 HTTP 接入，不引用 EmbeddedHost 程序集。
