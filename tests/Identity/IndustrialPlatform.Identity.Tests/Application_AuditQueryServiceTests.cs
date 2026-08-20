using System;
using System.Threading;
using System.Threading.Tasks;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Contracts.Management;
using Xunit;

namespace IndustrialPlatform.Identity.Application.Tests;

/// <summary>
/// 登录审计查询用例测试(§16.3/§19.1):租户编码强制覆盖,行投影字段完整性。
/// </summary>
public sealed class AuditQueryServiceTests
{
    [Fact]
    public async Task QueryLoginAuditsAsync_ForcesTenantAndMapsRows()
    {
        var auditStore = new FakeLoginAuditQueryStore
        {
            Result = new LoginAuditPage(
                [
                    new LoginAuditRow(
                        "development",
                        "user.alice",
                        "alice",
                        true,
                        null,
                        "ip-hash",
                        "ua-hash",
                        "trace-1",
                        new DateTimeOffset(2026, 8, 1, 1, 2, 3, TimeSpan.Zero)),
                ],
                1),
        };
        var service = new AuditQueryService(auditStore);

        var page = await service.QueryLoginAuditsAsync(
            "development",
            new LoginAuditFilter("ignored", "user.alice", true, 1, 20),
            CancellationToken.None);

        Assert.Equal("development", auditStore.LastFilter?.TenantNId);
        Assert.Equal(1, page.Total);
        var item = Assert.Single(page.Items);
        Assert.Equal("user.alice", item.UserNId);
        Assert.Equal("alice", item.LoginNameSnapshot);
        Assert.True(item.Success);
        Assert.Null(item.FailureCode);
        Assert.Equal("ip-hash", item.IpAddressHash);
        Assert.Equal("ua-hash", item.UserAgentHash);
        Assert.Equal("trace-1", item.TraceId);
    }

    [Fact]
    public async Task QueryLoginAuditsAsync_Empty_ReturnsEmpty()
    {
        var service = new AuditQueryService(new FakeLoginAuditQueryStore());

        var page = await service.QueryLoginAuditsAsync(
            "development",
            new LoginAuditFilter("development", null, null, 1, 20),
            CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
    }

    private sealed class FakeLoginAuditQueryStore : ILoginAuditQueryStore
    {
        public LoginAuditFilter? LastFilter { get; private set; }
        public LoginAuditPage Result { get; set; } = new([], 0);

        public Task<LoginAuditPage> QueryAsync(LoginAuditFilter filter, CancellationToken cancellationToken)
        {
            LastFilter = filter;
            return Task.FromResult(Result);
        }
    }
}
