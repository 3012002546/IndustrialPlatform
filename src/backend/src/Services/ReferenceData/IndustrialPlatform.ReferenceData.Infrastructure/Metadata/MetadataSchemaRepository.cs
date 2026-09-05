using System.Data.Common;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Application.Caching;
using IndustrialPlatform.ReferenceData.Application.Metadata;
using IndustrialPlatform.ReferenceData.Contracts.Metadata;
using IndustrialPlatform.ReferenceData.Contracts.Events;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Domain.Metadata;
using IndustrialPlatform.ReferenceData.Infrastructure.Caching;
using IndustrialPlatform.ReferenceData.Infrastructure.Outbox;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Infrastructure.Metadata;

public sealed class MetadataSchemaRepository(SqlSugarDbContext context) : IMetadataSchemaRepository
{
    private ISqlSugarClient Db => context.SqlSugar;
    private bool Postgres => Db.CurrentConnectionConfig.DbType == DbType.PostgreSQL;
    private string Schemas => Postgres ? "reference_data.metadata_entity_schema" : "reference_data_metadata_entity_schema";
    private string Attributes => Postgres ? "reference_data.metadata_attribute_definition" : "reference_data_metadata_attribute_definition";

    private ISugarQueryable<MetadataSchemaRow> Visible(string tenantNId) =>
        Db.Queryable<MetadataSchemaRow>().AS(Schemas)
            .Where(row => !row.IsDeleted && (row.TenantNId == tenantNId || row.TenantNId == null));

    public async Task<(IReadOnlyList<MetadataSchemaSummaryDto> Items, long Total)> SearchAsync(
        string tenantNId, MetadataSchemaQuery query, CancellationToken cancellationToken)
    {
        var filter = Visible(tenantNId);
        if (query.Keyword is { Length: > 0 } keyword)
        {
            keyword = keyword.ToUpperInvariant();
            filter = filter.Where(row => row.NId.Contains(keyword)
                || SqlFunc.Contains(SqlFunc.ToUpper(row.Name), keyword));
        }
        filter = query.Status is null
            ? filter.Where(row => row.Status == "Draft" || row.Status == "Published")
            : filter.Where(row => row.Status == query.Status);
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
        var rows = await filter.OrderBy(row => row.Id).ToPageListAsync(query.PageIndex, query.PageSize,
            cancellationToken);
        var loaded = await LoadAsync(rows, cancellationToken);
        return (loaded.Select(MetadataSchemaService.ToSummary).ToArray(), total);
    }

    public async Task<EntitySchema?> GetAsync(
        string tenantNId, Guid id, CancellationToken cancellationToken)
    {
        var rows = await Visible(tenantNId).Where(row => row.Id == id).ToListAsync(cancellationToken);
        var loaded = await LoadAsync(rows, cancellationToken);
        return loaded.Count == 0 ? null : loaded[0];
    }

    public async Task<EntitySchema?> GetEffectiveAsync(
        string tenantNId, string nId, CancellationToken cancellationToken)
    {
        var rows = await Visible(tenantNId)
            .Where(row => row.NId == nId && row.Status == "Published")
            .OrderByDescending(row => row.TenantNId).ToListAsync(cancellationToken);
        var selected = rows.FirstOrDefault(row => row.TenantNId == tenantNId)
            ?? rows.FirstOrDefault(row => row.TenantNId is null);
        if (selected is null) return null;
        return (await LoadAsync([selected], cancellationToken))[0];
    }

    public async Task<EntitySchema?> GetRevisionAsync(string tenantNId, string nId, string sourceScope,
        int revision, CancellationToken cancellationToken)
    {
        var filter = Db.Queryable<MetadataSchemaRow>().AS(Schemas)
            .Where(row => !row.IsDeleted && row.NId == nId && row.Revision == revision
                && row.PublishedOn != null);
        filter = sourceScope == nameof(ReferenceScopeType.Platform)
            ? filter.Where(row => row.ScopeType == "Platform" && row.TenantNId == null)
            : filter.Where(row => row.ScopeType == "Tenant" && row.TenantNId == tenantNId);
        var rows = await filter.Take(1).ToListAsync(cancellationToken);
        var loaded = await LoadAsync(rows, cancellationToken);
        return loaded.Count == 0 ? null : loaded[0];
    }

    public async Task<EntitySchema?> GetPublishedInScopeAsync(
        EntitySchema schema, CancellationToken cancellationToken)
    {
        var rows = await Db.Queryable<MetadataSchemaRow>().AS(Schemas)
            .Where(row => !row.IsDeleted && row.TenantNId == schema.TenantNId && row.NId == schema.NId
                && row.Status == "Published").Take(1).ToListAsync(cancellationToken);
        var loaded = await LoadAsync(rows, cancellationToken);
        return loaded.Count == 0 ? null : loaded[0];
    }

    public async Task<EntitySchema?> GetLastPublishedAsync(
        EntitySchema schema, CancellationToken cancellationToken)
    {
        var rows = await Db.Queryable<MetadataSchemaRow>().AS(Schemas)
            .Where(row => !row.IsDeleted && row.TenantNId == schema.TenantNId && row.NId == schema.NId
                && row.PublishedOn != null && row.Id != schema.Id)
            .OrderByDescending(row => row.Revision).Take(1).ToListAsync(cancellationToken);
        var loaded = await LoadAsync(rows, cancellationToken);
        return loaded.Count == 0 ? null : loaded[0];
    }

    public async Task<int> GetNextRevisionAsync(
        EntitySchema schema, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return checked(await Db.Queryable<MetadataSchemaRow>().AS(Schemas)
                .Where(row => !row.IsDeleted && row.TenantNId == schema.TenantNId && row.NId == schema.NId)
                .MaxAsync(row => row.Revision) + 1);
        }
        catch (OverflowException)
        {
            throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
        }
    }

    public async Task CreateAsync(EntitySchema schema, MetadataSchemaChange? source,
        CancellationToken cancellationToken)
    {
        await Db.Ado.BeginTranAsync();
        try
        {
            if (source is not null)
            {
                var count = await Db.Updateable(RootRow(source.Schema)).AS(Schemas)
                    .Where(row => row.Id == source.Schema.Id && !row.IsDeleted
                        && row.OptimisticVersion == source.ExpectedOptimisticVersion
                        && row.ConcurrencyVersion == source.ExpectedConcurrencyVersion)
                    .ExecuteCommandAsync(cancellationToken);
                if (count != 1) throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
            }
            await Db.Insertable(RootRow(schema)).AS(Schemas).ExecuteCommandAsync(cancellationToken);
            await InsertAttributesAsync(schema, cancellationToken);
            await Db.Ado.CommitTranAsync();
        }
        catch (Exception exception)
        {
            await Db.Ado.RollbackTranAsync();
            if (source is not null && IsUniqueViolation(exception))
                throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
            ThrowKnownDatabaseError(exception);
            throw;
        }
    }

    public async Task SaveAsync(IReadOnlyList<MetadataSchemaChange> changes,
        CancellationToken cancellationToken)
    {
        await Db.Ado.BeginTranAsync();
        try
        {
            foreach (var change in changes)
            {
                var schema = change.Schema;
                var count = await Db.Updateable(RootRow(schema)).AS(Schemas)
                    .Where(row => row.Id == schema.Id && row.TenantNId == schema.TenantNId && !row.IsDeleted
                        && row.OptimisticVersion == change.ExpectedOptimisticVersion
                        && row.ConcurrencyVersion == change.ExpectedConcurrencyVersion)
                    .ExecuteCommandAsync(cancellationToken);
                if (count != 1) throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
                if (change.SaveAttributes)
                {
                    await Db.Deleteable<MetadataAttributeRow>().AS(Attributes)
                        .Where(row => row.EntitySchemaId == schema.Id).ExecuteCommandAsync(cancellationToken);
                    await InsertAttributesAsync(schema, cancellationToken);
                }
                if (schema.Status == PublicationStatus.Published)
                    await ReferenceDataOutboxWriter.InsertAsync(Db, "metadata",
                        new ReferenceMetadataPublishedV1(schema.TenantNId, schema.ScopeType.ToString(),
                            null, schema.Id, schema.NId, schema.Revision), cancellationToken);
            }
            await ReferenceDataCacheGenerationStore.AdvanceAsync(Db,
                changes.Where(change => change.Schema.Status != PublicationStatus.Draft)
                    .Select(change => ReferenceDataCacheKeys.MetadataEntries(change.Schema.NId)),
                cancellationToken);
            await Db.Ado.CommitTranAsync();
        }
        catch (Exception exception)
        {
            await Db.Ado.RollbackTranAsync();
            ThrowKnownDatabaseError(exception);
            throw;
        }
    }

    private async Task<IReadOnlyList<EntitySchema>> LoadAsync(
        List<MetadataSchemaRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return [];
        var ids = rows.Select(row => row.Id).ToArray();
        var childRows = await Db.Queryable<MetadataAttributeRow>().AS(Attributes)
            .Where(row => ids.Contains(row.EntitySchemaId) && !row.IsDeleted)
            .OrderBy(row => row.Sort).OrderBy(row => row.NId).ToListAsync(cancellationToken);
        return rows.Select(row => EntitySchema.Restore(new(
                new(row.Id, row.IsFrozen, row.IsLocked, row.IsDeleted, Timestamp(row.CreatedOn),
                    Timestamp(row.LastUpdatedOn), row.OptimisticVersion, row.ConcurrencyVersion),
                row.NId, row.Name, row.Description, Enum.Parse<ReferenceScopeType>(row.ScopeType), row.TenantNId,
                row.Revision, Enum.Parse<PublicationStatus>(row.Status), row.SourceRevision,
                row.PublishedOn is null ? null : Timestamp(row.PublishedOn.Value), row.PublishedBy),
            childRows.Where(child => child.EntitySchemaId == row.Id).Select(Attribute).ToArray())).ToArray();
    }

    private AttributeDefinition Attribute(MetadataAttributeRow row) => AttributeDefinition.Restore(new(
            row.Id, row.IsFrozen, row.IsLocked, row.IsDeleted, Timestamp(row.CreatedOn),
            Timestamp(row.LastUpdatedOn), row.OptimisticVersion, row.ConcurrencyVersion),
        row.EntitySchemaId, row.NId, new(row.Name, Enum.Parse<ReferenceDataType>(row.DataType), row.Required,
            row.IsArray, row.Enabled, row.Sort, row.DefaultValue, row.MinLength, row.MaxLength, row.MinValue,
            row.MaxValue, row.Pattern, row.DictionaryNId, row.ReferenceTarget, row.Precision, row.Scale,
            row.UnitDimensionNId, row.DefaultUnitNId, row.UnitRevision,
            row.UnitSourceScope is null ? null : Enum.Parse<ReferenceScopeType>(row.UnitSourceScope),
            row.UnitSourceTenantNId, row.Description), row.WasPublished);

    private async Task InsertAttributesAsync(EntitySchema schema, CancellationToken cancellationToken)
    {
        if (schema.Attributes.Count == 0) return;
        await Db.Insertable(schema.Attributes.Select(AttributeRow).ToArray()).AS(Attributes)
            .ExecuteCommandAsync(cancellationToken);
    }

    private DateTimeOffset Timestamp(DateTimeOffset value) =>
        Postgres ? value.ToUniversalTime() : new(value.DateTime, TimeSpan.Zero);

    private static MetadataSchemaRow RootRow(EntitySchema schema) => new()
    {
        Id = schema.Id,
        TenantNId = schema.TenantNId,
        ScopeType = schema.ScopeType.ToString(),
        NId = schema.NId,
        Name = schema.Name,
        Description = schema.Description,
        Revision = schema.Revision,
        Status = schema.Status.ToString(),
        SourceRevision = schema.SourceRevision,
        PublishedOn = schema.PublishedOn,
        PublishedBy = schema.PublishedBy,
        IsFrozen = schema.IsFrozen,
        IsLocked = schema.IsLocked,
        IsDeleted = schema.IsDeleted,
        EntityType = schema.EntityType,
        CreatedOn = schema.CreatedOn,
        LastUpdatedOn = schema.LastUpdatedOn,
        OptimisticVersion = schema.OptimisticVersion,
        ConcurrencyVersion = schema.ConcurrencyVersion,
    };

    private static MetadataAttributeRow AttributeRow(AttributeDefinition attribute)
    {
        var settings = attribute.Settings;
        return new()
        {
            Id = attribute.Id,
            EntitySchemaId = attribute.EntitySchemaId,
            NId = attribute.NId,
            Name = settings.Name,
            DataType = settings.DataType.ToString(),
            Required = settings.Required,
            IsArray = settings.IsArray,
            Enabled = settings.Enabled,
            Sort = settings.Sort,
            DefaultValue = settings.DefaultValue,
            MinLength = settings.MinLength,
            MaxLength = settings.MaxLength,
            MinValue = settings.MinValue,
            MaxValue = settings.MaxValue,
            Pattern = settings.Pattern,
            DictionaryNId = settings.DictionaryNId,
            ReferenceTarget = settings.ReferenceTarget,
            Precision = settings.Precision,
            Scale = settings.Scale,
            UnitDimensionNId = settings.UnitDimensionNId,
            DefaultUnitNId = settings.DefaultUnitNId,
            UnitRevision = settings.UnitRevision,
            UnitSourceScope = settings.UnitSourceScope?.ToString(),
            UnitSourceTenantNId = settings.UnitSourceTenantNId,
            Description = settings.Description,
            WasPublished = attribute.WasPublished,
            IsFrozen = attribute.IsFrozen,
            IsLocked = attribute.IsLocked,
            IsDeleted = attribute.IsDeleted,
            EntityType = attribute.EntityType,
            CreatedOn = attribute.CreatedOn,
            LastUpdatedOn = attribute.LastUpdatedOn,
            OptimisticVersion = attribute.OptimisticVersion,
            ConcurrencyVersion = attribute.ConcurrencyVersion,
        };
    }

    private static void ThrowKnownDatabaseError(Exception exception)
    {
        if (exception is ReferenceDataException) return;
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is Microsoft.Data.Sqlite.SqliteException { SqliteExtendedErrorCode: 2067 or 1555 }
                || current is Npgsql.PostgresException { SqlState: "23505" })
                throw new ReferenceDataException("REF-VALIDATION-FAILED", 409, "nId");
        if (exception is DbException or SqlSugarException) throw new ReferenceDataException("503", 503);
    }

    private static bool IsUniqueViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is Microsoft.Data.Sqlite.SqliteException { SqliteExtendedErrorCode: 2067 or 1555 }
                || current is Npgsql.PostgresException { SqlState: "23505" }) return true;
        return false;
    }
}
