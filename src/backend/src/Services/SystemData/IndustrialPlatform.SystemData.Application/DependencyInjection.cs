using IndustrialPlatform.SystemData.Application.Assignments;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Options;
using IndustrialPlatform.SystemData.Application.IdentityDirectory;
using IndustrialPlatform.SystemData.Application.Organizations;
using IndustrialPlatform.SystemData.Application.Positions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialPlatform.SystemData.Application;

/// <summary>
/// SystemData 应用层依赖注入扩展。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 SystemData 应用层服务。TASK-SD-002 注册数据库编排用例
    /// (注册清单/计划/操作/审批/备份)与选项绑定;端口生命周期由各服务按需解析。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置源。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddSystemDataApplication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DatabaseOrchestrationOptions>()
            .Bind(configuration.GetSection(DatabaseOrchestrationOptions.SectionName));

        services.AddSingleton<IRegistrationService, DatabaseRegistrationService>();
        services.AddSingleton<IPlanService, DatabasePlanService>();
        services.AddSingleton<IOperationService, DatabaseOperationService>();
        services.AddSingleton<IApprovalService, DatabaseApprovalService>();
        services.AddSingleton<IBackupService, DatabaseBackupService>();

        // TASK-SD-006:组织/岗位/任职管理用例与审计/目录端口。
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILocalAuditCommand, NoopLocalAuditCommand>();
        services.AddSingleton<IIdentityUserDirectory, UnavailableIdentityUserDirectory>();
        services.AddSingleton<IAdministrativeOrganizationService, AdministrativeOrganizationService>();
        services.AddSingleton<IPositionService, PositionService>();
        services.AddSingleton<IUserAssignmentService, UserAssignmentService>();

        return services;
    }
}
