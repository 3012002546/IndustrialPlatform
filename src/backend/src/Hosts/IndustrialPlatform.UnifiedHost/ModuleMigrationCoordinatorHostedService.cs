using System.Diagnostics;
using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace IndustrialPlatform.UnifiedHost;

/// <summary>
/// UnifiedHost 启动协调器。宿主只按显式模块目录顺序调用服务初始化器，不引用具体 Migration 实现。
/// </summary>
public sealed partial class ModuleMigrationCoordinatorHostedService : IHostedService
{
    private readonly IReadOnlyList<IServiceInitializer> _initializers;
    private readonly IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Initialization.IServiceInitializationInvoker _invoker;
    private readonly IDatabaseTopologyProvider _topologyProvider;
    private readonly IOptions<SqlSugarOptions> _sqlSugarOptions;
    private readonly IOptions<BootstrapOptions> _bootstrapOptions;
    private readonly ILogger<ModuleMigrationCoordinatorHostedService> _logger;

    public ModuleMigrationCoordinatorHostedService(
        IEnumerable<IServiceInitializer> initializers,
        IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Initialization.IServiceInitializationInvoker invoker,
        IDatabaseTopologyProvider topologyProvider,
        IOptions<SqlSugarOptions> sqlSugarOptions,
        IOptions<BootstrapOptions> bootstrapOptions,
        ILogger<ModuleMigrationCoordinatorHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(initializers);
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(topologyProvider);
        ArgumentNullException.ThrowIfNull(sqlSugarOptions);
        ArgumentNullException.ThrowIfNull(bootstrapOptions);
        ArgumentNullException.ThrowIfNull(logger);
        _initializers = initializers.ToArray();
        _invoker = invoker;
        _topologyProvider = topologyProvider;
        _sqlSugarOptions = sqlSugarOptions;
        _bootstrapOptions = bootstrapOptions;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var initializationLock = _sqlSugarOptions.Value.DbType == DbType.SqlServer
            ? await SqlServerMigrationArtifacts.AcquireLockAsync(_sqlSugarOptions.Value.ConnectionString, "industrial-platform:initialization", cancellationToken)
            : null;
        var topology = _topologyProvider.GetTopology();
        var provider = _sqlSugarOptions.Value.DbType switch
        {
            DbType.Sqlite => DatabaseProvider.Sqlite,
            DbType.PostgreSQL => DatabaseProvider.PostgreSQL,
            DbType.SqlServer => DatabaseProvider.SqlServer,
            var value => throw new NotSupportedException($"UnifiedHost 不支持数据库提供程序 {value}。"),
        };
        var target = DatabaseTopologyResolver.Resolve(topology, "unifiedhost", provider, "unifiedhost_db");
        var context = new ServiceInitializationContext(
            topology.EnvironmentName,
            _bootstrapOptions.Value.TenantNId,
            $"unifiedhost-startup-{topology.EnvironmentName}",
            "unifiedhost",
            "unifiedhost",
            target,
            string.Empty,
            ServiceInitializationPolicy.Standard,
            Activity.Current?.Id ?? "unifiedhost-startup");

        LogInitializationStarted();
        await RunInitializersAsync(_invoker, _initializers, context, cancellationToken, _logger);
        LogInitializationCompleted();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>按 UnifiedHost 模块目录顺序运行服务初始化器。</summary>
    public static async Task RunInitializersAsync(
        IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Initialization.IServiceInitializationInvoker invoker,
        IEnumerable<IServiceInitializer> initializers,
        ServiceInitializationContext context,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(initializers);
        ArgumentNullException.ThrowIfNull(context);

        var byService = initializers.ToDictionary(initializer => initializer.ServiceKey, StringComparer.OrdinalIgnoreCase);
        foreach (var serviceKey in UnifiedHostModuleCatalog.GetServiceKeys(byService.ContainsKey("platform-studio") ? [.. UnifiedHostModuleCatalog.Modules, new IndustrialPlatform.PlatformStudio.Api.PlatformStudioUnifiedHostModule()] : UnifiedHostModuleCatalog.Modules))
        {
            if (!byService.TryGetValue(serviceKey, out var initializer))
            {
                throw new InvalidOperationException($"UnifiedHost 未注册 {serviceKey} 初始化器。");
            }

            var serviceContext = context with
            {
                ServiceKey = serviceKey,
                ModuleKey = initializer.ModuleKey,
                DesiredVersion = context.DesiredVersion,
                DatabaseTarget = context.DatabaseTarget with
                {
                    ServiceKey = serviceKey,
                    LogicalDatabaseName = $"{serviceKey}_db",
                },
            };
            ServiceInitializationState inspection;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    inspection = await invoker.InspectAsync(serviceContext, cancellationToken);
                    break;
                }
                // SqlSugar wraps connection-open failures without retaining the provider's
                // exception type. Retry only this observed connectivity failure and only the
                // read-only inspection; never replay migrations, seeds or authentication errors.
                catch (SqlSugarException exception) when (attempt < 3
                    && !cancellationToken.IsCancellationRequested
                    && exception.Message.Contains("Failed to connect to", StringComparison.Ordinal))
                {
                    if (logger is not null) LogConnectionRetry(logger, serviceKey, attempt, attempt * 2);
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
                }
            }
            var plan = await invoker.PlanAsync(serviceContext, inspection, cancellationToken);
            var executionContext = serviceContext with { DesiredVersion = plan.DesiredVersion };
            if (plan.RequiresApply)
            {
                await invoker.ApplyAsync(executionContext, plan, cancellationToken);
            }

            var verified = await invoker.VerifyAsync(executionContext, cancellationToken);
            if (!verified.Ready
                || !string.Equals(verified.ObservedVersion, plan.DesiredVersion, StringComparison.Ordinal))
            {
                var reason = !verified.Ready
                    ? (string.IsNullOrWhiteSpace(verified.Reason) ? "服务初始化验证未提供原因。" : verified.Reason)
                    : $"ObservedVersion {verified.ObservedVersion ?? "<null>"} 与目标版本 {plan.DesiredVersion} 不一致。";
                var failure = new InvalidOperationException(
                    $"UnifiedHost {serviceKey} 初始化验证未就绪: {reason}");
                failure.Data["ServiceKey"] = serviceKey;
                failure.Data["Reason"] = reason;
                throw failure;
            }
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "UnifiedHost 服务初始化:开始按显式模块目录顺序执行。")]
    private partial void LogInitializationStarted();

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "UnifiedHost 服务初始化:全部完成。")]
    private partial void LogInitializationCompleted();

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "UnifiedHost {ServiceKey} initialization inspection could not connect to the database; retry {Attempt}/2 in {DelaySeconds}s.")]
    private static partial void LogConnectionRetry(ILogger logger, string serviceKey, int attempt, int delaySeconds);
}
