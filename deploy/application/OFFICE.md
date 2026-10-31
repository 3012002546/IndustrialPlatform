# 平台与 Office 的 Tailnet 开发部署

`compose.office.yaml` 是 `compose.yaml` 的可选覆盖文件。UnifiedHost 提供前端与平台 API，Collabora CODE 作为独立进程运行。只使用可信镜像的固定 digest；Office 的源码、二进制及前端资源不复制进平台源代码。

## 入口及私有配置

- Tailscale Serve HTTPS 443 → 平台 `127.0.0.1:8080`；HTTPS 8443 → Office `127.0.0.1:9980`。不启用 Funnel，不开放公网服务端口。
- `PLATFORM_ORIGIN=https://<server>.<tailnet>.ts.net`，`OFFICE_AUTHORITY=<server>.<tailnet>.ts.net:8443`。浏览器和 Office 主机都必须能解析并访问平台域名，证书必须受信任。
- `APPLICATION_CONFIG_FILE`、`RENDERER_MANIFEST_FILE`、`APPLICATION_DATA_ROOT` 使用绝对路径。私有 JSON、环境文件、签名密钥和共享凭据不提交，配置/凭据文件设为 0600；给运行账号配置必要的目录遍历权限。
- 运行 UID/GID 为 1000，`HOME=/home/ubuntu`，持久密钥卷挂载到该 HOME 下。不要把它指向基础镜像中属于 UID 1001 的 `/home/pwuser`，否则 Chromium crashpad 和密钥访问会失败。
- `RENDERER_SECCOMP_PROFILE` 指向运维安装的 [Playwright v1.62.1 官方 seccomp 配置](https://github.com/microsoft/playwright/blob/v1.62.1/utils/docker/seccomp_profile.json)，SHA-256 `cc3e61cabda6bbc1e53e54d27ba4d55a9d3be829b6dd1a596f4a7b31b1cc7849`。它在默认拒绝的 Docker 规则上允许沙箱所需的用户命名空间；保持 `chromiumSandbox: true`，不使用 `--no-sandbox`、`seccomp=unconfined` 或特权容器。配置依据 [Playwright Docker 文档](https://playwright.dev/docs/docker)。
- 应用 JWT 签名密钥与 Data Protection 密钥目录持久化，不能每次重建容器生成一套。平台、Office 的宿主运行目录和镜像发布包须记录版本及 hash。
- Office `EditorAction` 从本次固定镜像的 `/hosting/discovery` 中取得 xlsx 的 edit action；`PublicWopiBase` 是平台的 HTTPS 根地址。不能从请求参数接受任意 Office 地址。
- `PostMessageOrigin` 指向宿主平台页面，不能误用 WOPI 网关地址。当前单一部署来源必须与 Office 的 `frame_ancestors` 匹配；多个平台页面来源尚需独立验证。
- 当前覆盖文件默认应用内存 1536 MiB、Office 1280 MiB，各 1 CPU，可分别调整。swap 用于开发峰值缓冲，不代表生产容量已通过验收。

启动前确认现有数据库/Redis/RabbitMQ 健康，再执行：

```bash
docker compose --env-file /absolute/private/.env \
  -f deploy/application/compose.yaml -f deploy/application/compose.office.yaml config --quiet
docker compose --env-file /absolute/private/.env \
  -f deploy/application/compose.yaml -f deploy/application/compose.office.yaml up -d app office
```

当前开发发布包含 Playwright 的匹配 Chromium、Node、Paged.js、本项目 renderer 脚本及清单 hash。普通 ASP.NET 运行时镜像仅启动 API，并不自动具备分页能力。可在发布机通过 `.NET SDK /t:PublishContainer` 生成 Linux 自包含 OCI 压缩包，在服务器校验 SHA-256 后 `docker load`，不在云端构建源码。

## 本地调试与云端共用环境

### 本地平台使用云端 Office

云平台可以保留为后续发布入口；本地调试不必把业务 API 改为云平台。编辑链路是本地页面 → 云端 Office → 本地 WOPI。数据库和文件存储继续按开发环境共用，SMB3 可保留，但它不是 Office 回调协议本身的要求。

**云端仅占位时停用其调度消费。** 覆盖文件默认 `CLOUD_SCHEDULER_ENABLED=false`，映射到既有 `SystemData:Scheduler:Enabled` 开关；云端私有配置也保持该值为 `false`。本地调试保留调度工作器。共享数据库也共享 Hangfire 队列，旧云平台会抢走本地提交的正式生成任务，导致预览使用新代码、正式输出使用旧代码；共享文件不能解决代码版本不一致。此配置只停用云平台调度，不影响独立 Office、WOPI、共享文件或平台页面占位。

后续启用云平台正式处理任务前，先排空活动任务，发布匹配的平台代码、迁移与 Renderer，再显式设置 `CLOUD_SCHEDULER_ENABLED=true`。若本地仍连接同一数据库，两边工作器必须使用兼容版本；不能把未经更新的占位进程当作额外工作器。验证正式输出时同时核对任务的 Renderer 执行目录/版本和实际文件内容。

2026-10-30 的旧占位镜像尚不认识工作副本迁移，重启会被“未知或未来迁移”保护拒绝。本次云端私有配置另设 `PlatformStudio:Enabled=false`，只保留基础平台占位入口；本地 Studio 和独立 Office 仍启用。不得删除新迁移或降低校验来启动旧镜像。后续发布匹配镜像后才恢复云端 Studio；共库期间先维持云端调度关闭，直到执行节点版本统一。

模块停用会运行共享控制面的导航种子，并非只影响该节点。本次停用旧云Studio触发的共享菜单撤下，已通过现有受信Studio种子、Identity真实权限核验、控制面CAS事务及缓存失效恢复，九个入口在本地页面核对。该一次性恢复没有删除或修改已应用种子账本。不要把云端旧镜像停用模块当作通用多节点部署方法；后续发布时应在启动新云镜像前恢复与本地一致的Studio模块声明，避免不同节点相互覆盖共享导航。

1. 本机 Tailscale 开启 HTTPS 后，用 `tailscale serve --bg --https=443 --set-path=/office/wopi http://127.0.0.1:<backend-port>/office/wopi`，仅转发 WOPI 路径。不要开放其他本地 API，不启用 Funnel，不跳过 TLS 校验。
2. 本地忽略配置中，`PlatformStudio:PublicOrigin` 为浏览器实际打开的本地前端 HTTPS origin；`Office:PublicWopiBase` 为本机 Tailnet HTTPS 根地址，`Office:EditorAction` 为云 Office discovery 中的 xlsx edit action。其余版本、日志脱敏、准入配置与服务实际发行版一致。
3. 云 Office 的 `OFFICE_DEVELOPMENT_WOPI_ORIGIN` 精确登记本机 Tailnet HTTPS origin；`OFFICE_DEVELOPMENT_FRAME_ORIGIN` 精确登记本地前端 origin。覆盖文件同时保留云平台已有来源，不使用通配 WOPI 主机或通配 iframe 来源。后续正式部署移除开发来源即可。
4. 从云主机和 Office 的实际运行网络验证本机 DNS、可信证书及回调可达。未准入旧进程的 WOPI 返回 503；加载配置后，无票据请求应被 401 拒绝。非 WOPI 路径应为 404，不能把入口能连通当作文件授权已通过。
5. 私有配置以 `reloadOnChange:false` 加载，需由开发者重启自己的后端调试实例；前端无需改成云端 API。重启若使临时 JWT 密钥下的旧登录失效，应重新登录本地页面。
6. 在本地页面另存联调工作簿，真实修改后核对 WOPI 文件读取/锁/保存、内容版本增长、历史版本原文件不变，并关闭重开核对内容。只把这些实际完成的步骤记录为通过。

### 编辑工作区与持续会话

工作簿编辑使用共享 `AppFormDrawer` 的全屏尺寸，填满浏览器内容区域；普通导入、另存、版本历史继续保留原弹窗/抽屉偏好。Office iframe 跟随剩余空间伸缩，不再固定为 `70vh`。

日常保存只更新当前工作副本，手动“保存版本”才增加历史，首次导入与已有历史保留。列表“更多→编辑”修改名称并说明版本策略；“保存版本”可填写说明，编辑器也提供相同入口。内部内容修订继续递增以保证WOPI与并发检查，不能当成用户历史版本数。被替代的工作文件进入持久回收队列，读票据宽限后复用Files引用释放与删除流程；历史和冲突恢复文件不回收。

退出前依据Office修改状态和保存回执：已保存不弹未保存警告，仍有修改提供“保存并离开”，确认成功后才关闭；失败或保存期间再次修改则保留编辑器。升级增加`STU-009-WorkbookWorkingCopy`，不回写旧迁移；本地与云端共库时，后续发布需使用包含该迁移的匹配平台版本。

平台不对持续编辑设置固定的 15 分钟或 8 小时总时长。新会话标记 `slidingSession=true`，提交给 Office 的 `access_token_ttl=0` 表示不显示固定到期倒计时；宿主在后台保持续期。服务端仍保留 15 分钟滑动租约，续期必须通过当前平台登录、会话所属用户、权限、工厂范围及归档状态复核，并更新原身份凭据快照。用户关闭、会话撤销、权限收回或页面长时间失联后，票据不能继续使用；不会创建永久凭据。

`SupportsTokenRefresh` 只控制已验证的 Office 票据轮换协议，不阻止宿主延长租约。不支持轮换时保留现有 token hash，续期返回 `accessToken=null`；前端保留原票据且不重新提交表单、不重载 iframe。支持轮换时仍使用 `Reset_Access_Token`。保存的内容版本仍以服务端确认结果为准，与会话续期分开处理。

Office 租约由当次通过平台身份验证的请求签发，使用自身的短期到期时间，不能截断为打开时 JWT 的剩余时间，否则 JWT 自动刷新前仍会失去写锁。执行身份仍绑定原平台会话及安全版本，回调逐次复核撤销、用户状态与权限；这里不延长平台登录，也不允许过期 Office 会话复活。

WOPI PutFile 在同一次请求内复用 `OwnedFileUploads.WaitForConfirmationAsync` 等待扫描/引用确认，最多等待 30 秒。它与正式报表产物共用同一方法，只重新确认同一上传，不重传文件、不跳过扫描。确认后重新校验写权限与锁，再用原 base 版本 CAS 更新内容版本；确认超时仍返回 503，冲突保留恢复副本。避免正常扫描异步窗口先报 503、稍后保存成功但 Office 仍残留错误提示。

### 共用文件存储

本轮按用户要求共用原开发数据库、Redis DB、RabbitMQ vhost；不使用克隆库作为新环境。两边共用数据库时，实际文件也必须使用同一存储。不能只复制一次文件后继续分别写本地磁盘。

现有 Files 使用文件系统适配器，因此开发联调使用加密 SMB3：

1. 云端 Samba 只提供 `platform-files` 目录，专用账号禁止 shell 登录，禁止匿名访问。强制 SMB3、加密和签名；只允许必要的 Tailnet 客户端。
2. Tailscale 是点对点网卡，本轮 Samba 未识别其 /32 地址。安全做法是让 Samba 仅监听 `127.0.0.1:445`，由系统自带 `systemd-socket-proxyd` 精确绑定服务器的一个 Tailnet IP:445，转发到回环。socket 配置 `IPAddressDeny=any` 和精确的客户端/服务端 `/32` 白名单。不要为修复识别问题扩大接口网段或监听 `0.0.0.0`。
3. Windows 使用 `\\<server>.<tailnet>.ts.net\platform-files`，凭据保存在当前 Windows 用户的凭据管理器；私有配置 `SystemData:FileStorageRoot` 指向该 UNC 路径。已启动的调试进程必须重启后加载。
4. 云端也通过 CIFS 访问同一 SMB 共享，不能直接访问 Samba 的底层目录：跨 Windows SMB 与 Linux 本地磁盘的独占锁不互通。使用 `vers=3.1.1,seal,uid=1000,gid=1000,forceuid,forcegid,file_mode=0600,dir_mode=0700,cache=none`，不使用 `nobrl`。凭据文件仅 root 可读。
5. 挂载点通过 `/etc/fstab` 的 `_netdev,x-systemd.automount,nofail,x-systemd.requires=platform-files-tailnet.socket` 管理。`APPLICATION_FILES_ROOT` 必须指向该 CIFS 挂载点，Compose 将其映射至 `/data/files`。重建应用前用 `mountpoint` 核对，避免错误绑定到底层空目录。
6. 初次迁移逐文件核对 SHA-256：相同文件跳过、缺少的复制、冲突的停止处理，不覆盖。保留原文件及配置备份。禁止上传中进行最终切换。

验收至少核对正常读写、SMB 实际协商加密/签名，以及双向互斥：Windows 独占文件时 Linux CIFS 打开失败，Linux 持独占锁时 Windows 打开返回共享冲突。仅测试两边能读同一文件不足以证明上传并发安全。

## 检查、恢复和边界

- `/health/ready`、Office capabilities/discovery、真实 xlsx 打开、修改、保存、关闭重开与不可变版本记录分别核对。接口准入和 discovery 成功不能替代真实编辑验证。
- 开发配置的扫描 bypass 会使 readiness 的文件扫描项显示 Degraded，不代表真实病毒扫描通过。生产部署配置可信扫描器后再验收，不能把 bypass 搬入生产。
- Office access/protocol/debug 日志关闭，避免 WOPI bearer、查询串或文件内容进入日志。排障应先收集脱敏状态、状态码、请求类型和资源占用，不导出原始 bearer URL。
- 回滚先停止本轮应用写入，保留数据库和文件备份；恢复上一不可变镜像及其匹配 renderer 清单。共享文件不跟随容器删除，不使用 `down -v`。本地恢复旧路径前必须先同步新版本，不能直接切回旧文件快照。
- SMB 凭据丢失时由管理员重置专用账号并同步 Windows/CIFS，不更改平台用户密码；挂载恢复后再重启本轮应用。服务器重启后的自动挂载/应用启动仍应独立验收。
- Collabora CODE 是开发发行版，本次部署不作为生产 SLA 或并发容量证明。生产选择受支持的 Office 服务，WOPI 接口保持独立。

组件许可仍须履行各自义务。独立服务边界可保留平台的开源协议选择空间，但不免除组件许可、浏览器资源分发及声明保留义务。依据：[Collabora COPYING](https://github.com/CollaboraOnline/online/blob/main/COPYING)、[Mozilla MPL FAQ](https://www.mozilla.org/en-US/MPL/2.0/FAQ/)、[CODE 官方说明](https://www.collaboraonline.com/code/)。SMB 设置以 [Samba 官方手册](https://www.samba.org/samba/docs/current/man-html/smb.conf.5.html) 为准。
