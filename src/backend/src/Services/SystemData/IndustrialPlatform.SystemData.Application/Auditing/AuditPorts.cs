using IndustrialPlatform.SystemData.Contracts.Auditing;

namespace IndustrialPlatform.SystemData.Application.Auditing;

public sealed record AuditFactRecord(
    string TenantNId,
    string ProducerServiceKey,
    string AuditEventNId,
    DateTimeOffset OccurredOn,
    DateTimeOffset ReceivedOn,
    string? ActorUserNId,
    string Action,
    string ObjectType,
    string? ObjectNId,
    string PayloadJson,
    string PayloadHash,
    string? TraceId,
    string Severity,
    string? SourceIp,
    string? UserAgent);

public sealed record AuditLifecycleUpdate(
    string TenantNId,
    string ProducerServiceKey,
    string AuditEventNId,
    string State,
    DateTimeOffset? RetentionUntil,
    bool? LegalHold,
    string? LegalHoldReason,
    DateTimeOffset ChangedOn);

public sealed record AuditLifecycleRecord(
    string TenantNId,
    string ProducerServiceKey,
    string AuditEventNId,
    string State,
    DateTimeOffset? RetentionUntil,
    bool LegalHold,
    string? LegalHoldReason,
    DateTimeOffset ChangedOn);

public sealed record AuditIngressFailureRecord(
    string FailureNId,
    string TenantNId,
    string ProducerServiceKey,
    string? AuditEventNId,
    string ErrorCode,
    string ErrorSummary,
    string? PayloadHash,
    DateTimeOffset OccurredOn);

public static class AuditLifecyclePolicy
{
    public static bool CanDelete(string state, DateTimeOffset? retentionUntil, bool legalHold, DateTimeOffset now) =>
        !legalHold && string.Equals(state, "Deleted", StringComparison.Ordinal) && retentionUntil is not null && retentionUntil <= now;
}

public interface IAuditFailureSink
{
    Task EnqueueAsync(AuditIngressFailureRecord failure, CancellationToken cancellationToken);
    IAsyncEnumerable<AuditIngressFailureRecord> ReadPendingAsync(CancellationToken cancellationToken);
    Task AcknowledgeAsync(AuditIngressFailureRecord failure, CancellationToken cancellationToken);
}

public sealed class NoopAuditFailureSink : IAuditFailureSink
{
    public Task EnqueueAsync(AuditIngressFailureRecord failure, CancellationToken cancellationToken) => Task.CompletedTask;
    public async IAsyncEnumerable<AuditIngressFailureRecord> ReadPendingAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        yield break;
    }
    public Task AcknowledgeAsync(AuditIngressFailureRecord failure, CancellationToken cancellationToken) => Task.CompletedTask;
}

public interface IAuditStore
{
    Task<AuditFactRecord?> GetAsync(string tenantNId, string producerServiceKey, string auditEventNId, CancellationToken cancellationToken);
    Task InsertAsync(AuditFactRecord fact, CancellationToken cancellationToken);
    Task RecordIngressFailureAsync(string tenantNId, string producerServiceKey, string? auditEventNId, string errorCode, string errorSummary, string? payloadHash, CancellationToken cancellationToken);
    Task RecordAccessAsync(string tenantNId, string actorUserNId, string action, string scope, CancellationToken cancellationToken);
    Task<AuditFactPageV1> QueryAsync(string tenantNId, string? producerServiceKey, string? action, DateTimeOffset? from, DateTimeOffset? until, int page, int pageSize, CancellationToken cancellationToken);
    Task<AuditLifecycleRecord?> GetLifecycleAsync(string tenantNId, string producerServiceKey, string auditEventNId, CancellationToken cancellationToken);
    Task UpdateLifecycleAsync(AuditLifecycleUpdate update, CancellationToken cancellationToken);
    Task RecordLifecycleAuditAsync(string tenantNId, string producerServiceKey, string auditEventNId, string action, string reason, CancellationToken cancellationToken);
    Task<int> CleanupAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> RecoverOutboxAsync(string tenantNId, Guid eventId, CancellationToken cancellationToken);
}

public interface IAuditService
{
    Task<AuditFactV1> IngestAsync(string tenantNId, AuditFactIngestRequest request, CancellationToken cancellationToken);
    Task<AuditFactPageV1> QueryAsync(string tenantNId, string actorUserNId, string? producerServiceKey, string? action, DateTimeOffset? from, DateTimeOffset? until, int page, int pageSize, CancellationToken cancellationToken);
    Task<AuditFactV1?> GetAsync(string tenantNId, string actorUserNId, string producerServiceKey, string auditEventNId, CancellationToken cancellationToken);
    Task<string> ExportCsvAsync(string tenantNId, string actorUserNId, string? producerServiceKey, string? action, DateTimeOffset? from, DateTimeOffset? until, CancellationToken cancellationToken);
    Task UpdateLifecycleAsync(string tenantNId, string actorUserNId, string producerServiceKey, string auditEventNId, AuditLifecycleRequest request, CancellationToken cancellationToken);
    Task<bool> RecoverOutboxAsync(string tenantNId, string actorUserNId, Guid eventId, CancellationToken cancellationToken);
}
