# IndustrialPlatform.Collaboration.EmbeddedHost

这是独立部署的协作宿主。MES 不引用平台程序集，只在当前登录用户打开聊天窗时拼接 `mode=standalone&account=<当前账户>`，并提供返回 `userId`、`userName` 的用户列表接口。独立服务用 `account` 精确匹配列表中的 `userId`；身份投影、页面会话、续期、存储与协作功能能力均由独立服务负责。该边界只校验账号在结果集中存在，不读取 MES 会话、租户、安全版本或权限。

## 唯一需要替换的 MES 方法

[Adapters/MesUserDirectoryAdapter.cs](Adapters/MesUserDirectoryAdapter.cs) 的 `LoadMesUsersAsync` 当前临时 `new` 三个 `MesUser(userId, userName)` 对象。接入时把这个方法体替换为 MES 用户列表接口调用；`GetDirectoryUserAsync`、`SearchDirectoryAsync` 和独立入口共用返回结果，不需要在页面或业务代码中写账号白名单。请保证 `userId` 稳定且唯一，`account` 与它精确一致；`userName` 只用于显示。

独立入口是 `POST /embedded/standalone/session?account=...`。未传账号、账号格式错误、账号不在用户列表、页面 Origin 不在允许列表时拒绝；匹配成功后独立服务签发只属于该页的短期凭据。后续 HTTP 与 SignalR 使用该凭据，服务端续期时重新查用户列表；用户从列表移除后不再续期。URL `account` 是本轮约定的账号选择与列表存在性校验，不额外声称它证明 MES 当前登录身份。

协作模块仍需要内部权限判断，但独立宿主只授予聊天、在线状态、语音与屏幕共享所需的固定能力；没有独立的权限分配页面，也不读取 MES 权限。平台原有权限管理和用户管理不走此宿主。

## 本地配置与启动

| 文件 | 用途 |
| --- | --- |
| 本目录 `appsettings.json` | 独立宿主入口配置：`EmbeddedCollaboration:Mode=Standalone`、`PlatformTenantNId`（独立库内部租户）、`AllowedParentOrigins`、`Initialization:Enabled`，以及 `Collaboration:Media` 的语音和屏幕共享开关。可从同目录 `appsettings.example.json` 复制。 |
| `src/backend/appsettings.Standalone.Development.local.json` | 独立数据库连接。可从同名 `.example.json` 复制；数据库只从这里读取，不回退平台配置。 |
| 平台 UnifiedHost、Gateway 及其他服务的 `appsettings*.json` | 仅各自服务读取，不是独立协作的配置入口。 |

`Sources`、`SourceSessions`、`Demo:Accounts` 均不是 Standalone 模式的配置。旧握手代码与演示参考夹具为既有兼容测试保留；`FixedDemo` 需要显式配置，Standalone 的页面入口不使用这些配置或断言流程。`PlatformTenantNId`、内部会话标识和固定功能能力由本服务管理，不要求 MES 接口返回。

```powershell
Set-Location -LiteralPath 'D:\Code\Industrial Platform\IndustrialPlatform\src\backend\src\Hosts\IndustrialPlatform.Collaboration.EmbeddedHost'
if (-not (Test-Path -LiteralPath 'appsettings.json')) {
    Copy-Item -LiteralPath 'appsettings.example.json' -Destination 'appsettings.json'
}
Set-Location -LiteralPath 'D:\Code\Industrial Platform\IndustrialPlatform\src\backend'
if (-not (Test-Path -LiteralPath 'appsettings.Standalone.Development.local.json')) {
    Copy-Item -LiteralPath 'appsettings.Standalone.Development.local.example.json' -Destination 'appsettings.Standalone.Development.local.json'
}
dotnet run --project 'src/Hosts/IndustrialPlatform.Collaboration.EmbeddedHost/IndustrialPlatform.Collaboration.EmbeddedHost.csproj' --launch-profile Collaboration.EmbeddedHost
```

本地宿主默认监听 `https://localhost:56364`、`http://localhost:56365`。开发前端运行 `pnpm dev:lan:https:collaboration`，例如打开 `https://localhost:5173/pc/collaboration?mode=standalone&account=operator-1`。临时用户可在 `LoadMesUsersAsync` 中增删，前端无需跟着改。

独立数据库支持 SQLite 与 PostgreSQL；初始化按 Identity → SystemData → ReferenceData → Collaboration 顺序复用既有迁移。Standalone 的在线租约和协作实时投递在宿主进程内完成，不要求 Redis 或 RabbitMQ；`/health/ready` 检查协作数据库与出站队列。当前只支持**单实例**部署，进程重启后在线状态由客户端重连恢复。Standalone 开发环境会自动允许本机当前网卡 IP 的 `https://IP:5173` 来源，切换网络不用改本地配置；正式部署仍须把准确的前端 Origin 写入 `AllowedParentOrigins`，并配置 HTTPS、IIS 子应用、SignalR WebSocket 与 iframe 所需的媒体权限。不要提交本地连接串或密钥。

独立后端的 `dotnet publish` 只输出程序，不携带本宿主或所引用服务的 `appsettings*.json`。部署时在发布目录单独放置宿主 `appsettings.json`（以本目录 `appsettings.example.json` 为模板），并用其中的 `Standalone:ConfigurationPath` 或环境变量 `INDUSTRIAL_PLATFORM_STANDALONE_CONFIG` 指向独立数据库配置文件的绝对路径。该文件可参考 `src/backend/appsettings.Standalone.Development.local.example.json`，SQLite 相对文件路径以这份配置文件所在目录为基准。发布目录与数据库目录应分开保存。

## 独立打包与发布

在仓库根目录运行一次脚本，会构建并更新下面两个固定发布目录；脚本先在临时目录检查产物，再复制到目标目录，不清空服务器上的配置或数据库文件。`-BuildOnly` 仅构建检查，不更新发布目录。

```powershell
Set-Location -LiteralPath 'D:\Code\Industrial Platform\IndustrialPlatform'
.\deploy\scripts\publish-collaboration.ps1 -BuildOnly
.\deploy\scripts\publish-collaboration.ps1
```

| 目录 | 内容 | IIS 用途 |
| --- | --- | --- |
| `D:\Code\Deploy\Collaboration.EmbeddedHost\frontend` | 独立协作 `index.html`、`assets/` 和静态站点 `web.config` | 独立 HTTPS 网站的根目录，不使用完整平台前端 `dist/` |
| `D:\Code\Deploy\Collaboration.EmbeddedHost\backend` | `dotnet publish` 的程序集、依赖和自动生成的 IIS `web.config` | 同一网站下路径为 `/_backend` 的 IIS **应用程序**物理目录 |

首次在 IIS 建站：将 `frontend` 设为独立网站根目录，在该站添加 `/_backend` 应用程序并指向 `backend`，为后端应用分配独立应用程序池，`.NET CLR Version` 设为 `No Managed Code`，工作进程数保持 1。`/_backend` 是应用程序，不是虚拟目录；这样前端内置的同源 `/_backend/...` 请求和 SignalR WebSocket 会直接进入后端，不需要 ARR 反向代理。前端 `web.config` 提供 `/pc/collaboration` 刷新时的 SPA 回退，并排除 `/_backend`。网站根目录需要独立的 HTTPS 域名或绑定；MES 只需打开该站的聊天窗 URL。IIS 子应用的做法见 [Microsoft IIS sub-app 文档](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/advanced?view=aspnetcore-10.0)。

**构建机**需要 .NET 10 SDK、Node.js 24（至少 24.18.0）和 pnpm 11.16.0。**IIS 服务器**采用当前脚本的框架依赖发布方式，需要先安装 IIS 和 [.NET 10 Hosting Bundle](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/hosting-bundle?view=aspnetcore-10.0)；它提供运行时及 ASP.NET Core Module，服务器无需安装 SDK、Node.js 或 pnpm。前端 `web.config` 依赖 [IIS URL Rewrite 模块](https://learn.microsoft.com/en-us/iis/extensions/url-rewrite-module/using-the-url-rewrite-module)；还需启用静态内容、默认文档和 WebSocket Protocol（聊天实时连接与媒体信令使用）。若先装 Hosting Bundle 后装 IIS，应修复安装 Hosting Bundle；安装后按微软文档重启 IIS。IIS 功能说明见 [Microsoft IIS 托管文档](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0)。

后端发布包刻意不带 `appsettings*.json`。首次部署时在 `backend` 目录单独放置按本目录 `appsettings.example.json` 填写的 `appsettings.json`，其中 `EmbeddedCollaboration:Mode` 保持 `Standalone`，`AllowedParentOrigins` 写前端网站**准确的 HTTPS Origin**。将独立数据库配置放在网站目录外的持久化位置，在宿主配置中加入路径，例如：

```json
"Standalone": {
  "ConfigurationPath": "D:\\CollaborationData\\standalone.json"
}
```

`standalone.json` 可参考 `src/backend/appsettings.Standalone.Development.local.example.json`，填写真实 SQLite/PostgreSQL 连接；也可用进程环境变量 `INDUSTRIAL_PLATFORM_STANDALONE_CONFIG` 指定路径。IIS 应用程序池身份需能读取配置、读写 SQLite 数据目录（若使用 SQLite），数据库与真实连接串不得放进 `frontend`。脚本会覆盖程序文件及前端 `web.config`，保留现有配置和数据库，不删除旧版本遗留文件；如需清理旧文件，先备份并人工确认。脚本更新后端期间会短暂写入 `app_offline.htm`，结束后移除；若目录已有该文件，脚本会停止，避免覆盖人工维护状态。

发布后访问 `https://<协作站点域名>/_backend/health/ready` 检查后端，再用 `https://<协作站点域名>/pc/collaboration?mode=standalone&account=<MES用户ID>` 验证页面、消息、状态和媒体信令。若 IIS 返回 500.19，先检查 URL Rewrite 和 `web.config`；若后端启动失败，检查 Hosting Bundle、配置和应用程序池目录权限。若页面能开但实时消息不可达，检查 WebSocket Protocol 和 `/_backend` 应用程序映射。当前仅支持单实例，应用程序池回收会使在线状态暂时中断，客户端重连后恢复。

## 代码位置

宿主源码按 `Models`、`Abstractions`、`Adapters`、`Services`、`Persistence`、`Authorization`、`Web`、`Demo`、`Configuration` 分类，命名空间与目录一致。独立入口在 [Web/StandaloneSessionEndpoint.cs](Web/StandaloneSessionEndpoint.cs)，人员列表替换点在 [Adapters/MesUserDirectoryAdapter.cs](Adapters/MesUserDirectoryAdapter.cs)；平台通用单页入口与 UnifiedHost 不因此改变。
