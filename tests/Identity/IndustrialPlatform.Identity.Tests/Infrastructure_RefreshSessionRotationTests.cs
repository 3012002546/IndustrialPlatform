using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Identity.Infrastructure.Authentication;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;
using IndustrialPlatform.Infrastructure.Database;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using SQLitePCL;
using Xunit;

namespace IndustrialPlatform.Identity.Infrastructure.Tests;

/// <summary>
/// 刷新会话旋转与撤销测试(§13):哈希定位、原子防重放、Family 旋转、单会话/全部注销撤销。
/// </summary>
[Collection(BootstrapEnvironmentTestGroup.Name)]
public sealed class RefreshSessionRotationTests : IDisposable
{
    private static readonly string[] BootstrapEnvNames =
    [
        "IDENTITY_BOOTSTRAP_TENANT_NID",
        "IDENTITY_BOOTSTRAP_USER_NID",
        "IDENTITY_BOOTSTRAP_LOGIN_NAME",
        "IDENTITY_BOOTSTRAP_PASSWORD",
    ];

    static RefreshSessionRotationTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    private readonly string _dbPath;
    private readonly SqlSugarDbContext _dbContext;
    private readonly RefreshSessionStore _sessions;
    private readonly UserRepository _users;

    public RefreshSessionRotationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-identity-refresh-{Guid.NewGuid():N}.db");
        _dbContext = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            ConnectionString = $"Data Source={_dbPath};Foreign Keys=True",
            DbType = DbType.Sqlite,
        }));

        foreach (var name in BootstrapEnvNames)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        IdentityTestDatabase.ApplyCatalogAsync(_dbContext).GetAwaiter().GetResult();

        _users = new UserRepository(_dbContext);
        _sessions = new RefreshSessionStore(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException)
        {
            // SqlSugarScope 连接池可能短暂占用文件句柄,忽略清理失败。
        }
    }

    private async Task<User> SeedUserAsync(string nId = "user.alice")
    {
        // loginName 唯一约束 (tenant_n_id, normalized_login_name):LoginName 与 NId 同源保证唯一。
        var user = User.Create("development", nId, nId, nId, null, null, "hash-1");
        await _users.AddAsync(user);
        return user;
    }

    private static NewRefreshSession NewSession(
        Guid userId,
        string nId,
        string family,
        string rawToken,
        DateTimeOffset? expires = null,
        DateTimeOffset? createdOn = null,
        DateTimeOffset? lastUpdatedOn = null)
        => new(
            "development",
            nId,
            family,
            userId,
            rawToken,
            expires ?? DateTimeOffset.UtcNow.AddDays(7),
            "10.0.0.1",
            "test-agent",
            createdOn ?? DateTimeOffset.UtcNow,
            lastUpdatedOn);

    [Fact]
    public async Task ListActiveForTenant_ReturnsOnlyValidSessionsWithSafeUserProjection()
    {
        var user = await SeedUserAsync();
        var now = DateTimeOffset.UtcNow;
        await _sessions.AddAsync(NewSession(user.Id, "SES-active", "FAM-active", "raw-active"), CancellationToken.None);
        await _sessions.AddAsync(NewSession(user.Id, "SES-expired", "FAM-expired", "raw-expired", now.AddMinutes(-1)), CancellationToken.None);
        await _sessions.AddAsync(NewSession(user.Id, "SES-used", "FAM-used", "raw-used"), CancellationToken.None);
        await _sessions.AddAsync(NewSession(user.Id, "SES-revoked", "FAM-revoked", "raw-revoked"), CancellationToken.None);

        await _dbContext.SqlSugar.Updateable<RefreshSessionTable>()
            .SetColumns(row => new RefreshSessionTable { UsedOn = now })
            .Where(row => row.NId == "SES-used")
            .ExecuteCommandAsync();
        await _dbContext.SqlSugar.Updateable<RefreshSessionTable>()
            .SetColumns(row => new RefreshSessionTable { RevokedOn = now })
            .Where(row => row.NId == "SES-revoked")
            .ExecuteCommandAsync();

        var result = await _sessions.ListActiveForTenantAsync("development", now, CancellationToken.None);

        var active = Assert.Single(result);
        Assert.Equal("SES-active", active.SessionNId);
        Assert.Equal(user.NId, active.UserNId);
        Assert.Equal(user.LoginName, active.LoginName);
        Assert.Equal(user.Name, active.Name);
    }

    [Fact]
    public async Task RevokeByNId_IsTenantScopedAndIdempotentForOnlyTheTargetSession()
    {
        var user = await SeedUserAsync();
        await _sessions.AddAsync(NewSession(user.Id, "SES-target", "FAM-target", "raw-target"), CancellationToken.None);
        await _sessions.AddAsync(NewSession(user.Id, "SES-sibling", "FAM-sibling", "raw-sibling"), CancellationToken.None);

        Assert.False(await _sessions.RevokeByNIdAsync("other-tenant", "SES-target", "admin_revoke", CancellationToken.None));
        Assert.True(await _sessions.RevokeByNIdAsync("development", "SES-target", "admin_revoke", CancellationToken.None));
        Assert.True(await _sessions.RevokeByNIdAsync("development", "SES-target", "admin_revoke", CancellationToken.None));

        var rows = await _dbContext.SqlSugar.Queryable<RefreshSessionTable>().ToListAsync();
        Assert.True(rows.Single(row => row.NId == "SES-target").RevokedOn.HasValue);
        Assert.Null(rows.Single(row => row.NId == "SES-sibling").RevokedOn);
    }

    [Fact]
    public async Task RevokeByNId_MakesOnlyTheTargetRefreshSessionInvalid()
    {
        var user = await SeedUserAsync();
        await _sessions.AddAsync(NewSession(user.Id, "SES-target", "FAM-target", "raw-target"), CancellationToken.None);

        Assert.True(await _sessions.RevokeByNIdAsync("development", "SES-target", "admin_revoke", CancellationToken.None));
        var revoked = await _sessions.FindByRawTokenAsync("raw-target", CancellationToken.None);
        Assert.NotNull(revoked);

        Assert.Equal(
            RefreshRotationStatus.Invalid,
            await _sessions.RotateAsync(
                revoked.Id,
                NewSession(user.Id, "SES-replacement", "FAM-target", "raw-replacement"),
                CancellationToken.None));
    }

    [Fact]
    public async Task FindByRawToken_MatchesByHashAndReturnsProjection()
    {
        var user = await SeedUserAsync();
        await _sessions.AddAsync(NewSession(user.Id, "SES-old", "FAM-1", "raw-token-1"), CancellationToken.None);

        var found = await _sessions.FindByRawTokenAsync("raw-token-1", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("SES-old", found.NId);
        Assert.Equal("FAM-1", found.FamilyNId);
        Assert.Equal(user.Id, found.UserId);
        Assert.False(found.UserIsDeleted);
        Assert.False(found.UsedOn.HasValue);
        Assert.False(found.RevokedOn.HasValue);
        Assert.Null(found.ReplacedBySessionNId);
    }

    [Fact]
    public async Task FindByRawToken_UnknownToken_ReturnsNull()
    {
        var user = await SeedUserAsync();
        await _sessions.AddAsync(NewSession(user.Id, "SES-old", "FAM-1", "raw-token-1"), CancellationToken.None);

        Assert.Null(await _sessions.FindByRawTokenAsync("ghost-token", CancellationToken.None));
    }

    [Fact]
    public async Task Rotate_Success_MarksCurrentUsedAndWritesSameFamilyReplacement()
    {
        var user = await SeedUserAsync();
        await _sessions.AddAsync(NewSession(user.Id, "SES-old", "FAM-1", "raw-token-1"), CancellationToken.None);
        var stored = await _sessions.FindByRawTokenAsync("raw-token-1", CancellationToken.None);
        Assert.NotNull(stored);

        var status = await _sessions.RotateAsync(
            stored.Id,
            NewSession(user.Id, "SES-new", "FAM-1", "raw-token-2"),
            CancellationToken.None);

        Assert.Equal(RefreshRotationStatus.Rotated, status);

        var rows = await _dbContext.SqlSugar.Queryable<RefreshSessionTable>()
            .OrderBy(t => t.CreatedOn)
            .ToListAsync();
        Assert.Equal(2, rows.Count);

        var current = rows.Single(t => t.Id == stored.Id);
        Assert.True(current.UsedOn.HasValue);
        Assert.Equal("SES-new", current.ReplacedBySessionNId);
        Assert.Equal(rows.Single(t => t.NId == "SES-new").Id, current.ReplacedBySessionId);
        Assert.False(current.ReplacedBySessionIsDeleted);

        var replacement = rows.Single(t => t.NId == "SES-new");
        Assert.Equal("FAM-1", replacement.FamilyNId);
        Assert.Equal(user.Id, replacement.UserId);
        Assert.False(replacement.UsedOn.HasValue);
        // 新 Token 只存哈希,不落明文
        Assert.NotEqual("raw-token-2", replacement.TokenHash);
    }

    [Fact]
    public async Task Rotate_PreservesOriginalLoginTimeAndRecordsRefreshTime()
    {
        var user = await SeedUserAsync();
        var t1 = DateTimeOffset.UtcNow.AddMinutes(-2);
        var t2 = t1.AddMinutes(1);
        await _sessions.AddAsync(
            NewSession(user.Id, "SES-old", "FAM-1", "raw-token-1", createdOn: t1),
            CancellationToken.None);
        var stored = await _sessions.FindByRawTokenAsync("raw-token-1", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(t1.DateTime, stored!.CreatedOn!.Value.DateTime);

        Assert.Equal(
            RefreshRotationStatus.Rotated,
            await _sessions.RotateAsync(
                stored.Id,
                NewSession(user.Id, "SES-new", "FAM-1", "raw-token-2", createdOn: t1, lastUpdatedOn: t2),
                CancellationToken.None));

        var active = Assert.Single(await _sessions.ListActiveForTenantAsync(
            "development",
            t2.AddSeconds(1),
            CancellationToken.None));
        Assert.Equal("SES-new", active.SessionNId);
        Assert.Equal(t1.DateTime, active.CreatedOn.DateTime);
        Assert.Equal(t2.DateTime, active.LastRefreshedOn.DateTime);
    }

    [Fact]
    public async Task Rotate_AlreadyUsed_ReturnsReused()
    {
        var user = await SeedUserAsync();
        await _sessions.AddAsync(NewSession(user.Id, "SES-old", "FAM-1", "raw-token-1"), CancellationToken.None);
        var stored = await _sessions.FindByRawTokenAsync("raw-token-1", CancellationToken.None);
        Assert.NotNull(stored);

        // 第一次旋转成功,标记已用
        Assert.Equal(
            RefreshRotationStatus.Rotated,
            await _sessions.RotateAsync(stored.Id, NewSession(user.Id, "SES-new", "FAM-1", "raw-token-2"), CancellationToken.None));

        // 同一会话再次旋转 = 顺序重放
        Assert.Equal(
            RefreshRotationStatus.Reused,
            await _sessions.RotateAsync(stored.Id, NewSession(user.Id, "SES-new2", "FAM-1", "raw-token-3"), CancellationToken.None));
    }

    [Fact]
    public async Task Rotate_RevokedFamily_ReturnsInvalid()
    {
        var user = await SeedUserAsync();
        await _sessions.AddAsync(NewSession(user.Id, "SES-old", "FAM-1", "raw-token-1"), CancellationToken.None);
        var stored = await _sessions.FindByRawTokenAsync("raw-token-1", CancellationToken.None);
        Assert.NotNull(stored);

        await _sessions.RevokeFamilyAsync("FAM-1", "logout", CancellationToken.None);

        Assert.Equal(
            RefreshRotationStatus.Invalid,
            await _sessions.RotateAsync(stored.Id, NewSession(user.Id, "SES-new", "FAM-1", "raw-token-2"), CancellationToken.None));
    }

    [Fact]
    public async Task Rotate_UnknownSession_ReturnsInvalid()
    {
        var user = await SeedUserAsync();
        await _sessions.AddAsync(NewSession(user.Id, "SES-old", "FAM-1", "raw-token-1"), CancellationToken.None);

        Assert.Equal(
            RefreshRotationStatus.Invalid,
            await _sessions.RotateAsync(Guid.NewGuid(), NewSession(user.Id, "SES-new", "FAM-1", "raw-token-2"), CancellationToken.None));
    }

    [Fact]
    public async Task RevokeFamily_RevokesAllMembersIdempotently()
    {
        var user = await SeedUserAsync();
        await _sessions.AddAsync(NewSession(user.Id, "SES-a1", "FAM-1", "raw-a"), CancellationToken.None);
        await _sessions.AddAsync(NewSession(user.Id, "SES-a2", "FAM-1", "raw-b"), CancellationToken.None);
        await _sessions.AddAsync(NewSession(user.Id, "SES-b1", "FAM-2", "raw-c"), CancellationToken.None);

        await _sessions.RevokeFamilyAsync("FAM-1", "replay", CancellationToken.None);
        await _sessions.RevokeFamilyAsync("FAM-1", "replay", CancellationToken.None); // 幂等

        var rows = await _dbContext.SqlSugar.Queryable<RefreshSessionTable>().ToListAsync();
        Assert.Equal("replay", rows.Single(t => t.NId == "SES-a1").RevokeReason);
        Assert.True(rows.Single(t => t.NId == "SES-a1").RevokedOn.HasValue);
        Assert.True(rows.Single(t => t.NId == "SES-a2").RevokedOn.HasValue);
        // 其他 Family 不受影响
        Assert.Null(rows.Single(t => t.NId == "SES-b1").RevokedOn);
    }

    [Fact]
    public async Task RevokeAllForUser_RevokesOnlyThatUsersSessions()
    {
        var alice = await SeedUserAsync("user.alice");
        var bob = await SeedUserAsync("user.bob");
        await _sessions.AddAsync(NewSession(alice.Id, "SES-a1", "FAM-1", "raw-a"), CancellationToken.None);
        await _sessions.AddAsync(NewSession(alice.Id, "SES-a2", "FAM-1", "raw-b"), CancellationToken.None);
        await _sessions.AddAsync(NewSession(bob.Id, "SES-b1", "FAM-2", "raw-c"), CancellationToken.None);

        await _sessions.RevokeAllForUserAsync(alice.Id, "password_changed", CancellationToken.None);

        var rows = await _dbContext.SqlSugar.Queryable<RefreshSessionTable>().ToListAsync();
        Assert.True(rows.Single(t => t.NId == "SES-a1").RevokedOn.HasValue);
        Assert.True(rows.Single(t => t.NId == "SES-a2").RevokedOn.HasValue);
        Assert.Equal("password_changed", rows.Single(t => t.NId == "SES-a1").RevokeReason);
        Assert.Null(rows.Single(t => t.NId == "SES-b1").RevokedOn);
    }
}
