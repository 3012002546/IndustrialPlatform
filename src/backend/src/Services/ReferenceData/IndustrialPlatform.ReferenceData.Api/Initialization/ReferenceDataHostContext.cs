using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Infrastructure.Initialization;
using IndustrialPlatform.SharedKernel.Topology;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Api.Initialization;

public sealed class ReferenceDataHostContext(IConfiguration configuration, IHostEnvironment environment, IOptions<SqlSugarOptions> database, bool unifiedHost)
{
    public ServiceInitializationContext Create(string operationNId, string traceId, string tenantNId = "", string? version = null, ServiceInitializationPolicy policy = ServiceInitializationPolicy.Standard)
    {
        var options = new DatabaseTopologyOptions();
        configuration.GetSection(DatabaseTopologyOptions.SectionName).Bind(options);
        if (!string.Equals(options.EnvironmentName, environment.EnvironmentName, StringComparison.Ordinal))
            throw new InvalidOperationException("ReferenceData database topology environment does not match the host environment.");
        var key = unifiedHost ? "unifiedhost" : "referencedata";
        if (options.Mode == DatabaseTopologyMode.PerService && !options.ServiceDatabases.ContainsKey(key))
            throw new InvalidOperationException("REF-INITIALIZATION-TARGET-MISSING");
        var provider = database.Value.DbType switch
        {
            DbType.PostgreSQL => DatabaseProvider.PostgreSQL,
            DbType.Sqlite => DatabaseProvider.Sqlite,
            DbType.SqlServer => DatabaseProvider.SqlServer,
            var dbType => throw new NotSupportedException($"ReferenceData 不支持的数据库类型:{dbType}。"),
        };
        var target = DatabaseTopologyResolver.Resolve(options.ToTopology(), key,
            provider, "referencedata_db")
            with { ServiceKey = "referencedata" };
        return new ServiceInitializationContext(environment.EnvironmentName, tenantNId, operationNId, "referencedata", "referencedata", target,
            version ?? ReferenceDataServiceInitializer.VersionFor(provider), policy, traceId);
    }
}

public sealed class ReferenceDataStartupInitialization(ReferenceDataServiceInitializer initializer, ReferenceDataHostContext contextFactory, IConfiguration configuration, IHostEnvironment environment) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("ReferenceData:Initialization:AutoApply")) return;
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Test"))
            throw new InvalidOperationException("REF-INITIALIZATION-AUTO-APPLY-FORBIDDEN");
        var context = contextFactory.Create("referencedata-startup", "referencedata-startup");
        var inspection = await initializer.InspectAsync(context, cancellationToken);
        var plan = await initializer.PlanAsync(context, inspection, cancellationToken);
        if (plan.RequiresApply) await initializer.ApplyAsync(context, plan, cancellationToken);
        if (!(await initializer.VerifyAsync(context, cancellationToken)).Ready)
            throw new InvalidOperationException("REF-INITIALIZATION-NOT-READY");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
