using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;

/// <summary>
/// SystemData 库全部迁移步骤(TASK-SD-002 九张数据库编排控制面表 + TASK-SD-004 模块作用域 ALTER 与种子观察表),由 SchemaMigrationRunner 按标识排序执行。
/// SDM-001 环境策略/注册清单、SDM-002 不可变计划/计划步骤/审批/备份证据、SDM-003 Operation/步骤/迁移观察、
/// SDM-004-01 注册清单模块作用域(module_key/seed_sets 列 + 唯一索引重建为四元组)、SDM-004-02 种子观察表。
/// DDL 类型词按目标数据库分支(SQLite 用 TEXT/INTEGER,PostgreSQL 用 uuid/timestamptz/BOOLEAN/BIGINT);
/// 无种子数据,环境策略默认由 Application 解析回退,不硬编码租户。
/// </summary>
public static class SystemDataSchemaMigrations
{
    /// <summary>全部迁移步骤,运行器按 Id 排序并幂等记账。</summary>
    public static IReadOnlyList<SchemaMigrationStep> All { get; } = BuildSteps();

    private static IReadOnlyList<SchemaMigrationStep> BuildSteps()
    {
        // 账本 migration_id 唯一:每张表一个迁移步骤,步骤 Id 带 SDM 分组前缀 + 顺序号;
        // 按 Id 序号排序即按建表依赖顺序执行(先父后子,PostgreSQL 引用表须先存在)。
        return
        [
            SystemDataMigrationHelpers.CreateTableStep("SDM-001-01", "system_data_database_environment_policy", EnvironmentPolicyDdl),
            SystemDataMigrationHelpers.CreateTableStep("SDM-001-02", "system_data_database_registration", RegistrationDdl),
            SystemDataMigrationHelpers.CreateTableStep("SDM-002-01", "system_data_database_plan", PlanDdl),
            SystemDataMigrationHelpers.CreateTableStep("SDM-002-02", "system_data_database_plan_step", PlanStepDdl),
            SystemDataMigrationHelpers.CreateTableStep("SDM-002-03", "system_data_database_approval", ApprovalDdl),
            SystemDataMigrationHelpers.CreateTableStep("SDM-002-04", "system_data_database_backup_evidence", BackupEvidenceDdl),
            SystemDataMigrationHelpers.CreateTableStep("SDM-003-01", "system_data_database_operation", OperationDdl),
            SystemDataMigrationHelpers.CreateTableStep("SDM-003-02", "system_data_database_operation_step", OperationStepDdl),
            SystemDataMigrationHelpers.CreateTableStep("SDM-003-03", "system_data_database_migration_observation", MigrationObservationDdl),
            SystemDataMigrationHelpers.CreateRawStep("SDM-004-01", "alter registration module scope", RegistrationModuleScopeAlterDdl),
            SystemDataMigrationHelpers.CreateTableStep("SDM-004-02", "system_data_seed_observation", SeedObservationDdl),
            SystemDataMigrationHelpers.CreateTableStep("SDM-004-03", "system_data_organization", OrganizationDdl),
            SystemDataMigrationHelpers.CreateTableStep("SDM-005-01", "system_data_position", PositionDdl),
            SystemDataMigrationHelpers.CreateTableStep("SDM-006-01", "system_data_user_assignment", UserAssignmentDdl),
        ];
    }

    private static string EnvironmentPolicyDdl(DbType dbType)
    {
        var (g, t, b, big, f) = SystemDataMigrationHelpers.TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            SystemDataMigrationHelpers.CommonColumns(g, t, b, big),
            "tenant_n_id TEXT NOT NULL",
            "environment_n_id TEXT NOT NULL",
            "environment_kind INTEGER NOT NULL",
            $"approval_required {b} NOT NULL",
            $"backup_required {b} NOT NULL",
            "plan_ttl_seconds INTEGER NOT NULL",
            "plan_timeout_seconds INTEGER NOT NULL",
            "apply_timeout_seconds INTEGER NOT NULL",
            "max_pre_migration_retries INTEGER NOT NULL",
            "policy_revision INTEGER NOT NULL",
            "CONSTRAINT uq_env_policy_id_isdel UNIQUE (id, is_deleted)",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS system_data_database_environment_policy (
            {columns}
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_env_policy_active ON system_data_database_environment_policy (tenant_n_id, environment_n_id) WHERE is_deleted = {f};
            """;
    }

    private static string RegistrationDdl(DbType dbType)
    {
        var (g, t, b, big, f) = SystemDataMigrationHelpers.TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            SystemDataMigrationHelpers.CommonColumns(g, t, b, big),
            "tenant_n_id TEXT NOT NULL",
            "environment_n_id TEXT NOT NULL",
            "service_key TEXT NOT NULL",
            "provider TEXT NOT NULL",
            "logical_database_name TEXT NOT NULL",
            "physical_database_name TEXT NOT NULL",
            $"is_shared_physical_database {b} NOT NULL",
            "topology_mode TEXT NOT NULL",
            "topology_revision TEXT NOT NULL",
            "migration_artifact_id TEXT NOT NULL",
            "migration_version TEXT NOT NULL",
            "artifact_checksum TEXT NOT NULL",
            "artifact_signature TEXT NULL",
            "owner_n_id TEXT NOT NULL",
            "desired_state INTEGER NOT NULL",
            $"auto_provision {b} NOT NULL",
            $"auto_migrate {b} NOT NULL",
            "manifest_version TEXT NOT NULL",
            "manifest_checksum TEXT NOT NULL",
            "status INTEGER NOT NULL",
            "CONSTRAINT uq_registration_id_isdel UNIQUE (id, is_deleted)",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS system_data_database_registration (
            {columns}
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_registration_active ON system_data_database_registration (tenant_n_id, environment_n_id, service_key) WHERE is_deleted = {f};
            """;
    }

    private static string PlanDdl(DbType dbType)
    {
        var (g, t, b, big, f) = SystemDataMigrationHelpers.TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            SystemDataMigrationHelpers.CommonColumns(g, t, b, big),
            "tenant_n_id TEXT NOT NULL",
            "plan_n_id TEXT NOT NULL",
            "environment_n_id TEXT NOT NULL",
            "service_key TEXT NOT NULL",
            "requested_migration_version TEXT NOT NULL",
            "current_migration_version TEXT NOT NULL",
            "target_state_fingerprint TEXT NOT NULL",
            "plan_checksum TEXT NOT NULL",
            "risk_level INTEGER NOT NULL",
            $"destructive_change_detected {b} NOT NULL",
            "required_policies INTEGER NOT NULL",
            $"expires_on {t} NOT NULL",
            "created_by_user_n_id TEXT NOT NULL",
            "CONSTRAINT uq_plan_id_isdel UNIQUE (id, is_deleted)",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS system_data_database_plan (
            {columns}
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_plan_active_nid ON system_data_database_plan (tenant_n_id, plan_n_id) WHERE is_deleted = {f};
            CREATE UNIQUE INDEX IF NOT EXISTS ux_plan_active_checksum ON system_data_database_plan (plan_checksum) WHERE is_deleted = {f};
            """;
    }

    private static string PlanStepDdl(DbType dbType)
    {
        var (g, t, b, big, _) = SystemDataMigrationHelpers.TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            SystemDataMigrationHelpers.CommonColumns(g, t, b, big),
            $"plan_id {g} NOT NULL",
            $"plan_is_deleted {b} NOT NULL",
            "sequence INTEGER NOT NULL",
            "step_kind TEXT NOT NULL",
            "input_summary TEXT NULL",
            "precondition_summary TEXT NULL",
            "postcondition_summary TEXT NULL",
            "risk_level INTEGER NOT NULL",
            "FOREIGN KEY (plan_id, plan_is_deleted) REFERENCES system_data_database_plan (id, is_deleted) ON UPDATE CASCADE",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS system_data_database_plan_step (
            {columns}
            );
            CREATE INDEX IF NOT EXISTS ix_plan_step_parent ON system_data_database_plan_step (plan_id, sequence);
            """;
    }

    private static string ApprovalDdl(DbType dbType)
    {
        var (g, t, b, big, f) = SystemDataMigrationHelpers.TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            SystemDataMigrationHelpers.CommonColumns(g, t, b, big),
            "tenant_n_id TEXT NOT NULL",
            "approval_n_id TEXT NOT NULL",
            "plan_n_id TEXT NOT NULL",
            "plan_checksum TEXT NOT NULL",
            "target_state_fingerprint TEXT NOT NULL",
            "approved_by_user_n_id TEXT NOT NULL",
            "reason TEXT NULL",
            $"approved_on {t} NOT NULL",
            $"expires_on {t} NOT NULL",
            "status INTEGER NOT NULL",
            "CONSTRAINT uq_approval_id_isdel UNIQUE (id, is_deleted)",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS system_data_database_approval (
            {columns}
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_approval_active_nid ON system_data_database_approval (tenant_n_id, approval_n_id) WHERE is_deleted = {f};
            CREATE INDEX IF NOT EXISTS ix_approval_plan ON system_data_database_approval (plan_n_id, approved_on);
            """;
    }

    private static string BackupEvidenceDdl(DbType dbType)
    {
        var (g, t, b, big, f) = SystemDataMigrationHelpers.TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            SystemDataMigrationHelpers.CommonColumns(g, t, b, big),
            "tenant_n_id TEXT NOT NULL",
            "evidence_n_id TEXT NOT NULL",
            "plan_n_id TEXT NOT NULL",
            "plan_checksum TEXT NOT NULL",
            "target_state_fingerprint TEXT NOT NULL",
            "backup_provider TEXT NOT NULL",
            "backup_reference TEXT NOT NULL",
            $"captured_on {t} NOT NULL",
            $"verified_on {t} NULL",
            $"retention_until {t} NOT NULL",
            "verified_by_user_n_id TEXT NULL",
            "status INTEGER NOT NULL",
            "CONSTRAINT uq_backup_evidence_id_isdel UNIQUE (id, is_deleted)",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS system_data_database_backup_evidence (
            {columns}
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_backup_evidence_active_nid ON system_data_database_backup_evidence (tenant_n_id, evidence_n_id) WHERE is_deleted = {f};
            CREATE INDEX IF NOT EXISTS ix_backup_evidence_plan ON system_data_database_backup_evidence (plan_n_id, captured_on);
            """;
    }

    private static string OperationDdl(DbType dbType)
    {
        var (g, t, b, big, f) = SystemDataMigrationHelpers.TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            SystemDataMigrationHelpers.CommonColumns(g, t, b, big),
            "tenant_n_id TEXT NOT NULL",
            "operation_n_id TEXT NOT NULL",
            "kind INTEGER NOT NULL",
            "environment_n_id TEXT NOT NULL",
            "service_key TEXT NOT NULL",
            "plan_n_id TEXT NULL",
            "requested_version TEXT NOT NULL",
            "idempotency_key TEXT NOT NULL",
            "request_hash TEXT NOT NULL",
            "status INTEGER NOT NULL",
            "phase INTEGER NOT NULL",
            "attempt INTEGER NOT NULL",
            "lease_owner TEXT NULL",
            $"lease_expires_on {t} NULL",
            $"heartbeat_on {t} NULL",
            $"queued_on {t} NOT NULL",
            $"started_on {t} NULL",
            $"completed_on {t} NULL",
            $"timeout_on {t} NOT NULL",
            "sanitized_error_code TEXT NULL",
            "sanitized_error_summary TEXT NULL",
            "trace_id TEXT NOT NULL",
            "created_by_user_n_id TEXT NOT NULL",
            "CONSTRAINT uq_operation_id_isdel UNIQUE (id, is_deleted)",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS system_data_database_operation (
            {columns}
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_operation_active_nid ON system_data_database_operation (tenant_n_id, operation_n_id) WHERE is_deleted = {f};
            CREATE UNIQUE INDEX IF NOT EXISTS ux_operation_active_idempotency ON system_data_database_operation (tenant_n_id, idempotency_key) WHERE is_deleted = {f};
            CREATE INDEX IF NOT EXISTS ix_operation_tenant_status ON system_data_database_operation (tenant_n_id, status, queued_on);
            """;
    }

    private static string OperationStepDdl(DbType dbType)
    {
        var (g, t, b, big, _) = SystemDataMigrationHelpers.TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            SystemDataMigrationHelpers.CommonColumns(g, t, b, big),
            $"operation_id {g} NOT NULL",
            $"operation_is_deleted {b} NOT NULL",
            "sequence INTEGER NOT NULL",
            "phase INTEGER NOT NULL",
            "attempt INTEGER NOT NULL",
            "status INTEGER NOT NULL",
            $"started_on {t} NULL",
            $"completed_on {t} NULL",
            "sanitized_error_code TEXT NULL",
            "sanitized_error_summary TEXT NULL",
            "FOREIGN KEY (operation_id, operation_is_deleted) REFERENCES system_data_database_operation (id, is_deleted) ON UPDATE CASCADE",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS system_data_database_operation_step (
            {columns}
            );
            CREATE INDEX IF NOT EXISTS ix_operation_step_parent ON system_data_database_operation_step (operation_id, sequence);
            """;
    }

    private static string MigrationObservationDdl(DbType dbType)
    {
        var (g, t, b, big, _) = SystemDataMigrationHelpers.TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            SystemDataMigrationHelpers.CommonColumns(g, t, b, big),
            "tenant_n_id TEXT NOT NULL",
            "environment_n_id TEXT NOT NULL",
            "service_key TEXT NOT NULL",
            "database_identity_fingerprint TEXT NOT NULL",
            "observed_version TEXT NOT NULL",
            "artifact_checksum TEXT NOT NULL",
            $"observed_on {t} NOT NULL",
            "operation_n_id TEXT NULL",
            "verification_status INTEGER NOT NULL",
            "CONSTRAINT uq_migration_observation_id_isdel UNIQUE (id, is_deleted)",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS system_data_database_migration_observation (
            {columns}
            );
            CREATE INDEX IF NOT EXISTS ix_migration_observation_target ON system_data_database_migration_observation (tenant_n_id, environment_n_id, service_key, observed_on);
            """;
    }

    /// <summary>
    /// SDM-004-01:模块作用域列。注册清单增 module_key/seed_sets 列并重建唯一索引为
    /// (tenant_n_id, environment_n_id, service_key, module_key);plan/operation 表同步增
    /// module_key 列(既有行回填 = service_key,保持 v1 兼容)。
    /// </summary>
    private static string RegistrationModuleScopeAlterDdl(DbType dbType)
    {
        var (_, _, _, _, f) = SystemDataMigrationHelpers.TypeWords(dbType);
        // SQLite 不支持 ADD COLUMN IF NOT EXISTS;既有索引由 SDM-001-02 创建,直接 DROP 再重建四元组部分唯一索引。
        var ifNotExists = dbType == DbType.PostgreSQL ? " IF NOT EXISTS" : string.Empty;
        return $"""
            ALTER TABLE system_data_database_registration ADD COLUMN{ifNotExists} module_key TEXT NULL;
            UPDATE system_data_database_registration SET module_key = service_key WHERE module_key IS NULL;
            ALTER TABLE system_data_database_registration ADD COLUMN{ifNotExists} seed_sets TEXT NULL;
            DROP INDEX IF EXISTS ux_registration_active;
            CREATE UNIQUE INDEX IF NOT EXISTS ux_registration_active ON system_data_database_registration (tenant_n_id, environment_n_id, service_key, module_key) WHERE is_deleted = {f};
            ALTER TABLE system_data_database_plan ADD COLUMN{ifNotExists} module_key TEXT NULL;
            UPDATE system_data_database_plan SET module_key = service_key WHERE module_key IS NULL;
            ALTER TABLE system_data_database_operation ADD COLUMN{ifNotExists} module_key TEXT NULL;
            UPDATE system_data_database_operation SET module_key = service_key WHERE module_key IS NULL;
            """;
    }

    /// <summary>SDM-004-02:脱敏种子观察表(只追加;索引按 (tenant_n_id, environment_n_id, service_key, module_key, seed_key))。</summary>
    private static string SeedObservationDdl(DbType dbType)
    {
        var (g, t, b, big, _) = SystemDataMigrationHelpers.TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            SystemDataMigrationHelpers.CommonColumns(g, t, b, big),
            "tenant_n_id TEXT NOT NULL",
            "environment_n_id TEXT NOT NULL",
            "service_key TEXT NOT NULL",
            "module_key TEXT NOT NULL",
            "seed_key TEXT NOT NULL",
            "seed_version TEXT NOT NULL",
            "checksum TEXT NOT NULL",
            "scope INTEGER NOT NULL",
            "status INTEGER NOT NULL",
            $"applied_on {t} NULL",
            "operation_n_id TEXT NULL",
            "verification_status INTEGER NOT NULL",
            "CONSTRAINT uq_seed_observation_id_isdel UNIQUE (id, is_deleted)",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS system_data_seed_observation (
            {columns}
            );
            CREATE INDEX IF NOT EXISTS ix_seed_observation_target ON system_data_seed_observation (tenant_n_id, environment_n_id, service_key, module_key, seed_key, created_on);
            """;
    }

    /// <summary>
    /// SDM-004-03:行政组织表(05 方案 §8.1 <c>system_data_organization</c>,统一有类型树)。
    /// 公司仅根、同租户多根、父子同租户;normalized_name 承载同父名称大小写不敏感唯一;
    /// 自引用复合外键 (parent_organization_id, parent_organization_is_deleted) ON UPDATE CASCADE;
    /// Tenant+Parent+Status+Order 索引;organization_revision 供移动预览过期校验。
    /// </summary>
    private static string OrganizationDdl(DbType dbType)
    {
        var (g, t, b, big, f) = SystemDataMigrationHelpers.TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            SystemDataMigrationHelpers.CommonColumns(g, t, b, big),
            "tenant_n_id TEXT NOT NULL",
            "n_id TEXT NOT NULL",
            "normalized_n_id TEXT NOT NULL",
            "name TEXT NOT NULL",
            "normalized_name TEXT NOT NULL",
            "type INTEGER NOT NULL",
            "parent_organization_n_id TEXT NULL",
            $"parent_organization_id {g} NULL",
            $"parent_organization_is_deleted {b} NOT NULL",
            "display_order INTEGER NOT NULL",
            "status INTEGER NOT NULL",
            $"organization_revision {big} NOT NULL",
            "CONSTRAINT uq_organization_id_isdel UNIQUE (id, is_deleted)",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS system_data_organization (
            {columns},
            FOREIGN KEY (parent_organization_id, parent_organization_is_deleted)
                REFERENCES system_data_organization (id, is_deleted) ON UPDATE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_organization_active_nid ON system_data_organization (tenant_n_id, normalized_n_id) WHERE is_deleted = {f};
            CREATE UNIQUE INDEX IF NOT EXISTS ux_organization_active_sibling_name ON system_data_organization (tenant_n_id, parent_organization_n_id, normalized_name) WHERE is_deleted = {f};
            CREATE INDEX IF NOT EXISTS ix_organization_parent ON system_data_organization (tenant_n_id, parent_organization_n_id, status, display_order);
            """;
    }

    /// <summary>
    /// SDM-005-01:岗位表(05 方案 §8.1 <c>system_data_position</c>)。
    /// 岗位专属于一个组织:organization_id/organization_is_deleted 复合外键引用组织表 ON UPDATE CASCADE;
    /// normalized_name 承载同组织名称大小写不敏感唯一;Tenant+NormalizedNId 全历史唯一。
    /// </summary>
    private static string PositionDdl(DbType dbType)
    {
        var (g, t, b, big, f) = SystemDataMigrationHelpers.TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            SystemDataMigrationHelpers.CommonColumns(g, t, b, big),
            "tenant_n_id TEXT NOT NULL",
            "n_id TEXT NOT NULL",
            "normalized_n_id TEXT NOT NULL",
            "organization_n_id TEXT NOT NULL",
            $"organization_id {g} NOT NULL",
            $"organization_is_deleted {b} NOT NULL",
            "name TEXT NOT NULL",
            "normalized_name TEXT NOT NULL",
            "description TEXT NULL",
            "display_order INTEGER NOT NULL",
            "status INTEGER NOT NULL",
            "CONSTRAINT uq_position_id_isdel UNIQUE (id, is_deleted)",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS system_data_position (
            {columns},
            FOREIGN KEY (organization_id, organization_is_deleted)
                REFERENCES system_data_organization (id, is_deleted) ON UPDATE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_position_active_nid ON system_data_position (tenant_n_id, normalized_n_id) WHERE is_deleted = {f};
            CREATE UNIQUE INDEX IF NOT EXISTS ux_position_active_org_name ON system_data_position (tenant_n_id, organization_n_id, normalized_name) WHERE is_deleted = {f};
            CREATE INDEX IF NOT EXISTS ix_position_organization ON system_data_position (tenant_n_id, organization_n_id);
            """;
    }

    /// <summary>
    /// SDM-006-01:用户任职表(05 方案 §8.1 <c>system_data_user_assignment</c>)。
    /// 时间化多任职:左闭右开区间 [effective_from, effective_to),effective_to 可空表示开放;
    /// position_id/position_is_deleted 复合外键引用岗位表 ON UPDATE CASCADE;
    /// User+time、Position+time 索引;Tenant+NormalizedNId 唯一。
    /// </summary>
    private static string UserAssignmentDdl(DbType dbType)
    {
        var (g, t, b, big, f) = SystemDataMigrationHelpers.TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            SystemDataMigrationHelpers.CommonColumns(g, t, b, big),
            "tenant_n_id TEXT NOT NULL",
            "n_id TEXT NOT NULL",
            "normalized_n_id TEXT NOT NULL",
            "user_n_id TEXT NOT NULL",
            "user_display_name_snapshot TEXT NOT NULL",
            "organization_n_id TEXT NOT NULL",
            "position_n_id TEXT NOT NULL",
            $"position_id {g} NOT NULL",
            $"position_is_deleted {b} NOT NULL",
            $"is_primary {b} NOT NULL",
            $"effective_from {t} NOT NULL",
            $"effective_to {t} NULL",
            "state INTEGER NOT NULL",
            $"cancelled_on {t} NULL",
            "cancel_reason TEXT NULL",
            "CONSTRAINT uq_user_assignment_id_isdel UNIQUE (id, is_deleted)",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS system_data_user_assignment (
            {columns},
            FOREIGN KEY (position_id, position_is_deleted)
                REFERENCES system_data_position (id, is_deleted) ON UPDATE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_user_assignment_active_nid ON system_data_user_assignment (tenant_n_id, normalized_n_id) WHERE is_deleted = {f};
            CREATE INDEX IF NOT EXISTS ix_user_assignment_user_time ON system_data_user_assignment (tenant_n_id, user_n_id, effective_from);
            CREATE INDEX IF NOT EXISTS ix_user_assignment_position_time ON system_data_user_assignment (position_n_id, effective_from);
            """;
    }
}
