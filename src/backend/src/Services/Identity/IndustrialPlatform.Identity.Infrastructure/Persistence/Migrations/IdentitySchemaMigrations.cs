using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;

/// <summary>
/// 身份库 DDL 迁移步骤(§11 建表 + §29A.4 种子账本/凭据交付表 + must_change_password 列)。
/// 由 SchemaMigrationRunner 按标识排序执行;DDL 类型词按目标数据库分支
/// (SQLite 用 TEXT/INTEGER,PostgreSQL 用 uuid/timestamptz/BOOLEAN)。
/// 三层种子(SystemCatalog/TenantSecurity/BootstrapAdmin)由 IdentitySeedRunner 执行并写
/// <c>identity_seed_ledger</c>,不在此混合;密码只保存哈希,明文不进入任何配置或代码常量。
/// </summary>
public static class IdentitySchemaMigrations
{
    /// <summary>全部迁移步骤,运行器按 Id 排序并幂等记账。</summary>
    public static IReadOnlyList<SchemaMigrationStep> All { get; } = BuildSteps();

    private static IReadOnlyList<SchemaMigrationStep> BuildSteps()
    {
        return
        [
            CreateTableStep("ID-004-01", "identity_user", UserDdl),
            CreateTableStep("ID-004-02", "identity_role", RoleDdl),
            CreateTableStep("ID-004-03", "identity_permission", PermissionDdl),
            CreateTableStep("ID-004-04", "identity_user_role", UserRoleDdl),
            CreateTableStep("ID-004-05", "identity_role_permission", RolePermissionDdl),
            CreateTableStep("ID-004-06", "identity_refresh_session", RefreshSessionDdl),
            CreateTableStep("ID-004-07", "identity_login_audit", LoginAuditDdl),
            CreateTableStep("ID-004-08", "identity_operation_audit", OperationAuditDdl),
            CreateTableStep("ID-004-09", "identity_outbox", OutboxDdl),
            CreateTableStep("ID-004-12", "identity_sso_provider", SsoProviderDdl),
            CreateTableStep("ID-004-13", "identity_sso_external_account", SsoExternalAccountDdl),
            CreateTableStep("ID-004-14", "identity_sso_client", SsoClientDdl),
            CreateTableStep("ID-004-15", "identity_sso_client_endpoint", SsoClientEndpointDdl),
            CreateTableStep("ID-004-16", "identity_sso_browser_session", SsoBrowserSessionDdl),
            CreateTableStep("ID-017-01", "identity_user_group", UserGroupDdl),
            CreateTableStep("ID-017-02", "identity_user_group_membership", UserGroupMembershipDdl),
            CreateTableStep("ID-017-03", "identity_user_group_role", UserGroupRoleDdl),
            CreateTableStep("ID-019-01", "identity_seed_ledger", SeedLedgerDdl),
            CreateTableStep("ID-019-02", "identity_bootstrap_credential", BootstrapCredentialDdl),
            new SchemaMigrationStep(
                "ID-019-03",
                "add identity_user.must_change_password column",
                AddMustChangePasswordColumnAsync),
        ];
    }

    private static SchemaMigrationStep CreateTableStep(
        string id,
        string tableName,
        Func<DbType, string> ddlBuilder) =>
        new(id, $"create {tableName}", (sugar, _) => sugar.Ado.ExecuteCommandAsync(ddlBuilder(sugar.CurrentConnectionConfig.DbType)));

    private static (string G, string T, string B, string Big, string F) TypeWords(DbType dbType) =>
        dbType switch
        {
            DbType.Sqlite => ("TEXT", "TEXT", "INTEGER", "INTEGER", "0"),
            DbType.PostgreSQL => ("uuid", "timestamptz", "BOOLEAN", "BIGINT", "false"),
            _ => throw new NotSupportedException($"不支持的目标数据库类型:{dbType}。"),
        };

    private static string CommonColumns(string g, string t, string b, string big) =>
        string.Join(",\n",
        [
            $"id {g} PRIMARY KEY NOT NULL",
            $"is_frozen {b} NOT NULL",
            $"is_locked {b} NOT NULL",
            $"is_deleted {b} NOT NULL",
            "entity_type TEXT NOT NULL",
            $"created_on {t} NOT NULL",
            $"last_updated_on {t} NOT NULL",
            $"optimistic_version {big} NOT NULL",
            $"concurrency_version {g} NOT NULL",
        ]);

    private static string UserDdl(DbType dbType)
    {
        var (g, t, b, big, f) = TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            CommonColumns(g, t, b, big),
            "tenant_n_id TEXT NOT NULL",
            "n_id TEXT NOT NULL",
            "normalized_n_id TEXT NOT NULL",
            "login_name TEXT NOT NULL",
            "normalized_login_name TEXT NOT NULL",
            "name TEXT NOT NULL",
            "password_hash TEXT NOT NULL",
            "email TEXT NULL",
            "phone TEXT NULL",
            "status INTEGER NOT NULL",
            "failed_login_count INTEGER NOT NULL",
            $"locked_until {t} NULL",
            "auth_version INTEGER NOT NULL",
            $"last_login_on {t} NULL",
            $"must_change_password {b} NOT NULL DEFAULT {f}",
            "CONSTRAINT uq_user_nid UNIQUE (tenant_n_id, normalized_n_id)",
            "CONSTRAINT uq_user_id_isdel UNIQUE (id, is_deleted)",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS identity_user (
            {columns}
            );
            CREATE INDEX IF NOT EXISTS ix_user_status_updated ON identity_user (status, last_updated_on, id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_user_active_login_name ON identity_user (tenant_n_id, normalized_login_name) WHERE is_deleted = {f};
            """;
    }

    private static string RoleDdl(DbType dbType)
    {
        var (g, t, b, big, _) = TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            CommonColumns(g, t, b, big),
            "tenant_n_id TEXT NOT NULL",
            "n_id TEXT NOT NULL",
            "normalized_n_id TEXT NOT NULL",
            "name TEXT NOT NULL",
            "description TEXT NULL",
            $"is_system {b} NOT NULL",
            "CONSTRAINT uq_role_nid UNIQUE (tenant_n_id, normalized_n_id)",
            "CONSTRAINT uq_role_id_isdel UNIQUE (id, is_deleted)",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS identity_role (
            {columns}
            );
            """;
    }

    private static string PermissionDdl(DbType dbType)
    {
        var (g, t, b, big, _) = TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            CommonColumns(g, t, b, big),
            "n_id TEXT NOT NULL",
            "normalized_n_id TEXT NOT NULL",
            "name TEXT NOT NULL",
            "type INTEGER NOT NULL",
            "parent_permission_n_id TEXT NULL",
            $"parent_permission_id {g} NULL",
            $"parent_permission_is_deleted {b} NULL",
            "description TEXT NULL",
            "status INTEGER NOT NULL",
            "CONSTRAINT uq_permission_nid UNIQUE (normalized_n_id)",
            "CONSTRAINT uq_permission_id_isdel UNIQUE (id, is_deleted)",
            "CONSTRAINT ck_permission_parent CHECK ((parent_permission_n_id IS NULL AND parent_permission_id IS NULL AND parent_permission_is_deleted IS NULL) OR (parent_permission_n_id IS NOT NULL AND parent_permission_id IS NOT NULL AND parent_permission_is_deleted IS NOT NULL))",
            "FOREIGN KEY (parent_permission_id, parent_permission_is_deleted) REFERENCES identity_permission (id, is_deleted) ON UPDATE CASCADE",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS identity_permission (
            {columns}
            );
            """;
    }

    private static string UserRoleDdl(DbType dbType)
    {
        var (g, t, b, big, f) = TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            CommonColumns(g, t, b, big),
            "tenant_n_id TEXT NOT NULL",
            $"user_id {g} NOT NULL",
            $"user_is_deleted {b} NOT NULL",
            $"role_id {g} NOT NULL",
            $"role_is_deleted {b} NOT NULL",
            "FOREIGN KEY (user_id, user_is_deleted) REFERENCES identity_user (id, is_deleted) ON UPDATE CASCADE",
            "FOREIGN KEY (role_id, role_is_deleted) REFERENCES identity_role (id, is_deleted) ON UPDATE CASCADE",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS identity_user_role (
            {columns}
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_user_role_active ON identity_user_role (tenant_n_id, user_id, role_id) WHERE is_deleted = {f};
            """;
    }

    private static string RolePermissionDdl(DbType dbType)
    {
        var (g, t, b, big, f) = TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            CommonColumns(g, t, b, big),
            $"role_id {g} NOT NULL",
            $"role_is_deleted {b} NOT NULL",
            $"permission_id {g} NOT NULL",
            $"permission_is_deleted {b} NOT NULL",
            "FOREIGN KEY (role_id, role_is_deleted) REFERENCES identity_role (id, is_deleted) ON UPDATE CASCADE",
            "FOREIGN KEY (permission_id, permission_is_deleted) REFERENCES identity_permission (id, is_deleted) ON UPDATE CASCADE",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS identity_role_permission (
            {columns}
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_role_permission_active ON identity_role_permission (role_id, permission_id) WHERE is_deleted = {f};
            """;
    }

    private static string RefreshSessionDdl(DbType dbType)
    {
        var (g, t, b, big, _) = TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            CommonColumns(g, t, b, big),
            "tenant_n_id TEXT NOT NULL",
            "n_id TEXT NOT NULL",
            "family_n_id TEXT NOT NULL",
            $"user_id {g} NOT NULL",
            $"user_is_deleted {b} NOT NULL",
            "token_hash TEXT NOT NULL",
            $"expires_on {t} NOT NULL",
            $"used_on {t} NULL",
            $"revoked_on {t} NULL",
            "revoke_reason TEXT NULL",
            "replaced_by_session_n_id TEXT NULL",
            $"replaced_by_session_id {g} NULL",
            $"replaced_by_session_is_deleted {b} NULL",
            "user_agent_hash TEXT NULL",
            "ip_address_hash TEXT NULL",
            "CONSTRAINT uq_refresh_nid UNIQUE (n_id)",
            "CONSTRAINT uq_refresh_token UNIQUE (token_hash)",
            "CONSTRAINT uq_refresh_id_isdel UNIQUE (id, is_deleted)",
            "FOREIGN KEY (user_id, user_is_deleted) REFERENCES identity_user (id, is_deleted) ON UPDATE CASCADE",
            "FOREIGN KEY (replaced_by_session_id, replaced_by_session_is_deleted) REFERENCES identity_refresh_session (id, is_deleted) ON UPDATE CASCADE",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS identity_refresh_session (
            {columns}
            );
            CREATE INDEX IF NOT EXISTS ix_refresh_family ON identity_refresh_session (family_n_id);
            CREATE INDEX IF NOT EXISTS ix_refresh_user ON identity_refresh_session (user_id);
            """;
    }

    private static string LoginAuditDdl(DbType dbType)
    {
        var (g, t, b, big, _) = TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            CommonColumns(g, t, b, big),
            "tenant_n_id TEXT NOT NULL",
            "user_n_id TEXT NULL",
            "login_name_snapshot TEXT NOT NULL",
            "result INTEGER NOT NULL",
            "failure_reason TEXT NULL",
            "ip_address_hash TEXT NOT NULL",
            "user_agent_hash TEXT NOT NULL",
            "trace_id TEXT NOT NULL",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS identity_login_audit (
            {columns}
            );
            CREATE INDEX IF NOT EXISTS ix_login_audit_tenant ON identity_login_audit (tenant_n_id, created_on);
            CREATE INDEX IF NOT EXISTS ix_login_audit_user ON identity_login_audit (user_n_id, created_on);
            """;
    }

    private static string OperationAuditDdl(DbType dbType)
    {
        var (g, t, b, big, _) = TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            CommonColumns(g, t, b, big),
            "tenant_n_id TEXT NOT NULL",
            "actor_user_n_id TEXT NOT NULL",
            "action TEXT NOT NULL",
            "object_type TEXT NOT NULL",
            "object_n_id TEXT NOT NULL",
            "before_summary TEXT NULL",
            "after_summary TEXT NULL",
            "trace_id TEXT NOT NULL",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS identity_operation_audit (
            {columns}
            );
            CREATE INDEX IF NOT EXISTS ix_operation_audit_tenant ON identity_operation_audit (tenant_n_id, created_on);
            CREATE INDEX IF NOT EXISTS ix_operation_audit_object ON identity_operation_audit (object_type, object_n_id);
            """;
    }

    private static string OutboxDdl(DbType dbType)
    {
        var (g, t, _, _, _) = TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            $"event_id {g} PRIMARY KEY NOT NULL",
            "event_type TEXT NOT NULL",
            "event_version INTEGER NOT NULL",
            "payload TEXT NOT NULL",
            $"event_created_time {t} NOT NULL",
            $"published_on {t} NULL",
            "retry_count INTEGER NOT NULL",
            "last_error TEXT NULL",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS identity_outbox (
            {columns}
            );
            CREATE INDEX IF NOT EXISTS ix_outbox_publish ON identity_outbox (published_on, event_created_time);
            """;
    }

    private static string SsoProviderDdl(DbType dbType)
    {
        var (g, t, b, big, f) = TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            CommonColumns(g, t, b, big),
            "tenant_n_id TEXT NOT NULL",
            "n_id TEXT NOT NULL",
            "normalized_n_id TEXT NOT NULL",
            "name TEXT NOT NULL",
            "protocol INTEGER NOT NULL",
            "authority_or_metadata_url TEXT NOT NULL",
            "client_id_or_entity_id TEXT NOT NULL",
            "secret_or_certificate_reference TEXT NULL",
            "callback_path TEXT NOT NULL",
            $"enabled {b} NOT NULL",
            $"auto_redirect {b} NOT NULL",
            "provisioning_mode INTEGER NOT NULL",
            "logout_mode INTEGER NOT NULL",
            "allowed_email_domains_json TEXT NOT NULL",
            "jit_default_role_n_ids_json TEXT NOT NULL",
            "CONSTRAINT uq_sso_provider_nid UNIQUE (tenant_n_id, normalized_n_id)",
            "CONSTRAINT uq_sso_provider_id_isdel UNIQUE (id, is_deleted)",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS identity_sso_provider (
            {columns}
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_sso_provider_active_client ON identity_sso_provider (tenant_n_id, client_id_or_entity_id) WHERE is_deleted = {f};
            """;
    }

    private static string SsoExternalAccountDdl(DbType dbType)
    {
        var (g, t, b, big, f) = TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            CommonColumns(g, t, b, big),
            "n_id TEXT NOT NULL",
            "normalized_n_id TEXT NOT NULL",
            $"sso_provider_id {g} NOT NULL",
            $"sso_provider_is_deleted {b} NOT NULL",
            "external_subject TEXT NOT NULL",
            $"user_id {g} NOT NULL",
            $"user_is_deleted {b} NOT NULL",
            "external_name TEXT NULL",
            "external_email TEXT NULL",
            $"last_login_on {t} NULL",
            "CONSTRAINT uq_sso_external_account_nid UNIQUE (n_id)",
            "CONSTRAINT uq_sso_external_account_id_isdel UNIQUE (id, is_deleted)",
            "FOREIGN KEY (sso_provider_id, sso_provider_is_deleted) REFERENCES identity_sso_provider (id, is_deleted) ON UPDATE CASCADE",
            "FOREIGN KEY (user_id, user_is_deleted) REFERENCES identity_user (id, is_deleted) ON UPDATE CASCADE",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS identity_sso_external_account (
            {columns}
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_sso_external_account_active ON identity_sso_external_account (sso_provider_id, external_subject) WHERE is_deleted = {f};
            """;
    }

    private static string SsoClientDdl(DbType dbType)
    {
        var (g, t, b, big, f) = TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            CommonColumns(g, t, b, big),
            "tenant_n_id TEXT NOT NULL",
            "n_id TEXT NOT NULL",
            "normalized_n_id TEXT NOT NULL",
            "name TEXT NOT NULL",
            "oauth_client_id TEXT NOT NULL",
            $"enabled {b} NOT NULL",
            "CONSTRAINT uq_sso_client_nid UNIQUE (tenant_n_id, normalized_n_id)",
            "CONSTRAINT uq_sso_client_id_isdel UNIQUE (id, is_deleted)",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS identity_sso_client (
            {columns}
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_sso_client_active_oauth ON identity_sso_client (tenant_n_id, oauth_client_id) WHERE is_deleted = {f};
            """;
    }

    private static string SsoClientEndpointDdl(DbType dbType)
    {
        var (g, t, b, big, f) = TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            CommonColumns(g, t, b, big),
            $"sso_client_id {g} NOT NULL",
            $"sso_client_is_deleted {b} NOT NULL",
            "n_id TEXT NOT NULL",
            "endpoint_type INTEGER NOT NULL",
            "uri TEXT NOT NULL",
            "normalized_uri TEXT NOT NULL",
            $"enabled {b} NOT NULL",
            "FOREIGN KEY (sso_client_id, sso_client_is_deleted) REFERENCES identity_sso_client (id, is_deleted) ON UPDATE CASCADE",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS identity_sso_client_endpoint (
            {columns}
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_sso_client_endpoint_active_nid ON identity_sso_client_endpoint (sso_client_id, n_id) WHERE is_deleted = {f};
            CREATE UNIQUE INDEX IF NOT EXISTS ux_sso_client_endpoint_active_uri ON identity_sso_client_endpoint (sso_client_id, endpoint_type, normalized_uri) WHERE is_deleted = {f};
            """;
    }

    private static string SsoBrowserSessionDdl(DbType dbType)
    {
        var (g, t, b, big, f) = TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            CommonColumns(g, t, b, big),
            "tenant_n_id TEXT NOT NULL",
            "n_id TEXT NOT NULL",
            "normalized_n_id TEXT NOT NULL",
            "provider_n_id TEXT NOT NULL",
            $"user_id {g} NOT NULL",
            $"user_is_deleted {b} NOT NULL",
            "session_handle_hash TEXT NOT NULL",
            "auth_version INTEGER NOT NULL",
            $"last_activity_on {t} NOT NULL",
            $"expires_on {t} NOT NULL",
            $"revoked_on {t} NULL",
            "revoke_reason INTEGER NULL",
            "CONSTRAINT uq_sso_browser_session_nid UNIQUE (n_id)",
            "CONSTRAINT uq_sso_browser_session_id_isdel UNIQUE (id, is_deleted)",
            "FOREIGN KEY (user_id, user_is_deleted) REFERENCES identity_user (id, is_deleted) ON UPDATE CASCADE",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS identity_sso_browser_session (
            {columns}
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_sso_browser_session_active_handle ON identity_sso_browser_session (session_handle_hash) WHERE is_deleted = {f};
            CREATE INDEX IF NOT EXISTS ix_sso_browser_session_tenant ON identity_sso_browser_session (tenant_n_id, user_id);
            """;
    }

    private static string UserGroupDdl(DbType dbType)
    {
        var (g, t, b, big, _) = TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            CommonColumns(g, t, b, big),
            "tenant_n_id TEXT NOT NULL",
            "n_id TEXT NOT NULL",
            "normalized_n_id TEXT NOT NULL",
            "name TEXT NOT NULL",
            "description TEXT NULL",
            "status INTEGER NOT NULL",
            "CONSTRAINT uq_user_group_nid UNIQUE (tenant_n_id, normalized_n_id)",
            "CONSTRAINT uq_user_group_id_isdel UNIQUE (id, is_deleted)",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS identity_user_group (
            {columns}
            );
            """;
    }

    private static string UserGroupMembershipDdl(DbType dbType)
    {
        var (g, t, b, big, f) = TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            CommonColumns(g, t, b, big),
            "tenant_n_id TEXT NOT NULL",
            $"user_group_id {g} NOT NULL",
            $"user_group_is_deleted {b} NOT NULL",
            $"user_id {g} NOT NULL",
            $"user_is_deleted {b} NOT NULL",
            "FOREIGN KEY (user_group_id, user_group_is_deleted) REFERENCES identity_user_group (id, is_deleted) ON UPDATE CASCADE",
            "FOREIGN KEY (user_id, user_is_deleted) REFERENCES identity_user (id, is_deleted) ON UPDATE CASCADE",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS identity_user_group_membership (
            {columns}
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_user_group_membership_active ON identity_user_group_membership (tenant_n_id, user_id, user_group_id) WHERE is_deleted = {f};
            CREATE INDEX IF NOT EXISTS ix_user_group_membership_group ON identity_user_group_membership (user_group_id);
            """;
    }

    private static string UserGroupRoleDdl(DbType dbType)
    {
        var (g, t, b, big, f) = TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            CommonColumns(g, t, b, big),
            "tenant_n_id TEXT NOT NULL",
            $"user_group_id {g} NOT NULL",
            $"user_group_is_deleted {b} NOT NULL",
            $"role_id {g} NOT NULL",
            $"role_is_deleted {b} NOT NULL",
            "FOREIGN KEY (user_group_id, user_group_is_deleted) REFERENCES identity_user_group (id, is_deleted) ON UPDATE CASCADE",
            "FOREIGN KEY (role_id, role_is_deleted) REFERENCES identity_role (id, is_deleted) ON UPDATE CASCADE",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS identity_user_group_role (
            {columns}
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_user_group_role_active ON identity_user_group_role (tenant_n_id, user_group_id, role_id) WHERE is_deleted = {f};
            CREATE INDEX IF NOT EXISTS ix_user_group_role_group ON identity_user_group_role (user_group_id);
            """;
    }

    private static string SeedLedgerDdl(DbType dbType)
    {
        var (g, t, b, big, _) = TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            CommonColumns(g, t, b, big),
            "tenant_n_id TEXT NOT NULL",
            "seed_n_id TEXT NOT NULL",
            "seed_version TEXT NOT NULL",
            "seed_class TEXT NOT NULL",
            "scope TEXT NOT NULL",
            "checksum TEXT NOT NULL",
            "status TEXT NOT NULL",
            $"applied_on {t} NULL",
            "system_data_operation_n_id TEXT NULL",
            "trace_id TEXT NULL",
            "CONSTRAINT uq_seed_ledger_version UNIQUE (tenant_n_id, seed_n_id, seed_version)",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS identity_seed_ledger (
            {columns}
            );
            CREATE INDEX IF NOT EXISTS ix_seed_ledger_tenant ON identity_seed_ledger (tenant_n_id, created_on);
            """;
    }

    private static string BootstrapCredentialDdl(DbType dbType)
    {
        var (g, t, b, big, _) = TypeWords(dbType);
        var columns = string.Join(",\n",
        [
            CommonColumns(g, t, b, big),
            "tenant_n_id TEXT NOT NULL",
            "user_n_id TEXT NOT NULL",
            "state INTEGER NOT NULL",
            "delivery_reference_hash TEXT NULL",
            "recovery_reference_hash TEXT NOT NULL",
            $"delivered_on {t} NULL",
            $"recovered_on {t} NULL",
        ]);
        return $"""
            CREATE TABLE IF NOT EXISTS identity_bootstrap_credential (
            {columns}
            );
            CREATE INDEX IF NOT EXISTS ix_bootstrap_credential_tenant ON identity_bootstrap_credential (tenant_n_id, user_n_id, created_on);
            """;
    }

    /// <summary>
    /// 为既有 identity_user 表补 must_change_password 列(§29A.4);新库由 UserDdl 直接包含。
    /// 列已存在时幂等跳过。
    /// </summary>
    private static async Task AddMustChangePasswordColumnAsync(ISqlSugarClient sugar, CancellationToken _)
    {
        var columns = sugar.DbMaintenance.GetColumnInfosByTableName("identity_user");
        if (columns.Any(c => string.Equals(c.DbColumnName, "must_change_password", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var dataType = sugar.CurrentConnectionConfig.DbType == DbType.Sqlite ? "INTEGER" : "BOOLEAN";
        var defaultValue = sugar.CurrentConnectionConfig.DbType == DbType.Sqlite ? "0" : "false";
        await sugar.Ado.ExecuteCommandAsync(
            $"ALTER TABLE identity_user ADD COLUMN must_change_password {dataType} NOT NULL DEFAULT {defaultValue}");
    }
}
