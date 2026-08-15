using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Seeds;

namespace IndustrialPlatform.Identity.Infrastructure.Bootstrap;

/// <summary>
/// Identity 初始化编排服务(§29A.4 首次部署固定流程的 Identity 侧执行器):
/// SchemaMigration → SystemSeed(SystemCatalog + TenantSecurity)→ BootstrapAdmin → Verify。
/// SystemData 编排调用方(外部 Runner,待验收)或受控部署身份通过本服务驱动初始化,
/// admin 临时密码只在成功结果的受保护响应中返回一次。
/// </summary>
public sealed class IdentityInitializationService
{
    private readonly ISchemaMigrationRunner _migrationRunner;
    private readonly IdentitySeedRunner _seedRunner;
    private readonly IBootstrapStore _store;

    public IdentityInitializationService(
        ISchemaMigrationRunner migrationRunner,
        IdentitySeedRunner seedRunner,
        IBootstrapStore store)
    {
        ArgumentNullException.ThrowIfNull(migrationRunner);
        ArgumentNullException.ThrowIfNull(seedRunner);
        ArgumentNullException.ThrowIfNull(store);
        _migrationRunner = migrationRunner;
        _seedRunner = seedRunner;
        _store = store;
    }

    /// <summary>
    /// 执行完整初始化:迁移 + 三层种子(含 SecretBootstrap 层)。
    /// 首次引导成功时返回 <see cref="IdentityInitializationResult.BootstrapAdmin"/>
    /// (含明文临时密码与一次性引用,只出现一次);重复执行不覆盖既有 admin 且不重发密码。
    /// </summary>
    public async Task<IdentityInitializationResult> InitializeAsync(
        IdentitySeedContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        await _migrationRunner.ApplyPendingAsync(cancellationToken);
        var run = await _seedRunner.RunAsync(context, includeBootstrapAdmin: true, cancellationToken);

        var schemaVersion = await _store.GetSchemaVersionAsync(cancellationToken);
        var admin = await _store.GetAdminIncludingDeletedAsync(context.TenantNId, BootstrapSeedCatalog.BootstrapUserNId, cancellationToken);
        var state = admin.Exists && !admin.IsDeleted && admin.IsActive && admin.HasSystemAdminRole
            ? BootstrapState.Ready
            : BootstrapState.Pending;

        return new IdentityInitializationResult(
            context.TenantNId,
            schemaVersion,
            run.SeedVersions,
            state,
            run.BootstrapAdmin);
    }
}

/// <summary>Identity 初始化结果(非敏感;含可选一次性凭据)。</summary>
public sealed record IdentityInitializationResult(
    string TenantNId,
    string SchemaVersion,
    IReadOnlyList<SeedVersionStatus> SeedVersions,
    BootstrapState BootstrapStatus,
    BootstrapSeedResult? BootstrapAdmin);
