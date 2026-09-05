using System.Data.Common;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Application.Caching;
using IndustrialPlatform.ReferenceData.Application.Dictionary;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Domain.Dictionary;
using IndustrialPlatform.ReferenceData.Contracts.Dictionary;
using IndustrialPlatform.ReferenceData.Contracts.Events;
using IndustrialPlatform.ReferenceData.Infrastructure.Caching;
using IndustrialPlatform.ReferenceData.Infrastructure.Outbox;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Infrastructure.Dictionary;

public sealed class DictionaryRepository(SqlSugarDbContext context) : IDictionaryRepository
{
    private ISqlSugarClient Db => context.SqlSugar;
    private string Definitions => Db.CurrentConnectionConfig.DbType == DbType.PostgreSQL ? "reference_data.dictionary_definition" : "reference_data_dictionary_definition";
    private string Items => Db.CurrentConnectionConfig.DbType == DbType.PostgreSQL ? "reference_data.dictionary_item" : "reference_data_dictionary_item";

    private ISugarQueryable<DictionaryRow> Visible(string tenantNId) => Db.Queryable<DictionaryRow>().AS(Definitions)
        .Where(row => !row.IsDeleted && (row.TenantNId == tenantNId || row.TenantNId == null));

    public async Task<(IReadOnlyList<DictionarySummaryDto> Items, long Total)> SearchAsync(string tenantNId, DictionaryQuery query, CancellationToken cancellationToken)
    {
        var filter = Visible(tenantNId);
        if (query.Keyword is { Length: > 0 } keyword)
        {
            keyword = keyword.ToUpperInvariant();
            filter = filter.Where(row => row.NId.Contains(keyword) || SqlFunc.Contains(SqlFunc.ToUpper(row.Name), keyword));
        }
        filter = query.Status is null ? filter.Where(row => row.Status == "Draft" || row.Status == "Published") : filter.Where(row => row.Status == query.Status);
        if (query.ScopeType is not null) filter = filter.Where(row => row.ScopeType == query.ScopeType);
        var direction = query.Descending ? OrderByType.Desc : OrderByType.Asc;
        filter = query.SortField switch
        {
            "nId" => filter.OrderBy(row => row.NId, direction),
            "name" => filter.OrderBy(row => row.Name, direction),
            "revision" => filter.OrderBy(row => row.Revision, direction),
            "status" => filter.OrderBy(row => row.Status, direction),
            "lastUpdatedOn" => filter.OrderBy(row => row.LastUpdatedOn, direction),
            _ => filter.OrderBy(row => row.LastUpdatedOn, OrderByType.Desc),
        };
        var total = await filter.CountAsync(cancellationToken);
        var rows = await filter.OrderBy(row => row.Id).ToPageListAsync(query.PageIndex, query.PageSize, cancellationToken);
        var ids = rows.Select(row => row.Id).ToArray();
        var enabled = ids.Length == 0 ? [] : await Db.Queryable<DictionaryItemRow>().AS(Items)
            .Where(row => ids.Contains(row.DictionaryDefinitionId) && row.Enabled && !row.IsDeleted)
            .GroupBy(row => row.DictionaryDefinitionId)
            .Select(row => new DictionaryItemCount { DictionaryDefinitionId = row.DictionaryDefinitionId, Count = SqlFunc.AggregateCount(row.Id) })
            .ToListAsync(cancellationToken);
        return (rows.Select(row => new DictionarySummaryDto(row.Id, row.NId, row.Name, row.ScopeType, row.TenantNId, row.Revision, row.Status,
            enabled.Find(item => item.DictionaryDefinitionId == row.Id)?.Count ?? 0, row.OptimisticVersion, row.ConcurrencyVersion,
            Timestamp(row.LastUpdatedOn), row.PublishedBy, row.IsFrozen, row.IsLocked)).ToArray(), total);
    }

    public async Task<DictionaryDefinition?> GetAsync(string tenantNId, Guid id, CancellationToken cancellationToken)
    {
        var rows = await Visible(tenantNId).Where(row => row.Id == id).ToListAsync(cancellationToken);
        var loaded = await LoadItemsAsync(rows, cancellationToken);
        return loaded.Count == 0 ? null : loaded[0];
    }

    public async Task<DictionaryDefinition?> GetEffectiveAsync(string tenantNId, string nId, CancellationToken cancellationToken)
    {
        var rows = await Visible(tenantNId).Where(row => row.NId == nId && row.Status == "Published")
            .OrderByDescending(row => row.TenantNId).ToListAsync(cancellationToken);
        var selected = rows.FirstOrDefault(row => row.TenantNId == tenantNId) ?? rows.FirstOrDefault(row => row.TenantNId is null);
        return selected is null ? null : (await LoadItemsAsync([selected], cancellationToken))[0];
    }

    public async Task<DictionaryDefinition?> GetPublishedInScopeAsync(DictionaryDefinition definition, CancellationToken cancellationToken)
    {
        var rows = await Db.Queryable<DictionaryRow>().AS(Definitions)
            .Where(row => !row.IsDeleted && row.TenantNId == definition.TenantNId && row.NId == definition.NId && row.Status == "Published")
            .ToListAsync(cancellationToken);
        var loaded = await LoadItemsAsync(rows, cancellationToken);
        return loaded.Count == 0 ? null : loaded[0];
    }

    public async Task<int> GetNextRevisionAsync(DictionaryDefinition definition, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return checked(await Db.Queryable<DictionaryRow>().AS(Definitions)
            .Where(row => row.TenantNId == definition.TenantNId && row.NId == definition.NId)
            .MaxAsync(row => row.Revision) + 1);
    }

    public async Task<DictionaryDefinition?> GetLastPublishedAsync(DictionaryDefinition definition, CancellationToken cancellationToken)
    {
        var rows = await Db.Queryable<DictionaryRow>().AS(Definitions)
            .Where(row => row.TenantNId == definition.TenantNId && row.NId == definition.NId && row.PublishedOn != null && row.Id != definition.Id)
            .OrderByDescending(row => row.Revision).Take(1).ToListAsync(cancellationToken);
        var loaded = await LoadItemsAsync(rows, cancellationToken);
        return loaded.Count == 0 ? null : loaded[0];
    }

    public async Task CreateAsync(DictionaryDefinition definition, DictionaryChange? source,
        CancellationToken cancellationToken)
    {
        await Db.Ado.BeginTranAsync();
        try
        {
            if (source is not null)
            {
                var count = await Db.Updateable(Row(source.Definition)).AS(Definitions)
                    .Where(row => row.Id == source.Definition.Id && !row.IsDeleted
                        && row.OptimisticVersion == source.ExpectedOptimisticVersion
                        && row.ConcurrencyVersion == source.ExpectedConcurrencyVersion)
                    .ExecuteCommandAsync(cancellationToken);
                if (count != 1) throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
            }
            await Db.Insertable(Row(definition)).AS(Definitions).ExecuteCommandAsync(cancellationToken);
            await InsertItemsAsync(definition, cancellationToken);
            await Db.Ado.CommitTranAsync();
        }
        catch (Exception exception)
        {
            await Db.Ado.RollbackTranAsync();
            ThrowKnownDatabaseError(exception, source is not null);
            throw;
        }
    }

    public async Task SaveAsync(IReadOnlyList<DictionaryChange> changes, CancellationToken cancellationToken)
    {
        await Db.Ado.BeginTranAsync();
        try
        {
            foreach (var change in changes)
            {
                var definition = change.Definition;
                var count = await Db.Updateable(Row(definition)).AS(Definitions)
                    .Where(row => row.Id == definition.Id && row.TenantNId == definition.TenantNId && !row.IsDeleted
                        && row.OptimisticVersion == change.ExpectedOptimisticVersion && row.ConcurrencyVersion == change.ExpectedConcurrencyVersion)
                    .ExecuteCommandAsync(cancellationToken);
                if (count != 1) throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
                if (definition.Status == PublicationStatus.Draft)
                {
                    await Db.Deleteable<DictionaryItemRow>().AS(Items).Where(item => item.DictionaryDefinitionId == definition.Id).ExecuteCommandAsync(cancellationToken);
                    await InsertItemsAsync(definition, cancellationToken);
                }
                if (definition.Status == PublicationStatus.Published)
                    await ReferenceDataOutboxWriter.InsertAsync(Db, "dictionary",
                        new ReferenceDictionaryPublishedV1(definition.TenantNId,
                            definition.ScopeType.ToString(), null, definition.Id, definition.NId,
                            definition.Revision), cancellationToken);
            }
            await ReferenceDataCacheGenerationStore.AdvanceAsync(Db,
                changes.Where(change => change.Definition.Status != PublicationStatus.Draft)
                    .Select(change => ReferenceDataCacheKeys.Dictionaries(change.Definition.NId)),
                cancellationToken);
            await Db.Ado.CommitTranAsync();
        }
        catch (Exception exception)
        {
            await Db.Ado.RollbackTranAsync();
            ThrowKnownDatabaseError(exception, concurrencyOnUnique: true);
            throw;
        }
    }

    private async Task InsertItemsAsync(DictionaryDefinition definition, CancellationToken cancellationToken)
    {
        if (definition.Items.Count == 0) return;
        var rows = definition.Items.Select(item => new DictionaryItemRow
        {
            Id = item.Id, DictionaryDefinitionId = definition.Id, NId = item.NId, Name = item.Name, Description = item.Description,
            Sort = item.Sort, Enabled = item.Enabled, IsFrozen = item.IsFrozen, IsLocked = item.IsLocked, IsDeleted = item.IsDeleted,
            EntityType = item.EntityType, CreatedOn = item.CreatedOn, LastUpdatedOn = item.LastUpdatedOn,
            OptimisticVersion = item.OptimisticVersion, ConcurrencyVersion = item.ConcurrencyVersion,
        }).ToArray();
        await Db.Insertable(rows).AS(Items).ExecuteCommandAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<DictionaryDefinition>> LoadItemsAsync(List<DictionaryRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return [];
        var ids = rows.Select(row => row.Id).ToArray();
        var children = await Db.Queryable<DictionaryItemRow>().AS(Items).Where(row => ids.Contains(row.DictionaryDefinitionId))
            .OrderBy(row => row.Sort).OrderBy(row => row.NId).ToListAsync(cancellationToken);
        return rows.Select(row => DictionaryDefinition.Restore(new DictionaryState(row.Id, row.NId, row.Name, row.Description,
            Enum.Parse<ReferenceScopeType>(row.ScopeType), row.TenantNId, row.Revision, Enum.Parse<PublicationStatus>(row.Status),
            row.PublishedOn is null ? null : Timestamp(row.PublishedOn.Value), row.PublishedBy, row.IsFrozen, row.IsLocked, row.IsDeleted, Timestamp(row.CreatedOn), Timestamp(row.LastUpdatedOn),
            row.OptimisticVersion, row.ConcurrencyVersion),
            children.Where(child => child.DictionaryDefinitionId == row.Id).Select(child => DictionaryItem.Restore(child.Id,
                child.NId, child.Name, child.Description, child.Sort, child.Enabled, Timestamp(child.CreatedOn), Timestamp(child.LastUpdatedOn),
                child.OptimisticVersion, child.ConcurrencyVersion)).ToArray())).ToArray();
    }

    private DateTimeOffset Timestamp(DateTimeOffset value) => Db.CurrentConnectionConfig.DbType == DbType.PostgreSQL
        ? value.ToUniversalTime() : new(value.DateTime, TimeSpan.Zero);

    private static DictionaryRow Row(DictionaryDefinition definition) => new()
    {
        Id = definition.Id, NId = definition.NId, Name = definition.Name, Description = definition.Description, TenantNId = definition.TenantNId,
        ScopeType = definition.ScopeType.ToString(), Revision = definition.Revision, Status = definition.Status.ToString(),
        PublishedOn = definition.PublishedOn, PublishedBy = definition.PublishedBy, IsFrozen = definition.IsFrozen,
        IsLocked = definition.IsLocked, IsDeleted = definition.IsDeleted, EntityType = definition.EntityType,
        CreatedOn = definition.CreatedOn, LastUpdatedOn = definition.LastUpdatedOn, OptimisticVersion = definition.OptimisticVersion,
        ConcurrencyVersion = definition.ConcurrencyVersion,
    };

    private static void ThrowKnownDatabaseError(Exception exception, bool concurrencyOnUnique = false)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is Microsoft.Data.Sqlite.SqliteException { SqliteExtendedErrorCode: 2067 or 1555 }
                || current is Npgsql.PostgresException { SqlState: "23505" })
                throw new ReferenceDataException(
                    concurrencyOnUnique ? "REF-CONCURRENCY-CONFLICT" : "REF-DICT-DUPLICATE-NID", 409);
        if (exception is DbException or SqlSugarException) throw new ReferenceDataException("503", 503);
    }
}

internal sealed class DictionaryItemCount
{
    public Guid DictionaryDefinitionId { get; set; }
    public int Count { get; set; }
}
