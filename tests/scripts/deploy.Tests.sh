#!/usr/bin/env bash
# deploy.sh 独立测试(无 shellcheck 依赖):使用假 docker/compose/curl 验证
# 网络变量统一(APPLICATION_NETWORK)、APP_HTTP_PORT 透传 Compose 与 readiness 探测、
# 自定义凭据文件名传递,以及环境文件不泄密。
# 另做静态断言:compose.yaml 只使用 APPLICATION_NETWORK / APP_HTTP_PORT,不残留 PLATFORM_NETWORK。
# 退出码 0 = 全部通过。
set -u

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SCRIPT_UNDER_TEST="$REPO_ROOT/deploy/application/deploy.sh"
COMPOSE_YAML="$REPO_ROOT/deploy/application/compose.yaml"
FAILURES=0
TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/industrial-platform-deploy-tests.XXXXXX")"

pass() { echo "PASS: $1"; }
fail() { echo "FAIL: $1"; FAILURES=$((FAILURES + 1)); }

# ---------------------------------------------------------------------------
# 假 docker:network inspect 成功;inspect 输出 healthy;run/compose 记录并成功
# ---------------------------------------------------------------------------
FAKE_BIN="$TMP_ROOT/fakebin"
mkdir -p "$FAKE_BIN"
cat > "$FAKE_BIN/docker" <<'FAKEDOCKER'
#!/usr/bin/env bash
echo "$*" >> "${FAKE_DOCKER_LOG:?}"
case "${1:-}" in
  network) exit 0 ;;                     # docker network inspect <net>
  inspect) echo "healthy"; exit 0 ;;     # docker inspect -f '{{.State.Health.Status}}' <c>
  run) exit "${FAKE_DOCKER_EXIT:-0}" ;;  # bootstrap-admin.sh 内部 docker run
  compose) exit 0 ;;                     # docker compose -f <file> up -d app
  *) exit 0 ;;
esac
FAKEDOCKER
chmod +x "$FAKE_BIN/docker"

cat > "$FAKE_BIN/curl" <<'FAKECURL'
#!/usr/bin/env bash
echo "$*" >> "${FAKE_CURL_LOG:?}"
echo "200"
exit 0
FAKECURL
chmod +x "$FAKE_BIN/curl"

export FAKE_DOCKER_LOG="$TMP_ROOT/docker-args.log"
export FAKE_CURL_LOG="$TMP_ROOT/curl-args.log"
export PATH="$FAKE_BIN:$PATH"

# ---------------------------------------------------------------------------
# 场景:自定义网络 / 自定义端口 / 自定义凭据文件名 / 不泄密
# ---------------------------------------------------------------------------
ENV_FILE="$TMP_ROOT/app.env"
printf 'ASPNETCORE_ENVIRONMENT=Development\nTOP_SECRET=SHOULD-NOT-APPEAR\n' > "$ENV_FILE"
CRED_DIR="$TMP_ROOT/creds"
mkdir -p "$CRED_DIR"
CRED_OUT="$CRED_DIR/bootstrap-custom.json"

: > "$FAKE_DOCKER_LOG"
: > "$FAKE_CURL_LOG"
out="$(APPLICATION_IMAGE='registry.example/app:1.2.3' \
  APPLICATION_ENV_FILE="$ENV_FILE" \
  APPLICATION_NETWORK='custom-infra-net' \
  TAILSCALE_IP='100.64.0.1' \
  APP_HTTP_PORT='8443' \
  CREDENTIAL_OUTPUT="$CRED_OUT" \
  WAIT_INFRA_SECONDS='10' \
  WAIT_APP_SECONDS='10' \
  bash "$SCRIPT_UNDER_TEST" 2>&1)"; code=$?

[ "$code" -eq 0 ] && pass "deploy.sh 端到端退出码 0" || fail "deploy.sh 应退出 0 (got $code)"
docker_args="$(cat "$FAKE_DOCKER_LOG")"
curl_args="$(cat "$FAKE_CURL_LOG")"

# 网络变量统一:admin 初始化与 UnifiedHost 都使用同一个 APPLICATION_NETWORK
echo "$docker_args" | grep -q -- 'network inspect custom-infra-net' \
  && pass "基础设施网络检查使用 APPLICATION_NETWORK" || fail "网络检查未使用自定义网络"
echo "$docker_args" | grep -q -- '--network custom-infra-net' \
  && pass "bootstrap docker run 使用同一 APPLICATION_NETWORK" || fail "bootstrap 网络参数不一致"

# Compose 启动
echo "$docker_args" | grep -q -- 'compose -f' && pass "调用 docker compose" || fail "未调用 docker compose"
echo "$docker_args" | grep -q -- 'up -d app' && pass "compose 启动 app 服务" || fail "compose 未启动 app 服务"

# APP_HTTP_PORT:readiness 探测实际发布的宿主端口
echo "$curl_args" | grep -q '100.64.0.1:8443/health/ready' \
  && pass "readiness 探测自定义端口 8443" || fail "readiness 未探测自定义端口 (日志: $curl_args)"

# 自定义凭据文件名传递(经 bootstrap-admin.sh 无新凭据路径;CREDENTIAL_OUTPUT 不落盘)
echo "$out" | grep -q -- "$CRED_OUT" \
  && pass "输出提及自定义凭据路径" || fail "输出未提及自定义凭据路径"
echo "$out" | grep -q '已初始化,无新凭据' \
  && pass "admin 已存在时无新凭据提示" || fail "缺少无新凭据提示"
[ ! -f "$CRED_OUT" ] && pass "无新凭据时不生成文件" || fail "无新凭据时不应生成文件"

# 环境文件不泄密
echo "$out" | grep -q 'SHOULD-NOT-APPEAR' \
  && fail "输出泄漏环境文件内容" || pass "输出未泄漏环境文件内容"

# ---------------------------------------------------------------------------
# 静态断言:compose.yaml 与 deploy.sh 变量一致
# ---------------------------------------------------------------------------
echo '== 静态断言 =='
grep -q 'APPLICATION_NETWORK' "$COMPOSE_YAML" && pass "compose 使用 APPLICATION_NETWORK" || fail "compose 缺少 APPLICATION_NETWORK"
grep -q 'PLATFORM_NETWORK' "$COMPOSE_YAML" && fail "compose 残留 PLATFORM_NETWORK" || pass "compose 无 PLATFORM_NETWORK 残留"
grep -q 'APP_HTTP_PORT' "$COMPOSE_YAML" && pass "compose 使用 APP_HTTP_PORT" || fail "compose 缺少 APP_HTTP_PORT"
grep -q 'APP_HTTP_PORT' "$SCRIPT_UNDER_TEST" && pass "deploy.sh 使用/导出 APP_HTTP_PORT" || fail "deploy.sh 缺少 APP_HTTP_PORT"
grep -q 'APPLICATION_NETWORK' "$SCRIPT_UNDER_TEST" && pass "deploy.sh 使用 APPLICATION_NETWORK" || fail "deploy.sh 缺少 APPLICATION_NETWORK"

# ---------------------------------------------------------------------------
# 清理
# ---------------------------------------------------------------------------
rm -rf "$TMP_ROOT"

if [ "$FAILURES" -gt 0 ]; then
  echo "FAILED: $FAILURES 个断言失败"
  exit 1
fi
echo 'ALL PASSED'
exit 0
