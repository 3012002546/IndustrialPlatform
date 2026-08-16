# deploy/application — 云端应用部署(单应用容器)

本目录承载平台云端短期部署的应用侧资产:一次性 admin 初始化脚本、
应用镜像与 Compose 编排(编排部分见下文「部署」)。

## 一次性 admin 初始化(bootstrap-admin.sh)

`bootstrap-admin.sh` 使用**同一不可变应用镜像**在容器内执行 `--initialize-admin --credential-output`,
首次创建 admin 时将一次性凭据 JSON **精确写入宿主机 `$CREDENTIAL_OUTPUT`**:
挂载凭据目录到容器 `/run/bootstrap`,容器内固定输出 `/run/bootstrap/bootstrap-admin.json`(原子写、0600),
容器退出成功后同目录原子 `mv` 对齐到 `$CREDENTIAL_OUTPUT`(默认 UTC 时间戳文件名或任意自定义文件名均支持)。
重复执行幂等:不生成、不覆盖、不重发凭据。

### 用法

```bash
export APPLICATION_IMAGE=registry.example/industrial-platform:2026.08.16-1   # 不可变 tag 或 @sha256 digest,禁止 latest
export APPLICATION_ENV_FILE=/etc/industrial-platform/app.env                # 绝对路径;需含 ASPNETCORE_ENVIRONMENT=Development
export APPLICATION_NETWORK=industrial-platform-backend                       # 连接现有基础设施(PostgreSQL/Redis)网络
export CREDENTIAL_OUTPUT=/var/lib/industrial-platform/bootstrap/bootstrap-admin.json  # 可选,默认带 UTC 时间戳

deploy/application/bootstrap-admin.sh
```

### 语义与安全

- **镜像不可变**:必须显式 tag(非 `:latest`)或 `@sha256:<64 hex>` digest;裸镜像名/空 tag/`latest` 一律拒绝。
- **不泄密**:脚本不打印环境文件内容;容器 stdout 只含脱敏状态与种子账本。
- **覆盖保护**:`$CREDENTIAL_OUTPUT` 已存在立即拒绝(凭据只交付一次)。
- **退出码透传**:容器/镜像执行失败时透传其退出码,便于 CI/编排判断。
- **权限**:凭据 JSON 由应用以仅当前用户(容器内运行用户)可访问方式写入,宿主机侧确保目录权限收敛。
- **不常驻**:`docker run --rm` 一次性执行,不启动常驻容器;服务器不需要 clone/build 源码。

### 凭据 JSON 字段

`tenantNId`、`userNId`、`loginName`、`temporaryPassword`、`deliveryReference`、`recoveryReference`、`deliveryId`、`createdOnUtc`。

### 测试

```bash
bash -n deploy/application/bootstrap-admin.sh
bash tests/scripts/bootstrap-admin.Tests.sh   # 假 docker;需要 Git Bash
```

## 部署(Compose 单应用容器)

常驻应用只运行一个 **UnifiedHost** 容器(组合 Identity/SystemData/ReferenceData);
前端生产静态文件由该容器提供(`wwwroot`),云端无需第二个前端容器。发布端口只绑定 Tailnet;
数据库与内部 API 不对外发布。服务器不 clone/build 源码,只使用 CI 发布的不可变镜像。

### 镜像构建(CI)

`Dockerfile` 多阶段:前端生产构建(`pnpm build`,产物进 `wwwroot`)→ `dotnet publish` UnifiedHost → 运行时镜像。

```bash
docker build -f deploy/application/Dockerfile \
  --build-arg VITE_API_BASE_URL=https://<tailnet-ip>:8080 \
  -t registry.example/industrial-platform:<version> .
```

- `VITE_API_BASE_URL` 必须显式传入部署后前端可达的同一源;`VITE_AUTH_MODE` 生产构建禁止 mock(默认 http)。
- 发布镜像必须使用**不可变 tag 或 `@sha256` digest**(禁止 `latest`),供 `bootstrap-admin.sh`/`deploy.sh` 校验。

### 部署顺序与脚本

```bash
export APPLICATION_IMAGE=registry.example/industrial-platform:<version>
export APPLICATION_ENV_FILE=/etc/industrial-platform/app.env     # 绝对路径;含 ASPNETCORE_ENVIRONMENT、SqlSugar/Redis/Identity 等配置
export APPLICATION_NETWORK=industrial-platform-dev_backend        # 现有 cloud-dev 基础设施网络(admin 初始化与 UnifiedHost 同一变量)
export TAILSCALE_IP=<tailnet-ip>
# 可选:export APP_HTTP_PORT=8080   # 宿主发布端口(默认 8080;容器内固定 8080;readiness 探测同一端口)

deploy/application/deploy.sh
```

`deploy.sh` 固定顺序(每步失败即退出并透传退出码):

1. **基础设施健康**:Docker 网络存在 + `industrial-platform-postgres`/`industrial-platform-redis` 容器 healthy(轮询 `WAIT_INFRA_SECONDS`,默认 120s)。
2. **同镜像一次性 admin 初始化**:调用 `bootstrap-admin.sh`(幂等,凭据只交付一次,不打印环境文件)。
3. **启动 UnifiedHost**:`docker compose -f deploy/application/compose.yaml up -d app`(单应用容器,端口只绑 Tailnet,宿主端口 `APP_HTTP_PORT`)。
4. **readiness**:轮询 `http://<TAILSCALE_IP>:<APP_HTTP_PORT>/health/ready` 直到 200(`WAIT_APP_SECONDS`,默认 180s)。

> 网络变量唯一来源为 `APPLICATION_NETWORK`(`bootstrap-admin.sh`、`deploy.sh`、`compose.yaml` 共用);
> `compose.yaml` 不再有独立网络变量,调用者必须显式传入基础设施网络,不依赖默认掩盖错误。

### 应用配置(app.env)

环境文件至少包含:数据库/Redis 连接与凭据、`DatabaseTopology`(Shared 模式)、`Identity:Jwt` 等
(与各服务 appsettings 配置节一致;UnifiedHost 单连接承载全部模块,PerService 拓扑需 `ServiceDatabases["unifiedhost"]` 显式映射)。
`ASPNETCORE_ENVIRONMENT` 按部署环境设置;admin 初始化仅允许 Development。

### 未来拆分(保留说明)

- 独立 API Host(Gateway/Identity/SystemData/ReferenceData)与 Gateway 全部保留,继续支持独立测试与未来多容器拆分。
- 未来把 `compose.yaml` 改为多容器(每模块一个容器 + Gateway)时,admin 初始化 job 的镜像切换为 **Identity 镜像**,
  `--initialize-admin --credential-output` 语义与账本不变,`bootstrap-admin.sh` 无需改动。
- 资源验收:UnifiedHost 相对多后端进程的冷启动/内存/线程/连接数测量见 PF 任务交付记录(用于容量规划,不否定统一运行目标)。
