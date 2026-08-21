# 本地调试指南

> 适用环境:Windows 11 / 本地无 Docker。数据库/缓存等依赖有两种形态:**无依赖 SQLite 基线**(2026-08-10 验证)与**经 RemoteDevelopment 连云端基础设施**(Tailnet,2026-08-12 验证)。本文记录于 2026-08-10,2026-08-12 更新启动方式勘误与云端联调结论,2026-08-13 补充 VS2026 与 VS Code 的详细调试步骤,2026-08-16 更新为默认 UnifiedHost 单进程调试 + admin 初始化脚本。

## 目录

0. [Windows 日常调试快速开始](#windows-日常调试快速开始)
1. [已验证的运行状态](#一已验证的运行状态)
2. [后端调试(VS2026)](#二后端调试vs2026)
3. [后端调试(VS Code)](#三后端调试vs-code)
4. [前端调试(VS Code)](#四前端调试vs-code)
5. [质量门禁命令](#五质量门禁命令)
6. [已知预期与注意事项](#六已知预期与注意事项)

---

## Windows 日常调试快速开始

当前默认调试拓扑为一个 `IndustrialPlatform.UnifiedHost` 后端进程(`:5041`)加一个 Vite 前端进程(`:5173`)。UnifiedHost 同时装载 Identity、SystemData 和 ReferenceData；日常整体调试不需要再启动 Gateway 和各独立 API Host。

### 0.1 检查本地私有配置

确认以下文件存在：

```text
src/backend/appsettings.Development.local.json
```

使用云端开发基础设施时，确保其中 `RemoteDevelopment.Enabled=true` 且数据库拓扑配置完整。该文件已被 Git 忽略，禁止提交或输出其中的服务器地址、账号、密码和 SSH 信息。

所有 Development 后端 Host 会从各自项目目录向上自动定位这一份统一配置，不依赖 Visual Studio profile、`launchSettings.json` 或当前工作目录。找不到文件或 `RemoteDevelopment.Enabled=false` 时启动会明确失败，不再静默回退本地 SQLite。SQLite 仅供自动化测试显式使用。

### 0.2 首次初始化 admin

在仓库根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Initialize-DevelopmentAdmin.ps1
```

首次创建成功后，一次性凭据默认写入：

```text
%LOCALAPPDATA%\IndustrialPlatform\bootstrap-admin-<UTC>.json
```

登录名为 `admin`，初始密码读取 JSON 中的 `temporaryPassword`。重复执行会显示“已初始化，无新凭据”，不会覆盖 admin 或重新签发密码；此时应使用此前安全保存的凭据。完整初始化说明见 [2.4A](#24a-admin-初始化development-一次性凭据)。

若 Development 环境的 admin 已存在但一次性凭据遗失，可显式重置：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Reset-DevelopmentAdmin.ps1
```

新凭据默认写入 `%LOCALAPPDATA%\IndustrialPlatform\reset-admin-<UTC>.json`。该命令只允许 Development，重置会推进安全版本并撤销既有登录态；不得用于生产环境。

### 0.3 用 Visual Studio 调试后端

1. 打开 `src/backend/IndustrialPlatform.slnx`。
2. 将 `IndustrialPlatform.UnifiedHost` 设为启动项目。
3. 设置断点后按 `F5`。
4. 检查 `http://localhost:5041/health` 和 `http://localhost:5041/health/ready`。

UnifiedHost 与独立 `Identity.Api` 都使用 `5041`，不能同时启动。若不需要后端断点，也可以在仓库根目录执行：

```powershell
.\deploy\scripts\dev.ps1 start -SkipInfrastructure
```

查看和停止默认 UnifiedHost：

```powershell
.\deploy\scripts\dev.ps1 status
.\deploy\scripts\dev.ps1 stop
```

### 0.4 启动真实登录前端

前端 Development 默认已指向 UnifiedHost `http://localhost:5041`，通常无需创建 `.env.local`。如需显式覆盖，可创建或修改 `src/frontend/.env.local`：

```env
VITE_AUTH_MODE=http
VITE_API_BASE_URL=http://localhost:5041
```

仅在调试独立 Gateway + Api Host 拓扑时，将 `VITE_API_BASE_URL` 显式覆盖为 `http://localhost:5080`。

另开终端启动前端：

```powershell
cd src\frontend
pnpm dev
```

打开 `http://localhost:5173`，使用 `admin` 和一次性凭据文件中的 `temporaryPassword` 登录。页面仍显示 `mock.admin` 时，先确认 `.env.local` 中为 `VITE_AUTH_MODE=http`，然后完全停止并重新启动 `pnpm dev`。

推荐的日常顺序是：检查私有配置 → 首次初始化 admin → Visual Studio F5 启动 UnifiedHost → `pnpm dev` → 登录调试。

---

## 一、已验证的运行状态

### 1.1 无依赖(SQLite)基线 —— 2026-08-10

后端三服务手工启动后逐项探测(探测后已停止,端口已释放):

| 探测项 | 结果 | 说明 |
| --- | --- | --- |
| Identity 直接访问 `:5041/health` | ✅ HTTP 200 | 服务正常启动 |
| ReferenceData 直接访问 `:62311/health` | ✅ HTTP 200 | 同上 |
| Gateway `/health` | ✅ HTTP 200 | 统一入口存活 |
| 网关转发 `/identity/health` | ✅ `{"status":"Healthy","service":"Identity"}` | YARP 前缀剥离转发正常 |
| 网关转发 `/referencedata/health` | ✅ `{"status":"Healthy","service":"ReferenceData"}` | 同上 |
| `/health/live` | ✅ HTTP 200 | 存活探针 |
| `/health/ready` | ⚠️ HTTP 503 | 预期:依赖全不可用,聚合 503(见 6.1) |
| `/unknown` | ✅ HTTP 404 信封 | `{"success":false,"code":"404","message":"路由不存在","data":null}` |

### 1.2 连接云端基础设施 —— 2026-08-12 实测

私有配置 `RemoteDevelopment.Enabled=true` 时,后端连云端(Tailnet)PostgreSQL/Redis 真实依赖:

| 探测项 | 结果 | 说明 |
| --- | --- | --- |
| 云端容器 postgres/redis/rabbitmq/seq | ✅ 全部 `running + healthy` | SSH `sudo docker inspect` 确认 |
| Identity `/health/ready` | ✅ HTTP 200 Healthy | `postgres Healthy`、`redis Healthy`、`seq 未启用跳过`;**本地 SQLite 基线为 503** |
| Identity 迁移 | ✅ 云端 `identity_db` 账本 16/16 | ID-004-12..16(SSO 新表)本次应用,幂等生效 |
| Identity 登录链路 | ✅ 全走云端 | 错误密码 → `ID_AUTH_INVALID_CREDENTIALS`(防枚举);失败审计落库(`identity_login_audit.result=Failure`,IP 仅哈希) |
| Gateway 转发 `/identity/health`、`/referencedata/health` | ✅ 转发正常 | 前缀剥离 |
| Gateway `/health/ready` 聚合 | ⚠️ `service.identity Healthy` / `service.referencedata Unhealthy` | 后者源于下游 ReferenceData 的 rabbitmq 检查(见 6.2) |
| ReferenceData `/health/ready` | ⚠️ Unhealthy | PG/Redis Healthy,rabbitmq 检查超时(见 6.2) |

前端独立可跑全流程:第一批 E2E **35/35**(mock 登录 → PC/PDA/Mobile 三端首页 → 403/404 → 退出)已通过 Vite dev server 验证。

---

## 二、后端调试(VS2026)

> **默认整体调试:只启动一个 `IndustrialPlatform.UnifiedHost` 进程(`:5041`,组合 Identity/SystemData/ReferenceData),前端 Vite(`:5173`)直连它,不再需要 Gateway + 三服务。** 独立服务调试(Gateway `:5080` + Identity `:5041` + ReferenceData `:62311`)仍保留,供边界验证与未来拆分。

### 2.1 打开解决方案

```text
src/backend/IndustrialPlatform.slnx
```

- VS2026 原生支持 `.slnx`(XML 解决方案格式),双击或用「打开解决方案」打开即可。仓库**没有** `.sln` 文件。
- 首次打开会触发 NuGet 还原;若还原报错(Windows 常见),先设置本地 CLI home(见 2.4)。

### 2.2 配置启动项目

**默认(推荐):UnifiedHost 单进程**
1. 解决方案资源管理器 → 右键 `IndustrialPlatform.UnifiedHost` → **设为启动项目** → F5。
2. 前端另开终端:`cd src/frontend && pnpm dev`(`VITE_API_BASE_URL` 指向 `http://localhost:5041`,见 4.4)。
3. 首次使用先执行 admin 初始化(见 2.4A),再登录 `admin`。

**方式 A:多启动项目(独立服务,边界验证/未来拆分)**
1. 解决方案资源管理器 → 右键 `IndustrialPlatform.slnx` → **配置启动项目**。
2. 选「多个启动项目」,把以下三个设为 **启动**,其余设为「无」:

| 项目 | 操作 |
| --- | --- |
| `IndustrialPlatform.Gateway` | 启动 |
| `IndustrialPlatform.Identity.Api` | 启动 |
| `IndustrialPlatform.ReferenceData.Api` | 启动 |

3. 「操作」列的下拉框里还能选 **不调试启动**(只跑不挂调试器,启动快、适合只调某一个服务时其余当陪跑)。

> 该选择会被 VS 保存到 `src/backend/IndustrialPlatform.slnLaunch.user`(与 `.slnx` 同目录),下次打开自动恢复。

**方式 B:单服务调试**
只想调 Identity 时,右键 `IndustrialPlatform.Identity.Api` → **设为启动项目** → F5;需要网关转发时再用 `dotnet run`(见 2.3)把其余服务跑起来。

### 2.3 启动方式(关键,别踩 ContentRoot 坑)

> ⚠️ **必须用 F5 或 `dotnet run --no-build --project <csproj>` 启动,让 ContentRoot = 项目目录。**
> **不要**从仓库根裸跑 `dotnet bin/Debug/net10.0/*.dll` —— 那样 ContentRoot 变成仓库根,`appsettings.json`/`appsettings.Development.json` 全部不加载(私有配置虽经绝对路径加载,但基础配置缺失),Gateway 会因 `Services` 为空导致 YARP 路由全 404、健康检查为空。

F5 / `dotnet run` 会读项目的 `Properties/launchSettings.json`,里面钉好端口并注入关键环境变量。profile 列表:

| 项目 | profile 名 | URL | 注入的额外环境变量 |
| --- | --- | --- | --- |
| UnifiedHost(默认) | `http` | `http://localhost:5041` | `IndustrialPlatform__LocalConfigurationPath=../../../../appsettings.Development.local.json` |
| Identity | `http` | `http://localhost:5041` | `IndustrialPlatform__LocalConfigurationPath=../../../../appsettings.Development.local.json` |
| Gateway | `http` | `http://localhost:5080` | 无(网关不需要私有 DB 配置) |
| ReferenceData | `IndustrialPlatform.ReferenceData.Api` | `https://localhost:62310;http://localhost:62311` | `IndustrialPlatform__LocalConfigurationPath=../../../../appsettings.Development.local.json` |

- ReferenceData 的 profile **不是** `http`,且默认 `launchBrowser: true`(F5 会弹浏览器,可忽略或改成 `false`)。
- ReferenceData 走 https `62310` 首次会触发开发证书信任;只想 http 可把 `applicationUrl` 里的 https 段去掉,或直接在启动项目下拉选 http 段。
- UnifiedHost 与 Identity 端口同为 `5041`:**同一时刻只启动其中一套后端**(默认 UnifiedHost;独立服务调试时才启动 Identity)。

**CLI 启动**(仓库根 `D:\Code\Industrial Platform\IndustrialPlatform`,先设 CLI home):

```bash
export DOTNET_CLI_HOME="$(git rev-parse --show-toplevel)/.dotnet_cli_home"

# 默认:UnifiedHost 单进程
dotnet run --no-build --project "src/backend/src/Hosts/IndustrialPlatform.UnifiedHost/IndustrialPlatform.UnifiedHost.csproj"

# 独立服务调试
dotnet run --no-build --project "src/backend/src/Services/Identity/IndustrialPlatform.Identity.Api/IndustrialPlatform.Identity.Api.csproj"
dotnet run --no-build --project "src/backend/src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Api/IndustrialPlatform.ReferenceData.Api.csproj"
dotnet run --no-build --project "src/backend/src/Gateway/IndustrialPlatform.Gateway/IndustrialPlatform.Gateway.csproj"
```

仓库统一启动脚本:`.\deploy\scripts\dev.ps1 start`(默认仅 UnifiedHost);`.\deploy\scripts\dev.ps1 start -IndependentServices` 启动 Gateway + Identity + ReferenceData 独立服务。`status`/`stop` 与 `start` 目标模式一致。

`--no-build` 前提是先 `dotnet build` 过;`http` Profile 会注入 `IndustrialPlatform__LocalConfigurationPath` 并钉好端口。

### 2.4 环境变量与私有配置

- **NuGet 还原**:Windows 下 NuGet 还原异常时,设 `DOTNET_CLI_HOME=<仓库根>\.dotnet_cli_home`。可在「工具 → 选项 → 环境 → 环境变量」或项目属性里配,或只在命令行用 `export`。
- **环境 = Development**:三个服务的 profile 都注入 `ASPNETCORE_ENVIRONMENT=Development`,所以启动即加载 `appsettings.Development.json`。
- **私有配置**:`IndustrialPlatform__LocalConfigurationPath` 指向 `src/backend/appsettings.Development.local.json`(相对 ContentRoot 上跳 4 级)。该文件存在且 `RemoteDevelopment.Enabled=true` 时,后端自动连云端 PG/Redis(Tailnet),无需本地 Docker。文件在 `.gitignore` 中,**禁止提交其中的服务器地址/账号/密码/SSH**。
- **bootstrap 管理员**:通过 `--initialize-admin` 显式初始化创建(见 2.4A),不再使用环境变量密码引导。

### 2.4A admin 初始化(Development 一次性凭据)

> 首次登录前需先创建内置 admin。初始化只允许 `ASPNETCORE_ENVIRONMENT=Development`,
> 复用 `IdentityInitializationService`,**重复执行幂等:不覆盖既有 admin、不重发凭据**。

**Windows 一键脚本**(推荐,自动校验前置条件并调用准确 csproj):

```bash
powershell -ExecutionPolicy Bypass -File scripts/Initialize-DevelopmentAdmin.ps1
# 可选指定输出路径(默认 %LOCALAPPDATA%\IndustrialPlatform\bootstrap-admin-<UTC>.json)
powershell -ExecutionPolicy Bypass -File scripts/Initialize-DevelopmentAdmin.ps1 -CredentialOutput D:\secrets\bootstrap-admin.json
```

**手动命令**(先 `dotnet build` 过,`--no-build` 前提同 2.3):

```bash
dotnet run --no-build --project "src/backend/src/Services/Identity/IndustrialPlatform.Identity.Api/IndustrialPlatform.Identity.Api.csproj" -- --initialize-admin --credential-output D:\secrets\bootstrap-admin.json
```

要点:

- 首次实际创建 admin 时才生成凭据 JSON(8 字段:`tenantNId`/`userNId`/`loginName`/`temporaryPassword`/`deliveryReference`/`recoveryReference`/`deliveryId`/`createdOnUtc`),写入方式为临时文件 + 原子重命名,**失败不留半文件**;Windows 仅当前用户可访问,Linux `0600`。
- **重复执行不生成、不覆盖、不重发**:admin 已存在时 stdout 显示「已初始化,无新凭据」。
- 输出路径必须为绝对路径且目标文件不存在;已存在立即拒绝(脚本在调用 dotnet 前检查,命令内再次兜底)。
- 提供 `--credential-output` 时 stdout 只显示脱敏状态、种子账本、是否新建 admin 与输出路径,**不显示密码或一次性引用**;不提供时保留控制台一次性交付兼容行为(凭据直接打印,慎用)。
- 凭据文件请立即安全保存;`temporaryPassword` 首次登录后应尽快修改。**禁止提交或输出该文件内容**。
- Linux/Docker 侧脚本见 `deploy/application/README.md`(`bootstrap-admin.sh`,使用不可变应用镜像在容器内完成同语义初始化)。

### 2.5 断点与调试窗口

- **设断点**:行首左侧槽单击,或光标停该行按 `F9`;再按 `F9` 删除。断点圆点空心(带警告)表示该代码当前未加载或符号不匹配。
- **运行到断点**:`F5` 启动调试,命中后程序暂停。
- **单步**:`F10` 逐过程(不进入函数)、`F11` 逐语句(进入函数)、`Shift+F11` 跳出当前函数、`F5` 继续到下一断点、`Shift+F5` 停止调试、`Ctrl+F5` 不调试直接启动。
- **查看变量**:调试暂停时,悬停变量看值;`监视` 窗口可加表达式(如 `user.FailedLoginCount`);`局部变量` 窗口看当前作用域;`即时窗口` 可在暂停时执行 C# 表达式(如 `user.NId`)。
- **调用堆栈**:`调用堆栈` 窗口看调用链,双击任意帧可跳转;对理解请求从 Gateway → 服务 → Application → Domain 的路径很有用。

推荐断点位置:

| 关注点 | 位置 |
| --- | --- |
| 登录编排/并发重试 | `Identity.Application/Authentication/AuthenticationService.cs` 的 `LoginAsync`、`RecordLoginSuccessAsync`、`RecordLoginFailureAsync` |
| 乐观并发原子更新 | `Identity.Infrastructure/Persistence/Repositories/UserRepository.cs` 的 `UpdateAsync`/`UpdateParentAsync` |
| 依赖健康检查 | 各服务 `Health/` 目录下的健康检查类 |
| 网关转发/错误 | Gateway 的 YARP 中间件、`IndustrialPlatform.Web.Middleware.RequestLoggingMiddleware`、`ExceptionMiddleware` |

### 2.6 条件断点、数据断点、异常设置

- **条件断点**:右键断点 → 「条件」→ 输入表达式(如 `user.NId == "user.alice"`),或按命中次数(如第 2 次命中才停)。适合在循环/高频路径里只拦感兴趣的那一次。
- **跟踪点(日志断点)**:右键断点 → 「操作」→ 勾选「继续执行」并填要输出的消息(如 `登录用户 {user.NId}`),不中断程序只打印到输出窗口,比到处加 `Console.WriteLine` 干净。
- **数据断点**:在 `局部变量`/`监视` 里右键字段 → 「断开当值更改时」,字段被写时停;用于追踪「谁在改这个版本号/状态」这类问题。
- **异常设置**(重要):`调试 → 窗口 → 异常设置`,勾选「公共语言运行时异常(Common Language Runtime Exceptions)」可让**任何**抛出的异常都停。但本项目大量「抛出即捕获」的领域异常(`DomainException`/`ConcurrencyException`/`UnauthorizedException` 等)会频繁中断;建议**默认不勾**,只在你怀疑某个异常时临时勾选并复现。

### 2.7 热重载(Hot Reload)

- 暂停或运行中修改 C# 后,点工具栏「火苗」图标(热重载)或默认快捷键(通常 `Alt+F10` 系列),把改动热应用,不重启进程。
- 仅限方法体等可热应用改动;改签名、改属性、加类成员等会提示「不支持的更改」,此时需重启(F5 或 `Shift+F5` 后重跑)。
- ASP.NET Core 项目热重载对控制器/服务方法内逻辑基本可用,是调 `AuthenticationService` 这类业务逻辑的高频手段。

### 2.8 附加到已运行进程(可选)

- `dotnet run` 启动的服务进程名是 `dotnet.exe`,多个 dotnet 进程并跑时难区分;要附加用 `调试 → 附加到进程`,按「标题」或「命令行」列分辨(命令行里带 `IndustrialPlatform.Identity.Api.dll` 那行即对应服务)。
- 一般不需要:能用 F5 一步到位启动 + 调试,就别用附加;只有「服务已在跑、只想挂个调试器看某个请求」时才用。

### 2.9 后端 VS2026 常见问题

| 现象 | 处理 |
| --- | --- |
| 裸跑 DLL 后所有路由 404 / 健康检查为空 | ContentRoot 错位,改用 F5 或 `dotnet run --project` 启动(见 2.3) |
| 端口被占 `:5041/:5080/:62311` | `netstat -ano | findstr :5041` 找 PID 后 `taskkill /PID <pid> /F`,或改 launchSettings 端口 |
| NuGet 还原失败 | 设 `DOTNET_CLI_HOME`(见 2.4)后重新还原 |
| SDK 版本告警 | 本机 10.0.400,`global.json` 钉 10.0.302 + `rollForward: latestFeature`,VS 正常解析 |
| ReferenceData 弹浏览器 | profile `launchBrowser: true` 所致,忽略或改 `false` |
| https 证书错误(62310) | `dotnet dev-certs https --trust` 信任开发证书,或只用 http 段 |

---

## 三、后端调试(VS Code)

> VS2026 之外的替代:用 VS Code 的 **C# Dev Kit** 调试后端。核心区别是 VS Code 不读 `launchSettings.json` 的端口,**必须自己在 `launch.json` 里配 `cwd`(ContentRoot)与 `ASPNETCORE_URLS`**。

### 3.1 安装扩展

- 安装 **C# Dev Kit**(`ms-dotnettools.csdevkit`,会一并带上 C# 扩展与 .NET Install Tool)。
- 用 VS Code 打开 `src/backend` 目录(或仓库根),C# Dev Kit 会自动发现 `IndustrialPlatform.slnx`。

### 3.2 关键概念:ContentRoot 与端口

VS Code 的 coreclr 调试器是直接拉起编译好的 DLL,与「裸跑 DLL」同一条路,**不读 launchSettings**。所以要手动补齐两件事:

1. **`cwd` = 项目目录** → 让 `WebApplication.CreateBuilder` 的 ContentRoot 落在项目目录,`appsettings.json` 才能加载(否则又是「路由全 404」的坑)。
2. **`ASPNETCORE_URLS` 钉端口** → launchSettings 里的 `applicationUrl` 对 DLL 直启无效,必须自己设。
3. **`IndustrialPlatform__LocalConfigurationPath`** → 相对 ContentRoot 上跳 4 级,连云端基础设施时必填(与 Identity/ReferenceData 的 launchSettings 一致)。

### 3.3 `.vscode/launch.json`(放在 `src/backend/.vscode/` 或仓库根 `.vscode/`)

```jsonc
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Identity",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "build",
      "program": "${workspaceFolder}/src/Services/Identity/IndustrialPlatform.Identity.Api/bin/Debug/net10.0/IndustrialPlatform.Identity.Api.dll",
      "cwd": "${workspaceFolder}/src/Services/Identity/IndustrialPlatform.Identity.Api",
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "ASPNETCORE_URLS": "http://localhost:5041",
        "IndustrialPlatform__LocalConfigurationPath": "../../../../appsettings.Development.local.json"
      },
      "stopAtEntry": false
    },
    {
      "name": "ReferenceData",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "build",
      "program": "${workspaceFolder}/src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Api/bin/Debug/net10.0/IndustrialPlatform.ReferenceData.Api.dll",
      "cwd": "${workspaceFolder}/src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Api",
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "ASPNETCORE_URLS": "http://localhost:62311",
        "IndustrialPlatform__LocalConfigurationPath": "../../../../appsettings.Development.local.json"
      },
      "stopAtEntry": false
    },
    {
      "name": "Gateway",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "build",
      "program": "${workspaceFolder}/src/Gateway/IndustrialPlatform.Gateway/bin/Debug/net10.0/IndustrialPlatform.Gateway.dll",
      "cwd": "${workspaceFolder}/src/Gateway/IndustrialPlatform.Gateway",
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "ASPNETCORE_URLS": "http://localhost:5080"
      },
      "stopAtEntry": false
    }
  ],
  "compounds": [
    {
      "name": "全部后端服务",
      "configurations": ["Identity", "ReferenceData", "Gateway"]
    }
  ]
}
```

> 上面 `${workspaceFolder}` 假设你打开的是 **`src/backend`** 目录;若打开的是仓库根,把路径里的 `src/` 前缀补成 `src/backend/src/...`,并把 `../../../../appsettings.Development.local.json` 保持不动(它相对的是各项目的 ContentRoot,不是 workspaceFolder)。

### 3.4 `.vscode/tasks.json`(配合 preLaunchTask 自动构建)

```jsonc
{
  "version": "2.0.0",
  "tasks": [
    {
      "label": "build",
      "command": "dotnet",
      "type": "process",
      "args": ["build", "IndustrialPlatform.slnx", "--nologo"],
      "options": {
        "cwd": "${workspaceFolder}",
        "env": { "DOTNET_CLI_HOME": "${workspaceFolder}/.dotnet_cli_home" }
      },
      "problemMatcher": "$msCompile",
      "group": { "kind": "build", "isDefault": true }
    }
  ]
}
```

- `preLaunchTask: "build"` 会在 F5 前先 `dotnet build`,保证 `program` 指向的 DLL 是最新;若只想调不改代码,可去掉 `preLaunchTask` 改用 `--no-build`(但 coreclr 调试器本身不带 `--no-build`,需手动先 build)。

### 3.5 调试流程与技巧

1. F5(或「运行与调试」→ 选 **Identity** / **全部后端服务**)启动;命中断点即暂停。
2. 与 VS2026 相同:F9 断点、F10/F11/Shift+F11 单步、悬停看值、`监视`/`局部变量`/`调用堆栈` 面板、条件断点、`调试控制台` 里执行表达式。
3. **多服务并行**:选 `compounds` 里的「全部后端服务」一次性拉起三进程(各自一个调试会话,左上角可切换)。
4. 想只调 Identity 又要网关转发,可:Identity 用 F5 调试,另外两个在集成终端用 `dotnet run --no-build --project ...` 跑。

### 3.6 后端 VS Code 常见问题

| 现象 | 处理 |
| --- | --- |
| 启动后全 404 / 配置缺失 | `cwd` 没指向项目目录,或 `ASPNETCORE_URLS` 没设(见 3.2) |
| 连不上云端库 | `IndustrialPlatform__LocalConfigurationPath` 没设或相对路径错(cwd 不是项目目录导致) |
| `preLaunchTask` 找不到 | 确认 `tasks.json` 与 `launch.json` 在同一 `.vscode/` 下、`label` 一致 |
| 断点空心/「未加载符号」 | 没 build 或 DLL 路径/`program` 拼错,先 `dotnet build` 再 F5 |
| 端口冲突 | 改 `env.ASPNETCORE_URLS` 的端口 |

---

## 四、前端调试(VS Code)

> 前端 Vue 3 + Vite 单包工程,目录 `src/frontend`。调试分两种:浏览器调试(断点打在 `.ts`/`.vue`)与 Playwright E2E 调试。

### 4.1 打开目录与工具链

- 用 VS Code 打开 `src/frontend`(或作为多根工作区的一根)。
- 仓库根 `.mise.toml` 钉定 **node 24.18.0 / pnpm 11.16.0**;集成终端若 node 版本不对(如系统自带 node 22),一律用 `mise exec -- pnpm ...`。

```bash
cd src/frontend
pnpm install --frozen-lockfile   # 严格按锁文件
pnpm dev                          # → http://localhost:5173
```

- 前端只通过 `VITE_API_BASE_URL`(默认 `http://localhost:5080`,Gateway)访问后端,不直连服务端口。

### 4.2 安装插件

- **Vue - Official(Volar)**:`.vue` 智能提示 + 模板/脚本类型检查;若类型报错是陈旧状态,命令面板执行 `Vue: Restart Vue Server`。
- 可选:ESLint、Prettier(与 `pnpm lint` / `pnpm format:check` 对齐)。

### 4.3 浏览器调试 `.vue` / `.ts`

1. 集成终端先 `pnpm dev` 把 Vite 跑起来(5173)。
2. 新增 `.vscode/launch.json`:

```jsonc
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Vite: Chrome",
      "type": "chrome",
      "request": "launch",
      "url": "http://localhost:5173",
      "webRoot": "${workspaceFolder}"
    },
    {
      "name": "Vite: Edge",
      "type": "msedge",
      "request": "launch",
      "url": "http://localhost:5173",
      "webRoot": "${workspaceFolder}"
    }
  ]
}
```

3. 在 `.vue` 的 `<script setup>` 或 `.ts` 里设断点,F5 启动 Chrome/Edge;Vite dev 默认带 sourcemap,断点能落回源码。
4. 浏览器里触发登录/路由跳转,命中断点后在 VS Code 侧栏 `运行` 面板看变量、单步。

> `.vue` 的 `<template>` 里也能断(经 sourcemap 映射),但 DOM 相关逻辑更建议用浏览器 DevTools 的 Elements/Sources 面板配合。

### 4.4 认证模式(mock / http)

运行配置由 `VITE_*` 环境变量决定(安全示例见 `.env.example`,真实凭据不得提交):

| 变量 | 默认 | 说明 |
| --- | --- | --- |
| `VITE_API_BASE_URL` | `http://localhost:5080` | 统一入口。**默认整体调试(UnifiedHost)时改为 `http://localhost:5041`**;独立服务调试(网关)保持 `http://localhost:5080` |
| `VITE_AUTH_MODE` | `http` | `http` = 真实登录;`mock` = 无后端即可跑(生产构建禁止 mock) |
| `VITE_REQUEST_TIMEOUT_MS` | `10000` | HTTP 超时毫秒数 |

- **mock 模式**:演示账号 `mock.admin` / `Mock@123456`,无需后端。
- **http 模式**:要先把后端(默认 UnifiedHost,或 Gateway + Identity)跑起来;登录走真实令牌,可配合后端断点联调。
- 改环境变量可在项目根 `.env.local`(gitignore)里写 `VITE_AUTH_MODE=http`、`VITE_API_BASE_URL=http://localhost:5041` 后重启 `pnpm dev`。
- 默认整体调试顺序:① admin 初始化(见 2.4A)→ ② 启动 UnifiedHost(`:5041`)→ ③ `pnpm dev`(`:5173`,API 指向 `:5041`)→ ④ 登录 `admin`。

### 4.5 三端访问与终端模拟

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

自动识别按 §11.1(宽度 `>=1200`→PC、`<768`→Mobile、`768–1199` 触控→PDA)。DevTools 设备模拟宽度小于 768 会识别为 **Mobile**;要调 PDA 必须用覆盖键,或拉宽到 768–1199 + 触控。

### 4.6 Playwright E2E 调试

- 装 **Playwright Test for VSCode**(`ms-playwright.playwright`),可在测试用例旁直接点运行/调试。
- 命令行调试单个用例(先 `pnpm exec playwright install chromium`):

```bash
pnpm exec playwright test --debug                       # 打开 Playwright Inspector 单步
pnpm exec playwright test tests/e2e/login.spec.ts --debug
pnpm exec playwright test --ui                         # 交互式 UI 模式
```

- 两套配置:`playwright.config.ts`(mock 模式)与 `playwright.real.config.ts`(http 真实登录,`workers:1` 串行)。跑真实登录 E2E 前需后端 Gateway+Identity 已启动。

### 4.7 前端常见问题

| 现象 | 处理 |
| --- | --- |
| `.vue` 类型报错但代码没错 | `Vue: Restart Vue Server` 重启语言服务 |
| pnpm 命令找不到 / node 版本错 | 用 `mise exec -- pnpm ...`,或确认 mise PATH 已注入 |
| http 模式登录失败 | 后端 Gateway/Identity 没起,或 `VITE_API_BASE_URL` 不对 |
| 生产构建禁止 mock | `VITE_AUTH_MODE=mock` 下 `pnpm build` 会失败(设计约束) |
| 调试断点不进源码 | 确认 Vite dev 的 sourcemap 开启、`webRoot` 指向 `src/frontend` |

---

## 五、质量门禁命令

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

# 脚本测试(tests/scripts)
powershell -NoProfile -ExecutionPolicy Bypass -File tests\scripts\Initialize-DevelopmentAdmin.Tests.ps1
bash tests/scripts/bootstrap-admin.Tests.sh   # 需要 Git Bash;先 bash -n 校验语法
```

> 2026-08-12 全量基线:后端 build 0 警告 0 错误、test **520/520**;前端 lint/typecheck 0、unit 223/223、build 通过、E2E 35/35。`test:unit:coverage` 当前 **38%(<70%)**,因 TASK-ID-010~015 新增 Identity/SSO 管理页无单测,属已知回归。

---

## 六、已知预期与注意事项

### 6.1 无依赖(SQLite)基线

| 现象 | 说明 |
| --- | --- |
| `/health/ready` 返回 503,耗时约 6~15s | 依赖健康检查 Postgres/Redis/RabbitMQ 均 Unhealthy(连接超时),聚合 503;**预期行为**,`/health` 与 `/health/live` 仍 200 |
| 日志出现 `PostgreSQL 无法连接:SqlSugarException`、`Redis 无法连接`、`RabbitMQ 连接失败:localhost:5672` | 均为依赖缺失,**非程序错误**;Identity 启动时提示「未注册任何集成事件订阅,跳过 RabbitMQ 消费启动」 |
| 控制台中文乱码 | Windows GBK 编码显示问题,不影响运行 |
| `launchSettings` 启动 ReferenceData 弹浏览器 | 正常,`launchBrowser: true` 所致 |

### 6.2 云端连接模式(2026-08-12 实测发现)

| 现象 | 说明 |
| --- | --- |
| **ReferenceData `/health/ready` 恒 Unhealthy** | `RabbitMqHealthCheck` 无条件注册且基础配置指向 `localhost:5672`;即使 `RabbitMq.Enabled=false` 也检查并超时(Identity 则不注册 rabbitmq 检查,不一致)。要让 ready 转绿,在私有配置 `RemoteDevelopment.RabbitMq.Enabled=true`(云端容器在跑,连上即绿)。**待协作方评估是否按 `RabbitMq.Enabled` 条件注册** |
| Gateway `/health/ready` 聚合 `service.referencedata Unhealthy` | 源于下游 ReferenceData 的 rabbitmq 检查,同上 |
| Identity `/health/ready` | ✅ Healthy(postgres/redis Healthy,seq 未启用跳过),无此问题 |
| JWT `SigningKey` 为空 | 每次启动临时 RSA 密钥,重启后既有令牌失效;仅限开发环境,需持久化则配密钥 |

要完整本地联调(不连云端),需安装 Docker 后执行 `docker compose up -d`(见 `deploy/scripts/README.md`),届时 `/health/ready` 应转 200。
