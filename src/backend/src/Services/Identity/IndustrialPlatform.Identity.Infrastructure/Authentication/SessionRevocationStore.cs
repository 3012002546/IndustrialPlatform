using IndustrialPlatform.Identity.Application.Authentication;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace IndustrialPlatform.Identity.Infrastructure.Authentication;

/// <summary>
/// 会话撤销存储实现(§13/§14):把会话 sid 写入 Redis 撤销键 <c>identity:session:revoked:{sid}</c>
/// 直到 Access Token 到期。写入尽力而为(数据库会话撤销为权威,失败仅告警);
/// 校验必须 fail-closed:Redis 不可用时抛安全存储不可用(§14),不得静默放行。
/// </summary>
public sealed class SessionRevocationStore : ISessionRevocationStore
{
    private static readonly Action<ILogger, string, Exception?> RevocationWriteFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(RevocationWriteFailed)),
            "Redis 会话撤销写入失败,数据库撤销为权威(键:{Key})。");

    private static readonly Action<ILogger, string, Exception?> RevocationCheckFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(2, nameof(RevocationCheckFailed)),
            "Redis 会话撤销校验失败,安全存储不可用(fail-closed,键:{Key})。");

    private readonly IDatabase _database;
    private readonly ILogger<SessionRevocationStore> _logger;

    /// <summary>初始化会话撤销存储。</summary>
    public SessionRevocationStore(IConnectionMultiplexer connection, ILogger<SessionRevocationStore> logger)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(logger);
        _database = connection.GetDatabase();
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task RevokeAsync(string sessionNId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionNId))
        {
            return;
        }

        var key = Key(sessionNId);
        try
        {
            await _database.StringSetAsync(key, "1", ttl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RevocationWriteFailed(_logger, key, ex);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsRevokedAsync(string sessionNId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionNId))
        {
            return false;
        }

        var key = Key(sessionNId);
        try
        {
            return await _database.KeyExistsAsync(key);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RevocationCheckFailed(_logger, key, ex);
            throw new SecurityStoreUnavailableException();
        }
    }

    private static string Key(string sessionNId) => $"identity:session:revoked:{sessionNId}";
}
