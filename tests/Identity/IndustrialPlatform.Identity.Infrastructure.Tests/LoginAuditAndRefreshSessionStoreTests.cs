using System.Security.Cryptography;
using System.Text;
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
/// 登录审计与刷新会话持久化测试(§13/§19.1):IP/User-Agent/Token 只存哈希、不落明文。
/// </summary>
public sealed class LoginAuditAndRefreshSessionStoreTests : IDisposable
{
    private static readonly string[] BootstrapEnvNames =
    [
        "IDENTITY_BOOTSTRAP_TENANT_NID",
        "IDENTITY_BOOTSTRAP_USER_NID",
        "IDENTITY_BOOTSTRAP_LOGIN_NAME",
        "IDENTITY_BOOTSTRAP_PASSWORD",
    ];

    static LoginAuditAndRefreshSessionStoreTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    private readonly string _dbPath;
    private readonly SqlSugarDbContext _dbContext;
    private readonly UserRepository _users;

    public LoginAuditAndRefreshSessionStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-identity-audit-{Guid.NewGuid():N}.db");
        _dbContext = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            ConnectionString = $"Data Source={_dbPath};Foreign Keys=True",
            DbType = DbType.Sqlite,
        }));

        foreach (var name in BootstrapEnvNames)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        new SchemaMigrationRunner(_dbContext, IdentitySchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance)
            .ApplyPendingAsync()
            .GetAwaiter()
            .GetResult();

        _users = new UserRepository(_dbContext);
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

    private static string HashHex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [Fact]
    public async Task AuditSink_PersistsHashedIpAndAgent()
    {
        var sink = new LoginAuditSink(_dbContext);
        var now = DateTimeOffset.UtcNow;

        await sink.WriteAsync(
            new LoginAuditEntry("development", "user.alice", "alice", true, null, "10.0.0.1", "Mozilla/5.0", "trace-1", now),
            CancellationToken.None);

        var row = Assert.Single(await _dbContext.SqlSugar.Queryable<LoginAuditTable>().ToListAsync());
        Assert.Equal("development", row.TenantNId);
        Assert.Equal("user.alice", row.UserNId);
        Assert.Equal("alice", row.LoginNameSnapshot);
        Assert.Equal(LoginAuditResult.Success, row.Result);
        Assert.Null(row.FailureReason);
        Assert.Equal(HashHex("10.0.0.1"), row.IpAddressHash);
        Assert.Equal(HashHex("Mozilla/5.0"), row.UserAgentHash);
        Assert.NotEqual("10.0.0.1", row.IpAddressHash);
        Assert.NotEqual("Mozilla/5.0", row.UserAgentHash);
        Assert.Equal("trace-1", row.TraceId);
    }

    [Fact]
    public async Task AuditSink_NullIpAgentTrace_UsesHashOfEmptyString()
    {
        var sink = new LoginAuditSink(_dbContext);

        await sink.WriteAsync(
            new LoginAuditEntry("development", null, "ghost", false, "invalid_credentials", null, null, null, DateTimeOffset.UtcNow),
            CancellationToken.None);

        var row = Assert.Single(await _dbContext.SqlSugar.Queryable<LoginAuditTable>().ToListAsync());
        Assert.Equal(LoginAuditResult.Failure, row.Result);
        Assert.Equal("invalid_credentials", row.FailureReason);
        Assert.Null(row.UserNId);
        Assert.Equal(HashHex(string.Empty), row.IpAddressHash);
        Assert.Equal(HashHex(string.Empty), row.UserAgentHash);
        Assert.Equal(string.Empty, row.TraceId);
    }

    [Fact]
    public async Task RefreshStore_PersistsTokenHashOnlyAndAssociatesUser()
    {
        var user = User.Create("development", "user.alice", "alice", "Alice", null, null, "hash-1");
        await _users.AddAsync(user);
        var now = DateTimeOffset.UtcNow;
        var session = new NewRefreshSession(
            "development",
            "SES-abc123",
            "SES-abc123",
            user.Id,
            "raw-refresh-token",
            now.AddDays(7),
            "10.0.0.1",
            "test-agent",
            now);

        await new RefreshSessionStore(_dbContext).AddAsync(session, CancellationToken.None);

        var row = Assert.Single(await _dbContext.SqlSugar.Queryable<RefreshSessionTable>().ToListAsync());
        Assert.Equal("development", row.TenantNId);
        Assert.Equal("SES-abc123", row.NId);
        Assert.Equal("SES-abc123", row.FamilyNId);
        Assert.Equal(user.Id, row.UserId);
        Assert.False(row.UserIsDeleted);
        Assert.Equal(HashHex("raw-refresh-token"), row.TokenHash);
        Assert.NotEqual("raw-refresh-token", row.TokenHash);
        Assert.Equal(HashHex("10.0.0.1"), row.IpAddressHash);
        Assert.Equal(HashHex("test-agent"), row.UserAgentHash);
        // SqlSugar SQLite provider 丢弃 UTC 偏移(读回为本地偏移),按墙钟一致断言
        Assert.Equal(now.AddDays(7).DateTime, row.ExpiresOn.DateTime);
    }
}
