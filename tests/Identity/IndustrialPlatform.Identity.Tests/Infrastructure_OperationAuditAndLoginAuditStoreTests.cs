using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Infrastructure.Authentication;
using IndustrialPlatform.Identity.Infrastructure.Management;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Infrastructure.Database;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using SQLitePCL;
using Xunit;

namespace IndustrialPlatform.Identity.Infrastructure.Tests;

/// <summary>
/// 操作审计追加写入与登录审计查询测试(§19.1/§19.2):只追加持久化、
/// 租户隔离分页、用户精确匹配与成功/失败过滤。审计结果仅含哈希摘要,不含敏感字段。
/// </summary>
[Collection(BootstrapEnvironmentTestGroup.Name)]
public sealed class OperationAuditAndLoginAuditStoreTests : IDisposable
{
    private static readonly string[] BootstrapEnvNames =
    [
        "IDENTITY_BOOTSTRAP_TENANT_NID",
        "IDENTITY_BOOTSTRAP_USER_NID",
        "IDENTITY_BOOTSTRAP_LOGIN_NAME",
        "IDENTITY_BOOTSTRAP_PASSWORD",
    ];

    static OperationAuditAndLoginAuditStoreTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    private readonly string _dbPath;
    private readonly SqlSugarDbContext _dbContext;

    public OperationAuditAndLoginAuditStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-mgmt-audit-{Guid.NewGuid():N}.db");
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

    private async Task SeedLoginAuditAsync(
        string tenantNId,
        string? userNId,
        string loginName,
        bool success,
        DateTimeOffset occurredOn)
    {
        var row = new LoginAuditTable
        {
            Id = Guid.NewGuid(),
            IsFrozen = false,
            IsLocked = false,
            IsDeleted = false,
            EntityType = typeof(LoginAuditTable).FullName ?? nameof(LoginAuditTable),
            CreatedOn = occurredOn,
            LastUpdatedOn = occurredOn,
            OptimisticVersion = 0,
            ConcurrencyVersion = Guid.NewGuid(),
            TenantNId = tenantNId,
            UserNId = userNId,
            LoginNameSnapshot = loginName,
            Result = success ? LoginAuditResult.Success : LoginAuditResult.Failure,
            FailureReason = success ? null : "ID_AUTH_INVALID_CREDENTIALS",
            IpAddressHash = "ip-hash-" + loginName,
            UserAgentHash = "ua-hash-" + loginName,
            TraceId = "trace-" + loginName,
        };
        await _dbContext.SqlSugar.Insertable(row).ExecuteCommandAsync(CancellationToken.None);
    }

    [Fact]
    public async Task OperationAuditSink_WritesAppendOnlyRow()
    {
        var sink = new OperationAuditSink(_dbContext);
        var occurredOn = new DateTimeOffset(2026, 8, 1, 1, 2, 3, TimeSpan.Zero);

        await sink.WriteAsync(
            new OperationAuditEntry(
                "development",
                "actor.admin",
                OperationAction.UserCreate,
                OperationObjectType.User,
                "alice.user",
                null,
                "loginName=alice,name=Alice,status=Active",
                "trace-1",
                occurredOn),
            CancellationToken.None);

        var row = await _dbContext.SqlSugar.Queryable<OperationAuditTable>()
            .Where(t => t.TenantNId == "development")
            .ToListAsync(CancellationToken.None);

        var audit = Assert.Single(row);
        Assert.Equal("actor.admin", audit.ActorUserNId);
        Assert.Equal(OperationAction.UserCreate, audit.Action);
        Assert.Equal(OperationObjectType.User, audit.ObjectType);
        Assert.Equal("alice.user", audit.ObjectNId);
        Assert.Equal("trace-1", audit.TraceId);
        // SqlSugar SQLite provider 丢弃 UTC 偏移(读回为本地偏移),按墙钟一致断言
        Assert.Equal(occurredOn.DateTime, audit.CreatedOn.DateTime);
        Assert.Null(audit.BeforeSummary);
        Assert.NotNull(audit.AfterSummary);
        Assert.Contains("loginName=alice", audit.AfterSummary);
    }

    [Fact]
    public async Task LoginAuditQuery_FiltersByTenantAndSuccess()
    {
        await SeedLoginAuditAsync("tenant-a", "user.alice", "alice", success: true, new DateTimeOffset(2026, 8, 1, 1, 0, 0, TimeSpan.Zero));
        await SeedLoginAuditAsync("tenant-a", "user.bob", "bob", success: false, new DateTimeOffset(2026, 8, 1, 2, 0, 0, TimeSpan.Zero));
        await SeedLoginAuditAsync("tenant-b", "user.alice", "alice", success: true, new DateTimeOffset(2026, 8, 1, 3, 0, 0, TimeSpan.Zero));
        var store = new LoginAuditQueryStore(_dbContext);

        var page = await store.QueryAsync(new LoginAuditFilter("tenant-a", null, true, 1, 20), CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal("tenant-a", item.TenantNId);
        Assert.Equal("user.alice", item.UserNId);
        Assert.True(item.Success);
        Assert.Equal("ip-hash-alice", item.IpAddressHash);
        Assert.Equal("ua-hash-alice", item.UserAgentHash);
        Assert.Equal("trace-alice", item.TraceId);
    }

    [Fact]
    public async Task LoginAuditQuery_UserNIdExactMatch()
    {
        await SeedLoginAuditAsync("tenant-a", "user.alice", "alice", success: true, new DateTimeOffset(2026, 8, 1, 1, 0, 0, TimeSpan.Zero));
        await SeedLoginAuditAsync("tenant-a", "user.bob", "bob", success: true, new DateTimeOffset(2026, 8, 1, 2, 0, 0, TimeSpan.Zero));
        var store = new LoginAuditQueryStore(_dbContext);

        var page = await store.QueryAsync(new LoginAuditFilter("tenant-a", "user.alice", null, 1, 20), CancellationToken.None);

        Assert.Equal(1, page.Total);
        Assert.Equal("user.alice", Assert.Single(page.Items).UserNId);
    }

    [Fact]
    public async Task LoginAuditQuery_Paging_OrdersNewestFirst()
    {
        await SeedLoginAuditAsync("tenant-a", "user.alice", "alice", success: true, new DateTimeOffset(2026, 8, 1, 1, 0, 0, TimeSpan.Zero));
        await SeedLoginAuditAsync("tenant-a", "user.bob", "bob", success: true, new DateTimeOffset(2026, 8, 1, 2, 0, 0, TimeSpan.Zero));
        await SeedLoginAuditAsync("tenant-a", "user.carol", "carol", success: true, new DateTimeOffset(2026, 8, 1, 3, 0, 0, TimeSpan.Zero));
        var store = new LoginAuditQueryStore(_dbContext);

        var firstPage = await store.QueryAsync(new LoginAuditFilter("tenant-a", null, null, 1, 2), CancellationToken.None);
        var secondPage = await store.QueryAsync(new LoginAuditFilter("tenant-a", null, null, 2, 2), CancellationToken.None);

        Assert.Equal(3, firstPage.Total);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Equal("user.carol", firstPage.Items[0].UserNId);
        Assert.Equal("user.bob", firstPage.Items[1].UserNId);
        var second = Assert.Single(secondPage.Items);
        Assert.Equal("user.alice", second.UserNId);
    }
}
