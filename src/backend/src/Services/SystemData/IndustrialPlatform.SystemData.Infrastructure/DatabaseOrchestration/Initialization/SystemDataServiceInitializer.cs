using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;

namespace IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Initialization;

/// <summary>SystemData 自有初始化器，只执行自己的 Migration Ledger。</summary>
public sealed class SystemDataServiceInitializer : IServiceInitializer
{
    private readonly ISchemaMigrationRunner _migrationRunner;
    private readonly SqlSugarDbContext _dbContext;

    public SystemDataServiceInitializer(ISchemaMigrationRunner migrationRunner, SqlSugarDbContext dbContext)
    {
        _migrationRunner = migrationRunner;
        _dbContext = dbContext;
    }

    public string ServiceKey => "systemdata";
    public string ModuleKey => "systemdata";

    public async Task<ServiceInitializationState> InspectAsync(ServiceInitializationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var latest = await _dbContext.SqlSugar.Queryable<SchemaMigrationRecord>()
                // 补录旧编号迁移时 AppliedOn 会晚于当前版本，不能用写入时间判断架构版本。
                .OrderByDescending(record => record.MigrationId)
                .FirstAsync(cancellationToken);
            return latest is null
                ? NotReady(context.DesiredVersion, "SystemData 本地迁移账本尚未完成验证。")
                : Ready(latest.MigrationId);
        }
        catch (Exception exception) when (IsMissingLocalTable(exception))
        {
            return NotReady(context.DesiredVersion, "SystemData 本地迁移账本尚未创建。");
        }
    }

    public Task<ServiceInitializationPlan> PlanAsync(
        ServiceInitializationContext context,
        ServiceInitializationState inspection,
        CancellationToken cancellationToken) =>
        Task.FromResult(CreatePlan(context, inspection));

    public async Task<ServiceInitializationState> ApplyAsync(
        ServiceInitializationContext context,
        ServiceInitializationPlan plan,
        CancellationToken cancellationToken)
    {
        await _migrationRunner.ApplyPendingAsync(cancellationToken);
        return await InspectAsync(context, cancellationToken);
    }

    public Task<ServiceInitializationState> VerifyAsync(ServiceInitializationContext context, CancellationToken cancellationToken) =>
        InspectAsync(context, cancellationToken);

    private ServiceInitializationState NotReady(string desiredVersion, string reason) =>
        new(ServiceKey, ModuleKey, null, false, false, true, false, reason);

    private ServiceInitializationState Ready(string version) =>
        new(ServiceKey, ModuleKey, version, true, true, true, true, null);

    private ServiceInitializationPlan CreatePlan(
        ServiceInitializationContext context,
        ServiceInitializationState inspection)
    {
        var desiredVersion = string.IsNullOrWhiteSpace(context.DesiredVersion)
            ? SystemDataSchemaMigrations.All[^1].Id
            : context.DesiredVersion;
        return new ServiceInitializationPlan(
            ServiceKey,
            ModuleKey,
            inspection.ObservedVersion,
            desiredVersion,
            !inspection.Ready || !string.Equals(inspection.ObservedVersion, desiredVersion, StringComparison.Ordinal),
            inspection.Ready ? [] : ["migration", "verify"]);
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
