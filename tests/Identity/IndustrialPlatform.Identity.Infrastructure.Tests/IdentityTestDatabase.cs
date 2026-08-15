using IndustrialPlatform.Identity.Infrastructure.Bootstrap;
using IndustrialPlatform.Identity.Infrastructure.Passwords;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Seeds;
using IndustrialPlatform.Infrastructure.Database;
using Microsoft.Extensions.Logging.Abstractions;

namespace IndustrialPlatform.Identity.Infrastructure.Tests;

/// <summary>
/// 测试库初始化助手(TASK-ID-019):迁移 + 不可变系统目录种子(SystemCatalog/TenantSecurity),
/// 不含 SecretBootstrap(内置 admin 由 IdentitySeedRunnerTests 显式验证)。
/// </summary>
internal static class IdentityTestDatabase
{
    public const string TestTenantNId = "development";

    /// <summary>应用全部 DDL 迁移与两个不可变目录种子(权限目录 + SYSTEM_ADMIN)。</summary>
    public static async Task ApplyCatalogAsync(SqlSugarDbContext dbContext, CancellationToken cancellationToken = default)
    {
        await new SchemaMigrationRunner(dbContext, IdentitySchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance)
            .ApplyPendingAsync(cancellationToken);

        var seedRunner = CreateSeedRunner(dbContext);
        await seedRunner.RunAsync(
            new IdentitySeedContext(TestTenantNId, SystemDataOperationNId: null, TraceId: null),
            includeBootstrapAdmin: false,
            cancellationToken);
    }

    /// <summary>应用全部 DDL 迁移与三层种子(含内置 admin 引导)。</summary>
    public static async Task<IdentitySeedRunResult> ApplyFullAsync(SqlSugarDbContext dbContext, CancellationToken cancellationToken = default)
    {
        await new SchemaMigrationRunner(dbContext, IdentitySchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance)
            .ApplyPendingAsync(cancellationToken);

        var seedRunner = CreateSeedRunner(dbContext);
        return await seedRunner.RunAsync(
            new IdentitySeedContext(TestTenantNId, SystemDataOperationNId: null, TraceId: null),
            includeBootstrapAdmin: true,
            cancellationToken);
    }

    public static IdentitySeedRunner CreateSeedRunner(SqlSugarDbContext dbContext) =>
        new(dbContext, new BcryptPasswordHasher(), new BootstrapCredentialStore(dbContext));
}
