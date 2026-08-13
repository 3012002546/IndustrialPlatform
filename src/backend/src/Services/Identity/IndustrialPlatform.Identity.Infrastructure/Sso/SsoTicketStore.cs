using System.Text.Json;
using IndustrialPlatform.Identity.Application.Sso;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace IndustrialPlatform.Identity.Infrastructure.Sso;

/// <summary>
/// OAuth state 与一次性登录票据存储实现(§26.5):键 <c>identity:sso:state:{state}</c> /
/// <c>identity:sso:ticket:{ticket}</c>,TTL 由用例传入,消费为原子取删(单次使用)。
/// Redis 不可用时 fail-closed:抛 <see cref="SsoProviderUnavailableException"/>,绝不静默放行。
/// </summary>
public sealed class SsoTicketStore : ISsoTicketStore
{
    private const string StatePrefix = "identity:sso:state:";
    private const string TicketPrefix = "identity:sso:ticket:";

    private static readonly Action<ILogger, string, Exception?> StateStoreFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, nameof(StateStoreFailed)),
            "SSO OAuth state 写入失败,安全存储不可用(fail-closed,键:{Key})。");

    private static readonly Action<ILogger, string, Exception?> StateConsumeFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(2, nameof(StateConsumeFailed)),
            "SSO OAuth state 消费失败,安全存储不可用(fail-closed,键:{Key})。");

    private static readonly Action<ILogger, string, Exception?> TicketStoreFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(3, nameof(TicketStoreFailed)),
            "SSO 登录票据写入失败,安全存储不可用(fail-closed,键:{Key})。");

    private static readonly Action<ILogger, string, Exception?> TicketConsumeFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(4, nameof(TicketConsumeFailed)),
            "SSO 登录票据消费失败,安全存储不可用(fail-closed,键:{Key})。");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDatabase _database;
    private readonly ILogger<SsoTicketStore> _logger;

    /// <summary>初始化票据存储。</summary>
    public SsoTicketStore(IConnectionMultiplexer connection, ILogger<SsoTicketStore> logger)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(logger);
        _database = connection.GetDatabase();
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task StoreOAuthStateAsync(string state, SsoOAuthState value, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var key = StatePrefix + state;
        try
        {
            await _database.StringSetAsync(key, JsonSerializer.Serialize(value, JsonOptions), ttl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StateStoreFailed(_logger, key, ex);
            throw new SsoProviderUnavailableException();
        }
    }

    /// <inheritdoc/>
    public async Task<SsoOAuthState?> ConsumeOAuthStateAsync(string state, CancellationToken cancellationToken)
    {
        var key = StatePrefix + state;
        try
        {
            var raw = await _database.StringGetDeleteAsync(key);
            if (raw.IsNullOrEmpty)
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<SsoOAuthState>((string)raw!, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StateConsumeFailed(_logger, key, ex);
            throw new SsoProviderUnavailableException();
        }
    }

    /// <inheritdoc/>
    public async Task StoreLoginTicketAsync(string ticket, SsoLoginTicket value, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var key = TicketPrefix + ticket;
        try
        {
            await _database.StringSetAsync(key, JsonSerializer.Serialize(value, JsonOptions), ttl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TicketStoreFailed(_logger, key, ex);
            throw new SsoProviderUnavailableException();
        }
    }

    /// <inheritdoc/>
    public async Task<SsoLoginTicket?> ConsumeLoginTicketAsync(string ticket, CancellationToken cancellationToken)
    {
        var key = TicketPrefix + ticket;
        try
        {
            var raw = await _database.StringGetDeleteAsync(key);
            if (raw.IsNullOrEmpty)
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<SsoLoginTicket>((string)raw!, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TicketConsumeFailed(_logger, key, ex);
            throw new SsoProviderUnavailableException();
        }
    }
}
