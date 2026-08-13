using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;
using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Outbox;

/// <summary>
/// Outbox 行写入助手(§20):把应用层构建的信封批量映射为 identity_outbox 行并插入。
/// 只负责映射与插入;事务边界由调用方仓储的 BeginTran/CommitTran 提供,
/// 保证业务写入、操作审计与 Outbox 在同一数据库事务提交(发布至少一次)。
/// </summary>
public static class OutboxRows
{
    /// <summary>
    /// 插入待发布事件信封(空集合或 <c>null</c> 直接返回,不产生任何写入)。
    /// </summary>
    /// <param name="sugar">当前事务上下文中的 SqlSugar 客户端。</param>
    /// <param name="envelopes">应用层构建的集成事件信封。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task InsertAsync(
        ISqlSugarClient sugar,
        IReadOnlyCollection<OutboxEnvelope>? envelopes,
        CancellationToken cancellationToken)
    {
        if (envelopes is null || envelopes.Count == 0)
        {
            return;
        }

        var rows = envelopes.Select(envelope => new OutboxTable
        {
            EventId = envelope.EventId,
            EventType = envelope.EventType,
            EventVersion = envelope.EventVersion,
            Payload = envelope.Payload,
            EventCreatedTime = envelope.EventCreatedTime,
            PublishedOn = null,
            RetryCount = 0,
            LastError = null,
        }).ToList();

        await sugar.Insertable(rows).ExecuteCommandAsync(cancellationToken);
    }
}
