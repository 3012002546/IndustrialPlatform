# PF-06A 自测与交接证据

## 2026-09-30 主控 SQL Server 收尾（最新状态）

SQL Server 适配代码已覆盖全部 109 张服务表、1777 列及迁移/种子/仓储/拓扑/目标适配器。2019 隔离库实测通过；四服务初始化 Ready、重复应用不再迁移、全部外键可信、1886 条表/列 MS_Description 完整。正常运行配置仍为 PostgreSQL；没有替用户启动服务或自动切换运行库。

实测业务包含：用户中文/UTC/并发拒绝/规范登录名去重；ReferenceData 七模块真实创建/发布与参数 JSON、精确 decimal 和 Int64；编码 8 并发分配及 6 次同请求重放；查询预算 10 路竞争不越过 200；消息隐藏幂等；任务只被一个工作器领取；用户分配/独立初始化/目标迁移锁；文件有效引用/保全阻止删除；迁移内容漂移拒绝、失败事务回滚；同版本 SQL 种子三路竞争只执行一次。

表、字段类型、默认、索引、FK/CHECK 及中文含义见[全表字段字典](../implementation/details/PF06A-SQLServer全表字段字典.md)；运行选择见[09A §9.2～9.3](../implementation/09A-Industrial%20Platform终端运行时与客户端打包开发实施方案.md#92-什么时候使用哪种数据库)。

当前结论是 **2019 适配代码与已列自动化实测完成，可进入 IDE 业务联调**。2022 未提供实例，NOT RUN；创建登录/角色权限、备份恢复、独立四物理库和完整客户端联合验收未实机完成。不能据此宣布两引擎生产门禁全部通过。无需另开发一套 2022 Provider。

验证命令/统计（修复后新鲜产物，均 exit code 0）：

```powershell
dotnet build src/backend/IndustrialPlatform.slnx --configuration Release -p:PF06A_SQLSERVER_ENABLED=1 --verbosity quiet
$env:PF06A_SQLSERVER_ACCEPTANCE='1'
dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build -m:1 --verbosity minimal
```

Release 编译 0 警告、0 错误；全量测试 1913 通过、0 失败、7 跳过。项目通过数：BuildingBlocks 176、Collaboration 168、Gateway 14、Identity 621、Integration 13、ReferenceData 262、SystemData 636、UnifiedHost 23。Integration 的 7 项跳过为未启用的外部 PostgreSQL/Redis/RabbitMQ 验收，不计为通过；本轮 SQL Server 2019 真库测试已启用并通过。

本轮全量回归暴露并修复构造阶段提前访问 SqlSugarScope 的上下文共享：最小八路操作修复前共用 1 个 ContextID，修复后独立，超时保留；与映射集合并发修改、宿主查询 500 的日志一致。最终统计必须取该修复后的新鲜 Release，早期失败不计通过。

历史处理：本轮初始空库测试暴露 SqlSugar 两个同名 SchemaMigrationRecord 串表。现已重命名 SystemData 类型并显式 AS；仅按已确认完整一致条件移除本轮误入 Identity 的 58 条 SystemData 记录，保留正确账本、checksum 和业务数据。ReferenceData 长度 CHECK 错误追加 012 修复，中文说明追加 013；Identity/SystemData 描述 v2 修正安全 nonce 归属，旧已应用描述 ID 保留。历史业务迁移未重写。

以下记录为历史快照，早期“只能连接、尚不能迁移”的判断已由本节覆盖；未测边界仍有效。


## SQL Server 全表兼容核查（2026-09-30，当前）

用户要求所有表兼容后再明确数据库使用策略。已核查 109 个服务/表目标（Identity 23、SystemData 42、ReferenceData 24、Collaboration 20），执行 259 次 DDL 方言生成/合同核对；其中 SQL Server 检查 93 个，73 个拒绝、7 个返回明显非 SQL Server 方言、10 个没有 SQL Server 合同、3 个仅生成基线/no-op 字符串，均不能作为建表通过。真实 2019 经项目 SqlClient 和 SqlSugar 上下文只读查询通过，ReferenceData `ApplyAsync` 明确拒绝未适配的 SQL Server 迁移；前后用户表数均为 0。完整表单/入口结果见[全表报告](PF-06A-SQL2019-compatibility-check.md)。新鲜 Release 构建退出 0（0 警告/错误），随后完整后端测试退出 0：1906 通过、7 外部依赖用例跳过、0 失败；不是 SQL Server 全表验收通过。

结论：当前 SQL Server 只能用于隔离兼容开发与只读测试，不能切换平台业务运行库。配置解析确认现有运行 Provider 仍是 PostgreSQL，验收连接串不会自动切库。SQLite 供显式隔离测试，正式环境按部署选择一个通过全表/业务/拓扑验收的关系库引擎，不按页面或某张表选库、不自动迁移数据。配置说明已更新[数据库拓扑与本地配置 §3.4](../../src/开发注意事项-数据库拓扑与本地配置.md#34-什么时候用什么数据库)。

## SQL Server 2019 隔离库连通性核查（2026-09-30）

用户已创建 `industrial_platform_sql2019`，并在 Git 忽略的 `src/backend/appsettings.Development.local.json` 配置 `SqlServerAcceptance:2019:ConnectionString`。连接串有服务器、库名、SQL 用户、密码、加密与证书信任设置，库名准确且没有示例占位值；运行时 `RemoteDevelopment:DatabaseProvider` 仍沿用 PostgreSQL，未切换现有服务。首次检查时目标私网主机可 ping 通但 TCP 1433 不通，SQL Server Browser UDP 1434 未响应；用户调整后 1433 可达。随后 `Microsoft.Data.SqlClient 5.2.2` 按原连接串连接时证书链未受信任（错误 `-2146893019`）；仅在临时只读探针中设 `TrustServerCertificate=true` 后成功。最终把这一设置写入 Git 忽略的 **2019 验收连接串**，保留 `Encrypt=True`，运行时配置不变；不覆盖公开 example 的安全默认值。

使用保存后的连接串、不加临时覆盖，执行只读查询通过：`ProductMajorVersion=15`、`ProductVersion=15.0.2000.5`、库名 `industrial_platform_sql2019`、兼容级别 `150`、排序规则 `SQL_Latin1_General_CP1_CI_AS`、用户表数 `0`、当前账号 `CREATE TABLE` 权限为 `1`。这只覆盖 SQL-A01 的基本连通与实例身份输入；证书未受信任的拒绝路径已观察到，但权限不足场景、迁移、结构、并发和业务真值仍未执行，不能宣布 SQL Server 支持已完成。全程未建表、未迁移、未写入业务数据；2022 环境不存在。

## 阶段提交前核验（2026-09-30）

本次对既有 PF-06A 工作树做新鲜验证：`dotnet build src/backend/IndustrialPlatform.slnx --configuration Release` 成功（0 警告、0 错误）；其后 `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build` 退出码 0，8 个测试程序集共 1899 通过、7 跳过、0 失败。前端用临时 TypeScript 缓存分别检查 app/node 配置，通过；Vite 8.2.1 用 `--configLoader runner` 输出到临时目录构建通过；同配置下 Vitest 146 文件／1088 项通过。Electron 从当前源码编译到临时目录，`runtimeUpdater` 3/3 通过。常规前端 `node_modules/.tmp`／`.vite-temp` 和 Electron `dist` 写入遇 EPERM，本次没有清理或结束用户进程。

Android 当前源码尝试离线 `:app:assembleDebug`，先遇 Gradle 工程缓存写锁；转用临时缓存后，Android Gradle Plugin 初始化 `AndroidLocationsBuildService` 仍失败，故**本次未取得新鲜 APK 编译通过证据**。下节 2026-09-29 的调试 APK 构建、hash 与 Android 10 真机结果仍有效，但不能把它写成本次重新构建。当前阶段只收束手机联调；旧 WebView、正式无 Vite 包与更新渠道、001/005 余项及 009 SQL Server 继续按[当前工作包](../tasks/active/PF-06A.md)推进。

## Android 协作媒体真机复测（2026-09-29，当前）

设备为 Vivo V1924A / Android 10 / WebView 153，Windows Electron 与手机 App 分别使用不同账号进入同一会话。Windows→手机共享画面用户确认正常；手机→Windows 采用系统 MediaProjection 授权、前台采屏服务、WebView Canvas 视频轨与原 PF06 单 PeerConnection，用户确认能看到手机画面，切到手机桌面/其他 App 后持续更新。画质调整后实际视频轨为 960×1902，用户确认文字清晰。手机停止共享复测后，页面为 `screen=Idle`、本地视频轨为空，ADB 未发现 App 采屏服务或活动 MediaProjection。

语音首次报 `MEDIA_MIC_PERMISSION_DENIED`：Android `RECORD_AUDIO` 已授予，但 Capacitor WebView 音频授权还依赖清单中的 `MODIFY_AUDIO_SETTINGS`；补齐并覆盖安装后，手机 `getUserMedia` 获得 1 条有效音频轨。真实双端通话中电脑能听到手机；手机端已接收远端音频并播放（WebRTC `connected`、远端音轨 live、音频包与能量非零），调高手机通话音量后用户确认能听到电脑。此前屏幕停止仅调用 `track.stop()`，系统采屏服务仍在；已改为显式释放原生服务并完成上述停止复测。

调试 APK 位于 `D:/Code/Industrial Platform/AndroidApk/IndustrialPlatform-live-debug.apk`，SHA-256 为 `C3DAE13A8B21D3ECAAF3FED7744025E24C86EEDD05CDFC7DA07C2E5EA4285221`，与原调试版签名一致。Gradle `:app:assembleDebug` 通过；前端 `vue-tsc`、相关 ESLint 与协作媒体 3 个测试文件 44 项通过。本轮未启动或停止用户在 VS Code 中运行的服务。证据限于该 Android 10 真机与开发签名局域网调试包；Android 14+、iOS、正式签名/更新渠道另行验收。

## Android 真机扫码与无线调试（2026-09-29，先前快照）

本节为当前实测结果；以下较早的“无 SDK/ADB/APK/真机”描述是当时快照，不代表现状。测试设备为 Vivo V1924A、Android 10；Android System WebView 已由 83 更新到 153。调试 APK 使用局域网 Vite HTTPS 页面，用户已安装开发 CA；登录页和真实账号登录均已在 App 中完成。Android 原生包默认进入 PDA 路由的源码已修正；账号可进入 `/pda/terminal/scanner-test`。本轮未启动或停止用户在 VS Code 中管理的 UnifiedHost/Vite 服务。

拍照扫码首次失败的原因有两项：手机未安装 Google Barcode Scanner 模块，以及 Capacitor 插件扫描结果字段为 `rawValue`，原桥接误读为 `value`。当前手机的模块已安装；桥接改为优先取 `rawValue`、保留前导零，缺失时才用 `displayValue`，并补齐首次安装检查、等待与错误提示。用户在修正后使用真实手机扫描二维码，确认下方结果区显示二维码原文。相关扫码测试 11/11、Vue 类型检查、显式 `VITE_DEPLOYMENT_ENVIRONMENT=DEV` 的前端构建均通过。AndroidManifest 增加扫码模块预下载声明；本节测试当时尚未因该清单改动重新打包，后来已按本文件最新一节重建并安装调试 APK。

用户拔除 USB 后，`adb devices -l` 仅显示 `192.168.3.153:5555 device`，并通过该无线目标执行 `getprop ro.build.version.release` 返回 `10`；无线 ADB 已实际连通。无线执行 `am force-stop`/`am start` 后，WebView 调试目标显示 `PDA 首页`、路径 `/pda/home`，证明本次冷启动恢复了当前登录会话并进入正确终端。电脑前端 `192.168.3.140:5173` 与本机后端 `127.0.0.1:5041` 端口均在监听。上述 IP 为本次局域网调试值，不作为发布配置。PDA 厂商广播不在本次交付；生产签名 APK、正式更新包/重登录强制更新仍需独立实测，不能以本次 Vite 热加载替代。

## Android 手机联调前置核查（2026-09-22）

用户手机与开发机在同一 Wi-Fi、无 USB 数据线。Android Studio 非 APK 构建的必要条件：工程已有 Gradle wrapper，开发机现有 `C:/Program Files/Android/openjdk/jdk-21.0.8` 可运行；此前 PATH 检查误判为无 JDK。Google 官方 Android 命令行工具已下载并核对 SHA-256，解压在仓库外的 `开发辅助文件/AndroidSdk`，`sdkmanager --version` 为 22.0。API 36、Build Tools 35、Platform Tools 安装时出现 Android SDK License Agreement，用户尚未确认接受，管理器自动跳过；因此目前仍无 SDK 平台/ADB/APK。Gradle 8.14.3 官方分发下载在约 37% 处低速中止，保留部分文件，未称构建通过。

Visual Studio 的 Debug UnifiedHost 在 `localhost:5041` 监听，`/health/live` 为 200。当前 Wi-Fi 为 `192.168.2.7`；仓库脚本复用既有开发 CA 更新了 HTTPS 服务器证书 SAN，前端局域网 HTTPS `https://192.168.2.7:5173/mobile` 与 `/_backend/health/live` 均返回 200，登录预检允许 `Origin: http://localhost`。用户已在手机浏览器安装该开发 CA 并确认看到登录页；账号登录结果未回报，浏览器验证不代表 Android APK/原生相机通过。单独提供 CA 公钥的临时 HTTP 下载服务在用户安装后已停止。Android debug source set 增加仅调试构建信任用户 CA 的网络配置，尚待 APK 编译验证；release 不引用此配置。

## Windows 联调时的 UnifiedHost 启动修复（2026-09-22）

实际开发库复现 `UnifiedHost systemdata 初始化验证未就绪`。只输出布尔值和内置资源ID/版本的临时诊断显示：`seed=True`、SystemData manifest/receipt 均为 v4、ReferenceData/Collaboration 已就绪，但 19 个既有 SystemData 页面资源仍为 v3；临时诊断代码随后撤除。原因是当前清单升级至 v4 时，负责重绑可信内置资源的 `SDM-018` 种子仍为 v2，旧库已有 v2 账本导致它跳过。将该种子版本升为 v3；真实 SQLite 回归构造 v3 资源及已执行的 v2 账本，修复前失败、修复后通过，并验证旧账本和租户自定义资源均保留。

最终新鲜 `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release` 退出码 0、0 警告/0 错误；随后 `dotnet test ... --configuration Release --no-build` 退出码 0，1,899 通过、7 项环境门控跳过、0 失败。使用当前 Development 数据库运行 Release UnifiedHost 两次，均完成模块初始化并监听 `http://localhost:5041`；`/health/live` 为 200。单独的 `/health/ready` 仍因 `referencedata.initialization` 三秒检查超时返回 503，未把总 readiness 记为通过。

为继续 Electron 本机登录，实测原开发 CORS 对 `Origin: app://app` 的登录预检返回 204 却没有 `Access-Control-Allow-Origin`，日志明确拒绝该来源。开发环境允许来源增加 `app://app`；Vite 与 Electron 两种来源的预检回归先 1/2 失败、修复后 2/2 通过。最终运行中的 UnifiedHost 上再次实测预检 204，`Access-Control-Allow-Origin: app://app`。Windows 客户端登录与扫码页面仍待用户账号现场操作，不记录为已通过。

## Windows 本机安装与 Web 更新实测（2026-09-22）

用户现场确认隔离安装的 Windows 客户端可见“用户名 / 密码 / 登录”页。继续测试时，按仓库 `http` 启动配置运行 Development UnifiedHost：项目编译成功、Redis 已连接，但在 SystemData 初始化验证时报“控制面种子或引导事实尚未完成”并退出（exit 1），5041未监听。`dev.ps1 status -SkipInfrastructure`亦显示UnifiedHost STOPPED；因此未提交账号密码，也未把登录后扫码记为通过。待核对用户平时可用的后端启动入口或修复该初始化事实后继续。

本轮在当前工作树发现并修复两个实际阻断：未设置`VITE_DEPLOYMENT_ENVIRONMENT`时，原安装包启动后因运行配置校验而白屏；现在Vite在构建阶段直接报缺项，验证包以`DEV/http/http://localhost:5041`显式构建。Electron原来从`app://app/index.html`启动，Web更新重载后会进入“页面不存在”；现在从`app://app/`启动，并对无扩展名的站内路径提供SPA入口，业务路由刷新可恢复。

使用NSIS安装包静默安装到隔离临时目录，使用独立`userData`启动；正常权限下登录页和Electron桥接能力可读。最终测试安装包SHA-256为`CDA4472A5AE41DDA964E2ED2A2BDA831D737D19BA44A4FEE274BC0336C807BE0`，`NotSigned`，只用于本机测试。Electron `pnpm run test`：3/3通过；前端以显式DEV配置`pnpm run build`通过；缺少部署环境变量时Vite构建立刻报错。Android `cap sync android`已重新同步该Web资源，仍不等于APK或真机验证。

在隔离安装资源中注入临时本机Ed25519公钥及`file:`清单，测试私钥只在生成时保存在进程内；W2 Web包版本`0.1.1`、SHA-256校验后由客户端自动下载、激活并完成健康确认。实测宿主能力返回`webVersion=0.1.1`，`active-web-slot.json`和`last-good-web-slot.json`均指向W2，`pending-web-activation.json`已清除。更新后登录页可见；登录页刷新仍可见；直接进入`/pc/terminal/scanner-test`得到登录页并保留目标路径。这只覆盖隔离本机W1→W2与路由/健康链路，未覆盖真实发布服务、完整登录后扫码、原生安装包升级或Android真机。

本机没有运行5041后端，也没有Android SDK、ADB或APK；因此Windows登录后扫码、Android安装/相机扫码及两端真实业务登录仍待执行。正式更新策略、公钥、下载源与代码签名证书也未注入正式包。

## 主控接管复核（2026-09-21，当前 `develop` 工作树）

本节记录开发交接后的新证据；下方“恢复执行记录”和原 `pf-06a` 工作树记录保留为历史快照，其测试数、文件哈希、安装包哈希及当时的 EPERM 结论不代表当前源码。当前改动仍未 commit/push，`develop` 比 `origin/develop` 领先 3 个既有提交。

**用户确认的交付上限：** Android只验用户手机的输入与拍照扫码，PDA厂商广播、SDK/Intent接收器和S06真机不属于本次交付。更新器只补齐当前包、一个lastGood、一个暂存包、固定60秒健康确认及必要的签名/hash/兼容校验；不增加策略中心、历史/审计平台、多级回滚、灰度编排、后台预下载、断点续传或独立安全下限治理。

**本轮纠偏：** 实测旧 JS 在宿主切换 Web slot 后仍可立即把宿主新版本号当成健康证据。现在旧JS只负责触发一次重载；新页面启动时向宿主确认其构建版本，版本缺失或不符均拒绝。Electron/Android宿主持久化一个待确认activation和60秒截止时间，成功后将当前包记为lastGood；超时恢复lastGood或内置包，并记录失败releaseNId，防止同一坏发布自动循环。Electron可变slot从安装目录迁到可写的`userData/runtime`，受信策略仍从安装资源读取。Electron preload、Capacitor桥与Android下载流透传真实字节进度。

**仍待真实环境：** Vite现在把同一`VITE_WEB_VERSION`同时写入JS和`runtime-version.json`；已用临时`1.0.1`构建证明两处一致。Electron单元测试覆盖W2激活、错误版本拒绝、健康确认、W3超时恢复W2和同一失败发布拒绝；Android实现同一最小状态，但本机缺Android SDK/adb/手机，Java尚未经过APK编译、安装与60秒恢复实测。生产发布清单、公钥、下载源和签名证书未注入；Windows/Android真实W1→W2及安装包升级仍未执行，因此005保持未完成。

| 当前源码验证 | 结果与边界 |
| --- | --- |
| 前端 `pnpm run build` | exit 0；Vue 类型检查及 Vite 构建，2400 modules |
| 临时 `VITE_WEB_VERSION=1.0.1` Vite构建 | exit 0；输出`runtime-version.json=1.0.1`，构建后的JS同样含`1.0.1`，证明发布版本输入一致 |
| 前端 `vitest run --configLoader runner` | 146 files / 1084 tests passed，exit 0，含旧JS健康误报、宿主健康拒绝和Capacitor进度回归 |
| Electron `pnpm run test` | exit 0；TypeScript 构建，3 个 updater 测试通过 |
| Electron `pnpm run package` | exit 0；当前源码Windows x64 NSIS安装包114,635,908 bytes，SHA-256 `86338A4F4C2BC7EF72303A774BB7EC399E329573B95C134E3A826049ED34ED72`；`NotSigned`，仅测试包 |
| 最新NSIS `/S /D=<isolated temp>` | exit 0；隔离安装目录存在`Industrial Platform.exe`，安装资源`runtime-version.json=0.1.0` |
| Android `cap sync android` | exit 0；复制最新前端产物并发现 5 个插件；不等于 APK 编译或真机验收 |
| `git diff --check` | exit 0；仅换行提示；`src/backend` 本轮未改 |

本机仍缺 Android SDK、`adb` 和 Android 手机接入，Gradle 分发下载未完成；没有 APK、安装、拍照扫码及冷启动实测。发布清单、公钥、真实下载源和签名证书仍未注入。009 只有[SQL 设计草案](PF-06A-SQL-design-draft.md)，主控复核确认逐表逐字段字典与四项合同尚未冻结，G06A-SQL 继续 OPEN；无 SQL Server 2019/2022 实例和真实引擎结果，不能称作数据库支持已完成。

### 历史交接元数据（前一工作树）

日期：2026-09-20
工作树：`D:/Code/Industrial Platform/IndustrialPlatform-PF-06A`
分支：`pf-06a`
开发交接时 `HEAD`：`e64f59c74812b5663ffd80612e6d0543cad5aca1`
角色：Luna 双角色接管；本文件完成后停止继续写入，交由主控接手。

## 恢复执行记录（当前主控工作树）

本节是模型强度调整后在主控工作树的追加证据；上方元数据和下方原交接内容属于前一工作树的历史记录，不覆盖、不重写。

- 当前工作树：`D:/Code/Industrial Platform/IndustrialPlatform`
- 当前分支：`develop`
- 执行基线：`02f11c6a17673d1040b475616c3574ffa66c1d0c`
- 既有文档 WIP：保留 5 个文件（`docs/implementation/09A-...md`、`docs/status/CURRENT.md`、`docs/tasks/active/PF-06A.md`、`docs/tasks/pending/PF-06A.md`、`docs/tasks/pending/README.md`），未回滚、未覆盖。
- 本轮仍未 commit、未 push；不声称 PF-06A-001/005/009 整包完成。

### 本轮实现边界（持续更新）

- 共享运行时：`runtimeBridge.ts`、`runtimeBootstrap.ts`、`runtimeStartupGate.ts`、`capacitorBridge.ts` 已形成统一能力快照、Vue 挂载前可见的登录前启动更新门禁、下载进度、失败重试/退出、生命周期、文件、通知、诊断、打印和媒体边界；Electron IPC 与 Android 插件提供真正退出动作，原生会话凭据通过宿主安全存储，Web 保留 sessionStorage/memory 降级。
- Windows Electron：`src/clients/electron` 固定 `app://` 前端加载、context isolation、sandbox、nodeIntegration=false、单实例/托盘、加密 auth 文件、受限 IPC、显式屏幕源选择；`runtimeUpdater.ts` 已实现 Ed25519 清单校验、artifact SHA-256、W1/W2 slot 下载/切换/回滚和 Windows installer 打开边界。
- Android Capacitor：`src/clients/pda` 含真实 `android/` 工程和 `IndustrialRuntime` 插件；Keystore AES/GCM auth、稳定 subject epoch、相机/麦克风权限、文件选择/保存、系统设置、系统打印取消回执、本地通知、APK FileProvider 安装和私有 Web Bundle slot 已写入。本次只交付Android手机输入/拍照扫码；PDA厂商广播明确范围外。
- 更新规则：共享启动流程执行 check → download → verify → activate → healthy；配置了发布策略的 Electron/Android 渠道严格 fail-closed；开发包未注入策略时明确显示更新能力 Degraded 但允许应用启动，任何实际更新调用仍 fail-closed，不绕过验签、不伪造成功。
- SQL：已新增 `docs/evidence/PF-06A-SQL-design-draft.md`，完成 D02/D03/D04/D06 逐服务设计输入；G06A-SQL 仍 OPEN，未修改 SQL 生产代码、未运行迁移。

### 新鲜验证矩阵

| 命令 | 结果 |
|---|---|
| `vue-tsc --noEmit -p tsconfig.app.json --tsBuildInfoFile %TEMP%\\industrial-platform-app.tsbuildinfo`（`src/frontend`） | exit 0；使用临时 build-info 路径，避开本地 `node_modules/.tmp` EPERM |
| `vitest run --configLoader runner`（`src/frontend`） | 145 files / 1082 tests passed，exit 0；含启动门禁进度、重试、退出测试 |
| `vite build --configLoader runner --outDir %TEMP%\\industrial-platform-frontend-dist-final`（`src/frontend`） | 2400 modules transformed，exit 0；临时输出目录；仅有 chunk >500 kB 提示 |
| `pnpm exec cap sync android`（`src/clients/pda`） | exit 0；复制前端产物并发现 5 个 Capacitor Android plugins |
| `tsc -p tsconfig.json --outDir %TEMP%\\industrial-platform-electron-ts-out`（`src/clients/electron`） | exit 0；当前源码编译通过，写入临时输出目录 |
| `pnpm run build`（`src/clients/electron`） | 当前工作树写 `dist/*.js` 遇 EPERM，未计 PASS；不是代码诊断结果 |
| `RUNTIME_UPDATER_DIST=<temp>/runtimeUpdater.js NODE_PATH=node_modules node --test tests/*.test.mjs`（`src/clients/electron`） | 3 tests passed / 0 failed，exit 0；含重复 key/签名清单拒绝篡改、W2 下载/哈希/解包/激活 |
| `pnpm package`（`src/clients/electron`，启动门禁 UI 补丁后的源码） | exit 0；NSIS x64 installer 114,632,614 bytes；SHA-256 `1D667BB22AD894C7EF6C44A13B4F6C1B28B52B4529B1D64AE5122865F46C2C31`；`Get-AuthenticodeSignature=NotSigned`（测试包）；该包早于本轮原生退出桥补丁 |
| `pnpm.cmd package`（`src/clients/electron`，含原生退出桥的当前源码） | exit 1；`tsc` 写入现有 `dist/main.js`、`preload.js`、`runtimeUpdater.js` 遇 EPERM，未进入 electron-builder；未将旧 installer 计作当前源码包 |
| Electron NSIS `/S /D=<isolated test-install>` | exit 0；隔离目录成功落盘 `Industrial Platform.exe` 等安装文件 |
| `gradlew.bat assembleDebug`（`src/clients/pda/android`，JAVA_HOME 为 JDK 21.0.8） | 未完成；Gradle 8.14.3 分发下载持续无进展后中止，exit 1；不是编译通过 |
| `git -c safe.directory='D:/Code/Industrial Platform/IndustrialPlatform' diff --check` | exit 0；仅有 LF/CRLF 转换提示 |

### 真实环境边界

- 已找到 JDK：`C:\Program Files\Android\openjdk\jdk-21.0.8`；未找到 Android SDK、`adb` 或可用 Android 真机，因此没有 APK、安装、相机扫码、冷启动/重新登录门禁和设备证据。
- Gradle wrapper 下载 `gradle-8.14.3-all.zip` 在本机网络环境持续停留，未进入 Android 编译阶段；不能把 Capacitor 工程生成或 `cap sync` 结果称为 APK 构建通过。
- Electron 已有启动门禁 UI 补丁后的 Windows x64 NSIS 包和隔离静默安装证据；包大小为 114,632,614 bytes，SHA-256 为 `1D667BB22AD894C7EF6C44A13B4F6C1B28B52B4529B1D64AE5122865F46C2C31`，但该包早于本轮原生退出桥补丁；当前源码重建被现有 `dist` 文件 EPERM 锁住，不能把旧包称作当前源码包。该 installer `Get-AuthenticodeSignature=NotSigned`，未配置真实生产证书/签名清单，不能称作生产签名版本；未做真实 Windows 登录/扫码。
- Electron/Android 的生产 release channel、可信公钥、签名 manifest、真实下载源仍待发布环境注入；本地 Electron 测试只验证 file URL 的隔离 W2 流程，不等同生产 HTTPS 渠道验收。
- Android 仍无 SDK/adb/真机；未得到 APK、安装、相机扫码、冷启动/重新登录门禁和设备证据。
- PDA厂商广播不属于本次交付，不作为未验证阻塞项；以后有真实PDA时另立任务。

### SQL-009 只读盘点与待确认设计

只读检查确认当前生产数据库抽象仍只有 SQLite/PostgreSQL：

- `src/backend/src/BuildingBlocks/IndustrialPlatform.SharedKernel/Topology/DatabaseProvider.cs` 只定义 `Sqlite`、`PostgreSQL`；`DatabaseTopologyResolver.cs` 只解析这两个物理目标。
- `src/backend/src/BuildingBlocks/IndustrialPlatform.Infrastructure/Database/SqlSugarOptions.cs` 默认 `DbType.PostgreSQL`；`SqlSugarDbContext.cs` 和 `DevelopmentInfrastructureConfiguration.cs` 只配置 PostgreSQL/SQLite 分支。
- `SystemDataMigrationHelpers.TypeWords()` 只提供 SQLite 与 PostgreSQL 类型词；`SystemDataSchemaMigrations.cs` 含大量 `CREATE TABLE`、`ALTER TABLE`、部分唯一索引和 PostgreSQL native types；`SchemaMigrationRunner.cs`、`SchemaPhysicalDriftGuard.cs` 通过 SQLite pragma 或 PostgreSQL `information_schema`/`pg_indexes`/`current_schema()` 做账本和漂移检查。
- 当前测试/本地配置走 SQLite 或 PostgreSQL；没有 SQL Server 2019/2022 实例、连接凭据、迁移输出或真实 schema diff 可运行。

待主机确认后再实施的设计边界：新增 `DatabaseProvider.SqlServer` 与 SqlSugar `DbType.SqlServer` 配置契约；为 GUID、时间、布尔、BIGINT、默认值、部分唯一索引/软删除和 schema 查询分别定义 SQL Server 语义；将 PostgreSQL-only advisory lock、`current_schema()`、`pg_indexes`、`ADD COLUMN IF NOT EXISTS`、native types 替换为 SQL Server 等价适配；逐库执行 Identity/SystemData/ReferenceData/Collaboration 的迁移与回滚演练，并保留同一拓扑 fail-closed 规则。本轮不落地这些生产代码。

### 当前恢复清单 SHA-256

以下清单覆盖本轮新增或修改的运行时/客户端关键源文件、锁文件、根 `.gitignore` 与 SQL 设计稿；构建产物、`node_modules`、`bin/`、`obj/`、前端 `dist/` 不纳入。本证据文件自身不纳入，避免自引用。

<!-- CURRENT_HASHES_START -->
```text
.gitignore B0850C6714636AFDBA35B015BCE07CDA94A6EC77012D5041207B41970C23DF86
docs/evidence/PF-06A-SQL-design-draft.md 2E25FC06135BF0C01535D05DC28388A30D7E9337D2B9DB801951B18FEC2EF7E7
src/frontend/src/auth/sessionStore.ts 98C5566399C0B78CBE77B6ED6ED98073EC0FFD31266BFB16F19F991EA9F58CAC
src/frontend/src/main.ts A01F2C5EFA3F1F8F4976E96E85377A347845B228DBB9A6094C58B304F0EEBBEE
src/frontend/src/platform/runtime/scannerRuntime.ts C2A73F25182D52E7451A7615417856D8EC3A6549D1F1EFB677B71B7A5767A81F
src/frontend/src/platform/runtime/capacitorBridge.ts B0CBA44614729651BE5C59217DEEE400004AAB6520913A6C8B18B88514477049
src/frontend/src/platform/runtime/runtimeBootstrap.ts DA5404B8DEFCCC6ABD8FFC34EFE629414CEF5FB9E1CBD713FBFCC7F7C1873CDC
src/frontend/src/platform/runtime/runtimeStartupGate.ts 89F441D8C064A0519C9A4355A5E1968A1A52341C348A982F20D0B867E026279D
src/frontend/src/platform/runtime/runtimeBridge.ts 28ED30D638DD8CF547DE84BF2F0021D4E81C6D22840BEF7331537C64826A842C
src/frontend/src/stores/authStore.ts DFF7292AF4A6D2552F5676A217EF449680996CEF797DFE8F03C3C1A7B9AA8F21
src/frontend/tests/unit/nativeAuthStorage.spec.ts 58C859291CE97288167FA194FC23B18DD5229845230C96E11D25E46981D52A1B
src/frontend/tests/unit/runtimeBootstrap.spec.ts 074E63CD38903BE5EED56538FCF454CC96A8E86F5AC379905F1BC6F204E3B0DF
src/frontend/tests/unit/runtimeStartupGate.spec.ts 714780E86852CBFA06FC6CB7ED66E596C7869D2A08EDB2EE4E22DC216F682713
src/frontend/tests/unit/runtimeBridge.spec.ts 704D1839745F2E66AE2D0CFED2548B0B568CA41CFCDB50E6998B9A4719A4D090
src/clients/electron/package.json 35D9997A965E1787F8FB0A8B9FE87C64D96D5C349572929A0C4B9A7139CA2BB8
src/clients/electron/pnpm-lock.yaml DD91413901AF481BC340661FE00AAC5055B319668FF717791A375C0CDB4E8D3C
src/clients/electron/tsconfig.json F7B7BF5FC2E44223C6EF37B4A174C127BF7DE340B2A92D3807F92A6AF81EAF91
src/clients/electron/electron-builder.yml 53C79EF4CDE640B6356202128DEEF985355F2EBFA13CDDB4F6DF33FDB7A72EDD
src/clients/electron/src/main.ts 52B242CA65EC74B78E5B23DDED9A586F168A132E21B8008E0A699D902C02C526
src/clients/electron/src/preload.ts 50E71538F560D5E9A9A5B7DD2A901DCB408C51B37BC40E121965969C0481E6CF
src/clients/electron/src/runtimeUpdater.ts 629095398B4C5DE65E158B74AB4646037DE60E30C7E6EE7D87BC9C4A01AE1A57
src/clients/electron/tests/runtimeUpdater.test.mjs BAD46AB0EDA21E240B222A1505A38584C580E3B0B539779BB2BD9700D63CB154
src/clients/electron/resources/runtime-policy.example.json C821AE0A6FAB6A02E592A07585158F1741186EEF318913F0183B883CB6682869
src/clients/pda/package.json A2412D7657A3D0964E9BD7DD4B0661C6F3062181A6D8F78C2F0F6E6AECDFFE85
src/clients/pda/pnpm-lock.yaml C63BBE88D085AD8351424AF6A9058718D34413970C9112890E9268E3F6B9014B
src/clients/pda/capacitor.config.ts 277D8E6502F01A2E7B6A3CCCB0968D999F00303FDD212FB51B0FD994C1450A5F
src/clients/pda/android/app/src/main/java/com/industrialplatform/pda/IndustrialRuntimePlugin.java B41CFFB284AC667B7B3AB2A619687038BE10695C215BCDF9D922268FA7536CBF
src/clients/pda/android/app/src/main/java/com/industrialplatform/pda/MainActivity.java 89B1B406CDB41932BF865E35AB75AE283266C641638ECE4E53B8D0C4FBBA58DE
src/clients/pda/android/app/src/main/AndroidManifest.xml FCC41BC0AD5454E54B5F799B808603CEBAADE31E18029B45116B92F2D2BC5359
src/clients/pda/android/app/build.gradle F3B75AA8B5599CA24DF051B8DD8E7BC999DE01C106BC4DC8D4F665E3A17C8C6F
src/clients/pda/android/build.gradle 46ADE7DB7AFEB8049CCEA59FCE83095DCA736CEB8C9ACD79CC944E988C3357D9
src/clients/pda/android/settings.gradle 6CE098D15EBD69B44C4ECD786BCA1BDC366340D2459B2AF4D7BB0AB317AA9669
src/clients/pda/android/gradle/wrapper/gradle-wrapper.properties 9A479A85AA879BB55284FC16D0DE5B81A745C3854F4E2D88C42E7DEBAF007435
```
清单聚合 SHA-256（按上述行顺序、以 LF 连接）：`2221596A0E000398E1D83FB2EF0B52C3CE4DA45806E9D17A8363590D74706966`。
<!-- CURRENT_HASHES_END -->

## 历史交接记录（前一工作树，已并入基线）

- 前端新增扫码运行时契约：单活跃会话、sessionNId/subjectEpoch 防旧回调、键盘/广播/相机来源、4096 字符边界、清理与错误事件。
- 新增共享 `AppScanInput` 与 `ScannerTestPage`，PC/PDA 仅保留薄路由入口；PC `/pc/terminal/scanner-test`、PDA `/pda/terminal/scanner-test`。
- 接入真实菜单、路由、双语文案与 `runtime.read`/`runtime.configure`/`runtime.update` 权限目录；测试页只读授权，配置权限不足时禁用测试输入。
- 新增 Web 更新协调器状态机：启动/重新登录门禁、检查、下载、验签、激活、健康确认、回滚与恢复态；业务期不允许下载/激活。
- 后端补齐权限目录、首批种子、策略注册、系统数据基线版本与默认导航测试入口；未触碰 SQL 生产代码。

## WIP 边界

以下文件在本轮开始前已经存在未提交改动，保留且不纳入本次实现归属：

- `docs/blueprint/09-Industrial Platform开发总TodoList.md`
- `docs/blueprint/34-Industrial Platform终端运行时与客户端架构.md`
- `docs/implementation/09A-Industrial Platform终端运行时与客户端打包开发实施方案.md`
- `docs/implementation/README.md`
- `docs/implementation/STANDARD-派遣前详细设计与页面验收.md`
- `docs/implementation/details/PF06A-SQLServer2019-2022接入补充规格.md`
- `docs/implementation/details/PF06A-运行时字段与页面规格.md`
- `docs/status/CURRENT.md`
- `docs/tasks/pending/PF-06A.md`
- `docs/tasks/pending/README.md`
- `src/开发注意事项-数据库拓扑与本地配置.md`
- `docs/implementation/details/PF06A-运行时字段字典.md`
- `docs/tasks/active/PF-06A.md`

## 验证矩阵

| 命令 | 结果 |
|---|---|
| `& '.\\node_modules\\.bin\\vue-tsc.CMD' --build`（`src/frontend`） | exit 0 |
| `& '.\\node_modules\\.bin\\vitest.CMD' run tests/unit/scannerRuntime.spec.ts tests/components/AppScanInput.spec.ts tests/unit/updateCoordinator.spec.ts` | 3 files / 11 tests passed，exit 0 |
| `& '.\\node_modules\\.bin\\vitest.CMD' run`（`src/frontend`） | 141 files / 1074 tests passed，exit 0 |
| `& '.\\node_modules\\.bin\\vite.CMD' build`（`src/frontend`） | 2395 modules，exit 0；仅有 chunk >500 kB 提示 |
| `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release` | 0 warnings / 0 errors，exit 0 |
| `dotnet test tests/Identity/IndustrialPlatform.Identity.Tests/IndustrialPlatform.Identity.Tests.csproj --configuration Release --no-build` | 621 passed / 0 failed，exit 0 |
| `dotnet test tests/SystemData/IndustrialPlatform.SystemData.Tests/IndustrialPlatform.SystemData.Tests.csproj --configuration Release --no-build` | 633 passed / 0 failed，exit 0 |
| `git -c safe.directory='D:/Code/Industrial Platform/IndustrialPlatform-PF-06A' diff --check` | exit 0；仅有 LF/CRLF 转换提示 |

## 真实环境验证边界

- 本机未发现 `java`、`adb`；工作树没有 Electron 客户端目录、Capacitor Android 工程或对应依赖。因此 Windows Electron 安装→登录→扫描、Android 真机安装→登录→相机扫码、原生更新包/APK、冷启动/重新登录更新门禁均记为 `BLOCKED/NOT RUN`，没有伪造通过结果。
- 本历史轮次只完成并验证Web DOM/运行时契约与路由菜单；真实Android手机相机和原生桥接当时未验证。PDA广播已在后续主控边界中排除，不作为当前交付缺项。
- SQL 009 仍处于设计闸门；本轮未改 SQL 生产代码、未执行数据库迁移。

## 未提交状态

- 不执行 commit，不执行 push，不改变主控工作树。
- 构建生成的 `bin/`、`obj/`、前端 `dist/` 属于可再生产物，不作为源代码证据。

## SHA-256 清单

清单使用 PowerShell `Get-FileHash -Algorithm SHA256` 对本轮实现/测试文件逐文件计算；本证据文件自身不纳入清单，避免自引用。最终值由交接前补入。

<!-- HASHES_START -->
```text
src/backend/src/Services/Identity/IndustrialPlatform.Identity.Api/Authorization/PermissionPolicies.cs 28E3C70984F7B51951510FE77265C1A4B3A52C16614F38350DBCF8EA4FDE78B8
src/backend/src/Services/Identity/IndustrialPlatform.Identity.Application/Bootstrap/BootstrapSeedCatalog.cs 1B68307E384133935703A26ECDF767778299D11F4E53E1040AE16821FCA3C91E
src/backend/src/Services/Identity/IndustrialPlatform.Identity.Domain/Permissions/PermissionCatalog.cs EB8B80D6AE02B7519338A95063AD8A0636C6267F4C12A7C1B4D68ABC761A94E4
src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Application/ControlPlane/ControlPlaneServices.cs 3409EC0FA6940C4F536DCA68CFCE8AF595DDB7CF21ADD06BF63BC8A1B34DBAD9
src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Reliability/SystemDataBaselineSeedRunner.cs 8E703699CEF3829F9F6ABA645B23C985F010C7737797EFE18DBF6134F90DFD86
src/frontend/src/components/home/TerminalFeatureMenu.vue B199E8FE98BE22C3F3F518EFAA6E5361446C41DDA2C7AC26959FAC737581696F
src/frontend/src/components/navigation/navigation.ts FAFA0256704C4B0FEF08A939C224FBD1504F7C4325372DAEEA121B5561A09341
src/frontend/src/locales/en-US.ts EB5ED8C97A80CBB6BE26E719A0A6F8473A5C22D81F29FFA854839EA4BC67DC26
src/frontend/src/locales/zh-CN.ts 9F12419239BC02E689E12B269C023C1286DF14D9934D29EF2B5ABCB5C44CECA8
src/frontend/src/permissions/catalog.generated.ts 37214A1382499A0832A4F9BE4339D693C80A2E6412C7D5E5954CCA2DED3DD282
src/frontend/src/permissions/catalog.ts 57876B552F1C35B7FF86BB042E8EA8729F6BEB19F5F18CC9CA8FA959CA066A0B
src/frontend/src/router/routeNames.ts 6305DFED4F87399373040D2110FEC1B47B516AF60E5C0A229555A2FD19B49E52
src/frontend/src/router/routes.ts 1FC1773CDF5948C983C9C9276679F5FB8B341D3178A2FC67D373D07745B033C4
src/frontend/src/components/runtime/AppScanInput.vue 40123E7F7852497D9F737EC0C9A5803CB54D158830DF44CEEC2D4DF70C95B341
src/frontend/src/components/runtime/ScannerTestPage.vue 2433912949FBF4FC109DC0D7ED9F63EE6F772625FAC7E916C8B956C045C393A8
src/frontend/src/pages/pc/RuntimeScannerTestPage.vue 73CA26AEE0AF4C85A572FE3F1C94A3A15B55B47D4A9F8CD97F4B71A872D5C25C
src/frontend/src/pages/pda/RuntimeScannerTestPage.vue CD8FC2BCE667BC6EE0B9206223E3FE748B471135C224FC939F1E3EA979C74901
src/frontend/src/platform/runtime/scannerRuntime.ts 60686521971BE17684C13F4B9CA7AC54BD053DB64E12B6721936FC0CBF06CE7F
src/frontend/src/platform/runtime/updateCoordinator.ts B5F3C455BC657687E94AC320664837153AF382D507DA5C6290A04D08F626C650
src/frontend/tests/components/AppScanInput.spec.ts 435942393E8B47DFC81B2A9B5880670DBCE88BECD62AE7E2AD8CA438A97F0308
src/frontend/tests/unit/navigation.spec.ts EB5C150EE2439535AE27A35638594349649B44B8BBD1A55F423401968ED73F4F
src/frontend/tests/unit/scannerRuntime.spec.ts A3198011ED41A62AFD73394800CEE5D4711E5B85B2DF3D8766F0BE6AD83DC29E
src/frontend/tests/unit/updateCoordinator.spec.ts FA6767D9CD9486C56BB33F7E3E5232E00FC1B35E3FD2B66571B72A39C476ACF7
tests/Identity/IndustrialPlatform.Identity.Tests/Application_BootstrapSeedCatalogTests.cs 4089DC454D9319FC45550FF5B17E2DBD1D9256130C0CEEFC5791B93432C49540
tests/Identity/IndustrialPlatform.Identity.Tests/Domain_PermissionCatalogTests.cs 4698440540F4B49C9C718F1CA2654500E9773A30910DE86684DA9F05E27C99E6
tests/Identity/IndustrialPlatform.Identity.Tests/Infrastructure_IdentityMigrationTests.cs ADBD9A3F59866B1611E9B1CBC8260E483B26747475D5D5AEC5F5670B57183019
tests/Identity/IndustrialPlatform.Identity.Tests/Infrastructure_IdentityRepositoryTests.cs 7A7AEE7152E95CA2C29228E6382B4553FBC4224E932ECFC36624956E6C0B2FA2
tests/Identity/IndustrialPlatform.Identity.Tests/Infrastructure_IdentitySeedRunnerTests.cs 576B639A222BE990C317B33E9C5842C53835744FC4969746E05959E879210E88
tests/SystemData/IndustrialPlatform.SystemData.Tests/Application_ControlPlaneServiceTests.cs E3F42C014D9D83C2765BD34D3259F686FC3FC886C8B82AFABD9305DC2ABACE1C
tests/SystemData/IndustrialPlatform.SystemData.Tests/Infrastructure_SystemDataBaselineSeedRunnerTests.cs 238EB554342FD79D6A139450D0FD8119193BD3A98FEC8A309924E53EE649D70B
```

清单聚合 SHA-256（按上述行顺序、以 LF 连接）：`7A11C85F98E8A4F03129D8E63485736B47C0A13ED738B9758646838BB0E0D415`。
<!-- HASHES_END -->

## 主控合并前核验

- 30 个交接文件的 SHA-256 均与上方清单一致。其中 `tests/SystemData/IndustrialPlatform.SystemData.Tests/Infrastructure_SystemDataBaselineSeedRunnerTests.cs` 与基线提交内容相同，因此没有形成 Git 变更；其余实现与测试文件及本证据文件单独暂存。
- 新鲜 `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release`：exit 0，0 警告、0 错误。
- 首次全方案测试发现 UnifiedHost 测试仍将资源清单版本断言为 `3`，与本轮增量种子的版本 `4` 不一致。主控只修正 `tests/UnifiedHost/IndustrialPlatform.UnifiedHost.Tests/UnifiedHostTests.cs` 的该断言；重新 Release 构建 exit 0，定向用例 1/1 通过。
- 并行重跑全方案时 ReferenceData 有 1 个临时 SQLite 初始化用例失败；该用例单独 1/1、ReferenceData 全组 261/261 均通过。随后使用 `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build -m:1` 顺序运行，exit 0：8 个测试项目合计 1898 通过、0 失败、7 个外部环境用例跳过。并行失败未复现，不能计为外部集成已通过。
- 开发任务的前端类型检查、141 文件/1074 用例与 Vite 构建证据沿上表复用；主控没有修改前端源码，不重复运行。
- 本次仍只交付 Web 扫码与更新协调器及菜单/权限基础。Electron/Capacitor 原生外壳、真实设备扫码、安装包与真实热更新未实现或未验证；PF-06A 001/005 不标记整包通过。SQL Server 009 设计门禁仍未关闭。
