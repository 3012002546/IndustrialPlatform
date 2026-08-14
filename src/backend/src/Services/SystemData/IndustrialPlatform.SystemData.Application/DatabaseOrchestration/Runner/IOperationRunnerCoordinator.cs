namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;

/// <summary>
/// 数据库编排 Operation Runner 协调端口(05 方案 §7.1.4)。由 Infrastructure HostedService 驱动:
/// 轮询领取 Queued 操作、推进 Validate→…→Verify 状态机、心跳续租、同目标 advisory lock、
/// 受限重试与脱敏失败;不暴露任何 Secret/连接细节。
/// </summary>
public interface IOperationRunnerCoordinator
{
    /// <summary>是否有正在执行的 Operation(供 HostedService 心跳)。</summary>
    bool HasActiveOperation { get; }

    /// <summary>当前活动操作标识(供 HostedService 心跳;无活动返回 <c>null</c>)。</summary>
    string? ActiveOperationNId { get; }

    /// <summary>执行一轮:领取一个操作并执行到结束/失败/超时。返回本轮领取并处理的操作数(0 或 1)。</summary>
    Task<int> RunOnceAsync(CancellationToken cancellationToken);

    /// <summary>对当前活动操作续租(心跳)。无活动操作为空操作。</summary>
    Task HeartbeatActiveAsync(CancellationToken cancellationToken);
}
