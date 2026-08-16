#!/usr/bin/env bash
# bootstrap-admin.sh 独立测试(无 shellcheck 依赖):使用假 docker 验证
# 必填参数、绝对路径、镜像不可变 tag/digest、挂载与退出码透传、不打印环境文件、
# 凭据文件精确落盘(自定义文件名与默认 UTC 时间戳文件名)。
# 退出码 0 = 全部通过。
set -u

SCRIPT_UNDER_TEST="$(cd "$(dirname "$0")/../.." && pwd)/deploy/application/bootstrap-admin.sh"
FAILURES=0
TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/industrial-platform-bash-tests.XXXXXX")"

pass() { echo "PASS: $1"; }
fail() { echo "FAIL: $1"; FAILURES=$((FAILURES + 1)); }

# ---------------------------------------------------------------------------
# 假 docker:记录参数;可创建挂载内的凭据文件;退出码由环境变量控制
# ---------------------------------------------------------------------------
FAKE_BIN="$TMP_ROOT/fakedocker"
mkdir -p "$FAKE_BIN"
cat > "$FAKE_BIN/docker" <<'FAKEDOCKER'
#!/usr/bin/env bash
echo "$@" >> "${FAKE_DOCKER_LOG:?}"
prev=""
for arg in "$@"; do
  if [ "$prev" = "-v" ]; then
    hostdir="${arg%%:*}"
    if [ -n "${FAKE_DOCKER_CREATE_FILE:-}" ]; then
      : > "$hostdir/bootstrap-admin.json"
    fi
  fi
  prev="$arg"
done
exit "${FAKE_DOCKER_EXIT:-0}"
FAKEDOCKER
chmod +x "$FAKE_BIN/docker"

export FAKE_DOCKER_LOG="$TMP_ROOT/docker-args.log"
export PATH="$FAKE_BIN:$PATH"

run_script() {
  # 清空日志;调用方自行控制 FAKE_DOCKER_EXIT / FAKE_DOCKER_CREATE_FILE 等额外变量
  : > "$FAKE_DOCKER_LOG"
  env -u APPLICATION_IMAGE -u APPLICATION_ENV_FILE -u APPLICATION_NETWORK -u CREDENTIAL_OUTPUT \
    "$@" bash "$SCRIPT_UNDER_TEST"
  return $?
}

ENV_FILE="$TMP_ROOT/app.env"
printf 'ASPNETCORE_ENVIRONMENT=Development\nTOP_SECRET=SHOULD-NOT-APPEAR\n' > "$ENV_FILE"
OUT_DIR="$TMP_ROOT/out"
mkdir -p "$OUT_DIR"
CRED_OUT="$OUT_DIR/bootstrap-admin.json"

# ---------------------------------------------------------------------------
# 1. 必填参数
# ---------------------------------------------------------------------------
echo '== 必填参数 =='
out="$(run_script 2>&1)"; code=$?
[ "$code" -ne 0 ] && pass "缺少必填环境变量时脚本失败 (code $code)" || fail "缺少必填环境变量时应失败"
echo "$out" | grep -q 'APPLICATION_IMAGE' && pass "usage 提到 APPLICATION_IMAGE" || fail "usage 未提到 APPLICATION_IMAGE"

# ---------------------------------------------------------------------------
# 2. 绝对路径
# ---------------------------------------------------------------------------
echo '== 绝对路径 =='
out="$(run_script APPLICATION_IMAGE=app:1.0.0 APPLICATION_ENV_FILE=relative.env APPLICATION_NETWORK=net 2>&1)"; code=$?
[ "$code" -ne 0 ] && pass "相对 APPLICATION_ENV_FILE 被拒绝" || fail "相对 APPLICATION_ENV_FILE 应被拒绝"
echo "$out" | grep -q '绝对路径' && pass "相对路径错误信息正确" || fail "相对路径错误信息缺失"

out="$(run_script APPLICATION_IMAGE=app:1.0.0 APPLICATION_ENV_FILE="$ENV_FILE" APPLICATION_NETWORK=net CREDENTIAL_OUTPUT=relative.json 2>&1)"; code=$?
[ "$code" -ne 0 ] && pass "相对 CREDENTIAL_OUTPUT 被拒绝" || fail "相对 CREDENTIAL_OUTPUT 应被拒绝"

# ---------------------------------------------------------------------------
# 3. 镜像不可变性
# ---------------------------------------------------------------------------
echo '== 镜像不可变性 =='
for bad in 'registry.example/app:latest' 'app' 'app:' 'registry.example/app' 'app@sha256:abc'; do
  out="$(run_script APPLICATION_IMAGE="$bad" APPLICATION_ENV_FILE="$ENV_FILE" APPLICATION_NETWORK=net 2>&1)"; code=$?
  [ "$code" -ne 0 ] && pass "镜像被拒绝: $bad" || fail "镜像应被拒绝: $bad"
done
[ ! -s "$FAKE_DOCKER_LOG" ] && pass "镜像非法时未调用 docker" || fail "镜像非法时不应调用 docker"

# ---------------------------------------------------------------------------
# 4. 快乐路径:tag 与 digest
# ---------------------------------------------------------------------------
echo '== 快乐路径 =='
run_script APPLICATION_IMAGE='registry.example/app:1.2.3' APPLICATION_ENV_FILE="$ENV_FILE" APPLICATION_NETWORK=net CREDENTIAL_OUTPUT="$CRED_OUT" >/dev/null 2>&1
code=$?
[ "$code" -eq 0 ] && pass "tag 镜像执行成功" || fail "tag 镜像应成功 (code $code)"
args="$(cat "$FAKE_DOCKER_LOG")"
echo "$args" | grep -q -- '--rm' && pass "docker 带 --rm" || fail "docker 缺少 --rm"
echo "$args" | grep -q -- "--env-file $ENV_FILE" && pass "docker 带 env-file" || fail "docker 缺少 env-file"
echo "$args" | grep -q -- '--network net' && pass "docker 带 network" || fail "docker 缺少 network"
echo "$args" | grep -q -- "-v $OUT_DIR:/run/bootstrap" && pass "docker 带 bind mount" || fail "docker 缺少 bind mount"
echo "$args" | grep -q -- 'registry.example/app:1.2.3' && pass "docker 使用同一应用镜像" || fail "docker 镜像参数错误"
echo "$args" | grep -q -- '--initialize-admin --credential-output /run/bootstrap/bootstrap-admin.json' && pass "容器内固定输出路径" || fail "容器内输出路径参数错误"

# digest 镜像
: > "$FAKE_DOCKER_LOG"
run_script APPLICATION_IMAGE='registry.example/app@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' APPLICATION_ENV_FILE="$ENV_FILE" APPLICATION_NETWORK=net CREDENTIAL_OUTPUT="$CRED_OUT" >/dev/null 2>&1
code=$?
[ "$code" -eq 0 ] && [ -s "$FAKE_DOCKER_LOG" ] && pass "digest 镜像执行成功" || fail "digest 镜像应成功"

# ---------------------------------------------------------------------------
# 5. 覆盖拒绝与环境文件不泄密
# ---------------------------------------------------------------------------
echo '== 覆盖拒绝与泄密 =='
: > "$FAKE_DOCKER_LOG"
touch "$CRED_OUT"
out="$(run_script APPLICATION_IMAGE='registry.example/app:1.2.3' APPLICATION_ENV_FILE="$ENV_FILE" APPLICATION_NETWORK=net CREDENTIAL_OUTPUT="$CRED_OUT" 2>&1)"; code=$?
[ "$code" -ne 0 ] && pass "目标文件已存在被拒绝" || fail "目标文件已存在应被拒绝"
[ ! -s "$FAKE_DOCKER_LOG" ] && pass "目标已存在时未调用 docker" || fail "目标已存在时不应调用 docker"
rm -f "$CRED_OUT"
echo "$out" | grep -q 'SHOULD-NOT-APPEAR' && fail "输出泄漏环境文件内容" || pass "输出未泄漏环境文件内容"

# ---------------------------------------------------------------------------
# 6. 退出码透传与凭据消息
# ---------------------------------------------------------------------------
echo '== 退出码与消息 =='
export FAKE_DOCKER_EXIT=7
run_script APPLICATION_IMAGE='registry.example/app:1.2.3' APPLICATION_ENV_FILE="$ENV_FILE" APPLICATION_NETWORK=net CREDENTIAL_OUTPUT="$CRED_OUT" >/dev/null 2>&1
code=$?
[ "$code" -eq 7 ] && pass "docker 退出码透传 (7)" || fail "docker 退出码应透传 7 (got $code)"
unset FAKE_DOCKER_EXIT

export FAKE_DOCKER_CREATE_FILE=1
out="$(run_script APPLICATION_IMAGE='registry.example/app:1.2.3' APPLICATION_ENV_FILE="$ENV_FILE" APPLICATION_NETWORK=net CREDENTIAL_OUTPUT="$CRED_OUT" 2>&1)"; code=$?
unset FAKE_DOCKER_CREATE_FILE
[ "$code" -eq 0 ] && pass "凭据文件生成场景退出码 0" || fail "凭据文件生成场景应退出 0"
echo "$out" | grep -q '已创建 admin' && pass "输出提示已创建 admin" || fail "输出缺少已创建 admin 提示"
[ -f "$CRED_OUT" ] && pass "凭据文件已落盘" || fail "凭据文件应已落盘"
rm -f "$CRED_OUT"

out="$(run_script APPLICATION_IMAGE='registry.example/app:1.2.3' APPLICATION_ENV_FILE="$ENV_FILE" APPLICATION_NETWORK=net CREDENTIAL_OUTPUT="$CRED_OUT" 2>&1)"; code=$?
[ "$code" -eq 0 ] && pass "无新凭据场景退出码 0" || fail "无新凭据场景应退出 0"
echo "$out" | grep -q '已初始化,无新凭据' && pass "输出提示已初始化无新凭据" || fail "输出缺少无新凭据提示"
[ ! -f "$CRED_OUT" ] && pass "未生成凭据文件" || fail "无新凭据时不应生成文件"

# 默认 CREDENTIAL_OUTPUT(静态/参数断言;不读写真实 /var/lib,普通 Git Bash / 非 root CI 可运行)
# 默认路径 /var/lib/industrial-platform/bootstrap/bootstrap-admin-<UTC>.json 只做源码断言;
# 实际落盘与原子移动由自定义路径(临时目录)场景验证(见 7a)。
echo '== 默认 CREDENTIAL_OUTPUT(静态断言)=='
grep -q '/var/lib/industrial-platform/bootstrap/bootstrap-admin-' "$SCRIPT_UNDER_TEST" \
  && pass "默认凭据路径 /var/lib/industrial-platform/bootstrap/bootstrap-admin-<UTC>.json" \
  || fail "脚本缺少默认凭据路径"
grep -q 'date -u +%Y%m%dT%H%M%SZ' "$SCRIPT_UNDER_TEST" \
  && pass "默认文件名含 UTC 时间戳(date -u +%Y%m%dT%H%M%SZ)" \
  || fail "脚本缺少 UTC 时间戳文件名生成"
grep -q 'FIXED_HOST_OUTPUT' "$SCRIPT_UNDER_TEST" \
  && pass "脚本含固定宿主文件 mv 对齐逻辑" \
  || fail "脚本缺少固定宿主文件 mv 对齐逻辑"

# ---------------------------------------------------------------------------
# 7. 凭据文件名精确落盘:自定义文件名与默认时间戳文件名
# ---------------------------------------------------------------------------
echo '== 凭据文件名精确落盘 =='

# 7a. 自定义文件名:容器固定文件必须精确 mv 对齐到自定义路径,不残留固定文件
export FAKE_DOCKER_CREATE_FILE=1
CUSTOM_CRED="$OUT_DIR/custom-credentials.json"
out="$(run_script APPLICATION_IMAGE='registry.example/app:1.2.3' APPLICATION_ENV_FILE="$ENV_FILE" APPLICATION_NETWORK=net CREDENTIAL_OUTPUT="$CUSTOM_CRED" 2>&1)"; code=$?
unset FAKE_DOCKER_CREATE_FILE
[ "$code" -eq 0 ] && pass "自定义凭据文件名场景退出码 0" || fail "自定义凭据文件名场景应退出 0"
[ -f "$CUSTOM_CRED" ] && pass "凭据精确落到自定义路径" || fail "凭据未落到自定义路径: $CUSTOM_CRED"
[ ! -f "$OUT_DIR/bootstrap-admin.json" ] && pass "容器固定文件已 mv 对齐(无残留)" || fail "容器固定文件残留: $OUT_DIR/bootstrap-admin.json"
echo "$out" | grep -q '已创建 admin' && pass "自定义文件名场景提示已创建" || fail "自定义文件名场景缺少已创建提示"
echo "$out" | grep -q -- "$CUSTOM_CRED" && pass "输出提示自定义路径" || fail "输出未提示自定义路径"
rm -f "$CUSTOM_CRED"

# 7b. 默认 UTC 时间戳文件名:只做静态/参数断言(不写真实 /var/lib)。
#     默认路径 /var/lib/.../bootstrap-admin-<UTC>.json 与 date -u 时间戳生成已在上方静态断言;
#     实际落盘/原子移动由 7a 自定义路径(临时目录)验证,默认与自定义走同一 mv 对齐逻辑。

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
