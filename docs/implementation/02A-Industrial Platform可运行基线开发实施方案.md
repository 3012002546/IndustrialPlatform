# Industrial Platform 可运行基线开发 TODO

版本：v1.0
当前阶段：BuildingBlocks 完成后的第一优先级
规格依据：`docs/superpowers/specs/2026-08-09-runnable-baseline-first-development-sequence-design.md`

## 1. 目标与边界

在开发业务功能前建立可重复启动、诊断和验证的本地平台基线：基础依赖可启动，现有后端服务健康检查可访问，统一入口可用，新开发环境有明确启动流程。

本阶段不实现 Identity 登录、ReferenceData 业务功能、任何 MES 领域功能或业务页面。BuildingBlocks 已完成，不得重复搭建。

## 2. 当前输入状态

- BuildingBlocks 及 Security、Web 补充能力已完成。
- Identity、ReferenceData 只有四层服务骨架和 `/health`。
- 前端、Docker、部署仍是占位。
- 当前验证记录：BuildingBlocks 64/64、全解决方案 74/74；执行任务时必须重新验证，不直接复用历史结果。
- 2026-08-09 重新验证通过：SDK 10.0.302，restore/build 0 警告 0 错误，全量 test 74/74 通过（BuildingBlocks 64、Identity 5、ReferenceData 5），`--vulnerable` 审计全干净；`--deprecated` 发现 xunit 2.9.3 被标记 Legacy（见 TASK-BASE-001 执行记录）。

## 3. 全局约束

- .NET 目标框架为 `net10.0`，Nullable 开启，警告视为错误。
- 配置提交安全示例值，不提交数据库密码、RabbitMQ 凭据、Seq API Key 或其他真实密钥。
- PostgreSQL、Redis、RabbitMQ、Seq 必须有健康检查；启动失败必须可由日志和诊断命令定位。
- Docker 数据卷、容器名、端口和网络使用统一前缀，避免污染其他项目。
- Windows 命令必须设置仓库内 `DOTNET_CLI_HOME`；说明同时提供 PowerShell 与跨平台命令差异。
- 状态流转：`可派遣 → 已派遣 → 开发中 → 待验收 → 已完成`；设计冲突改为 `设计待确认`。

## 4. 任务依赖图

```text
BASE-001 → BASE-002 → BASE-003 → BASE-004 → BASE-005 → BASE-006
```

## TASK-BASE-001 固化当前后端构建与测试基线

**状态：** 已完成

**目标：** 以当前提交重新验证解决方案、BuildingBlocks、Identity 和 ReferenceData 骨架，形成可对比的基线报告。

**输入文档：** `CLAUDE.md`、`global.json`、`Directory.Build.props`、`Directory.Packages.props`。

**依赖：** BuildingBlocks 已完成。

**允许修改范围：** 仅测试或构建配置中与可复现基线直接相关的缺陷；不得添加业务功能。结果回写本文件与 `CLAUDE.md` 由代码协作方完成。

**预期输出：** restore、build、全量 test、NuGet deprecated/vulnerable 审计的命令与结果；失败时给出最小复现和归属。

**验证与证据：** 记录 `dotnet restore src/backend/IndustrialPlatform.slnx`、`dotnet build ... --no-restore`、`dotnet test ... --no-build` 的退出码、通过/失败/跳过数量；记录所有项目包审计结果。

**结果回写：** 更新状态、验证时间、SDK 版本、测试总数与发现的基线偏差。

**建议提交：** `chore(platform): verify backend baseline`

## TASK-BASE-002 建立本地基础设施 Docker Compose

**状态：** 待验收（实现完成，本机未安装 Docker，验证命令待具备 Docker 的环境执行）

**目标：** 使用 Docker Compose 启动 PostgreSQL、Redis、RabbitMQ 和 Seq，并为每项依赖定义健康检查和持久化卷。

**输入文档：** 蓝图 07、08、20、30；仓库 `docker/**` 占位目录。

**依赖：** TASK-BASE-001。

**允许修改范围：** `docker/**`、根目录安全示例环境变量文件、相关说明和基础设施验证脚本。

**预期输出：** Compose 文件、网络、命名卷、四个服务、健康检查、安全示例配置和停止/清理说明；不自动删除持久化数据。

**验证与证据：** 运行 `docker compose config`、`docker compose up -d`、`docker compose ps`；分别验证 PostgreSQL、Redis、RabbitMQ 管理端和 Seq 健康状态，记录容器状态与端口。

**结果回写：** 回写镜像版本、端口、卷名、环境变量和诊断命令。

**建议提交：** `feat(platform): add local infrastructure compose`

## TASK-BASE-003 统一后端开发配置与依赖健康检查

**状态：** 待验收（实现完成；配置绑定、缺失配置、依赖中断/强制失败场景测试全绿。真实依赖联调需有 Docker 环境执行 TASK-BASE-002 后验收）

**目标：** 让 Identity、ReferenceData 使用一致的开发配置读取基础依赖，并暴露可区分自身与依赖状态的健康检查。

**输入文档：** TASK-BASE-002 输出、BuildingBlocks Infrastructure/Logging/Web 扩展。

**依赖：** TASK-BASE-002。

**允许修改范围：** Identity、ReferenceData Api 的配置、启动注册、健康检查与对应测试；BuildingBlocks 只允许修复复用扩展缺陷。

**预期输出：** 安全的 `appsettings.Development` 示例、环境变量覆盖、PostgreSQL/Redis/RabbitMQ/Seq 配置绑定、liveness/readiness 端点和 TraceId 日志。

**验证与证据：** 提供配置绑定测试、缺失配置失败信息、liveness/readiness 在依赖正常与中断时的 API 测试；证明日志未输出凭据。

**结果回写：** 回写配置节名称、健康端点、依赖超时和敏感字段屏蔽规则。

**建议提交：** `feat(platform): add dependency health checks`

### 实施记录（TASK-BASE-003）

**交付物：**
- 配置节（`appsettings.Development.json`）：`Serilog`（ServiceName/Console/File/Seq）、`SqlSugar`（ConnectionString/DbType）、`Redis`（ConnectionString）、`RabbitMQ`（Host/Port/UserName/Password/VirtualHost，仅 ReferenceData）。真实连接凭据一律环境变量覆盖，样例值 `sample-dev-password` 仅限本地开发。
- 健康端点：`/health`（进程存活，静态 200）、`/health/live`（liveness，不含依赖检查，`Predicate = _ => false`）、`/health/ready`（readiness，聚合全部依赖检查）。
- 依赖检查与超时：Postgres（`SELECT 1`）、Redis（`PING`）、Seq（`GET /api`，未启用视为健康、故障降级）、RabbitMQ（建连+建通道，仅 ReferenceData），每个检查超时 3s（.NET 10 端点级 `HealthCheckOptions` 无 `Timeout` 属性，改为 per-check `AddCheck(..., timeout:)`）。
- 响应写出器只输出 `status` + `checks[{name,status,description}]`，**不含异常详情/连接串/凭据**；测试断言响应体不包含 `sample-dev-password`。
- 敏感字段屏蔽规则：健康检查响应禁止回显依赖错误消息原文（统一 `不可访问:{异常类型名}`），配置凭据样例仅存于 `appsettings.Development.json`。

**关键修复（BuildingBlocks 复用扩展缺陷，任务允许范围）：** `AddRedis` 注册的 `IConnectionMultiplexer` 默认 `abortConnect=true`，Redis 不可达时首次解析即在健康检查工厂内抛 `RedisConnectionException`，而 .NET 10 `DefaultHealthCheckService` 的实例缓存解析（`ConcurrentDictionary.GetOrAdd`）不被按检查捕获，异常逃逸成 500。已改为 `AbortOnConnectFail=false`：连接失败返回断开的复用器并后台重试，错误留到 `PingAsync` 被检查捕获 → Unhealthy/503。

**验证证据（2026-08-10）：** Identity 12/12、ReferenceData 13/13 通过（含配置绑定、缺失连接串 `ArgumentException`「未配置 SqlSugar 连接字符串」、/health/live 200、/health/ready 检查名齐全且无凭据泄漏、强制失败 503）；全解决方案 build 0 警告 0 错误、test 127/127 通过（BB 102、Identity 12、RefData 13）。

**测试方法命名说明：** 服务测试项目缺失局部 `.editorconfig`，CA1707 按错误生效；已为 Identity/ReferenceData 测试项目补齐（与 BuildingBlocks 一致，关闭 CA1707 允许下划线命名）。

## TASK-BASE-004 建立开发期统一 API 入口

**状态：** 待验收（实现完成；构建、路由转发、服务不可用、超时、健康聚合与 CORS 测试全绿。真实联调待有 Docker 环境执行 TASK-BASE-002 后验收）

**目标：** 建立最小 ApiGateway 或统一开发入口，转发 Identity、ReferenceData 健康端点并提供平台健康聚合。

**输入文档：** 蓝图 01、06、20、27；TASK-BASE-003 健康端点。

**依赖：** TASK-BASE-003。

**允许修改范围：** `src/backend/src/Gateway/**`、解决方案注册、Gateway 测试与本地配置。

**预期输出：** 可独立启动的 Gateway、服务路由、平台 health/readiness 聚合、统一错误返回和开发期 CORS；不实现认证业务。

**验证与证据：** 提供 Gateway 构建、路由转发、服务不可用、超时、健康聚合和 CORS 测试；记录统一入口 URL。

**结果回写：** 回写路由前缀、端口、健康聚合结构与错误码。

**建议提交：** `feat(gateway): add local service entry point`

### 实施记录（TASK-BASE-004）

**交付物：**
- `src/backend/src/Gateway/IndustrialPlatform.Gateway/`：YARP 2.3.0 反向代理（仅引用 BuildingBlocks Logging/Web，不含业务）。
- 统一入口 URL：`http://localhost:5080`（launchSettings http profile）。
- 路由：`/identity/**` → `http://localhost:5041/**`、`/referencedata/**` → `http://localhost:62311/**`，转发时 `PathRemovePrefix` 剥离前缀；配置节 `Gateway:Services`（Name/PathPrefix/DestinationUrl），`Gateway:RequestTimeoutSeconds` 默认 10s。
- 平台健康聚合：`/health`（静态 `{status=Healthy, service=Gateway}`）、`/health/live`（不查下游）、`/health/ready`（逐下游 GET `/health/ready`，任一 Unhealthy 整体 503；检查超时 10s 匹配下游依赖全挂 ~6s 的最坏就绪耗时；响应仅 `status+checks`，不含凭据/异常原文）。
- 统一错误：代理错误经 `IForwarderErrorFeature` 中间件输出 `ApiResult` 信封——连接失败/目标不可用 503「下游服务不可用」、转发超时/取消 504「网关转发请求超时」；未匹配路由 fallback 404「路由不存在」。
- 开发期 CORS：`Gateway:Cors:AllowedOrigins` 默认 `http://localhost:5173`（Vue3 dev）、`http://localhost:4173`（preview），任意方法/头，预检在网关短路处理。

**关键技术决策：**
- YARP 2.3.0 无 `IProxyErrorHandler` 扩展点，统一错误通过 `IForwarderErrorFeature` 中间件改写响应实现。
- 前缀剥离用 `PathRemovePrefix` transform（`PathPrefix` 为追加语义，曾导致 `/identity/identity/health` 双前缀）。
- `WebApplicationFactory` 的 `ConfigureAppConfiguration` 注入晚于 Program 启动读取，对 minimal API 无效；Gateway 测试用 `UseSetting("Gateway:…")` 在 CreateBuilder 前覆盖。
- .NET 10 已移除 `AddCheck(name, Func<IServiceProvider,IHealthCheck>)` 重载，参数化健康检查改用 `AddTypeActivatedCheck<T>(name, failureStatus, tags, timeout, args)`。
- 下游就绪检查超时 3s 会临界截断下游 503 响应（依赖全挂时下游约 6s 才返回），放宽到 10s 以获得明确的就绪判定。

**验证证据（2026-08-10）：** 全量 build 0 警告 0 错误；test 140/140（BB 102、Identity 12、RefData 13、Gateway 13）。Gateway 测试覆盖：配置绑定、路由转发+前缀剥离（桩服务回显 receivedPath）、下游不可达 503 信封、超时 504 信封、未匹配 404 信封、CORS 简单/预检、`/health/ready` 聚合与全挂 503、响应不含 `sample-dev-password`。`--vulnerable`/`--deprecated` 审计 Gateway 干净。

## TASK-BASE-005 建立一键启动与停止流程

**状态：** 待验收（实现完成；启动、状态、重复启动、端口冲突与停止流程实测通过，退出码符合预期。基础设施联调待有 Docker 环境执行 TASK-BASE-002 后验收）

**目标：** 提供可审计的开发启动流程，按依赖顺序启动基础设施、后端服务和 Gateway，并安全停止进程。

**输入文档：** TASK-BASE-002 至 TASK-BASE-004 的命令和端口。

**依赖：** TASK-BASE-004。

**允许修改范围：** `deploy/scripts/**` 或明确的开发脚本目录、README 与启动验证测试；不得保存机器专属绝对路径。

**预期输出：** PowerShell 启动/停止/状态脚本，跨平台手工等价命令，端口冲突检查，进程标识和失败清理。

**验证与证据：** 在干净终端运行启动、状态、重复启动、部分依赖失败和停止流程；记录每步退出码，证明停止不删除 Docker 数据卷。

**结果回写：** 回写命令入口、进程管理方式、端口冲突规则和已知平台差异。

**建议提交：** `feat(platform): add local startup workflow`

### 实施记录（TASK-BASE-005）

**交付物：**
- 一键脚本 `deploy/scripts/dev.ps1`：`start` / `stop` / `status` 三个子命令，`#requires 5.1`，输出英文规避 Windows PowerShell 5.1 无 BOM UTF-8 乱码。
- 使用说明 `deploy/scripts/README.md`（命令入口、端口表、进程管理、端口冲突规则、跨平台手工等价 bash 命令、已知平台差异）。
- 根 `.gitignore` 新增 `.run/`（PID 文件与服务日志）。

**命令入口与进程管理：**
- `./deploy/scripts/dev.ps1 start`：基础设施（`docker compose config --quiet` + `up -d`，缺失 docker/校验失败/启动失败均警告降级不中止）→ 构建（`dotnet build`，自动设 `DOTNET_CLI_HOME`，失败中止）→ 端口预检 → 后台启动 Gateway(5080)/Identity(5041)/ReferenceData(62311) → 轮询各 `/health`（30s 超时）→ 输出访问 URL 表。常用开关：`-SkipInfrastructure`（无 Docker）、`-SkipBuild`。
- PID 文件 `.run/<name>.pid`；日志 `.run/<name>.stdout.log` / `.stderr.log`（Serilog 走 stdout）。服务以 `dotnet <dll>` + `ASPNETCORE_URLS` 钉端口 + `ASPNETCORE_ENVIRONMENT=Development` 启动，工作目录为项目目录，不依赖 launchSettings。
- `stop`：按 PID 文件结束进程并清除 PID 文件；`-StopInfrastructure` 才执行 `docker compose stop`；明确提示「Docker 数据卷未删除，仅显式 `docker compose down -v` 才删除」。
- `status`：逐服务读 PID → 进程存活 → HTTP `/health` 探测；全部健康退出码 0，否则 1。docker 可用时附 `docker compose ps`。
- **端口冲突规则**：未被占用→启动；被本脚本 PID 占用→「already running」跳过（幂等）；被其它进程占用→硬冲突，中止整个 start（退出码 1）并打印占用者 PID/进程名，不启动任何服务。

**验证证据（2026-08-10，本机无 Docker，基础设施用 `-SkipInfrastructure` 实测）：**
1. `start`：构建 0 警告 0 错误；三服务启动、`/health` 200；退出码 0。
2. `status`：三服务 RUNNING + 200 OK；退出码 0。
3. 重复 `start`：识别 already running 跳过，退出码 0。
4. 端口冲突：占用 5041 后 `start` → 报「Identity port 5041 is occupied by PID…」退出码 1，无任何 PID 文件/服务启动。
5. 无 docker 默认 `start`：警告「docker CLI not found; skipping infrastructure」降级继续，退出码 0。
6. `stop`：三进程结束、PID 文件清除、端口释放、提示数据卷未删除；退出码 0。
7. 集成冒烟：经网关 `GET /identity/health` 200；`/health/ready` 503（依赖未起，符合预期）。
8. 全量 build 0 警告 0 错误、test 140/140 保持（脚本不参与 .NET 构建）。

**回写（命令入口、进程管理、端口冲突规则、平台差异）：** 见 `deploy/scripts/README.md` 与上述实施记录。

## TASK-BASE-006 完成新环境冒烟验收

**状态：** 已完成（Docker 基础设施联调与容器状态按用户要求留后续验证，见「实施记录（TASK-BASE-006）」边界说明）

**目标：** 从新环境视角验证 clone 后能够启动依赖、后端和统一入口，并形成下一阶段前端可消费的运行契约。

**输入文档：** TASK-BASE-001 至 TASK-BASE-005 全部输出。

**依赖：** TASK-BASE-005。

**允许修改范围：** 根 README、开发启动说明、冒烟测试和本文件执行记录；只修复验收阻塞问题。

**预期输出：** 最短启动路径、前置软件版本、端口表、健康检查表、常见故障诊断和前端 API Base URL。

**验证与证据：** 重新执行基础设施启动、全解决方案构建测试、Gateway/Identity/ReferenceData 健康检查；记录总耗时、退出码、测试数量和容器状态。

**结果回写：** 将基线状态改为已完成，登记验证提交和 Phase 2 前端输入契约。

**建议提交：** `docs(platform): publish runnable baseline guide`

### 实施记录（TASK-BASE-006）

**交付物：**
- 根 `README.md` 重写为可运行基线指南：前置软件版本、最短启动路径、端口表、健康检查表、常见故障诊断、前端 API Base URL 与响应契约。
- 冒烟测试 `deploy/scripts/smoke.ps1`：新环境视角端到端验收——构建 → 全量测试 → 启动服务（`dev.ps1 start -SkipInfrastructure -SkipBuild`）→ 端点探测 → 容器状态 → 停止，输出分步退出码、测试数量与总耗时。
- 02A 本文档执行记录与完成标准核对。

**验证证据（2026-08-10，本机无 Docker，`-SkipInfrastructure`）：**
- 全流程 `smoke.ps1` 一次通过（Overall PASS），**总耗时 52.9s**。
- 构建：exit 0，0 警告 / 0 错误。测试：exit 0，**140/140** 通过、0 失败。
- 启动：`dev.ps1 start -SkipInfrastructure -SkipBuild` exit 0。
- 探测全 PASS：Gateway/Identity/ReferenceData `/health` 200、`/health/live` 200、经网关 `/identity/health` 200 且响应体 `service=Identity`（前缀剥离生效）、`/unknown` 404 统一信封。
- `/health/ready` 503（无 Docker 依赖未起，符合预期）；`/health/ready` 聚合耗时约 10s（下游检查并行超时），探测超时放宽到 20s。
- 容器状态：**N/A**（Docker 不可用，留待 TASK-BASE-002 环境验收）。
- 停止：exit 0，数据卷提示不删除。

**前端 Phase 2 输入契约（已登记至根 README「前端 API 契约」）：**
- API Base URL `http://localhost:5080`（Gateway 统一入口，前端不直连服务）。
- 路由前缀 `/identity/**`、`/referencedata/**`（网关剥离前缀）。
- CORS 允许 `http://localhost:5173` / `http://localhost:4173`，预检网关短路。
- 响应信封 `ApiResult{success,code,message,data}`；错误码 400/401/404/500/503/504；健康端点 `/health` `/health/live` `/health/ready`。

**边界说明（按用户指示）：** Docker 基础设施（TASK-BASE-002）不参与本次验收，其真实依赖联调、容器状态与最终容器化验收留后续环境进行；完成标准的 Docker 相关条目相应保留为待验收。

## 5. 完成标准

- 新开发环境可按文档启动 PostgreSQL、Redis、RabbitMQ、Seq、Identity、ReferenceData 和统一入口。
- 所有依赖有健康状态，错误能通过 TraceId 与日志定位。
- restore、build、test 和包审计有最新证据。
- 已明确前端使用的 API Base URL、CORS 与健康端点。
- 未提前实现 Identity 业务功能或 MES 业务领域。

**完成标准核对（TASK-BASE-006，2026-08-10）：** 新环境可按根 README 最短路径启动后端与统一入口 ✅（基础设施依赖按用户指示留 Docker 环境验收）；依赖健康状态可通过 `/health/ready` 聚合与 TraceId 日志定位 ✅（容器化依赖状态留后续）；restore/build/test/包审计最新证据：build 0/0、test 140/140、`--vulnerable`/`--deprecated` 干净（Gateway 项目审计）✅；前端 API Base URL/CORS/健康端点已登记 ✅；未实现 Identity 业务或 MES 领域 ✅。

## 6. 执行记录

| 任务 | 状态 | 执行者/任务 | 提交 | 验证证据 | 结果回写 |
| --- | --- | --- | --- | --- | --- |
| TASK-BASE-001 | 已完成 | 本任务 | - | 2026-08-09 SDK 10.0.302：restore/build 0 警告 0 错误；test 74/74 通过（BB 64、Identity 5、RefData 5）；`--vulnerable` 全干净；`--deprecated` 仅 xunit 2.9.3 Legacy（替代 xunit.v3，待迁移） | 状态见上文；偏差记录见 CLAUDE.md |
| TASK-BASE-002 | 待验收 | 本任务 | - | 交付 `docker/docker-compose.yml` + `.env.example` + README（镜像 postgres:18-alpine / redis:7.4-alpine / rabbitmq:4-management / datalust/seq:2025,统一前缀 industrial-platform,四服务健康检查 + 命名卷 + 桥接网络）。本机未安装 Docker,WSL 无发行版,`docker compose config/up/ps` 未执行,待有 Docker 环境验收 | 状态见上文;交付物与验证命令见 `docker/README.md` |
| TASK-BASE-003 | 待验收 | 本任务 | - | 2026-08-10 全量 build 0 警告 0 错误、test 127/127（BB 102、Identity 12、RefData 13）；配置绑定/缺失配置/health 端点/强制失败 503 测试全绿；本机无 Docker 未做真实依赖联调，待有环境验收 | 状态见上文；配置节、健康端点、超时与敏感字段屏蔽规则见「实施记录（TASK-BASE-003）」 |
| TASK-BASE-004 | 待验收 | 本任务 | - | 2026-08-10 全量 build 0 警告 0 错误、test 140/140（Gateway 13：配置绑定/路由转发/503/504/404 信封/CORS/健康聚合）；真实联调待有 Docker 环境验收 | 状态见上文；端口、路由前缀、健康聚合结构与错误码见「实施记录（TASK-BASE-004）」 |
| TASK-BASE-005 | 待验收 | 本任务 | - | 2026-08-10 无 Docker 环境实测通过：start/status/重复 start/端口冲突/stop 退出码符合预期（0/1），网关转发与健康聚合正常；基础设施联调待有 Docker 环境 | 状态见上文；命令入口、进程管理、端口冲突规则与平台差异见「实施记录（TASK-BASE-005）」与 `deploy/scripts/README.md` |
| TASK-BASE-006 | 已完成 | 本任务 | - | 2026-08-10 冒烟全流程 PASS：总耗时 52.9s、构建 0/0、test 140/140、三服务/网关转发/404 信封探测全过、`/health/ready` 503（依赖未起）、停止 exit 0；容器状态 N/A（Docker 留后续） | 状态见上文；前端 API 契约（Base URL 5080、前缀、CORS、信封、错误码）已登记至根 README「前端 API 契约」；交付物见「实施记录（TASK-BASE-006）」 |
