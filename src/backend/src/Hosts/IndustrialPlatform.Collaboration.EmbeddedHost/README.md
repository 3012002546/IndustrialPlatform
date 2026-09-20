# IndustrialPlatform.Collaboration.EmbeddedHost

独立部署的协作服务。MES 打开 `.../pc/collaboration?mode=standalone&account=<当前账户>`，并提供只含 `userId`、`userName` 的用户列表；宿主用 `account` 精确匹配 `userId`。这只校验账号在列表中，**不证明 MES 登录身份**。页面会话、数据和聊天/在线/语音/屏幕共享能力由宿主处理，不使用 MES 权限，也不影响原 PC 端。

## 接入 MES

- 只替换 [MesUserDirectoryAdapter.cs](Adapters/MesUserDirectoryAdapter.cs) 中的 `LoadMesUsersAsync`：把临时 `new MesUser(...)` 改为调用真实用户列表接口。`userId` 须稳定、唯一；`userName` 用于显示。
- 前端拼接 `mode=standalone&account=<当前MES账户>`。宿主的 `POST /embedded/standalone/session` 会校验账号和页面 Origin，签发短期页面会话；用户被移出列表后不再续期。
- `Sources`、`SourceSessions`、`Demo:Accounts` 和 `FixedDemo` 不属于 Standalone 配置。

## 本地运行

1. 将本目录 `appsettings.example.json` 复制为 `appsettings.json`；将 `src/backend/appsettings.Standalone.Development.local.example.json` 复制为同目录下不带 `.example` 的文件，填写独立数据库连接。不要提交连接串。
2. 在 `src/backend` 运行：

   ```powershell
   dotnet run --project 'src/Hosts/IndustrialPlatform.Collaboration.EmbeddedHost/IndustrialPlatform.Collaboration.EmbeddedHost.csproj' --launch-profile Collaboration.EmbeddedHost
   ```

3. 在 `src/frontend` 运行 `pnpm dev:lan:https:collaboration`，打开 `https://localhost:5173/pc/collaboration?mode=standalone&account=operator-1`。本地后端端口为 `56364/56365`；它们和前端的 `5173` **不是 IIS 发布端口**。

## 打包与 IIS 发布（HTTPS 5443 示例）

在仓库根目录运行 `./deploy/scripts/publish-collaboration.ps1`。先加 `-BuildOnly` 可只检查构建。脚本输出到下列目录，不覆盖已有 `appsettings*.json` 或数据库；后端更新期间会短暂使用 `app_offline.htm`。

| IIS 位置 | 发布目录或设置 |
| --- | --- |
| 新网站根目录 | `D:\Code\Deploy\Collaboration.EmbeddedHost\frontend` |
| 新网站绑定 | `https`、`5443`、实际主机名及匹配且受信任的证书 |
| 网站下添加**应用程序** `_backend` | `D:\Code\Deploy\Collaboration.EmbeddedHost\backend`；单独应用程序池 `Collaboration.EmbeddedHost.Backend`，`No Managed Code`，工作进程数 `1` |

- 表中 `D:\Code\Deploy\...` 是脚本输出；若复制到其他服务器，IIS 物理路径改为该服务器的实际目录。本次服务器使用 `C:\Wellthinic\Collaboration.EmbeddedHost`。
- 前后端共用 **5443**，`/_backend` 不另设端口，也不需要 ARR。放行 TCP 5443；`localhost` 只供服务器本机访问。
- IIS 服务器启用静态内容、默认文档、WebSocket Protocol，安装 **.NET 10 Hosting Bundle** 和 **IIS URL Rewrite**；构建机需要 .NET 10 SDK、Node.js 24（至少 24.18.0）和 pnpm 11.16.0。
- 实际主机名须能解析到 IIS 服务器，证书须匹配且受浏览器信任，否则麦克风与屏幕共享不可用。

后端发布包**不含** `appsettings*.json`。在发布后的 `backend` 放置宿主 `appsettings.json`，关键配置为：

```json
{
  "EmbeddedCollaboration": {
    "Mode": "Standalone",
    "Initialization": { "Enabled": true },
    "PlatformTenantNId": "standalone",
    "AllowedParentOrigins": ["https://collab.mes.local:5443"]
  },
  "Standalone": {
    "ConfigurationPath": "D:\\Code\\Deploy\\Collaboration.EmbeddedHost\\data\\standalone.json"
  }
}
```

`AllowedParentOrigins` 必须与前端实际 Origin 一致；若本机用 `https://localhost:5443` 测试，将它也加入数组。`standalone.json` 参考 `src/backend/appsettings.Standalone.Development.local.example.json`，填写 `Standalone:Database`。建议把数据库和该配置放在前后端发布目录之外；SQLite 相对路径以 `standalone.json` 所在目录为基准。后端应用程序池身份需要读取配置、**修改 SQLite 文件及其所在目录**的权限。

验收：先访问 `https://实际主机名:5443/_backend/health/ready`，再打开 `https://实际主机名:5443/pc/collaboration?mode=standalone&account=<MES用户ID>`，确认会话请求成功、消息及媒体可用。当前仅支持**单实例**部署。

## 本次故障：IIS 503.0 / SQLite 只读

在 `C:\Wellthinic\Collaboration.EmbeddedHost` 的实际部署中，前端能打开，健康检查曾返回 `Healthy`，但会话请求得到 **IIS 503.0 Server has been shutdown**。事件查看器“应用程序”日志显示 `RemoteAssistanceLifecycleWorker` 写 SQLite 时发生 `SQLite Error 8: attempt to write a readonly database`；后台服务异常导致宿主停止。**健康检查通过不代表数据库可写。**

1. 从后端 `appsettings.json` 的 `Standalone:ConfigurationPath` 找到 `standalone.json`，再从 `Standalone:Database:SqliteFile` 确定实际 `.db` 路径。宿主按它生成连接；只改 `appsettings.json` 的 `SqlSugar:ConnectionString` 无效。
2. 给实际数据库**所在目录**的 `IIS AppPool\Collaboration.EmbeddedHost.Backend` 授予“修改”权限，确保 `.db` 没有只读属性，并允许创建 SQLite 日志文件。不要给整个网站或磁盘写权限。
3. 重启后端应用程序池，重新验证健康检查及 `session?account=...` 请求。如果仍为 503.0，按时间检查事件查看器“系统”的 WAS 事件和“应用程序”的 .NET Runtime 错误；不要用 `BackgroundServiceExceptionBehavior=Ignore` 掩盖写入失败。

独立入口在 [StandaloneSessionEndpoint.cs](Web/StandaloneSessionEndpoint.cs)。IIS 子应用配置参见 [Microsoft 文档](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/advanced?view=aspnetcore-10.0)；SQLite 目录写权限参见 [SQLite WAL 文档](https://www.sqlite.org/wal.html)。
