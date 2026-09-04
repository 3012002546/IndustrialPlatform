using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Seeds;
using IndustrialPlatform.SharedKernel.Topology;

namespace IndustrialPlatform.Identity.Infrastructure.Bootstrap;

/// <summary>
/// Identity 自有初始化器。它只调用 Identity 的 Migration、Seed、Bootstrap 和本地状态读取，
/// 不依赖 SystemData 在线，也不把一次性凭据返回给控制面。
/// </summary>
public sealed class IdentityServiceInitializer : IServiceInitializer
{
    private readonly IdentityInitializationService _initializationService;
    private readonly IBootstrapService _bootstrapService;

    public IdentityServiceInitializer(
        IdentityInitializationService initializationService,
        IBootstrapService bootstrapService)
    {
        _initializationService = initializationService;
        _bootstrapService = bootstrapService;
    }

    public string ServiceKey => "identity";
    public string ModuleKey => "identity";

    public async Task<ServiceInitializationState> InspectAsync(
        ServiceInitializationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var readiness = await _bootstrapService.GetReadinessAsync(context.TenantNId, cancellationToken);
            var bootstrapReady = context.Policy != ServiceInitializationPolicy.Advanced || readiness.BootstrapReady;
            var requiredSeedReady = HasRequiredSeeds(readiness.Seeds, context.Policy);
            var ready = readiness.MigrationReady && requiredSeedReady && bootstrapReady;
            return new ServiceInitializationState(
                ServiceKey,
                ModuleKey,
                readiness.SchemaVersion,
                readiness.MigrationReady,
                requiredSeedReady,
                bootstrapReady,
                ready,
                ready ? null : readiness.Reason,
                readiness.Seeds
                    .Select(seed => new ServiceInitializationSeedState(
                        seed.SeedKey,
                        seed.SeedVersion,
                        seed.Status,
                        seed.AppliedOn,
                        seed.Checksum,
                        NormalizeScope(seed.Scope)))
                    .ToList());
        }
        catch (Exception exception) when (IsMissingLocalTable(exception))
        {
            return new ServiceInitializationState(
                ServiceKey,
                ModuleKey,
                null,
                false,
                false,
                false,
                false,
                "Identity 本地初始化账本尚未创建。");
        }
    }

    public Task<ServiceInitializationPlan> PlanAsync(
        ServiceInitializationContext context,
        ServiceInitializationState inspection,
        CancellationToken cancellationToken)
    {
        var steps = new List<string>();
        var desiredVersion = string.IsNullOrWhiteSpace(context.DesiredVersion)
            ? IdentitySchemaMigrations.All[^1].Id
            : context.DesiredVersion;
        if (!inspection.MigrationReady)
        {
            steps.Add("migration");
        }

        if (!inspection.RequiredSeedReady)
        {
            steps.Add("required-seed");
        }

        if (!inspection.BootstrapReady)
        {
            steps.Add("bootstrap");
        }

        return Task.FromResult(new ServiceInitializationPlan(
            ServiceKey,
            ModuleKey,
            inspection.ObservedVersion,
            desiredVersion,
            !inspection.Ready || !string.Equals(inspection.ObservedVersion, desiredVersion, StringComparison.Ordinal),
            steps));
    }

    public async Task<ServiceInitializationState> ApplyAsync(
        ServiceInitializationContext context,
        ServiceInitializationPlan plan,
        CancellationToken cancellationToken)
    {
        var result = await _initializationService.InitializeAsync(
            new IdentitySeedContext(context.TenantNId, context.OperationNId, context.TraceId),
            includeBootstrapAdmin: context.Policy == ServiceInitializationPolicy.Advanced,
            cancellationToken);

        var migrationReady = !string.IsNullOrWhiteSpace(result.SchemaVersion);
        var requiredSeedReady = IsAppliedCurrentSeed(result.SeedVersions, BootstrapSeedCatalog.SystemCatalogSeedKey)
            && IsAppliedCurrentSeed(result.SeedVersions, BootstrapSeedCatalog.TenantSecuritySeedKey)
            && (context.Policy != ServiceInitializationPolicy.Advanced
                || IsAppliedCurrentSeed(result.SeedVersions, BootstrapSeedCatalog.BootstrapAdminSeedKey));
        var bootstrapReady = context.Policy != ServiceInitializationPolicy.Advanced
            || result.BootstrapStatus == BootstrapState.Ready;
        return new ServiceInitializationState(
            ServiceKey,
            ModuleKey,
            result.SchemaVersion,
            migrationReady,
            requiredSeedReady,
            bootstrapReady,
            migrationReady && requiredSeedReady && bootstrapReady,
            migrationReady && requiredSeedReady && bootstrapReady ? null : "Identity 本地初始化事实未达到期望状态。",
            result.SeedVersions
                .Select(seed => new ServiceInitializationSeedState(
                    seed.SeedKey,
                    seed.SeedVersion,
                    seed.Status,
                    seed.AppliedOn,
                    seed.Checksum,
                    NormalizeScope(seed.Scope)))
                .ToList());
    }

    public Task<ServiceInitializationState> VerifyAsync(
        ServiceInitializationContext context,
        CancellationToken cancellationToken) =>
        InspectAsync(context, cancellationToken);

    private static bool IsMissingLocalTable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("no such table", StringComparison.OrdinalIgnoreCase)
                || (message.Contains("relation", StringComparison.OrdinalIgnoreCase)
                    && message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasRequiredSeeds(
        IReadOnlyList<SeedVersionStatus> seeds,
        ServiceInitializationPolicy policy)
    {
        var required = new[]
        {
            BootstrapSeedCatalog.SystemCatalogSeedKey,
            BootstrapSeedCatalog.TenantSecuritySeedKey,
        };
        if (policy == ServiceInitializationPolicy.Advanced)
        {
            required = [.. required, BootstrapSeedCatalog.BootstrapAdminSeedKey];
        }

        return required.All(requiredKey => IsAppliedCurrentSeed(seeds, requiredKey));
    }

    private static bool IsAppliedCurrentSeed(IReadOnlyList<SeedVersionStatus> seeds, string seedKey) =>
        seeds.Any(seed => string.Equals(seed.SeedKey, seedKey, StringComparison.Ordinal)
            && string.Equals(seed.SeedVersion, BootstrapSeedCatalog.SeedVersion, StringComparison.Ordinal)
            && string.Equals(seed.Status, "Applied", StringComparison.Ordinal));

    private static string? NormalizeScope(string? scope) => scope?.Trim() switch
    {
        null => null,
        "" => string.Empty,
        var value when string.Equals(value, BootstrapSeedCatalog.SystemScope, StringComparison.OrdinalIgnoreCase) => "System",
        var value when string.Equals(value, BootstrapSeedCatalog.TenantScope, StringComparison.OrdinalIgnoreCase) => "Tenant",
        _ => scope.Trim(),
    };
}
