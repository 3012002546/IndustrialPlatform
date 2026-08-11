using System.Diagnostics;
using System.Security.Cryptography;
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
    private readonly IOptions<AuthenticationOptions> _options;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(
        IAuthenticationStore store,
        IPasswordHasher hasher,
        IAccessTokenFactory tokenFactory,
        IRefreshSessionStore refreshStore,
        ILoginRateLimiter rateLimiter,
        ILoginAuditSink auditSink,
        IOptions<AuthenticationOptions> options,
        ILogger<AuthenticationService> logger)
    {
        _store = store;
        _hasher = hasher;
        _tokenFactory = tokenFactory;
        _refreshStore = refreshStore;
        _rateLimiter = rateLimiter;
        _auditSink = auditSink;
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
                var expectedOptimistic = user.OptimisticVersion;
                var expectedConcurrency = user.ConcurrencyVersion;
                user.RecordLoginFailure(now, new LoginAttemptPolicy(options.MaxLoginFailures, options.LockDuration));
                await _store.UpdateUserAsync(user, expectedOptimistic, expectedConcurrency, cancellationToken);
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

        // 成功:清零失败计数并记录最近登录时间
        var successExpectedOptimistic = user.OptimisticVersion;
        var successExpectedConcurrency = user.ConcurrencyVersion;
        user.RecordLoginSuccess(now);
        await _store.UpdateUserAsync(user, successExpectedOptimistic, successExpectedConcurrency, cancellationToken);
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

    [LoggerMessage(EventId = 1001, Level = LogLevel.Error, Message = "刷新会话持久化失败,登录终止。")]
    private static partial void LogRefreshSessionPersistFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error, Message = "登录审计写入失败,已忽略。")]
    private static partial void LogAuditWriteFailed(ILogger logger, Exception ex);

    private static string? TraceId => Activity.Current?.TraceId.ToString();

    /// <summary>会话业务标识:字母开头,符合 NId 格式。</summary>
    private static string GenerateSessionNId() => "SES-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

    /// <summary>256 bit 随机不透明刷新令牌(base64url)。</summary>
    private static string GenerateRefreshToken() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
