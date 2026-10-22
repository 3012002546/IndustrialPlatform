using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Contracts;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Application;

/// <summary>Publishing拥有的资源/修订应用端口；消费者不得跨模块访问仓储。</summary>
public interface IStudioResourceStore
{
    Task<T> TransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken);
    Task<StudioResource?> FindResourceByIdAsync(string tenantNId, Guid id, CancellationToken cancellationToken);
    Task<StudioResource?> FindResourceAsync(string tenantNId, string nId, CancellationToken cancellationToken);
    Task<IReadOnlyList<StudioResource>> ListResourcesAsync(StudioPrincipal principal, string kind, StudioQuery query, CancellationToken cancellationToken);
    Task<long> CountResourcesAsync(StudioPrincipal principal, string kind, StudioQuery query, CancellationToken cancellationToken);
    Task InsertResourceAsync(StudioResource resource, CancellationToken cancellationToken);
    Task<bool> UpdateResourceAsync(StudioResource resource, long expectedVersion, Guid expectedConcurrencyVersion, CancellationToken cancellationToken);
    Task<StudioRevision?> FindRevisionAsync(string tenantNId, string nId, CancellationToken cancellationToken);
    Task<StudioRevision?> FindRevisionByIdAsync(string tenantNId, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<StudioRevision>> ListRevisionsAsync(string tenantNId, Guid resourceId, CancellationToken cancellationToken);
    Task InsertRevisionAsync(StudioRevision revision, CancellationToken cancellationToken);
    Task<IReadOnlyList<StudioGrant>> GetGrantsAsync(string tenantNId, Guid resourceId, CancellationToken cancellationToken);
    Task ReplaceGrantsAsync(string tenantNId, Guid resourceId, IReadOnlyList<StudioGrant> grants, CancellationToken cancellationToken);
    Task<StudioSource?> FindSourceAsync(string tenantNId, string nId, CancellationToken cancellationToken);
    Task<IReadOnlyList<StudioSource>> ListSourcesAsync(string tenantNId, StudioQuery query, CancellationToken cancellationToken);
    Task<long> CountSourcesAsync(string tenantNId, StudioQuery query, CancellationToken cancellationToken);
    Task InsertSourceAsync(StudioSource source, CancellationToken cancellationToken);
    Task<bool> UpdateSourceAsync(StudioSource source, long expectedVersion, Guid concurrencyVersion, CancellationToken cancellationToken);
    Task<StudioOperationReceipt?> FindReceiptAsync(string tenantNId, string kind, string operationKey, CancellationToken cancellationToken);
    Task InsertReceiptAsync(StudioOperationReceipt receipt, CancellationToken cancellationToken);
    Task<bool> ClaimReceiptAsync(Guid id, string tenantNId, CancellationToken cancellationToken);
    Task UpdateReceiptAsync(StudioOperationReceipt receipt, CancellationToken cancellationToken);
    Task AppendAuditAsync(StudioPrincipal principal, string action, string resourceNId, JsonObject summary, CancellationToken cancellationToken);
    Task<IReadOnlyList<StudioAuditOutbox>> PendingAuditsAsync(int count, CancellationToken cancellationToken);
    Task MarkAuditSentAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken);
    Task MarkAuditFailedAsync(Guid id, int attempts, DateTimeOffset nextAttempt, string code, CancellationToken cancellationToken);
    Task<long> CountLedgerRecordsAsync(string tenantNId, Guid modelId, CancellationToken cancellationToken);
    Task<StudioLedgerRecord?> FindLedgerRecordAsync(string tenantNId, Guid modelId, string nId, CancellationToken cancellationToken);
    Task<IReadOnlyList<StudioLedgerRecord>> LoadLedgerRecordsAsync(string tenantNId, Guid modelId, CancellationToken cancellationToken);
    Task<StudioPage<StudioLedgerRecord>> QueryLedgerAsync(string tenantNId, Guid modelId, LedgerQuery query, IReadOnlyDictionary<string, string> indexColumns, CancellationToken cancellationToken);
    Task InsertLedgerRecordAsync(StudioLedgerRecord record, CancellationToken cancellationToken);
    Task<bool> UpdateLedgerRecordAsync(StudioLedgerRecord record, long expectedVersion, Guid concurrencyVersion, CancellationToken cancellationToken);
    Task ReplaceLedgerIndexesAsync(Guid recordId, IReadOnlyList<StudioLedgerValue> indexes, CancellationToken cancellationToken);
    Task<bool> IsResourceReferencedAsync(string tenantNId, string resourceNId, CancellationToken cancellationToken);
}

/// <summary>身份消费端口；原生与外部适配必须生成同一可信上下文。</summary>
public interface IStudioIdentity
{
    Task<StudioPrincipal> GetCurrentAsync(CancellationToken cancellationToken);
    Task<StudioPrincipal> RefreshAsync(StudioPrincipal principal, CancellationToken cancellationToken);
}

public interface IStudioSourceExecutor
{
    string ConfigurationHash(StudioSourceEndpoint endpoint);
    Task<JsonObject> TestAsync(StudioSource source, CancellationToken cancellationToken);
    Task<StudioSourceResult> ExecuteAsync(StudioPrincipal principal, StudioSource source, JsonObject document, JsonObject parameters, bool preview, CancellationToken cancellationToken);
}
public sealed record StudioSourceResult(JsonNode? Data, DateTimeOffset ObservedOn, DateTimeOffset ReceivedOn, long RowCount, long ElapsedMs, JsonObject? PageInfo = null);

/// <summary>可靠审计接收适配：原生接平台公开端口，独立模式写Embedded持久事实。</summary>
public interface IStudioAudit
{
    Task IngestAsync(StudioAuditOutbox item, CancellationToken cancellationToken);
}

/// <summary>台账附件消费公开文件能力，不凭台账可见性扩大文件权限。</summary>
public interface IStudioFiles
{
    bool Enabled { get; }
    Task ValidateReferenceAsync(StudioPrincipal principal, string fileNId, string referenceNId, CancellationToken cancellationToken);
}

/// <summary>代码白名单动作；真实执行与写动作模拟具有不同入口。</summary>
public interface IStudioAction
{
    string Key { get; }
    string ExecutorVersion { get; }
    string PermissionKey { get; }
    bool Mutating { get; }
    JsonObject InputSchema { get; }
    JsonObject OutputSchema { get; }
    Task<JsonNode?> PreviewAsync(StudioPrincipal principal, JsonObject input, CancellationToken cancellationToken);
    Task<StudioActionResult> ExecuteAsync(StudioPrincipal principal, string operationKey, JsonObject input, CancellationToken cancellationToken);
    Task<StudioActionResult?> GetByOperationAsync(StudioPrincipal principal, string operationKey, CancellationToken cancellationToken);
}
public sealed record StudioActionResult(string Status, JsonNode? Result, string? ExternalReference = null, string? ErrorCode = null);

public interface IStudioAuthorization
{
    void RequirePermission(StudioPrincipal principal, string permission);
    Task RequireAsync(StudioPrincipal principal, StudioResource resource, string permission, string capability, CancellationToken cancellationToken);
}
