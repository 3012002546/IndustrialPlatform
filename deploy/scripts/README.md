# 本地开发一键脚本

TASK-BASE-005 交付:按依赖顺序启动基础设施、后端服务与 Gateway,并提供状态查询与安全停止。

## 命令入口

```powershell
./deploy/scripts/dev.ps1 start    # 启动:基础设施 → 构建 → 服务
./deploy/scripts/dev.ps1 status   # 状态:进程 + 健康探测
./deploy/scripts/dev.ps1 stop     # 停止:结束服务进程,不删除 Docker 数据卷
```

常用变体:

```powershell
./deploy/scripts/dev.ps1 start -SkipInfrastructure   # 无 Docker 环境:跳过基础设施,仅启动后端
./deploy/scripts/dev.ps1 start -SkipBuild            # 跳过构建(已构建过时加速)
./deploy/scripts/dev.ps1 stop -StopInfrastructure    # 停止后端的同时 docker compose stop(不删卷)
```

退出码:`start`/`stop` 成功为 0;端口冲突、构建失败、服务启动失败为非 0;`status` 全部健康为 0,存在未运行/不健康为 1。

## 冒烟测试

```powershell
./deploy/scripts/smoke.ps1              # 构建 → 全量测试 → 启动 UnifiedHost → 探测 → 停止,输出分步退出码/测试数/耗时
./deploy/scripts/smoke.ps1 -KeepRunning # 探测后不停止服务
```

- 默认探测只访问 UnifiedHost `http://localhost:5041`:校验 `/health`、`/health/live`、未知 API 404 及响应体 `service=UnifiedHost`;`/health/ready` 为信息项(200/503)。
- 无 Docker 时容器状态记 `N/A`(基础设施验收留 Docker 环境),不阻塞整体 PASS。
- 任何一步失败仍继续收集全部证据,最终退出码非 0。

## 端口表

| 组件 | 端口 | 说明 |
| --- | --- | --- |
| Gateway | 5080 | 统一入口,`/identity/**`、`/referencedata/**` 转发 |
| Identity | 5041 | 直接访问(绕过网关) |
| ReferenceData | 62311 | 直接访问(绕过网关) |
| PostgreSQL | 5432 | Docker Compose |
| Redis | 6379 | Docker Compose |
| RabbitMQ | 5672 / 15672 | AMQP / 管理台 |
| Seq | 5341 | 日志 Web/API |

默认 UnifiedHost 健康入口:`http://localhost:5041/health`(存活)、`/health/live`、`/health/ready`(模块聚合)。

## 进程管理方式

- **PID 文件**:每个服务写入 `<仓库根>/.run/<name>.pid`,`stop`/`status` 据此识别由脚本启动的进程;`.run/` 已在 `.gitignore`。
- **日志**:每个服务输出到 `.run/<name>.stdout.log` 与 `.run/<name>.stderr.log`(Serilog 走 stdout),用于审计与排障。
- **端口钉死**:服务以 `ASPNETCORE_URLS=http://localhost:<port>` + `ASPNETCORE_ENVIRONMENT=Development` 后台启动(`dotnet <dll>`,工作目录为项目目录),不依赖 launchSettings。
- **失败清理**:`start` 先做全量端口预检,任一硬冲突即中止且不启动任何服务;单个服务启动秒退立即报错,日志指向 stderr 文件。
- **幂等**:重复 `start` 检测到端口由本脚本 PID 持有时报「already running」并跳过,不重复拉起。

## 端口冲突规则

1. 端口未被占用 → 正常启动。
2. 端口被本脚本已管理的 PID 占用 → 视为「已在运行」,跳过(幂等)。
3. 端口被其它进程占用 → 硬冲突,中止整个 `start`(退出码 1),打印占用者 PID 与进程名。

`start` 的容器端口(5432 等)由 Docker 负责,脚本不检查。

## 跨平台手工等价命令

未安装 PowerShell 的环境(如 Linux/macOS 终端)可按序手工执行:

```bash
# 1. 基础设施(Docker)
cd docker && cp .env.example .env   # 首次
docker compose config               # 校验
docker compose up -d

# 2. 构建
dotnet build src/backend/IndustrialPlatform.slnx

# 3. 启动服务(各开一个终端;Ctrl+C 停止)
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5080 \
  dotnet run --project src/backend/src/Gateway/IndustrialPlatform.Gateway/IndustrialPlatform.Gateway.csproj --no-launch-profile
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5041 \
  dotnet run --project src/backend/src/Services/Identity/IndustrialPlatform.Identity.Api/IndustrialPlatform.Identity.Api.csproj --no-launch-profile
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:62311 \
  dotnet run --project src/backend/src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Api/IndustrialPlatform.ReferenceData.Api.csproj --no-launch-profile

# 4. 状态
curl -fsS http://localhost:5080/health
curl -fsS http://localhost:5080/health/ready

# 5. 停止基础设施(不删卷)
docker compose stop        # 删容器/网络但保留数据:docker compose down
# 数据卷仅显式删除:docker compose down -v(不可恢复)
```

## 已知平台差异

- **PowerShell 版本**:脚本 `#requires 5.1`,输出为英文以规避 Windows PowerShell 5.1 对无 BOM UTF-8 的乱码。PowerShell 7 同样可用。
- **Windows**:必须本地 CLI home 才能稳定 NuGet 恢复,脚本在构建步骤自动设置 `DOTNET_CLI_HOME=<仓库根>/.dotnet_cli_home`(见根 CLAUDE.md)。
- **无 Docker 环境**:`start` 检测不到 `docker` 会警告「docker CLI not found」并跳过基础设施,后端仍启动(`/health/ready` 报 Unhealthy 属预期);建议显式 `-SkipInfrastructure`。
- **基础设施故障降级**:`docker compose config` 或 `up -d` 失败仅警告不中止,后端健康检查会反映真实依赖状态。
- 脚本不检查 Docker 端口占用(5432 等);Docker 启动冲突由 `docker compose up -d` 自身报错并警告。
