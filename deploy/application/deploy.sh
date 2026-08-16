#!/usr/bin/env bash
# deploy.sh — 云端单应用容器部署编排。
# 固定顺序:基础设施健康 → 同镜像一次性 admin 初始化 → 启动 UnifiedHost → readiness。
# 使用 CI 发布的不可变镜像;服务器不 clone/build 源码。
#
# 必需环境变量:
#   APPLICATION_IMAGE     不可变镜像(显式 tag 或 @sha256 digest,禁止 latest)
#   APPLICATION_ENV_FILE  环境文件绝对路径(含 ASPNETCORE_ENVIRONMENT=Production/Development、数据库与 Redis 配置)
#   APPLICATION_NETWORK   基础设施网络(cloud-dev compose 网络)
#   TAILSCALE_IP          Tailnet 地址(发布端口只绑定该地址)
# 可选:
#   CREDENTIAL_OUTPUT     admin 凭据输出路径(透传 bootstrap-admin.sh)
#   APP_HTTP_PORT         应用宿主发布端口(默认 8080;容器内固定 8080;readiness 探测同一端口)
#   WAIT_INFRA_SECONDS    基础设施健康等待上限(默认 120)
#   WAIT_APP_SECONDS      readiness 等待上限(默认 180)
set -Eeuo pipefail

fail() {
  echo "deploy: $*" >&2
  exit 1
}

: "${APPLICATION_IMAGE:?APPLICATION_IMAGE is required}"
: "${APPLICATION_ENV_FILE:?APPLICATION_ENV_FILE is required}"
: "${APPLICATION_NETWORK:?APPLICATION_NETWORK is required}"
: "${TAILSCALE_IP:?TAILSCALE_IP is required}"
CREDENTIAL_OUTPUT="${CREDENTIAL_OUTPUT:-/var/lib/industrial-platform/bootstrap/bootstrap-admin-$(date -u +%Y%m%dT%H%M%SZ).json}"
APP_HTTP_PORT="${APP_HTTP_PORT:-8080}"
WAIT_INFRA_SECONDS="${WAIT_INFRA_SECONDS:-120}"
WAIT_APP_SECONDS="${WAIT_APP_SECONDS:-180}"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

command -v docker >/dev/null 2>&1 || fail "docker CLI 不可用"
command -v curl >/dev/null 2>&1 || fail "服务器需要 curl 用于 readiness 探测"

# ---------------------------------------------------------------------------
# 1. 基础设施健康:网络存在 + cloud-dev 容器(postgres/redis)healthy
# ---------------------------------------------------------------------------
docker network inspect "$APPLICATION_NETWORK" >/dev/null 2>&1 \
  || fail "Docker 网络不存在: $APPLICATION_NETWORK"

for container in industrial-platform-postgres industrial-platform-redis; do
  for i in $(seq 1 "$WAIT_INFRA_SECONDS"); do
    status="$(docker inspect -f '{{.State.Health.Status}}' "$container" 2>/dev/null || echo missing)"
    [ "$status" = "healthy" ] && break
    if [ "$i" -eq "$WAIT_INFRA_SECONDS" ]; then
      fail "基础设施容器 $container 未就绪(最后状态: $status)"
    fi
    sleep 2
  done
done
echo "deploy: 基础设施健康(postgres/redis)。"

# ---------------------------------------------------------------------------
# 2. 同镜像一次性 admin 初始化(幂等;凭据只交付一次)
# ---------------------------------------------------------------------------
APPLICATION_IMAGE="$APPLICATION_IMAGE" \
APPLICATION_ENV_FILE="$APPLICATION_ENV_FILE" \
APPLICATION_NETWORK="$APPLICATION_NETWORK" \
CREDENTIAL_OUTPUT="$CREDENTIAL_OUTPUT" \
  "$SCRIPT_DIR/bootstrap-admin.sh"

# ---------------------------------------------------------------------------
# 3. 启动常驻 UnifiedHost(单应用容器)
#    compose.yaml 读取与 bootstrap-admin.sh 相同的 APPLICATION_NETWORK;
#    宿主端口 APP_HTTP_PORT 一并导出(容器内固定 8080)。
# ---------------------------------------------------------------------------
export APPLICATION_IMAGE APPLICATION_ENV_FILE APPLICATION_NETWORK TAILSCALE_IP APP_HTTP_PORT
docker compose -f "$SCRIPT_DIR/compose.yaml" up -d app

# ---------------------------------------------------------------------------
# 4. readiness
# ---------------------------------------------------------------------------
url="http://${TAILSCALE_IP}:${APP_HTTP_PORT}/health/ready"
for i in $(seq 1 "$WAIT_APP_SECONDS"); do
  code="$(curl -s -o /dev/null -w '%{http_code}' "$url" 2>/dev/null || echo 000)"
  [ "$code" = "200" ] && break
  if [ "$i" -eq "$WAIT_APP_SECONDS" ]; then
    fail "UnifiedHost readiness 超时(最后状态码 $code): $url"
  fi
  sleep 2
done
echo "deploy: UnifiedHost readiness OK ($url)。"
