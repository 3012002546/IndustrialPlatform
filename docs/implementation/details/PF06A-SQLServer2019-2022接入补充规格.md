# PF06A SQL Server 2019/2022 接入补充规格

日期：2026-09-14；对应`TASK-PF06A-009`。来源：用户明确要求当前项目增加SQL Server 2019/2022支持，作为任务补充。状态：待派遣、待接入细化与真实环境核验；本轮只登记设计和任务，不代表已实现支持。

## 1. 目标与边界

SQL Server 2019和2022作为平台后端数据库选项，与现有PostgreSQL并存，保留SQLite隔离测试支持。范围覆盖当前已接入的Identity、SystemData（含File/Notification/Audit及数据库编排）、ReferenceData、Collaboration（含PF05/06），不是只验证SqlSugar能够连接。后续模块沿同一Provider契约，不再默认“非SQLite就是PostgreSQL”。

PF06A原001～008终端任务编号和边界保留；009是用户新增的平台兼容范围，不给Runtime新增业务数据库/Service Host。Web/Electron/Capacitor仍通过现有API/Hub访问平台，安装包不包含数据库驱动配置、SQL账号或连接串。现用云PostgreSQL不自动切换或搬迁数据；历史PostgreSQL→SQL Server数据转换、双写同步、跨引擎自动回滚不在本任务内。版本兼容认证包括2019数据库备份恢复到隔离2022的升级演练，不是现用生产库迁移。

## 2. 当前代码证据与缺口

2026-09-14只读检查得到以下结果；未连接SQL Server实例，不能据此宣称已有支持。

| 实际入口（相对仓库） | 发现/接入要求 |
| --- | --- |
| `src/backend/src/BuildingBlocks/IndustrialPlatform.SharedKernel/Topology/DatabaseProvider.cs` | 当前枚举只有Sqlite/PostgreSQL；增加SqlServer需贯通序列化、配置、验证、目标注册、权限编排和测试，不仅增加枚举项 |
| 同目录`DatabaseTopologyResolver.cs` | Shared分支只接上述两类；SqlServer应使用SharedDatabaseName，PerService仍要求ServiceDatabases显式映射。Shared仅Development限制保持 |
| `src/backend/src/BuildingBlocks/IndustrialPlatform.Infrastructure/Database/SqlSugarOptions.cs`及`Extensions/SqlSugarServiceCollectionExtensions.cs` | 底层已有SqlSugar配置绑定，但不能由DbType可设推出所有模块适配完成；核验DbType.SqlServer、实际驱动/依赖版本及连接构建 |
| `src/backend/src/BuildingBlocks/IndustrialPlatform.Web/Configuration/DevelopmentInfrastructureConfiguration.cs` | 统一开发配置入口；新增Provider选择不能被现有PostgreSQL生成逻辑覆盖，也不能复制宿主私有配置 |
| `src/backend/src/BuildingBlocks/IndustrialPlatform.Infrastructure/Database/SchemaPhysicalDriftGuard.cs` | 当前SQLite之外走current_schema()/pg_indexes；必须新增SQL Server实际元数据分支，不能把PG查询照发SQL Server |
| `src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/CollaborationSchemaMigrations.cs` | 存在CREATE TABLE IF NOT EXISTS、timestamptz等方言；逐迁移/仓储/租约/Outbox核对，不能靠连接驱动转换全部DDL |
| Identity/SystemData迁移runner、ReferenceData持久化与各初始化器 | 全量清点Provider判断、SQL模板、初始化互斥、Migration/Seed账本、原生SQL查询和异常映射；具体清单在G06A-SQL关闭前落定 |
| `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/DatabaseOrchestration/Runner/DatabaseTargetAdapterRouter.cs`及同目录`PostgreSqlTargetDatabaseAdapter.cs`、`SqliteTargetDatabaseAdapter.cs` | 当前Router只分派PostgreSQL/Sqlite；PG adapter直接使用Npgsql。SQL Server须贯通ITargetDatabaseInspector、ITargetDatabaseProvisioner、IMigrationExecutor、ITargetDatabaseAdvisoryLock及DI注册，不能仅换SqlSugar DbType；备份/恢复入口另沿实际调用链清点 |
| `Directory.Packages.props` | 当前集中声明SqlSugarCore 5.1.4.216、Npgsql 5.0.18；这是现有依赖事实，不是SQL Server驱动选型结论。核实最终解析到的SQL客户端包/精确版本、目标框架与加密参数行为后再冻结，不盲升全仓依赖 |

已有[2026-09-10 SQL Server接入评估](../../evidence/2026-09-10-sqlserver-screego-effort-assessment.md)的SQL Server章节作为历史清点输入；其中媒体路线与工期估算不作为本任务现行契约。当前接入补充已明确范围和验收，但仍缺逐表/逐调用点实施字典；不把历史评估或关键词命中数当完整改造清单。

## 3. 数据库与配置支持合同

| 维度 | 要求 |
| --- | --- |
| 引擎/兼容级别 | 必测SQL Server 2019（15.x）/150，SQL Server 2022（16.x）/160；2019升级恢复到2022保留150的路径另测。2022切到150不能代替真实2019引擎测试 |
| Edition/系统/驱动 | 开工记录实际Edition、完整build/CU、OS、驱动/SqlSugar及.NET版本；开发测试版本的通过不自动代表其他Edition、Linux/Windows、Express限制或HA已认证 |
| Provider值 | 新增逻辑值`SqlServer`，映射SqlSugar `DbType.SqlServer`；不新建SqlServer2019/SqlServer2022两套仓储，引擎版本属于环境能力而非业务Provider |
| 拓扑 | Development Shared：同一SharedDatabaseName，各服务对象命名空间隔离；PerService：每个服务显式物理库映射。UnifiedHost与独立Host/Gateway分别核验，不改变既有跨服务所有权 |
| 连接配置 | 保持后端唯一配置入口，精确新配置键/默认值/优先级和模板在G06A-SQL冻结。Host、Port、认证方式、凭据引用、Encrypt、证书信任、连接/命令超时与池参数明确；数据库名只由DatabaseTopology提供，拒绝重复冲突配置 |
| 身份与权限 | 运行账号与初始化/发布账号最小权限分开；不依赖sa/sysadmin日常运行。首轮至少明确一种真实认证模式；Windows集成认证若要求支持，另列宿主身份和测试证据，不默认跨系统可用 |
| TLS与秘密 | 使用SQL Server驱动的连接串构建器和显式加密/证书验证策略，不把全局TrustServerCertificate=true当正式解决方案；秘密只在受控配置/Secret Provider，不出现在任务、客户端、日志或诊断包 |
| 启动失败 | 未支持Provider/版本、映射缺失、连错库、权限不足、迁移失败或真实结构漂移要有明确错误/readiness，不静默回SQLite/PG，不自动创建任意数据库 |

本表为待实现要求；不得提前把示例中的新Provider键写成当前可用配置。现有开发私有文件和正在运行的服务保持不动，真实试验使用已批准隔离实例/数据库。

## 4. 数据类型、SQL方言与业务语义

| 范围 | 必须细化和验证的内容 |
| --- | --- |
| 类型映射 | Guid→uniqueidentifier，DateTimeOffset→datetimeoffset(7)，bool→bit，long→bigint，decimal保持原精度/scale；中文/多语言采用nvarchar及适当长度，二进制按实际长度用varbinary。JSON存储/校验/查询路径逐字段确认，不把PG jsonb直接替换为字符串后跳过语义验证 |
| 版本字段 | 保留Id/ConcurrencyVersion的Guid、OptimisticVersion的long及PF05业务版本用途；SQL Server rowversion不是日期，也不替换现有业务版本/JSON契约 |
| Unicode与比较 | 明确数据库及关键列collation；NId/幂等键/用户名/编码的大小写、重音、尾随空格和唯一语义沿原领域定义，不能让默认CI排序规则合并不同业务标识。真实测试中文、前导零及边界长度 |
| 对象/索引 | 每表列出SQL Server schema/表/列/约束/索引及查询用途，校验标识符/索引键宽度、nullable唯一键、过滤索引与软删除语义；不照搬PG标识符截断策略，不统一降长度或删除唯一约束来编译通过 |
| 方言 | 覆盖建schema/表/索引的幂等存在性检查、引用符、分页稳定排序、日期运算、JSON、大小写搜索、聚合、返回修改行/行数、参数类型和批量参数预算；逐个处理LIMIT、ON CONFLICT、RETURNING、PRAGMA及PG系统目录使用点 |
| 条件更新/并发 | 原有ExpectedVersion、租约、原子max、消息Sequence和软删除竞争必须仍由数据库条件/事务保证；不使用先SELECT后无条件UPDATE，不加NOLOCK掩盖阻塞，不引入未验证MERGE路径 |
| 超时/死锁/重试 | 分类唯一键冲突、死锁、命令超时、连接中断；事务回滚与结果未知分开。只有已确认可幂等操作才允许受限重试，连接丢失后不盲目重发可能已提交业务命令 |
| 业务真值 | PF05消息/附件绑定/合规审计、PF06会话/语音slot/截止/Outbox、种子菜单可见性必须实测；数据库换型不能放宽身份、审计成功顺序或媒体本地安全停止规则 |

UTC必须实际写入SQL Server、重新读取、跨进程再比较，包含PF05审批截止、PF06邀请/KeepAlive截止及Outbox原occurredOn；禁止延长租约来补偿偏移错误。decimal/long边界、GUID映射和时间精度要有真实仓储断言，SQLite替身不能作为该项证据。

## 5. 迁移、初始化与运维编排

1. **历史不改写**：PG/SQLite已应用migration/seed ID、checksum和内容保持不变。SQL Server建立明确的Provider专属DDL/签名与账本映射；同逻辑迁移跨Provider如何识别在编码前冻结，禁止因添加SQL分支导致旧库checksum变化后删除账本补救。
2. **新库与旧库**：覆盖所有当前模块从空库初始化，SQL Server旧版测试快照升级到本版，幂等二次启动、失败重试及旧账本结构兼容。当前尚无正式SQL Server旧版时，使用本任务生成并留存的合法前序迁移测试快照，不能伪称客户历史库已验。
3. **结构检查**：SQL Server使用自身catalog/object_id/schema对象关系检查列和索引；区分对象确实缺失、权限不足导致看不到、元数据查询失败。Inspect严格只读，不借检查补表/列；保留PG长索引名和SQLite修复成果。
4. **互斥与事务**：初始化锁需跨进程，在目标库正确作用域内工作；可评估sp_getapplock或现有受控锁抽象的SQL Server实现，冻结资源键/锁归属/超时/返回码/释放与故障恢复。不能把PG advisory lock直接替换为进程内锁。Migration与账本写入原子性、失败事务状态须实测；RCSI/SNAPSHOT不能未经验证默认开启或全局修改客户库。
5. **生产操作**：沿原SystemData plan→审批→备份→apply及服务所有者执行，不新增启动自动生产DDL。明确SQL Server计划、备份/恢复、Inspect、Apply、失败反馈和版本签名适配；不能前端下拉出现Provider、后端执行器仍拒绝。备份恢复使用隔离目标演练、声明服务器侧路径/执行身份，不写入客户端路径。
6. **升级兼容**：2019隔离库备份→2022独立目标恢复→保留150运行→受控切160回归，分别留证据；现用库不在本轮切版本。恢复路径是保留旧实例/备份和运行手册，不承诺2022备份可降回2019。

## 6. 执行步骤、修改范围与前置门禁

009内部连续步骤：接入清单与配置/字段冻结→Provider/拓扑/连接→逐服务DDL/迁移/仓储→SystemData编排/元数据/初始化锁→2019和2022真实纵向业务链→升级/故障/PG及SQLite回归→支持矩阵与交接。步骤不是新增派遣卡，不逐项提交。

允许修改：`src/backend/src/BuildingBlocks/`的数据库/拓扑/开发配置/查询和必要可靠设施；`src/backend/src/Services/{Identity,SystemData,ReferenceData,Collaboration}/`的Infrastructure及必要Provider契约/验证接入；`src/backend/src/Hosts/IndustrialPlatform.UnifiedHost/ModuleMigrationCoordinatorHostedService.cs`的Provider映射与相关现有初始化测试；`tests/{BuildingBlocks,Identity,SystemData,ReferenceData,Collaboration}/`对应回归；根`Directory.Packages.props`仅已核验SQL驱动所需项、`src/backend`配置example、相关部署模板和数据库文档。前端仅在现有数据库编排页面确有Provider枚举/验证需求时定点补充并复用现有页面与权限，不重做UI。其他Host先核对是否属于当前SQL支持矩阵，不将demo文件命中自动扩成全部样例宿主部署支持。

派遣前锁定实际文件清单和唯一写入者，尤其SchemaPhysicalDriftGuard、Collaboration迁移/仓储、Identity权限种子等既有PF05/06 WIP。009与001设备核验逻辑独立，可以安排不重叠的检查；未分配工作线不并改共享文件。原003～008不因本补充获得任意后端修改权。

**G06A-SQL（待关闭）**：主控归集2019/2022真实实例的版本/Edition/OS、隔离库创建与备份恢复权限、认证/TLS/collation、目标拓扑；接入细化输出驱动精确版本、配置键/示例、逐服务DDL与元数据/锁方案、迁移签名/账本不变证明、测试数据及清理范围。未知实例信息只记录缺项，不向现用服务试猜账号/数据库，不把本任务录入当作安装数据库或操作生产库授权。

D02/D03/D04/D06尚需逐服务精确清单回写，设计就绪度保持待细化/待前置核验；可先做已明确的只读清点。已经完成设计但缺真实引擎测试时单列待验收。009不依赖PDA/BLE/签名输入，不让设备缺失阻挡平台适配的独立前置准备。

接入细化的最小退出产物如下，不再只补通用原则：

| 产物 | 编码前必须明确 | 责任/边界 |
| --- | --- | --- |
| 逐服务对象清单 | 实际文件/服务/表与schema、逐列SQL Server映射、默认与精度、索引及过滤条件、对应查询；引用已有权威字段字典并逐项列差异，不复制第二套业务模型 | 主控清点现有迁移和实体；不替PF06改未交接业务字段 |
| 迁移与并发清单 | 每个迁移/seed的Provider选择、ID与checksum计算输入、旧账本兼容；初始化锁端口、会话/事务归属、返回码、释放和失败恢复 | 主控给实际映射与旧Provider签名不变检查方式，再由009实现 |
| 配置与执行链清单 | 精确键/类型/默认/优先级和脱敏模板、驱动锁定；Provider从配置/页面/契约到Router/DI/连接、Inspect/Provision/Apply/备份恢复的调用点、错误码和权限 | 由当前代码追踪，不只登记顶层Provider枚举 |
| 环境与测试清单 | 已确定支持的Edition/OS/认证/TLS/collation/拓扑；2019和2022各自隔离实例/数据库、测试账号权限、测试入口与数据清理范围 | 主控归集环境方输入；连接秘密只保留托管引用。可先准备测试入口，未提供实例不能计实测通过 |

缺真实引擎执行证据单独阻挡支持声明；会改变数据等价性、DDL或认证实现的环境要求未定则阻挡相应生产编码。PF06收尾不计入本次009设计缺项，已有PF06表/仓储仅做只读清点，共享文件实际改动仍需交接。

### 6.1 已确认的接入清点与内部顺序

2026-09-14按用户“启动开发前任务一定要细化”补充。以下是已读代码或搜索定位得到的最小接入清单，步骤属于009内部连续执行；逐对象SQL映射及真实目标尚未全部确定，因此不是生产开工PASS。简称路径均相对仓库：`B=src/backend/src/BuildingBlocks/`；`I=src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/`；`S=src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/`；`R=src/backend/src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Infrastructure/`；`C=src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/`。

| 顺序 / 现有入口 | 当前证据与必须完成的细化 | 退出判据 / 验收映射 |
| --- | --- | --- |
| 1 配置/拓扑：`B`下`IndustrialPlatform.SharedKernel/Topology/{DatabaseProvider,DatabaseTopologyOptions,DatabaseTopologyResolver}.cs`；`IndustrialPlatform.Web/Configuration/DevelopmentInfrastructureConfiguration.cs` | Provider目前仅Sqlite/PostgreSQL；Development统一配置生成PostgreSQL并覆盖SqlSugar设置。先固定Provider选择的精确键/默认/优先级与SQL配置模板，物理名仍只由拓扑解析 | 配置→实际Provider→目标库逐项可追踪；旧配置保持PG/SQLite，非法Provider/漏映射拒绝；SQL-A01/02/09 |
| 2 ORM/驱动：`B`下`IndustrialPlatform.Infrastructure/Database/{SqlSugarOptions,SqlSugarDbContext}.cs`、`Extensions/SqlSugarServiceCollectionExtensions.cs`，根`Directory.Packages.props` | 明确SqlSugar最终SQL客户端包及版本，参数化连接构建、命令超时/事务和GUID/时间/decimal绑定；不能以Npgsql版本充当SQL Server驱动 | 锁定依赖及配置对应表；连接成功/错误证书/低权限分别给预期；SQL-A01/05 |
| 3 Host/身份匹配：`src/backend/src/Hosts/IndustrialPlatform.UnifiedHost/ModuleMigrationCoordinatorHostedService.cs`；`R`下`Initialization/ReferenceDataInitializationLedger.cs`；ReferenceData Api下`Initialization/ReferenceDataHostContext.cs` | UnifiedHost当前非SQLite视为PG；ReferenceData部分逻辑非PG视为SQLite并取文件路径。逐调用点改为显式三Provider语义，固定实际数据库身份读取方式 | SQL Server目标不能成为PG上下文或被Path.GetFullPath处理；UnifiedHost/独立服务与Inspect目标一致；SQL-A02/03 |
| 4 公共结构检查：`B`下`IndustrialPlatform.Infrastructure/Database/SchemaPhysicalDriftGuard.cs` | 当前PG/SQLite元数据路径不能用于SQL Server；细化schema/object/列/索引、大小写、长名称及元数据权限不足的判定 | 只读Inspect不补结构；缺列/缺索引/不可见/查询失败分别可观察；SQL-A04/09 |
| 5 Identity：`I`下`Persistence/Migrations/{IdentitySchemaMigrations,SchemaMigrationRunner,SchemaMigrationStep,SchemaMigrationRecord}.cs`及现有实体/仓储 | 逐步骤展开现有DDL和列映射，账本补列仍含PG/SQLite方言；迁移checksum算法见§6.3。覆盖密码状态、会话、角色授权、导航依赖与并发记录 | 空库、合法前序快照、旧账本无checksum、二次启动及登录/刷新/授权闭环；SQL-A03/05/06/09 |
| 6 SystemData自身表：`S`下`Persistence/Migrations/{SystemDataSchemaMigrations,SystemDataMigrationHelpers,SchemaMigrationRunner}.cs`、`Persistence/SystemData/{Pf04Store,UserAssignmentAdvisoryLock}.cs`、`DatabaseOrchestration/DatabaseOrchestrationStore.cs` | 逐表覆盖平台配置及File/Notification/Audit/编排数据；用户分配锁与其他初始化/迁移锁分别登记资源键、持有连接/事务和释放，不以一个进程锁替代全部 | 数据/唯一性/UTC及两个进程竞争结果明确；外部审计不可达不改变本地停止语义；SQL-A03/05/06/07 |
| 7 ReferenceData：`R`下`Persistence/ReferenceData*Migrations.cs`及实际单数Migration文件、`Initialization/ReferenceDataInitializationLedger.cs`；各`Dictionary/Metadata/Parameter/CodingRule/DynamicProperty/StateMachine/UnitOfMeasure`迁移/仓储 | 逐模块确定schema与前缀规则；搜索命中不是完整清单，补实体及字符串拼接调用；当前PG命名与SQLite前缀不可直接作为SQL规则。目标身份、LIMIT、布尔/冲突、JSON及索引逐项映射 | 每模块的表/列/索引/查询对应已有权威业务字典；租户范围、版本、编码分配等结果不变；SQL-A03/05/06/07 |
| 8 缓存/Outbox：`R`下`Caching/ReferenceDataCacheGenerationStore.cs`、`Outbox/{ReferenceDataOutboxWriter,ReferenceDataOutboxStore}.cs`及对应迁移；其他服务同类入口沿实际调用清点 | 业务写与Outbox原子提交、批量领取/租约/重试、缓存代次更新及未知提交结果分别列SQL路径；不只验证普通CRUD | 故障后既无丢失已提交事实，也无错误自动重做业务；SQL-A07/09 |
| 9 Collaboration：`C`下`Persistence/{CollaborationSchemaMigrations,SqlCollaborationRepository}.cs`与partial/表、`Initialization/CollaborationServiceInitializer.cs` | PF05/PF06现有契约保持；逐迁移/原生SQL覆盖消息序号、幂等、附件绑定、未读及媒体持久化/并发slot/租约；PF06收尾文件先只读清点 | 新Provider沿同一业务版本/权限/时间约束；SQL-A03/05/06/07。PF06未交接内容不由009改业务设计 |
| 10 编排Provider全链：SystemData Application下`DatabaseOrchestration/DatabaseRegistrationService.cs`和`Runner/DatabaseOperationRunner.cs`；`S`下`DatabaseOrchestration/Runner/DatabaseTargetAdapterRouter.cs`及DI | 注册校验及Router均仅支持旧两种Provider；贯通注册/计划/审批/目标解析/凭据解析/Inspector/Provisioner/MigrationExecutor/AdvisoryLock，逐端口列输入/输出/错误和权限 | 不出现页面能选但Apply报ProviderUnsupported；Inspect与Apply指向相同受信目标；SQL-A02/04/08 |
| 11 目标账本/产物：`S`下`DatabaseOrchestration/Runner/{MigrationLedgerContracts,SeedLedgerContracts,TargetSeedLedger,SqlSeedBundleExecutor,MigrationArtifactChecksumVerifier}.cs` | 账本当前有CREATE TABLE IF NOT EXISTS、TEXT、LIMIT及MAX(sequence)+1。SQL分支必须明确键长/排序/事务互斥与表名白名单；区分内容checksum、SignatureRef及密码学签名 | 不能靠替换时间类型完成DDL；同版本不同内容拒绝，两个进程不重复记账，旧Provider产物校验不变；SQL-A03/07/09 |
| 12 运维/真实闭环 | 沿既有DatabaseBackupService及实际执行端点追到Provider实现，记录服务器端备份路径、执行身份、隔离恢复目标；为§7全部场景绑定已有或待新增测试入口 | 2019/150和2022/160分别通过；2019恢复到隔离2022保留150再切160；PG/SQLite回归，终端联合结果另列；SQL-A01～10 |

009不增加第二套业务Entity或ORM。以上“迁移/仓储清点”必须继续展开为实际文件和对象行；命中`DbType.PostgreSQL`之外，还要追踪`DbType.Sqlite`二元分支、`current_schema()`、`LIMIT`、SQL字符串拼接和初始化目标匹配，避免漏掉SchemaMigrationRunner等未被第一轮关键词覆盖的入口。

### 6.2 已有配置合同与本次不得误改的含义

| 现有精确入口 | 代码现状 / 009细化约束 |
| --- | --- |
| `IndustrialPlatform:DevelopmentInfrastructureMode` | 当前仅Unified/Sqlite，空值走统一配置；这是开发基础设施模式，不是数据库引擎名称。SQL Provider选择不能擅自写成第三种运行模式并破坏Redis/MQ等共用配置 |
| `IndustrialPlatform:LocalConfigurationPath` | 用于定位统一Development私有文件；不为SQL在每个Host增加另一份秘密配置 |
| `RemoteDevelopment:Enabled`、`:Host`、`:PostgreSql` | 统一文件在Apply中加入配置，随后按当前PG分支生成SqlSugar覆盖项；需先确定SQL专属配置键/地址是否独立及选择逻辑，不能假设外部SqlSugar:DbType必定最后生效 |
| `SqlSugar:DbType`、`:ConnectionString`、`:IsAutoCloseConnection` | 当前绑定SqlSugarOptions；默认PostgreSQL、自动关闭为true。新增SQL引擎选择与生成连接保持一致；秘密不输出，任何测试报告仅记录Provider与脱敏目标标识 |
| `DatabaseTopology:EnvironmentName`、`:Mode`、`:SharedDatabaseName`、`:SharedSqliteFile`、`:ServiceDatabases` | 现有键复用；Shared仅Development，SqlServer用SharedDatabaseName，PerService必须显式映射；不得另设SQL配置中的DatabaseName与拓扑争夺权威 |
| UnifiedHost的`ServiceDatabases:unifiedhost` | 当前UnifiedHost为单连接组合模块；PerService下必须有unifiedhost映射，不能据“PerService”宣称同一UnifiedHost已自动拆成每模块独立连接。真正各服务物理分库按独立服务宿主测试 |

新SQL连接字段仍须按§3固定精确键/类型/默认/有效范围/优先级/秘密来源/成功与失败样例。当前表记录已核实的兼容合同，不将尚未确定的SQL键写成已支持配置，也不改动正常调试私有文件。

### 6.3 迁移校验值的四类来源分别细化

1. Identity的`SchemaMigrationRunner`、SystemData的同名Runner，以及Collaboration的`CollaborationServiceInitializer`，当前步骤checksum均为UTF-8编码的`Id + "|" + Description`计算SHA-256后转小写hex。009保留历史ID、Description与该算法；新增Provider分支不因此证明DDL内容相同，必须另给逐Provider DDL和结构检查。禁止因“统一校验算法”让旧记录全部漂移。
2. SystemData `MigrationArtifactChecksumVerifier`当前对按Sequence排序的步骤拼接内容计算checksum；迁移内容含SQL/RollbackSql/Destructive，种子内容含SQL/RollbackSql，外层含Version和SignatureRef。SQL Server产物必须按实际生成内容登记校验值，不能复用PG的同名产物checksum。现有算法和签名引用比较不改写；需要新的内容规范化格式时使用明确版本并保持旧产物兼容。
3. 上述Verifier目前校验内容checksum及SignatureRef相等，代码并未实施密码学签名验证。009不得把这项测试写成“已有私钥对应公钥验签通过”；原生更新§1.2要求的密码学签名另属005，不能互相冒充，也不因SQL接入顺带新建发布PKI。
4. ReferenceData还有`target_identity`与模块种子校验。当前目标身份会按Provider区分库名和SQLite文件路径；SQL Server须按真实引擎/物理库身份细化，不能走SQLite路径归一化。其migration/seed输入沿当前模块逐项登记，不能套用前三类推断。

每条最终迁移/seed记录至少给：`serviceKey / provider / schema.table / sourceFile / migrationOrSeedId / version / oldChecksumRule / sqlServerChecksumRule / desiredStructure / transactionBoundary / lockScope / failureRecovery / legacyProviderAssertion / testEntry`。现存旧ID逐条说明“不改动”及检查依据；新SQL专属ID或产物区分方式在编码前落定，不把需要决定的编号交给实现者临场填写。

### 6.4 从清点到编码的交接检查

- 主控完成§6.1调用链、§6.2配置合同、§6.3账本来源，并逐表展开§6要求的四类产物；环境方输入只登记脱敏目标/能力，秘密不进规格。
- 每个对象记录物理schema/表/列、当前.NET与JSON字段、SQL Server类型/长度/精度/nullable/default、PK/FK/索引及过滤条件、查询用途、迁移步骤和对应场景。通用字段引用既有唯一字典，差异逐列列明；不以“使用CodeFirst自动生成”跳过索引/字符比较语义。
- 定向回归已有入口包括：BuildingBlocks的`DevelopmentInfrastructureConfigurationTests`、`SchemaPhysicalDriftGuardTests`；SystemData的`Domain_DatabaseTopologyResolverTests`、`Application_DatabaseOperationRunnerTests`、`Infrastructure_SchemaMigrationRunnerTests`、`Infrastructure_SeedLedgerExecutorTests`；Identity的`Infrastructure_IdentityMigrationTests`、`Infrastructure_SchemaMigrationRunnerTests`；ReferenceData各`*PersistenceTests`、`ReferenceDataServiceInitializerTests`、`OutboxAtomicityTests`；Collaboration的`Infrastructure_SqlMessageSequenceConcurrencyTests`及初始化/附件/媒体并发测试。现有用例通过不等于它们已覆盖SQL Server，接入时明确新增真实引擎fixture与断言位置。
- SQL实例fixture必须显式选择2019或2022、隔离库和目标拓扑；没有连接输入时报告NOT RUN，不能自动转SQLite。测试清理只操作本次登记的隔离目标，删除前校验目标标识；业务关键并发/UTC案例用真实引擎。
- 生产派遣范围全部具有确定的输入/文件/预期/异常/测试入口后，主控更新其设计就绪度。开发按009内部顺序连续实现，首先跑通“连接→初始化→登录/菜单→一次真实持久化读回”，再扩完整矩阵；不等待最后才发现目标匹配或账本拒绝启动。

### 6.5 表实体字段字典与人工接手

按用户要求，009所有纳入接入的表/实体均需完成[共同规则§3.1](../STANDARD-派遣前详细设计与页面验收.md#31-面向人工接手的表实体与字段说明)的说明；覆盖领域表、关联表、migration/seed账本、Outbox、租约/幂等记录及投影，不只写页面可见字段。现有权威业务字典已有完整语义时直接引用，再补SQL Server物理差异，不重新解释PF05/06业务权威。

每张表的说明卡固定记录：`中文名称 / serviceKey / 实体类别 / 一行含义 / Entity或Table类与文件 / 各Provider的schema.table / PK与业务键 / 租户范围 / 创建与更新方 / 主要查询 / 生命周期与清理 / 通用字段字典 / 迁移和测试位置`。技术记录不继承业务生命周期时明确写不适用。表名相似但含义不同的记录分别说明，不能将业务ID、数据库PK、请求ID和会话ID互换。

字段分两张表逐项对应：

| 语义表列 | 填写要求 |
| --- | --- |
| 属性名 / 物理列 / 中文名称 | 定位到真实Entity/Table属性及实际列名，API不外露的字段写“内部字段”，不虚构DTO |
| 含义 / 来源 / 写入时机 | 完整说明该值表达的事实、由谁产生、何时变化、是否派生、是否允许用户维护 |
| 取值 / 空值 / 单位 / 样例 | 逐枚举值含义、空值与零的区别、UTC事件语义、数值单位；样例标明示意且不含真实秘密 |
| 关联 / 使用 / 风险 | 关联哪张表或外部服务标识、主要用于哪个查询/并发/鉴权/恢复判断、误改会有什么影响、是否敏感 |

| 物理表列 | 填写要求 |
| --- | --- |
| 同一属性与列标识 | 与语义表逐项对应，继承字段也有引用或本表差异 |
| .NET / JSON / 各Provider SQL | 包括准确类型、长度、精度、可空与序列化约定；当前代码未显式固定的SQL细节标待核验，不从C# string猜长度 |
| 默认 / 校验 / 约束 | 分别写代码初始化、DB DEFAULT、历史回填；PK/FK/唯一/Check及索引中的角色；字符比较/软删除条件明确 |
| 实现与证据 | 对应迁移步骤、仓储写入口、DTO/映射位置、结构/业务断言；已确定/待核验及原因分开 |

#### 已读代码的完整四字段示例：Identity迁移账本

本例来源为`src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Migrations/SchemaMigrationRecord.cs`及同目录`SchemaMigrationRunner.cs`；是当前代码的语义说明示范，不代表SQL Server映射已完成。

- 中文名称：Identity已应用迁移账本；类：`SchemaMigrationRecord`；实体类别：内部技术记录，无通用Entity基类字段，不是用户业务数据。
- 物理表名：映射声明为`identity_schema_migrations`，该声明未显式限定schema；SQL Server目标schema须在009物理字典确定。不能把本例直接作为已批准DDL。
- 一行含义：一个已执行并随事务提交的Identity迁移步骤，用于去重、历史描述/校验漂移检查及后续结构验证。PK为MigrationId，无业务NId或TenantNId，不通过业务API外露。
- 写入方：`SchemaMigrationRunner.ApplyStepAsync`执行步骤并在同一事务插入记录；应用失败回滚。`ApplyPendingAsync`读取历史记录并在满足检查时回填旧checksum。说明维护不得删除记录诱使重复执行，也不得改ID或Description掩盖漂移。

| 属性 / 列 / 中文名 | 含义、来源与写入时机 | 取值及人工维护注意事项 |
| --- | --- | --- |
| `MigrationId` / `migration_id` / 迁移步骤标识 | 来源SchemaMigrationStep.Id；执行新步骤时写入；主键用于判断该步骤是否已应用，Runner按ID的Ordinal顺序处理步骤 | 非空有效值由迁移定义提供；不得重命名已应用ID或拿安装包版本代替。类中string.Empty是构造初值，不代表合法迁移ID或DB DEFAULT |
| `Description` / `description` / 迁移定义说明 | 来源SchemaMigrationStep.Description；记录当时步骤含义，同时参与checksum输入 | 不是任意可编辑备注。旧账本checksum为空时须与当前步骤描述Ordinal相等才回填；修改历史描述会触发漂移。表/列数据库注释与此数据字段是不同对象 |
| `Checksum` / `checksum` / 步骤定义校验值 | Runner对UTF-8编码的Id、分隔符和Description计算SHA-256，小写hex；新步骤写入，旧记录在描述核对通过后可回填 | 新计算结果64字符；null表示兼容历史记录，不表示忽略检查。它不是SQL全文hash、文件hash或密码学签名；不得人工改成期望值绕过漂移 |
| `AppliedOn` / `applied_on` / 迁移记录时间 | ApplyStepAsync在步骤执行后、插入记录前读取DateTimeOffset.UtcNow；记录随当前事务提交后可见 | UTC时间点；不是数据库精确commit时间、业务发生时间或本地时区文本。示意`2026-09-14T01:00:00Z`；失败事务不应留下本行，不手调时间改变应用顺序 |

| 属性 | 当前.NET / 映射可空与键 | 已知默认与尚需核验的物理内容 |
| --- | --- | --- |
| MigrationId | string；非可空属性；SugarColumn显式PK | C#初值string.Empty；未声明列长度或DB DEFAULT，SQL Server/PG/SQLite实际映射按迁移及真实结构核验 |
| Description | string；非可空属性 | C#初值string.Empty；未声明列长度或DB DEFAULT，SQL Server索引/约束不由本例推断 |
| Checksum | string?；SugarColumn显式IsNullable=true | 兼容旧null；逻辑输出64位hex不等于当前物理列已限定长度64，目标类型/长度在009固定 |
| AppliedOn | DateTimeOffset；非可空属性 | 由Runner显式赋UTC值；代码未声明数据库时间DEFAULT。各Provider类型/精度与UTC往返验证在009固定 |

源码交付时补齐上述类/属性的中文XML说明，SQL Server表/列说明经受控迁移写入并能查询；不修改Description历史数据来润色注释。其他表按同样粒度展开全部字段，说明示例完整不代表全平台字段字典已经完成。

## 7. 验收矩阵与完成标准

| 案例 | 操作和可观察结果 |
| --- | --- |
| SQL-A01 两种引擎 | 分别记录2019/150、2022/160完整build/Edition；实际连接、参数化查询/事务、权限不足、TLS拒绝，不能只在2022/150测一次 |
| SQL-A02 拓扑/Host | 两引擎均验证Development Shared与PerService；UnifiedHost及独立服务经Gateway最小业务入口，目标库准确、漏映射拒绝、无自动回退 |
| SQL-A03 初始化 | 所有当前服务新库启动、合法前序SQL Server快照升级、二次启动幂等；两个进程同时初始化不重复迁移/种子；失败后账本与结构可恢复 |
| SQL-A04 Inspect/漂移 | 只读身份Inspect无写入；隔离样本缺列/缺索引确实拒绝，权限不可见不误报缺表，长名称/命名空间不会串服务；受控迁移后表/列说明可查询并与§6.5字典一致，Inspect不补写注释 |
| SQL-A05 数据语义 | UTC跨进程读回、decimal/long/GUID/Unicode、大小写/尾随空格、软删除/nullable唯一索引；无截断/舍入/错误去重 |
| SQL-A06 业务闭环 | 登录/刷新/角色授权→真实动态菜单与用户查询→SystemData/ReferenceData CRUD→PF05文本/附件合法上传绑定/撤回/未读/合规→PF06邀请/绑定/停止持久事实；扫描环境沿原安排，不假装真实ClamAV通过 |
| SQL-A07 并发/故障 | 消息序号/幂等重投、语音slot竞争、租约截止、Outbox稳定事实/重投、死锁/提交时断连；不重复副作用，不因Audit不可达阻止本地媒体停止 |
| SQL-A08 运维/升级 | SystemData计划/审批/备份/Apply/失败/Inspect语义可用；2019备份恢复到隔离2022，150与160分别业务回归，恢复证据完整 |
| SQL-A09 旧Provider回归 | PG/SQLite配置、旧迁移checksum、初始化、结构漂移及受影响业务仍通过；保留原真实PG修复，不重写已应用历史 |
| SQL-A10 客户端接入 | 同一API契约下Web及已可用原生客户端能登录/菜单/聊天；客户端不持有SQL连接信息，缺原生设备不冒充原生通过 |

SQL-A01～09完成且两引擎真实业务结果通过才可宣布SQL Server 2019/2022后端支持；SQL-A10按终端交付状态单列联合结果。原008只关闭终端范围；PF06A新增范围整包关闭须009也完成。平台数据库支持不冒充PF06网络质量/所有原生能力或用户延期项目已通过。

执行时先新鲜Release构建，再`dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build`，另按实际登记命令启用两种引擎隔离集成测试；读取失败/跳过数，未启用外部测试记NOT RUN，不能默认计PASS。独立验收复用稳定快照，主控不机械第三次重跑。

证据归`docs/evidence/PF-06A.md`的009小节，可链接SQL Server专项脱敏报告；记录引擎/兼容级别/驱动/拓扑/迁移清单/场景/命令/退出码及未验项。当前没有SQL Server运行或验收证据；本轮不创建虚假PASS记录。

## 8. 技术依据

引擎版本与兼容级别须分别验收，参见[Microsoft兼容级别文档](https://learn.microsoft.com/en-us/sql/t-sql/statements/alter-database-transact-sql-compatibility-level?view=sql-server-ver16)。版本/时间字段不能混用，参见[Microsoft rowversion文档](https://learn.microsoft.com/en-us/sql/t-sql/data-types/rowversion-transact-sql?view=sql-server-ver16)。其他方言、驱动、锁与部署细节在接入细化时按锁定版本核验；本文候选方案不作为已实现事实。
