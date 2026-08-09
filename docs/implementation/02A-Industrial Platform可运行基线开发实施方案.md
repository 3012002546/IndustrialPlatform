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

**状态：** 可派遣

**目标：** 以当前提交重新验证解决方案、BuildingBlocks、Identity 和 ReferenceData 骨架，形成可对比的基线报告。

**输入文档：** `CLAUDE.md`、`global.json`、`Directory.Build.props`、`Directory.Packages.props`。

**依赖：** BuildingBlocks 已完成。

**允许修改范围：** 仅测试或构建配置中与可复现基线直接相关的缺陷；不得添加业务功能。结果回写本文件与 `CLAUDE.md` 由代码协作方完成。

**预期输出：** restore、build、全量 test、NuGet deprecated/vulnerable 审计的命令与结果；失败时给出最小复现和归属。

**验证与证据：** 记录 `dotnet restore src/backend/IndustrialPlatform.slnx`、`dotnet build ... --no-restore`、`dotnet test ... --no-build` 的退出码、通过/失败/跳过数量；记录所有项目包审计结果。

**结果回写：** 更新状态、验证时间、SDK 版本、测试总数与发现的基线偏差。

**建议提交：** `chore(platform): verify backend baseline`

## TASK-BASE-002 建立本地基础设施 Docker Compose

**状态：** 可派遣

**目标：** 使用 Docker Compose 启动 PostgreSQL、Redis、RabbitMQ 和 Seq，并为每项依赖定义健康检查和持久化卷。

**输入文档：** 蓝图 07、08、20、30；仓库 `docker/**` 占位目录。

**依赖：** TASK-BASE-001。

**允许修改范围：** `docker/**`、根目录安全示例环境变量文件、相关说明和基础设施验证脚本。

**预期输出：** Compose 文件、网络、命名卷、四个服务、健康检查、安全示例配置和停止/清理说明；不自动删除持久化数据。

**验证与证据：** 运行 `docker compose config`、`docker compose up -d`、`docker compose ps`；分别验证 PostgreSQL、Redis、RabbitMQ 管理端和 Seq 健康状态，记录容器状态与端口。

**结果回写：** 回写镜像版本、端口、卷名、环境变量和诊断命令。

**建议提交：** `feat(platform): add local infrastructure compose`

## TASK-BASE-003 统一后端开发配置与依赖健康检查

**状态：** 可派遣

**目标：** 让 Identity、ReferenceData 使用一致的开发配置读取基础依赖，并暴露可区分自身与依赖状态的健康检查。

**输入文档：** TASK-BASE-002 输出、BuildingBlocks Infrastructure/Logging/Web 扩展。

**依赖：** TASK-BASE-002。

**允许修改范围：** Identity、ReferenceData Api 的配置、启动注册、健康检查与对应测试；BuildingBlocks 只允许修复复用扩展缺陷。

**预期输出：** 安全的 `appsettings.Development` 示例、环境变量覆盖、PostgreSQL/Redis/RabbitMQ/Seq 配置绑定、liveness/readiness 端点和 TraceId 日志。

**验证与证据：** 提供配置绑定测试、缺失配置失败信息、liveness/readiness 在依赖正常与中断时的 API 测试；证明日志未输出凭据。

**结果回写：** 回写配置节名称、健康端点、依赖超时和敏感字段屏蔽规则。

**建议提交：** `feat(platform): add dependency health checks`

## TASK-BASE-004 建立开发期统一 API 入口

**状态：** 可派遣

**目标：** 建立最小 ApiGateway 或统一开发入口，转发 Identity、ReferenceData 健康端点并提供平台健康聚合。

**输入文档：** 蓝图 01、06、20、27；TASK-BASE-003 健康端点。

**依赖：** TASK-BASE-003。

**允许修改范围：** `src/backend/src/Gateway/**`、解决方案注册、Gateway 测试与本地配置。

**预期输出：** 可独立启动的 Gateway、服务路由、平台 health/readiness 聚合、统一错误返回和开发期 CORS；不实现认证业务。

**验证与证据：** 提供 Gateway 构建、路由转发、服务不可用、超时、健康聚合和 CORS 测试；记录统一入口 URL。

**结果回写：** 回写路由前缀、端口、健康聚合结构与错误码。

**建议提交：** `feat(gateway): add local service entry point`

## TASK-BASE-005 建立一键启动与停止流程

**状态：** 可派遣

**目标：** 提供可审计的开发启动流程，按依赖顺序启动基础设施、后端服务和 Gateway，并安全停止进程。

**输入文档：** TASK-BASE-002 至 TASK-BASE-004 的命令和端口。

**依赖：** TASK-BASE-004。

**允许修改范围：** `deploy/scripts/**` 或明确的开发脚本目录、README 与启动验证测试；不得保存机器专属绝对路径。

**预期输出：** PowerShell 启动/停止/状态脚本，跨平台手工等价命令，端口冲突检查，进程标识和失败清理。

**验证与证据：** 在干净终端运行启动、状态、重复启动、部分依赖失败和停止流程；记录每步退出码，证明停止不删除 Docker 数据卷。

**结果回写：** 回写命令入口、进程管理方式、端口冲突规则和已知平台差异。

**建议提交：** `feat(platform): add local startup workflow`

## TASK-BASE-006 完成新环境冒烟验收

**状态：** 可派遣

**目标：** 从新环境视角验证 clone 后能够启动依赖、后端和统一入口，并形成下一阶段前端可消费的运行契约。

**输入文档：** TASK-BASE-001 至 TASK-BASE-005 全部输出。

**依赖：** TASK-BASE-005。

**允许修改范围：** 根 README、开发启动说明、冒烟测试和本文件执行记录；只修复验收阻塞问题。

**预期输出：** 最短启动路径、前置软件版本、端口表、健康检查表、常见故障诊断和前端 API Base URL。

**验证与证据：** 重新执行基础设施启动、全解决方案构建测试、Gateway/Identity/ReferenceData 健康检查；记录总耗时、退出码、测试数量和容器状态。

**结果回写：** 将基线状态改为已完成，登记验证提交和 Phase 2 前端输入契约。

**建议提交：** `docs(platform): publish runnable baseline guide`

## 5. 完成标准

- 新开发环境可按文档启动 PostgreSQL、Redis、RabbitMQ、Seq、Identity、ReferenceData 和统一入口。
- 所有依赖有健康状态，错误能通过 TraceId 与日志定位。
- restore、build、test 和包审计有最新证据。
- 已明确前端使用的 API Base URL、CORS 与健康端点。
- 未提前实现 Identity 业务功能或 MES 业务领域。

## 6. 执行记录

| 任务 | 状态 | 执行者/任务 | 提交 | 验证证据 | 结果回写 |
| --- | --- | --- | --- | --- | --- |
| TASK-BASE-001 | 可派遣 | - | - | - | - |
| TASK-BASE-002 | 可派遣 | - | - | - | - |
| TASK-BASE-003 | 可派遣 | - | - | - | - |
| TASK-BASE-004 | 可派遣 | - | - | - | - |
| TASK-BASE-005 | 可派遣 | - | - | - | - |
| TASK-BASE-006 | 可派遣 | - | - | - | - |
