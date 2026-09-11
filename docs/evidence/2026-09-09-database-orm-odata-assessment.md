# 多数据库、SqlSugar / EF Core 共存与 OData 评估

评估日期：2026-09-09。状态：技术评估，尚未采用为开发基线。

补充：用户后续明确计划开源及商业实施，Provider 还须通过[组件许可与商业交付评估](2026-09-09-open-source-commercial-delivery-assessment.md)；尤其是 SqlSugar 间接驱动和官方 MySQL EF Provider / MySql.Data 的授权边界。下列“技术支持”不代表已经获得再分发许可。

用户已澄清“同时监控”为“同时兼容两种 ORM 和上述数据库”。本文评估 PostgreSQL、SQLite、SQL Server、MySQL、Oracle，以及 SqlSugar 与 EF Core 共存或逐步替换的可行性。本轮只核查代码与官方资料，没有安装依赖、运行兼容性 PoC、修改业务代码或执行数据库迁移。工作区存在正在进行的 PF-05 等开发改动，以下是读取时的现状，不能当作已验收结果。

## 1. 结论与建议

**可以设计为同时支持两种 ORM 和多种数据库，但当前平台还不具备全部组合的运行能力。建议保留现有 SqlSugar，先验证 EF Core 的小范围接入，再按持久化边界逐步推广。** 多数据库适配和 ORM 替换应作为两个独立决策，避免把客户数据库兼容需求扩大成全平台重写。

| 问题 | 评估结论 |
| --- | --- |
| SqlSugar 是否仍在维护？ | 是。核查时 NuGet 最新为 5.1.4.219，2026-09-04 更新；仓库固定 5.1.4.216。维护状态本身不构成更换理由。 |
| 能否改为 EF Core？ | 可以。收益主要是 LINQ 查询组合、显式模型配置、变更跟踪和 .NET 生态；代价主要在现有仓储、事务、迁移和 SQL 方言，而不只是替换包。 |
| 能否同时使用两种 ORM？ | 可以作为平台级能力。同一进程仍需先解决驱动依赖兼容；同一持久化边界应明确写入、事务和迁移所有者。 |
| 能否兼容五种数据库？ | 两种 ORM 均有相应数据库访问路径；平台要逐项适配和验收。数据库品牌相同但服务器版本不同，也不能直接认定兼容。 |
| EF Core 是否更适合 OData？ | 标准 LINQ / IQueryable 路径更直接，但平台已经实现 OData 查询解析，不必为现有功能更换 ORM。完整 OData 协议支持是另一项 API 契约工作。 |
| 当前宜采用什么方向？ | SqlSugar 保持现有默认；EF Core 先做隔离验证；新增客户数据库以明确交付需求排序，建议 SQL Server 先行，MySQL / Oracle 按实际服务器版本推进。 |

维护情况依据：[SqlSugar 官方 NuGet 包](https://www.nuget.org/packages/SqlSugarCore)。数据库覆盖依据：[SqlSugar 官方项目](https://github.com/DotNetNext/SqlSugar)、[EF Core Provider 清单](https://learn.microsoft.com/en-us/ef/core/providers/)。这些资料证明存在访问能力，不证明本平台已兼容。

## 2. 先区分三种客户需求

| 场景 | 应对方式 | 对当前平台的影响 |
| --- | --- | --- |
| 平台连接客户 MES 的 SQL Server / MySQL / Oracle | 优先调用 MES 公开 API；必须直连时，以受控外部适配器访问约定视图或接口表 | 平台自有库可以继续使用 PostgreSQL；客户表不进入平台迁移管理，禁止任意跨库直写 |
| 标签、协作等相对独立功能，安装时必须使用客户指定数据库 | 为该交付单元及其必需依赖提供已验证的 Provider、迁移和初始化支持 | 需要覆盖该单元完整业务链；仅标签表兼容 SQL Server，而必需的本地身份/文件持久化仍强制 PG，不能声称可只用 SQL Server 交付 |
| 整个平台全部业务关系数据迁往客户指定数据库 | 覆盖所有必需服务、表、初始化器、查询、后台任务和运维路径 | 最大工作量，涉及存量数据迁移及恢复演练；不能通过一个连接字符串开关完成 |

PF-05、PF-06、PF-08、PF-09、PF-10、PF-10A、PF-10B、PF-11继续遵循“平台原生优先、同一业务核心按需独立交付”。相对独立不等于每个 PF 都有独立进程、DbContext、迁移账本或任意数据库选择权。共享初始化单元仍按蓝图 32 / 33 管理。

同时连接不同品牌数据库，应分别配置可信连接目标和作用域；不保证跨数据库原子事务。跨服务一致性继续通过已有公开契约、事件与 Outbox 处理。数据库不可用不能自动回退到另一 Provider，更不能将连接切换等同于数据迁移。

PostgreSQL / TimescaleDB 的时序能力、Redis、文件对象存储等也不因业务关系库兼容五种数据库而自动获得等价替代。SQLite 当前是显式选择的 Development 替身；若终端离线运行另需 SQLite，要单独定义其同步和数据生命周期，不能直接扩大当前服务端 SQLite 的生产支持承诺。

## 3. 当前仓库事实

| 已核查位置 | 当前行为与含义 |
| --- | --- |
| [Directory.Packages.props](../../Directory.Packages.props)、[global.json](../../global.json) | SDK 10.0.302；SqlSugarCore 5.1.4.216、Npgsql 5.0.18、Microsoft.Data.Sqlite 10.0.9、Microsoft.AspNetCore.OData 9.5.0；当前中央包清单未配置 EF Core |
| [DatabaseProvider.cs](../../src/backend/src/BuildingBlocks/IndustrialPlatform.SharedKernel/Topology/DatabaseProvider.cs) | 平台拓扑枚举只定义 Sqlite / PostgreSQL，并明确 SQLite 为开发替身 |
| [DatabaseTopologyOptions.cs](../../src/backend/src/BuildingBlocks/IndustrialPlatform.SharedKernel/Topology/DatabaseTopologyOptions.cs) | 已有 Shared / PerService 与服务目标映射；仍有 PG / SQLite 专属配置语义，不能直接据此宣称异构目标已支持 |
| [SqlSugarDbContext.cs](../../src/backend/src/BuildingBlocks/IndustrialPlatform.Infrastructure/Database/SqlSugarDbContext.cs)、[SqlSugarServiceCollectionExtensions.cs](../../src/backend/src/BuildingBlocks/IndustrialPlatform.Infrastructure/Extensions/SqlSugarServiceCollectionExtensions.cs) | 包装 SqlSugarScope；上下文按 Singleton 注册，通用 Repository / IUnitOfWork 按 Scoped 注册。EF DbContext 不能照搬该 Singleton 生命周期 |
| [SqlSugarUnitOfWork.cs](../../src/backend/src/BuildingBlocks/IndustrialPlatform.Infrastructure/Transaction/SqlSugarUnitOfWork.cs) | SaveChangesAsync 实际提交已有 SqlSugar 事务；与 EF 将跟踪变更写入数据库的 SaveChanges 语义不等价 |
| [SqlSystemDataWriteTransaction.cs](../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Reliability/SqlSystemDataWriteTransaction.cs) | 业务写入、审计与 Audit Outbox 共享 SqlSugar 事务。只将业务 Repository 换成 EF 会破坏原子性假设 |
| [ODataQueryDescriptorParser.cs](../../src/backend/src/BuildingBlocks/IndustrialPlatform.Web/Querying/ODataQueryDescriptorParser.cs)、[SqlSugarQueryAdapter.cs](../../src/backend/src/BuildingBlocks/IndustrialPlatform.Infrastructure/Querying/SqlSugarQueryAdapter.cs) | 官方 OData 解析器生成 QueryDescriptor，再由 SqlSugar 适配器转换为查询；解析层和 ORM 已有边界 |
| [UsersODataController.cs](../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Api/Controllers/UsersODataController.cs) | 经应用服务和可信用户上下文返回平台分页结果，明确不暴露 IQueryable / EnableQuery |
| [CollaborationSchemaMigrations.cs](../../src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/CollaborationSchemaMigrations.cs) | 当前工作区已有 PF-05 实现；显式迁移包含 PG / SQLite 类型映射、PRAGMA、current_schema 和原生 DDL，不是尚未接触持久化的空白模块 |

只读扫描 `src/backend/**/*.cs`，排除 `bin/obj`，发现 122 个文件命中 SqlSugar 类型/命名空间/映射特征：BuildingBlocks / Hosts 等 9、Identity 48、SystemData 39、ReferenceData 22、Collaboration 4。另有 41 个文件命中 `ON CONFLICT / PRAGMA / timestamptz / jsonb / pg_advisory / RETURNING` 等方言候选特征。数字是评估时快照，可能含注释，不是必改文件数，也未包含仓库根目录下的 `tests/`。

## 4. 数据库与 EF Provider 可选路径

下表记录本次核查事实，不是 TODO 的固定分支或版本前置条件。实施前应重新核对依赖矩阵，并以真实服务器版本验收。

| 数据库 | SqlSugar 路径 | EF Core 路径 | 本平台需要特别补齐 |
| --- | --- | --- | --- |
| PostgreSQL | 当前已使用 | Npgsql.EntityFrameworkCore.PostgreSQL，已有 EF Core 10 Provider | 当前 Npgsql 5.0.18 与候选 Provider 的 10.x 驱动要求；UTC / Guid / jsonb、原生 SQL、迁移与锁回归 |
| SQLite | 当前开发路径已使用 | Microsoft.EntityFrameworkCore.Sqlite | decimal / DateTimeOffset 查询语义、表重建迁移、外键和锁；不能代替其他数据库集成测试 |
| SQL Server | SqlSugar 支持该 Provider | Microsoft.EntityFrameworkCore.SqlServer | compatibility level、Unicode / collation、DDL、分页、参数数量、并发及初始化权限 |
| MySQL | SqlSugar 当前包依赖 MySqlConnector | 官方 MySql.EntityFrameworkCore 或 Pomelo，需选定并验证 | MySQL 实际版本、字符集、大小写、DDL 提交行为、Guid / 时间 / JSON / 索引；两个 Provider 不能视为可无差别互换 |
| Oracle | SqlSugar 支持该 Provider，但仍受底层 ODP.NET 版本约束 | Oracle.EntityFrameworkCore；官方资料明确 23.26.0 起支持 EF Core 10 | 现代驱动的服务器支持范围、schema / user、空字符串、数字布尔、时间与 Guid 映射、DDL / 权限 / 标识符长度 |

具体版本证据：

- Npgsql EF Provider 10.0.0 要求 EF Core `>=10.0.0,<11.0.0`、Npgsql `>=10.0.0`。[官方包依赖](https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL/10.0.0)
- 核查时官方 MySql.EntityFrameworkCore 为 10.0.9，其 net10.0 资产要求 EF Core >=10.0.9、MySql.Data >=26.7.0；Pomelo 发布页最新稳定项为 9.0.0，匹配 EF Core 9，不能直接配 EF Core 10。微软 Provider 总表和厂商概览可能更新不同步，实施以所选包的实际依赖、厂商说明及验证结果共同判断。[MySQL 官方包](https://www.nuget.org/packages/MySql.EntityFrameworkCore)、[Pomelo 发布说明](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql/releases)
- Oracle 当前 ODP.NET Core 系统要求为 Oracle Database 19c 或更新版本。因此客户若是 11g / 12c，不能承诺本次候选 EF Core 10 组合支持；需要另行确定受支持连接方案或服务器升级路径。继续用 SqlSugar 也不能绕过其底层驱动限制。[Oracle EF 要求](https://docs.oracle.com/en/database/oracle/oracle-database/26/odpnt/InstallEFCoreRequirements.html)、[ODP.NET 系统要求](https://docs.oracle.com/en/database/oracle/oracle-database/26/odpnt/InstallSystemRequirements.html)
- SQLite 对 DateTimeOffset、decimal 等类型的部分比较和排序有 Provider 限制。工业计量数据不能为了兼容而一律转成 double，也不能先全表拉到内存再过滤分页。[EF SQLite 限制](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations)

尚缺客户数据库的精确大版本、补丁、兼容级别、字符集、部署权限和实际使用场景。因此目前只能给出候选路径，不能给出五库生产认证结论。

## 5. 双 ORM 的合理边界

### 5.1 推荐共存方式

1. Domain / Application 保持已有业务端口；SqlSugar 与 EF Core 的查询、映射和持久化留在 Infrastructure。无需为了兼容一次性建立覆盖全部 LINQ / SQL 功能的“万能 ORM”。
2. 同一部署可以包含 SqlSugar 持久化单元和 EF 持久化单元，选择由服务装配与可信配置决定；不要向业务页面开放任意切换 ORM 的按钮。
3. 一个聚合写操作及其必须同事务的审计 / Outbox 使用一个事务所有者。既有共享事务中的模块，要么整体保持 SqlSugar，要么以完整事务链为单位迁移。
4. 同一表只设一个 Schema 迁移所有者；按现有初始化单元管理 Migration / Seed / Bootstrap / Verify / Ledger。更换 ORM 不自动拆出新账本，不重复执行两套建表工具。
5. EF 只读投影试点可读取本模块既有表，关闭不必要跟踪，由现有初始化器继续管理 Schema；这不授权跨模块绕过公开契约读表。
6. EF Context 按请求或独立操作创建；后台任务使用适当的作用域 / 工厂，不跨并发任务共享实例。注入时按实际持久化边界区分，避免两个 IUnitOfWork 注册互相覆盖。

### 5.2 同一事务混用两种 ORM

不建议作为默认开发方式。EF 支持外部 DbConnection / DbTransaction，但要与 SqlSugar 共用事务，必须实证两边使用**同一个底层连接对象、同一个事务对象、兼容的 ADO.NET 驱动**，并明确连接释放、提交、回滚、重试和跟踪缓存责任。连接字符串相同或 DI Scope 相同都不够。EF 官方互操作说明不等于已证明当前 SqlSugar 封装支持该组合。[EF 事务说明](https://learn.microsoft.com/en-us/ef/core/saving/transactions)

例如 SqlSugar 当前 MySQL 路径使用 MySqlConnector，而官方 MySQL EF Provider 使用 MySql.Data，二者不能直接共用同一个连接 / 事务对象。同进程分别访问不要求共用事务，但每条写入链仍需保持自己的原子性。

### 5.3 驱动依赖是先行验证项

当前中央清单固定 Npgsql 5.0.18，候选 EF PG Provider 要求 Npgsql >=10.0.0。SqlSugar 5.1.4.216 的 NuGet 要求是 Npgsql >=5.0.18，没有证明它能在 Npgsql 10 下正确工作。应先验证依赖解析和实际运行；既不能宣称必然不兼容，也不能因为 NuGet 允许较高版本就认定兼容。[SqlSugar 当前包依赖](https://www.nuget.org/packages/SqlSugarCore/5.1.4.216)

验证包括参数绑定、时间映射、事务、批量操作及现有原生 SQL。SQL Server / Oracle 等共享驱动也要检查传递依赖。若共进程依赖确实无法兼容，再评估升级受支持驱动或利用已有独立 Host 边界隔离；不默认引入自定义程序集加载框架。

## 6. OData 的实际收益与保留边界

当前链路为：`OData 参数 → 官方解析器 → QueryDescriptor 校验 → 应用层权限 / 数据范围 → SqlSugar 查询适配 → 平台响应`。

已支持的查询选项包括 `$filter / $select / $orderby / $top / $skip / $count`，并有字段、操作符、分页等限制。更换 ORM 时，可以新增内部 EF 查询适配，将同一 QueryDescriptor 转成可翻译 LINQ；前端请求、可信租户上下文和分页响应无需因此改变。

EF 的价值是减少自行拼接部分 SQL 的工作，并让查询投影更贴近 IQueryable。OData 官方查询选项本身并不强制 EF。引用的官方文档是 Web API 8 的机制说明，当前仓库是 OData 9.5.0；两者不能作为 EF Core 10 组合已通过测试的证据。[OData 查询选项](https://learn.microsoft.com/en-us/odata/webapi-8/fundamentals/query-options)

若后续需要标准 EDM、`$metadata`、`@odata.context / @odata.count / @odata.nextLink`、关联展开或聚合，应另行定义协议范围和前端兼容策略。加上 EF 或 EnableQuery 不会自动满足这些契约。

不论 ORM 如何选择，都必须保留字段白名单、租户与数据权限、软删除规则、稳定排序、分页上限、取消 / 超时和导出权限；禁止将任意 DbSet 暴露为无限查询入口。`$count` 与数据列表必须使用相同授权范围；不能以 EF 全局过滤器替代全部业务授权。查询失败不能静默改为内存执行。

## 7. 具体修改范围

以下为采用方案后需要修改的位置，不表示本轮已经实施。相对工作量按代码职责评估，不给未经 PoC 的人日承诺。

| 范围 | 现有入口 / 位置 | 所需修改 | 相对工作量 |
| --- | --- | --- | --- |
| 包与 Host 装配 | Directory.Packages.props、各 Infrastructure / Host csproj、各服务 DependencyInjection | 明确选用 ORM / Provider 的兼容组合，处理传递依赖、作用域和启动校验；不把所有 Provider 强制带入每个交付包 | 中 |
| 拓扑与配置 | SharedKernel/Topology、SystemData.Infrastructure/Topology、各 Host 配置 | 扩展 Provider 与可信目标解析；保留逻辑身份、Shared / PerService 及 fail-fast；区分运行连接与初始化权限 | 中高 |
| 物理目标编排 | SystemData.Infrastructure/DatabaseOrchestration/Runner 中现有 PostgreSqlTargetDatabaseAdapter / SqliteTargetDatabaseAdapter | 增加必要的 SQL Server / MySQL / Oracle provision、inspect、锁及取消语义；支持客户预建库模式，不假设 runtime 账号可建库 | 高 |
| 仓储和映射 | Infrastructure/Repository、各服务 Persistence / Entities / Repository | 试点增加 EF 实现及显式映射；保留业务端口、字段含义、公共 DTO，不机械给所有仓储各复制一份 | 高，随采用范围变化 |
| 事务和可靠性 | Infrastructure/Transaction、各服务 Reliability、审计 / Outbox / 后台租约 | 对齐立即执行与变更跟踪语义，验证失败回滚、重试、幂等及后台任务作用域 | 高 |
| 迁移和漂移 | 各服务 Persistence/Migrations、服务初始化器、SchemaPhysicalDriftGuard | 方言化 DDL / 元数据读取 / 类型比较；保留已应用迁移事实，处理基线映射、锁、失败恢复和 readiness | 高 |
| 查询和 OData | Web/Querying、Infrastructure/Querying、Identity 用户查询链 | 保留 QueryDescriptor；新增 EF 适配或完善现有方言处理；验证 LIKE 转义、排序、分页、权限与错误语义 | 中高 |
| 数据库专属功能 | 原生 SQL、唯一索引、租约抢占、批量导入、JSON / 时序查询 | 能力按 Provider 显式声明；必要时为某交付组合禁用依赖专属能力的功能，不假装全库等价 | 按模块评估 |
| 部署和验收 | deploy、配置样例、tests、初始化 / 备份说明 | 提供真实数据库集成测试及安装 / 升级样例，明确受支持的服务器版本、最小权限和恢复步骤 | 高 |

迁移所有权建议：试点先继续使用现有显式迁移，只增加 EF 数据访问映射；后续若采用 EF Migrations，则每个 Provider 都要维护相应迁移集合，并接入现有服务初始化与账本契约。不能对旧库直接生成一份 InitialCreate 并重建，也不能用 EnsureCreated 掩盖升级路径。[EF 多 Provider 迁移规则](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/providers)

### 7.1 字段规格需要增加的内容

以下是待确认的映射候选，不是直接执行的 DDL。现有物理格式优先兼容；具体长度、精度、NULL、默认值、索引与约束仍须逐表确定。

| 逻辑字段 / 类型 | PG | SQLite | SQL Server 候选 | MySQL 候选 | Oracle 候选 |
| --- | --- | --- | --- | --- | --- |
| `Id / ConcurrencyVersion : Guid` | uuid | 沿用当前 TEXT 格式 | uniqueidentifier | binary(16) 或 char(36)，明确字节序 / 格式 | RAW(16)，明确字节序 |
| `OptimisticVersion : long` | bigint | INTEGER | bigint | bigint | NUMBER(19,0)，另校验 Int64 范围 |
| `IsDeleted / IsFrozen / IsLocked : bool` | boolean | INTEGER + 0/1 约束 | bit | tinyint(1) + 约束 | 以 19c 为目标时 NUMBER(1) + 0/1 约束 |
| `CreatedOn / LastUpdatedOn : DateTimeOffset` | timestamptz，统一 UTC | 沿用规定的 UTC 存储与比较格式 | datetimeoffset(p) | datetime(p) 保存 UTC，转换由代码明确处理 | TIMESTAMP(p) WITH TIME ZONE |
| `NId / TenantNId : string` | 有明确长度与约束的字符串 | TEXT + 长度 / 语义校验 | nvarchar(n) | varchar(n)，明确 utf8mb4 和 collation | VARCHAR2(n CHAR)，明确字符集及空串处理 |
| 业务精确数值 `decimal` | numeric(p,s) | 逐项确定精确存储与查询策略 | decimal(p,s) | decimal(p,s) | NUMBER(p,s) |
| JSON 负载 | 按现有列使用 jsonb / text | TEXT | 按目标版本选择并验证，不能假设与 jsonb 等价 | 按目标版本 JSON 或受校验文本 | 按目标版本 JSON 或 CLOB + 校验 |

`ConcurrencyVersion` 保持现有 Guid 契约，不因 SQL Server 有 rowversion 就更改 API；`OptimisticVersion` 仍是 long 的既有语义。长整数 JSON 表达、时间精度截断、空字符串与 NULL、软删除后的唯一性、大小写比较、索引键长度，都应写进派遣前规格。共享唯一性语义不代表五库可以使用完全相同的索引 DDL。

### 7.2 若采纳，需要同步的文档

| 文档 | 同步内容 |
| --- | --- |
| 蓝图 07、12、25、26 与 README | 从“默认技术栈”中分清可选持久化实现和已验收数据库范围；区分通用逻辑类型与 PG 专属物理规范 |
| 蓝图 27 | 保留现有 OData / QueryDescriptor 契约，明确是否另外扩展完整 OData 协议 |
| 蓝图 32、33 | ORM 选择不得改写 Host / Module / Initialization Unit / Deployment Unit 区分；补充异构目标、迁移所有权、权限、锁和恢复 |
| 开发总 TodoList、实施方案 02 / 03 / 05 / 06 / 07，以及受影响 PF 方案 | 写清采用范围、前置条件、字段映射、事务归属、迁移入口、验证证据与回退条件；不写固定代码分支 / 提交版本 |
| STANDARD-派遣前详细设计与页面验收、TEMPLATE、PF 详细规格 | 增加“受支持 ORM × Provider × 服务器版本范围”和字段差异栏；继续先完成任务细化和线条图，再进入派遣 |
| PF-05 / PF-06、终端运行时、标签与后续独立能力方案 | 说明交付单元连同必需依赖支持哪些数据库；共享初始化单元未能裁剪时，不能只凭模块命名宣称可独立安装 |

本次保持上述正式蓝图和开发 TODO 的既有技术决策不变，避免把“可行性评估”写成“已经选型 / 已经兼容”。采纳后应将对应蓝图、TODO 和详细规格一起修改并再做语义同步检查。

## 8. 推荐的验证顺序与前置条件

| 步骤 | 前置条件 | 交付物与通过条件 |
| --- | --- | --- |
| A. 明确兼容范围 | 客户数据库品牌 / 版本 / 兼容级别、权限、实际集成或自有库替换需求明确 | 每个交付单元的支持矩阵；清楚标注未支持项，避免直接承诺十种组合 |
| B. 双 ORM 依赖验证 | 选定符合当前 .NET 基线的 EF 与驱动组合；使用隔离测试环境 | 同一 Host 加载两种 ORM，PG / SQLite 基本查询、参数绑定、事务运行正常；消除依赖降级与运行时方法缺失问题 |
| C. 查询适配试点 | B 通过；选定本模块只读投影，保留现有 API 和 Schema 所有者 | 相同授权上下文下，两种适配器过滤、计数、分页、投影结果等价；禁止全量内存回退 |
| D. 完整写入试点 | 选定独立小写入单元或隔离夹具；明确事务与迁移所有者 | 新增 / 更新 / 并发冲突 / 幂等 / 回滚 / 审计 Outbox 全链通过，保证失败时无半提交 |
| E. 新数据库纵向验证 | 优先选择有明确需求的 SQL Server；准备真实服务器 | 该交付单元从空库安装、旧数据升级、运行查询到后台任务全链通过；MySQL / Oracle 按相同标准逐个推进 |
| F. 正式采纳与同步 | 记录已验证矩阵、未支持能力、迁移 / 恢复方案 | 统一修改蓝图 / TODO / 详细规格；再标记可派遣。现有 PF 不因本评估自动重排或自动改用 EF |

这是一组评估后的候选工作，不是已经创建或派遣的开发任务。EF 优先选择隔离且容易验证的路径，不能将当前已有 PF-05 代码当作空白起点推倒重做。

## 9. 验收与回退要求

- **依赖与装配**：两种 ORM 同进程加载；不同持久化单元配置不会串库；未知 Provider 和不支持组合在启动 / 初始化阶段明确失败。
- **查询与安全**：同一授权范围、软删除规则、字段白名单；测试 NULL、空串、中文、大小写、特殊 LIKE 字符、时间与 decimal 比较、稳定分页、count 和导出。输入超限与查询不可翻译时返回规定错误。
- **写入与并发**：校验 Guid 并发版本、long 版本、幂等冲突、后台租约争用；业务、审计和 Outbox 失败一起回滚；批量操作不得绕过原有生命周期规则。
- **初始化与升级**：空库、已有库、重复执行、中断重试、物理漂移、缺权限、并发初始化均有结果；保持账本事实可追溯。目标数据库可能有不同 DDL 提交行为，不能只用业务事务回滚测试代替迁移恢复验证。
- **真实数据库**：PG / SQL Server / MySQL / Oracle 各用对应服务器验证已声明的组合；SQLite 测试通过不代表其他库通过。Oracle 使用符合目标版本的验证环境。
- **性能**：针对相同数据规模、SQL 与索引比较延迟、扫描量、连接数和内存；结果出来前不声称某 ORM 一定更快。长查询具备超时和取消；拒绝用内存分页掩盖翻译失败。
- **恢复**：试点读取可恢复原适配器；写入切换需保证 Schema / 数据仍向后兼容，否则按验证过的备份恢复或前向修复执行。切回 ORM 配置不等于撤销已发生的数据或 Schema 变化。

已有测试可作为扩展入口：`tests/BuildingBlocks/.../ODataQueryDescriptorParserTests.cs`、`SqlSugarQueryAdapterTests.cs`、`QueryDescriptorTests.cs`，以及 Identity 查询 / 迁移测试、SystemData 拓扑 / 初始化测试。采用后还需补充真实数据库集成用例，不能仅重复实现逻辑的单元断言。

本轮完成的是代码静态核查、官方资料核查和修改范围评估。没有执行运行兼容性测试；因此结论是“具备可行路径，需按矩阵验证”，不是“十种组合已经兼容”。
