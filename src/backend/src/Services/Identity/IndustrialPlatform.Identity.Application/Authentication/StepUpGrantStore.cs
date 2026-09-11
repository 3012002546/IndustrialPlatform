namespace IndustrialPlatform.Identity.Application.Authentication;

/// <summary>PF05 单次短时再认证证明的持久化端口；只保存证明哈希，不保存明文 proof。</summary>
public interface IStepUpGrantStore
{
    Task IssueAsync(StepUpGrant grant, CancellationToken cancellationToken);

    Task<StepUpGrantConsumeResult?> ConsumeAsync(
        string proofHash,
        string tenantNId,
        string actorUserNId,
        string actorSessionNId,
        string actorSecurityVersion,
        string action,
        string requestNId,
        string scopeChecksum,
        string requestHash,
        string consumingService,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed record StepUpGrantConsumeResult(string ReceiptNId, DateTimeOffset ConsumedOn);

public sealed record StepUpGrant(
    string TenantNId,
    string ActorUserNId,
    string ActorSessionNId,
    string ActorSecurityVersion,
    string Action,
    string RequestNId,
    string ScopeChecksum,
    string RequestHash,
    string BindingHash,
    string ProofHash,
    DateTimeOffset IssuedOn,
    DateTimeOffset ExpiresOn);
