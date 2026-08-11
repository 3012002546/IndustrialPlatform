using System.Text.Json;
using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Identity.Domain.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace IndustrialPlatform.Identity.Infrastructure.Authentication;

/// <summary>
/// 权限缓存存储实现(§14):版本化键
/// <c>identity:user:{tenantNId}:{userNId}:v{authVersion}</c> 与
/// <c>identity:permission:{tenantNId}:{userNId}:v{authVersion}</c>。
/// 缓存自愈:Redis 不可用时读取返回 <c>null</c>、写入/失效静默忽略(降级数据库,TTL 收敛)。
/// </summary>
public sealed partial class PermissionCacheStore : IPermissionCache
{
    private readonly IDatabase _database;
    private readonly IOptions<AuthorizationOptions> _options;
    private readonly ILogger<PermissionCacheStore> _logger;

    /// <summary>初始化权限缓存存储。</summary>
    public PermissionCacheStore(
        IConnectionMultiplexer connection,
        IOptions<AuthorizationOptions> options,
        ILogger<PermissionCacheStore> logger)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _database = connection.GetDatabase();
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<UserSecurityCacheEntry?> TryGetUserSnapshotAsync(
        string tenantNId,
        string userNId,
        int authVersion,
        CancellationToken cancellationToken)
    {
        var key = UserKey(tenantNId, userNId, authVersion);
        try
        {
            var value = await _database.StringGetAsync(key);
            if (value.IsNullOrEmpty || !Enum.TryParse<UserStatus>(value.ToString(), out var status))
            {
                return null;
            }

            return new UserSecurityCacheEntry(status);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCacheReadFailed(_logger, key, ex);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>?> TryGetPermissionsAsync(
        string tenantNId,
        string userNId,
        int authVersion,
        CancellationToken cancellationToken)
    {
        var key = PermissionKey(tenantNId, userNId, authVersion);
        try
        {
            var value = await _database.StringGetAsync(key);
            if (value.IsNullOrEmpty)
            {
                return null;
            }

            return JsonSerializer.Deserialize<List<string>>(value.ToString()) ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCacheReadFailed(_logger, key, ex);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task SetAsync(AuthorizationSnapshot snapshot, CancellationToken cancellationToken)
    {
        var userKey = UserKey(snapshot.TenantNId, snapshot.UserNId, snapshot.AuthVersion);
        var permissionKey = PermissionKey(snapshot.TenantNId, snapshot.UserNId, snapshot.AuthVersion);
        var ttl = _options.Value.PermissionCacheTtl;
        try
        {
            await _database.StringSetAsync(userKey, snapshot.Status.ToString(), ttl);
            await _database.StringSetAsync(permissionKey, JsonSerializer.Serialize(snapshot.PermissionNIds), ttl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCacheWriteFailed(_logger, userKey, ex);
        }
    }

    /// <inheritdoc/>
    public async Task InvalidateAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
    {
        // Redis 不可达时跳过 SCAN,避免阻塞;旧版本键由 TTL 收敛(版本隔离保证无陈旧命中)。
        if (!_database.Multiplexer.IsConnected)
        {
            LogInvalidateSkippedUnavailable(_logger, tenantNId, userNId);
            return;
        }

        var server = _database.Multiplexer.GetServer(_database.Multiplexer.GetEndPoints().First());
        try
        {
            foreach (var prefix in new[] { UserPrefix(tenantNId, userNId), PermissionPrefix(tenantNId, userNId) })
            {
                await foreach (var key in server.KeysAsync(database: _database.Database, pattern: prefix + ":v*", pageSize: 100))
                {
                    await _database.KeyDeleteAsync(key);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogInvalidateFailed(_logger, tenantNId, userNId, ex);
        }
    }

    private static string UserPrefix(string tenantNId, string userNId) => $"identity:user:{EscapePattern(tenantNId)}:{EscapePattern(userNId)}";

    private static string PermissionPrefix(string tenantNId, string userNId) => $"identity:permission:{EscapePattern(tenantNId)}:{EscapePattern(userNId)}";

    private static string UserKey(string tenantNId, string userNId, int authVersion) => $"{UserPrefix(tenantNId, userNId)}:v{authVersion}";

    private static string PermissionKey(string tenantNId, string userNId, int authVersion) => $"{PermissionPrefix(tenantNId, userNId)}:v{authVersion}";

    /// <summary>转义 SCAN glob 元字符,防止租户/用户标识含特殊字符时误匹配其他键。</summary>
    private static string EscapePattern(string value) =>
        value.Replace(@"\", @"\\").Replace("*", @"\*").Replace("?", @"\?").Replace("[", @"\[").Replace("]", @"\]");

    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning, Message = "权限缓存读取失败,降级数据库装载(键:{Key})。")]
    private static partial void LogCacheReadFailed(ILogger logger, string key, Exception ex);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Warning, Message = "权限缓存写入失败,已忽略(键:{Key})。")]
    private static partial void LogCacheWriteFailed(ILogger logger, string key, Exception ex);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Warning, Message = "权限缓存失效跳过:Redis 不可达(租户:{TenantNId} 用户:{UserNId})。")]
    private static partial void LogInvalidateSkippedUnavailable(ILogger logger, string tenantNId, string userNId);

    [LoggerMessage(EventId = 4004, Level = LogLevel.Warning, Message = "权限缓存失效失败,已忽略(TTL 收敛;租户:{TenantNId} 用户:{UserNId})。")]
    private static partial void LogInvalidateFailed(ILogger logger, string tenantNId, string userNId, Exception ex);
}
