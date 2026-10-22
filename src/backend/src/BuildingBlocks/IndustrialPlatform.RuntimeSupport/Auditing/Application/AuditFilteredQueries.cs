using System.Text;
using IndustrialPlatform.SystemData.Contracts.Auditing;
using IndustrialPlatform.SystemData.Domain.Auditing;

namespace IndustrialPlatform.SystemData.Application.Auditing;

/// <summary>审计拥有者公开的过滤查询扩展；actor/object在数据库中分页，不在截取结果后过滤。</summary>
public interface IAuditFilteredQueries
{
    Task<AuditFactPageV1> QueryFilteredAsync(string tenantNId, string requestingActor, string? producerServiceKey, string? action, DateTimeOffset? from, DateTimeOffset? until, string? actor, string? objectNId, int page, int pageSize, CancellationToken cancellationToken);
    Task<string> ExportFilteredCsvAsync(string tenantNId, string requestingActor, string? producerServiceKey, string? action, DateTimeOffset? from, DateTimeOffset? until, string? actor, string? objectNId, CancellationToken cancellationToken);
}
public interface IAuditFilteredStore
{
    Task<AuditFactPageV1> QueryFilteredAsync(string tenantNId, string? producerServiceKey, string? action, DateTimeOffset from, DateTimeOffset until, string? actor, string? objectNId, int page, int pageSize, CancellationToken cancellationToken);
}

public sealed partial class AuditService : IAuditFilteredQueries
{
    public async Task<AuditFactPageV1> QueryFilteredAsync(string tenantNId, string requestingActor, string? producerServiceKey, string? action, DateTimeOffset? from, DateTimeOffset? until, string? actor, string? objectNId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var range = FilterRange(from, until, actor, objectNId, producerServiceKey, action);
        if (page < 1 || pageSize is < 1 or > 200 || page > int.MaxValue / pageSize) throw Error("AUDIT_QUERY_SCOPE_INVALID", "审计分页范围无效。");
        await _store.RecordAccessAsync(tenantNId, requestingActor, "audit.query", $"producer={producerServiceKey};action={action};actor={actor};object={objectNId};from={range.From:O};until={range.Until:O}", cancellationToken);
        return await FilteredStore().QueryFilteredAsync(tenantNId, producerServiceKey, action, range.From, range.Until, actor, objectNId, page, pageSize, cancellationToken);
    }
    public async Task<string> ExportFilteredCsvAsync(string tenantNId, string requestingActor, string? producerServiceKey, string? action, DateTimeOffset? from, DateTimeOffset? until, string? actor, string? objectNId, CancellationToken cancellationToken)
    {
        var range = FilterRange(from, until, actor, objectNId, producerServiceKey, action);
        await _store.RecordAccessAsync(tenantNId, requestingActor, "audit.export", $"producer={producerServiceKey};action={action};actor={actor};object={objectNId};from={range.From:O};until={range.Until:O}", cancellationToken);
        var store = FilteredStore();
        var first = await store.QueryFilteredAsync(tenantNId, producerServiceKey, action, range.From, range.Until, actor, objectNId, 1, 200, cancellationToken);
        if (first.Total > MaxExportRows) throw Error("AUDIT_QUERY_SCOPE_INVALID", $"审计导出最多支持 {MaxExportRows} 条记录。");
        var builder = new StringBuilder("producerServiceKey;auditEventNId;occurredOn;actorUserNId;action;objectType;objectNId;severity;payloadJson\r\n");
        static void Append(StringBuilder output, IReadOnlyList<AuditFactV1> facts)
        {
            foreach (var fact in facts)
            {
                output.AppendJoin(';', AuditPayloadRules.SafeCsvCell(fact.ProducerServiceKey), AuditPayloadRules.SafeCsvCell(fact.AuditEventNId), AuditPayloadRules.SafeCsvCell(fact.OccurredOn.ToString("O", System.Globalization.CultureInfo.InvariantCulture)), AuditPayloadRules.SafeCsvCell(fact.ActorUserNId), AuditPayloadRules.SafeCsvCell(fact.Action), AuditPayloadRules.SafeCsvCell(fact.ObjectType), AuditPayloadRules.SafeCsvCell(fact.ObjectNId), AuditPayloadRules.SafeCsvCell(fact.Severity), AuditPayloadRules.SafeCsvCell(fact.PayloadJson));
                output.Append("\r\n");
            }
        }
        Append(builder, first.Items);
        var count = first.Items.Count;
        for (var page = 2; count < first.Total; page++)
        {
            var next = await store.QueryFilteredAsync(tenantNId, producerServiceKey, action, range.From, range.Until, actor, objectNId, page, 200, cancellationToken);
            if (next.Items.Count == 0) break;
            if (count + next.Items.Count > MaxExportRows) throw Error("AUDIT_QUERY_SCOPE_INVALID", "审计导出超过安全预算。");
            Append(builder, next.Items); count += next.Items.Count;
        }
        return builder.ToString();
    }
    private IAuditFilteredStore FilteredStore() => _store as IAuditFilteredStore ?? throw Error("AUDIT_QUERY_UNAVAILABLE", "审计过滤持久端口未配置。", 503);
    private (DateTimeOffset From, DateTimeOffset Until) FilterRange(DateTimeOffset? from, DateTimeOffset? until, string? actor, string? objectNId, string? producerServiceKey, string? action)
    {
        if (actor?.Length > 128 || objectNId?.Length > 128 || actor?.Any(char.IsControl) == true || objectNId?.Any(char.IsControl) == true) throw Error("AUDIT_QUERY_SCOPE_INVALID", "审计过滤标识无效。");
        if (producerServiceKey?.Length > 64 || action?.Length > 128 || producerServiceKey?.Any(char.IsControl) == true || action?.Any(char.IsControl) == true) throw Error("AUDIT_QUERY_SCOPE_INVALID", "审计动作或生产者过滤无效。");
        var to = (until ?? _clock.GetUtcNow()).ToUniversalTime();
        if (from is null && to < DateTimeOffset.MinValue.AddDays(366)) throw Error("AUDIT_QUERY_SCOPE_INVALID", "审计时间范围无效。");
        var start = (from ?? to.AddDays(-366)).ToUniversalTime();
        if (to < start || to - start > TimeSpan.FromDays(366)) throw Error("AUDIT_QUERY_SCOPE_INVALID", "审计查询时间范围不能超过366天。");
        return (start, to);
    }
}
