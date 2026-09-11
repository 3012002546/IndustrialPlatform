namespace IndustrialPlatform.EventBus.Options;

/// <summary>
/// RabbitMQ 连接与事件总线配置,对应配置节点 "RabbitMQ"。
/// </summary>
public sealed class RabbitMqOptions
{
    /// <summary>主机名,默认本地。</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>端口,默认 5672。</summary>
    public int Port { get; set; } = 5672;

    /// <summary>用户名,默认 guest。</summary>
    public string UserName { get; set; } = "guest";

    /// <summary>密码,默认 guest。</summary>
    public string Password { get; set; } = "guest";

    /// <summary>虚拟主机,默认 /。</summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>Topic 交换器名称,默认 industrial.business。</summary>
    public string ExchangeName { get; set; } = "industrial.business";

    /// <summary>消费队列名称,默认 industrial.events。</summary>
    public string QueueName { get; set; } = "industrial.events";

    /// <summary>队列绑定交换器的路由模式,默认 # 匹配全部。</summary>
    public string RoutingPattern { get; set; } = "#";

    /// <summary>消费者预取数量。</summary>
    public ushort PrefetchCount { get; set; } = 10;

    /// <summary>失败消息暂存交换器；主队列 reject 后进入该交换器，TTL 到期后回主交换器。</summary>
    public string RetryExchangeName { get; set; } = "industrial.business.retry";

    /// <summary>失败消息暂存队列。</summary>
    public string RetryQueueName { get; set; } = "industrial.events.retry";

    /// <summary>失败消息最大投递次数，超过后进入 DLQ。</summary>
    public int MaxDeliveryAttempts { get; set; } = 5;

    /// <summary>每次重试在 retry queue 中等待的毫秒数。</summary>
    public int RetryDelayMilliseconds { get; set; } = 5000;

    /// <summary>租约竞争消息使用的独立延迟交换器。</summary>
    public string BusyRetryExchangeName { get; set; } = "industrial.business.busy-retry";

    /// <summary>租约竞争消息使用的独立延迟队列。</summary>
    public string BusyRetryQueueName { get; set; } = "industrial.events.busy-retry";

    /// <summary>租约竞争消息在独立队列中的等待毫秒数。</summary>
    public int BusyRetryDelayMilliseconds { get; set; } = 5000;

    /// <summary>死信交换器。</summary>
    public string DeadLetterExchangeName { get; set; } = "industrial.business.dlx";

    /// <summary>死信队列。</summary>
    public string DeadLetterQueueName { get; set; } = "industrial.events.dlq";

    /// <summary>死信交换器投递路由键。</summary>
    public string DeadLetterRoutingKey { get; set; } = "dead-letter";
}
