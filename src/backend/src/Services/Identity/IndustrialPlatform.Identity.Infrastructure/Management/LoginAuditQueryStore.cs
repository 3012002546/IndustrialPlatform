using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;
using IndustrialPlatform.Infrastructure.Database;
using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Management;

/// <summary>
/// 登录审计查询端口实现(§19.1/§16.3):按租户分页只读查询 identity_login_audit。
/// 仅暴露哈希摘要与快照,完整 IP、User-Agent 与敏感请求体绝不下库,亦不进入查询结果。
/// </summary>
public sealed class LoginAuditQueryStore : ILoginAuditQueryStore
{
    private readonly SqlSugarDbContext _dbContext;

    /// <summary>初始化登录审计查询存储。</summary>
    public LoginAuditQueryStore(SqlSugarDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    public async Task<LoginAuditPage> QueryAsync(LoginAuditFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = _dbContext.SqlSugar.Queryable<LoginAuditTable>()
            .Where(t => t.TenantNId == filter.TenantNId && !t.IsDeleted);

        if (!string.IsNullOrWhiteSpace(filter.UserNId))
        {
            var normalized = filter.UserNId.Trim();
            query = query.Where(t => t.UserNId == normalized);
        }

        if (filter.Success is { } success)
        {
            query = query.Where(t => t.Result == (success ? LoginAuditResult.Success : LoginAuditResult.Failure));
        }

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var keyword = filter.Keyword.Trim();
            query = query.Where(t => (t.UserNId != null && t.UserNId.Contains(keyword)) || t.LoginNameSnapshot.Contains(keyword) || (t.FailureReason != null && t.FailureReason.Contains(keyword)) || t.IpAddressHash.Contains(keyword) || t.UserAgentHash.Contains(keyword) || t.TraceId.Contains(keyword));
        }
        if (!string.IsNullOrWhiteSpace(filter.LoginNameSnapshot)) query = query.Where(t => t.LoginNameSnapshot.Contains(filter.LoginNameSnapshot.Trim()));
        if (!string.IsNullOrWhiteSpace(filter.FailureCode)) query = query.Where(t => t.FailureReason != null && t.FailureReason.Contains(filter.FailureCode.Trim()));
        if (!string.IsNullOrWhiteSpace(filter.IpAddressHash)) query = query.Where(t => t.IpAddressHash.Contains(filter.IpAddressHash.Trim()));
        if (!string.IsNullOrWhiteSpace(filter.UserAgentHash)) query = query.Where(t => t.UserAgentHash.Contains(filter.UserAgentHash.Trim()));
        if (!string.IsNullOrWhiteSpace(filter.TraceId)) query = query.Where(t => t.TraceId.Contains(filter.TraceId.Trim()));
        if (filter.OccurredFrom is { } occurredFrom) query = query.Where(t => t.CreatedOn >= occurredFrom);
        if (filter.OccurredTo is { } occurredTo) query = query.Where(t => t.CreatedOn <= occurredTo);

        var total = await query.CountAsync(cancellationToken);
        var orderedQuery = filter.SortField?.ToLowerInvariant() switch
        {
            "usernid" => filter.SortOrder == "desc" ? query.OrderByDescending(t => t.UserNId) : query.OrderBy(t => t.UserNId),
            "loginnamesnapshot" => filter.SortOrder == "desc" ? query.OrderByDescending(t => t.LoginNameSnapshot) : query.OrderBy(t => t.LoginNameSnapshot),
            _ => query.OrderBy(t => t.CreatedOn, OrderByType.Desc),
        };
        var rows = await orderedQuery
            .Skip((filter.PageIndex - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(ToRow).ToList();
        return new LoginAuditPage(items, total);
    }

    private static LoginAuditRow ToRow(LoginAuditTable row) => new(
        row.TenantNId,
        row.UserNId,
        row.LoginNameSnapshot,
        row.Result == LoginAuditResult.Success,
        row.FailureReason,
        row.IpAddressHash,
        row.UserAgentHash,
        row.TraceId,
        row.CreatedOn);
}
