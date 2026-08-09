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
}
