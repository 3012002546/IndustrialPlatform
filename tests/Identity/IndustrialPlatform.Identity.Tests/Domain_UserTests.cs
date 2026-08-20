using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.Identity.Domain.Tests;

/// <summary>
/// User 聚合根测试:创建、资料变更、登录名/密码变更、生命周期守卫与领域事件。
/// </summary>
public sealed class UserTests
{
    private static User CreateUser() =>
        User.Create("tenant-01", "user-001", "alice", "Alice", "alice@example.com", "13800138000", "hashed-password");

    [Fact]
    public void Create_SetsAllFields()
    {
        var user = CreateUser();

        Assert.Equal("tenant-01", user.TenantNId);
        Assert.Equal("user-001", user.NId);
        Assert.Equal("alice", user.LoginName);
        Assert.Equal("Alice", user.Name);
        Assert.Equal("alice@example.com", user.Email);
        Assert.Equal("13800138000", user.Phone);
        Assert.Equal("hashed-password", user.PasswordHash);
    }

    [Fact]
    public void Create_InitializesActiveSecurityState()
    {
        var user = CreateUser();

        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal(0, user.FailedLoginCount);
        Assert.Null(user.LockedUntil);
        Assert.Equal(0, user.AuthVersion);
        Assert.Null(user.LastLoginOn);
    }

    [Fact]
    public void Create_NormalizesNId()
    {
        var user = User.Create("tenant-01", "  user-001 ", "alice", "Alice", null, null, "hash");

        Assert.Equal("user-001", user.NId);
        Assert.Equal("USER-001", user.NormalizedNId);
    }

    [Fact]
    public void Create_NormalizesLoginName()
    {
        var user = User.Create("tenant-01", "user-001", "  Alice ", "Alice", null, null, "hash");

        Assert.Equal("Alice", user.LoginName);
        Assert.Equal("ALICE", user.NormalizedLoginName);
    }

    [Fact]
    public void Create_TrimsEmailAndPhone()
    {
        var user = User.Create("tenant-01", "user-001", "alice", "Alice", "  alice@example.com ", "  13800138000 ", "hash");

        Assert.Equal("alice@example.com", user.Email);
        Assert.Equal("13800138000", user.Phone);
    }

    [Fact]
    public void Create_EmptyEmailAndPhone_BecomeNull()
    {
        var user = User.Create("tenant-01", "user-001", "alice", "Alice", "   ", "  ", "hash");

        Assert.Null(user.Email);
        Assert.Null(user.Phone);
    }

    [Fact]
    public void Create_PublishesUserCreatedEvent()
    {
        var user = CreateUser();

        var domainEvent = Assert.Single(user.DomainEvents);
        var created = Assert.IsType<UserCreatedEvent>(domainEvent);
        Assert.Equal("tenant-01", created.TenantNId);
        Assert.Equal("user-001", created.UserNId);
        Assert.Equal("alice", created.LoginName);
        Assert.Equal(0, created.AuthVersion);
    }

    [Fact]
    public void Create_InvalidNId_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            User.Create("tenant-01", "..bad", "alice", "Alice", null, null, "hash"));
    }

    [Fact]
    public void Create_EmptyTenantNId_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            User.Create("   ", "user-001", "alice", "Alice", null, null, "hash"));
    }

    [Fact]
    public void Create_EmptyLoginName_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            User.Create("tenant-01", "user-001", "  ", "Alice", null, null, "hash"));
    }

    [Fact]
    public void Create_LoginNameTooLong_ThrowsValidationException()
    {
        var tooLong = new string('a', User.LoginNameMaxLength + 1);

        Assert.Throws<ValidationException>(() =>
            User.Create("tenant-01", "user-001", tooLong, "Alice", null, null, "hash"));
    }

    [Fact]
    public void Create_EmptyName_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            User.Create("tenant-01", "user-001", "alice", "  ", null, null, "hash"));
    }

    [Fact]
    public void Create_EmptyPasswordHash_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            User.Create("tenant-01", "user-001", "alice", "Alice", null, null, "   "));
    }

    [Fact]
    public void Create_EmailTooLong_ThrowsValidationException()
    {
        var tooLong = new string('a', User.EmailMaxLength + 1) + "@x.com";

        Assert.Throws<ValidationException>(() =>
            User.Create("tenant-01", "user-001", "alice", "Alice", tooLong, null, "hash"));
    }

    [Fact]
    public void ChangeProfile_UpdatesFields()
    {
        var user = CreateUser();

        user.ChangeProfile("Bob", "bob@example.com", "13900139000");

        Assert.Equal("Bob", user.Name);
        Assert.Equal("bob@example.com", user.Email);
        Assert.Equal("13900139000", user.Phone);
    }

    [Fact]
    public void ChangeProfile_DoesNotChangeAuthVersionOrPublishEvents()
    {
        var user = CreateUser();
        user.ClearDomainEvents();

        user.ChangeProfile("Bob", null, null);

        Assert.Equal(0, user.AuthVersion);
        Assert.Empty(user.DomainEvents);
    }

    [Fact]
    public void ChangeProfile_TouchesEntity()
    {
        var user = CreateUser();
        user.ClearDomainEvents();
        var versionBefore = user.OptimisticVersion;

        user.ChangeProfile("Bob", null, null);

        Assert.Equal(versionBefore + 1, user.OptimisticVersion);
    }

    [Fact]
    public void ChangeLoginName_UpdatesLoginNameAndNormalized()
    {
        var user = CreateUser();

        user.ChangeLoginName("  bob  ");

        Assert.Equal("bob", user.LoginName);
        Assert.Equal("BOB", user.NormalizedLoginName);
    }

    [Fact]
    public void ChangeLoginName_DoesNotChangeNId()
    {
        var user = CreateUser();

        user.ChangeLoginName("bob");

        Assert.Equal("user-001", user.NId);
        Assert.Equal("USER-001", user.NormalizedNId);
    }

    [Fact]
    public void ChangeLoginName_IncrementsAuthVersion()
    {
        var user = CreateUser();

        user.ChangeLoginName("bob");

        Assert.Equal(1, user.AuthVersion);
    }

    [Fact]
    public void ChangeLoginName_PublishesSecurityChangedEvent()
    {
        var user = CreateUser();
        user.ClearDomainEvents();

        user.ChangeLoginName("bob");

        var domainEvent = Assert.Single(user.DomainEvents);
        var securityChanged = Assert.IsType<UserSecurityChangedEvent>(domainEvent);
        Assert.Equal(UserSecurityChangeReason.LoginNameChanged, securityChanged.Reason);
        Assert.Equal("user-001", securityChanged.UserNId);
        Assert.Equal(1, securityChanged.AuthVersion);
    }

    [Fact]
    public void ChangeLoginName_Invalid_ThrowsValidationException()
    {
        var user = CreateUser();

        Assert.Throws<ValidationException>(() => user.ChangeLoginName("  "));
    }

    [Fact]
    public void ChangePasswordHash_UpdatesHashAndIncrementsAuthVersion()
    {
        var user = CreateUser();

        user.ChangePasswordHash("new-hash");

        Assert.Equal("new-hash", user.PasswordHash);
        Assert.Equal(1, user.AuthVersion);
    }

    [Fact]
    public void ChangePasswordHash_PublishesSecurityChangedEvent()
    {
        var user = CreateUser();
        user.ClearDomainEvents();

        user.ChangePasswordHash("new-hash");

        var domainEvent = Assert.Single(user.DomainEvents);
        var securityChanged = Assert.IsType<UserSecurityChangedEvent>(domainEvent);
        Assert.Equal(UserSecurityChangeReason.PasswordChanged, securityChanged.Reason);
        Assert.Equal(1, securityChanged.AuthVersion);
    }

    [Fact]
    public void ChangePasswordHash_Empty_ThrowsValidationException()
    {
        var user = CreateUser();

        Assert.Throws<ValidationException>(() => user.ChangePasswordHash("   "));
    }

    [Fact]
    public void IncrementAuthVersion_IncrementsAndTouches()
    {
        var user = CreateUser();
        var versionBefore = user.OptimisticVersion;

        user.IncrementAuthVersion();

        Assert.Equal(1, user.AuthVersion);
        Assert.Equal(versionBefore + 1, user.OptimisticVersion);
    }

    [Fact]
    public void MarkDeleted_ThenChangeProfile_ThrowsBusinessException()
    {
        var user = CreateUser();
        user.MarkDeleted();

        Assert.Throws<BusinessException>(() => user.ChangeProfile("Bob", null, null));
    }

    [Fact]
    public void Freeze_ThenChangeLoginName_ThrowsBusinessException()
    {
        var user = CreateUser();
        user.Freeze();

        Assert.Throws<BusinessException>(() => user.ChangeLoginName("bob"));
    }

    [Fact]
    public void Lock_ThenChangePasswordHash_ThrowsBusinessException()
    {
        var user = CreateUser();
        user.Lock();

        Assert.Throws<BusinessException>(() => user.ChangePasswordHash("new-hash"));
    }

    [Fact]
    public void ClearDomainEvents_EmptiesCollection()
    {
        var user = CreateUser();

        user.ClearDomainEvents();

        Assert.Empty(user.DomainEvents);
    }
}
