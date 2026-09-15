# 平台前端与独立协作宿主部署边界

更新：2026-09-14。本文件只记录当前可执行的命令、入口和边界；尚未实现的独立裁剪产物不能当作已完成能力。

## 构建和脚本边界

```json
"dev": "vite",
"dev:lan": "vite --mode lan",
"dev:lan:https": "vite --mode lan-https",
"dev:lan:https:collaboration": "vite --mode lan-https-collaboration"
```

`dev` 是 HTTP localhost；`dev:lan` 是 HTTP 所有 LAN 地址；`dev:lan:https` 和 `dev:lan:https:collaboration` 才启用 HTTPS。普通 `dev:lan` 不改变原有 HTTP 行为。

- `pnpm build` 仍构建完整平台 PC/PDA/Mobile 前端到 `dist/`。
- `VITE_AUTH_MODE=embedded` 只替换认证/接口装配，复用聊天、SignalR、目录和媒体实现，不自动删除平台路由或菜单。
- 当前没有裁剪后的 `build:collaboration` 命令；不要把完整 `dist/` 改个标题就宣称独立前端包。

## 本地联调

后端宿主启动方式见 `src/backend/src/Hosts/IndustrialPlatform.Collaboration.EmbeddedHost/README.md`。前端代理从同源 `/_backend/...` 转发到独立宿主 HTTP 56365，浏览器页面使用 HTTPS 5173：

```powershell
Set-Location -LiteralPath 'D:\Code\Industrial Platform\IndustrialPlatform\src\frontend'
pnpm dev:lan:https:collaboration
```

打开：

```text
https://localhost:5173/pc/collaboration                 # 默认 xxA
https://localhost:5173/pc/collaboration?account=xxA
https://localhost:5173/pc/collaboration?account=xxB
https://localhost:5173/pc/collaboration?account=xxC
```

只有 `xxA`、`xxB`、`xxC` 有效；未知或重复 `account` 参数会被拒绝，不会回退到 A。页面未提供参数时默认 A。此参数只选择 FixedDemo 开发账户，不是生产凭据。

`lan-https-collaboration` 模式由 `src/frontend/vite.config.ts` 设置 embedded 认证、同源 `/_backend` 代理和 HTTPS；运行时由 `src/frontend/src/config/runtimeConfig.ts` 解析 URL。普通 `lan` 模式不启用演示自动登录，也不会被本改动强制升级为 HTTPS。正式 MES 的 `embedded` 页面也可携带一次可选 `account`；`wwwroot/embedded-handshake.js` 会把它贯穿 challenge、assertion、exchange/renew 和 session/heartbeat 请求，由服务端当前用户适配器核对；重复、空值或不匹配会拒绝。HTTP/JS 示例见 `docs/examples/EmbeddedMesHandshakeExample.cs`。

## 页面会话、实时连接和媒体边界

`src/frontend/src/auth/embeddedAuthGateway.ts:createEmbeddedAuthGateway` 调用 `POST /embedded/demo/session`，把响应中的 `pageSession.token/binding` 只保存在当前页面内存。`src/frontend/src/api/httpClient.ts:createHttpClient` 给 API 请求注入 `X-Embedded-Session`、`X-Embedded-Binding`；`src/frontend/src/api/collaborationHub.ts:CollaborationRealtimeManager` 给 SignalR 使用同一页凭据。凭据不写 localStorage、URL 或普通 bearer Authorization。

因此同源打开 A/B 两页时，页面身份由页级内存凭据区分，即使浏览器共享 HttpOnly cookie 也不会把 B 请求变成 A；关闭一个页面不会调用其他页面的 logout。页面连接、60 秒嵌入会话租约、20 秒 Presence 续租、媒体邀请不是同一个生命周期。页面停止后不再发送 heartbeat，服务端在租约到期后拒绝请求；媒体邀请仍由服务端判断目标在线和权限，首个有效接受者获胜。

后端会话路径：`POST /api/v1/embedded/session/heartbeat`。服务端 `EmbeddedSessionAuthenticationMiddleware` 与 `EmbeddedHostHandshakeService.TryReadSessionCredential` 优先读取页级请求头，再读取 SignalR `access_token=embedded-session:<token>:<binding>` 和 cookie。JWT bearer 不会把该页级凭据误判成 JWT。

## FixedDemo 适配验证

这是现有前端联调，不是独立裁剪生产包。后端必须使用 `EmbeddedCollaboration:Mode=FixedDemo`，前端仅在 `lan-https-collaboration` 开发模式自动建立演示会话；普通平台登录和生产构建不自动启用它。完整链路包括目录 Search/Get、文本消息、Presence、语音及屏幕共享组件，现场媒体效果仍需浏览器验收。

最小验证顺序：

1. 启动 EmbeddedHost 和 `pnpm dev:lan:https:collaboration`。
2. 分别打开 `?account=xxA`、`?account=xxB`、`?account=xxC`，确认页面身份不串页。
3. A 搜索 B/C，创建会话并发送文本；离线目标的媒体邀请必须由服务端拒绝。
4. 关闭其中一页，确认其他页面仍保持自己的会话；停止页面后等待会话租约失效。

## 正式发布边界

独立后端可单独发布：

```powershell
Set-Location -LiteralPath 'D:\Code\Industrial Platform\IndustrialPlatform'
dotnet publish 'src/backend/src/Hosts/IndustrialPlatform.Collaboration.EmbeddedHost/IndustrialPlatform.Collaboration.EmbeddedHost.csproj' -c Release -o 'D:\Code\Industrial Platform\开发辅助文件\发布包\Collaboration\backend'
```

部署服务器必须提供独立私有配置 `src/backend/appsettings.Standalone.Development.local.json` 对应的内容、持久化数据库目录、Redis 和正式签名密钥；生产不使用 `launchSettings.json`，不携带示例身份/密钥。若使用 PresenceRegistry，需配置宿主的 `Redis:ConnectionString`。

完整平台前端的 embedded 变体仍可用于适配验证：

```powershell
Set-Location -LiteralPath 'D:\Code\Industrial Platform\IndustrialPlatform\src\frontend'
$env:VITE_AUTH_MODE = 'embedded'
$env:VITE_API_BASE_URL = '/_backend'
$env:VITE_DEPLOYMENT_ENVIRONMENT = 'TEST'
pnpm build
```

反向代理必须提供 SPA 回退、API 和 WebSocket 转发；不要把 dist 直接复制到宿主 `wwwroot` 后宣称已经实现独立路由/插件裁剪。正式 MES 模式关闭 FixedDemo，替换 EmbeddedHost 的当前用户、目录和权限适配器；稳定主体映射由宿主内置完成。`.NET Framework 4.5.2`/`.NET 6` MES 通过 HTTP 接入，不引用 EmbeddedHost 程序集。
