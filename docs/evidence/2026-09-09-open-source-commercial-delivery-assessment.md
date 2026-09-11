# 开源发布与商业实施：组件许可及交付评估

评估日期：2026-09-09。状态：许可与交付可行性筛查，尚未执行组件替换或正式发布。

## 1. 结论

**平台可以走“开源基础平台 + 收费实施 / 运维 / 定制”的路线。目前未发现必须推翻 .NET / Vue / SqlSugar 技术路线的理由，但现有依赖和部署包不能未经整理就声明全部可自由再分发。**

最需要先处理的是：平台自身缺少 LICENSE、Redis 7.4 的非宽松许可、Seq 的商业使用范围，以及 SqlSugar 间接带入的数据库驱动授权。后续屏幕共享、报表、标签、终端 SDK 的选型，也要同时考虑开源与客户交付。

本轮检查了中央 NuGet 清单、前端清单 / 本地包、两份基础设施 Compose、既有 UnifiedHost assets 与构建目录、README 及相关蓝图，并查阅官方许可资料。保存了 [依赖元数据快照](2026-09-09-license-inventory.json)：63 个现有 UnifiedHost NuGet 包、9 个前端直接运行依赖。该快照不是新 restore 结果，不覆盖所有前端传递依赖、容器层、测试工具、字体或未来 SDK，也不是最终发行包 SBOM。

以下结论用于工程选型和交付准备；许可不明的二进制、闭源组合及 OEM 再分发，需要取得对应版本的权利证明，必要时进行法律复核，不能用本评估代替授权。

## 2. 已发现的问题与优先级

| 优先级 | 当前事实 | 对开源 / 商业实施的影响 | 建议 |
| --- | --- | --- | --- |
| 发布前必须闭合 | 仓库根目录没有 LICENSE / NOTICE | 公开仓库不等于授予他人使用、修改和分发权；也没有统一说明平台与第三方的授权边界 | 先确定平台版权归属和许可证，再补 LICENSE、第三方声明与发布材料；本轮不代选并应用许可证 |
| 再分发前必须闭合 | SqlSugar 依赖包含 Oscar、达梦、金仓、Oracle 驱动；现有构建目录确实有对应 DLL | 没有主动连接这些数据库，也可能随发布产物交付；顶层 ORM 的许可证不覆盖所有驱动 | 按最终产物核对；授权不明项取得条款 / 权利证明，或采用经验证的依赖裁剪 / 替代方案 |
| 首次对外交付前明确 | Compose 使用 `redis:7.4-alpine` | 不能把该版本当 BSD / MIT 组件；普通缓存使用、再分发和提供数据库服务应分别判断 | 评估 Valkey 作为开源版默认缓存；保留客户 Redis 接入选项。替换前验证协议、Lua、锁、TTL 和故障恢复 |
| 客户部署前明确 | Compose 使用 `datalust/seq:2025`，设置 `ACCEPT_EULA: Y` | 接受协议不等于拥有多用户或对外转授权；不能把自己的许可直接覆盖所有客户 | Seq 改为可选部署项，客户按实际授权使用；默认日志链可沿用现有 Console / File |
| 新组件采用前明确 | 旧蓝图含 MinIO / TimescaleDB / Grafana / QuestPDF；PF06、标签、终端另有候选 | 开源、源码可见、免费社区版、商业客户端不是同一种许可 | 具体到组件 / 版本 / 模块 / 是否修改 / 发布形态进行核验，不能按项目名字统一放行 |

具体位置：[本地 Compose](../../docker/docker-compose.yml)、[云开发 Compose](../../deploy/cloud-dev/compose.yaml)、[中央包清单](../../Directory.Packages.props)、[前端清单](../../src/frontend/package.json)。

没有 LICENSE 的含义参见 [GitHub 官方说明](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository)。

## 3. 当前核心依赖的许可情况

“通常可保留”表示未发现其许可类别本身阻止收费实施，仍需保留相应版权、许可证及第三方材料，不等于免除全部义务。

| 组件 / 类别 | 核查结果 | 建议 |
| --- | --- | --- |
| Vue、Vue Router、Pinia、Axios、Element Plus、icons-vue、vue-i18n、SignalR 前端包 | 本地安装的 9 个直接运行依赖均声明 MIT | 通常可保留；制作前端 bundle、安装包时附第三方声明，继续扫描传递依赖 |
| vxe-table 4.15.13 | 本地 package.json 与 LICENSE 均为 MIT，包含在上述 9 项内 | 当前核心表格可保留；增值插件、其他套件和商业服务不能由该结论代替审核 |
| .NET / ASP.NET Core 相关托管包、YARP、Serilog、StackExchange.Redis 等 | 现有 NuGet 元数据以 MIT / Apache-2.0 等宽松许可为主，详见快照 | 通常可保留；运行时原生库另算，Redis 客户端许可也不等于 Redis 服务端许可 |
| Npgsql 5.0.18 | 本地包声明 PostgreSQL License | 通常可保留，携带许可与版权材料 |
| PostgreSQL 服务端 | PostgreSQL License，允许按条件使用、修改、分发 | 可继续作为默认关系数据库；镜像内操作系统包等另需清单。[官方许可](https://www.postgresql.org/about/licence/) |
| SQLite 与 SQLitePCLRaw | 数据库核心、.NET 封装与本机库是不同交付对象 | 依据实际包逐项登记，不能因 SQLite 核心的授权方式而省略封装库和原生资源说明 |
| RabbitMQ.Client 7.2.2 | 本地包声明 `Apache-2.0 OR MPL-2.0` | 记录实际采用的许可路径 |
| RabbitMQ Server 4 | 服务端及核心插件主要为 MPL-2.0，部分文件另有许可 | 可独立部署；再分发需履行源码可得性等义务，对 MPL 文件的修改需要按规则提供源码，不要求所有独立应用代码统一改成 MPL。[服务端许可](https://github.com/rabbitmq/rabbitmq-server/blob/main/LICENSE)、[MPL 官方 FAQ](https://www.mozilla.org/en-US/MPL/2.0/FAQ/) |
| BCrypt.Net-Next 4.0.3 | 本地元数据指向项目 licence.txt，未给 SPDX 表达式 | 发行清单中补归档实际许可证；不能把空表达式当作无需许可 |
| SqlSugarCore 5.1.4.216 | 本地 nuspec 指向 Apache-2.0；核查时上游仓库 LICENSE 为 MIT，存在证据口径差异 | 两者都属于宽松许可，但发布清单须核实该包版本的适用授权，不能擅自视为可任选的双许可。[上游 LICENSE](https://github.com/DotNetNext/SqlSugar/blob/master/LICENSE) |

### 3.1 SqlSugar 的间接驱动：比 ORM 名字更值得注意

从现有 UnifiedHost 的 `obj/project.assets.json` 和 NuGet 包核查到：

| 间接组件 | 当前版本 / 文件 | 许可事实与缺口 |
| --- | --- | --- |
| Oracle.ManagedDataAccess.Core | 23.8.0 / Oracle.ManagedDataAccess.dll | 包内 LICENSE 为 Oracle Free Distribution, Hosting, and Use Terms and Conditions；允许符合条件的未修改程序使用和再分发，并非 MIT |
| Microsoft.Data.SqlClient.SNI.runtime | 5.2.0 / 本机 SNI 库 | 包内是 Microsoft Software License Terms；允许在所开发应用中按条件分发目标码，包含随附条款和再分发限制，不能归类为整个 SqlClient 都是 MIT |
| Oscar.Data.SqlClient | 4.0.4 / Oscar.Data.SqlClient.dll | nuspec 未提供 license / licenseUrl，包目录未发现独立许可文件；当前只能标记“再分发权利未核实” |
| SqlSugarCore.Dm | 8.8.1 / DM.DmProvider.dll | nuspec 指向 Apache-2.0，描述为达梦官方驱动；仍需核实所含厂商二进制的授权依据与完整材料 |
| SqlSugarCore.Kdbndp | 9.3.8.413 / Kdbndp.dll | nuspec 指向 Apache-2.0，描述为金仓官方驱动；同样需核对原厂二进制权利及来源 |

检查时现有 UnifiedHost 的 Debug / Release 输出均出现 Oracle、Oscar、DM、Kdbndp DLL。这是构建产物快照，不是新鲜 publish 验证，但足以说明“仅配置 PG，所以交付中肯定没有其他驱动”这个假设不成立。

Oracle 的已读条款要求携带许可、保持未修改与权利声明；不得单独为该程序的分发 / 使用加收费用，但允许它成为具有实质附加价值的收费产品或服务的一部分。应把 Oracle 驱动自身与 Oracle 数据库服务端采购分开。[Oracle 官方条款](https://www.oracle.com/downloads/licenses/oracle-free-license.html)

SNI 与 Oracle 的版本证据来自本机 NuGet 包内 LICENSE.txt，路径分别为 `C:/Users/DONG/.nuget/packages/microsoft.data.sqlclient.sni.runtime/5.2.0/` 与 `C:/Users/DONG/.nuget/packages/oracle.manageddataaccess.core/23.8.0/`。正式交付须从可信包归档所需材料，不能要求客户依赖开发者机器上的缓存。

建议优先做发行依赖最小化：只装配交付所需的 Provider。是否能够安全裁剪 SqlSugar 的特定依赖要先验证，不能直接删除输出 DLL，也不能仅靠 `PrivateAssets` 就声称运行包不会携带它们。若当前包结构无法可靠裁剪，再比较按 Provider 打包的实现或其他持久化路径。

## 4. Redis、Seq 与 Docker 的交付边界

### 4.1 Redis 7.4

官方说明：7.4～7.8 使用 RSALv2 / SSPLv1 双许可，Redis 8 才增加 AGPLv3 选项。因此不能沿用老 Redis 的 BSD 判断，也不能认为升到 8 就恢复宽松许可。[Redis 许可说明](https://redis.io/legal/licenses/)

平台把 Redis 用作内部缓存，不应直接等同于出售 Redis 服务；但将其功能向第三方提供或再分发，需要按所选条款判断。RSAL 的服务 / 分发限制和 SSPL 的义务不适合用一句“商用免费”覆盖所有交付方式。[RSALv2 原文](https://redis.io/legal/rsalv2-agreement/)

**建议评估 Valkey 为开源版默认项。** Valkey 核心采用 BSD-3-Clause，通常更便于开源平台及现场发行包保持清晰边界；仍需保留声明并验证当前 StackExchange.Redis 调用、Lua 脚本、分布式锁、消息 / 会话语义及运维路径。[Valkey 许可](https://github.com/valkey-io/valkey/blob/unstable/COPYING)

### 4.2 Seq

Seq 是商业软件；Serilog 和 Serilog.Sinks.Seq 的许可不能替 Seq 服务端授权。官方当前 Individual 规则是一个人访问实例，多人使用需要相应订阅；不能简单概括为“只要生产就收费”或“团队共用同一账号就免费”。现场部署还要确认许可持有人、服务器数量及再分发方式。[Seq 许可说明](https://datalust.co/pricing)

Compose 的 `ACCEPT_EULA=Y` 是接受协议的机制，不是购买许可或 OEM 授权。当前镜像为 2025 系列，最终应归档实际交付版本随附的 EULA 和采购条款，不能拿今天官网套餐回溯替代旧版本合同。[Seq EULA](https://datalust.co/doc/eula-current.pdf)

现有日志代码已有 Console / File，以及显式启用的 Seq Sink，可优先使 Seq 成为可选运维组件，无需重写日志架构。客户购买或自备 Seq 时照常接入，平台开源许可不包含 Seq 的转授权。

### 4.3 Docker Desktop

Docker Desktop 的免费条件受使用主体和用途限制；商业主体通常要求员工少于 250 且年收入低于 1000 万美元，政府实体另有付费要求。“我们的代码开源”不自动覆盖实施公司或客户的 Desktop 使用资格。Docker Engine / Compose 与 Desktop 的许可应分开核查，Linux 服务器运行容器不应直接套用 Desktop 订阅结论。[Docker 官方说明](https://docs.docker.com/subscription-billing/desktop-license/)

## 5. 后续蓝图组件与候选：不能视为已经采用

| 组件 / 方向 | 仓库中的关系 | 许可 / 商业交付考虑 |
| --- | --- | --- |
| Screego | PF06 未修改基准 PoC，非已选生产依赖 | GPL-3.0。交付二进制 / 修改版要落实对应源码与许可义务；不因收费就不能用，也不因独立 Docker 就自动免责。继续按现有最小 WebRTC 信令候选与 PoC 结果决定。[LICENSE](https://github.com/screego/server/blob/master/LICENSE) |
| ClamAV | 已有扫描适配，真实服务验收另计 | 引擎、独立 clamd 服务和病毒库应分别核验许可、分发及更新条件。不能从应用经协议调用扫描服务推导必须公开整个平台，也不能在整包分发时省略引擎对应材料；本轮未完成该交付包全部许可核对 |
| MinIO | 旧蓝图中的对象存储方向，非当前基础 Compose 组件 | 当前上游 AGPLv3；需要区分服务端、SDK、是否修改及网络使用。若商业要求与义务不符，另选存储实现或取得适用商业授权；不能将“兼容 S3”与“任意实现均同许可”混同。[LICENSE](https://github.com/minio/minio/blob/master/LICENSE) |
| Grafana / Loki / Tempo | 可观测性蓝图方向 | 核心项目已转 AGPLv3，企业功能另有条款。不能把它们当作替换 Seq 后完全没有许可义务的方案。[官方许可说明](https://grafana.com/licensing/) |
| TimescaleDB | 时序数据蓝图方向 | Apache-2.0 / 兼容许可与 Timescale License 按源码目录和二进制区分；必须明确使用的扩展及功能，托管 / 再分发场景另核对。[LICENSE](https://github.com/timescale/timescaledb/blob/main/LICENSE) |
| QuestPDF | EBR 报表蓝图中的候选 | 社区免费资格有主体、项目和商业场景条件，不是无条件 MIT。当前官方规则包括开源项目资格及商业规避例外，客户是否需要许可也不能只看实施方规模；采用前按实际直接 / 间接使用方式判断。[许可指南](https://www.questpdf.com/license/guide.html) |
| pdfme | 标签设计 / 渲染候选 | 上游为 MIT，可优先做功能与真机评估；字体、图片、插件与可选云服务仍独立核验。[官方项目](https://github.com/pdfme/pdfme) |
| vue-plugin-hiprint | 标签候选 | 核查时仓库 LICENSE 为 MIT，不能误报为整个项目必定 AGPL；配套 electron 客户端、中转服务、嵌入库与字体要逐项查，不把 wrapper 许可扩大到所有配套产品。[LICENSE](https://github.com/CcSimple/vue-plugin-hiprint/blob/main/LICENSE) |
| QZ Tray | 打印桥接对照候选 | 官方说明为 LGPL-2.1；证书 / Premium Support、品牌版本另有条件。静默体验、证书采购和开源程序义务分开评估。[官方说明](https://qz.io/docs/licensing) |
| JSPrintManager / PrintNode | 打印商业候选，未默认采购 | 采用前核对 SDK / 客户端再分发、站点或设备计费、内网运行及续费；不能从可下载演示版推导免费交付权 |
| Electron / Capacitor / 更新插件 / PDA JAR、AAR / 打印 SDK | 终端路线与候选，专项尚待落地 | 基础框架、原生包、Chromium 第三方材料、更新服务、签名及厂商 SDK 分项核验；使用开源壳不会自动取得硬件 SDK 的发布权 |

GPL / AGPL 不禁止商业收费，但可能要求向接收者或符合条件的网络用户提供对应源码。是否形成衍生 / 组合作品，要看实际复制、链接和协作方式；“拆成一个服务就一定没影响”与“连接 AGPL 服务就必须公开全部平台”都过于绝对。原样分发也可能有义务，不能只检查是否修改过源代码。

## 6. 对上一轮 EF Core 评估的补充

**EF Core 本体是 MIT，但换成 EF 不会自动解决所有驱动许可问题。** 它的潜在优势之一是更容易按目标 Provider 装配依赖，是否确实减少本项目发布包中的无关驱动，需要在真实 publish 结果中验证。[EF Core 许可](https://github.com/dotnet/efcore/blob/main/LICENSE.txt)

MySQL 尤其要区分：

- 当前 SqlSugar 间接使用的 MySqlConnector 是 MIT；Pomelo EF Provider 也采用 MIT，许可路线较清晰，但仍受前轮查出的 EF 主版本兼容条件约束。[MySqlConnector](https://github.com/mysql-net/MySqlConnector/blob/master/LICENSE)、[Pomelo](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql/blob/main/LICENSE)
- 上一轮列出的官方 MySql.EntityFrameworkCore 候选会带入 MySql.Data。MySQL Connector/NET 官方许可包含 GPLv2 与 Universal FOSS Exception；例外并非对所有闭源客户交付自动适用。实际候选包版本、开源组合与商业许可需共同核验，不能只因官方 Provider 支持 EF 10 就直接采用。[官方 Connector/NET 许可](https://github.com/mysql/mysql-connector-net/blob/9.x/LICENSE)
- Oracle 驱动自身许可、SQL Server / Oracle 数据库服务端授权，与 ORM 选择是不同问题。客户自有数据库授权不自动允许实施方转售驱动或数据库安装介质；反过来，可分发驱动也不等于取得数据库服务端许可。

因此 ORM 决策应同时比较：功能 / 性能 / 迁移成本、版本兼容和最终交付依赖清单。保持上一轮的渐进验证建议，不因这次发现就立即全仓替换。

## 7. 平台自身的开源方式

**若主要目标是推广平台，并通过现场实施、运维、培训及定制收费，可优先考虑 Apache-2.0。** 它有明确的专利许可及声明规则，允许商业使用；同时也允许他人在遵守条件时分发和开展商业服务。不能一边采用 Apache-2.0，一边再用附加条款禁止所有第三方收费实施。[Apache-2.0 原文](https://www.apache.org/licenses/LICENSE-2.0)

MIT 也是宽松选项。如果主要目标变成要求某些修改持续公开，才进一步比较 GPL / AGPL 及商业双许可；这会影响客户闭源集成和组件兼容，不宜仅凭“防止别人拿去卖”直接选择。

基础平台与客户定制应先明确版权、交付和维护边界。收费实施不要求客户定制必定闭源；能否闭源取决于基础许可、第三方依赖及合同。若计划未来双许可，需要提前安排外部贡献授权；DCO 的来源声明不等于取得任意重新许可的权利。

README 中 MalusAdmin、TMom、Admin.NET、yefeng、IM 等目前被标为设计参考。借鉴布局思路与复制源代码、图片、模板、文档是不同情况；本轮没有完成逐行来源比对，因此不能据 README 的“参考”标签断言完全没有复制。用户既有 Siemens OP / EBR、客户模板和现场数据尤其要确认权利，不能默认属于可公开资产。

品牌图、截图、字体与示例数据也应有来源记录；本轮只发现这些文件位置，没有完成素材权属或历史密钥扫描。发布时处理现有文件与 Git 历史，但不得未经核实破坏性重写历史。

## 8. 建议落地的修改项与验收

以下是评估建议，不是本轮已经修改的代码或已派遣任务。

| 修改项 | 范围 | 前置条件 / 完成证据 |
| --- | --- | --- |
| 确定开源授权 | LICENSE、README、贡献说明 | 版权主体和商业目标明确；选择许可证并列明第三方 / 客户专有内容边界 |
| 建立发行依赖材料 | THIRD-PARTY-NOTICES、licenses 目录、发行 SBOM | 以真实 publish / frontend build / 镜像 digest / 安装包为对象；覆盖直接及传递包、原生库、OS 包、字体、SDK；记录实际许可、来源、修改及义务 |
| 清理无关驱动交付 | 数据访问包与 Host 装配 / publish | Oracle / SNI 材料完整；Oscar 权利明确；DM / Kdbndp 原厂二进制条款闭合；裁剪后所有已声明数据库路径通过验证 |
| 缓存默认实现评估 | 两份基础 Compose、缓存集成测试、部署说明 | Valkey 候选通过当前调用、脚本、分布式锁和故障恢复测试；保留 Redis 接入边界，不自动换现有运行环境 |
| Seq 可选部署 | 两份基础 Compose、README、运维配置 | 无 Seq 时日志与排障路径仍可用；Seq 启用有明确许可主体与版本条款，不默认替客户接受 EULA |
| PF 选型增加许可字段 | TEMPLATE、派遣前细化标准、相关 PF 方案 | 每项登记包 / 版本或适用范围、调用方式、是否修改、是否随包分发、客户是否需采购、许可材料和替代路径 |
| 客户交付清单 | 安装包与项目交付说明 | 分清自研开源核心、第三方许可、客户自备软件、商业采购及源码提供义务；PF 独立交付的最小依赖一并检查 |
| 发布检查 | CI / Release 流程 | 新增许可证变更可见；Unknown / 缺失许可人工闭合；完整供应链与漏洞扫描、素材权属和密钥检查另有真实结果；不能用本次许可筛查代替安全验收 |

当前最省返工的顺序是：**明确平台开源目标 → 闭合已携带驱动许可 → 确定 Redis / Seq 的发行边界 → 将许可检查并入后续 PF 选型 → 最后按真实发行包生成完整材料。**

本轮只新增评估和元数据快照，没有设置平台 LICENSE、采购或接受新的商业条款、切换组件、修改业务代码、发布仓库或提交推送。
