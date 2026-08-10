# Industrial Platform

.NET 10 微服务平台(工业/MES 领域),Clean Architecture + DDD。当前处于**可运行基线**里程碑:基础组件与统一入口可用,业务功能未实现。

## 前置软件版本

| 软件 | 版本 | 用途 |
| --- | --- | --- |
| .NET SDK | 10.0.302 | 构建与运行后端 |
| PowerShell | 5.1 及以上(Windows) | 一键脚本(另提供跨平台手工命令) |
| Docker Desktop / Engine | 任意近期版本 | 本地基础设施(PostgreSQL/Redis/RabbitMQ/Seq);**无 Docker 可跳过,依赖降级为 Unhealthy** |
| 前端(Phase 2) | Node.js 18+ | 统一前端(Vue3),当前为空目录 |

> 校验:`dotnet --version`、`powershell -NoProfile -Command '$PSVersionTable.PSVersion'`、`docker compose version`。

## 最短启动路径

```powershell
# 1. 设置本地 CLI home(Windows 必需,否则 NuGet 恢复异常)
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet_cli_home')

# 2. 构建
dotnet build src/backend/IndustrialPlatform.slnx

# 3. 启动(基础设施 + 后端 + 统一入口,按依赖顺序)
./deploy/scripts/dev.ps1 start          # 有 Docker
./deploy/scripts/dev.ps1 start -SkipInfrastructure   # 无 Docker

# 4. 验证
./deploy/scripts/dev.ps1 status
Invoke-RestMethod http://localhost:5080/health/ready
```

一键脚本详解见 [`deploy/scripts/README.md`](deploy/scripts/README.md)。

## 端口表

| 组件 | 端口 | 说明 |
| --- | --- | --- |
| **Gateway** | **5080** | **统一入口,前端只走这里** |
| Identity | 5041 | 直接访问(绕过网关) |
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
| `GET /health/ready` | readiness,聚合全部依赖/下游 | 200(全绿)/ 503(任一失败) |
| 下游逐项 | 经网关 `/health/ready` 聚合(响应不含凭据) | Postgres/Redis/RabbitMQ/Seq |

- 每个服务自检 Postgres/Redis/Seq(ReferenceData 另含 RabbitMQ),超时 3s;响应仅 `status + checks`。
- Gateway 对每个下游 GET `/health/ready` 聚合,任一 Unhealthy 整体 503。
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

## 前端 API 契约(Phase 2 输入)

**API Base URL:`http://localhost:5080`**(Gateway 统一入口;前端不直连各服务)。

- **路由前缀**:`/identity/**`、`/referencedata/**`(网关转发时剥离前缀,下游收到原相对路径)。
- **CORS**:允许 `http://localhost:5173`(Vue3 dev)、`http://localhost:4173`(preview),任意方法/头;预检在网关短路处理。
- **统一响应信封**(`ApiResult`):

```json
{ "success": true, "code": "200", "message": "success", "data": null }
```

- **错误码**:`400`(业务/校验)、`401`(未授权)、`404`(资源或路由不存在)、`500`(未预期)、`503`(下游服务不可用)、`504`(网关转发超时);业务码 `模块_编号`(如 `WO_001`)后续阶段引入。
- **健康端点**:`/health`、`/health/live`、`/health/ready`。
- 未实现:认证(JWT)、限流、灰度、HTTPS;Identity 登录与 ReferenceData 业务接口后续阶段交付。

## 当前里程碑范围

已交付(可运行基线):BuildingBlocks 组件、Identity/ReferenceData 服务骨架、Gateway 统一入口、本地基础设施编排、一键脚本与冒烟测试。

未实现(后续):登录/JWT/权限、ReferenceData 业务功能、MasterData/OperationalData、统一前端、Docker 部署、生产配置。

## 文档

- [架构蓝图](docs/blueprint/README.md)
- [实施文档索引](docs/implementation/README.md)
- [可运行基线开发 TODO](docs/implementation/02A-Industrial%20Platform可运行基线开发实施方案.md)
- [统一前端开发 TODO](docs/implementation/02B-Industrial%20Platform统一前端第一批开发实施方案.md)
- [本地基础设施 Compose 说明](docker/README.md)
- [一键脚本与冒烟说明](deploy/scripts/README.md)
