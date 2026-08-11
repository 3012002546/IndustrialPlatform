using IndustrialPlatform.Identity.Domain.Identities;
using IndustrialPlatform.Identity.Domain.Passwords;
using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Identity.Infrastructure.Passwords;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;
using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;

/// <summary>
/// 身份库全部迁移步骤(§11 建表 + §21.2 种子),由 SchemaMigrationRunner 按标识排序执行。
/// DDL 类型词按目标数据库分支(SQLite 用 TEXT/INTEGER,PostgreSQL 用 uuid/timestamptz/BOOLEAN);
/// 种子全部幂等(check-then-insert)。密码只保存哈希,初始化不输出密码、无固定默认账号。
/// </summary>
public static class IdentitySchemaMigrations
{
    /// <summary>全部迁移步骤,运行器按 Id 排序并幂等记账。</summary>
    public static IReadOnlyList<SchemaMigrationStep> All { get; } = BuildSteps();

    private static readonly (string NId, string Name, PermissionType Type)[] FirstBatchPermissions =
    [
        (PermissionCatalog.UserView, "查看用户", PermissionType.Page),
        (PermissionCatalog.UserCreate, "创建用户", PermissionType.Action),
        (PermissionCatalog.UserUpdate, "更新用户", PermissionType.Action),
        (PermissionCatalog.UserStatus, "用户状态管理", PermissionType.Action),
        (PermissionCatalog.UserAssignRole, "分配角色", PermissionType.Action),
        (PermissionCatalog.RoleView, "查看角色", PermissionType.Page),
        (PermissionCatalog.RoleCreate, "创建角色", PermissionType.Action),
        (PermissionCatalog.RoleUpdate, "更新角色", PermissionType.Action),
        (PermissionCatalog.RoleAssignPermission, "分配权限", PermissionType.Action),
        (PermissionCatalog.PermissionView, "查看权限", PermissionType.Page),
        (PermissionCatalog.AuditLoginView, "查看登录审计", PermissionType.Page),
        (PermissionCatalog.SsoView, "查看 SSO", PermissionType.Page),
        (PermissionCatalog.SsoManage, "管理 SSO", PermissionType.Action),
        (PermissionCatalog.SsoTest, "测试 SSO", PermissionType.Action),
        (PermissionCatalog.PlatformHomeView, "平台首页", PermissionType.Page),
        (PermissionCatalog.PlatformPdaView, "PDA 端页面", PermissionType.Page),
        (PermissionCatalog.PlatformMobileView, "移动端页面", PermissionType.Page),
    ];

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
            new SchemaMigrationStep(
                "ID-004-10",
                "seed permission catalog, SYSTEM_ADMIN role and its permissions",
                SeedSystemAdminAsync),
            new SchemaMigrationStep(
                "ID-004-11",
                "bootstrap explicit initial admin from environment variables (idempotent)",
                BootstrapAdminAsync),
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

    private static async Task SeedSystemAdminAsync(ISqlSugarClient sugar, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (nId, name, type) in FirstBatchPermissions)
        {
            var nIdValue = NId.Create(nId);
            if (await sugar.Queryable<PermissionTable>().AnyAsync(p => p.NormalizedNId == nIdValue.Normalized, cancellationToken))
            {
                continue;
            }

            await sugar.Insertable(new PermissionTable
            {
                Id = Guid.NewGuid(),
                IsFrozen = false,
                IsLocked = false,
                IsDeleted = false,
                EntityType = typeof(Permission).FullName ?? typeof(Permission).Name,
                CreatedOn = now,
                LastUpdatedOn = now,
                OptimisticVersion = 0,
                ConcurrencyVersion = Guid.NewGuid(),
                NId = nIdValue.Value,
                NormalizedNId = nIdValue.Normalized,
                Name = name,
                Type = type,
                ParentPermissionNId = null,
                ParentPermissionId = null,
                ParentPermissionIsDeleted = null,
                Description = null,
                Status = PermissionStatus.Active,
            }).ExecuteCommandAsync(cancellationToken);
        }

        // 与 02B mock 一致的开发租户 SYSTEM_ADMIN 系统角色
        var roleId = await EnsureSystemAdminRoleAsync(sugar, "development", now, cancellationToken);
        await AssignAllPermissionsAsync(sugar, roleId, now, cancellationToken);
    }

    /// <summary>确保租户存在 SYSTEM_ADMIN 系统角色,不存在则创建;返回其主键。</summary>
    private static async Task<Guid> EnsureSystemAdminRoleAsync(
        ISqlSugarClient sugar,
        string tenantNId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existingId = await sugar.Queryable<RoleTable>()
            .Where(r => r.TenantNId == tenantNId && r.NId == "SYSTEM_ADMIN" && !r.IsDeleted)
            .Select(r => r.Id)
            .FirstAsync(cancellationToken);
        if (existingId != Guid.Empty)
        {
            return existingId;
        }

        var nIdValue = NId.Create("SYSTEM_ADMIN");
        var role = new RoleTable
        {
            Id = Guid.NewGuid(),
            IsFrozen = false,
            IsLocked = false,
            IsDeleted = false,
            EntityType = typeof(Role).FullName ?? typeof(Role).Name,
            CreatedOn = now,
            LastUpdatedOn = now,
            OptimisticVersion = 0,
            ConcurrencyVersion = Guid.NewGuid(),
            TenantNId = tenantNId,
            NId = nIdValue.Value,
            NormalizedNId = nIdValue.Normalized,
            Name = "系统管理员",
            Description = "内置系统管理员角色,拥有全部第一批权限。",
            IsSystem = true,
        };
        await sugar.Insertable(role).ExecuteCommandAsync(cancellationToken);
        return role.Id;
    }

    /// <summary>为角色补齐全部活动权限的关联,幂等。</summary>
    private static async Task AssignAllPermissionsAsync(
        ISqlSugarClient sugar,
        Guid roleId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var permissions = await sugar.Queryable<PermissionTable>()
            .Where(p => !p.IsDeleted)
            .ToListAsync(cancellationToken);
        foreach (var permission in permissions)
        {
            if (await sugar.Queryable<RolePermissionTable>()
                .AnyAsync(rp => rp.RoleId == roleId && rp.PermissionId == permission.Id && !rp.IsDeleted, cancellationToken))
            {
                continue;
            }

            await sugar.Insertable(new RolePermissionTable
            {
                Id = Guid.NewGuid(),
                IsFrozen = false,
                IsLocked = false,
                IsDeleted = false,
                EntityType = typeof(RolePermission).FullName ?? typeof(RolePermission).Name,
                CreatedOn = now,
                LastUpdatedOn = now,
                OptimisticVersion = 0,
                ConcurrencyVersion = Guid.NewGuid(),
                RoleId = roleId,
                RoleIsDeleted = false,
                PermissionId = permission.Id,
                PermissionIsDeleted = permission.IsDeleted,
            }).ExecuteCommandAsync(cancellationToken);
        }
    }

    private static async Task BootstrapAdminAsync(ISqlSugarClient sugar, CancellationToken cancellationToken)
    {
        var tenantNId = Environment.GetEnvironmentVariable("IDENTITY_BOOTSTRAP_TENANT_NID");
        var userNId = Environment.GetEnvironmentVariable("IDENTITY_BOOTSTRAP_USER_NID");
        var loginName = Environment.GetEnvironmentVariable("IDENTITY_BOOTSTRAP_LOGIN_NAME");
        var password = Environment.GetEnvironmentVariable("IDENTITY_BOOTSTRAP_PASSWORD");

        // 未显式提供全部初始化配置时不创建固定默认账号
        if (string.IsNullOrWhiteSpace(tenantNId)
            || string.IsNullOrWhiteSpace(userNId)
            || string.IsNullOrWhiteSpace(loginName)
            || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        // 库中已有用户 → 幂等跳过
        if (await sugar.Queryable<UserTable>().AnyAsync(cancellationToken))
        {
            return;
        }

        PasswordPolicy.Validate(password, loginName, userNId);

        var now = DateTimeOffset.UtcNow;
        var hasher = new BcryptPasswordHasher();
        var user = User.Create(tenantNId, userNId, loginName, loginName, null, null, hasher.Hash(password));
        var roleId = await EnsureSystemAdminRoleAsync(sugar, tenantNId, now, cancellationToken);
        await AssignAllPermissionsAsync(sugar, roleId, now, cancellationToken);

        await sugar.Insertable(TableMapper.ToTable(user)).ExecuteCommandAsync(cancellationToken);
        await sugar.Insertable(new UserRoleTable
        {
            Id = Guid.NewGuid(),
            IsFrozen = false,
            IsLocked = false,
            IsDeleted = false,
            EntityType = typeof(UserRole).FullName ?? typeof(UserRole).Name,
            CreatedOn = now,
            LastUpdatedOn = now,
            OptimisticVersion = 0,
            ConcurrencyVersion = Guid.NewGuid(),
            TenantNId = tenantNId,
            UserId = user.Id,
            UserIsDeleted = false,
            RoleId = roleId,
            RoleIsDeleted = false,
        }).ExecuteCommandAsync(cancellationToken);
    }
}
