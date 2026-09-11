# SQL Server 2019 / 2022 与 Screego 平台集成：工作量及开源交付评估

日期：2026-09-10。状态：评估，未派遣、未实施、未调整当前开发顺序。

## 1. 已确认范围与结论

用户已明确：SQL Server 目标为 **2019、2022**；Screego 需要**平台内集成，复用身份、聊天邀请、逐人授权与停止共享**，不是独立工具跳转。

SQL Server 按“平台自身的业务关系库可部署到 SQL Server”估算，覆盖当前 Identity、SystemData（含 File / Notification / Audit）、ReferenceData、Collaboration 及必需公共基础设施。Screego 按现有 PF-06 的一名共享者、1～3 名观看者、PC 共享与 PDA / Mobile 观看、权限及审计边界估算。

**建议先补 SQL Server，保持现有 SqlSugar；Screego 先用短期 PoC 验证平台授权与真实断流，再进入正式集成。暂不把 EF Core 切换、五库全适配、Redis / Seq 替换一起纳入。**

| 工作包 | 最小验证投入 | 完整目标的基础估算 | 估算可信度 |
| --- | --- | --- | --- |
| SQL Server 2019 / 2022 兼容 | 3～5 人日 | **24～38 人日** | 中：主要修改位置可定位，真实迁移及两版本测试尚未执行 |
| Screego 平台内集成 | 3～5 人日 | **24～40 人日** | 中低：身份适配及媒体撤权仍可能改变引擎集成方式 |
| 两项合计 | 6～10 人日 | **48～78 人日** | PoC 通过后按实际差距重估 |

最小验证已包含在完整估算中，不能重复相加。人日按约 8 小时有效工程投入计算，包含设计、编码、测试、修复及交付材料；由熟悉现有项目的工程人员使用现有 AI 工具完成，不是 Codex 自动运行时长或交付承诺。两项顺序执行约相当于一人 10～16 个工作周；排期可另留约 20% 风险余量，约 58～94 人日。多人协作不能机械按人数等比例缩短。

估算不含当前 PF-05 既存回归、客户环境等待 / 采购 / 差旅、全平台历史 PG 数据搬迁、SQL Server 高可用集群、2019 / 2022 以外版本、完整 EF 改造、Electron / PDA 原生工程、录屏 / 远控 / 音视频会议及未来所有 PF 的业务实现。

## 2. 当前代码与方案证据

- [DatabaseProvider.cs](../../src/backend/src/BuildingBlocks/IndustrialPlatform.SharedKernel/Topology/DatabaseProvider.cs) 仅有 Sqlite / PostgreSQL；[DatabaseTopologyResolver.cs](../../src/backend/src/BuildingBlocks/IndustrialPlatform.SharedKernel/Topology/DatabaseTopologyResolver.cs) 的 Shared 目标解析也仅覆盖这两种，且 Shared 当前只允许 Development。
- [SchemaPhysicalDriftGuard.cs](../../src/backend/src/BuildingBlocks/IndustrialPlatform.Infrastructure/Database/SchemaPhysicalDriftGuard.cs) 使用 SQLite PRAGMA 或 PG 的 current_schema / pg_indexes。即使连接 SQL Server 成功，初始化 / 漂移检查也不能直接通过。
- [CollaborationSchemaMigrations.cs](../../src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/CollaborationSchemaMigrations.cs) 已有真实迁移代码；[SqlCollaborationRepository.cs](../../src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/SqlCollaborationRepository.cs) 包含 ON CONFLICT 等原生 SQL。PF-05 不能作为空白模块重新估算。
- 当前服务源文件中检索到 96 处 `CREATE TABLE` 文本命中，包含重复 / 条件路径，不等于 96 张表；这是迁移范围的线索，不是逐表核算结果。
- [当前状态](../status/CURRENT.md) 仍有 PF-05 运行验收和升级回归记录；生产改造应基于稳定交接后的实际代码。旧“待开发”段落不能代替最新代码与进行中记录。
- [PF-06 实施方案](../implementation/09-Industrial%20Platform%20RemoteAssistance开发实施方案.md) 当前将未修改 Screego 定位为基准 PoC，原生信令为推荐生产候选；[PF-06 详细规格](../implementation/details/PF06-数据接口与页面规格.md) 已定义会话、邀请、参与人授权、一次性票据与引擎租约等契约。本次增加 Screego 正式接入的估算，不把它静默改成已采用路线。

## 3. SQL Server：工作量拆分

| 项目 | 人日 | 具体产出 |
| --- | --- | --- |
| 范围与最小纵向验证 | 3～5 | 确定两版服务器、edition / compatibility level / collation、目标拓扑；验证连接、代表性 CRUD、并发版本、查询与一条迁移链；补逐表差异台账 |
| Provider、配置与初始化目标 | 3～5 | SqlServer 目标解析、连接配置、预建库模式、必要 provision / inspect / 锁、最小权限、启动失败语义；保持现有初始化单元和拓扑边界 |
| 现有 Schema / 迁移 / Seed 适配 | 6～10 | 四个服务的 SQL Server DDL、类型、默认值、唯一约束、索引、种子及账本；元数据检查和漂移识别；不重写已应用的 PG / SQLite 迁移事实 |
| 查询、写入与事务差异 | 4～6 | 原生 SQL、受控 OData 查询、分页 / 排序 / LIKE、幂等及 upsert、审计 / Outbox、后台任务抢占与租约；避免“先查后插”并发窗口 |
| 两版本集成与原有数据库回归 | 6～9 | 2019 / 2022 独立安装、初始化重入、权限和并发测试、代表性页面链、升级与恢复；PG / SQLite 定向回归，统一与分布式入口验证 |
| 文档与交付 | 2～3 | 安装参数、初始化账号 / runtime 账号、支持矩阵、发行包许可证材料、蓝图 / TODO / 字段规格同步 |
| **合计** | **24～38** | 覆盖当前平台业务链；不包含未来模块业务开发或批量历史数据迁移 |

### 3.1 首期应固定的边界

1. 继续使用 SqlSugar，不把 SQL Server 兼容和 ORM 替换绑定。EF 的评估仍保留为后续选择。
2. 兼容 SQL Server 2019 / 2022；记录真实兼容级别，分别以通常对应的 150 / 160 为验证基线，再按客户实际设置调整。不能只在 2022 上降低兼容级别就宣称完成真实 2019 验收。
3. 保持现有 Development Shared 与其他环境 PerService 的规则。若客户额外要求生产单物理库、多 Schema，需另评估蓝图 32 / 33 的拓扑变化，不包含在本次兼容估算中。
4. 优先支持客户预先建库及分离初始化权限；应用不要求长期持有 sysadmin。开发用 Developer，生产按目标 edition 验证，不使用 Developer 特有能力冒充 Standard / Express 能力。
5. 自有表命名与逻辑字段语义保持稳定：Guid → uniqueidentifier、bool → bit、long → bigint、精确数值 → decimal(p,s)、UTC 时间 → 明确精度的 datetimeoffset。NId / 租户键用有长度限制的 Unicode 字符串，不能把所有 TEXT 直接替换为 nvarchar(max) 后用于索引。
6. 并发版本、软删除唯一性、大小写 / 尾空格 / NULL、索引键长度、JSON 校验与原生 SQL 必须逐项核对。不要将 ON CONFLICT 机械替换为未经并发验证的 MERGE。

### 3.2 哪些新增要求会扩大工作量

| 追加范围 | 估算影响 |
| --- | --- |
| 只对接客户 MES 的一组读取接口 | 属于更小的另一种范围；约 3～7 人日可做有明确字段 / 查询的适配，不能算平台 SQL Server 全兼容 |
| 把现有 PG 历史业务数据完整搬到 SQL Server | 另预留约 6～12 人日起，取决于数据量、外键 / 业务标识、文件关联、迁移窗口、校验与回退；数据未知时可能明显超过此区间 |
| SQL Server 高可用、灾备切换、Windows 集成认证等专项 | 按客户环境独立估算，不纳入基础连接与迁移工作 |
| 同时切换 EF Core 或默认支持两种 ORM | 需复评仓储、事务、迁移所有权及依赖许可；不是本项顺带完成 |

### 3.3 SQL Server 的开源与商业问题

开源应用可以连接 SQL Server，并不因此失去自身开源许可。但 SQL Server 服务端属于微软产品，平台开源协议不授予客户数据库使用权。

- Developer 只用于非生产开发 / 测试；不能随现场生产系统作为免费数据库使用。Express 可用于适用的生产场景，但有容量和资源限制；Standard / Enterprise 按实际采购协议使用。[微软下载与 edition 说明](https://www.microsoft.com/en-us/sql-server/sql-server-downloads)
- 如果以 SQL Server 2022 Express 交付，需要按其每库 10 GB 等限制做容量判断；该限制只在此处针对 2022，不泛化到其他版本。[SQL Server 2022 edition 对比](https://learn.microsoft.com/en-us/sql/sql-server/editions-and-components-of-sql-server-2022?view=sql-server-ver16)
- 自研适配代码可以按平台选定的许可发布；驱动及原生 SNI 库仍按各自条款随包登记。SqlSugar 间接带入的其他数据库 DLL 不会因为“本轮只支持 SQL Server”而自动消失。
- 对外优先交付应用与配置 / 安装说明，由客户提供合规 SQL Server。若做集成安装器、离线介质、转售或托管，另确认微软对应授权，不从开发下载许可推导 OEM 权利。

相关依赖问题沿用[开源与商业交付评估](2026-09-09-open-source-commercial-delivery-assessment.md)，其中许可不明驱动仍需在发行前闭合；技术兼容通过不代表许可已闭合。

## 4. Screego：平台内集成的真实工作量

### 4.1 现有能力与需要补齐的差距

Screego 已提供 WebRTC 屏幕共享、多人观看、Docker / 单二进制与 TURN 路径，可减少媒体基础能力的起步工作。[官方项目说明](https://github.com/screego/server)

但上游认证代码采用用户文件及 Cookie Session；配置中的 `all / turn / none` 不是平台的会话白名单。[认证实现](https://raw.githubusercontent.com/screego/server/master/auth/auth.go)、[配置样例](https://raw.githubusercontent.com/screego/server/master/screego.config.example)

本次抽查 `event_join.go`：进入房间后登记用户并建立共享会话，未出现平台的 Invitation / Grant / JoinTicket 校验；`event_stop_share.go` 有停止当前共享者会话的处理，但不能据此证明平台管理员撤销某一观看者后会立即断流。这是本项目集成差距，不是声称上游所有版本都缺少某项能力。[加入房间](https://raw.githubusercontent.com/screego/server/master/ws/event_join.go)、[停止共享](https://raw.githubusercontent.com/screego/server/master/ws/event_stop_share.go)

### 4.2 工作拆分

| 项目 | 人日 | 具体产出 |
| --- | --- | --- |
| 引擎 / 网络 / 授权 PoC | 3～5 | 按 PF06-001 做最小对照；选择候选版本，验证 1～3 观看者、画质、TURN、入场鉴权及断流；明确外部适配能否满足，是否需要 fork |
| 平台控制面 | 4～6 | 会话、邀请、参与人 Grant、一次性 JoinTicket、幂等、状态机、审计及租约，接已有 PF-05 / Identity 契约 |
| Screego 适配与媒体撤权 | 5～9 | 平台身份映射、入场和信令授权、逐人撤销、重连 / 票据重放、房间结束、实际媒体停止；若需 fork，将必要修改集中并记录 |
| 平台页面与聊天集成 | 4～6 | 邀请卡片、PC 共享页、PDA / Mobile 观看、错误 / 断线 / 结束状态；复用现有线框并修订引擎相关交互 |
| 数据库、三端与现场矩阵验收 | 6～10 | PG / SQLite / SQL Server 2019 / 2022 上新增持久化链；真实浏览器、直连 / 中继 / UDP 受限、30 分钟稳定性、撤权与异常；平台 / 可信嵌入装配验证 |
| GPL 与部署交付材料 | 2～4 | 对应源代码包、修改记录、构建说明、LICENSE / notices、镜像与端口说明、升级 / 回退和 PF 文档同步 |
| **合计** | **24～40** | 假设核心 PoC 可行、具备 Go / WebRTC 调试能力与可用验证网络 |

SQL Server 工作包只处理已有四服务持久化；Screego 工作包处理新增 RemoteAssistance 持久化，因此两项表内数据库工作不重复计算。已有公共组件和设计可复用，不计作从零重做。

仅部署原版 Screego 并加跳转入口大致 1～3 人日，但不满足用户已确认的身份 / 邀请 / 白名单目标，不能拿此数字作为本次报价或排期。

### 4.3 先验证的关键点

1. 平台只给已获授权的本人签发短期一次性入场凭证；房间标识、共享链接或泛化登录不能替代授权。
2. 创建 / 加入房间、SDP / ICE 与重新连接都校验相应会话权限，不能只在 HTTP 首页或 WebSocket 建连时鉴权一次。
3. 观看者退出 / 被移除只停止其连接；共享者停止 / 会话终止影响全部观看者。控制面状态与媒体结果分别观测。
4. **关闭 WebSocket、票据过期或隐藏画面不等于已停止媒体。** 对实际直连 / 中继路径验证租约、身份撤销、服务异常和恢复后旧连接的行为；按 PF06 既有时限给出新鲜证据。若不能满足，不进入正式交付。
5. HTTPS/WSS、TURN、域名 / 证书、代理和防火墙按客户网络验证。普通 HTTP 反向代理不能自动解决媒体端口；有 TURN 也不保证任意“只允许 443”的网络可用。
6. 保持主动浏览器捕获，不做录屏、远控或无人值守；PDA / Mobile 首期主要作为观看端，不能用 PC 测试结果承诺所有移动设备都能共享屏幕。

如 PoC 证明必须改成不同媒体路径、大幅修改上游或重建引擎，本次 24～40 人日上限就不再适用，应先重估方案，不能以增加少量缓冲掩盖架构变化。

## 5. Screego 的 GPL 与客户交付

**Screego 使用 GPL-3.0，可以商业使用和收费实施；关键是交付和修改时履行许可义务。** 对应源码、构建材料、修改标记和接收者权利需要有可执行的提供方式。[Screego 随附 GPL-3.0](https://github.com/screego/server/blob/master/LICENSE)

| 使用方式 | 应处理的问题 |
| --- | --- |
| 内部原版 PoC | 保留版本及许可记录；这不代表生产适配已完成 |
| 原版二进制 / 镜像随项目交付 | 即便不修改，也需按 GPL 所选分发方式提供许可证与对应源码获取渠道；一条指向随时变化的 master 链接不是完整交付证据 |
| 为平台集成修改 Screego | 提供该修改版完整对应源码及必要构建材料，标记修改；不能仅把修改后的二进制交给客户并要求其放弃 GPL 权利 |
| 将 Screego 源码复制 / 改写并并入平台前后端 | 需要判断是否形成 GPL 组合作品；不能默认整包仍仅用 MIT / Apache-2.0 交付，也不能以“翻译成另一语言”规避衍生作品判断 |
| 独立服务通过接口协作 | 有助于明确技术及交付边界，但不是自动豁免；仍需审视代码来源和实际组合方式，并履行 Screego 本身的分发义务 |
| 只提供网络使用 | GPL-3.0 本身不同于 AGPL 的网络条款；仍要区分是否把服务端程序、浏览器 JS 或安装包交付给用户，不能简单认定“走网页就没有分发” |

建议设计上让平台保留自己的会话 / 授权 / 审计事实，Screego 保持明确的引擎及发行边界。若需要 fork，单独管理其源码、构建与版本，保持修改小而可追踪。能否继续用平台选定的许可证发布平台部分，取决于最终代码和组合关系，需在集成方案确定后复核。

GPL 义务针对对应软件，不是要求公开客户业务数据、账号、密钥或私有业务记录。也不能把全部 GPL 义务简化为“我们本来就会开源，所以不用管”；第三方 notices、具体修改版源码和许可证兼容仍要落实。

本报告中 GPL 判断为工程交付筛查。平台嵌入、闭源客户模块或 OEM 包存在边界疑问时，以具体组合和许可复核结果为准；不预设上游提供商业双许可。

## 6. 先做的顺序、前置条件与后续维护

建议顺序：

1. PF-05 当前开发和验收稳定交接；评估工作可先完成，SQL Server 的迁移 / 公共基础设施修改不与 PF-05 重叠写入。
2. 在隔离环境验证 SQL Server 2019 / 2022 的最小纵向链，完成字段、DDL、配置和测试清单后再整包开发。
3. Screego 3～5 人日 PoC 可在独立实验环境提前安排，不需要先改平台主工作树；正式接入仍依赖 PF-05 契约及引擎路线结论。本轮没有实际安排或启动 PoC。
4. SQL Server 基础能力通过后，RemoteAssistance 新增表 / 迁移直接纳入 PG / SQLite / SQL Server 支持矩阵，减少写完 PF06 再返工数据库适配。
5. 采纳后再统一回写蓝图 / 总 TODO / 实施方案 / 详细规格。沿用 PF-06 工作包，不另建一个功能重复的“Screego 平台”。任务只写实际前置条件，页面先有线框，表字段 / API / 状态 / 事务和许可交付先细化再派遣。

正式排期还需具备：两版 SQL Server 验证实例及权限、客户 edition / collation / 兼容级别、HTTPS 与可控 TURN 网络、真实 PC / PDA / Mobile 浏览器、拟定开源许可和能提供 GPL 对应源码的发行方式。以上不是要求现在暂停全部工作，而是进入对应实现 / 验收前应具备的输入。

后续每次 Schema 或查询规则变更，要持续覆盖 PG / SQLite / SQL Server 2019 / 2022 的相关测试，不能一次通过后永久宣称兼容；Screego / 浏览器 / TURN 升级需重复授权、直连 / 中继与断流测试。预算上可为普通 SQL Provider / 驱动升级预留约 1～3 人日，为有 fork 的普通 Screego 升级预留约 2～5 人日；重大破坏变更另估。这些是维护预留，不是固定月费或已测工时。

本轮只完成当前代码、上游配置 / 授权处理与许可核查，新增本评估。没有执行 PoC、运行测试、切换数据库、修改正式路线、操作用户服务、派遣任务或提交推送。
