using IndustrialPlatform.Application.Abstractions.Initialization;

namespace IndustrialPlatform.ReferenceData.Infrastructure.Initialization;

/// <summary>ReferenceData 服务级初始化器，只应用一个 baseline，不实现五个业务模块。</summary>
public sealed class ReferenceDataServiceInitializer : IServiceInitializer
{
    public const string BaselineVersion = "reference-data-baseline-v1";
    public const string BaselineSeedKey = "reference-data.baseline";

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
        try
        {
            var migration = await _ledger.GetMigrationAsync(cancellationToken);
            var seed = await _ledger.GetSeedAsync(BaselineSeedKey, BaselineVersion, cancellationToken);
            var migrationReady = migration?.MigrationId == BaselineVersion;
            var seedReady = seed is not null;
            return new ServiceInitializationState(
                ServiceKey,
                ModuleKey,
                migration?.MigrationId,
                migrationReady,
                seedReady,
                true,
                migrationReady && seedReady,
                migrationReady && seedReady ? null : "ReferenceData 服务级 baseline 尚未完成。");
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
        _ledger.EnsureTables();
        if (await _ledger.GetMigrationAsync(cancellationToken) is null)
        {
            await _ledger.RecordMigrationAsync(BaselineVersion, cancellationToken);
        }

        if (await _ledger.GetSeedAsync(BaselineSeedKey, BaselineVersion, cancellationToken) is null)
        {
            await _ledger.RecordSeedAsync(BaselineSeedKey, BaselineVersion, context.OperationNId, context.TraceId, cancellationToken);
        }

        return await InspectAsync(context, cancellationToken);
    }

    public Task<ServiceInitializationState> VerifyAsync(ServiceInitializationContext context, CancellationToken cancellationToken) =>
        InspectAsync(context, cancellationToken);

    private ServiceInitializationPlan CreatePlan(
        ServiceInitializationContext context,
        ServiceInitializationState inspection)
    {
        var desiredVersion = string.IsNullOrWhiteSpace(context.DesiredVersion)
            ? BaselineVersion
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
