# Industrial Platform

.NET 10 工业/MES 平台，采用 Clean Architecture，并按业务复杂度选择 DDD。当前 BuildingBlocks、统一前端、Identity 当前范围和 SystemData `TASK-SD-001～006` 已完成；ReferenceData 仍为服务骨架。

当前支持两种严格区分的部署入口：

- **统一部署（默认开发路径）**：Browser → `UnifiedHost`（`:5041`）→ 进程内 Identity、SystemData、ReferenceData 模块。UnifiedHost 组合模块、统一中间件、协调模块自己的启动迁移并托管生产 SPA；不运行 YARP，也不代理下游。
- **分布式部署（边界验证与未来部署路径）**：Browser → `Gateway`（`:5080`）→ 独立 API Host。Gateway 只负责 YARP 路由、服务前缀、CORS、下游健康聚合和代理错误；不加载业务模块、不托管前端、不执行迁移。

## 前置软件版本

| 软件 | 版本 | 用途 |
| --- | --- | --- |
| .NET SDK | 10.0.302 | 构建与运行后端 |
| PowerShell | 5.1 及以上(Windows) | 一键脚本(另提供跨平台手工命令) |
| Docker Desktop / Engine | 任意近期版本 | 本地基础设施(PostgreSQL/Redis/RabbitMQ/Seq);**无 Docker 可跳过,依赖降级为 Unhealthy** |
| 前端 | Node.js 18+、pnpm | 统一前端（Vue 3）开发、测试与构建 |

> 校验:`dotnet --version`、`powershell -NoProfile -Command '$PSVersionTable.PSVersion'`、`docker compose version`。

## 最短启动路径

```powershell
# 1. 设置本地 CLI home(Windows 必需,否则 NuGet 恢复异常)
$env:DOTNET_CLI_HOME = (Join-Path (git rev-parse --show-toplevel) '.dotnet_cli_home')

# 2. 构建
dotnet build src/backend/IndustrialPlatform.slnx

# 3. 启动（默认 UnifiedHost；按需切换分布式入口）
./deploy/scripts/dev.ps1 start          # 有 Docker
./deploy/scripts/dev.ps1 start -SkipInfrastructure   # 无 Docker
./deploy/scripts/dev.ps1 start -IndependentServices  # Gateway + 独立 API Host

# 4. 验证
./deploy/scripts/dev.ps1 status
Invoke-RestMethod http://localhost:5041/health/ready  # 默认 UnifiedHost
```

一键脚本详解见 [`deploy/scripts/README.md`](deploy/scripts/README.md)。

## 端口表

| 组件 | 端口 | 说明 |
| --- | --- | --- |
| **UnifiedHost** | **5041** | **默认统一进程入口，组合当前三个模块并托管生产 SPA** |
| **Gateway** | **5080** | **分布式入口，YARP 转发到独立 API Host** |
| Identity | 5041 | 独立 API Host 模式；与 UnifiedHost 端口相同，不同时启动 |
| ReferenceData | 62311 | 直接访问(绕过网关) |
| PostgreSQL | 5432 | Docker Compose |
| Redis | 6379 | Docker Compose |
| RabbitMQ | 5672 / 15672 | AMQP / 管理台 |
| Seq | 5341 | 日志 Web/API |

## 健康检查表

| 端点 | 语义 | 成功 |
| --- | --- | --- |
| `GET /health` | 进程存活(静态) | 200 `{status,service}` |
| `GET /health/live` | liveness,不含依赖检查 | 200 |
| `GET /health/ready` | readiness；服务/模块按自己的数据库事实判断，入口再聚合 | 200（全绿）/ 503（任一失败） |
| 下游逐项 | 仅 Gateway 经 `/health/ready` 聚合独立 API Host（响应不含凭据） | 各下游本地 readiness |

- 每个服务自检 Postgres/Redis/Seq(ReferenceData 另含 RabbitMQ),超时 3s;响应仅 `status + checks`。
- Gateway 对每个独立 API Host 的 `GET /health/ready` 聚合；UnifiedHost 聚合进程内模块的本地 readiness。已初始化服务的日常 readiness 不依赖 SystemData 在线。
- 无 Docker 时依赖检查报 Unhealthy 属**预期**,`/health`、`/health/live` 不受影响。

## 常见故障诊断

| 现象 | 排查 |
| --- | --- |
| 启动报端口冲突 | `dev.ps1 start` 已预检并打印占用者 PID/进程名;`netstat -ano \| findstr :<port>` 复核 |
| 服务启动后秒退 | 看 `.run/<name>.stderr.log` |
| `/health/ready` 503 | 看具体 `checks[].description`(如 `PostgreSQL 不可访问`);先 `docker compose ps` 确认依赖 healthy |
| 依赖连不上但已起容器 | `.env` 凭据与 `appsettings.Development.json` 不一致;改凭据后需 `docker compose down` 重建 |
| 构建 NuGet 恢复异常 | Windows 未设 `DOTNET_CLI_HOME`(见最短启动路径第 1 步) |
| 日志无 TraceId | 确认 Seq 可达且 `Serilog:Seq:Enabled=true`;经网关请求头不传 TraceId 时由网关生成 |
| 无 Docker 环境 | 全程 `-SkipInfrastructure`,依赖降级 Unhealthy,后端与网关正常 |

## 前端 API 入口

默认开发 API Base URL 是 `http://localhost:5041`（UnifiedHost）；验证分布式拓扑时改为 `http://localhost:5080`（Gateway）。两种入口保持相同外部服务前缀，前端不直连业务服务内部端口。

- **路由前缀**：`/identity/**`、`/systemdata/**`、`/referencedata/**`。Gateway 转发时剥离服务前缀；UnifiedHost 直接映射内置模块并保持相同外部路径。
- **CORS**:允许 `http://localhost:5173`(Vue3 dev)、`http://localhost:4173`(preview),任意方法/头;预检在网关短路处理。
- **统一响应信封**(`ApiResult`):

```json
{ "success": true, "code": "200", "message": "success", "data": null }
```

- **错误码**:`400`(业务/校验)、`401`(未授权)、`404`(资源或路由不存在)、`500`(未预期)、`503`(下游服务不可用)、`504`(网关转发超时);业务码 `模块_编号`(如 `WO_001`)后续阶段引入。
- **健康端点**:`/health`、`/health/live`、`/health/ready`。
- Identity 登录、令牌、权限和管理当前范围已实现；ReferenceData 业务接口仍待 PF-03，限流、灰度和生产 HTTPS 由后续部署范围处理。

## 当前里程碑范围

已交付：BuildingBlocks 组件、统一前端与 PF-01 平台外壳、Identity 当前范围、SystemData `TASK-SD-001～006`、ReferenceData 服务骨架、Gateway 分布式入口、UnifiedHost 统一入口、本地基础设施编排与启动脚本。

后续：先完成架构收敛整改工作包 2～4，再从 PF-03 ReferenceData 继续；MasterData、OperationalData 及后续 MES 服务不在当前范围。

## 文档

- [架构蓝图](docs/blueprint/README.md)
- [实施文档索引](docs/implementation/README.md)
- [当前状态](docs/status/CURRENT.md)
- [架构收敛整改设计](docs/superpowers/specs/2026-08-20-industrial-platform-architecture-consolidation-design.md)
- [可运行基线开发 TODO](docs/implementation/02A-Industrial%20Platform可运行基线开发实施方案.md)
- [统一前端开发 TODO](docs/implementation/02B-Industrial%20Platform统一前端第一批开发实施方案.md)
- [本地基础设施 Compose 说明](docker/README.md)
- [一键脚本与冒烟说明](deploy/scripts/README.md)
- [本地调试指南(后端 VS2026 / 前端 VS Code,无 Docker 环境)](src/DEBUGGING.md)
