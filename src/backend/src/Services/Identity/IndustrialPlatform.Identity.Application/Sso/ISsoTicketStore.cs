namespace IndustrialPlatform.Identity.Application.Sso;

/// <summary>
/// OAuth state 与一次性登录票据存储端口(§26.5)。基于 Redis 实现,TTL 由用例传入
/// (state 默认 5 分钟、票据默认 60 秒),消费为原子取删,保证单次使用语义。
/// </summary>
public interface ISsoTicketStore
{
    /// <summary>保存 OAuth state。state 为随机不透明值,禁止落入日志。</summary>
    Task StoreOAuthStateAsync(string state, SsoOAuthState value, TimeSpan ttl, CancellationToken cancellationToken);

    /// <summary>原子消费 OAuth state:一次性取回并删除;不存在或已过期返回 <c>null</c>。</summary>
    Task<SsoOAuthState?> ConsumeOAuthStateAsync(string state, CancellationToken cancellationToken);

    /// <summary>保存一次性登录票据。</summary>
    Task StoreLoginTicketAsync(string ticket, SsoLoginTicket value, TimeSpan ttl, CancellationToken cancellationToken);

    /// <summary>原子消费登录票据:一次性取回并删除;不存在或已过期返回 <c>null</c>。</summary>
    Task<SsoLoginTicket?> ConsumeLoginTicketAsync(string ticket, CancellationToken cancellationToken);
}
