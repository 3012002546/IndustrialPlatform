namespace IndustrialPlatform.Infrastructure.Caching;

/// <summary>
/// Redis 连接配置。
/// </summary>
public sealed class RedisOptions
{
    /// <summary>连接字符串,默认本地 6379。</summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>默认数据库编号。</summary>
    public int DefaultDatabase { get; set; }

    /// <summary>默认过期时间,默认 30 分钟。</summary>
    public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromMinutes(30);
}
