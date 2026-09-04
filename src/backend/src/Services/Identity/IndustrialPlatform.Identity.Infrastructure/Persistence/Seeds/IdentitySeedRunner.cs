using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Identity.Domain.Identities;
using IndustrialPlatform.Identity.Domain.Passwords;
using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Identity.Infrastructure.Persistence;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;
using IndustrialPlatform.Identity.Infrastructure.Security;
using IndustrialPlatform.Infrastructure.Database;
using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Seeds;

/// <summary>种子执行上下文(非敏感):租户 + SystemData Operation 关联。</summary>
public sealed record IdentitySeedContext(string TenantNId, string? SystemDataOperationNId, string? TraceId);

/// <summary>
/// bootstrap admin 一次性凭据交付结果(§29A.4):明文临时密码与一次性引用
/// 只存在于本次调用返回的受保护结果中,绝不入库、入日志、入审计或入事件。
/// </summary>
public sealed record BootstrapSeedResult(
    string TenantNId,
    string UserNId,
    string LoginName,
    string TemporaryPassword,
    string DeliveryReference,
    string RecoveryReference,
    Guid DeliveryId);

/// <summary>一次种子执行的汇总(种子账本快照 + 可选 bootstrap 凭据)。</summary>
public sealed record IdentitySeedRunResult(
    IReadOnlyList<SeedVersionStatus> SeedVersions,
    BootstrapSeedResult? BootstrapAdmin);

/// <summary>
/// Identity 三层幂等种子执行器(§29A.4):SystemCatalogSeed → TenantSecuritySeed → BootstrapAdminSeed。
/// 每层与 seed ledger 记账在同一事务内提交;同版本同 checksum 幂等跳过,同版本不同 checksum drift 拒绝。
/// 明文密码绝不进入数据库、Operation、日志、Trace、审计或事件。
/// </summary>
public sealed class IdentitySeedRunner
{
    private readonly SqlSugarDbContext _dbContext;
    private readonly IPasswordHasher _hasher;
    private readonly IBootstrapCredentialStore _credentials;

    public IdentitySeedRunner(SqlSugarDbContext dbContext, IPasswordHasher hasher, IBootstrapCredentialStore credentials)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(credentials);
        _dbContext = dbContext;
        _hasher = hasher;
        _credentials = credentials;
    }

    /// <summary>
    /// 按序执行三层种子。
    /// </summary>
    /// <param name="context">种子执行上下文。</param>
    /// <param name="includeBootstrapAdmin">是否执行 SecretBootstrap 层(内置 admin 引导)。</param>
    public async Task<IdentitySeedRunResult> RunAsync(
        IdentitySeedContext context,
        bool includeBootstrapAdmin,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(context.TenantNId))
        {
            throw new ArgumentException("种子执行需要租户标识。", nameof(context));
        }

        // 1. SystemCatalogSeed(权限目录 + 系统角色 + 系统角色权限,SystemBaseline)
        await ApplyCatalogSeedAsync(context, cancellationToken);

        // 2. TenantSecuritySeed(按租户确认系统角色,TenantBaseline)
        await ApplyTenantSecuritySeedAsync(context, cancellationToken);

        // 3. BootstrapAdminSeed(内置 admin 引导,SecretBootstrap;仅显式初始化时执行)
        BootstrapSeedResult? bootstrap = null;
        if (includeBootstrapAdmin)
        {
            bootstrap = await ApplyBootstrapAdminSeedAsync(context, cancellationToken);
        }

        var seeds = await ReadSeedLedgerAsync(context.TenantNId, cancellationToken);
        return new IdentitySeedRunResult(seeds, bootstrap);
    }

    // ---------- SystemCatalogSeed ----------

    private async Task ApplyCatalogSeedAsync(IdentitySeedContext context, CancellationToken cancellationToken)
    {
        var checksum = BootstrapSeedCatalog.Checksum(BootstrapSeedCatalog.SystemCatalogContent());

        await ApplySeedOnceAsync(
            context,
            BootstrapSeedCatalog.SystemCatalogSeedKey,
            BootstrapSeedCatalog.SeedVersion,
            BootstrapSeedCatalog.SystemScope,
            checksum,
            async (sugar, ct) =>
            {
                var now = DateTimeOffset.UtcNow;
                await EnsurePermissionsAsync(sugar, now, ct);
                var role = await EnsureSystemAdminRoleAsync(sugar, context.TenantNId, now, ct);
                await AssignAllPermissionsAsync(sugar, role.Id, now, ct);
            },
            cancellationToken);
    }

    // ---------- TenantSecuritySeed ----------

    private async Task ApplyTenantSecuritySeedAsync(IdentitySeedContext context, CancellationToken cancellationToken)
    {
        var checksum = BootstrapSeedCatalog.Checksum(BootstrapSeedCatalog.TenantSecurityContent(context.TenantNId));

        await ApplySeedOnceAsync(
            context,
            BootstrapSeedCatalog.TenantSecuritySeedKey,
            BootstrapSeedCatalog.SeedVersion,
            BootstrapSeedCatalog.TenantScope,
            checksum,
            async (sugar, ct) =>
            {
                var now = DateTimeOffset.UtcNow;
                var role = await EnsureSystemAdminRoleAsync(sugar, context.TenantNId, now, ct);
                await AssignAllPermissionsAsync(sugar, role.Id, now, ct);
            },
            cancellationToken);
    }

    // ---------- BootstrapAdminSeed ----------

    private async Task<BootstrapSeedResult?> ApplyBootstrapAdminSeedAsync(IdentitySeedContext context, CancellationToken cancellationToken)
    {
        var checksum = BootstrapSeedCatalog.Checksum(BootstrapSeedCatalog.BootstrapAdminContent());

        // 账本已应用且 checksum 一致 → 只核验不覆盖;临时密码永不重发。
        var applied = await FindLedgerAsync(
            context.TenantNId,
            BootstrapSeedCatalog.BootstrapAdminSeedKey,
            BootstrapSeedCatalog.SeedVersion,
            cancellationToken);
        if (applied is not null)
        {
            if (!string.Equals(applied.Checksum, checksum, StringComparison.OrdinalIgnoreCase))
            {
                throw new SeedDriftException(BootstrapSeedCatalog.BootstrapAdminSeedKey, BootstrapSeedCatalog.SeedVersion);
            }

            if (string.Equals(applied.Status, "Applied", StringComparison.OrdinalIgnoreCase))
            {
                await EnsureAdminHealthyOrThrowAsync(context.TenantNId, cancellationToken);
                return null;
            }
        }

        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            var adminRow = await FindAdminIncludingDeletedAsync(sugar, context.TenantNId, cancellationToken);
            BootstrapSeedResult? result = null;

            if (adminRow is null)
            {
                // 首次引导:创建 admin,只保存 BCrypt 哈希;临时密码经受保护结果交付一次。
                result = await CreateBootstrapAdminAsync(sugar, context.TenantNId, cancellationToken);
            }
            else
            {
                // 已存在 admin(历史来源):绝不覆盖密码/资料/状态/授权;异常状态只可诊断,不自动修复。
                EnsureAdminHealthyOrThrow(adminRow);
            }

            await InsertLedgerAsync(
                sugar,
                context,
                BootstrapSeedCatalog.BootstrapAdminSeedKey,
                BootstrapSeedCatalog.SeedVersion,
                "SecretBootstrap",
                BootstrapSeedCatalog.TenantScope,
                checksum,
                cancellationToken);
            sugar.Ado.CommitTran();

            if (result is not null)
            {
                // 受保护响应产生即视为一次性领取(§29A.4:只能领取一次)。
                await _credentials.ClaimAsync(result.DeliveryId, cancellationToken);
            }

            return result;
        }
        catch (Exception ex) when (IsUniqueConstraintViolation(ex))
        {
            // 并发执行:另一执行器已创建 admin/已记账 → 只核验不覆盖,不重发凭据。
            sugar.Ado.RollbackTran();
            var afterLedger = await FindLedgerAsync(
                context.TenantNId,
                BootstrapSeedCatalog.BootstrapAdminSeedKey,
                BootstrapSeedCatalog.SeedVersion,
                cancellationToken);
            if (afterLedger is not null
                && string.Equals(afterLedger.Checksum, checksum, StringComparison.OrdinalIgnoreCase)
                && string.Equals(afterLedger.Status, "Applied", StringComparison.OrdinalIgnoreCase))
            {
                await EnsureAdminHealthyOrThrowAsync(context.TenantNId, cancellationToken);
                return null;
            }

            throw;
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    private async Task<BootstrapSeedResult> CreateBootstrapAdminAsync(ISqlSugarClient sugar, string tenantNId, CancellationToken cancellationToken)
    {
        var password = SecureRandomPasswordGenerator.Generate(
            BootstrapSeedCatalog.BootstrapPasswordMinLength,
            BootstrapSeedCatalog.BootstrapLoginName,
            BootstrapSeedCatalog.BootstrapUserNId);
        var passwordHash = _hasher.Hash(password);

        var now = DateTimeOffset.UtcNow;
        var user = User.Create(
            tenantNId,
            BootstrapSeedCatalog.BootstrapUserNId,
            BootstrapSeedCatalog.BootstrapLoginName,
            BootstrapSeedCatalog.SystemAdminRole.Name,
            email: null,
            phone: null,
            passwordHash,
            mustChangePassword: false,
            id: BootstrapSeedCatalog.BootstrapAdminStableId);
        await sugar.Insertable(TableMapper.ToTable(user)).ExecuteCommandAsync(cancellationToken);

        var role = await EnsureSystemAdminRoleAsync(sugar, tenantNId, now, cancellationToken);
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
            RoleId = role.Id,
            RoleIsDeleted = false,
        }).ExecuteCommandAsync(cancellationToken);

        // 交付记录:只保存一次性引用哈希,明文密码/引用绝不入库。
        var deliveryReference = BootstrapHashing.NewReference();
        var recoveryReference = BootstrapHashing.NewReference();
        var delivery = new BootstrapCredentialTable
        {
            Id = Guid.NewGuid(),
            IsFrozen = false,
            IsLocked = false,
            IsDeleted = false,
            EntityType = typeof(BootstrapCredentialTable).FullName ?? typeof(BootstrapCredentialTable).Name,
            CreatedOn = now,
            LastUpdatedOn = now,
            OptimisticVersion = 0,
            ConcurrencyVersion = Guid.NewGuid(),
            TenantNId = tenantNId,
            UserNId = BootstrapSeedCatalog.BootstrapUserNId,
            State = (int)BootstrapDeliveryState.Pending,
            DeliveryReferenceHash = BootstrapHashing.HashReference(deliveryReference),
            RecoveryReferenceHash = BootstrapHashing.HashReference(recoveryReference),
            DeliveredOn = null,
            RecoveredOn = null,
        };
        await sugar.Insertable(delivery).ExecuteCommandAsync(cancellationToken);

        return new BootstrapSeedResult(
            tenantNId,
            BootstrapSeedCatalog.BootstrapUserNId,
            BootstrapSeedCatalog.BootstrapLoginName,
            password,
            deliveryReference,
            recoveryReference,
            delivery.Id);
    }

    // ---------- 共享账本/内容助手 ----------

    private async Task ApplySeedOnceAsync(
        IdentitySeedContext context,
        string seedKey,
        string seedVersion,
        string scope,
        string checksum,
        Func<ISqlSugarClient, CancellationToken, Task> apply,
        CancellationToken cancellationToken)
    {
        var existing = await FindLedgerAsync(context.TenantNId, seedKey, seedVersion, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.Checksum, checksum, StringComparison.OrdinalIgnoreCase))
            {
                throw new SeedDriftException(seedKey, seedVersion);
            }

            if (string.Equals(existing.Status, "Applied", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            await apply(sugar, cancellationToken);
            await InsertLedgerAsync(sugar, context, seedKey, seedVersion, scope, checksum, cancellationToken);
            sugar.Ado.CommitTran();
        }
        catch (Exception ex) when (IsUniqueConstraintViolation(ex))
        {
            // 并发执行:另一执行器已先记账 → 按账本幂等收敛,不重复应用。
            sugar.Ado.RollbackTran();
            var after = await FindLedgerAsync(context.TenantNId, seedKey, seedVersion, cancellationToken);
            if (after is not null
                && string.Equals(after.Checksum, checksum, StringComparison.OrdinalIgnoreCase)
                && string.Equals(after.Status, "Applied", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            throw;
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    private static async Task InsertLedgerAsync(
        ISqlSugarClient sugar,
        IdentitySeedContext context,
        string seedKey,
        string seedVersion,
        string scope,
        string checksum,
        CancellationToken cancellationToken)
        => await InsertLedgerAsync(sugar, context, seedKey, seedVersion, SeedClassFor(seedKey), scope, checksum, cancellationToken);

    private static string SeedClassFor(string seedKey) => seedKey switch
    {
        BootstrapSeedCatalog.SystemCatalogSeedKey => "SystemBaseline",
        BootstrapSeedCatalog.TenantSecuritySeedKey => "TenantBaseline",
        BootstrapSeedCatalog.BootstrapAdminSeedKey => "SecretBootstrap",
        _ => "SystemBaseline",
    };

    private static async Task InsertLedgerAsync(
        ISqlSugarClient sugar,
        IdentitySeedContext context,
        string seedKey,
        string seedVersion,
        string seedClass,
        string scope,
        string checksum,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await sugar.Insertable(new SeedLedgerTable
        {
            Id = Guid.NewGuid(),
            IsFrozen = false,
            IsLocked = false,
            IsDeleted = false,
            EntityType = typeof(SeedLedgerTable).FullName ?? typeof(SeedLedgerTable).Name,
            CreatedOn = now,
            LastUpdatedOn = now,
            OptimisticVersion = 0,
            ConcurrencyVersion = Guid.NewGuid(),
            TenantNId = context.TenantNId,
            SeedNId = seedKey,
            SeedVersion = seedVersion,
            SeedClass = seedClass,
            Scope = scope,
            Checksum = checksum,
            Status = "Applied",
            AppliedOn = now,
            SystemDataOperationNId = context.SystemDataOperationNId,
            TraceId = context.TraceId,
        }).ExecuteCommandAsync(cancellationToken);
    }

    private async Task<SeedLedgerTable?> FindLedgerAsync(
        string tenantNId,
        string seedKey,
        string seedVersion,
        CancellationToken cancellationToken)
    {
        return (SeedLedgerTable?)await _dbContext.SqlSugar.Queryable<SeedLedgerTable>()
            .Where(t => t.TenantNId == tenantNId
                && t.SeedNId == seedKey
                && t.SeedVersion == seedVersion
                && !t.IsDeleted)
            .OrderByDescending(t => t.CreatedOn)
            .FirstAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<SeedVersionStatus>> ReadSeedLedgerAsync(string tenantNId, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SqlSugar.Queryable<SeedLedgerTable>()
            .Where(t => t.TenantNId == tenantNId && !t.IsDeleted)
            .OrderByDescending(t => t.CreatedOn)
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(r => r.SeedNId, StringComparer.Ordinal)
            .Select(g => g.First())
            .Select(r => new SeedVersionStatus(r.SeedNId, r.SeedVersion, r.Status, r.Checksum, r.Scope, r.AppliedOn))
            .OrderBy(s => s.SeedKey, StringComparer.Ordinal)
            .ToList();
    }

    private static async Task EnsurePermissionsAsync(ISqlSugarClient sugar, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var entry in BootstrapSeedCatalog.Permissions)
        {
            var nId = NId.Create(entry.NId);
            if (await sugar.Queryable<PermissionTable>().AnyAsync(p => p.NormalizedNId == nId.Normalized, cancellationToken))
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
                NId = nId.Value,
                NormalizedNId = nId.Normalized,
                Name = entry.Name,
                Type = entry.Type,
                ParentPermissionNId = null,
                ParentPermissionId = null,
                ParentPermissionIsDeleted = null,
                Description = null,
                Status = PermissionStatus.Active,
            }).ExecuteCommandAsync(cancellationToken);
        }
    }

    private static async Task<RoleTable> EnsureSystemAdminRoleAsync(ISqlSugarClient sugar, string tenantNId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await sugar.Queryable<RoleTable>()
            .Where(r => r.TenantNId == tenantNId && r.NId == BootstrapSeedCatalog.SystemAdminRoleNId && !r.IsDeleted)
            .FirstAsync(cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var nId = NId.Create(BootstrapSeedCatalog.SystemAdminRoleNId);
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
            NId = nId.Value,
            NormalizedNId = nId.Normalized,
            Name = BootstrapSeedCatalog.SystemAdminRole.Name,
            Description = BootstrapSeedCatalog.SystemAdminRole.Description,
            IsSystem = true,
        };
        await sugar.Insertable(role).ExecuteCommandAsync(cancellationToken);
        return role;
    }

    private static async Task AssignAllPermissionsAsync(ISqlSugarClient sugar, Guid roleId, DateTimeOffset now, CancellationToken cancellationToken)
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

    private async Task EnsureAdminHealthyOrThrowAsync(string tenantNId, CancellationToken cancellationToken)
    {
        var adminRow = await FindAdminIncludingDeletedAsync(_dbContext.SqlSugar, tenantNId, cancellationToken);
        if (adminRow is null)
        {
            throw new BootstrapRecoveryRequiredException();
        }

        EnsureAdminHealthyOrThrow(adminRow);
    }

    private static void EnsureAdminHealthyOrThrow(UserTable adminRow)
    {
        // §29A.4:admin 已存在但被禁用/删除/失去系统角色/凭据遗失时,种子失败且不自动修复。
        if (adminRow.IsDeleted || adminRow.Status != UserStatus.Active)
        {
            throw new BootstrapRecoveryRequiredException();
        }
    }

    private static async Task<UserTable?> FindAdminIncludingDeletedAsync(ISqlSugarClient sugar, string tenantNId, CancellationToken cancellationToken)
    {
        var normalized = NId.Create(BootstrapSeedCatalog.BootstrapUserNId).Normalized;
        return (UserTable?)await sugar.Queryable<UserTable>()
            .Where(t => t.TenantNId == tenantNId && t.NormalizedNId == normalized)
            .FirstAsync(cancellationToken);
    }

    /// <summary>判断异常链是否源于唯一约束/主键冲突(SQLite UNIQUE/PRIMARY、PostgreSQL 23505)。</summary>
    private static bool IsUniqueConstraintViolation(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("PRIMARY KEY constraint failed", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("23505", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
