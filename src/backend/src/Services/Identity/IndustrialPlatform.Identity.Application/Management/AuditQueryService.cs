using IndustrialPlatform.Identity.Contracts.Management;

namespace IndustrialPlatform.Identity.Application.Management;

/// <summary>
/// 审计查询实现(§16.3/§19.1)。登录审计条目只暴露哈希摘要与快照,不含完整 IP、User-Agent 或敏感请求体。
/// </summary>
public sealed class AuditQueryService : IAuditQueryService
{
    private readonly ILoginAuditQueryStore _auditStore;

    public AuditQueryService(ILoginAuditQueryStore auditStore)
    {
        _auditStore = auditStore;
    }

    /// <inheritdoc/>
    public async Task<ManagementPage<LoginAuditItem>> QueryLoginAuditsAsync(
        string tenantNId,
        LoginAuditFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var page = await _auditStore.QueryAsync(filter with { TenantNId = tenantNId }, cancellationToken);
        var items = page.Items
            .Select(a => new LoginAuditItem(
                a.TenantNId,
                a.UserNId,
                a.LoginNameSnapshot,
                a.Success,
                a.FailureCode,
                a.IpAddressHash,
                a.UserAgentHash,
                a.TraceId,
                a.OccurredOn))
            .ToList();
        return new ManagementPage<LoginAuditItem>(items, page.Total, filter.PageIndex, filter.PageSize);
    }
}
