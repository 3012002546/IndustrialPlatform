using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Identity.Infrastructure.Management;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.Querying.Descriptors;
using IndustrialPlatform.Querying.Validation;
using Microsoft.Extensions.Options;
using SqlSugar;
using Xunit;

namespace IndustrialPlatform.Identity.Infrastructure.Tests;

[Collection(BootstrapEnvironmentTestGroup.Name)]
public sealed class UserQueryStoreTests : IDisposable
{
    private const string Tenant = "odata-store-test";
    private const string PasswordHash = "$2a$12$abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-odata-{Guid.NewGuid():N}.db");
    private readonly SqlSugarDbContext _dbContext;
    private readonly UserRepository _users;
    private readonly ManagementStore _store;

    public UserQueryStoreTests()
    {
        SQLitePCL.Batteries_V2.Init();
        _dbContext = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            ConnectionString = $"Data Source={_dbPath};Foreign Keys=True",
            DbType = DbType.Sqlite,
        }));
        IdentityTestDatabase.ApplyCatalogAsync(_dbContext).GetAwaiter().GetResult();
        _users = new UserRepository(_dbContext);
        _store = new ManagementStore(
            _dbContext,
            _users,
            new RoleRepository(_dbContext),
            new PermissionRepository(_dbContext));
    }

    [Fact]
    public async Task QueryUsersAsync_UsesDescriptorAndKeepsTenantScope()
    {
        await _users.AddAsync(User.Create(Tenant, "alice.user", "alice", "Alice", null, null, PasswordHash));
        await _users.AddAsync(User.Create("other-tenant", "alice.other", "alice2", "Other", null, null, PasswordHash));

        var page = await _store.QueryUsersAsync(
            Tenant,
            new QueryDescriptor(
                [new QueryFilter("userNId", QueryOperator.Contains, "alice")],
                [],
                ["userNId", "loginName"],
                1,
                20),
            CancellationToken.None);

        Assert.Equal(1, page.Total);
        Assert.Equal("alice.user", Assert.Single(page.Items).NId);
    }

    [Fact]
    public async Task QueryUsersAsync_TreatsLikeWildcardsAsLiteralInput()
    {
        await _users.AddAsync(User.Create(Tenant, "literal-percent", "literal%user", "Percent", null, null, PasswordHash));
        await _users.AddAsync(User.Create(Tenant, "literal-underscore", "literal_user", "Underscore", null, null, PasswordHash));
        await _users.AddAsync(User.Create(Tenant, "literal-other", "literalXuser", "Other", null, null, PasswordHash));

        var containsPercent = await _store.QueryUsersAsync(
            Tenant,
            new QueryDescriptor(
                [new QueryFilter("loginName", QueryOperator.Contains, "literal%")],
                [],
                ["userNId", "loginName"],
                1,
                20),
            CancellationToken.None);
        var startsWithUnderscore = await _store.QueryUsersAsync(
            Tenant,
            new QueryDescriptor(
                [new QueryFilter("loginName", QueryOperator.StartsWith, "literal_")],
                [],
                ["userNId", "loginName"],
                1,
                20),
            CancellationToken.None);

        Assert.Equal(["literal-percent"], containsPercent.Items.Select(item => item.NId));
        Assert.Equal(["literal-underscore"], startsWithUnderscore.Items.Select(item => item.NId));
    }

    [Fact]
    public async Task QueryUsersAsync_UsesSqlNullSemanticsForNullableEmail()
    {
        await _users.AddAsync(User.Create(Tenant, "email-null", "email-null", "No email", null, null, PasswordHash));
        await _users.AddAsync(User.Create(Tenant, "email-present", "email-present", "Has email", "person@example.test", null, PasswordHash));

        var nullPage = await _store.QueryUsersAsync(
            Tenant,
            new QueryDescriptor(
                [new QueryFilter("email", QueryOperator.Eq, null)],
                [],
                ["userNId", "email"],
                1,
                20),
            CancellationToken.None);
        var presentPage = await _store.QueryUsersAsync(
            Tenant,
            new QueryDescriptor(
                [new QueryFilter("email", QueryOperator.Ne, null)],
                [],
                ["userNId", "email"],
                1,
                20),
            CancellationToken.None);

        Assert.Equal(["email-null"], nullPage.Items.Select(item => item.NId));
        Assert.Equal(["email-present"], presentPage.Items.Select(item => item.NId));
    }

    [Fact]
    public async Task QueryUsersAsync_MapsPublicStatusTextToStoredEnumValues()
    {
        var active = User.Create(Tenant, "status-active", "status-active", "Active", null, null, PasswordHash);
        var disabled = User.Create(Tenant, "status-disabled", "status-disabled", "Disabled", null, null, PasswordHash);
        await _users.AddAsync(active);
        await _users.AddAsync(disabled);
        var disabledVersion = disabled.OptimisticVersion;
        var disabledConcurrency = disabled.ConcurrencyVersion;
        disabled.Disable();
        await _users.UpdateAsync(disabled, disabledVersion, disabledConcurrency, CancellationToken.None);

        var activePage = await _store.QueryUsersAsync(
            Tenant,
            new QueryDescriptor(
                [new QueryFilter("status", QueryOperator.Eq, "Active")],
                [],
                ["userNId", "status"],
                1,
                20),
            CancellationToken.None);
        var disabledPage = await _store.QueryUsersAsync(
            Tenant,
            new QueryDescriptor(
                [new QueryFilter("status", QueryOperator.Eq, "Disabled")],
                [],
                ["userNId", "status"],
                1,
                20),
            CancellationToken.None);

        Assert.Contains(activePage.Items, item => item.NId == active.NId);
        Assert.DoesNotContain(activePage.Items, item => item.NId == disabled.NId);
        Assert.Contains(disabledPage.Items, item => item.NId == disabled.NId);
        Assert.DoesNotContain(disabledPage.Items, item => item.NId == active.NId);

        var invalid = await Assert.ThrowsAsync<QueryValidationException>(() => _store.QueryUsersAsync(
            Tenant,
            new QueryDescriptor(
                [new QueryFilter("status", QueryOperator.Eq, "Unknown")],
                [],
                ["userNId", "status"],
                1,
                20),
            CancellationToken.None));
        Assert.Equal("PLATFORM_QUERY_INVALID", invalid.Error.Code);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // SqlSugar may hold a pooled handle for a short period after disposal.
        }
    }
}
