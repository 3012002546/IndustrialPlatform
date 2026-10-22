using System.Text.Json;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Domain.Auditing;

namespace IndustrialPlatform.RuntimeSupport.Auditing;

/// <summary>文件变更与本地事实共享同一个执行上下文和事务；嵌入模式不加载SystemData控制面。</summary>
public sealed class RuntimeWriteTransaction(SqlSugarDbContext database) : ISystemDataWriteTransaction
{
    public async Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ado = database.SqlSugar.Ado;
        var owns = ado.IsNoTran();
        if (owns) ado.BeginTran();
        try { await action(); if (owns) ado.CommitTran(); }
        catch { if (owns) ado.RollbackTran(); throw; }
    }
}

/// <summary>写入可靠审计事实；故障直接使调用方同库事务回滚。</summary>
public sealed class RuntimeLocalAuditCommand(IAuditStore store, TimeProvider clock) : ILocalAuditCommand
{
    public Task RecordAsync(LocalAuditEntry entry, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var eventNId = "local-" + Guid.NewGuid().ToString("N");
        var payload = AuditPayloadRules.Sanitize(JsonSerializer.Serialize(new { entry.Reason, entry.BeforeSummary, entry.AfterSummary }), out _);
        var hash = AuditPayloadRules.ComputeIdempotencyHash("platform-studio", eventNId, now, entry.ActorUserNId, entry.Action, entry.ObjectType, entry.ObjectNId, payload, entry.TraceId, "Info", null, null);
        return store.InsertAsync(new(entry.TenantNId, "platform-studio", eventNId, now, now, entry.ActorUserNId, entry.Action, entry.ObjectType, entry.ObjectNId, payload, hash, entry.TraceId, "Info", null, null), cancellationToken);
    }
}
