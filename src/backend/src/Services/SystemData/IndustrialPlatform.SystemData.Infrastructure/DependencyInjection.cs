using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.SystemData.Application.Assignments;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Initialization;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Application.Organizations;
using IndustrialPlatform.SystemData.Application.Positions;
using IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Initialization;
using IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.SystemData;
using IndustrialPlatform.SystemData.Infrastructure.Topology;
using IndustrialPlatform.SharedKernel.Topology;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialPlatform.SystemData.Infrastructure;

/// <summary>
/// SystemData 服务基础设施依赖注入扩展。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 SystemData 服务的基础依赖(SqlSugar/Redis/拓扑选项),配置节见 Api 的 appsettings.Development.json。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置源。</param>
    /// <param name="includeStartupMigrationService">
    /// 是否注册启动迁移后台服务(默认 true)。UnifiedHost 组合多模块时为避免两套迁移后台服务
    /// 并行迁移同一物理库,传入 false 并由宿主级模块迁移协调器确定顺序执行
    /// (<see cref="SystemDataStartupMigrations"/>);独立 SystemData.Api 保持默认行为。
    /// </param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddSystemDataInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool includeStartupMigrationService = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSqlSugar(configuration);
        services.AddRedis(configuration);

        // 受信任环境的数据库拓扑选项(05 方案 §2.3/§7.1),供拓扑解析与编排使用。
        services.AddOptions<DatabaseTopologyOptions>()
            .Bind(configuration.GetSection(DatabaseTopologyOptions.SectionName));

        // 迁移执行框架与 SystemData 库迁移步骤(TASK-SD-001):运行器按 Id 排序幂等执行。
        services.AddTransient<ISchemaMigrationRunner, SchemaMigrationRunner>();
        services.AddSingleton<SystemDataServiceInitializer>();
        services.AddSingleton<IServiceInitializer>(sp => sp.GetRequiredService<SystemDataServiceInitializer>());
        services.AddHttpClient<HttpServiceInitializationInvoker>();
        services.AddSingleton<IServiceInitializationInvoker>(sp =>
            sp.GetRequiredService<HttpServiceInitializationInvoker>());
        foreach (var step in SystemDataSchemaMigrations.All)
        {
            services.AddTransient<SchemaMigrationStep>(_ => step);
        }

        if (includeStartupMigrationService)
        {
            services.AddHostedService<SchemaMigrationBackgroundService>();
        }

        // 数据库编排(TASK-SD-002):持久化端口与可信拓扑提供(TASK-SD-001 的 DatabaseTopologyOptions 已注册)。
        services.AddSingleton<IDatabaseOrchestrationStore, DatabaseOrchestrationStore>();
        services.AddSingleton<IDatabaseTopologyProvider, ConfigurationDatabaseTopologyProvider>();

        // 数据库编排 Runner(TASK-SD-003):选项绑定 + 目标适配器路由 + 凭据/产物/校验端口 + 编排核心 + 后台驱动。
        // 端口生命周期:适配器具体类与路由均由容器解析,编排核心只依赖端口接口,不感知驱动。
        services.AddOptions<DatabaseOperationRunnerOptions>()
            .Bind(configuration.GetSection(DatabaseOperationRunnerOptions.SectionName));

        services.AddSingleton<PostgreSqlTargetDatabaseAdapter>();
        services.AddSingleton<SqliteTargetDatabaseAdapter>();
        services.AddSingleton<DatabaseTargetAdapterRouter>();
        services.AddSingleton<ITargetDatabaseInspector>(sp => sp.GetRequiredService<DatabaseTargetAdapterRouter>());
        services.AddSingleton<ITargetDatabaseProvisioner>(sp => sp.GetRequiredService<DatabaseTargetAdapterRouter>());
        services.AddSingleton<IMigrationExecutor>(sp => sp.GetRequiredService<DatabaseTargetAdapterRouter>());
        services.AddSingleton<ITargetDatabaseAdvisoryLock>(sp => sp.GetRequiredService<DatabaseTargetAdapterRouter>());

        services.AddSingleton<MigrationArtifactChecksumVerifier>();
        services.AddSingleton<IMigrationArtifactVerifier>(sp => sp.GetRequiredService<MigrationArtifactChecksumVerifier>());
        services.AddSingleton<ISeedArtifactVerifier>(sp => sp.GetRequiredService<MigrationArtifactChecksumVerifier>());
        services.AddSingleton<FileSystemArtifactStore>();
        services.AddSingleton<IMigrationArtifactStore>(sp => sp.GetRequiredService<FileSystemArtifactStore>());
        services.AddSingleton<ISeedArtifactStore>(sp => sp.GetRequiredService<FileSystemArtifactStore>());
        services.AddSingleton<EnvironmentCredentialResolver>();
        services.AddSingleton<IDatabaseCredentialResolver>(sp => sp.GetRequiredService<EnvironmentCredentialResolver>());
        services.AddSingleton<EnvironmentSeedSecretResolver>();
        services.AddSingleton<ISeedSecretResolver>(sp => sp.GetRequiredService<EnvironmentSeedSecretResolver>());
        services.AddSingleton<FileCredentialSink>();
        services.AddSingleton<IDatabaseCredentialSink>(sp => sp.GetRequiredService<FileCredentialSink>());

        // 种子执行器(TASK-SD-004,蓝图 §5.2):按产物 ExecutorKind 选择;多实现以 IEnumerable 注入 Runner。
        services.AddSingleton<ISeedExecutor, SqlSeedBundleExecutor>();
        services.AddSingleton<ISeedExecutor, ServiceInitializerExecutor>();

        services.AddSingleton<DatabaseOperationRunner>();
        services.AddSingleton<IOperationRunnerCoordinator>(sp => sp.GetRequiredService<DatabaseOperationRunner>());
        services.AddHostedService<DatabaseOrchestrationRunnerHostedService>();

        // 组织/岗位/任职持久化端口(TASK-SD-005):仓储与按用户 advisory lock。
        services.AddSingleton<IAdministrativeOrganizationStore, AdministrativeOrganizationStore>();
        services.AddSingleton<IPositionStore, PositionStore>();
        services.AddSingleton<IUserAssignmentStore, UserAssignmentStore>();
        services.AddSingleton<IUserAssignmentAdvisoryLock, UserAssignmentAdvisoryLock>();

        return services;
    }
}
