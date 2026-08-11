using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace IndustrialPlatform.Identity.Infrastructure.Authentication;

/// <summary>
/// 登录限流/组合锁实现(§10.3/§14 Redis 键)。Redis 不可用时明确降级放行并告警(§14),
/// 持久化用户级锁定由领域层独立保证,不依赖 Redis。
/// </summary>
public sealed class LoginRateLimiter : ILoginRateLimiter
{
    private static readonly Action<ILogger, string, Exception?> RateLimitDegraded =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(RateLimitDegraded)),
            "Redis 不可用,登录限流降级放行(键:{Key})。");

    private readonly IDatabase _database;
    private readonly IOptions<AuthenticationOptions> _options;
    private readonly ILogger<LoginRateLimiter> _logger;

    /// <summary>初始化限流器。</summary>
    public LoginRateLimiter(IConnectionMultiplexer connection, IOptions<AuthenticationOptions> options, ILogger<LoginRateLimiter> logger)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _database = connection.GetDatabase();
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> IsIpRateLimitedAsync(string? clientIp, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(clientIp))
        {
            return false;
        }

        var key = $"identity:rate:login:ip:{Hashing.Sha256Hex(clientIp)}";
        try
        {
            var value = await _database.StringIncrementAsync(key);
            if (value == 1)
            {
                await _database.KeyExpireAsync(key, _options.Value.IpRateLimitWindow);
            }

            return value > _options.Value.IpRateLimitMaxAttempts;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RateLimitDegraded(_logger, key, ex);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsAccountLockedAsync(string tenantNId, string normalizedLoginName, string? clientIp, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(clientIp))
        {
            return false;
        }

        var key = ComboKey(tenantNId, normalizedLoginName, clientIp);
        try
        {
            var value = await _database.StringGetAsync(key);
            return value.TryParse(out long count) && count >= _options.Value.MaxLoginFailures;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RateLimitDegraded(_logger, key, ex);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task RecordLoginFailureAsync(string tenantNId, string normalizedLoginName, string? clientIp, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(clientIp))
        {
            return;
        }

        var key = ComboKey(tenantNId, normalizedLoginName, clientIp);
        try
        {
            var value = await _database.StringIncrementAsync(key);
            if (value == 1)
            {
                await _database.KeyExpireAsync(key, _options.Value.LockDuration);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RateLimitDegraded(_logger, key, ex);
        }
    }

    /// <inheritdoc/>
    public async Task RecordLoginSuccessAsync(string tenantNId, string normalizedLoginName, string? clientIp, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(clientIp))
        {
            return;
        }

        var key = ComboKey(tenantNId, normalizedLoginName, clientIp);
        try
        {
            await _database.KeyDeleteAsync(key);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RateLimitDegraded(_logger, key, ex);
        }
    }

    private static string ComboKey(string tenantNId, string normalizedLoginName, string clientIp) =>
        $"identity:login:fail:{tenantNId}:{normalizedLoginName}:{Hashing.Sha256Hex(clientIp)}";
}
