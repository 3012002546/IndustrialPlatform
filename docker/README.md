# Docker — 本地开发基础设施

TASK-BASE-002 交付:PostgreSQL / Redis / RabbitMQ / Seq 四服务 Compose 编排,含健康检查与持久化卷。

## 前置要求

- Docker Desktop(Windows/Mac)或 Docker Engine + Docker Compose v2(Linux)
- 校验:`docker compose version` 输出版本号

## 快速开始

```bash
cd docker
cp .env.example .env        # 首次;按需修改示例值
docker compose config       # 校验编排与配置(必须 0 错误)
docker compose up -d        # 后台启动四服务
docker compose ps           # 等待各服务 healthy
```

> `.env` 已在 `.gitignore`,凭据不进版本库。

## 服务与端口

| 服务 | 容器名 | 镜像 | 宿主机端口 | 健康检查 |
| --- | --- | --- | --- | --- |
| PostgreSQL | `industrial-platform-postgres` | `postgres:18-alpine` | 5432 | `pg_isready` |
| Redis | `industrial-platform-redis` | `redis:7.4-alpine` | 6379 | `redis-cli ping` |
| RabbitMQ | `industrial-platform-rabbitmq` | `rabbitmq:4-management` | 5672(AMQP)、15672(管理台) | `rabbitmq-diagnostics ping` |
| Seq | `industrial-platform-seq` | `datalust/seq:2025` | 5341(Web/API) | `curl /api` |

网络:`industrial-platform-network`(bridge);数据卷: `industrial-platform_postgres-data`、`industrial-platform_redis-data`、`industrial-platform_rabbitmq-data`、`industrial-platform_seq-data`。

## 健康验证

```bash
docker compose ps                    # 四服务全部 healthy
# 逐项验证
docker compose exec postgres pg_isready -U industrial -d industrial_platform
docker compose exec redis redis-cli ping                # 期望 PONG
docker compose exec rabbitmq rabbitmq-diagnostics -q ping
curl -fsS http://localhost:5341/api | head -c 200       # Seq API 返回 JSON
```

RabbitMQ 管理台:`http://localhost:15672`(用户名/密码取自 `.env`)。Seq Web:`http://localhost:5341`。

## 停止与清理

```bash
docker compose stop        # 停止容器,保留卷与配置
docker compose start       # 再次启动
docker compose down        # 移除容器与网络,保留数据卷
docker compose down -v     # ⚠️ 同时删除数据卷,数据不可恢复,仅在确认后执行
```

`stop` / `down` 均不自动删除持久化数据;清理数据卷必须显式使用 `-v`。

## 诊断命令

```bash
docker compose logs -f postgres      # 跟踪某服务日志
docker compose logs --tail=200 rabbitmq
docker compose ps -a                 # 含已停止容器
docker compose top                   # 容器内进程
docker compose exec seq curl -fsS http://localhost:80/api
```

## 常见故障

- **端口被占用**:启动报 `port is already allocated`。`netstat -ano | findstr :5432` 定位占用进程,或修改 `docker-compose.yml` 宿主机端口(如 `"5433:5432"`)。
- **服务一直 starting / unhealthy**:先 `docker compose ps`,再 `docker compose logs <服务>` 定位;常见于首次拉取镜像或 `.env` 凭据不一致(改凭据后需 `docker compose down` 重建)。
- **镜像拉取失败**:确认 Docker Desktop 已登录/可访问镜像源;中国大陆网络建议配置镜像加速。
- **Seq healthcheck 异常**:若 `datalust/seq` 镜像内无 `curl`(个别版本),将 healthcheck 改为镜像自带检测或将 `test` 置空依赖镜像内置健康检查。

## 与后续任务衔接

- TASK-BASE-003 将读取以上端口/凭据,为 Identity/ReferenceData 配置依赖连接与 liveness/readiness。
- TASK-BASE-005 将把 `docker compose up -d` 纳入一键启动脚本。
- PostgreSQL 时间列映射 `timestamptz`、Seq 日志链路(TraceId)在 TASK-BASE-003 验收。
