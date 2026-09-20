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

独立数据库支持 SQLite 与 PostgreSQL；初始化按 Identity → SystemData → ReferenceData → Collaboration 顺序复用既有迁移。Standalone 的在线租约和协作实时投递在宿主进程内完成，不要求 Redis 或 RabbitMQ；`/health/ready` 检查协作数据库与出站队列。当前只支持**单实例**部署，进程重启后在线状态由客户端重连恢复。Standalone 开发环境会自动允许本机当前网卡 IP 的 `https://IP:5173` 来源，切换网络不用改本地配置；正式部署仍须把准确的前端 Origin 写入 `AllowedParentOrigins`，并配置 HTTPS、反向代理、SignalR WebSocket 与 iframe 所需的媒体权限。不要提交本地连接串或密钥。

独立后端的 `dotnet publish` 只输出程序，不携带本宿主或所引用服务的 `appsettings*.json`。部署时在发布目录单独放置宿主 `appsettings.json`（以本目录 `appsettings.example.json` 为模板），并设置环境变量 `INDUSTRIAL_PLATFORM_STANDALONE_CONFIG` 指向独立数据库配置文件的绝对路径。该文件可参考 `src/backend/appsettings.Standalone.Development.local.example.json`，SQLite 相对文件路径以这份配置文件所在目录为基准。发布目录与数据库目录应分开保存。

## 独立打包与发布

以下命令在 Windows PowerShell 中执行。`$packageRoot` 是本机打包输出目录，可按实际环境更换；前后端是两个独立产物，不要把完整平台前端 `dist/` 当作协作前端发布。

```powershell
Set-Location -LiteralPath 'D:\Code\Industrial Platform\IndustrialPlatform'
$packageRoot = 'D:\Code\Industrial Platform\开发辅助文件\发布包\Collaboration'
$backendOut = Join-Path $packageRoot 'backend'
dotnet publish 'src/backend/src/Hosts/IndustrialPlatform.Collaboration.EmbeddedHost/IndustrialPlatform.Collaboration.EmbeddedHost.csproj' --configuration Release --output $backendOut

Set-Location -LiteralPath 'D:\Code\Industrial Platform\IndustrialPlatform\src\frontend'
$env:VITE_DEPLOYMENT_ENVIRONMENT = 'PROD'
pnpm install --frozen-lockfile
pnpm build:collaboration
$frontendOut = Join-Path $packageRoot 'frontend'
New-Item -ItemType Directory -Path $frontendOut -Force | Out-Null
Copy-Item -Path '.\dist-collaboration\*' -Destination $frontendOut -Recurse -Force
```

后端发布目录包含 `IndustrialPlatform.Collaboration.EmbeddedHost.dll` 及依赖文件；前端发布目录包含 `index.html` 和 `assets/`。部署时将两个目录分别交给后端进程和静态站点。后端发布包刻意不带配置文件：在服务器的后端目录放置按 `appsettings.example.json` 填写的 `appsettings.json`，将 `EmbeddedCollaboration:AllowedParentOrigins` 改为前端实际 HTTPS Origin；另在持久化位置放置独立数据库配置，并把 `INDUSTRIAL_PLATFORM_STANDALONE_CONFIG` 设为它的绝对路径。例如：

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:56365'
$env:INDUSTRIAL_PLATFORM_STANDALONE_CONFIG = 'D:\CollaborationData\standalone.json'
Set-Location -LiteralPath 'D:\Collaboration\backend'
dotnet .\IndustrialPlatform.Collaboration.EmbeddedHost.dll
```

上例的服务器路径仅示意，`standalone.json` 可从 `src/backend/appsettings.Standalone.Development.local.example.json` 建立并填写实际 SQLite/PostgreSQL 连接。不要将真实连接串或数据库文件放入静态站点。反向代理需对前端地址提供 HTTPS 和 `/pc/collaboration` 的 SPA 回退，将同源 `/_backend/*` 去掉 `/_backend` 前缀后转发到后端，并转发 SignalR WebSocket。发布后先检查后端 `/health/ready`，再用前端地址 `/pc/collaboration?mode=standalone&account=<MES用户ID>` 验证。当前部署仅支持单个后端实例。

## 代码位置

宿主源码按 `Models`、`Abstractions`、`Adapters`、`Services`、`Persistence`、`Authorization`、`Web`、`Demo`、`Configuration` 分类，命名空间与目录一致。独立入口在 [Web/StandaloneSessionEndpoint.cs](Web/StandaloneSessionEndpoint.cs)，人员列表替换点在 [Adapters/MesUserDirectoryAdapter.cs](Adapters/MesUserDirectoryAdapter.cs)；平台通用单页入口与 UnifiedHost 不因此改变。
