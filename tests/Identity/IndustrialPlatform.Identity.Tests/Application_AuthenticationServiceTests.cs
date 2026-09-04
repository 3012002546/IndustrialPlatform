using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Identity.Contracts.Authentication;
using IndustrialPlatform.Identity.Domain.LoginSecurity;
using IndustrialPlatform.Identity.Domain.Passwords;
using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.SharedKernel.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IndustrialPlatform.Identity.Application.Tests;

/// <summary>
/// AuthenticationService 用例测试(§10.3/§12/§13/§15.2/§17)。
/// 使用假端口隔离,覆盖正确登录、错误/不存在/禁用/锁定/限流、审计与安全存储不可用。
/// </summary>
public class AuthenticationServiceTests
{
    private const string Tenant = "development";
    private const string Password = "Passw0rd!";

    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly string RoleNId = "role.operator";

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsAuthSessionWithTokensAndUser()
    {
        // Arrange
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var store = new FakeStore();
        store.ByLoginName[(Tenant, "ALICE")] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        store.Permissions = [Permission.Create("perm.view", "查看", PermissionType.Menu, null, null)];
        var hasher = new FakeHasher(Password);
        var tokens = new FakeTokenFactory();
        var refresh = new FakeRefreshStore();
        var limiter = new FakeRateLimiter();
        var audit = new FakeAuditSink();
        var service = CreateService(store, hasher, tokens, refresh, limiter, audit);

        // Act
        var result = await service.LoginAsync(
            new LoginRequest(" alice ", Password),
            "10.0.0.1",
            "test-agent",
            CancellationToken.None);

        // Assert: 会话契约
        Assert.Equal("jwt-token", result.AccessToken);
        Assert.Equal(tokens.ExpiresAt, result.ExpiresAt);
        Assert.False(string.IsNullOrWhiteSpace(result.RefreshToken));
        Assert.Equal("user.alice", result.User.UserNId);
        Assert.Equal("alice", result.User.LoginName);
        Assert.Equal("Alice", result.User.Name);
        Assert.Equal(Tenant, result.User.TenantNId);
        Assert.Equal([RoleNId], result.User.RoleNIds);
        Assert.Equal(["perm.view"], result.User.PermissionNIds);

        // Assert: descriptor claims(§12:sub=UserNId、role、sid、ver,不含 Guid)
        Assert.NotNull(tokens.Descriptor);
        Assert.Equal("user.alice", tokens.Descriptor.Subject);
        Assert.Equal("alice", tokens.Descriptor.UserName);
        Assert.Equal(Tenant, tokens.Descriptor.TenantNId);
        Assert.Equal([RoleNId], tokens.Descriptor.Roles);
        Assert.StartsWith("SES-", tokens.Descriptor.SessionId);
        Assert.Equal(user.AuthVersion, tokens.Descriptor.AuthVersion);

        // Assert: 成功清零失败计数并写成功审计
        Assert.Equal(0, user.FailedLoginCount);
        Assert.Single(audit.Entries, e => e.Success);
        Assert.Contains(refresh.Sessions, s => s.UserId == user.Id && s.RawToken == result.RefreshToken);
    }

    [Fact]
    public async Task LoginAsync_FiltersProtectedInitializationPermissionsForOrdinaryRole()
    {
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var store = new FakeStore();
        store.ByLoginName[(Tenant, "ALICE")] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        store.Permissions =
        [
            Permission.Create("perm.view", "查看", PermissionType.Menu, null, null),
            Permission.Create("systemdata.database-orchestration.apply", "编排", PermissionType.Action, null, null),
        ];
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), new FakeRefreshStore(), new FakeRateLimiter(), new FakeAuditSink());

        var result = await service.LoginAsync(new LoginRequest("alice", Password), null, null, CancellationToken.None);

        Assert.Equal(["perm.view"], result.User.PermissionNIds);
    }

    [Fact]
    public async Task LoginAsync_PreservesProtectedInitializationPermissionsForSystemAdmin()
    {
        var user = User.Create(Tenant, "user.admin", "admin", "Admin", null, null, "hash-1");
        var store = new FakeStore();
        store.ByLoginName[(Tenant, "ADMIN")] = new AuthenticatedUser(user, [RoleId], ["SYSTEM_ADMIN"], IsSystemAdmin: true);
        store.Permissions =
        [
            Permission.Create("perm.view", "查看", PermissionType.Menu, null, null),
            Permission.Create("systemdata.database-orchestration.apply", "编排", PermissionType.Action, null, null),
        ];
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), new FakeRefreshStore(), new FakeRateLimiter(), new FakeAuditSink());

        var result = await service.LoginAsync(new LoginRequest("admin", Password), null, null, CancellationToken.None);

        Assert.Equal(["perm.view", "systemdata.database-orchestration.apply"], result.User.PermissionNIds);
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_PassesOriginalDoubleVersionToUpdate()
    {
        // Arrange
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var originalOptimistic = user.OptimisticVersion;
        var originalConcurrency = user.ConcurrencyVersion;
        var store = new FakeStore();
        store.ByLoginName[(Tenant, "ALICE")] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), new FakeRefreshStore(), new FakeRateLimiter(), new FakeAuditSink());

        // Act
        await service.LoginAsync(new LoginRequest("alice", Password), null, null, CancellationToken.None);

        // Assert: UpdateUserAsync 收到的是读取时保存的原始双版本
        var (_, expectedOptimistic, expectedConcurrency, _) = store.LastUpdate;
        Assert.Equal(originalOptimistic, expectedOptimistic);
        Assert.Equal(originalConcurrency, expectedConcurrency);
    }

    [Fact]
    public async Task LoginAsync_SuccessUpdateConflict_RetriesAndStillReturnsSession()
    {
        // Arrange: 并发登录同一账号时,第一次成功登记的双版本更新冲突,重新装载后重试成功
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var store = new FakeStore { ConcurrentFailuresRemaining = 1 };
        store.ByLoginName[(Tenant, "ALICE")] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        store.ByUserId[user.Id] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), new FakeRefreshStore(), new FakeRateLimiter(), new FakeAuditSink());

        // Act
        var result = await service.LoginAsync(new LoginRequest("alice", Password), null, null, CancellationToken.None);

        // Assert: 冲突被重试消化,登录成功而非 500;成功登记最终清零失败计数
        Assert.Equal("jwt-token", result.AccessToken);
        Assert.Equal(2, store.UpdateCount);
        Assert.Equal(0, user.FailedLoginCount);
    }

    [Fact]
    public async Task LoginAsync_FailureUpdateConflict_StillReturnsInvalidCredentials()
    {
        // Arrange: 并发失败登录时,失败计数登记冲突,重试后仍统一返回 401 防枚举(不得 500)
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var store = new FakeStore { ConcurrentFailuresRemaining = 1 };
        store.ByLoginName[(Tenant, "ALICE")] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        store.ByUserId[user.Id] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), new FakeRefreshStore(), new FakeRateLimiter(), new FakeAuditSink());

        // Act
        var ex = await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            service.LoginAsync(new LoginRequest("alice", "wrong-password"), null, null, CancellationToken.None));

        // Assert: 登记冲突被重试消化,仍返回 401
        Assert.Equal("ID_AUTH_INVALID_CREDENTIALS", ex.Code);
        Assert.Equal(2, store.UpdateCount);
    }

    [Theory]
    [InlineData("alice", "wrong-password")]
    [InlineData("ghost", "Passw0rd!")]
    public async Task LoginAsync_WrongCredentialsOrUnknownUser_ThrowsInvalidCredentials(string loginName, string password)
    {
        // Arrange
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var store = new FakeStore();
        store.ByLoginName[(Tenant, "ALICE")] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var audit = new FakeAuditSink();
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), new FakeRefreshStore(), new FakeRateLimiter(), audit);

        // Act
        var ex = await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            service.LoginAsync(new LoginRequest(loginName, password), null, null, CancellationToken.None));

        // Assert: 外部错误一致(防枚举)、不含内部信息
        Assert.Equal("ID_AUTH_INVALID_CREDENTIALS", ex.Code);
        Assert.Equal(401, ex.StatusCode);
        Assert.Equal("用户名或密码错误。", ex.Message);

        var failedAudit = Assert.Single(audit.Entries, e => !e.Success);
        Assert.Equal("invalid_credentials", failedAudit.FailureCode);
        if (loginName == "alice")
        {
            Assert.Equal("user.alice", failedAudit.UserNId);
            Assert.Equal(1, user.FailedLoginCount);
            Assert.Equal(1, store.UpdateCount);
        }
        else
        {
            Assert.Null(failedAudit.UserNId);
            Assert.Equal(0, store.UpdateCount);
        }

        Assert.Single(audit.Entries);
    }

    [Fact]
    public async Task LoginAsync_UserLockedByPersistedState_ThrowsRateLimitExceeded()
    {
        // Arrange
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        user.RecordLoginFailure(DateTimeOffset.UtcNow.AddMinutes(-1), new LoginAttemptPolicy(1, TimeSpan.FromMinutes(15)));
        Assert.True(user.LockedUntil > DateTimeOffset.UtcNow);

        var store = new FakeStore();
        store.ByLoginName[(Tenant, "ALICE")] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var audit = new FakeAuditSink();
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), new FakeRefreshStore(), new FakeRateLimiter(), audit);

        // Act
        var ex = await Assert.ThrowsAsync<RateLimitExceededException>(() =>
            service.LoginAsync(new LoginRequest("alice", Password), null, null, CancellationToken.None));

        Assert.Equal("ID_AUTH_RATE_LIMITED", ex.Code);
        Assert.Equal(429, ex.StatusCode);
        Assert.Equal("account_locked", Assert.Single(audit.Entries).FailureCode);
    }

    [Fact]
    public async Task LoginAsync_ComboLockActive_ThrowsRateLimitExceeded()
    {
        // Arrange
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var store = new FakeStore();
        store.ByLoginName[(Tenant, "ALICE")] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var limiter = new FakeRateLimiter { AccountLocked = true };
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), new FakeRefreshStore(), limiter, new FakeAuditSink());

        // Act
        var ex = await Assert.ThrowsAsync<RateLimitExceededException>(() =>
            service.LoginAsync(new LoginRequest("alice", Password), "10.0.0.1", null, CancellationToken.None));

        Assert.Equal("ID_AUTH_RATE_LIMITED", ex.Code);
        Assert.True(limiter.AccountLockedChecks > 0);
    }

    [Fact]
    public async Task LoginAsync_IpRateLimited_ThrowsRateLimitExceededBeforeCredentialCheck()
    {
        // Arrange
        var store = new FakeStore();
        var limiter = new FakeRateLimiter { IpRateLimited = true };
        var audit = new FakeAuditSink();
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), new FakeRefreshStore(), limiter, audit);

        // Act
        var ex = await Assert.ThrowsAsync<RateLimitExceededException>(() =>
            service.LoginAsync(new LoginRequest("alice", Password), "10.0.0.1", null, CancellationToken.None));

        Assert.Equal(429, ex.StatusCode);
        Assert.Equal(0, store.FindByLoginNameCount);
        Assert.Equal("rate_limited", Assert.Single(audit.Entries).FailureCode);
        Assert.Null(Assert.Single(audit.Entries).UserNId);
    }

    [Fact]
    public async Task LoginAsync_DisabledUser_ThrowsAccountDisabled()
    {
        // Arrange
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        user.Disable();
        var store = new FakeStore();
        store.ByLoginName[(Tenant, "ALICE")] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var audit = new FakeAuditSink();
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), new FakeRefreshStore(), new FakeRateLimiter(), audit);

        // Act
        var ex = await Assert.ThrowsAsync<AccountDisabledException>(() =>
            service.LoginAsync(new LoginRequest("alice", Password), null, null, CancellationToken.None));

        Assert.Equal("ID_AUTH_ACCOUNT_DISABLED", ex.Code);
        Assert.Equal(403, ex.StatusCode);
        Assert.Equal("account_disabled", Assert.Single(audit.Entries).FailureCode);
        Assert.Equal(0, store.UpdateCount);
    }

    [Fact]
    public async Task LoginAsync_AuditSinkFailure_DoesNotBlockSuccessfulLogin()
    {
        // Arrange
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var store = new FakeStore();
        store.ByLoginName[(Tenant, "ALICE")] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var audit = new FakeAuditSink { ThrowOnWrite = true };
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), new FakeRefreshStore(), new FakeRateLimiter(), audit);

        // Act
        var result = await service.LoginAsync(new LoginRequest("alice", Password), null, null, CancellationToken.None);

        // Assert: 审计失败不阻断登录(§19 best-effort)
        Assert.Equal("jwt-token", result.AccessToken);
    }

    [Fact]
    public async Task LoginAsync_RefreshSessionStoreUnavailable_ThrowsSecurityStoreUnavailable()
    {
        // Arrange
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var store = new FakeStore();
        store.ByLoginName[(Tenant, "ALICE")] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var refresh = new FakeRefreshStore { ThrowOnAdd = true };
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), refresh, new FakeRateLimiter(), new FakeAuditSink());

        // Act
        var ex = await Assert.ThrowsAsync<SecurityStoreUnavailableException>(() =>
            service.LoginAsync(new LoginRequest("alice", Password), null, null, CancellationToken.None));

        Assert.Equal("ID_AUTH_SECURITY_STORE_UNAVAILABLE", ex.Code);
        Assert.Equal(503, ex.StatusCode);
    }

    [Theory]
    [InlineData(null, "Passw0rd!")]
    [InlineData("   ", "Passw0rd!")]
    [InlineData("alice", null)]
    [InlineData("alice", "")]
    public async Task LoginAsync_InvalidRequest_ThrowsValidationException(string? loginName, string? password)
    {
        // Arrange
        var service = CreateService(new FakeStore(), new FakeHasher(Password), new FakeTokenFactory(), new FakeRefreshStore(), new FakeRateLimiter(), new FakeAuditSink());

        // Act
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.LoginAsync(new LoginRequest(loginName, password), null, null, CancellationToken.None));
    }

    [Fact]
    public async Task LoginAsync_Success_StoresRefreshSessionWithExpiry()
    {
        // Arrange
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var store = new FakeStore();
        store.ByLoginName[(Tenant, "ALICE")] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var refresh = new FakeRefreshStore();
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), refresh, new FakeRateLimiter(), new FakeAuditSink());

        // Act
        var result = await service.LoginAsync(new LoginRequest("alice", Password), "10.0.0.1", "test-agent", CancellationToken.None);

        // Assert: §13 会话契约(family=NId,有效期 7 天,userId 关联,不含租户以外的明文)
        var session = Assert.Single(refresh.Sessions);
        Assert.Equal(Tenant, session.TenantNId);
        Assert.Equal(session.NId, session.FamilyNId);
        Assert.Equal(user.Id, session.UserId);
        Assert.Equal(result.RefreshToken, session.RawToken);
        Assert.Equal("10.0.0.1", session.IpAddress);
        Assert.Equal("test-agent", session.UserAgent);
        Assert.True(session.ExpiresOn > DateTimeOffset.UtcNow.AddDays(6) && session.ExpiresOn <= DateTimeOffset.UtcNow.AddDays(7));
    }

    [Fact]
    public async Task GetCurrentUserAsync_ValidUser_ReturnsAuthUser()
    {
        // Arrange
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var store = new FakeStore();
        store.ByNId["user.alice"] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        store.Permissions = [Permission.Create("perm.z", "Z", PermissionType.Menu, null, null), Permission.Create("perm.a", "A", PermissionType.Menu, null, null)];
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), new FakeRefreshStore(), new FakeRateLimiter(), new FakeAuditSink());

        // Act
        var result = await service.GetCurrentUserAsync("user.alice", CancellationToken.None);

        // Assert: 权限按序去重
        Assert.Equal("user.alice", result.UserNId);
        Assert.Equal(["perm.a", "perm.z"], result.PermissionNIds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("user.ghost")]
    public async Task GetCurrentUserAsync_UnknownOrEmpty_ThrowsSessionInvalid(string? userNId)
    {
        // Arrange
        var service = CreateService(new FakeStore(), new FakeHasher(Password), new FakeTokenFactory(), new FakeRefreshStore(), new FakeRateLimiter(), new FakeAuditSink());

        // Act
        var ex = await Assert.ThrowsAsync<SessionInvalidException>(() =>
            service.GetCurrentUserAsync(userNId!, CancellationToken.None));

        Assert.Equal("401", ex.Code);
        Assert.Equal(401, ex.StatusCode);
    }

    private static StoredRefreshSession Session(
        Guid userId,
        string nId = "SES-old",
        string family = "FAM-1",
        DateTimeOffset? expires = null,
        DateTimeOffset? used = null,
        DateTimeOffset? revoked = null,
        string? replacedBy = null)
        => new(
            Guid.NewGuid(),
            Tenant,
            nId,
            family,
            userId,
            false,
            expires ?? DateTimeOffset.UtcNow.AddDays(7),
            used,
            revoked,
            null,
            replacedBy);

    [Fact]
    public async Task RefreshAsync_ValidToken_RotatesSameFamilyAndReturnsNewSession()
    {
        // Arrange
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var store = new FakeStore();
        store.ByUserId[user.Id] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        store.Permissions = [Permission.Create("perm.view", "查看", PermissionType.Menu, null, null)];
        var refresh = new FakeRefreshStore { FindResult = Session(user.Id) };
        var tokens = new FakeTokenFactory();
        var service = CreateService(store, new FakeHasher(Password), tokens, refresh, new FakeRateLimiter(), new FakeAuditSink());

        // Act
        var result = await service.RefreshAsync(new RefreshRequest("raw-token"), "10.0.0.1", "test-agent", CancellationToken.None);

        // Assert: 返回新会话契约
        Assert.Equal("jwt-token", result.AccessToken);
        Assert.False(string.IsNullOrWhiteSpace(result.RefreshToken));
        Assert.Equal("user.alice", result.User.UserNId);
        Assert.Equal(["perm.view"], result.User.PermissionNIds);

        // Assert: 旋转同 Family、新 NId、新 Token,descriptor 的 sid=新会话、ver=当前 AuthVersion
        var replacement = Assert.Single(refresh.RotatedReplacements);
        Assert.Equal("FAM-1", replacement.FamilyNId);
        Assert.StartsWith("SES-", replacement.NId);
        Assert.NotEqual("SES-old", replacement.NId);
        Assert.Equal(result.RefreshToken, replacement.RawToken);
        Assert.Equal(user.Id, replacement.UserId);
        Assert.NotNull(tokens.Descriptor);
        Assert.Equal(tokens.Descriptor.SessionId, replacement.NId);
        Assert.Equal(user.AuthVersion, tokens.Descriptor.AuthVersion);
        Assert.Equal([RoleNId], tokens.Descriptor.Roles);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RefreshAsync_EmptyToken_ThrowsValidation(string? refreshToken)
    {
        var service = CreateService(new FakeStore(), new FakeHasher(Password), new FakeTokenFactory(), new FakeRefreshStore(), new FakeRateLimiter(), new FakeAuditSink());

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.RefreshAsync(new RefreshRequest(refreshToken), null, null, CancellationToken.None));

        Assert.Contains("刷新令牌", ex.Message);
    }

    [Fact]
    public async Task RefreshAsync_UnknownToken_ThrowsRefreshTokenInvalid()
    {
        var service = CreateService(new FakeStore(), new FakeHasher(Password), new FakeTokenFactory(), new FakeRefreshStore(), new FakeRateLimiter(), new FakeAuditSink());

        var ex = await Assert.ThrowsAsync<RefreshTokenInvalidException>(() =>
            service.RefreshAsync(new RefreshRequest("ghost-token"), null, null, CancellationToken.None));

        Assert.Equal("ID_AUTH_REFRESH_INVALID", ex.Code);
        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task RefreshAsync_ExpiredToken_ThrowsRefreshTokenInvalid()
    {
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var store = new FakeStore();
        store.ByUserId[user.Id] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var refresh = new FakeRefreshStore { FindResult = Session(user.Id, expires: DateTimeOffset.UtcNow.AddMinutes(-1)) };
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), refresh, new FakeRateLimiter(), new FakeAuditSink());

        await Assert.ThrowsAsync<RefreshTokenInvalidException>(() =>
            service.RefreshAsync(new RefreshRequest("raw-token"), null, null, CancellationToken.None));
    }

    [Fact]
    public async Task RefreshAsync_RevokedSession_ThrowsRefreshTokenInvalid()
    {
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var store = new FakeStore();
        store.ByUserId[user.Id] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var refresh = new FakeRefreshStore { FindResult = Session(user.Id, revoked: DateTimeOffset.UtcNow) };
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), refresh, new FakeRateLimiter(), new FakeAuditSink());

        await Assert.ThrowsAsync<RefreshTokenInvalidException>(() =>
            service.RefreshAsync(new RefreshRequest("raw-token"), null, null, CancellationToken.None));
    }

    [Fact]
    public async Task RefreshAsync_ReplacedSession_ThrowsRefreshTokenInvalid()
    {
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var store = new FakeStore();
        store.ByUserId[user.Id] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var refresh = new FakeRefreshStore { FindResult = Session(user.Id, replacedBy: "SES-newer") };
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), refresh, new FakeRateLimiter(), new FakeAuditSink());

        await Assert.ThrowsAsync<RefreshTokenInvalidException>(() =>
            service.RefreshAsync(new RefreshRequest("raw-token"), null, null, CancellationToken.None));
    }

    [Fact]
    public async Task RefreshAsync_ReusedToken_RevokesFamilyAndThrowsReused()
    {
        // Arrange
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var store = new FakeStore();
        store.ByUserId[user.Id] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var refresh = new FakeRefreshStore { FindResult = Session(user.Id, used: DateTimeOffset.UtcNow.AddMinutes(-1)) };
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), refresh, new FakeRateLimiter(), new FakeAuditSink());

        // Act
        var ex = await Assert.ThrowsAsync<RefreshTokenReusedException>(() =>
            service.RefreshAsync(new RefreshRequest("raw-token"), null, null, CancellationToken.None));

        // Assert: 重放撤销整个 Family
        Assert.Equal("ID_AUTH_REFRESH_REUSED", ex.Code);
        Assert.Contains("FAM-1", refresh.RevokedFamilies);
    }

    [Fact]
    public async Task RefreshAsync_ConcurrentReuse_RotateReturnsReused_RevokesFamilyAndThrowsReused()
    {
        // Arrange
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var store = new FakeStore();
        store.ByUserId[user.Id] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var refresh = new FakeRefreshStore { FindResult = Session(user.Id), RotateResult = RefreshRotationStatus.Reused };
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), refresh, new FakeRateLimiter(), new FakeAuditSink());

        // Act
        var ex = await Assert.ThrowsAsync<RefreshTokenReusedException>(() =>
            service.RefreshAsync(new RefreshRequest("raw-token"), null, null, CancellationToken.None));

        Assert.Equal("ID_AUTH_REFRESH_REUSED", ex.Code);
        Assert.Contains("FAM-1", refresh.RevokedFamilies);
    }

    [Fact]
    public async Task RefreshAsync_RotateInvalid_ThrowsRefreshTokenInvalid()
    {
        // Arrange
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var store = new FakeStore();
        store.ByUserId[user.Id] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var refresh = new FakeRefreshStore { FindResult = Session(user.Id), RotateResult = RefreshRotationStatus.Invalid };
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), refresh, new FakeRateLimiter(), new FakeAuditSink());

        await Assert.ThrowsAsync<RefreshTokenInvalidException>(() =>
            service.RefreshAsync(new RefreshRequest("raw-token"), null, null, CancellationToken.None));
    }

    [Fact]
    public async Task RefreshAsync_RevokedInRedis_ThrowsRefreshTokenInvalid()
    {
        // Arrange
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var store = new FakeStore();
        store.ByUserId[user.Id] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var revocation = new FakeRevocationStore();
        revocation.Revoked.Add("SES-old");
        var refresh = new FakeRefreshStore { FindResult = Session(user.Id) };
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), refresh, new FakeRateLimiter(), new FakeAuditSink(), revocation);

        await Assert.ThrowsAsync<RefreshTokenInvalidException>(() =>
            service.RefreshAsync(new RefreshRequest("raw-token"), null, null, CancellationToken.None));
    }

    [Fact]
    public async Task RefreshAsync_RedisUnavailable_FailsClosedWithSecurityStoreUnavailable()
    {
        // Arrange: 撤销校验必须 fail-closed(§14),Redis 不可用不得放行
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var store = new FakeStore();
        store.ByUserId[user.Id] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var revocation = new FakeRevocationStore { FailOnCheck = true };
        var refresh = new FakeRefreshStore { FindResult = Session(user.Id) };
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), refresh, new FakeRateLimiter(), new FakeAuditSink(), revocation);

        var ex = await Assert.ThrowsAsync<SecurityStoreUnavailableException>(() =>
            service.RefreshAsync(new RefreshRequest("raw-token"), null, null, CancellationToken.None));

        Assert.Equal("ID_AUTH_SECURITY_STORE_UNAVAILABLE", ex.Code);
        Assert.Equal(503, ex.StatusCode);
    }

    [Fact]
    public async Task RefreshAsync_UserDisabled_ThrowsRefreshTokenInvalid()
    {
        // Arrange
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        user.Disable();
        var store = new FakeStore();
        store.ByUserId[user.Id] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var refresh = new FakeRefreshStore { FindResult = Session(user.Id) };
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), refresh, new FakeRateLimiter(), new FakeAuditSink());

        await Assert.ThrowsAsync<RefreshTokenInvalidException>(() =>
            service.RefreshAsync(new RefreshRequest("raw-token"), null, null, CancellationToken.None));
    }

    [Fact]
    public async Task LogoutAsync_RevokesFamilyAndWritesSidRevocation()
    {
        // Arrange
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var refresh = new FakeRefreshStore { FindResult = Session(user.Id) };
        var revocation = new FakeRevocationStore();
        var service = CreateService(new FakeStore(), new FakeHasher(Password), new FakeTokenFactory(), refresh, new FakeRateLimiter(), new FakeAuditSink(), revocation);

        // Act
        await service.LogoutAsync(new LogoutRequest("raw-token"), "SES-abc", TimeSpan.FromMinutes(30), CancellationToken.None);

        // Assert: 撤销 Family + sid 写入撤销键
        Assert.Contains("FAM-1", refresh.RevokedFamilies);
        Assert.Equal([("SES-abc", TimeSpan.FromMinutes(30))], revocation.Revokes);
    }

    [Fact]
    public async Task LogoutAsync_UnknownToken_StillSucceedsAndWritesSid()
    {
        // 重复注销幂等:未知 Token 不抛错,仍写 sid 撤销
        var refresh = new FakeRefreshStore();
        var revocation = new FakeRevocationStore();
        var service = CreateService(new FakeStore(), new FakeHasher(Password), new FakeTokenFactory(), refresh, new FakeRateLimiter(), new FakeAuditSink(), revocation);

        await service.LogoutAsync(new LogoutRequest("ghost-token"), "SES-abc", TimeSpan.FromMinutes(30), CancellationToken.None);

        Assert.Empty(refresh.RevokedFamilies);
        Assert.Single(revocation.Revokes);
    }

    [Fact]
    public async Task LogoutAllAsync_RevokesAllSessionsAndIncrementsAuthVersion()
    {
        // Arrange
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var originalVersion = user.AuthVersion;
        var store = new FakeStore();
        store.ByNId["user.alice"] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var refresh = new FakeRefreshStore();
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), refresh, new FakeRateLimiter(), new FakeAuditSink());

        // Act
        await service.LogoutAllAsync("user.alice", CancellationToken.None);

        // Assert: 推进安全版本并撤销全部会话
        Assert.Equal(originalVersion + 1, user.AuthVersion);
        Assert.Equal(1, store.UpdateCount);
        Assert.Equal(user.Id, refresh.RevokedUserIds.Single());
    }

    [Fact]
    public async Task LogoutAllAsync_InvalidatesPermissionCache()
    {
        // Arrange: 数据库已提交新安全版本,提交后删除该用户全部版本缓存键(TASK-ID-007)
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var store = new FakeStore();
        store.ByNId["user.alice"] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var cache = new FakePermissionCache();
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), new FakeRefreshStore(), new FakeRateLimiter(), new FakeAuditSink(), permissionCache: cache);

        // Act
        await service.LogoutAllAsync("user.alice", CancellationToken.None);

        // Assert: 提交后按租户+用户失效
        Assert.Equal([(Tenant, "user.alice")], cache.Invalidated);
    }

    [Fact]
    public async Task LogoutAllAsync_CacheInvalidateFailure_StillSucceeds()
    {
        // Arrange: 缓存失效为尽力而为,失败不阻断用例(TTL 兜底收敛)
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var store = new FakeStore();
        store.ByNId["user.alice"] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var cache = new FakePermissionCache { ThrowOnInvalidate = true };
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), new FakeRefreshStore(), new FakeRateLimiter(), new FakeAuditSink(), permissionCache: cache);

        // Act
        await service.LogoutAllAsync("user.alice", CancellationToken.None);

        // Assert: 不抛错,数据库已提交
        Assert.Equal(1, store.UpdateCount);
    }

    [Fact]
    public async Task LogoutAllAsync_UnknownUser_ThrowsSessionInvalid()
    {
        var service = CreateService(new FakeStore(), new FakeHasher(Password), new FakeTokenFactory(), new FakeRefreshStore(), new FakeRateLimiter(), new FakeAuditSink());

        var ex = await Assert.ThrowsAsync<SessionInvalidException>(() =>
            service.LogoutAllAsync("user.ghost", CancellationToken.None));

        Assert.Equal("401", ex.Code);
    }

    [Fact]
    public async Task ChangePasswordAsync_WrongCurrentPassword_ThrowsInvalidCredentials()
    {
        // Arrange
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var store = new FakeStore();
        store.ByNId["user.alice"] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), new FakeRefreshStore(), new FakeRateLimiter(), new FakeAuditSink());

        // Act
        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            service.ChangePasswordAsync(new ChangePasswordRequest("wrong-password", "New-Passw0rd!"), "user.alice", CancellationToken.None));
    }

    [Fact]
    public async Task ChangePasswordAsync_WeakNewPassword_ThrowsValidation()
    {
        // Arrange
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var store = new FakeStore();
        store.ByNId["user.alice"] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), new FakeRefreshStore(), new FakeRateLimiter(), new FakeAuditSink());

        // Act
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ChangePasswordAsync(new ChangePasswordRequest(Password, "short"), "user.alice", CancellationToken.None));
    }

    [Fact]
    public async Task ChangePasswordAsync_ValidPassword_UpdatesHashAndRevokesAllSessions()
    {
        // Arrange
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var originalVersion = user.AuthVersion;
        var store = new FakeStore();
        store.ByNId["user.alice"] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var refresh = new FakeRefreshStore();
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), refresh, new FakeRateLimiter(), new FakeAuditSink());

        // Act
        const string newPassword = "New-Passw0rd!";
        await service.ChangePasswordAsync(new ChangePasswordRequest(Password, newPassword), "user.alice", CancellationToken.None);

        // Assert: 哈希更新、安全版本推进、全部会话撤销(前端需重新登录)
        Assert.Equal("hash:" + newPassword, user.PasswordHash);
        Assert.Equal(originalVersion + 1, user.AuthVersion);
        Assert.Equal(1, store.UpdateCount);
        Assert.Equal(user.Id, refresh.RevokedUserIds.Single());
    }

    [Fact]
    public async Task ChangePasswordAsync_InvalidatesPermissionCache()
    {
        // Arrange: 修改密码后提交并失效权限缓存(TASK-ID-007)
        var user = User.Create(Tenant, "user.alice", "alice", "Alice", null, null, "hash-1");
        var store = new FakeStore();
        store.ByNId["user.alice"] = new AuthenticatedUser(user, [RoleId], [RoleNId]);
        var cache = new FakePermissionCache();
        var service = CreateService(store, new FakeHasher(Password), new FakeTokenFactory(), new FakeRefreshStore(), new FakeRateLimiter(), new FakeAuditSink(), permissionCache: cache);

        // Act
        await service.ChangePasswordAsync(new ChangePasswordRequest(Password, "New-Passw0rd!"), "user.alice", CancellationToken.None);

        // Assert: 提交后按租户+用户失效
        Assert.Equal([(Tenant, "user.alice")], cache.Invalidated);
    }

    private static AuthenticationService CreateService(
        IAuthenticationStore store,
        IPasswordHasher hasher,
        IAccessTokenFactory tokenFactory,
        IRefreshSessionStore refreshStore,
        ILoginRateLimiter rateLimiter,
        ILoginAuditSink auditSink,
        ISessionRevocationStore? sessionRevocation = null,
        IPermissionCache? permissionCache = null)
        => new(
            store,
            hasher,
            tokenFactory,
            refreshStore,
            rateLimiter,
            auditSink,
            sessionRevocation ?? new FakeRevocationStore(),
            permissionCache ?? new FakePermissionCache(),
            Options.Create(new AuthenticationOptions()),
            NullLogger<AuthenticationService>.Instance);

    private sealed class FakeStore : IAuthenticationStore
    {
        public Dictionary<(string Tenant, string LoginName), AuthenticatedUser> ByLoginName { get; } = [];
        public Dictionary<string, AuthenticatedUser> ByNId { get; } = [];
        public Dictionary<Guid, AuthenticatedUser> ByUserId { get; } = [];
        public IReadOnlyList<Permission> Permissions { get; set; } = [];
        public int FindByLoginNameCount { get; private set; }
        public int UpdateCount { get; private set; }
        public int ConcurrentFailuresRemaining { get; set; }
        public (User User, long Optimistic, Guid Concurrency, CancellationToken Ct) LastUpdate { get; private set; }

        public Task<AuthenticatedUser?> FindByNormalizedLoginNameAsync(
            string tenantNId, string normalizedLoginName, CancellationToken cancellationToken)
        {
            FindByLoginNameCount++;
            return Task.FromResult(ByLoginName.TryGetValue((tenantNId, normalizedLoginName), out var account) ? account : null);
        }

        public Task<AuthenticatedUser?> FindByNIdAsync(string userNId, CancellationToken cancellationToken)
            => Task.FromResult(ByNId.TryGetValue(userNId, out var account) ? account : null);

        public Task<AuthenticatedUser?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken)
            => Task.FromResult(ByUserId.TryGetValue(userId, out var account) ? account : null);

        public Task UpdateUserAsync(
            User user, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, CancellationToken cancellationToken)
        {
            UpdateCount++;
            if (ConcurrentFailuresRemaining > 0)
            {
                ConcurrentFailuresRemaining--;
                throw new ConcurrencyException($"实体 {user.EntityType} 更新失败:并发版本不匹配或记录不存在。");
            }

            LastUpdate = (user, expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Permission>> GetPermissionsForRolesAsync(
            IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
            => Task.FromResult(Permissions);
    }

    private sealed class FakeHasher : IPasswordHasher
    {
        private readonly string _password;

        public FakeHasher(string password) => _password = password;

        public string Hash(string password) => "hash:" + password;

        public bool Verify(string passwordHash, string password) => password == _password;

        public bool NeedsRehash(string passwordHash) => false;
    }

    private sealed class FakeTokenFactory : IAccessTokenFactory
    {
        public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddMinutes(30);
        public AccessTokenDescriptor? Descriptor { get; private set; }

        public AccessTokenResult Create(AccessTokenDescriptor descriptor)
        {
            Descriptor = descriptor;
            return new AccessTokenResult("jwt-token", ExpiresAt);
        }
    }

    private sealed class FakeRateLimiter : ILoginRateLimiter
    {
        public bool IpRateLimited { get; set; }
        public bool AccountLocked { get; set; }
        public int AccountLockedChecks { get; private set; }
        public int FailureRecords { get; private set; }
        public int SuccessRecords { get; private set; }

        public Task<bool> IsIpRateLimitedAsync(string? clientIp, CancellationToken cancellationToken)
            => Task.FromResult(IpRateLimited);

        public Task<bool> IsAccountLockedAsync(string tenantNId, string normalizedLoginName, string? clientIp, CancellationToken cancellationToken)
        {
            AccountLockedChecks++;
            return Task.FromResult(AccountLocked);
        }

        public Task RecordLoginFailureAsync(string tenantNId, string normalizedLoginName, string? clientIp, CancellationToken cancellationToken)
        {
            FailureRecords++;
            return Task.CompletedTask;
        }

        public Task RecordLoginSuccessAsync(string tenantNId, string normalizedLoginName, string? clientIp, CancellationToken cancellationToken)
        {
            SuccessRecords++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuditSink : ILoginAuditSink
    {
        public List<LoginAuditEntry> Entries { get; } = [];
        public bool ThrowOnWrite { get; set; }

        public async Task WriteAsync(LoginAuditEntry entry, CancellationToken cancellationToken)
        {
            if (ThrowOnWrite)
            {
                await Task.Yield();
                throw new InvalidOperationException("audit unavailable");
            }

            Entries.Add(entry);
            await Task.CompletedTask;
        }
    }

    private sealed class FakeRefreshStore : IRefreshSessionStore
    {
        public List<NewRefreshSession> Sessions { get; } = [];
        public bool ThrowOnAdd { get; set; }
        public StoredRefreshSession? FindResult { get; set; }
        public RefreshRotationStatus RotateResult { get; set; } = RefreshRotationStatus.Rotated;
        public List<string> RevokedFamilies { get; } = [];
        public List<Guid> RevokedUserIds { get; } = [];
        public List<NewRefreshSession> RotatedReplacements { get; } = [];

        public async Task AddAsync(NewRefreshSession session, CancellationToken cancellationToken)
        {
            if (ThrowOnAdd)
            {
                await Task.Yield();
                throw new InvalidOperationException("refresh store unavailable");
            }

            Sessions.Add(session);
            await Task.CompletedTask;
        }

        public Task<StoredRefreshSession?> FindByRawTokenAsync(string rawToken, CancellationToken cancellationToken)
            => Task.FromResult(FindResult);

        public Task<RefreshRotationStatus> RotateAsync(Guid currentSessionId, NewRefreshSession replacement, CancellationToken cancellationToken)
        {
            RotatedReplacements.Add(replacement);
            return Task.FromResult(RotateResult);
        }

        public Task RevokeFamilyAsync(string familyNId, string reason, CancellationToken cancellationToken)
        {
            RevokedFamilies.Add(familyNId);
            return Task.CompletedTask;
        }

        public Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken cancellationToken)
        {
            RevokedUserIds.Add(userId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRevocationStore : ISessionRevocationStore
    {
        public HashSet<string> Revoked { get; } = [];
        public bool FailOnCheck { get; set; }
        public List<(string SessionNId, TimeSpan Ttl)> Revokes { get; } = [];

        public Task RevokeAsync(string sessionNId, TimeSpan ttl, CancellationToken cancellationToken)
        {
            Revokes.Add((sessionNId, ttl));
            return Task.CompletedTask;
        }

        public async Task<bool> IsRevokedAsync(string sessionNId, CancellationToken cancellationToken)
        {
            if (FailOnCheck)
            {
                await Task.Yield();
                throw new SecurityStoreUnavailableException();
            }

            return Revoked.Contains(sessionNId);
        }
    }

    private sealed class FakePermissionCache : IPermissionCache
    {
        public List<(string TenantNId, string UserNId)> Invalidated { get; } = [];
        public bool ThrowOnInvalidate { get; set; }

        public Task<UserSecurityCacheEntry?> TryGetUserSnapshotAsync(string tenantNId, string userNId, int authVersion, CancellationToken cancellationToken)
            => Task.FromResult<UserSecurityCacheEntry?>(null);

        public Task<IReadOnlyList<string>?> TryGetPermissionsAsync(string tenantNId, string userNId, int authVersion, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>?>(null);

        public Task SetAsync(AuthorizationSnapshot snapshot, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public async Task InvalidateAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
        {
            if (ThrowOnInvalidate)
            {
                await Task.Yield();
                throw new InvalidOperationException("cache unavailable");
            }

            Invalidated.Add((tenantNId, userNId));
        }
    }
}
