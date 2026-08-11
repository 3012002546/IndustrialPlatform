using IndustrialPlatform.Identity.Domain.LoginSecurity;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.Identity.Domain.Tests;

/// <summary>
/// 用户登录安全测试:失败计数、临时锁定、成功清零、登录许可与禁用/启用。
/// </summary>
public sealed class UserLoginSecurityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 8, 0, 0, TimeSpan.Zero);
    private static readonly LoginAttemptPolicy Policy = new(5, TimeSpan.FromMinutes(15));

    private static User CreateUser() =>
        User.Create("tenant-01", "user-001", "alice", "Alice", null, null, "hashed-password");

    private static void FailLogin(User user, int times)
    {
        for (var i = 0; i < times; i++)
        {
            user.RecordLoginFailure(Now, Policy);
        }
    }

    [Fact]
    public void RecordLoginFailure_IncrementsCount()
    {
        var user = CreateUser();

        user.RecordLoginFailure(Now, Policy);

        Assert.Equal(1, user.FailedLoginCount);
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public void RecordLoginFailure_BelowThreshold_DoesNotLock()
    {
        var user = CreateUser();

        FailLogin(user, 4);

        Assert.Equal(4, user.FailedLoginCount);
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public void RecordLoginFailure_AtThreshold_LocksAndResetsCount()
    {
        var user = CreateUser();

        FailLogin(user, 5);

        Assert.Equal(0, user.FailedLoginCount);
        Assert.Equal(Now + TimeSpan.FromMinutes(15), user.LockedUntil);
    }

    [Fact]
    public void RecordLoginFailure_WhenLocked_DoesNotCountOrExtend()
    {
        var user = CreateUser();
        FailLogin(user, 5);
        var lockedUntil = user.LockedUntil;

        user.RecordLoginFailure(Now.AddMinutes(5), Policy);

        Assert.Equal(0, user.FailedLoginCount);
        Assert.Equal(lockedUntil, user.LockedUntil);
    }

    [Fact]
    public void RecordLoginFailure_AfterLockExpires_CountsAgain()
    {
        var user = CreateUser();
        FailLogin(user, 5);

        user.RecordLoginFailure(Now.AddMinutes(16), Policy);

        Assert.Equal(1, user.FailedLoginCount);
    }

    [Fact]
    public void RecordLoginSuccess_ResetsCountAndLock()
    {
        var user = CreateUser();
        FailLogin(user, 3);

        user.RecordLoginSuccess(Now);

        Assert.Equal(0, user.FailedLoginCount);
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public void RecordLoginSuccess_SetsLastLoginOn()
    {
        var user = CreateUser();

        user.RecordLoginSuccess(Now);

        Assert.Equal(Now, user.LastLoginOn);
    }

    [Fact]
    public void RecordLoginFailure_NullPolicy_ThrowsArgumentNullException()
    {
        var user = CreateUser();

        Assert.Throws<ArgumentNullException>(() => user.RecordLoginFailure(Now, null!));
    }

    [Fact]
    public void RecordLoginFailure_WhenFrozen_ThrowsBusinessException()
    {
        var user = CreateUser();
        user.Freeze();

        Assert.Throws<BusinessException>(() => user.RecordLoginFailure(Now, Policy));
    }

    [Fact]
    public void EnsureLoginAllowed_ActiveUser_Passes()
    {
        var user = CreateUser();

        user.EnsureLoginAllowed(Now);
    }

    [Fact]
    public void EnsureLoginAllowed_Disabled_ThrowsUnauthorizedException()
    {
        var user = CreateUser();
        user.Disable();

        Assert.Throws<UnauthorizedException>(() => user.EnsureLoginAllowed(Now));
    }

    [Fact]
    public void EnsureLoginAllowed_Locked_ThrowsUnauthorizedException()
    {
        var user = CreateUser();
        FailLogin(user, 5);

        Assert.Throws<UnauthorizedException>(() => user.EnsureLoginAllowed(Now.AddMinutes(5)));
    }

    [Fact]
    public void EnsureLoginAllowed_LockExpired_Passes()
    {
        var user = CreateUser();
        FailLogin(user, 5);

        user.EnsureLoginAllowed(Now.AddMinutes(16));
    }

    [Fact]
    public void EnsureLoginAllowed_Deleted_ThrowsUnauthorizedException()
    {
        var user = CreateUser();
        user.MarkDeleted();

        Assert.Throws<UnauthorizedException>(() => user.EnsureLoginAllowed(Now));
    }

    [Fact]
    public void Disable_SetsStatusAndIncrementsAuthVersion()
    {
        var user = CreateUser();
        user.ClearDomainEvents();

        user.Disable();

        Assert.Equal(UserStatus.Disabled, user.Status);
        Assert.Equal(1, user.AuthVersion);
    }

    [Fact]
    public void Disable_PublishesStatusChangedEvent()
    {
        var user = CreateUser();
        user.ClearDomainEvents();

        user.Disable();

        var domainEvent = Assert.Single(user.DomainEvents);
        var statusChanged = Assert.IsType<UserStatusChangedEvent>(domainEvent);
        Assert.Equal(UserStatus.Active, statusChanged.OldStatus);
        Assert.Equal(UserStatus.Disabled, statusChanged.NewStatus);
        Assert.Equal("user-001", statusChanged.UserNId);
        Assert.Equal(1, statusChanged.AuthVersion);
    }

    [Fact]
    public void Disable_WhenAlreadyDisabled_IsIdempotent()
    {
        var user = CreateUser();
        user.ClearDomainEvents();
        user.Disable();

        user.Disable();

        Assert.Equal(UserStatus.Disabled, user.Status);
        Assert.Equal(1, user.AuthVersion);
        Assert.Single(user.DomainEvents);
    }

    [Fact]
    public void Enable_SetsStatusActive()
    {
        var user = CreateUser();
        user.Disable();

        user.Enable();

        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Fact]
    public void Enable_ClearsFailedLoginCountAndLock()
    {
        var user = CreateUser();
        FailLogin(user, 5);
        user.Disable();

        user.Enable();

        Assert.Equal(0, user.FailedLoginCount);
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public void Enable_DoesNotIncrementAuthVersion()
    {
        var user = CreateUser();
        user.Disable();
        var versionAfterDisable = user.AuthVersion;

        user.Enable();

        Assert.Equal(versionAfterDisable, user.AuthVersion);
    }

    [Fact]
    public void Enable_PublishesStatusChangedEvent()
    {
        var user = CreateUser();
        user.Disable();
        user.ClearDomainEvents();

        user.Enable();

        var domainEvent = Assert.Single(user.DomainEvents);
        var statusChanged = Assert.IsType<UserStatusChangedEvent>(domainEvent);
        Assert.Equal(UserStatus.Disabled, statusChanged.OldStatus);
        Assert.Equal(UserStatus.Active, statusChanged.NewStatus);
    }

    [Fact]
    public void Enable_WhenAlreadyActive_IsIdempotent()
    {
        var user = CreateUser();
        user.ClearDomainEvents();

        user.Enable();

        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Empty(user.DomainEvents);
    }
}
