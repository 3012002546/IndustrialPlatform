using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;
using IndustrialPlatform.Identity.Infrastructure.Security;
using IndustrialPlatform.Infrastructure.Database;

namespace IndustrialPlatform.Identity.Infrastructure.Authentication;

/// <summary>
/// 登录审计持久化实现(§19.1):追加写入 identity_login_audit。
/// IP/User-Agent 在写入前哈希,原始值绝不下库;无连接远程端点时用空串哈希占位(NOT NULL 约束)。
/// </summary>
public sealed class LoginAuditSink : ILoginAuditSink
{
    private readonly SqlSugarDbContext _dbContext;

    /// <summary>初始化审计写入器。</summary>
    public LoginAuditSink(SqlSugarDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    public async Task WriteAsync(LoginAuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var row = new LoginAuditTable
        {
            Id = Guid.NewGuid(),
            IsFrozen = false,
            IsLocked = false,
            IsDeleted = false,
            EntityType = typeof(LoginAuditTable).FullName ?? typeof(LoginAuditTable).Name,
            CreatedOn = entry.OccurredOn,
            LastUpdatedOn = entry.OccurredOn,
            OptimisticVersion = 0,
            ConcurrencyVersion = Guid.NewGuid(),
            TenantNId = entry.TenantNId,
            UserNId = entry.UserNId,
            LoginNameSnapshot = entry.LoginNameSnapshot,
            Result = entry.Success ? LoginAuditResult.Success : LoginAuditResult.Failure,
            FailureReason = entry.FailureCode,
            IpAddressHash = Hashing.Sha256Hex(entry.IpAddress ?? string.Empty),
            UserAgentHash = Hashing.Sha256Hex(entry.UserAgent ?? string.Empty),
            TraceId = entry.TraceId ?? string.Empty,
        };

        await _dbContext.SqlSugar.Insertable(row).ExecuteCommandAsync(cancellationToken);
    }
}
