#!/usr/bin/env bash
# bootstrap-admin.sh — 使用不可变应用镜像执行一次性 admin 初始化(仅首次创建时交付凭据)。
#
# 复用应用镜像内的 --initialize-admin --credential-output 命令(底层 IdentityInitializationService),
# 首次创建 admin 时将一次性凭据 JSON 精确写入宿主机 $CREDENTIAL_OUTPUT:
#   1. 挂载宿主机凭据目录到容器 /run/bootstrap;
#   2. 容器内固定输出 /run/bootstrap/bootstrap-admin.json(原子写,0600);
#   3. 容器退出成功后,宿主机固定文件同目录原子 mv 对齐到 $CREDENTIAL_OUTPUT
#      (支持默认 UTC 时间戳文件名与任意自定义文件名)。
# 重复执行不生成、不覆盖、不重发;目标已存在立即拒绝;不打印环境文件内容;
# 不启动常驻容器,不需要在服务器 clone/build 源码。
#
# 必需环境变量:
#   APPLICATION_IMAGE     不可变镜像引用(显式 tag,禁止 :latest;或 @sha256: digest)
#   APPLICATION_ENV_FILE  容器环境文件(绝对路径;需含 ASPNETCORE_ENVIRONMENT=Development)
#   APPLICATION_NETWORK   应用所在 Docker 网络(连接现有基础设施网络)
# 可选:
#   CREDENTIAL_OUTPUT     宿主机凭据输出路径(绝对);默认
#                         /var/lib/industrial-platform/bootstrap/bootstrap-admin-<UTC>.json
set -Eeuo pipefail

usage() {
  cat >&2 <<'EOF'
用法:
  APPLICATION_IMAGE=<image> APPLICATION_ENV_FILE=<abs path> APPLICATION_NETWORK=<network> \
    [CREDENTIAL_OUTPUT=<abs path>] bootstrap-admin.sh
EOF
}

fail() {
  echo "bootstrap-admin: $*" >&2
  exit 1
}

# ---------------------------------------------------------------------------
# 必填参数与绝对路径校验
# ---------------------------------------------------------------------------
: "${APPLICATION_IMAGE:?APPLICATION_IMAGE is required}"
: "${APPLICATION_ENV_FILE:?APPLICATION_ENV_FILE is required}"
: "${APPLICATION_NETWORK:?APPLICATION_NETWORK is required}"

case "$APPLICATION_ENV_FILE" in
  /*) ;;
  *) fail "APPLICATION_ENV_FILE 必须为绝对路径: $APPLICATION_ENV_FILE" ;;
esac

if [ -z "${CREDENTIAL_OUTPUT:-}" ]; then
  CREDENTIAL_OUTPUT="/var/lib/industrial-platform/bootstrap/bootstrap-admin-$(date -u +%Y%m%dT%H%M%SZ).json"
fi

case "$CREDENTIAL_OUTPUT" in
  /*) ;;
  *) fail "CREDENTIAL_OUTPUT 必须为绝对路径: $CREDENTIAL_OUTPUT" ;;
esac

# ---------------------------------------------------------------------------
# 镜像不可变性校验:显式 tag(非 latest)或 sha256 digest
# ---------------------------------------------------------------------------
if [[ "$APPLICATION_IMAGE" == *":latest" ]]; then
  fail "APPLICATION_IMAGE 禁止使用 :latest(必须为不可变 tag 或 digest): $APPLICATION_IMAGE"
fi

if [[ "$APPLICATION_IMAGE" == *"@"* ]]; then
  if [[ ! "$APPLICATION_IMAGE" =~ ^[^@]+@sha256:[0-9a-f]{64}$ ]]; then
    fail "APPLICATION_IMAGE digest 必须是 @sha256:<64位十六进制>: $APPLICATION_IMAGE"
  fi
else
  last_segment="${APPLICATION_IMAGE##*/}"
  if [[ "$last_segment" != *":"* ]]; then
    fail "APPLICATION_IMAGE 必须钉定不可变 tag 或 digest(不能裸引用镜像名): $APPLICATION_IMAGE"
  fi
  tag="${last_segment##*:}"
  if [ -z "$tag" ]; then
    fail "APPLICATION_IMAGE tag 不能为空: $APPLICATION_IMAGE"
  fi
fi

# ---------------------------------------------------------------------------
# 环境与目标文件检查
# ---------------------------------------------------------------------------
command -v docker >/dev/null 2>&1 || fail "未找到 docker CLI。"
[ -f "$APPLICATION_ENV_FILE" ] || fail "环境文件不存在: $APPLICATION_ENV_FILE"
[ -e "$CREDENTIAL_OUTPUT" ] && fail "凭据输出文件已存在,拒绝覆盖: $CREDENTIAL_OUTPUT (凭据只交付一次)。"

CREDENTIAL_DIR="$(dirname "$CREDENTIAL_OUTPUT")"
FIXED_CONTAINER_OUTPUT="/run/bootstrap/bootstrap-admin.json"
FIXED_HOST_OUTPUT="$CREDENTIAL_DIR/bootstrap-admin.json"

mkdir -p "$CREDENTIAL_DIR"

# ---------------------------------------------------------------------------
# 执行:同镜像一次性初始化;容器内固定输出路径经目录 bind mount 落盘
# ---------------------------------------------------------------------------
echo "bootstrap-admin: 执行 admin 初始化 (镜像 $APPLICATION_IMAGE, 输出 $CREDENTIAL_OUTPUT)"

if docker run --rm \
  --env-file "$APPLICATION_ENV_FILE" \
  --network "$APPLICATION_NETWORK" \
  -v "$CREDENTIAL_DIR:/run/bootstrap" \
  "$APPLICATION_IMAGE" \
  --initialize-admin --credential-output "$FIXED_CONTAINER_OUTPUT"; then
  :
else
  code=$?
  echo "bootstrap-admin: 初始化失败(退出码 $code),请检查上方容器输出。" >&2
  exit "$code"
fi

# ---------------------------------------------------------------------------
# 对齐:容器固定路径文件(宿主机 CREDENTIAL_DIR/bootstrap-admin.json)精确落到
# $CREDENTIAL_OUTPUT。默认时间戳文件名或自定义文件名均支持;同路径(用户恰好指定
# 固定名)时无需移动。同目录 mv 为原子操作,失败不留半文件。
# ---------------------------------------------------------------------------
if [ -f "$FIXED_HOST_OUTPUT" ] && [ "$FIXED_HOST_OUTPUT" != "$CREDENTIAL_OUTPUT" ]; then
  mv -f "$FIXED_HOST_OUTPUT" "$CREDENTIAL_OUTPUT"
fi

if [ -f "$CREDENTIAL_OUTPUT" ]; then
  echo "bootstrap-admin: 已创建 admin,一次性凭据已写入(仅当前用户可读): $CREDENTIAL_OUTPUT"
else
  echo "bootstrap-admin: 已初始化,无新凭据。输出路径(未生成文件): $CREDENTIAL_OUTPUT"
fi
exit 0
