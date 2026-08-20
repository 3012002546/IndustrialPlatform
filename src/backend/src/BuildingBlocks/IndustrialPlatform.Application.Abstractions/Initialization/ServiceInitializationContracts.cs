using IndustrialPlatform.SharedKernel.Topology;

namespace IndustrialPlatform.Application.Abstractions.Initialization;

/// <summary>服务初始化执行策略。</summary>
public enum ServiceInitializationPolicy
{
    Standard,
    Advanced,
}

/// <summary>
/// 初始化器的非敏感输入。数据库凭据、原始 SQL、种子内容和文件路径不属于此协议。
/// </summary>
public sealed record ServiceInitializationContext(
    string EnvironmentName,
    string TenantNId,
    string OperationNId,
    string ServiceKey,
    string ModuleKey,
    ResolvedDatabaseTarget DatabaseTarget,
    string DesiredVersion,
    ServiceInitializationPolicy Policy,
    string TraceId);

/// <summary>服务本地初始化事实，供 readiness 和控制面观察使用。</summary>
public sealed record ServiceInitializationState(
    string ServiceKey,
    string ModuleKey,
    string? ObservedVersion,
    bool MigrationReady,
    bool RequiredSeedReady,
    bool BootstrapReady,
    bool Ready,
    string? Reason);

/// <summary>服务本地初始化计划，只描述步骤，不携带执行内容。</summary>
public sealed record ServiceInitializationPlan(
    string ServiceKey,
    string ModuleKey,
    string? CurrentVersion,
    string DesiredVersion,
    bool RequiresApply,
    IReadOnlyList<string> Steps);

/// <summary>服务拥有的 Migration、Seed、Bootstrap、Verify 和 Ledger 统一入口。</summary>
public interface IServiceInitializer
{
    string ServiceKey { get; }
    string ModuleKey { get; }

    Task<ServiceInitializationState> InspectAsync(
        ServiceInitializationContext context,
        CancellationToken cancellationToken);

    Task<ServiceInitializationPlan> PlanAsync(
        ServiceInitializationContext context,
        ServiceInitializationState inspection,
        CancellationToken cancellationToken);

    Task<ServiceInitializationState> ApplyAsync(
        ServiceInitializationContext context,
        ServiceInitializationPlan plan,
        CancellationToken cancellationToken);

    Task<ServiceInitializationState> VerifyAsync(
        ServiceInitializationContext context,
        CancellationToken cancellationToken);
}
