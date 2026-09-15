# IndustrialPlatform.Collaboration.EmbeddedHost

这是 Collaboration 的独立进程宿主。MES 不引用本仓库程序集，只通过 HTTP/SignalR 使用聊天、语音和屏幕共享能力；MES 自己保留登录和当前用户会话。`.NET Framework 4.5.2` 与 `.NET 6` 均走相同的 HTTP 边界。

## 本地启动

源码宿主位于本目录，前端仍复用 `src/frontend` 的 Collaboration 页面。

```powershell
Set-Location -LiteralPath 'D:\Code\Industrial Platform\IndustrialPlatform\src\backend\src\Hosts\IndustrialPlatform.Collaboration.EmbeddedHost'
if (-not (Test-Path -LiteralPath 'appsettings.json')) {
    Copy-Item -LiteralPath 'appsettings.example.json' -Destination 'appsettings.json'
}
Set-Location -LiteralPath 'D:\Code\Industrial Platform\IndustrialPlatform\src\backend'
if (-not (Test-Path -LiteralPath 'appsettings.Standalone.Development.local.json')) {
    Copy-Item -LiteralPath 'appsettings.Standalone.Development.local.example.json' -Destination 'appsettings.Standalone.Development.local.json'
}
Set-Location -LiteralPath 'D:\Code\Industrial Platform\IndustrialPlatform\src\backend\src\Hosts\IndustrialPlatform.Collaboration.EmbeddedHost'
dotnet run --project 'IndustrialPlatform.Collaboration.EmbeddedHost.csproj' --launch-profile Collaboration.EmbeddedHost
```

默认监听 `https://localhost:56364` 和 `http://localhost:56365`。前端开发代理使用 HTTP 56365；浏览器直接访问宿主或生产反向代理时使用 HTTPS。独立宿主不连接平台默认云数据库。

数据库只有一个输入：`src/backend/appsettings.Standalone.Development.local.json` 的 `Standalone:Database`。示例默认 SQLite，文件相对该配置文件目录解析；也可将 `Provider` 改为 `PostgreSQL` 并提供 `ConnectionString`。`StandaloneConfiguration.Apply` 将这一个输入投影到 SqlSugar 和 DatabaseTopology；不支持 SQL Server，也不会回退读取平台 `appsettings.Development.local.json`。该 `.local.json` 已被忽略，不能提交真实连接串、密钥或 MES 凭据。

宿主自己的 `appsettings.json` 只保存监听相关的 Embedded/MES 接入、页面来源和功能模式配置。`appsettings.example.json` 是 FixedDemo 参考夹具模板，不是生产配置；示例不包含私钥，复制后必须把密钥路径替换为仓库外的本机开发文件，演示人员也只能用于本机开发。

## 账号参数和页面级会话

开发前端命令：

```powershell
Set-Location -LiteralPath 'D:\Code\Industrial Platform\IndustrialPlatform\src\frontend'
pnpm dev:lan:https:collaboration
```

打开以下地址即可选择参考账号：

```text
https://localhost:5173/pc/collaboration?account=xxA   # 默认账号，也可省略 account
https://localhost:5173/pc/collaboration?account=xxB
https://localhost:5173/pc/collaboration?account=xxC
```

`xxA`、`xxB`、`xxC` 必须是单个、精确匹配的值；未知值或重复参数会被拒绝，不会静默回退到 A。前端 `src/frontend/src/config/runtimeConfig.ts` 的 `parseEmbeddedDemoAccount` 负责页面 URL 校验；后端 `FixedDemoAccountCatalog.Resolve` 再按服务端白名单校验。`FixedDemoSessionEndpoint.StartAsync` 按账号选择 source session，返回 `account`、`currentUser`、身份投影及短期 `pageSession`。

`pageSession` 仅保存在当前页面内存中，后续 HTTP 请求由 `createHttpClient` 注入 `X-Embedded-Session` 和 `X-Embedded-Binding`；SignalR 由 `CollaborationRealtimeManager` 使用同一页凭据。服务端 `EmbeddedSessionAuthenticationMiddleware` 和 `EmbeddedHostHandshakeService.TryReadSessionCredential` 优先读取这两个成对的请求头，再处理 SignalR 查询参数和 HttpOnly cookie。因此同源页面 A/B/C 不依赖共享 Cookie 来区分身份，关闭一个页面不会主动注销其他页面。

页面生命周期与应用实时连接、Presence 心跳、媒体邀请分别管理。默认会话租约为 60 秒；已有的 Presence 周期同时调用 `/api/v1/embedded/session/heartbeat` 续租，页面停止后不再续租并在租约内失效。媒体邀请仍由服务端权限判断，离线目标不会被客户端伪造为可接受；同一邀请由首个有效接受者获胜。

演示入口只在 `EmbeddedCollaboration:Mode=FixedDemo` 时映射：

```text
POST /embedded/demo/session?account=xxB
```

返回 400 表示账号参数不合法；返回的 `pageSession.token` 和 `pageSession.binding` 不应写入 localStorage、URL 或正式日志。该入口只用于 `lan-https-collaboration` 开发模式，生产构建和普通 Embedded 模式不会自动调用它。

## FixedDemo 参考链路

复制宿主示例配置并设置 `EmbeddedCollaboration:AllowedParentOrigins`，至少包含实际前端 Origin，例如 `https://localhost:5173` 或 `https://10.13.49.141:5173`。保留 `SourceResolver.Mode=ReferenceFixture`、`AccessAdapter.Mode=ReferenceFixture`，它们只读取示例中的 `SourceSessions`、身份映射和逐项权限。

启动后，可通过既有接口验证最小链路：

1. `GET /collaboration/api/v1/users?keyword=系统` 搜索 B/C。
2. `POST /collaboration/api/v1/conversations`，body `{ "peerUserNId": "<B 或 C 的 userNId>" }`。
3. `POST /collaboration/api/v1/conversations/<conversationNId>/messages`，body `{ "clientMessageNId": "demo-1", "messageType": "Text", "textContent": "来自系统 A 的演示消息" }`。
4. `GET /collaboration/api/v1/conversations/<conversationNId>/messages` 读回消息。

参考夹具的 `SourceSessions` 配置键只是上游 cookie 索引；真正绑定依赖条目内显式的 `SessionNId`、主体、租户和安全版本。账号 A/B/C 都有独立 source session 和相同的最小协作权限投影。

独立启动初始化由 `StandaloneDatabaseInitializationHostedService.StartAsync` 编排，顺序固定为 Identity → SystemData → ReferenceData → Collaboration。它复用已有 `IServiceInitializer` 的 inspect/plan/apply/verify，并在实际物理数据库目标上持有 SQLite 文件锁或 PostgreSQL advisory lock；`EmbeddedCollaboration:Initialization:Enabled=true` 时 FixedDemo 和正式 MES 模式都可使用，普通平台宿主不注册此服务。

## 正式 MES 接入

正式模式关闭 FixedDemo，并在 `Program.cs` 替换以下两个 DI seam；默认实现全部失败关闭，不允许 allow-all 或客户端自报身份。稳定的平台租户和外部主体映射由宿主内置的 `ServerDerivedEmbeddedSubjectIdentityMapper` 完成；它不要求 MES 再实现第二套身份映射接口：

- `IEmbeddedCurrentUserAdapter.ResolveAsync(HttpContext, string? requestedAccountNId, CancellationToken)`：服务端调用现有 MES 当前用户接口，返回 `EmbeddedSourcePrincipal` 的 `SourceNId`、`ExternalTenantNId`、稳定 `ExternalSubject`、`SessionNId`、`SecurityVersion`、`DisplayName` 和已核对的 `AccountNId`。`requestedAccountNId` 只能与 MES 当前登录用户比较，不能直接回填。
- `IEmbeddedCollaborationAccessAdapter.GetDirectoryUserAsync` / `SearchDirectoryAsync`：只提供人员目录 Search/Get。身份和会话验证成功后，宿主通过 `EmbeddedCollaborationPermissionCatalog` 授予消息、Presence 和媒体所需的显式最小权限，不要求 MES 实现整套平台权限目录，也不会放行管理员或合规权限。

推荐单独部署一个服务端 HTTP adapter/BFF，EmbeddedHost 只调用它。浏览器仍只发送宿主会话，不能传递 MES 密码、subject、tenant 或权限。旧 MES 不需要引用新程序集；adapter 应读取服务端 `HttpContext` 中的 MES Cookie/服务器会话并核对当前用户，伪代码如下（`IMesCurrentUserClient` 是部署方现有 MES client，不是浏览器 header）：

```csharp
public async Task<EmbeddedSourcePrincipal?> ResolveAsync(
    HttpContext context,
    string? requestedAccountNId,
    CancellationToken cancellationToken)
{
    var current = await mesCurrentUserClient.GetCurrentAsync(context, cancellationToken);
    if (current is null || (requestedAccountNId is not null
        && !String.Equals(current.AccountNId, requestedAccountNId, StringComparison.Ordinal)))
        return null;
    return new EmbeddedSourcePrincipal(
        "mes", current.TenantNId, current.Subject, current.DisplayName,
        current.SecurityVersion, current.SessionNId, current.AccountNId);
}
```

宿主实际握手路由为 `POST /api/v1/embedded/challenges` → `GET /api/v1/embedded/assertions?nonce=...` → `POST /api/v1/embedded/exchanges`；已有会话使用 `GET /api/v1/embedded/session?account=...` 和 `POST /api/v1/embedded/session/heartbeat?account=...`。浏览器示例 `wwwroot/embedded-handshake.js` 的 `establishEmbeddedSession({ ..., account })` 与 `renewEmbeddedSession({ ..., account })` 会把 account 同时附加到 challenge、assertion、exchange/renew，服务端再次用 MES 当前用户核对；缺少、重复或不匹配时拒绝。正式部署应设置 `EmbeddedCollaboration:PlatformTenantNId`，并替换当前用户和目录适配器。

可执行的 HTTP 调用示例见 [`docs/examples/EmbeddedMesHandshakeExample.cs`](../../../../../docs/examples/EmbeddedMesHandshakeExample.cs)。示例只依赖 `HttpClient`、Cookie 和 JSON 文本解析，使用的请求顺序同时适用于 .NET Framework 4.5.2 与 .NET 6；前端 iframe 侧直接复用 [`wwwroot/embedded-handshake.js`](wwwroot/embedded-handshake.js)。

实际的 MES 地址、服务端票据转发、租户字段、CSP `frame-ancestors`、CORS、HTTPS 和 iframe `allow` 属性由部署方确定；本宿主不从浏览器猜测这些信息。

## HTTP/SignalR 部署契约

- API：`/collaboration/api/v1`；Hub：`/collaboration/hubs/collaboration-v1`；嵌入会话：`/api/v1/embedded/*`。
- `AllowedParentOrigins` 必须是精确 Origin；启用 credentials 时不能使用 `*`。
- 嵌入 cookie 为 `HttpOnly; Secure; SameSite=None`，生产环境必须 HTTPS；页面凭据只在当前页内存中使用。
- 反向代理需转发 API、SignalR WebSocket、嵌入会话和握手路径，并配置 SPA 回退及实际需要的 `microphone`、`display-capture` 权限。
- 会话过期或撤销后，前端清理本地状态并提示从 MES 重新打开，不跳转平台用户名密码登录页。
