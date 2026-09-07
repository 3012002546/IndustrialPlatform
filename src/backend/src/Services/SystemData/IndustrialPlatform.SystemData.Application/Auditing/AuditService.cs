using System.Text;
using IndustrialPlatform.SystemData.Application.Pf04;
using IndustrialPlatform.SystemData.Contracts.Auditing;
using IndustrialPlatform.SystemData.Domain.Auditing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IndustrialPlatform.SystemData.Application.Auditing;

public sealed partial class AuditService : IAuditService
{
    private const int MaxExportRows = 10_000;
    private readonly IAuditStore _store;
    private readonly IAuditFailureSink _failureSink;
    private readonly ISystemDataWriteTransaction _transaction;
    private readonly ILogger<AuditService> _logger;
    private readonly TimeProvider _clock;

    public AuditService(
        IAuditStore store,
        TimeProvider clock,
        IAuditFailureSink? failureSink = null,
        ISystemDataWriteTransaction? transaction = null,
        ILogger<AuditService>? logger = null)
    {
        _store = store;
        _clock = clock;
        _failureSink = failureSink ?? new NoopAuditFailureSink();
        _transaction = transaction ?? new NoopSystemDataWriteTransaction();
        _logger = logger ?? NullLogger<AuditService>.Instance;
    }

    public async Task<AuditFactV1> IngestAsync(string tenantNId, AuditFactIngestRequest request, CancellationToken cancellationToken)
    {
        var producer = Require(request.ProducerServiceKey, "AUDIT_PRODUCER_REQUIRED", "审计生产者不能为空。");
        var eventNId = Require(request.AuditEventNId, "AUDIT_EVENT_REQUIRED", "审计事件标识不能为空。");
        var action = Require(request.Action, "AUDIT_ACTION_REQUIRED", "审计动作不能为空。");
        var objectType = Require(request.ObjectType, "AUDIT_OBJECT_REQUIRED", "审计对象类型不能为空。");
        string payload;
        try { payload = AuditPayloadRules.Sanitize(request.PayloadJson, out _); }
        catch (ArgumentException exception) { throw Error("AUDIT_PAYLOAD_INVALID", exception.Message, 422); }
        var occurredOn = request.OccurredOn ?? _clock.GetUtcNow();
        var severity = string.IsNullOrWhiteSpace(request.Severity) ? "Info" : request.Severity.Trim();
        var payloadHash = AuditPayloadRules.ComputeIdempotencyHash(producer, eventNId, occurredOn, request.ActorUserNId, action, objectType, request.ObjectNId, payload, request.TraceId, severity, request.SourceIp, request.UserAgent);
        var fact = new AuditFactRecord(tenantNId, producer, eventNId, occurredOn, _clock.GetUtcNow(), request.ActorUserNId, action, objectType, request.ObjectNId, payload, payloadHash, request.TraceId, severity, request.SourceIp, request.UserAgent);
        try
        {
            var existing = await _store.GetAsync(tenantNId, producer, eventNId, cancellationToken);
            if (existing is not null)
            {
                if (!string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal)) throw Error("AUDIT_FACT_CONFLICT", "审计事件标识已绑定不同 payload。", 409);
                return ToContract(existing);
            }

            await _store.InsertAsync(fact, cancellationToken);
        }
        catch (Pf04ServiceException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var failure = new AuditIngressFailureRecord($"failure-{Guid.NewGuid():N}", tenantNId, producer, eventNId, "AUDIT_WRITE_UNAVAILABLE", "审计事实写入失败。", payloadHash, _clock.GetUtcNow());
            try { await _failureSink.EnqueueAsync(failure, CancellationToken.None); }
            catch (Exception spoolException)
            {
                LogFailureSpoolEnqueueFailed(_logger, failure.FailureNId, spoolException);
            }
            throw Error("AUDIT_WRITE_UNAVAILABLE", "审计事实写入失败。", 503);
        }
        return ToContract(fact);
    }

    public async Task<AuditFactPageV1> QueryAsync(string tenantNId, string actorUserNId, string? producerServiceKey, string? action, DateTimeOffset? from, DateTimeOffset? until, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (from is not null && until is not null && until < from) throw Error("AUDIT_QUERY_SCOPE_INVALID", "审计查询时间范围无效。");
        var scopeUntil = until ?? _clock.GetUtcNow();
        var scopeFrom = from ?? scopeUntil.AddDays(-366);
        if (scopeUntil - scopeFrom > TimeSpan.FromDays(366)) throw Error("AUDIT_QUERY_SCOPE_INVALID", "审计查询时间范围不能超过 366 天。");
        await _store.RecordAccessAsync(tenantNId, actorUserNId, "audit.query", $"producer={producerServiceKey ?? "*"};action={action ?? "*"};from={scopeFrom:O};until={scopeUntil:O}", cancellationToken);
        return await _store.QueryAsync(tenantNId, producerServiceKey, action, scopeFrom, scopeUntil, Math.Max(1, page), Math.Clamp(pageSize, 1, 200), cancellationToken);
    }

    public async Task<AuditFactV1?> GetAsync(string tenantNId, string actorUserNId, string producerServiceKey, string auditEventNId, CancellationToken cancellationToken)
    {
        await _store.RecordAccessAsync(tenantNId, actorUserNId, "audit.get", $"producer={producerServiceKey};event={auditEventNId}", cancellationToken);
        return ToContractOrNull(await _store.GetAsync(tenantNId, producerServiceKey, auditEventNId, cancellationToken));
    }

    public async Task<string> ExportCsvAsync(string tenantNId, string actorUserNId, string? producerServiceKey, string? action, DateTimeOffset? from, DateTimeOffset? until, CancellationToken cancellationToken)
    {
        if (from is not null && until is not null && until < from) throw Error("AUDIT_QUERY_SCOPE_INVALID", "审计查询时间范围无效。");
        var scopeUntil = until ?? _clock.GetUtcNow();
        var scopeFrom = from ?? scopeUntil.AddDays(-366);
        if (scopeUntil - scopeFrom > TimeSpan.FromDays(366)) throw Error("AUDIT_QUERY_SCOPE_INVALID", "审计查询时间范围不能超过 366 天。");
        await _store.RecordAccessAsync(tenantNId, actorUserNId, "audit.export", $"producer={producerServiceKey ?? "*"};action={action ?? "*"};from={scopeFrom:O};until={scopeUntil:O}", cancellationToken);
        var page = await _store.QueryAsync(tenantNId, producerServiceKey, action, scopeFrom, scopeUntil, 1, 200, cancellationToken);
        if (page.Total > MaxExportRows) throw Error("AUDIT_QUERY_SCOPE_INVALID", $"审计导出最多支持 {MaxExportRows} 条记录。");
        var builder = new StringBuilder("producerServiceKey;auditEventNId;occurredOn;action;objectType;objectNId;severity;payloadJson\r\n");
        var items = page.Items.ToList();
        for (var nextPage = 2; items.Count < page.Total; nextPage++)
        {
            var next = await _store.QueryAsync(tenantNId, producerServiceKey, action, scopeFrom, scopeUntil, nextPage, 200, cancellationToken);
            if (next.Items.Count == 0) break;
            items.AddRange(next.Items);
        }
        foreach (var fact in items)
        {
            builder.AppendJoin(';', AuditPayloadRules.SafeCsvCell(fact.ProducerServiceKey), AuditPayloadRules.SafeCsvCell(fact.AuditEventNId), AuditPayloadRules.SafeCsvCell(fact.OccurredOn.ToString("O")), AuditPayloadRules.SafeCsvCell(fact.Action), AuditPayloadRules.SafeCsvCell(fact.ObjectType), AuditPayloadRules.SafeCsvCell(fact.ObjectNId), AuditPayloadRules.SafeCsvCell(fact.Severity), AuditPayloadRules.SafeCsvCell(fact.PayloadJson));
            builder.Append("\r\n");
        }
        return builder.ToString();
    }

    public async Task UpdateLifecycleAsync(string tenantNId, string actorUserNId, string producerServiceKey, string auditEventNId, AuditLifecycleRequest request, CancellationToken cancellationToken)
    {
        var state = request.State?.Trim();
        if (state is not ("Active" or "Archived" or "Deleted")) throw Error("AUDIT_LIFECYCLE_INVALID", "审计生命周期状态必须是 Active、Archived 或 Deleted。", 422);
        if (await _store.GetAsync(tenantNId, producerServiceKey, auditEventNId, cancellationToken) is null) throw Error("AUDIT_NOT_FOUND", "审计事实不存在。", 404);
        var current = await _store.GetLifecycleAsync(tenantNId, producerServiceKey, auditEventNId, cancellationToken) ?? throw Error("AUDIT_LIFECYCLE_NOT_FOUND", "审计生命周期记录不存在。", 409);
        var legalHold = request.LegalHold ?? current.LegalHold;
        if (legalHold && string.IsNullOrWhiteSpace(request.LegalHoldReason) && request.LegalHold is true)
            throw Error("AUDIT_LEGAL_HOLD_REASON_REQUIRED", "启用审计保全必须提供原因。", 422);
        if (state == "Deleted" && legalHold)
            throw Error("AUDIT_LEGAL_HOLD_ACTIVE", "审计事实处于法律保全状态，禁止删除。", 409);
        if (state == "Deleted" && request.RetentionUntil is null)
            throw Error("AUDIT_RETENTION_REQUIRED", "删除状态必须明确保留期截止时间。", 422);
        await _transaction.ExecuteAsync(async () =>
        {
            await _store.UpdateLifecycleAsync(new AuditLifecycleUpdate(tenantNId, producerServiceKey, auditEventNId, state, request.RetentionUntil, request.LegalHold, request.LegalHold == false ? null : request.LegalHoldReason, _clock.GetUtcNow()), cancellationToken);
            await _store.RecordAccessAsync(tenantNId, actorUserNId, "audit.lifecycle", $"producer={producerServiceKey};event={auditEventNId};state={state}", cancellationToken);
        }, cancellationToken);
    }

    public async Task<bool> RecoverOutboxAsync(string tenantNId, string actorUserNId, Guid eventId, CancellationToken cancellationToken)
    {
        var recovered = await _store.RecoverOutboxAsync(tenantNId, eventId, cancellationToken);
        if (recovered) await _store.RecordAccessAsync(tenantNId, actorUserNId, "audit.outbox.recover", eventId.ToString("D"), cancellationToken);
        return recovered;
    }

    private static string Require(string? value, string code, string message) => string.IsNullOrWhiteSpace(value) ? throw Error(code, message) : value.Trim();
    private static Pf04ServiceException Error(string code, string message, int status = 400) => new(code, message, status);
    private static AuditFactV1? ToContractOrNull(AuditFactRecord? fact) => fact is null ? null : ToContract(fact);
    private static AuditFactV1 ToContract(AuditFactRecord fact) => new()
    {
        TenantNId = fact.TenantNId,
        ProducerServiceKey = fact.ProducerServiceKey,
        AuditEventNId = fact.AuditEventNId,
        OccurredOn = fact.OccurredOn,
        ReceivedOn = fact.ReceivedOn,
        ActorUserNId = fact.ActorUserNId,
        Action = fact.Action,
        ObjectType = fact.ObjectType,
        ObjectNId = fact.ObjectNId,
        PayloadJson = fact.PayloadJson,
        TraceId = fact.TraceId ?? string.Empty,
        Severity = fact.Severity
    };

    [LoggerMessage(EventId = 409, Level = LogLevel.Error, Message = "审计事实写入失败且独立失败落盘也失败: {FailureNId}。")]
    private static partial void LogFailureSpoolEnqueueFailed(ILogger logger, string failureNId, Exception exception);
}
