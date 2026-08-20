using IndustrialPlatform.Application.Abstractions.Initialization;

namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Initialization;

/// <summary>
/// SystemData 的初始化调用端口。传输方式由 Infrastructure 适配器决定。
/// </summary>
public interface IServiceInitializationInvoker
{
    Task<ServiceInitializationState> InspectAsync(ServiceInitializationContext context, CancellationToken cancellationToken);

    Task<ServiceInitializationPlan> PlanAsync(
        ServiceInitializationContext context,
        ServiceInitializationState inspection,
        CancellationToken cancellationToken);

    Task<ServiceInitializationState> ApplyAsync(
        ServiceInitializationContext context,
        ServiceInitializationPlan plan,
        CancellationToken cancellationToken);

    Task<ServiceInitializationState> VerifyAsync(ServiceInitializationContext context, CancellationToken cancellationToken);
}
