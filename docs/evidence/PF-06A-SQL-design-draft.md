# PF-06A / 009 SQL Server 接入设计草案（D02/D03/D04/D06）

> 历史设计草案。2026-09-30 当前实现/验收以 [09A §9](../implementation/09A-Industrial%20Platform终端运行时与客户端打包开发实施方案.md#9-sql-server-20192022接入)、[最新验证报告](PF-06A-SQL2019-compatibility-check.md)和[全表字典](../implementation/details/PF06A-SQLServer全表字段字典.md)为准。下文描述的是接入前现状，不再作为未实现清单。2022 与生产专项仍未实测。

**2026-09-30 状态更新：** 下文是 09-21 的只读清点快照，其中“尚未进入编码”等表述只适用于当时。主控按用户继续开发的指令已做 Provider、拓扑、Development 连接构造、catalog 检查和 ReferenceData 目标身份等局部代码；未切换现有运行数据库。用户已提供 SQL Server 2019 隔离库的受控连接串；只读连通与实例身份检查通过，详情见[PF-06A 证据](PF-06A.md#sql-server-2019-隔离库连通性核查2026-09-30)。G06A-SQL 仍 OPEN，完整 DDL/仓储/锁与字段字典未完成，不构成 SQL Server 可用或验收结论；2022 真引擎验收保持 `NOT RUN`。

本轮本机验证：原工作树 `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release` 退出码 0、0 警告/0 错误；`dotnet test ... --no-build` 并行运行时 Identity 协作目录一例失败，其余七组通过；随后该例单独复测通过、Identity 整组串行复测 621/621 通过。BuildingBlocks 173/173、ReferenceData 262/262、SystemData 636/636、Collaboration 165/165、Gateway 14/14、UnifiedHost 23/23、Integration 12 通过/7 跳过。以上仅为现有 Provider 和新基础件回归，SQL-A01～A09 真实 SQL Server 2019/2022 仍 `NOT RUN`。清理后 `dotnet/testhost/VBCSCompiler` 残留进程为 0。

> 状态：G06A-SQL **OPEN，主控复核未通过**。本文是基于当前仓库只读清点形成的逐服务设计输入，不是 SQL Server 已实现或已验收报告。
>
> 本轮没有修改 `src/backend` 生产代码，没有切换当前数据库，也没有创建 SQL Server 连接或凭据。主控确认配置合同、schema 策略、锁语义和驱动版本后，才允许进入编码与真实引擎验收。

## 主控复核（2026-09-21）

**结论：G06A-SQL 保持 OPEN，009 尚未进入生产编码。** 下文完成了服务和表组级别的风险盘点，可供拆解实现；但它不是 09A §9.6.4～§9.6.5 要求的可直接交接设计。尤其 ReferenceData 仍按七个模块汇总、SystemData 多张表合并描述，Identity 和 Collaboration 也只列关键列。缺少逐表说明卡、每个真实属性与物理列的一一对应、语义/物理两张字段表、准确长度和可空性、迁移 ID 与测试入口。不能把这些汇总行当作完整字段字典，也不能据此生成或批准 SQL Server DDL。

进入编码前，主控需要在同一份 09A 字典中补齐：每张表的所有字段（含继承字段、账本、租约、Outbox/Inbox）、来源与写入时机、空值/单位/敏感性、.NET/JSON/SQL 类型及长度精度、默认值和约束、索引过滤条件、对应迁移步骤及可执行断言。当前未显式固定的物理细节标“待真实结构核验”，不可从 CLR 类型猜填。迁移/seed 还须逐条固定旧 ID、checksum 来源、事务与锁边界，证明 PG/SQLite 历史不变。

主控可把 `dbo + 已有服务前缀表名` 作为优先核对方案，以减少命名迁移；这**不是已冻结 schema 合同**。冻结前须列出四服务全部物理表和 ledger 名并做大小写/排序规则下的碰撞核验。配置键、密钥注入优先级、目标库身份、SQL Client/SqlSugar 精确支持版本及事务锁方案也未冻结；尤其 `sp_getapplock` 只是候选，不得在未经 2019/2022 隔离实例验证时写成已通过。

环境输入仍缺两套隔离 SQL Server 2019/2022 实例的脱敏目标、Edition/build/compatibility level、认证/TLS/collation、创建/恢复/只读检查权限与 Shared/PerService 目标拓扑。取得输入前可继续做只读清点和字段字典，不对现用库探测或迁移；真实引擎 SQL-A01～A09 均记 `NOT RUN`。

## 1. 审定边界与当前事实

本稿把 09A §9.6.1 的 12 个接入面落到 D02（配置/拓扑）、D03（物理结构/字段）、D04（迁移/种子/账本/锁）、D06（运行链与验收入口）。表名和列名来自当前 `SugarTable`、`SugarColumn`、迁移 DDL 和仓储查询；没有从 DTO 反推不存在的列。

当前实现事实：

- `DatabaseProvider` 只有 `Sqlite` 与 `PostgreSQL`；`DbType.SqlServer` 尚未接入。
- `SqlSugarOptions` 默认 PostgreSQL；`SystemDataMigrationHelpers`、Identity/Collaboration/ReferenceData 迁移均存在“SQLite / PostgreSQL / otherwise unsupported”分支。
- `SchemaPhysicalDriftGuard` 当前只查询 SQLite pragma 或 PostgreSQL `information_schema` / `pg_indexes`。
- ReferenceData 的 PostgreSQL 目标使用 `Search Path` 推导 schema，非 PostgreSQL 使用 `reference_data_<name>` 平面表名；SQL Server 不能被现有非 PG 分支当作 SQLite。
- Identity、SystemData、Collaboration 的本地 migration runner 已有 checksum 账本；ReferenceData 有服务级 migration/seed ledger 与 cache generation；SystemData 另有编排计划、审批、观察、备份和 artifact checksum。
- 当前 artifact verifier 是 SHA-256/checksum/`SignatureRef` 一致性校验，代码注释明确密码学私钥验签尚未接入；这不能被写成“签名已验收”。
- PostgreSQL 用户分配锁是同一 SqlSugar 事务中的 `pg_advisory_xact_lock`；SQLite 只是进程内信号量。SQL Server 锁方案必须在主控确认后实现，不能沿用进程锁。

## 2. D02：配置、Provider 与物理拓扑合同

### 2.1 需要主控锁定的键和优先级

| 配置键/代码入口 | 当前含义 | SQL Server 设计候选 | 审定点 |
| --- | --- | --- | --- |
| `IndustrialPlatform:DevelopmentInfrastructureMode` | 仅 `Unified` / `Sqlite`；控制 Development 基础设施组合，不是数据库引擎 | 保持原语义；不添加 `SqlServer` 模式值 | 确认 Provider 不能借此绕过 Redis/MQ/Host 选择 |
| `IndustrialPlatform:LocalConfigurationPath` | 本地受信配置文件路径 | 不变 | 不在日志中输出连接串 |
| `RemoteDevelopment:Enabled` / `Host` / `PostgreSql` | 当前统一文件生成 PostgreSQL 连接配置 | 增加 SQL Server 目标时应有明确的 `RemoteDevelopment:SqlServer` 或统一连接对象；不得让外部 `SqlSugar:DbType` 与拓扑相互矛盾 | 主控确定键名和 secret 注入方式 |
| `SqlSugar:DbType` | 绑定 `SqlSugarOptions.DbType`，默认 PostgreSQL | `SqlServer` → `SqlSugar.DbType.SqlServer` | 明确大小写/空值/非法值的拒绝策略 |
| `SqlSugar:ConnectionString` | 当前连接串 | SQL Server 加密连接串由 secret/config provider 注入 | 只记录脱敏目标指纹；不把密码写入证据 |
| `SqlSugar:IsAutoCloseConnection` | 默认 `true` | 保持；锁/迁移事务必须显式持有同一连接 | 主控确认 SqlSugar 事务生命周期 |
| `DatabaseTopology:EnvironmentName` | `Development/Test/Staging/Production` | 不变 | 用于目标身份与 manifest，不作数据库名推断 |
| `DatabaseTopology:Mode` | `Shared` / `PerService` | 不变 | Shared 和 PerService 都必须有 SQL-A02 实测 |
| `DatabaseTopology:SharedDatabaseName` | Shared PostgreSQL 物理库名 | SQL Server Shared database 名 | 与 `SharedSqliteFile` 互斥，错误组合拒绝 |
| `DatabaseTopology:SharedSqliteFile` | Shared SQLite 文件 | SQL Server 路径下必须为空 | SQL Server 不能走 `Path.GetFullPath` |
| `DatabaseTopology:SharedDatabaseSchema` | PG search path/schema identity | SQL Server 默认候选 `dbo`，或服务 schema 映射 | 这是必须人工批准的 schema 合同 |
| `DatabaseTopology:ServiceDatabases:unifiedhost` | PerService 逻辑库映射 | SQL Server 同样按 service key 解析 | 漏映射拒绝，不自动回退 Shared |

### 2.2 Provider / schema / table 命名候选

以下是待审定候选，不是已批准结果：

| Provider | 数据库身份 | 服务表物理名候选 | 说明 |
| --- | --- | --- | --- |
| SQLite | 文件 canonical path + 文件 fingerprint | 当前平面名 | 保持现有测试语义 |
| PostgreSQL | host/database/search_path | 当前 PG schema/table 语义 | 不重写已有历史迁移 |
| SQL Server | server/database/schema | `dbo.identity_*`、`dbo.system_*`、`dbo.reference_data_*`、`dbo.collaboration_*` | 以现有前缀表名降低跨服务碰撞；若使用服务 schema，必须一次性固定并加入 artifact checksum |

建议不为 SQL Server 2019/2022 建两个业务 Provider；统一逻辑值 `SqlServer`，引擎版本只存在于环境登记和验收证据。若主控选择服务 schema，则建议 `identity`、`system_data`、`reference_data`、`collaboration`，且迁移 ledger 与业务表同 schema；若选择 `dbo`，必须拒绝跨服务同名 ledger 和无前缀新表。

### 2.3 D02 配置验证与测试入口

执行链应保持：

`trusted config load → DatabaseProvider parse → DatabaseTopologyOptions bind → DatabaseTopologyResolver.Resolve(serviceKey) → ResolvedDatabaseTarget identity → SqlSugarOptions/connection build → service initializer`

每一步的拒绝结果：

1. Provider 缺失/非法：启动失败 `ProviderUnsupported` 或等价稳定错误，不回退 PostgreSQL。
2. SQL Server 下出现 `SharedSqliteFile`：配置无效。
3. PerService 缺少 service key：解析失败，不能使用统一库猜测。
4. 目标身份与 `EnvironmentName/ServiceKey/DatabaseName/Schema` 不一致：Inspect 失败且无写入。
5. SQL Server 连接权限不足、TLS 拒绝、数据库不存在：分别保留原始分类，密码不输出。

测试入口：

- 代码：`DatabaseProvider.cs`、`DatabaseTopologyOptions.cs`、`DatabaseTopologyResolver.cs`、`ResolvedDatabaseTarget.cs`、`DevelopmentInfrastructureConfiguration.cs`、`SqlSugarOptions.cs`、`SqlSugarServiceCollectionExtensions.cs`。
- 单元/契约：Shared/PerService、Provider 非法、漏映射、SQL Server 不走 SQLite/PG fallback。
- 真实：SQL-A01（2019/150、2022/160连接/参数化/事务/TLS/权限）、SQL-A02（UnifiedHost与独立服务两种拓扑）、SQL-A09（PG/SQLite回归）。

## 3. D03：逐服务物理表与字段清单

### 3.1 通用字段规则（Identity / SystemData / Collaboration 公共实体）

下列字段在当前实体中反复出现，SQL Server 设计必须逐表确认长度、精度和索引，不得仅凭 CLR 类型生成默认值：

| 属性/列 | 当前语义 | SQL Server 候选 | 风险/验证 |
| --- | --- | --- | --- |
| `Id/id` | 数据库 PK，通常 `Guid` | `uniqueidentifier` | SQL Server 默认生成策略与旧数据导入核验 |
| `NId/n_id` | 业务标识，不等同 PK | `nvarchar(128)` 候选 | 各实体已有 Length 注解不一致，逐表固定 |
| `TenantNId/tenant_n_id` | 租户隔离边界 | `nvarchar(128)` 候选 | 所有唯一键/查询需包含租户时不得遗漏 |
| `IsDeleted/is_deleted` | 软删除状态 | `bit NOT NULL` | filtered unique index 语义需保留 |
| `IsFrozen/is_frozen`、`IsLocked/is_locked` | 业务/运维锁状态 | `bit NOT NULL` | 不得与迁移互斥锁混用 |
| `CreatedOn/created_on`、`LastUpdatedOn/last_updated_on` | UTC 事实时间 | `datetimeoffset(7)` 候选 | 跨进程读回、精度、时区偏移均测 SQL-A05 |
| `OptimisticVersion` | 业务并发版本 | `bigint NOT NULL` | 不改成 rowversion；业务版本需可跨 Provider 读写 |
| `ConcurrencyVersion` | Guid 并发标识 | `uniqueidentifier NOT NULL` | 更新条件与实体契约保持 |
| `*Json` / `Payload` | 结构化 JSON 或事件载荷 | `nvarchar(max)` 候选 | JSON schema/哈希/敏感字段规则由服务验证，不依赖 SQL Server JSON 类型 |
| `Checksum` / `Hash` | 内容或引用校验 | `varchar(64)` 或 `nvarchar(128)` 候选 | 必须区分 migration checksum、artifact hash、业务 hash |

### 3.2 Identity（serviceKey=`identity`）

权威入口：`IdentitySchemaMigrations.cs`、`SchemaMigrationRunner.cs`、`IdentitySeedRunner.cs`、`Persistence/Entities/*.cs`、`Persistence/Repositories/*.cs`。当前表均为无显式 schema 的前缀表；SQL Server 的 schema 由 D02 审定。

| 表 / Entity | 关键列（除通用字段外） | 键、索引、关联与 SQL Server 注意 |
| --- | --- | --- |
| `identity_user` / `UserTable` | `tenant_n_id,n_id,normalized_n_id,login_name,normalized_login_name,name,password_hash,email,phone,status,failed_login_count,locked_until,auth_version,last_login_on,must_change_password` | tenant+normalized_n_id 唯一；软删除 active login filtered unique；角色/刷新会话/审计引用；`password_hash` 不可回读到诊断 |
| `identity_role` / `RoleTable` | tenant/nid/normalized/name/description/is_system | tenant+normalized_n_id 唯一，`(id,is_deleted)` 复合引用 |
| `identity_permission` / `PermissionTable` | nid/normalized/name/type/parent_permission_n_id/parent_permission_id/parent_permission_is_deleted/description/status | normalized_n_id 唯一；自引用复合 FK；parent check 必须保留 |
| `identity_user_role` / `UserRoleTable` | tenant/user_id/user_is_deleted/role_id/role_is_deleted | active `(tenant,user,role)` unique filtered；复合 FK |
| `identity_role_permission` / `RolePermissionTable` | role_id/role_is_deleted/permission_id/permission_is_deleted | active `(role,permission)` unique filtered；复合 FK |
| `identity_refresh_session` / `RefreshSessionTable` | tenant/nid/family_n_id/user_id/user_is_deleted/token_hash/expires_on/used_on/revoked_on/revoke_reason/replaced_by_* /user_agent_hash/ip_address_hash | nid/token hash unique；user/self replacement FK；时间为 UTC |
| `identity_login_audit` / `LoginAuditTable` | tenant/user_n_id/login_name_snapshot/result/failure_reason/ip_address_hash/user_agent_hash/trace_id | tenant+created、user+created 查询索引；只保存 hash/脱敏摘要 |
| `identity_operation_audit` / `OperationAuditTable` | tenant/actor_user_n_id/action/object_type/object_n_id/before_summary/after_summary/trace_id | tenant+created、object 查询索引；摘要不得含 token/密码 |
| `identity_outbox` / `OutboxTable` | event_id/event_type/event_version/payload/event_created_time/published_on/retry_count/last_error | event_id PK；pending publish `(published_on,event_created_time)` |
| `identity_sso_provider` / `SsoProviderTable` | tenant/nid/normalized/name/protocol/authority_or_metadata_url/client_id_or_entity_id/secret_or_certificate_reference/callback_path/enabled/auto_redirect/provisioning_mode/logout_mode/allowed_email_domains_json/jit_default_role_n_ids_json | tenant+normalized unique；active client filtered unique；secret/reference 不进日志 |
| `identity_sso_external_account` / `SsoExternalAccountTable` | nid/normalized/sso_provider_id/sso_provider_is_deleted/external_subject/user_id/user_is_deleted/external_name/external_email/last_login_on | nid unique；active provider+subject filtered unique；双复合 FK |
| `identity_sso_client` / `SsoClientTable` | tenant/nid/normalized/name/oauth_client_id/enabled | tenant+normalized unique；active OAuth client filtered unique |
| `identity_sso_client_endpoint` / `SsoClientEndpointTable` | sso_client_id/sso_client_is_deleted/nid/endpoint_type/uri/normalized_uri/enabled | client+NId 与 client+type+normalized URI active unique |
| `identity_sso_browser_session` / `SsoBrowserSessionTable` | tenant/nid/normalized/provider_n_id/user_id/user_is_deleted/session_handle_hash/auth_version/last_activity_on/expires_on/revoked_on/revoke_reason | session hash、expiry、lifecycle 清理；不可把 handle 当明文 |
| `identity_user_group` / `UserGroupTable` | tenant/nid/normalized/name/description/status | tenant+normalized unique；成员和角色关联 |
| `identity_user_group_membership` / `UserGroupMembershipTable` | tenant/user_group_id/user_group_is_deleted/user_id/user_is_deleted | group/user active unique；复合 FK |
| `identity_user_group_role` / `UserGroupRoleTable` | tenant/user_group_id/user_group_is_deleted/role_id/role_is_deleted | group/role active unique；复合 FK |
| `identity_seed_ledger` / `SeedLedgerTable` | tenant/seed_n_id/seed_version/seed_class/scope/checksum/status/applied_on/system_data_operation_n_id/trace_id | seed identity、checksum、status 是幂等依据；同事务写入 |
| `identity_bootstrap_credential` / `BootstrapCredentialTable` | tenant/user/state/delivery_reference_hash/recovery_reference_hash/delivered_on/recovered_on | 凭据交付事实，不保存明文；清理/冻结策略需运维确认 |
| `identity_write_idempotency` / `IdentityWriteIdempotencyTable` | tenant/actor_user_n_id/idempotency_key/request_hash/completed/completed_on | tenant+actor+key 唯一候选；重试不得重复副作用 |
| `pf05_service_call_nonce` / `TrustedServiceCallNonceTable` | issuer/nonce/expires_on/created_on | issuer+nonce 唯一；短期清理；跨服务可信调用防重放 |
| `pf05_step_up_grant` / `StepUpGrantTable` | tenant/actor_user_n_id/actor_session_n_id/actor_security_version/action/request_n_id/scope_checksum/request_hash/binding_hash/proof_hash/issued_on/expires_on/consumed_on/consumed_by_service/consumed_receipt_n_id | 已有列长度注解；request/scope/binding/proof hash 需固定为 64；消费状态原子更新 |

Identity 迁移顺序：表建模 → `ID-019` seed/credential → `ID-019-03` must-change 列 → idempotency → PF05 nonce/grant → 扩展 grant。每个步骤执行、校验、写 `identity_schema_migrations`（当前代码对应 `SchemaMigrationRecord`）必须同一 SQL Server 事务；种子另走 `IdentitySeedRunner`，不混入 DDL。

### 3.3 SystemData（serviceKey=`systemdata`）

权威入口：`SystemDataSchemaMigrations.cs`、`SystemDataMigrationHelpers.cs`、`Persistence/Entities/`、`Pf04Store.cs`、`UserAssignmentStore.cs`、`DatabaseOrchestration/DatabaseOrchestrationStore.cs`。表按事实域分为控制平面、文件、通知、审计、PF04、数据库编排。

| 表组 / 表 | 关键列 | SQL Server 设计与锁点 |
| --- | --- | --- |
| `system_data_module_manifest`、`system_data_navigation_entry`、`system_data_theme_policy`、`system_data_projection_revision` | 通用控制列 + `module_n_id/manifest_version/checksum/permission_*`；导航 `route_key/label_key/parent_key/order/visibility`；主题 allowed/default/revision；projection `area/revision/generated_on` | JSON 为 `nvarchar(max)`，checksum 单独列；动态菜单查询依赖 tenant/status/revision 索引 |
| `system_data_administrative_organization` / `AdministrativeOrganizationTable` | tenant/nid/normalized/name/parent_n_id/level/path/status/… | tenant+normalized 唯一；层级/软删约束；组织移动与 assignment 同事务边界 |
| `system_data_position` / `PositionTable` | tenant/nid/normalized/name/organization_n_id/status/… | tenant+normalized 唯一；岗位归属与 assignment 快照，不以跨库 FK 假设 Identity |
| `system_data_user_assignment` / `UserAssignmentTable` | tenant/nid/normalized/user_n_id/user_display_name_snapshot/organization_n_id/position_n_id/position_id/position_is_deleted/is_primary/effective_from/effective_to/state/cancelled_on/cancel_reason | 时间区间、primary 唯一/状态规则；用户级锁键 `(tenant_n_id,user_n_id)`；`position_id,is_deleted` 复合引用 |
| `system_file_upload_session`、`system_file_object` | session 表含 transport/purpose/name/type/expected_length/sha/current_offset/writer_epoch/status/expiry/file_n_id；object 含 file_n_id/session/owner/name/type/length/sha/storage_key/scan_status/restricted/deletion/retention | long→bigint；sha 与 storage key 不混；上传 offset 更新需乐观版本/事务 |
| `system_file_status_outbox`、`system_file_scan_attempt` | event/file/status/restricted/deletion/observed/published/next_attempt/retry/error/dead；scan `scan_n_id/file_n_id/engine/status/start/completed/detail` | outbox pending + lease/retry 组合索引；扫描失败不得伪造 clean |
| `system_file_reference_grant`、`system_collaboration_file_reference`、`system_collaboration_file_hold` | reference/file/owner/purpose/created/deleted；跨服务引用含 conversation/message/attachment/owner/status/version/released；hold 含 case/scope_checksum/revision/release | 引用释放与 legal hold 不能物理误删；复合 PK 不用 Guid 替换 |
| `system_notification_announcement`、`system_notification_message`、`system_notification_inbox_delivery` | announcement title/body/priority/status/audience/publish/expiry/revoke/resource/route/idempotency；message notification/kind/content/sender；delivery recipient/delivered/read/expiry/revoke | tenant+notification/recipient 幂等；本地通知与服务事实分离 |
| `system_audit_fact`、`system_audit_lifecycle`、`system_audit_ingress_failure`、`system_audit_outbox` | fact producer/event/occurred/received/actor/action/object/payload/hash/trace/severity/source；lifecycle state/retention/archive/delete/legal hold；failure code/summary/hash；outbox event/payload/publish/retry/next/dead | 审计 append/lifecycle 语义；payload/hash 不应被 SQL Server 自动截断 |
| `system_data_operation_audit`、`system_data_outbox`、`system_data_seed_ledger` | 记录 tenant/actor/action/object/before/after/trace；outbox event/payload/retry；seed migration/seed identity/checksum/status | 本地事实与外部 Audit 不可达时停止/重试规则由当前 worker 保持 |
| PF04 `system_file_*`、notification、audit 组合 | 以上文件/通知/审计表是 PF04 事实 | `Pf04Store` 内写入、outbox、audit port 的事务边界需 SQL-A06/A07 验证 |
| 编排表：`system_data_database_registration`、`system_data_database_environment_policy`、`system_data_database_provision_plan`、`system_data_database_plan_step`、`system_data_database_provision_operation`、`system_data_database_operation_step`、`system_data_database_migration_observation`、`system_data_database_seed_observation`、`system_data_database_backup_evidence`、`system_data_database_approval` | registration/service/provider/environment/database/schema/connection ref/status；policy environment/provider/topology/allow flags；plan/step/operation/observation/backup/approval 各自有 operation/step/status/error/checksum/signature/started/completed/evidence 等列 | 编排账本不能存 secret；operation/step 状态转移与审批必须有唯一键、时间和错误证据；备份路径是服务器事实而非客户端路径 |

SystemData 的表建链：公共控制/基础表 → PF04 file/notification/audit → seed ledger/outbox → database orchestration tables → critical-table drift validation。`UserAssignmentAdvisoryLock` 不是数据库表，但其 SQL Server 适配必须与 `UserAssignmentStore` 同连接/事务，并且与 migration/init lock 分离。

### 3.4 ReferenceData（serviceKey=`referencedata`）

七个逻辑模块共享一个服务级初始化单元：Dictionary、Parameter、DynamicProperty、Metadata、CodingRule、StateMachine、UnitOfMeasure。当前 `ReferenceDataSqlNames.Table()`：PG 走 schema.table，非 PG 走 `reference_data_<name>`；SQL Server 必须新增显式分支。

| 模块/表前缀 | 关键列组 | 物理/并发设计 |
| --- | --- | --- |
| `dictionary_*` | tenant/nid/normalized、code/name/label/status、version/effective、history 字段 | SQL Server schema/table 名明确；编码/名称唯一键包含租户和软删/状态 |
| `parameter_*` | parameter/configuration nid、key/value/type、tenant、revision/version、effective/status/history | JSON/string 值长度与类型语义分开；`ParameterRepository` 写业务事实后同事务写 outbox |
| `dynamic_property_*` | definition/value、entity/object nid、property key/type/value、tenant、revision | value 可能为 JSON/text；大小写与尾随空格需 SQL-A05 |
| `metadata_*` | schema/field/label/type/required/visible、version/status、tenant | `MetadataSchemaRepository` 与 outbox；不得把 JSON 约束遗漏到应用校验 |
| `coding_rule_*` | rule nid、scope、prefix、pattern、next value/sequence、version/status、tenant | 序号分配/并发更新必须 SQL Server 事务锁或原子更新；不使用 SQLite 自增假设 |
| `state_machine_*` | machine/state/transition/action/guard、tenant、version/status | 机器/状态/transition 唯一键和跨引用；状态迁移顺序不由字符串排序推断 |
| `unit_of_measure_*` | dimension/unit/code/symbol/factor/offset、tenant、version/status | decimal precision/scale 必须显式确定；换算结果不能因 SQL Server numeric 默认值舍入 |
| `reference_data_schema_migrations`、`reference_data_seed_ledger` | migration id/description/checksum/applied_on；seed id/version/class/scope/checksum/status/applied_on | PG schema 下为 `reference_data.*` 候选；SQL Server 同 schema/显式表名；初始化 ledger 与七模块共享一个锁 |
| `reference_data_outbox` | event/module/aggregate/payload/created/published/retry/lease/error | `ReferenceDataOutboxWriter/Store/Dispatcher` 的领取、租约、重试与业务写同一事实边界；没有入站消费者不建 Inbox |
| cache generation | module/key/generation/changed_on（由 `ReferenceDataCacheGenerationStore`/迁移定义） | cache generation 提交晚于业务事实；断线后不能错误回退到旧代次 |

每个模块的完整字段卡必须从对应 `*Rows.cs`、`*Repository.cs`、`*Migration.cs` 和 `ReferenceDataMigrations.cs` 逐项回写；本表是设计入口，不把模块名当作一张不存在的总表。

### 3.5 Collaboration（serviceKey=`collaboration`）

权威入口：`CollaborationSchemaMigrations.cs`、`CollaborationTables.cs`、`RemoteAssistanceTables.cs`、`SqlCollaborationRepository*.cs`、`CollaborationServiceInitializer.cs`。PF05/PF06 持久化事实保持不变。

| 表 | 关键列 | SQL Server 设计与并发 |
| --- | --- | --- |
| `collaboration_conversation` | common + tenant/nid/participant_low/high/status/last_message_sequence/last_message_n_id/last_message_on/retention_floor_sequence | tenant+nid、participant pair 唯一；低/高排序 check；消息序号为 bigint |
| `collaboration_conversation_member` | tenant/conversation/user/display snapshot/joined/visibility/hidden through/last read/unread/projection version | conversation+user 唯一；user 查询索引；未读 projection 原子更新 |
| `collaboration_message` | tenant/conversation/message_n_id/sequence/sender/client_message_n_id/request_hash/type/text/reply/attachment/accepted/retracted/reason/state version | conversation+sequence、sender+client idempotency 唯一；序号分配不能 `MAX+1` 无锁 |
| `collaboration_chat_attachment` | tenant/conversation/attachment/uploader/file snapshot/type/size/purpose/file state/reference state/retention/bound message/intent/reference | attachment nid、intent 唯一；文件事实由 SystemData 所有，引用不可伪造 |
| `collaboration_messaging_deduplication` | tenant/sender/client_message_n_id/request_hash/message_n_id/conversation/sequence/accepted/content_purged | 三列复合 PK；重投返回原 message/sequence |
| `collaboration_compliance_disposition` | tenant/disposition/subject type/nid/state/reason/created by/expiry/request/hash | disposition、request 幂等唯一；合规状态约束 |
| `collaboration_legal_hold_case` | tenant/hold/state/scope_json/scope_checksum/reason/release/approval/review/external case/file sync | hold/release request 唯一；scope checksum 与 JSON 同时验证 |
| `collaboration_compliance_export` | tenant/export/state/scope/reason/expiry/artifact/request/case/approval/run/completed/error/fields/operation/retention | export/request/operation 唯一；worker lease/expiry 不得被普通更新覆盖 |
| `collaboration_retention_policy` | tenant/policy/message/attachment/audit days/enabled/export/review/status | tenant+policy 唯一；天/小时单位固定 |
| `collaboration_compliance_command`、`collaboration_compliance_preparation`、`collaboration_compliance_view_budget` | command/preparation 的 tenant/actor/request/action/target/hash/scope/json/status/time；budget 的 tenant/actor/window/result_count | preparation/command 复合 PK；view budget 受窗口并发控制 |
| `collaboration_outbox_message`、`collaboration_event_inbox` | event/tenant/type/payload/timestamps/retry/error/lease/dead；inbox status/received/processed/retry/lease | outbox lease/重试；inbox 当前服务确有入站事实，不能按 ReferenceData 规则删除 |
| `collaboration_retention_checkpoint` | id/tenant/operation/cutoff/last accepted/message/stage/lease/error | tenant+operation 唯一；租约领取需 SQL Server 行锁/跳过锁语义 |
| `collaboration_remote_assistance_session` | tenant/nid/conversation/initiator/invitee/direction/sharer/viewer/state/answer/request/hash/deadline/accepted/started/ended/reason/connection/alive/stopped | conversation active、sharer active filtered unique；PF06 停止必须持久化 |
| `collaboration_remote_assistance_voice_call` | tenant/nid/conversation/caller/callee/state/answer/request/hash/deadline/accepted/started/ended/reason/connection/alive/ready/stopped | active conversation filtered unique；deadline/lease/停止竞争需真实并发验证 |
| `collaboration_remote_assistance_voice_slot` | tenant/user/voice_call/created/expires | `(tenant,user)` PK；同用户语音 slot 唯一；不能进程锁替代 |

Collaboration 初始化链：ledger inspect（表/列/旧 checksum）→ ordered migration → step validate / critical table query → same-transaction ledger write → verify。当前 initializer 的 PG/SQLite metadata query 和 `ALTER TABLE ... ADD COLUMN` 分支需统一到 SQL Server metadata/DDL helper。

## 4. D04：迁移、种子、账本、锁和执行链

### 4.1 账本与 checksum 合同

| 服务/账本 | 当前类/表 | 计算规则 | SQL Server 要求 |
| --- | --- | --- | --- |
| Identity migration | `SchemaMigrationRecord` / `identity_schema_migrations` | `SHA-256(UTF8(Id|Description))`，小写 hex；旧 null checksum 只在 Description 精确匹配时回填 | `migration_id` PK、description/checksum/applied_on；同一事务；历史 checksum 不重写 |
| SystemData migration | `SchemaMigrationRecord` / `system_data_schema_migrations` | 同上；`SystemDataMigrationHelpers` 当前包含 PG/SQLite 方言词 | TypeWords 增加 SQL Server；DDL/索引/filtered unique 全部进入 step checksum 或不可变 artifact |
| Collaboration migration | `CollaborationSchemaMigrationRecord` / `collaboration_schema_migrations` | `SHA-256(UTF8(Id|Description))`；initializer 已有 checksum drift | metadata/ALTER/critical validation 需 SQL Server 分支，旧 PG/SQLite 记录保持 |
| ReferenceData migration | `ReferenceDataInitializationLedger` 及服务 migration ledger | 服务版本、步骤描述/checksum、applied 状态 | 七模块共享初始化互斥；schema/table identity 进入 observed facts |
| Identity/SystemData/ReferenceData seed | 各服务 seed ledger | seed id/version/class/scope/ordered content checksum/status | seed 写入与业务事实原子；内容变化拒绝伪装成同版本成功 |
| SystemData artifact | `MigrationArtifactChecksumVerifier` / runner contracts | migration：按 sequence 排序的 `Sequence|StepId|Sql|RollbackSql|Destructive`；seed：`Sequence|StepId|Sql|RollbackSql`；前后拼 Version/SignatureRef | checksum、expected checksum、SignatureRef 三者一致；真实密码学签名另设 verifier，未接入前不得宣称 signed |

### 4.2 锁资源矩阵

| 资源 | 当前实现 | SQL Server 候选（待审） | 必须保证 |
| --- | --- | --- | --- |
| 服务 migration/init | runner 当前依赖目标库事务/表账本，统一 coordinator 排序 | 同一数据库显式应用锁（候选 `sp_getapplock`，Transaction owner）或锁表行；超时/释放可观测 | 两进程只执行一次；失败回滚后可重试 |
| seed ledger | ledger unique identity + runner 顺序 | unique key + transaction/app lock | 同版本不同 checksum 拒绝；不重复副作用 |
| `UserAssignment` 用户锁 | PG `pg_advisory_xact_lock(hashtextextended(tenant|user))`；SQLite 进程信号量 | `sp_getapplock @Resource = tenant|user, @LockOwner='Transaction'` 候选；与 Store 共用连接/事务 | 同一用户串行，不阻塞无关租户/用户；commit/rollback 自动释放 |
| Collaboration message sequence | 当前业务/repository + dedup/unique | transaction + update lock/序号 allocator（不得裸 `MAX+1`） | 重投返回原序号，不能重复消息 |
| Voice slot / remote assistance | DB active unique + 业务状态 | unique PK + conditional/filtered index 或状态更新事务 | 同一用户/会话最多一个 active slot |
| Outbox lease | published/retry/lease 字段 | `UPDLOCK, READPAST, ROWLOCK` 等需以 SQL Server 兼容级别实测 | 领取不重复，提交时断连可恢复 |
| backup/operation apply | SystemData operation/step 状态 | operation id + approval + lock resource | Inspect 不写；Apply/rollback/backup 证据完整 |

`sp_getapplock` 只是候选：主控必须确认数据库权限、事务 owner、死锁/超时错误码、连接断开时释放、SQL Server 2019/2022兼容级别；若不批准，则改为受控锁表并纳入 migration/ledger 设计。

### 4.3 服务执行链

统一链路：

`Resolve target → Inspect identity/read-only → Inspect local ledger/physical shape → Plan missing steps → acquire service lock → begin transaction → execute one step → validate shape → write migration/seed observation → commit → release → Verify`

服务差异：

- Identity：UnifiedHost 按 coordinator 在 SystemData 之后触发，Identity 自己拥有 migration/seed/bootstrap/verify；登录/刷新验证必须指向同一目标。
- SystemData：先自己的基础 schema，再文件/通知/审计、PF04、编排对象；编排 Apply 还要经过 registration → plan → approval → backup → artifact verify → migration/seed executor。
- ReferenceData：七模块共享 `ReferenceDataServiceInitializer`；不拆成七把独立锁，模块级表/seed/outbox 在服务锁内按固定顺序完成。
- Collaboration：在 Identity/SystemData 后执行；PF05/PF06 表、outbox/inbox、retention、remote assistance 一起按 migration id 顺序完成。

错误分类必须可观察：target mismatch、provider unsupported、artifact checksum/signature invalid、schema drift、permission denied、lock timeout/deadlock、migration failed、seed failed、unknown commit result。未知提交结果不能自动重做业务副作用，必须通过 ledger/operation inspect 决定恢复。

## 5. D06：验证入口、证据和 SQL-A 映射

| 设计点 | 入口/断言 | SQL-A |
| --- | --- | --- |
| Provider/目标 | `DatabaseTopologyResolver`、`ResolvedDatabaseTarget`、SqlSugar connection | A01/A02/A09 |
| 身份 Inspect | server/database/schema/service key 脱敏指纹；只读用户无写入 | A02/A04 |
| 结构漂移 | SQL Server `sys.tables`、`sys.schemas`、`sys.columns`、`sys.indexes`/约束；缺表、缺列、缺索引、权限不可见分开报错 | A04 |
| 迁移幂等 | 空库、合法旧快照、旧 null checksum、二次启动、两进程同时启动 | A03 |
| 类型语义 | Guid、DateTimeOffset、bool、long、decimal、Unicode、JSON、filtered unique | A05 |
| Identity 业务 | 登录、刷新、角色授权、动态菜单、用户查询、SSO/step-up 最小闭环 | A06 |
| SystemData/ReferenceData | CRUD、文件状态/引用、通知/审计、七模块、cache generation/outbox | A06/A07 |
| Collaboration/PF06 | 消息序号/幂等、附件引用、邀请/绑定/停止、remote assistance、voice slot | A06/A07 |
| 故障/重试 | outbox lease/retry/dead letter、提交时断连、锁超时/死锁 | A07 |
| 运维 | registration/plan/approval/backup/apply/inspect/recovery，2019→隔离2022恢复 | A08 |
| 旧 Provider | PG/SQLite 配置、旧迁移 checksum、初始化、漂移、受影响业务 | A09 |
| 客户端联合 | 客户端仅走 API；Web/Electron/PDA 不持有 DB 连接信息 | A10 |

建议新增/补齐的测试入口（实现阶段再落文件）：

1. `tests/BuildingBlocks/IndustrialPlatform.Infrastructure.Tests`：Provider mapping、SQL Server metadata adapter、config rejection、schema identity。
2. `tests/Identity/IndustrialPlatform.Identity.Tests`：migration/seed/bootstrap/lock/checksum，沿用现有 `FullyQualifiedName~Migration|Seed|Bootstrap` 过滤。
3. `tests/SystemData/IndustrialPlatform.SystemData.Tests`：SystemData migration helpers、PF04、assignment lock、orchestration artifact/ledger/router。
4. `tests/ReferenceData/IndustrialPlatform.ReferenceData.Tests`：七模块 migration/seed/cache/outbox/initializer。
5. `tests/Collaboration/IndustrialPlatform.Collaboration.Tests`：migration/repository/outbox/inbox/remote assistance/voice slot。
6. `tests/Integration` 或现有真实引擎 harness：SQL Server 2019/150 与 2022/160 两套隔离目标；每个目标记录 Edition、CU、compatibility level、driver、OS、topology、迁移版本。

真实引擎未提供前：SQL-A01～A09 均为 `NOT RUN/BLOCKED`，不能用 SQLite 代替 SQL Server 结论；SQLite 只能作为旧 Provider 回归的一部分。SQL-A10 的前端/Electron/PDA 客户端证据单列，不改变 SQL gate。

## 6. 待关闭的设计项

请主控确认以下四项后关闭 G06A-SQL 设计门：

1. SQL Server schema 采用 `dbo + 服务前缀表名`，还是服务 schema（`identity/system_data/reference_data/collaboration`）。
2. `RemoteDevelopment`/`SqlSugar`/`DatabaseTopology` 的 SQL Server 配置键、优先级、secret 注入与目标身份字段。
3. migration/init/user-assignment/outbox/voice-slot 的 SQL Server 锁实现：是否批准 `sp_getapplock`（事务 owner、超时、权限）或指定锁表方案。
4. 最终 SQL Client/SqlSugar 支持版本、SQL Server 2019/2022 compatibility level，以及 JSON/Unicode/decimal/time 精度合同。

审定完成后，编码顺序固定为：D02 配置/Provider → D03 metadata/drift/type helper → Identity/SystemData/ReferenceData/Collaboration migration → D04 ledger/lock/router/DI → D06 real-engine tests → A01～A09 → A10 联合验收。
