using System.Diagnostics;
using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Initialization;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace IndustrialPlatform.Collaboration.EmbeddedHost;

/// <summary>
/// 独立协作宿主的启动编排，复用各模块已有初始化器，
/// 按身份 → 系统数据 → 参考数据 → 协作的顺序初始化独立宿主数据库。
/// FixedDemo 与正式 MES 模式都可显式启用；普通平台宿主不注册此服务。
/// </summary>
public class StandaloneDatabaseInitializationHostedService : IHostedService
{
    private static readonly string[] ServiceOrder = ["identity", "systemdata", "referencedata", "collaboration"];
    private readonly Dictionary<string, IServiceInitializer> _initializers;
    private readonly IServiceInitializationInvoker _invoker;
    private readonly IOptions<DatabaseTopologyOptions> _topologyOptions;
    private readonly IOptions<SqlSugarOptions> _sqlSugarOptions;
    private readonly IOptions<BootstrapOptions> _bootstrapOptions;
    private readonly IHostEnvironment _environment;
    private readonly IStandaloneInitializationLock _initializationLock;

    public StandaloneDatabaseInitializationHostedService(
        IEnumerable<IServiceInitializer> initializers,
        IServiceInitializationInvoker invoker,
        IOptions<DatabaseTopologyOptions> topologyOptions,
        IOptions<SqlSugarOptions> sqlSugarOptions,
        IOptions<BootstrapOptions> bootstrapOptions,
        IHostEnvironment environment,
        IStandaloneInitializationLock initializationLock)
    {
        _initializers = initializers.ToDictionary(item => item.ServiceKey, StringComparer.OrdinalIgnoreCase);
        _invoker = invoker;
        _topologyOptions = topologyOptions;
        _sqlSugarOptions = sqlSugarOptions;
        _bootstrapOptions = bootstrapOptions;
        _environment = environment;
        _initializationLock = initializationLock;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var topology = _topologyOptions.Value.ToTopology();
        if (!topology.IsStandalone)
            throw new InvalidOperationException("Standalone 初始化器必须使用显式 IsStandalone 拓扑。");
        var provider = _sqlSugarOptions.Value.DbType == DbType.Sqlite
            ? DatabaseProvider.Sqlite
            : DatabaseProvider.PostgreSQL;
        var operationNId = $"standalone-startup-{_environment.EnvironmentName}";
        var lockTarget = DatabaseTopologyResolver.Resolve(topology, ServiceOrder[0], provider, $"{ServiceOrder[0]}_db");
        await using var initializationLock = await _initializationLock.AcquireAsync(lockTarget, cancellationToken);

        foreach (var serviceKey in ServiceOrder)
        {
            if (!_initializers.TryGetValue(serviceKey, out var initializer))
                throw new InvalidOperationException($"独立宿主未注册 {serviceKey} 初始化器。");

            var target = DatabaseTopologyResolver.Resolve(topology, serviceKey, provider, $"{serviceKey}_db") with
            {
                ServiceKey = serviceKey,
            };
            var context = new ServiceInitializationContext(
                topology.EnvironmentName,
                _bootstrapOptions.Value.TenantNId,
                operationNId,
                serviceKey,
                initializer.ModuleKey,
                target,
                string.Empty,
                string.Equals(topology.EnvironmentName, "Production", StringComparison.OrdinalIgnoreCase)
                    ? ServiceInitializationPolicy.Advanced
                    : ServiceInitializationPolicy.Standard,
                Activity.Current?.Id ?? operationNId);

            var inspection = await _invoker.InspectAsync(context, cancellationToken);
            var plan = await _invoker.PlanAsync(context, inspection, cancellationToken);
            var executionContext = context with { DesiredVersion = plan.DesiredVersion };
            if (plan.RequiresApply)
                await _invoker.ApplyAsync(executionContext, plan, cancellationToken);

            var verified = await _invoker.VerifyAsync(executionContext, cancellationToken);
            if (!verified.Ready || !string.Equals(verified.ObservedVersion, plan.DesiredVersion, StringComparison.Ordinal))
                throw new InvalidOperationException($"独立宿主 {serviceKey} 初始化验证未就绪: {verified.Reason ?? "版本不一致"}");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Compatibility name for older local launch profiles; new hosts use the standalone name.</summary>
[Obsolete("Use StandaloneDatabaseInitializationHostedService.")]
public sealed class FixedDemoDatabaseInitializationHostedService : StandaloneDatabaseInitializationHostedService
{
    public FixedDemoDatabaseInitializationHostedService(
        IEnumerable<IServiceInitializer> initializers,
        IServiceInitializationInvoker invoker,
        IOptions<DatabaseTopologyOptions> topologyOptions,
        IOptions<SqlSugarOptions> sqlSugarOptions,
        IOptions<BootstrapOptions> bootstrapOptions,
        IHostEnvironment environment,
        IStandaloneInitializationLock initializationLock)
        : base(initializers, invoker, topologyOptions, sqlSugarOptions, bootstrapOptions, environment, initializationLock)
    {
    }
}
