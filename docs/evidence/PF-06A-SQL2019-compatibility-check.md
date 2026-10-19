# PF-06A SQL Server 2019 全表兼容核查（更新于 2026-10-06）

## 2026-10-06 领域命名调整

当前安全表为 `identity_service_call_nonce`、`identity_step_up_grant`。追加改名迁移，保留已应用历史脚本和 checksum；SQL Server 2019 实测新旧对象 ID 与记录数一致，旧表名不再存在，109 表／1777 字段及 1886 条说明数量不变。再次认证授权和防重放在新映射下真实读写通过。正常 PostgreSQL 运行库未启动或迁移。

表实体类型原本已按用途命名，本轮同步把 `Pf04Store` 改为 `SystemDataOperationalStore`、`Pf04ServiceException` 改为 `SystemDataOperationException`，前端 `Pf04Api` 改为 `SystemDataOperationsApi`，更新文件名及所有引用。SQLite 两项新增迁移回归验证数据/账本保留、重复执行和新旧名冲突回滚；前端类型检查通过，相关 6 文件／48 测试通过。历史版本中的任务名和迁移 ID 仅用于追踪，不是当前表或模型名。

最终后端回归：新鲜 Release 编译 0 警告、0 错误；全量 1915 通过、0 失败、7 跳过，命令均 exit code 0。通过数为 BuildingBlocks 176、Collaboration 168、Gateway 14、Identity 623、Integration 13、ReferenceData 262、SystemData 636、UnifiedHost 23。7 项为未启用的外部 PG/Redis/RabbitMQ 测试；正常 PG 库及 SQL Server 2022 未实机验证本次改名。

```powershell
dotnet build src/backend/IndustrialPlatform.slnx --configuration Release -p:PF06A_SQLSERVER_ENABLED=1 --verbosity quiet
$env:PF06A_SQLSERVER_ACCEPTANCE='1'
dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build -m:1 --verbosity minimal
# 在 src/frontend 执行（使用本机已安装的 Node，无重新安装依赖）
node node_modules/vue-tsc/bin/vue-tsc.js --build
node node_modules/vitest/vitest.mjs run tests/components/Pf04ManagementPages.spec.ts tests/components/FileUploadPage.spec.ts tests/components/NotificationCenterPage.spec.ts tests/components/shell/PlatformSessionControls.spec.ts tests/unit/sessionStore.spec.ts tests/unit/notificationHub.spec.ts --reporter=dot
$env:VITE_DEPLOYMENT_ENVIRONMENT='DEV'
node node_modules/vite/bin/vite.js build
```

前端 DEV 生产构建退出 0，入口及改名后的 API 引用可正常打包；存在大于 500 kB 的 chunk 提示，未将其作为本轮 SQL Server 接入的优化范围。首次未指定环境时被现有构建校验拒绝，补充 DEV 环境标识后通过，未修改或绕过校验。

提交前整体复核再次执行上述后端编译/测试门禁，仍为 1915 通过、0 失败、7 跳过，两条命令退出 0。验证前后 220 个源码、项目及 SQL 文件摘要一致，未用旧编译产物验证新源码。暂存范围仅含本轮接入、命名调整、对应测试与文档；私有凭据比对无匹配，构建产物和本地手动文档编辑排除。未发现新的提交阻断问题；PF07 后续先按既有派遣标准细化，不把本轮未测部署项或终端遗留验收虚标完成。

## 2026-09-30 主控 SQL Server 适配收尾记录

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

回归排障记录：首次并行全量有配置夹具覆盖及宿主查询失败；修正夹具后串行仍出现 ReferenceData readiness 503 和宿主角色查询 500。定向日志抓到 SqlSugar 映射列表的 Collection was modified，不能按偶发环境问题忽略。最小八路测试在修复前实际只有 1 个 ContextID（FAIL），改为 Scope 配置回调设置超时、从 DbType 配置快照读取 Provider 后，八路 ContextID 独立且超时仍为 30（PASS）。最终门禁必须使用此修复后的新鲜编译。

2022 测试入口已复用：在同一私有文件增加 SqlServerAcceptance:2022:ConnectionString（指向 industrial_platform_sql2022），设置 PF06A_SQLSERVER_ENGINE_YEAR=2022 再按同一构建/测试命令执行。入口会要求实际引擎 major=16 和对应隔离库，不接受 2019 冒充。当前未设置或执行 2022 入口。

历史处理：本轮初始空库测试暴露 SqlSugar 两个同名 SchemaMigrationRecord 串表。现已重命名 SystemData 类型并显式 AS；仅按已确认完整一致条件移除本轮误入 Identity 的 58 条 SystemData 记录，保留正确账本、checksum 和业务数据。ReferenceData 长度 CHECK 错误追加 012 修复，中文说明追加 013；Identity/SystemData 描述 v2 修正安全 nonce 归属，旧已应用描述 ID 保留。历史业务迁移未重写。

以下记录为历史快照，早期“只能连接、尚不能迁移”的判断已由本节覆盖；未测边界仍有效。


## 历史初始核查（当前结论以上方最新状态为准）

当时结论为未通过，尚不能将平台运行库切换为 SQL Server。清点出的 109 张表全部纳入兼容交付范围，连接和只读查询仅证明驱动链可用；当时表迁移、仓储 SQL、初始化和数据库编排仍有未适配入口，未完成全表建库/读写验收。2022 当时没有实例，未测。

## 1. 实际验证与结果

| 检查 | 结果与界限 |
| --- | --- |
| 新鲜 Release 构建 | `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release`：退出 0，0 警告、0 错误 |
| 构建后完整后端测试 | `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build`：退出 0；1906 通过、7 跳过、0 失败。跳过项为未启用的 PG/Redis/RabbitMQ 外部测试；本命令没有 SQL Server 全表 fixture |
| SQL Client 5.2.2 实例核对 | SQL Server 2019，15.0.2000.5，`industrial_platform_sql2019`，兼容级别 150，排序规则 `SQL_Latin1_General_CP1_CI_AS` |
| 项目实际 SqlSugar 路径 | `SqlSugarDbContext(DbType.SqlServer)` 的 `SELECT CAST(1 AS int)` 成功，未绕过项目数据库上下文 |
| 全表清点 | Infrastructure 中全部 SugarTable 映射，与当前迁移函数实际生成的 SQLite 建表语句取并集；补入 ReferenceData 两本 ledger。共 109 个服务/表目标。清点不等于这些表已在 SQL Server 存在 |
| DDL 函数核对 | 三个服务逐个调用全部单参数 `DbType → string` 的迁移函数，另检查 ReferenceData 十个 bool 方言函数；259 次方言生成调用/合同核对，其中 93 个 SQL Server 检查见 §4 |
| ReferenceData 真实 Provider 拒绝路径 | 使用 SQL Server 上下文调用 `ApplyAsync`，在接触业务 SQL 前抛 `NotSupportedException`：迁移脚本尚未完成。未绕过此保护 |
| 运行库选择 | 从现有私有配置经 `DevelopmentInfrastructureConfiguration.Apply` 生成的运行 Provider 为 `PostgreSQL`；`SqlServerAcceptance` 不参与应用运行库选择 |
| 无数据库写入 | SQL Server 核对前后用户表数均为 0。未执行 CodeFirst、业务 DDL、迁移、种子或数据库清理 |

本轮临时探针只运行 DDL 字符串生成、项目的只读查询和 ReferenceData 已有拒绝分支，不启动 Host。三个可生成 SQL 的 SystemData 函数是基线 no-op/PG 专属条件函数，不能算业务表适配通过。已有迁移链阻断，逐表 CRUD、并发、时间精度、索引和故障矩阵为 NOT RUN，不能逐表虚标 FAIL 或 PASS。

## 2. 阻碍完整兼容的入口

| 服务/接入面 | 已证实缺口 | 后续完成条件 |
| --- | --- | --- |
| Identity | `IdentitySchemaMigrations.TypeWords` 不接受 SqlServer；22 个 DDL 生成入口拒绝 SQL Server，账本补列及部分 ALTER 路径仍按 SQLite/PG 分支 | 业务表、三层种子/账本、SSO、幂等与授权仓储在 SQL Server 初始化和读写，历史 PG/SQLite checksum 不变 |
| SystemData | `SystemDataMigrationHelpers.TypeWords` 不接受 SqlServer；46 个生成入口拒绝、7 个生成 SQLite/PG 专用语句 | 控制面、组织岗位、文件、通知、审计、Outbox/账本全部适配；包括当前源码执行 ALTER 的入口 |
| ReferenceData | 七模块及共享/完整性/缓存十个 SQL 函数仅有 bool PG/SQLite 合同；Apply 明确拒绝 SqlServer；编码规则、缓存 generation 等仍有 PRAGMA、ON CONFLICT、RETURNING | 24 张表、迁移/seed/checksum、七模块仓储、序列/幂等、缓存 generation 原子更新全部完成 SQL 分支 |
| Collaboration | 五个基础 DDL 入口拒绝 SqlServer；增量迁移含 timestamptz/CREATE TABLE IF NOT EXISTS；消息/预算写路径有 ON CONFLICT | 聊天、附件、合规、去重、事件 Inbox/Outbox、PF06 会话/语音 slot 的持久化与并发回归 |
| 数据库编排 | `DatabaseTargetAdapterRouter` 仅装配 PostgreSQL/SQLite；SQL Server Inspect/Apply/迁移锁等未装配 | 不仅能连接，还需目标身份、只读 Inspect、运行器错误码、迁移互斥与权限流程可用 |
| 独立协作宿主 | `StandaloneConfiguration` 只接受 PostgreSQL/SQLite；平台宿主当前统一私有配置加载也未完整验证生产配置路径 | 明确对应 Host 的 Provider/拓扑/凭据来源并验证；不能把改一个 Development 键宣称为正式部署支持 |

源码核对入口：

- [Identity 迁移](../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Migrations/IdentitySchemaMigrations.cs)、[账本运行器](../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Migrations/SchemaMigrationRunner.cs)
- [SystemData 类型/DDL 助手](../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Migrations/SystemDataMigrationHelpers.cs)、[完整迁移](../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Migrations/SystemDataSchemaMigrations.cs)
- [ReferenceData 初始化账本](../../src/backend/src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Infrastructure/Initialization/ReferenceDataInitializationLedger.cs)、[编码仓储](../../src/backend/src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Infrastructure/CodingRule/CodingRuleRepository.cs)、[缓存代数存储](../../src/backend/src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Infrastructure/Caching/ReferenceDataCacheGenerationStore.cs)
- [Collaboration 迁移](../../src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/CollaborationSchemaMigrations.cs)、[仓储](../../src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/SqlCollaborationRepository.cs)
- [数据库编排路由](../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/DatabaseOrchestration/Runner/DatabaseTargetAdapterRouter.cs)、[Standalone 配置](../../src/backend/src/Hosts/IndustrialPlatform.Collaboration.EmbeddedHost/Configuration/StandaloneConfiguration.cs)

## 3. 全表交付清单

每行状态均为“迁移链阻断，SQL Server 表/字段/索引/CRUD 未验”。这包含技术账本、种子、Outbox/Inbox、去重/租约与领域表，不只包含页面业务表；某本账本可能能由 CodeFirst 建立，也不能越过其服务完整迁移链判为完成。来源是实际实体映射和迁移生成函数；ReferenceData 表名按目标 dbo 平面前缀记录，PG schema 名不同。

### Identity（23 张）

| 表名 | 当前实体或 DDL 来源 | SQL Server 验收状态 |
| --- | --- | --- |
| `identity_bootstrap_credential` | IdentitySchemaMigrations.BootstrapCredentialDdl；Persistence.Entities.BootstrapCredentialTable | NOT RUN：迁移链未适配 |
| `identity_login_audit` | IdentitySchemaMigrations.LoginAuditDdl；Persistence.Entities.LoginAuditTable | NOT RUN：迁移链未适配 |
| `identity_operation_audit` | IdentitySchemaMigrations.OperationAuditDdl；Persistence.Entities.OperationAuditTable | NOT RUN：迁移链未适配 |
| `identity_outbox` | IdentitySchemaMigrations.OutboxDdl；Persistence.Entities.OutboxTable | NOT RUN：迁移链未适配 |
| `identity_permission` | IdentitySchemaMigrations.PermissionDdl；Persistence.Entities.PermissionTable | NOT RUN：迁移链未适配 |
| `identity_refresh_session` | IdentitySchemaMigrations.RefreshSessionDdl；Persistence.Entities.RefreshSessionTable | NOT RUN：迁移链未适配 |
| `identity_role` | IdentitySchemaMigrations.RoleDdl；Persistence.Entities.RoleTable | NOT RUN：迁移链未适配 |
| `identity_role_permission` | IdentitySchemaMigrations.RolePermissionDdl；Persistence.Entities.RolePermissionTable | NOT RUN：迁移链未适配 |
| `identity_schema_migrations` | Persistence.Migrations.SchemaMigrationRecord | NOT RUN：迁移链未适配 |
| `identity_seed_ledger` | IdentitySchemaMigrations.SeedLedgerDdl；Persistence.Entities.SeedLedgerTable | NOT RUN：迁移链未适配 |
| `identity_sso_browser_session` | IdentitySchemaMigrations.SsoBrowserSessionDdl；Persistence.Entities.SsoBrowserSessionTable | NOT RUN：迁移链未适配 |
| `identity_sso_client` | IdentitySchemaMigrations.SsoClientDdl；Persistence.Entities.SsoClientTable | NOT RUN：迁移链未适配 |
| `identity_sso_client_endpoint` | IdentitySchemaMigrations.SsoClientEndpointDdl；Persistence.Entities.SsoClientEndpointTable | NOT RUN：迁移链未适配 |
| `identity_sso_external_account` | IdentitySchemaMigrations.SsoExternalAccountDdl；Persistence.Entities.SsoExternalAccountTable | NOT RUN：迁移链未适配 |
| `identity_sso_provider` | IdentitySchemaMigrations.SsoProviderDdl；Persistence.Entities.SsoProviderTable | NOT RUN：迁移链未适配 |
| `identity_user` | IdentitySchemaMigrations.UserDdl；Persistence.Entities.UserTable | NOT RUN：迁移链未适配 |
| `identity_user_group` | IdentitySchemaMigrations.UserGroupDdl；Persistence.Entities.UserGroupTable | NOT RUN：迁移链未适配 |
| `identity_user_group_membership` | IdentitySchemaMigrations.UserGroupMembershipDdl；Persistence.Entities.UserGroupMembershipTable | NOT RUN：迁移链未适配 |
| `identity_user_group_role` | IdentitySchemaMigrations.UserGroupRoleDdl；Persistence.Entities.UserGroupRoleTable | NOT RUN：迁移链未适配 |
| `identity_user_role` | IdentitySchemaMigrations.UserRoleDdl；Persistence.Entities.UserRoleTable | NOT RUN：迁移链未适配 |
| `identity_write_idempotency` | IdentitySchemaMigrations.WriteIdempotencyDdl；Persistence.Entities.IdentityWriteIdempotencyTable | NOT RUN：迁移链未适配 |
| `pf05_service_call_nonce` | IdentitySchemaMigrations.TrustedServiceCallNonceDdl；Persistence.Entities.TrustedServiceCallNonceTable | NOT RUN：迁移链未适配 |
| `pf05_step_up_grant` | IdentitySchemaMigrations.StepUpGrantDdl；Persistence.Entities.StepUpGrantTable | NOT RUN：迁移链未适配 |

### SystemData（42 张）

| 表名 | 当前实体或 DDL 来源 | SQL Server 验收状态 |
| --- | --- | --- |
| `system_audit_fact` | Persistence.Entities.AuditFactTable；SystemDataSchemaMigrations.AuditFactDdl | NOT RUN：迁移链未适配 |
| `system_audit_ingress_failure` | Persistence.Entities.AuditIngressFailureTable；SystemDataSchemaMigrations.AuditIngressFailureDdl | NOT RUN：迁移链未适配 |
| `system_audit_lifecycle` | Persistence.Entities.AuditLifecycleTable；SystemDataSchemaMigrations.AuditLifecycleDdl | NOT RUN：迁移链未适配 |
| `system_audit_outbox` | Persistence.Entities.AuditOutboxTable；SystemDataSchemaMigrations.AuditOutboxDdl | NOT RUN：迁移链未适配 |
| `system_collaboration_file_hold` | Persistence.Entities.CollaborationFileHoldTable；SystemDataSchemaMigrations.CollaborationFileHoldDdl | NOT RUN：迁移链未适配 |
| `system_collaboration_file_reference` | Persistence.Entities.CollaborationFileReferenceTable；SystemDataSchemaMigrations.CollaborationFileReferenceDdl | NOT RUN：迁移链未适配 |
| `system_data_database_approval` | Persistence.Entities.DatabaseApprovalTable；SystemDataSchemaMigrations.ApprovalDdl | NOT RUN：迁移链未适配 |
| `system_data_database_backup_evidence` | Persistence.Entities.DatabaseBackupEvidenceTable；SystemDataSchemaMigrations.BackupEvidenceDdl | NOT RUN：迁移链未适配 |
| `system_data_database_environment_policy` | Persistence.Entities.DatabaseEnvironmentPolicyTable；SystemDataSchemaMigrations.EnvironmentPolicyDdl | NOT RUN：迁移链未适配 |
| `system_data_database_migration_observation` | Persistence.Entities.DatabaseMigrationObservationTable；SystemDataSchemaMigrations.MigrationObservationDdl | NOT RUN：迁移链未适配 |
| `system_data_database_operation` | Persistence.Entities.DatabaseProvisionOperationTable；SystemDataSchemaMigrations.OperationDdl | NOT RUN：迁移链未适配 |
| `system_data_database_operation_step` | Persistence.Entities.DatabaseOperationStepTable；SystemDataSchemaMigrations.OperationStepDdl | NOT RUN：迁移链未适配 |
| `system_data_database_plan` | Persistence.Entities.DatabaseProvisionPlanTable；SystemDataSchemaMigrations.PlanDdl | NOT RUN：迁移链未适配 |
| `system_data_database_plan_step` | Persistence.Entities.DatabasePlanStepTable；SystemDataSchemaMigrations.PlanStepDdl | NOT RUN：迁移链未适配 |
| `system_data_database_registration` | Persistence.Entities.DatabaseRegistrationTable；SystemDataSchemaMigrations.RegistrationDdl | NOT RUN：迁移链未适配 |
| `system_data_feature` | Persistence.Entities.SystemDataFeatureTable；SystemDataSchemaMigrations.FeatureDdl | NOT RUN：迁移链未适配 |
| `system_data_feature_override` | Persistence.Entities.SystemDataFeatureOverrideTable；SystemDataSchemaMigrations.FeatureOverrideDdl | NOT RUN：迁移链未适配 |
| `system_data_module_manifest` | Persistence.Entities.SystemDataModuleManifestTable；SystemDataSchemaMigrations.ModuleManifestDdl | NOT RUN：迁移链未适配 |
| `system_data_navigation` | Persistence.Entities.SystemDataNavigationTable；SystemDataSchemaMigrations.NavigationDdl | NOT RUN：迁移链未适配 |
| `system_data_navigation_set` | Persistence.Entities.SystemDataNavigationSetTable；SystemDataSchemaMigrations.NavigationSetDdl | NOT RUN：迁移链未适配 |
| `system_data_navigation_snapshot` | Persistence.Entities.SystemDataNavigationSnapshotTable；SystemDataSchemaMigrations.NavigationSnapshotDdl | NOT RUN：迁移链未适配 |
| `system_data_operation_audit` | Persistence.Entities.SystemDataOperationAuditTable；SystemDataSchemaMigrations.AuditDdl | NOT RUN：迁移链未适配 |
| `system_data_organization` | Persistence.Entities.AdministrativeOrganizationTable；SystemDataSchemaMigrations.OrganizationDdl | NOT RUN：迁移链未适配 |
| `system_data_outbox` | Persistence.Entities.SystemDataOutboxTable；SystemDataSchemaMigrations.OutboxDdl | NOT RUN：迁移链未适配 |
| `system_data_position` | Persistence.Entities.PositionTable；SystemDataSchemaMigrations.PositionDdl | NOT RUN：迁移链未适配 |
| `system_data_projection_revision` | Persistence.Entities.SystemDataProjectionRevisionTable；SystemDataSchemaMigrations.ProjectionRevisionDdl | NOT RUN：迁移链未适配 |
| `system_data_schema_migrations` | Persistence.Migrations.SchemaMigrationRecord | NOT RUN：迁移链未适配 |
| `system_data_seed_ledger` | Persistence.Entities.SystemDataSeedLedgerTable；SystemDataSchemaMigrations.SeedLedgerDdl | NOT RUN：迁移链未适配 |
| `system_data_seed_observation` | Persistence.Entities.DatabaseSeedObservationTable；SystemDataSchemaMigrations.SeedObservationDdl | NOT RUN：迁移链未适配 |
| `system_data_service_catalog` | Persistence.Entities.SystemDataServiceCatalogTable；SystemDataSchemaMigrations.ServiceCatalogDdl | NOT RUN：迁移链未适配 |
| `system_data_theme_policy` | Persistence.Entities.SystemDataThemePolicyTable；SystemDataSchemaMigrations.ThemePolicyDdl | NOT RUN：迁移链未适配 |
| `system_data_ui_resource` | Persistence.Entities.SystemDataUiResourceTable；SystemDataSchemaMigrations.UiResourceDdl | NOT RUN：迁移链未适配 |
| `system_data_user_assignment` | Persistence.Entities.UserAssignmentTable；SystemDataSchemaMigrations.UserAssignmentDdl | NOT RUN：迁移链未适配 |
| `system_file_object` | Persistence.Entities.FileObjectTable；SystemDataSchemaMigrations.FileObjectDdl | NOT RUN：迁移链未适配 |
| `system_file_reference_grant` | Persistence.Entities.FileReferenceGrantTable；SystemDataSchemaMigrations.FileReferenceGrantDdl | NOT RUN：迁移链未适配 |
| `system_file_scan_attempt` | Persistence.Entities.FileScanAttemptTable；SystemDataSchemaMigrations.FileScanAttemptDdl | NOT RUN：迁移链未适配 |
| `system_file_status_outbox` | Persistence.Entities.FileStatusOutboxTable；SystemDataSchemaMigrations.FileStatusOutboxDdl | NOT RUN：迁移链未适配 |
| `system_file_upload_session` | Persistence.Entities.FileUploadSessionTable；SystemDataSchemaMigrations.FileUploadSessionDdl | NOT RUN：迁移链未适配 |
| `system_notification_announcement` | Persistence.Entities.NotificationAnnouncementTable；SystemDataSchemaMigrations.NotificationAnnouncementDdl | NOT RUN：迁移链未适配 |
| `system_notification_inbox_delivery` | Persistence.Entities.NotificationInboxDeliveryTable；SystemDataSchemaMigrations.NotificationInboxDeliveryDdl | NOT RUN：迁移链未适配 |
| `system_notification_message` | Persistence.Entities.NotificationMessageTable；SystemDataSchemaMigrations.NotificationMessageDdl | NOT RUN：迁移链未适配 |
| `system_trusted_service_call_nonce` | Security.SystemDataTrustedServiceCallNonceTable；SystemDataSchemaMigrations.TrustedServiceCallNonceDdl | NOT RUN：迁移链未适配 |

### ReferenceData（24 张）

| 表名 | 当前实体或 DDL 来源 | SQL Server 验收状态 |
| --- | --- | --- |
| `reference_data_cache_generation` | ReferenceDataCacheGenerationMigration.Sql | NOT RUN：迁移链未适配 |
| `reference_data_coding_rule_definition` | CodingRuleMigration.Sql | NOT RUN：迁移链未适配 |
| `reference_data_coding_rule_idempotency_record` | CodingRuleMigration.Sql | NOT RUN：迁移链未适配 |
| `reference_data_coding_rule_sequence` | CodingRuleMigration.Sql | NOT RUN：迁移链未适配 |
| `reference_data_dictionary_definition` | DictionaryMigration.Sql | NOT RUN：迁移链未适配 |
| `reference_data_dictionary_item` | DictionaryMigration.Sql | NOT RUN：迁移链未适配 |
| `reference_data_dynamic_property_definition` | DynamicConfigurationMigration.Sql | NOT RUN：迁移链未适配 |
| `reference_data_dynamic_property_field` | DynamicConfigurationMigration.Sql | NOT RUN：迁移链未适配 |
| `reference_data_dynamic_property_record` | DynamicConfigurationMigration.Sql | NOT RUN：迁移链未适配 |
| `reference_data_dynamic_property_value` | DynamicConfigurationMigration.Sql | NOT RUN：迁移链未适配 |
| `reference_data_metadata_attribute_definition` | MetadataMigration.Sql | NOT RUN：迁移链未适配 |
| `reference_data_metadata_entity_schema` | MetadataMigration.Sql | NOT RUN：迁移链未适配 |
| `reference_data_outbox_message` | ReferenceDataSharedMigration.Sql | NOT RUN：迁移链未适配 |
| `reference_data_parameter_app_domain` | ParameterMigration.Sql | NOT RUN：迁移链未适配 |
| `reference_data_parameter_history` | ParameterMigration.Sql | NOT RUN：迁移链未适配 |
| `reference_data_parameter_key` | ParameterMigration.Sql | NOT RUN：迁移链未适配 |
| `reference_data_parameter_multi_value` | ParameterMigration.Sql | NOT RUN：迁移链未适配 |
| `reference_data_schema_migrations` | ReferenceDataInitializationLedger.SchemaSql | NOT RUN：迁移链未适配 |
| `reference_data_seed_ledger` | ReferenceDataInitializationLedger.SchemaSql | NOT RUN：迁移链未适配 |
| `reference_data_state_machine_definition` | StateMachineMigration.Sql | NOT RUN：迁移链未适配 |
| `reference_data_state_machine_node` | StateMachineMigration.Sql | NOT RUN：迁移链未适配 |
| `reference_data_state_machine_transition` | StateMachineMigration.Sql | NOT RUN：迁移链未适配 |
| `reference_data_unit_of_measure_dimension` | UnitOfMeasureMigration.Sql | NOT RUN：迁移链未适配 |
| `reference_data_unit_of_measure_unit` | UnitOfMeasureMigration.Sql | NOT RUN：迁移链未适配 |

### Collaboration（20 张）

| 表名 | 当前实体或 DDL 来源 | SQL Server 验收状态 |
| --- | --- | --- |
| `collaboration_chat_attachment` | CollaborationSchemaMigrations.MessagingDdl；Persistence.AttachmentTable | NOT RUN：迁移链未适配 |
| `collaboration_compliance_command` | CollaborationSchemaMigrations.ComplianceDdl；Persistence.ComplianceCommandTable | NOT RUN：迁移链未适配 |
| `collaboration_compliance_disposition` | CollaborationSchemaMigrations.ComplianceDdl；Persistence.DispositionTable | NOT RUN：迁移链未适配 |
| `collaboration_compliance_export` | CollaborationSchemaMigrations.ComplianceDdl；Persistence.ExportTable | NOT RUN：迁移链未适配 |
| `collaboration_compliance_preparation` | CollaborationSchemaMigrations.ComplianceDdl；Persistence.CompliancePreparationTable | NOT RUN：迁移链未适配 |
| `collaboration_compliance_view_budget` | CollaborationSchemaMigrations.ComplianceDdl；Persistence.ComplianceViewBudgetTable | NOT RUN：迁移链未适配 |
| `collaboration_conversation` | CollaborationSchemaMigrations.MessagingDdl；Persistence.ConversationTable | NOT RUN：迁移链未适配 |
| `collaboration_conversation_member` | CollaborationSchemaMigrations.MessagingDdl；Persistence.ConversationMemberTable | NOT RUN：迁移链未适配 |
| `collaboration_event_inbox` | CollaborationSchemaMigrations.ComplianceDdl；Persistence.CollaborationEventInboxTable | NOT RUN：迁移链未适配 |
| `collaboration_legal_hold_case` | CollaborationSchemaMigrations.ComplianceDdl；Persistence.LegalHoldTable | NOT RUN：迁移链未适配 |
| `collaboration_message` | CollaborationSchemaMigrations.MessagingDdl；Persistence.MessageTable | NOT RUN：迁移链未适配 |
| `collaboration_message_personal_visibility` | Persistence.MessagePersonalVisibilityTable | NOT RUN：迁移链未适配 |
| `collaboration_messaging_deduplication` | CollaborationSchemaMigrations.ComplianceDdl | NOT RUN：迁移链未适配 |
| `collaboration_outbox_message` | CollaborationSchemaMigrations.ComplianceDdl；Persistence.CollaborationOutboxTable | NOT RUN：迁移链未适配 |
| `collaboration_remote_assistance_session` | CollaborationSchemaMigrations.RemoteAssistanceScreenDdl；Persistence.RemoteAssistanceScreenTable | NOT RUN：迁移链未适配 |
| `collaboration_remote_assistance_voice_call` | CollaborationSchemaMigrations.RemoteAssistanceVoiceDdl；Persistence.RemoteAssistanceVoiceTable | NOT RUN：迁移链未适配 |
| `collaboration_remote_assistance_voice_slot` | CollaborationSchemaMigrations.RemoteAssistanceVoiceDdl；Persistence.RemoteAssistanceVoiceSlotTable | NOT RUN：迁移链未适配 |
| `collaboration_retention_checkpoint` | CollaborationSchemaMigrations.ComplianceDdl；Persistence.RetentionCheckpointTable | NOT RUN：迁移链未适配 |
| `collaboration_retention_policy` | CollaborationSchemaMigrations.ComplianceDdl；Persistence.RetentionPolicyTable | NOT RUN：迁移链未适配 |
| `collaboration_schema_migrations` | Persistence.CollaborationSchemaMigrationRecord | NOT RUN：迁移链未适配 |

## 4. SQL Server DDL 入口实测明细

REJECTED：当前生成函数明确抛出不支持；UNSUPPORTED_DIALECT：返回语句仍有 ADD COLUMN 等 SQLite/PG 专用语法；NO_SQLSERVER_CONTRACT：ReferenceData 仅暴露 bool 方言；GENERATED_NOT_ENGINE_TESTED：返回了字符串但未执行，不计通过。未向 SQL Server 提交这些 DDL。

| 服务 | 入口 | 结果 | 发现 |
| --- | --- | --- | --- |
| Collaboration | `CollaborationSchemaMigrations.ComplianceDdl` | REJECTED | NotSupportedException |
| Collaboration | `CollaborationSchemaMigrations.IndexDdl` | REJECTED | NotSupportedException |
| Collaboration | `CollaborationSchemaMigrations.MessagingDdl` | REJECTED | NotSupportedException |
| Collaboration | `CollaborationSchemaMigrations.RemoteAssistanceScreenDdl` | REJECTED | NotSupportedException |
| Collaboration | `CollaborationSchemaMigrations.RemoteAssistanceVoiceDdl` | REJECTED | NotSupportedException |
| Identity | `IdentitySchemaMigrations.BootstrapCredentialDdl` | REJECTED | NotSupportedException |
| Identity | `IdentitySchemaMigrations.LoginAuditDdl` | REJECTED | NotSupportedException |
| Identity | `IdentitySchemaMigrations.OperationAuditDdl` | REJECTED | NotSupportedException |
| Identity | `IdentitySchemaMigrations.OutboxDdl` | REJECTED | NotSupportedException |
| Identity | `IdentitySchemaMigrations.PermissionDdl` | REJECTED | NotSupportedException |
| Identity | `IdentitySchemaMigrations.RefreshSessionDdl` | REJECTED | NotSupportedException |
| Identity | `IdentitySchemaMigrations.RoleDdl` | REJECTED | NotSupportedException |
| Identity | `IdentitySchemaMigrations.RolePermissionDdl` | REJECTED | NotSupportedException |
| Identity | `IdentitySchemaMigrations.SeedLedgerDdl` | REJECTED | NotSupportedException |
| Identity | `IdentitySchemaMigrations.SsoBrowserSessionDdl` | REJECTED | NotSupportedException |
| Identity | `IdentitySchemaMigrations.SsoClientDdl` | REJECTED | NotSupportedException |
| Identity | `IdentitySchemaMigrations.SsoClientEndpointDdl` | REJECTED | NotSupportedException |
| Identity | `IdentitySchemaMigrations.SsoExternalAccountDdl` | REJECTED | NotSupportedException |
| Identity | `IdentitySchemaMigrations.SsoProviderDdl` | REJECTED | NotSupportedException |
| Identity | `IdentitySchemaMigrations.StepUpGrantDdl` | REJECTED | NotSupportedException |
| Identity | `IdentitySchemaMigrations.TrustedServiceCallNonceDdl` | REJECTED | NotSupportedException |
| Identity | `IdentitySchemaMigrations.UserDdl` | REJECTED | NotSupportedException |
| Identity | `IdentitySchemaMigrations.UserGroupDdl` | REJECTED | NotSupportedException |
| Identity | `IdentitySchemaMigrations.UserGroupMembershipDdl` | REJECTED | NotSupportedException |
| Identity | `IdentitySchemaMigrations.UserGroupRoleDdl` | REJECTED | NotSupportedException |
| Identity | `IdentitySchemaMigrations.UserRoleDdl` | REJECTED | NotSupportedException |
| Identity | `IdentitySchemaMigrations.WriteIdempotencyDdl` | REJECTED | NotSupportedException |
| ReferenceData | `CodingRuleMigration.Sql` | NO_SQLSERVER_CONTRACT | 未执行建表/迁移 |
| ReferenceData | `DictionaryMigration.Sql` | NO_SQLSERVER_CONTRACT | 未执行建表/迁移 |
| ReferenceData | `DynamicConfigurationMigration.Sql` | NO_SQLSERVER_CONTRACT | 未执行建表/迁移 |
| ReferenceData | `MetadataMigration.Sql` | NO_SQLSERVER_CONTRACT | 未执行建表/迁移 |
| ReferenceData | `ParameterMigration.Sql` | NO_SQLSERVER_CONTRACT | 未执行建表/迁移 |
| ReferenceData | `ReferenceDataCacheGenerationMigration.Sql` | NO_SQLSERVER_CONTRACT | 未执行建表/迁移 |
| ReferenceData | `ReferenceDataIntegrityMigration.Sql` | NO_SQLSERVER_CONTRACT | 未执行建表/迁移 |
| ReferenceData | `ReferenceDataSharedMigration.Sql` | NO_SQLSERVER_CONTRACT | 未执行建表/迁移 |
| ReferenceData | `StateMachineMigration.Sql` | NO_SQLSERVER_CONTRACT | 未执行建表/迁移 |
| ReferenceData | `UnitOfMeasureMigration.Sql` | NO_SQLSERVER_CONTRACT | 未执行建表/迁移 |
| SystemData | `SystemDataSchemaMigrations.ApprovalDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.AuditDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.AuditFactDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.AuditIngressFailureDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.AuditLifecycleDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.AuditLifecycleLegalHoldAlterDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.AuditOutboxDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.BackupEvidenceDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.BaselineSeedDdl` | GENERATED_NOT_ENGINE_TESTED | 未执行建表/迁移 |
| SystemData | `SystemDataSchemaMigrations.BaselineSeedNoopDdl` | GENERATED_NOT_ENGINE_TESTED | 未执行建表/迁移 |
| SystemData | `SystemDataSchemaMigrations.CollaborationFileHoldDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.CollaborationFileReferenceDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.EnvironmentPolicyDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.FeatureDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.FeatureOverrideDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.FileObjectDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.FileReferenceGrantDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.FileScanAttemptDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.FileStatusOutboxDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.FileUploadSessionDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.MigrationObservationDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.MigrationObservationModuleScopeAlterDdl` | UNSUPPORTED_DIALECT | ADD COLUMN，CREATE INDEX IF NOT EXISTS |
| SystemData | `SystemDataSchemaMigrations.ModuleManifestDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.ModuleManifestFeatureDeclarationsDdl` | UNSUPPORTED_DIALECT | ADD COLUMN |
| SystemData | `SystemDataSchemaMigrations.ModuleManifestPermissionDeclarationsDdl` | UNSUPPORTED_DIALECT | ADD COLUMN |
| SystemData | `SystemDataSchemaMigrations.ModuleManifestResourceDeclarationsDdl` | UNSUPPORTED_DIALECT | ADD COLUMN |
| SystemData | `SystemDataSchemaMigrations.NavigationActionResourcesDdl` | UNSUPPORTED_DIALECT | ADD COLUMN |
| SystemData | `SystemDataSchemaMigrations.NavigationDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.NavigationSetDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.NavigationSnapshotDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.NotificationAnnouncementDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.NotificationInboxDeliveryDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.NotificationMessageDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.OperationDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.OperationStepDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.OrganizationDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.OutboxBackoffDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.OutboxDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.PlanDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.PlanStepDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.PositionDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.PostgreSqlControlPlaneNativeTypesDdl` | GENERATED_NOT_ENGINE_TESTED | 未执行建表/迁移 |
| SystemData | `SystemDataSchemaMigrations.ProjectionRevisionDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.ProjectionRevisionUniqueIndexDdl` | UNSUPPORTED_DIALECT | CREATE UNIQUE INDEX IF NOT EXISTS |
| SystemData | `SystemDataSchemaMigrations.RegistrationDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.RegistrationModuleScopeAlterDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.SeedLedgerDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.SeedObservationDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.ServiceCatalogDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.ServiceCatalogOwnerSnapshotsDdl` | UNSUPPORTED_DIALECT | ADD COLUMN |
| SystemData | `SystemDataSchemaMigrations.ServiceInitializerRegistrationAlterDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.ServicePlanSemanticsAlterDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.ThemePolicyDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.TrustedServiceCallNonceDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.UiResourceDdl` | REJECTED | NotSupportedException |
| SystemData | `SystemDataSchemaMigrations.UserAssignmentDdl` | REJECTED | NotSupportedException |

## 5. 什么时候使用什么数据库

| 场景 | 数据库及选择方式 | 当前状态 |
| --- | --- | --- |
| 当前日常开发/IDE 调试 | 现用云 PostgreSQL；统一私有 JSON 的 `RemoteDevelopment:DatabaseProvider` 缺省/`PostgreSQL` | 继续使用当前配置；本轮实际解析确认 PostgreSQL |
| SQL Server 兼容开发和实测 | 隔离 `industrial_platform_sql2019`；从同一私有文件的 `SqlServerAcceptance:2019:ConnectionString` 读取测试目标 | 驱动/只读查询通过，完整迁移和业务未通过；验收连接串不会自动切换应用 |
| 快速隔离单元测试/临时 fixture | SQLite；显式启动键 `IndustrialPlatform:DevelopmentInfrastructureMode=Sqlite` | 保留；其 PASS 不能证明 SQL Server 的类型、索引、并发和 SQL 语义 |
| 后续客户/正式环境 | 由部署选择一个经验证的关系库引擎，PG 或 SQL Server；该环境内所有当前模块按相同 Provider 合同运行 | SQL Server 完整验收通过后才启用；生产 Host 配置/拓扑需另按现有合同验收，当前不宣称已可用 |

选择引擎与选择物理库是两件事：`RemoteDevelopment:DatabaseProvider` 决定 Development 运行引擎，`DatabaseTopology` 决定该引擎的实际库名。业务代码通过统一上下文/仓储访问，不由页面、某张表或扫码/聊天功能挑库；不会在失败后偷偷改为另一引擎。Shared 只限平台 Development，统一 Host 当前是一套连接承载各模块；生产 PerService 的服务库映射属于既定拓扑约束，不能仅改名就把一个开发共享库当正式分库方案。

SQL Server Development 切换条件全部满足后，在现有私有文件配置 `RemoteDevelopment:DatabaseProvider=SqlServer`、补齐 `RemoteDevelopment:SqlServer` 主机/端口/SQL 账号/加密参数，设置 `DatabaseTopology:SharedDatabaseName=industrial_platform_sql2019` 与 `SharedSqliteFile=""`，然后重启 Host 生效。当前还不能执行这个切换。Provider 改动只改变后续连接目标，不搬迁现有数据；PG 库里的账号/菜单/业务不会自动出现在 SQL Server，新 SQL 库需经受控迁移和初始化种子生成自身事实。

最终通过条件：109 张表逐个核对列/类型/空值/默认/主外键/索引/说明，新库初始化与二次启动幂等；四服务真实读写及登录/菜单/聊天/文件/语音 slot/Outbox，UTC/Unicode/decimal/大小写/尾随空格/并发/失败事务，数据库编排与旧 Provider 回归全部有证据。当前 CI 排序规则需特别验证标识和编码比较语义，不以现成默认排序规则替代领域规则。详见 [09A §9](../implementation/09A-Industrial%20Platform终端运行时与客户端打包开发实施方案.md#9-sql-server-20192022接入)。
