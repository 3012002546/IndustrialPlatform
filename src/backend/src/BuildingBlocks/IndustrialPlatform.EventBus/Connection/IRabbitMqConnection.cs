using RabbitMQ.Client;

namespace IndustrialPlatform.EventBus.Connection;

/// <summary>
/// RabbitMQ 连接抽象,负责连接生命周期与通道创建。
/// </summary>
public interface IRabbitMqConnection
{
    /// <summary>
    /// 确保连接可用并创建一个新通道。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已打开的通道。</returns>
    Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default);

    /// <summary>确保连接可用并按指定发布确认策略创建一个新通道。</summary>
    /// <param name="publisherConfirmationsEnabled">是否启用发布者确认与确认跟踪。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已打开的通道。</returns>
    Task<IChannel> CreateChannelAsync(
        bool publisherConfirmationsEnabled,
        CancellationToken cancellationToken = default);
}
