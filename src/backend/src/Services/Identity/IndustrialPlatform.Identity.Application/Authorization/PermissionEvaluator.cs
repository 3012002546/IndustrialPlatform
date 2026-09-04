using System.Diagnostics;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Domain.Users;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.Identity.Application.Authorization;

/// <summary>
/// 权限评估器(§14/§18):撤销会话 fail-closed → 版本化缓存命中快速裁决 →
/// 数据库权威装载 → 回填缓存;缓存不可用降级数据库,授权数据不可用 fail-closed 503。
/// </summary>
public sealed partial class PermissionEvaluator : IPermissionEvaluator
{
    private readonly ISessionRevocationStore _sessionRevocation;
    private readonly IAuthorizationDataStore _dataStore;
    private readonly IPermissionCache _cache;
    private readonly IAuthorizationDenialSink _denialSink;
    private readonly ILogger<PermissionEvaluator> _logger;

    public PermissionEvaluator(
        ISessionRevocationStore sessionRevocation,
        IAuthorizationDataStore dataStore,
        IPermissionCache cache,
        IAuthorizationDenialSink denialSink,
        ILogger<PermissionEvaluator> logger)
    {
        _sessionRevocation = sessionRevocation;
        _dataStore = dataStore;
        _cache = cache;
        _denialSink = denialSink;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<PermissionEvaluation> EvaluateAsync(
        string tenantNId,
        string userNId,
        string? sessionNId,
        int authVersion,
        string requiredPermissionNId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantNId)
            || string.IsNullOrWhiteSpace(userNId)
            || string.IsNullOrWhiteSpace(requiredPermissionNId))
        {
            // 无用户身份可审计,直接拒绝。
            return new PermissionEvaluation(false, AuthorizationDenialReason.SessionInvalid);
        }

        var protectedInitializationPermission = AuthorizationPermissionPolicy.IsProtectedInitializationPermission(requiredPermissionNId);

        // 1. 会话撤销校验(fail-closed:Redis 不可用抛安全存储不可用,不得放行)。
        if (!string.IsNullOrEmpty(sessionNId)
            && await _sessionRevocation.IsRevokedAsync(sessionNId, cancellationToken))
        {
            return await DenyAndAuditAsync(tenantNId, userNId, sessionNId, requiredPermissionNId, AuthorizationDenialReason.SessionInvalid, cancellationToken);
        }

        // 2. 版本化权限缓存命中 → 快速裁决,不落数据库(§14)。
        // 保留权限缓存用于普通权限；初始化/数据库编排权限必须每次装载当前租户的
        // SYSTEM_ADMIN 事实，避免旧缓存或 JWT 权限声明在管理员被撤销后继续放行。
        var cached = protectedInitializationPermission
            ? null
            : await TryGetCachedAsync(tenantNId, userNId, authVersion, cancellationToken);
        if (cached is not null)
        {
            return cached.Status == UserStatus.Active
                ? await EvaluatePermissionAsync(cached.PermissionNIds, requiredPermissionNId, tenantNId, userNId, sessionNId, cancellationToken)
                : await DenyAndAuditAsync(tenantNId, userNId, sessionNId, requiredPermissionNId, AuthorizationDenialReason.AccountDisabled, cancellationToken);
        }

        // 3. 数据库权威装载;存储不可用 → fail-closed 503。
        AuthorizationSnapshot? snapshot;
        try
        {
            snapshot = await _dataStore.GetSnapshotAsync(tenantNId, userNId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAuthorizationDataUnavailable(_logger, ex);
            throw new SecurityStoreUnavailableException();
        }

        if (snapshot is null || snapshot.AuthVersion != authVersion)
        {
            return await DenyAndAuditAsync(tenantNId, userNId, sessionNId, requiredPermissionNId, AuthorizationDenialReason.SessionInvalid, cancellationToken);
        }

        if (snapshot.Status != UserStatus.Active)
        {
            return await DenyAndAuditAsync(tenantNId, userNId, sessionNId, requiredPermissionNId, AuthorizationDenialReason.AccountDisabled, cancellationToken);
        }

        // 4. 回填缓存(尽力而为)。
        if (protectedInitializationPermission && !snapshot.IsSystemAdmin)
        {
            return await DenyAndAuditAsync(
                tenantNId,
                userNId,
                sessionNId,
                requiredPermissionNId,
                AuthorizationDenialReason.MissingPermission,
                cancellationToken);
        }

        if (protectedInitializationPermission)
        {
            return new PermissionEvaluation(true, AuthorizationDenialReason.None);
        }

        await TryPopulateCacheAsync(snapshot, cancellationToken);

        return await EvaluatePermissionAsync(snapshot.PermissionNIds, requiredPermissionNId, tenantNId, userNId, sessionNId, cancellationToken);
    }

    private async Task<PermissionEvaluation> EvaluatePermissionAsync(
        IReadOnlyList<string> permissionNIds,
        string requiredPermissionNId,
        string tenantNId,
        string userNId,
        string? sessionNId,
        CancellationToken cancellationToken)
    {
        if (permissionNIds.Contains(requiredPermissionNId, StringComparer.Ordinal))
        {
            return new PermissionEvaluation(true, AuthorizationDenialReason.None);
        }

        return await DenyAndAuditAsync(tenantNId, userNId, sessionNId, requiredPermissionNId, AuthorizationDenialReason.MissingPermission, cancellationToken);
    }

    private async Task<CachedAuthorization?> TryGetCachedAsync(string tenantNId, string userNId, int authVersion, CancellationToken cancellationToken)
    {
        try
        {
            var cachedUser = await _cache.TryGetUserSnapshotAsync(tenantNId, userNId, authVersion, cancellationToken);
            if (cachedUser is null)
            {
                return null;
            }

            var permissions = await _cache.TryGetPermissionsAsync(tenantNId, userNId, authVersion, cancellationToken);
            return permissions is null ? null : new CachedAuthorization(cachedUser.Status, permissions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 缓存不可用降级数据库装载(§14)。
            LogPermissionCacheUnavailable(_logger, ex);
            return null;
        }
    }

    private async Task TryPopulateCacheAsync(AuthorizationSnapshot snapshot, CancellationToken cancellationToken)
    {
        try
        {
            await _cache.SetAsync(snapshot, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPermissionCacheWriteFailed(_logger, ex);
        }
    }

    private async Task<PermissionEvaluation> DenyAndAuditAsync(
        string tenantNId,
        string userNId,
        string? sessionNId,
        string requiredPermissionNId,
        AuthorizationDenialReason reason,
        CancellationToken cancellationToken)
    {
        try
        {
            await _denialSink.RecordDenialAsync(
                new AuthorizationDenial(tenantNId, userNId, sessionNId, requiredPermissionNId, reason, TraceId),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDenialAuditFailed(_logger, ex);
        }

        return new PermissionEvaluation(false, reason);
    }

    private sealed record CachedAuthorization(UserStatus Status, IReadOnlyList<string> PermissionNIds);

    private static string? TraceId => Activity.Current?.TraceId.ToString();

    [LoggerMessage(EventId = 3001, Level = LogLevel.Error, Message = "授权数据存储不可用,授权裁决 fail-closed(503)。")]
    private static partial void LogAuthorizationDataUnavailable(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning, Message = "权限缓存不可用,降级数据库装载。")]
    private static partial void LogPermissionCacheUnavailable(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning, Message = "权限缓存写入失败,已忽略。")]
    private static partial void LogPermissionCacheWriteFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Warning, Message = "拒绝审计写入失败,已忽略。")]
    private static partial void LogDenialAuditFailed(ILogger logger, Exception ex);
}
