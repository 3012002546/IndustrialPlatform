using IndustrialPlatform.Application.Abstractions.Initialization;

namespace IndustrialPlatform.ReferenceData.Infrastructure.Initialization;

/// <summary>ReferenceData 服务级初始化器；核心就绪只由本地数据库事实决定。</summary>
public sealed class ReferenceDataServiceInitializer : IServiceInitializer
{
    public const string BaselineVersion = "reference-data-2.7-001";
    public const string CurrentVersion = "reference-data-2.7-011";
    public const string BaselineSeedKey = "reference-data.baseline";
    public static readonly string BaselineChecksum =
        System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{BaselineSeedKey}|{BaselineVersion}|System|reference-data-baseline"))
        .Aggregate(string.Empty, (current, value) => current + value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));

    private readonly ReferenceDataInitializationLedger _ledger;

    public ReferenceDataServiceInitializer(ReferenceDataInitializationLedger ledger)
    {
        _ledger = ledger;
    }

    public string ServiceKey => "referencedata";
    public string ModuleKey => "referencedata";

    public async Task<ServiceInitializationState> InspectAsync(ServiceInitializationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_ledger.MatchesTarget(context))
            return new ServiceInitializationState(ServiceKey, ModuleKey, null, false, false, true, false, "REF-INITIALIZATION-TARGET-MISMATCH");
        return await InspectLocalAsync(cancellationToken);
    }

    public async Task<ServiceInitializationState> InspectLocalAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var migration = await _ledger.GetMigrationAsync(cancellationToken);
            var seed = await _ledger.GetSeedAsync(BaselineSeedKey, BaselineVersion, cancellationToken);
            var migrationReady = migration?.MigrationId == CurrentVersion && await _ledger.MigrationValidAsync(cancellationToken);
            var seedReady = seed is not null
                            && string.Equals(seed.Checksum, BaselineChecksum, StringComparison.Ordinal)
                            && string.Equals(seed.Scope, "System", StringComparison.OrdinalIgnoreCase);
            return new ServiceInitializationState(
                ServiceKey,
                ModuleKey,
                migration?.MigrationId,
                migrationReady,
                seedReady,
                true,
                migrationReady && seedReady,
                migrationReady && seedReady ? null : "ReferenceData 服务级 baseline 尚未完成。",
                seed is null
                    ? []
                    : [new ServiceInitializationSeedState(
                        BaselineSeedKey,
                        BaselineVersion,
                        "Applied",
                        seed.AppliedOn,
                        seed.Checksum,
                        seed.Scope)]);
        }
        catch (Exception exception) when (IsMissingLocalTable(exception))
        {
            return new ServiceInitializationState(
                ServiceKey,
                ModuleKey,
                null,
                false,
                false,
                true,
                false,
                "ReferenceData 服务级初始化账本尚未创建。");
        }
    }

    public Task<ServiceInitializationPlan> PlanAsync(ServiceInitializationContext context, ServiceInitializationState inspection, CancellationToken cancellationToken) =>
        Task.FromResult(CreatePlan(context, inspection));

    public async Task<ServiceInitializationState> ApplyAsync(ServiceInitializationContext context, ServiceInitializationPlan plan, CancellationToken cancellationToken)
    {
        if (!_ledger.MatchesTarget(context)) throw new InvalidOperationException("REF-INITIALIZATION-TARGET-MISMATCH");
        if (string.Equals(context.EnvironmentName, "Production", StringComparison.OrdinalIgnoreCase)
            && context.Policy != ServiceInitializationPolicy.Advanced)
            throw new InvalidOperationException("REF-INITIALIZATION-ADVANCED-REQUIRED");
        if (plan.ServiceKey != ServiceKey || plan.ModuleKey != ModuleKey || plan.DesiredVersion != CurrentVersion
            || (!string.IsNullOrEmpty(context.DesiredVersion) && context.DesiredVersion != CurrentVersion))
            throw new InvalidOperationException("REF-INITIALIZATION-VERSION-UNSUPPORTED");
        await _ledger.ApplyAsync(context, cancellationToken);
        return await InspectAsync(context, cancellationToken);
    }

    public Task<ServiceInitializationState> VerifyAsync(ServiceInitializationContext context, CancellationToken cancellationToken) =>
        InspectAsync(context, cancellationToken);

    private ServiceInitializationPlan CreatePlan(
        ServiceInitializationContext context,
        ServiceInitializationState inspection)
    {
        var desiredVersion = string.IsNullOrWhiteSpace(context.DesiredVersion)
            ? CurrentVersion
            : context.DesiredVersion;
        return new ServiceInitializationPlan(
            ServiceKey,
            ModuleKey,
            inspection.ObservedVersion,
            desiredVersion,
            !inspection.Ready || !string.Equals(inspection.ObservedVersion, desiredVersion, StringComparison.Ordinal),
            inspection.Ready ? [] : ["reference-data-schema-migration", "reference-data-required-seed"]);
    }

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
}
