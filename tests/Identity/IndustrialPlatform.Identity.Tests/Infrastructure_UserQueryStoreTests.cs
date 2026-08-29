using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Identity.Infrastructure.Management;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.Querying.Descriptors;
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
