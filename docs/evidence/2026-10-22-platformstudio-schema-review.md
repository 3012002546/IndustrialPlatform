# PlatformStudio单库schema与表分配复核

日期：2026-10-22；HEAD=`03e06f31ff99030d93c346c3a7f1e36587fe6297`。仅检查和修正规格，保留现有PF08/PF09文档WIP；“PF-08 低代码 | 主控”仍为只读准备、idle，本轮没有并写或发送任务。未连接正常数据库、未读取私有连接、未实施迁移或修改源码。

## 1. 源码核对结果

| 依据 | 本轮确认的事实 |
| --- | --- |
| [DatabaseTopologyResolver](../../src/backend/src/BuildingBlocks/IndustrialPlatform.SharedKernel/Topology/DatabaseTopologyResolver.cs) | Shared解析一个实际物理目标；显式IsStandalone才允许独立非Development Shared；SQL Server业务schema只接受dbo；PG SharedSchema不是每个服务自行生成的schema |
| [DevelopmentInfrastructureConfiguration](../../src/backend/src/BuildingBlocks/IndustrialPlatform.Web/Configuration/DevelopmentInfrastructureConfiguration.cs) | UnifiedHost为单连接；当前PG连接构造未写SearchPath。不能把配置声明或历史聊天当已生效schema，必须比对实际连接/元数据 |
| [已有独立配置投影](../../src/backend/src/Hosts/IndustrialPlatform.Collaboration.EmbeddedHost/Configuration/StandaloneConfiguration.cs) | 同一个受信独立输入投影物理库、SqlSugar和拓扑；PG显式设置SearchPath/SharedDatabaseSchema，SQL Server限制dbo。只参考其做法，不将Collaboration宿主当Studio依赖 |
| [SchedulerStorageSchema](../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Scheduler/SchedulerStorageSchema.cs)及[原生字段字典](../implementation/details/PF07-Hangfire存储字段字典.md) | PG/SQL Server引擎在scheduler_hangfire schema；SQLite引擎文件为主库绝对路径追加.scheduler.db，Prefix不隔离物理表。此为当前代码事实，不是新方案猜测 |
| [SqlServerMigrationArtifacts](../../src/backend/src/BuildingBlocks/IndustrialPlatform.Infrastructure/Database/SqlServerMigrationArtifacts.cs) | 当前helper的账本名白名单尚无Studio；实现时显式增加Studio归属，保留既有DDL/checksum，不开放任意表名 |

## 2. 文档修正

权威规则新增至[数据规格§6.3](../implementation/details/PF08-PF09-数据模型与生命周期.md#63-单库下的schema表和账本分配)，实施11/12升为V1.4并映射PF08-001、PF09-009/015及B30/D13～D15。

1. 撤回“PG一律硬编码platform_studio”的歧义：原生沿宿主已核验业务schema；ExternalMes新安装默认platform_studio且可显式配置。SQL Server业务表dbo，SQLite主文件main。studio-core是迁移单元键，四产品不分别建schema。
2. 逐表列出16张核心表；Dashboard/Playlist/TraceModel用共用resource/revision，不另开实体表。File六表、Audit四表、Jobs两业务表随所选适配安装，归同一业务schema；原生沿原服务，不复制一套Embedded。
3. 补清studio_schema_migrations、studio_seed_ledger两张宿主框架账本的归属/字段依据；16不是全安装物理表总数。Embedded三项全选为28张核心/运行表，另加2张账本和供应商引擎表。
4. 单库中的引擎存储单列：PG/SQL Server仍在同一数据库的scheduler_hangfire；SQLite是业务主文件加引擎文件，不能宣传严格单文件。未选Report不创建引擎存储；停用后的历史文件保留并纳入备份。
5. 固定独立数据库输入及向既有拓扑的唯一投影、实际schema/文件诊断、标识符检查、同名对象冲突和权限规则。初始化/仓储/Verify必须同目标；不改现有平台search_path、不接管MES表、不通过切schema绕过账本。

数据库原理交叉核对：[PG schema/search_path](https://www.postgresql.org/docs/current/ddl-schemas.html)、[SQL Server schema](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-schema-transact-sql?view=sql-server-ver17)、[SQLite main/temp/ATTACH](https://sqlite.org/lang_attach.html)。SQL Server本身支持自定义schema；本次dbo限制来自当前平台实现。SQLite附加库别名不等于同文件内业务schema；本轮不更换已验证引擎以追求单文件。

## 3. 本轮检查与待实施项

本轮新鲜检查如下，均退出0；脚本/结果位于忽略的`.run/`目录：

| 检查 | 结果 |
| --- | --- |
| `studio-schema-check.py` | 定义中的16张核心表与§6.3清单16/16一致；6/4/2张按需运行表和2张框架账本单列；三Provider、4项实际源码合同检查通过 |
| `pf08-pf09-doc-check.py` | 15份规格/索引、232个本地引用、12个JSON样例；24张任务卡/216字段、A16/B45及原13业务表结构检查通过 |
| `studio-adapter-doc-check.py` | 15份当前变更文档、200个本地引用、12个JSON样例；3身份表、24卡适配引用、D16及示例配置检查通过；无src/deploy或暂存改动 |
| `git diff --check` | 退出0；LF→CRLF提示沿现有autocrlf，未改Git配置 |

检查仅证明表清单/合同/源码依据一致，不代表已实现Studio初始化或三Provider实机验收。没有运行后端构建/业务测试，未改源码或数据库；之前未受影响的验证保持原范围。HEAD保持03e06f3，未提交或推送。

待实现验收：C0的三Provider新库与旧账本升级、PG原生/独立及自定义schema同目标、SQL Server dbo白名单、SQLite业务单文件；C3的引擎schema/派生文件、备份恢复、取消与Unknown；C5的所选包完整恢复。SQL2022等缺实机项仍待验证。用户如后续要求SQLite整个部署必须严格单文件，需单独验证存储适配及升级策略，不把当前双文件实现写成已满足。
