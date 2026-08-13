using System.Diagnostics;
using System.Security.Cryptography;
using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Identity.Contracts.Authentication;
using IndustrialPlatform.Identity.Domain.LoginSecurity;
using IndustrialPlatform.Identity.Domain.Passwords;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.SharedKernel.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.Identity.Application.Authentication;

/// <summary>
/// 登录与当前用户用例(§10.3/§12/§13/§19.1)。编排认证端口与领域登录方法;
/// 错误统一映射为 <see cref="AuthenticationException"/>,密码/Token/内部哈希/用户是否存在绝不出现在消息。
/// </summary>
public sealed partial class AuthenticationService : IAuthenticationService
{
    private readonly IAuthenticationStore _store;
    private readonly IPasswordHasher _hasher;
    private readonly IAccessTokenFactory _tokenFactory;
    private readonly IRefreshSessionStore _refreshStore;
    private readonly ILoginRateLimiter _rateLimiter;
    private readonly ILoginAuditSink _auditSink;
    private readonly ISessionRevocationStore _sessionRevocation;
    private readonly IPermissionCache _permissionCache;
    private readonly IOptions<AuthenticationOptions> _options;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(
        IAuthenticationStore store,
        IPasswordHasher hasher,
        IAccessTokenFactory tokenFactory,
        IRefreshSessionStore refreshStore,
        ILoginRateLimiter rateLimiter,
        ILoginAuditSink auditSink,
        ISessionRevocationStore sessionRevocation,
        IPermissionCache permissionCache,
        IOptions<AuthenticationOptions> options,
        ILogger<AuthenticationService> logger)
    {
        _store = store;
        _hasher = hasher;
        _tokenFactory = tokenFactory;
        _refreshStore = refreshStore;
        _rateLimiter = rateLimiter;
        _auditSink = auditSink;
        _sessionRevocation = sessionRevocation;
        _permissionCache = permissionCache;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<AuthSession> LoginAsync(
        LoginRequest request,
        string? clientIp,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var loginName = request.LoginName?.Trim();
        var password = request.Password;
        if (string.IsNullOrWhiteSpace(loginName))
        {
            throw new ValidationException("登录名不能为空。");
        }

        if (string.IsNullOrEmpty(password))
        {
            throw new ValidationException("密码不能为空。");
        }

        var options = _options.Value;
        var now = DateTimeOffset.UtcNow;
        var tenantNId = options.DefaultTenantNId;
        var normalizedLoginName = loginName.ToUpperInvariant();

        // 按 IP 限流(Redis,不可用降级)
        if (await _rateLimiter.IsIpRateLimitedAsync(clientIp, cancellationToken))
        {
            await TryWriteAuditAsync(
                new LoginAuditEntry(tenantNId, null, loginName, false, "rate_limited", clientIp, userAgent, TraceId, now),
                cancellationToken);
            throw new RateLimitExceededException();
        }

        var account = await _store.FindByNormalizedLoginNameAsync(tenantNId, normalizedLoginName, cancellationToken);
        var user = account?.User;

        // 不存在用户与错误密码返回相同外部错误(§10.3 防枚举)
        if (user is null || !_hasher.Verify(user.PasswordHash, password))
        {
            if (user is not null)
            {
                // 失败计数登记为尽力而为:并发失败登录的登记冲突不阻断返回 401(防枚举)。
                await RecordLoginFailureAsync(user, now, options, cancellationToken);
            }

            await _rateLimiter.RecordLoginFailureAsync(tenantNId, normalizedLoginName, clientIp, cancellationToken);
            await TryWriteAuditAsync(
                new LoginAuditEntry(tenantNId, user?.NId, loginName, false, "invalid_credentials", clientIp, userAgent, TraceId, now),
                cancellationToken);
            throw new InvalidCredentialsException();
        }

        if (user!.IsDeleted || user.Status == UserStatus.Disabled)
        {
            await TryWriteAuditAsync(
                new LoginAuditEntry(tenantNId, user.NId, loginName, false, "account_disabled", clientIp, userAgent, TraceId, now),
                cancellationToken);
            throw new AccountDisabledException();
        }

        // 临时锁定:用户级持久锁 或 Redis 组合锁
        if ((user.LockedUntil is { } lockedUntil && lockedUntil > now)
            || await _rateLimiter.IsAccountLockedAsync(tenantNId, normalizedLoginName, clientIp, cancellationToken))
        {
            await TryWriteAuditAsync(
                new LoginAuditEntry(tenantNId, user.NId, loginName, false, "account_locked", clientIp, userAgent, TraceId, now),
                cancellationToken);
            throw new RateLimitExceededException("登录失败次数过多，账号已临时锁定，请稍后再试。");
        }

        // 成功:清零失败计数并记录最近登录时间。
        // 并发登录同一账号会触发双版本乐观并发冲突;重新装载后重试,连续冲突时降级为放弃登记,
        // 登录本身不受影响,避免把并发登录整体 500 化。
        await RecordLoginSuccessAsync(user, now, cancellationToken);
        await _rateLimiter.RecordLoginSuccessAsync(tenantNId, normalizedLoginName, clientIp, cancellationToken);

        // 刷新会话:只存哈希;持久化失败 → 安全存储不可用,不返回令牌
        var sessionNId = GenerateSessionNId();
        var rawRefreshToken = GenerateRefreshToken();
        try
        {
            await _refreshStore.AddAsync(
                new NewRefreshSession(
                    tenantNId,
                    sessionNId,
                    sessionNId,
                    user.Id,
                    rawRefreshToken,
                    now.AddDays(options.RefreshTokenLifetimeDays),
                    clientIp,
                    userAgent,
                    now),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRefreshSessionPersistFailed(_logger, ex);
            throw new SecurityStoreUnavailableException();
        }

        var accessToken = _tokenFactory.Create(new AccessTokenDescriptor(
            user.NId,
            user.LoginName,
            user.TenantNId,
            account!.RoleNIds,
            sessionNId,
            user.AuthVersion,
            now));

        var authUser = await BuildAuthUserAsync(account, cancellationToken);

        await TryWriteAuditAsync(
            new LoginAuditEntry(tenantNId, user.NId, loginName, true, null, clientIp, userAgent, TraceId, now),
            cancellationToken);

        return new AuthSession(accessToken.Token, rawRefreshToken, accessToken.ExpiresAt, authUser);
    }

    /// <inheritdoc/>
    public async Task<AuthUser> GetCurrentUserAsync(string userNId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userNId))
        {
            throw new SessionInvalidException();
        }

        var account = await _store.FindByNIdAsync(userNId, cancellationToken);
        if (account is null)
        {
            throw new SessionInvalidException();
        }

        return await BuildAuthUserAsync(account, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<AuthSession> RefreshAsync(
        RefreshRequest request,
        string? clientIp,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw new ValidationException("刷新令牌不能为空。");
        }

        var now = DateTimeOffset.UtcNow;
        var session = await _refreshStore.FindByRawTokenAsync(request.RefreshToken, cancellationToken);
        if (session is null)
        {
            throw new RefreshTokenInvalidException();
        }

        // Redis 会话撤销校验(fail-closed:Redis 不可用抛安全存储不可用,不得放行)
        if (await _sessionRevocation.IsRevokedAsync(session.NId, cancellationToken))
        {
            throw new RefreshTokenInvalidException();
        }

        // 已使用过(顺序重放):撤销整个 Family,拒绝复用
        if (session.UsedOn is not null)
        {
            await _refreshStore.RevokeFamilyAsync(session.FamilyNId, "replay", cancellationToken);
            throw new RefreshTokenReusedException();
        }

        if (session.RevokedOn is not null || session.ReplacedBySessionNId is not null)
        {
            throw new RefreshTokenInvalidException();
        }

        if (session.ExpiresOn <= now)
        {
            throw new RefreshTokenInvalidException();
        }

        var account = await _store.FindByUserIdAsync(session.UserId, cancellationToken);
        var user = account?.User;
        if (user is null || user.IsDeleted || user.Status == UserStatus.Disabled)
        {
            throw new RefreshTokenInvalidException();
        }

        // 旋转:同 Family 生成新会话;并发/顺序重用由原子守卫兜底
        var newSessionNId = GenerateSessionNId();
        var newRawRefresh = GenerateRefreshToken();
        var status = await _refreshStore.RotateAsync(
            session.Id,
            new NewRefreshSession(
                session.TenantNId,
                newSessionNId,
                session.FamilyNId,
                user.Id,
                newRawRefresh,
                now.AddDays(_options.Value.RefreshTokenLifetimeDays),
                clientIp,
                userAgent,
                now),
            cancellationToken);

        if (status == RefreshRotationStatus.Reused)
        {
            await _refreshStore.RevokeFamilyAsync(session.FamilyNId, "replay", cancellationToken);
            throw new RefreshTokenReusedException();
        }

        if (status != RefreshRotationStatus.Rotated)
        {
            throw new RefreshTokenInvalidException();
        }

        var accessToken = _tokenFactory.Create(new AccessTokenDescriptor(
            user.NId,
            user.LoginName,
            user.TenantNId,
            account!.RoleNIds,
            newSessionNId,
            user.AuthVersion,
            now));

        var authUser = await BuildAuthUserAsync(account, cancellationToken);
        return new AuthSession(accessToken.Token, newRawRefresh, accessToken.ExpiresAt, authUser);
    }

    /// <inheritdoc/>
    public async Task LogoutAsync(
        LogoutRequest request,
        string? sessionNId,
        TimeSpan sidRevocationTtl,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 重复注销保持幂等,不暴露 Refresh Token 是否曾存在
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            var session = await _refreshStore.FindByRawTokenAsync(request.RefreshToken, cancellationToken);
            if (session is not null)
            {
                await _refreshStore.RevokeFamilyAsync(session.FamilyNId, "logout", cancellationToken);
            }
        }

        // sid 撤销直到 Access Token 到期;Redis 写失败由存储告警,数据库撤销为权威
        if (!string.IsNullOrWhiteSpace(sessionNId))
        {
            await _sessionRevocation.RevokeAsync(sessionNId, sidRevocationTtl, cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task LogoutAllAsync(string userNId, CancellationToken cancellationToken)
    {
        var account = await RequireCurrentUserAsync(userNId, cancellationToken);
        var user = account.User;

        var expectedOptimistic = user.OptimisticVersion;
        var expectedConcurrency = user.ConcurrencyVersion;
        user.IncrementAuthVersion();
        await _store.UpdateUserAsync(user, expectedOptimistic, expectedConcurrency, cancellationToken);

        await _refreshStore.RevokeAllForUserAsync(user.Id, "logout_all", cancellationToken);

        await TryInvalidatePermissionCacheAsync(user, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task ChangePasswordAsync(ChangePasswordRequest request, string userNId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(request.CurrentPassword))
        {
            throw new ValidationException("当前密码不能为空。");
        }

        var account = await RequireCurrentUserAsync(userNId, cancellationToken);
        var user = account.User;

        // Validate 保证非空后才可空感知
        PasswordPolicy.Validate(request.NewPassword, user.LoginName, user.NId);
        var newPassword = request.NewPassword!;

        if (!_hasher.Verify(user.PasswordHash, request.CurrentPassword))
        {
            throw new InvalidCredentialsException();
        }

        var expectedOptimistic = user.OptimisticVersion;
        var expectedConcurrency = user.ConcurrencyVersion;
        user.ChangePasswordHash(_hasher.Hash(newPassword));
        await _store.UpdateUserAsync(user, expectedOptimistic, expectedConcurrency, cancellationToken);

        await _refreshStore.RevokeAllForUserAsync(user.Id, "password_changed", cancellationToken);

        await TryInvalidatePermissionCacheAsync(user, cancellationToken);
    }

    private async Task<AuthenticatedUser> RequireCurrentUserAsync(string userNId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userNId))
        {
            throw new SessionInvalidException();
        }

        var account = await _store.FindByNIdAsync(userNId, cancellationToken);
        if (account is null || account.User.IsDeleted || account.User.Status == UserStatus.Disabled)
        {
            throw new SessionInvalidException();
        }

        return account;
    }

    private async Task<AuthUser> BuildAuthUserAsync(AuthenticatedUser account, CancellationToken cancellationToken)
    {
        var permissions = await _store.GetPermissionsForRolesAsync(account.RoleIds, cancellationToken);
        var permissionNIds = permissions
            .Select(p => p.NId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        return new AuthUser(
            account.User.NId,
            account.User.LoginName,
            account.User.Name,
            account.User.TenantNId,
            account.RoleNIds,
            permissionNIds);
    }

    private async Task TryWriteAuditAsync(LoginAuditEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await _auditSink.WriteAsync(entry, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAuditWriteFailed(_logger, ex);
        }
    }

    /// <summary>权限缓存失效(尽力而为):数据库已提交新安全版本,删除旧版本缓存键,TTL 兜底收敛。</summary>
    private async Task TryInvalidatePermissionCacheAsync(User user, CancellationToken cancellationToken)
    {
        try
        {
            await _permissionCache.InvalidateAsync(user.TenantNId, user.NId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPermissionCacheInvalidateFailed(_logger, ex);
        }
    }

    /// <summary>登录状态登记的最大重试次数(并发冲突时重新装载用户后重试)。</summary>
    private const int LoginStateChangeRetryLimit = 3;

    /// <summary>
    /// 成功登录登记:清零失败计数与临时锁定并记录最近登录时间。
    /// 并发登录同一账号时双版本更新可能冲突;重新装载用户后重试,
    /// 连续冲突时降级为放弃本次登记(登录本身不受影响),避免把并发登录整体 500 化。
    /// </summary>
    private async Task RecordLoginSuccessAsync(User user, DateTimeOffset now, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var expectedOptimistic = user.OptimisticVersion;
            var expectedConcurrency = user.ConcurrencyVersion;
            user.RecordLoginSuccess(now);

            try
            {
                await _store.UpdateUserAsync(user, expectedOptimistic, expectedConcurrency, cancellationToken);
                return;
            }
            catch (ConcurrencyException) when (attempt < LoginStateChangeRetryLimit)
            {
                var refreshed = await _store.FindByUserIdAsync(user.Id, cancellationToken);
                if (refreshed is null || refreshed.User.IsDeleted || refreshed.User.Status == UserStatus.Disabled)
                {
                    throw new AccountDisabledException();
                }

                user = refreshed.User;
            }
            catch (ConcurrencyException)
            {
                LogLoginStateChangeSkipped(_logger, user.NId);
                return;
            }
        }
    }

    /// <summary>
    /// 失败登录登记(尽力而为):递增连续失败计数、必要时触发临时锁定。
    /// 并发失败登录的登记冲突时重新装载用户后重试,连续冲突降级为放弃登记;
    /// 无论如何不得把登记失败转化为 500(登录失败本身由外层统一返回 401 防枚举)。
    /// </summary>
    private async Task RecordLoginFailureAsync(
        User user,
        DateTimeOffset now,
        AuthenticationOptions options,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var expectedOptimistic = user.OptimisticVersion;
            var expectedConcurrency = user.ConcurrencyVersion;
            user.RecordLoginFailure(now, new LoginAttemptPolicy(options.MaxLoginFailures, options.LockDuration));

            try
            {
                await _store.UpdateUserAsync(user, expectedOptimistic, expectedConcurrency, cancellationToken);
                return;
            }
            catch (ConcurrencyException) when (attempt < LoginStateChangeRetryLimit)
            {
                var refreshed = await _store.FindByUserIdAsync(user.Id, cancellationToken);
                if (refreshed is null || refreshed.User.IsDeleted)
                {
                    return;
                }

                user = refreshed.User;
            }
            catch (ConcurrencyException)
            {
                LogLoginStateChangeSkipped(_logger, user.NId);
                return;
            }
        }
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Error, Message = "刷新会话持久化失败,登录终止。")]
    private static partial void LogRefreshSessionPersistFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error, Message = "登录审计写入失败,已忽略。")]
    private static partial void LogAuditWriteFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "权限缓存失效失败,已忽略(TTL 兜底)。")]
    private static partial void LogPermissionCacheInvalidateFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning, Message = "登录状态登记并发冲突,已降级放弃本次登记。用户: {userNId}")]
    private static partial void LogLoginStateChangeSkipped(ILogger logger, string userNId);

    private static string? TraceId => Activity.Current?.TraceId.ToString();

    /// <summary>会话业务标识:字母开头,符合 NId 格式。</summary>
    private static string GenerateSessionNId() => "SES-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

    /// <summary>256 bit 随机不透明刷新令牌(base64url)。</summary>
    private static string GenerateRefreshToken() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
