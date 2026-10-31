using IndustrialPlatform.PlatformStudio.Application.Ledger;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Initialization;

/// <summary>分批补齐历史删除记录的类型索引，不改业务真值、版本、审计或唯一占用。</summary>
public static class StudioDeletedLedgerIndexes
{
    public const string Step = "STU-010-DeletedLedgerIndexes";

    public static async Task RebuildAsync(StudioDatabase database, TimeProvider clock, CancellationToken ct)
    {
        var store = new StudioResourceStore(database, clock);
        Guid? after = null;
        while (true)
        {
            var query = database.Client.Queryable<StudioLedgerRecord>().Where(row => row.IsDeleted);
            if (after is { } id) query = query.Where("id > @after", new { after = id });
            var rows = await query.OrderBy(row => row.Id).ToPageListAsync(1, 200, ct);
            foreach (var row in rows)
            {
                StudioSqliteUtc.Normalize(row, database.Provider);
                var model = await store.FindResourceByIdAsync(row.TenantNId, row.ModelResourceId, ct)
                    ?? throw new StudioException("DEPENDENCY_CHANGED", "历史台账模型不存在。", 409);
                var revision = await store.FindRevisionByIdAsync(row.TenantNId, model.PublishedRevisionId ?? row.SchemaRevisionId, ct)
                    ?? throw new StudioException("DEPENDENCY_CHANGED", "历史台账模型修订不存在。", 409);
                await store.TransactionAsync(async () =>
                {
                    // CAS同时取得行写锁；并发恢复/编辑已改变版本时不覆盖其索引。
                    if (await store.UpdateLedgerRecordAsync(row, row.OptimisticVersion, row.ConcurrencyVersion, ct))
                        await store.ReplaceLedgerIndexesAsync(row.Id, StudioLedger.Indexes(row, StudioPublishing.Parse(revision.DocumentJson)), ct);
                    return true;
                }, ct);
            }
            if (rows.Count < 200) break;
            after = rows[^1].Id;
        }
    }
}
