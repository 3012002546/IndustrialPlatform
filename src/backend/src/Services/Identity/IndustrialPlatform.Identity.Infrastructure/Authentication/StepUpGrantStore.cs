using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;
using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Authentication;

public sealed class StepUpGrantStore : IStepUpGrantStore
{
    private readonly SqlSugarDbContext _dbContext;

    public StepUpGrantStore(SqlSugarDbContext dbContext) => _dbContext = dbContext;

    public async Task IssueAsync(StepUpGrant grant, CancellationToken cancellationToken)
    {
        await _dbContext.SqlSugar.Insertable(new StepUpGrantTable
        {
            Id = Guid.NewGuid(),
            TenantNId = grant.TenantNId,
            ActorUserNId = grant.ActorUserNId,
            ActorSessionNId = grant.ActorSessionNId,
            ActorSecurityVersion = grant.ActorSecurityVersion,
            Action = grant.Action,
            RequestNId = grant.RequestNId,
            ScopeChecksum = grant.ScopeChecksum,
            RequestHash = grant.RequestHash,
            BindingHash = grant.BindingHash,
            ProofHash = grant.ProofHash,
            IssuedOn = grant.IssuedOn,
            ExpiresOn = grant.ExpiresOn,
        }).ExecuteCommandAsync(cancellationToken);
    }

    public async Task<StepUpGrantConsumeResult?> ConsumeAsync(string proofHash, string tenantNId, string actorUserNId, string actorSessionNId, string actorSecurityVersion, string action, string requestNId, string scopeChecksum, string requestHash, string consumingService, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<StepUpGrantTable>()
            .Where(item => item.ProofHash == proofHash && item.TenantNId == tenantNId && item.ActorUserNId == actorUserNId && item.ActorSessionNId == actorSessionNId && item.ActorSecurityVersion == actorSecurityVersion && item.Action == action && item.RequestNId == requestNId && item.ScopeChecksum == scopeChecksum && item.RequestHash == requestHash)
            .FirstAsync(cancellationToken);
        if (row is null || IsExpired(row.ExpiresOn, now))
            return null;
        if (row.ConsumedOn is not null)
            return string.Equals(row.ConsumedByService, consumingService, StringComparison.Ordinal)
                ? new StepUpGrantConsumeResult(row.ConsumedReceiptNId ?? Receipt(row.Id, consumingService), row.ConsumedOn.Value)
                : null;
        var receipt = Receipt(row.Id, consumingService);
        var affected = await _dbContext.SqlSugar.Updateable<StepUpGrantTable>()
            .SetColumns(item => new StepUpGrantTable { ConsumedOn = now, ConsumedByService = consumingService, ConsumedReceiptNId = receipt })
            .Where(item => item.Id == row.Id && item.ConsumedOn == null)
            .ExecuteCommandAsync(cancellationToken);
        if (affected == 1)
            return new StepUpGrantConsumeResult(receipt, now);
        var consumed = await _dbContext.SqlSugar.Queryable<StepUpGrantTable>()
            .Where(item => item.Id == row.Id)
            .FirstAsync(cancellationToken);
        return consumed?.ConsumedOn is not null
            && string.Equals(consumed.ConsumedByService, consumingService, StringComparison.Ordinal)
            ? new StepUpGrantConsumeResult(consumed.ConsumedReceiptNId ?? receipt, consumed.ConsumedOn.Value)
            : null;
    }

    private static string Receipt(Guid id, string service) => "SUR-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{id:N}:{service}"))).ToLowerInvariant()[..32];

    private bool IsExpired(DateTimeOffset expiresOn, DateTimeOffset now) =>
        _dbContext.SqlSugar.CurrentConnectionConfig.DbType == DbType.Sqlite
            ? expiresOn.DateTime <= now.UtcDateTime
            : expiresOn <= now;

    public static string HashProof(string proof) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(proof))).ToLowerInvariant();
}
