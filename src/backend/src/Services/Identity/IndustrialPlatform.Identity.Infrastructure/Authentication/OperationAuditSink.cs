using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;
using IndustrialPlatform.Infrastructure.Database;

namespace IndustrialPlatform.Identity.Infrastructure.Authentication;

/// <summary>
/// 操作审计持久化实现(§19.2):追加写入 identity_operation_audit。
/// 只读用途由查询存储承载;本实现仅负责只追加写入,绝不修改或删除既有审计记录。
/// </summary>
public sealed class OperationAuditSink : IOperationAuditSink
{
    private readonly SqlSugarDbContext _dbContext;

    /// <summary>初始化操作审计写入器。</summary>
    public OperationAuditSink(SqlSugarDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    public async Task WriteAsync(OperationAuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var row = new OperationAuditTable
        {
            Id = Guid.NewGuid(),
            IsFrozen = false,
            IsLocked = false,
            IsDeleted = false,
            EntityType = typeof(OperationAuditTable).FullName ?? typeof(OperationAuditTable).Name,
            CreatedOn = entry.OccurredOn,
            LastUpdatedOn = entry.OccurredOn,
            OptimisticVersion = 0,
            ConcurrencyVersion = Guid.NewGuid(),
            TenantNId = entry.TenantNId,
            ActorUserNId = entry.ActorUserNId,
            Action = entry.Action,
            ObjectType = entry.ObjectType,
            ObjectNId = entry.ObjectNId,
            BeforeSummary = entry.BeforeSummary,
            AfterSummary = entry.AfterSummary,
            TraceId = entry.TraceId ?? string.Empty,
        };

        await _dbContext.SqlSugar.Insertable(row).ExecuteCommandAsync(cancellationToken);
    }
}
