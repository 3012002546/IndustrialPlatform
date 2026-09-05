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
        options.EnvironmentName = environment.EnvironmentName;
        var key = unifiedHost ? "unifiedhost" : "referencedata";
        if (options.Mode == DatabaseTopologyMode.PerService && !options.ServiceDatabases.ContainsKey(key))
            throw new InvalidOperationException("REF-INITIALIZATION-TARGET-MISSING");
        var target = DatabaseTopologyResolver.Resolve(options.ToTopology(), key,
            database.Value.DbType == DbType.PostgreSQL ? DatabaseProvider.PostgreSQL : DatabaseProvider.Sqlite, "referencedata_db")
            with { ServiceKey = "referencedata" };
        return new ServiceInitializationContext(environment.EnvironmentName, tenantNId, operationNId, "referencedata", "referencedata", target,
            version ?? ReferenceDataServiceInitializer.CurrentVersion, policy, traceId);
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
