using System.Data.Common;
using System.Globalization;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Application.Caching;
using IndustrialPlatform.ReferenceData.Application.UnitOfMeasure;
using IndustrialPlatform.ReferenceData.Contracts.UnitOfMeasure;
using IndustrialPlatform.ReferenceData.Contracts.Events;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure;
using IndustrialPlatform.ReferenceData.Infrastructure.Caching;
using IndustrialPlatform.ReferenceData.Infrastructure.Outbox;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Infrastructure.UnitOfMeasure;

public sealed class UnitDimensionRepository(SqlSugarDbContext context) : IUnitDimensionRepository
{
    private ISqlSugarClient Db => context.SqlSugar;
    private bool Postgres => Db.CurrentConnectionConfig.DbType == DbType.PostgreSQL;
    private string Dimensions => Postgres ? "reference_data.unit_of_measure_dimension" : "reference_data_unit_of_measure_dimension";
    private string Units => Postgres ? "reference_data.unit_of_measure_unit" : "reference_data_unit_of_measure_unit";

    private ISugarQueryable<UnitDimensionRow> Visible(string tenantNId) =>
        Db.Queryable<UnitDimensionRow>().AS(Dimensions)
            .Where(row => !row.IsDeleted && (row.TenantNId == tenantNId || row.TenantNId == null));

    public async Task<(IReadOnlyList<UnitDimensionSummaryDto> Items, long Total)> SearchAsync(
        string tenantNId, UnitDimensionQuery query, CancellationToken cancellationToken)
    {
        var filter = Visible(tenantNId);
        filter = ApplyKeyword(filter, query.Keyword);
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
        filter = filter.OrderBy(row => row.Id);
        return await PageAsync(filter, query.PageIndex, query.PageSize, cancellationToken);
    }

    public async Task<(IReadOnlyList<AvailableUnitDimensionDto> Items, long Total)> ListAvailableAsync(
        string tenantNId, AvailableUnitDimensionQuery query, CancellationToken cancellationToken)
    {
        var filter = ApplyKeyword(Visible(tenantNId).Where(row => row.Status == "Published"), query.Keyword)
            .OrderBy(row => row.NId).OrderBy(row => row.ScopeType).OrderBy(row => row.TenantNId).OrderBy(row => row.Id);
        var page = await PageAsync(filter, query.PageIndex, query.PageSize, cancellationToken);
        return (page.Items.Select(item => new AvailableUnitDimensionDto(
            item.NId, item.Name, item.ScopeType, item.TenantNId, item.Revision, item.PublishedOn!.Value,
            item.IsSystemDefined, item.ConversionKind, item.BaseUnitNId, item.UnitCount)).ToArray(), page.Total);
    }

    public async Task<UnitDimension?> GetAsync(string tenantNId, Guid id, CancellationToken cancellationToken)
    {
        var rows = await Visible(tenantNId).Where(row => row.Id == id).ToListAsync(cancellationToken);
        var loaded = await LoadAsync(rows, cancellationToken);
        return loaded.Count == 0 ? null : loaded[0];
    }

    public async Task<UnitDimension?> GetCurrentAsync(
        string tenantNId, string nId, string sourceScope, CancellationToken cancellationToken)
    {
        var filter = Db.Queryable<UnitDimensionRow>().AS(Dimensions)
            .Where(row => !row.IsDeleted && row.NId == nId && row.Status == "Published");
        filter = sourceScope == nameof(ReferenceScopeType.Platform)
            ? filter.Where(row => row.TenantNId == null && row.ScopeType == "Platform")
            : filter.Where(row => row.TenantNId == tenantNId && row.ScopeType == "Tenant");
        var rows = await filter.Take(1).ToListAsync(cancellationToken);
        var loaded = await LoadAsync(rows, cancellationToken);
        return loaded.Count == 0 ? null : loaded[0];
    }

    public async Task<UnitDimension?> GetRevisionAsync(
        string tenantNId, string nId, string sourceScope, int revision, CancellationToken cancellationToken)
    {
        var filter = Db.Queryable<UnitDimensionRow>().AS(Dimensions)
            .Where(row => !row.IsDeleted && row.NId == nId && row.Revision == revision && row.PublishedOn != null
                && (row.Status == "Published" || row.Status == "Superseded" || row.Status == "Disabled"));
        filter = sourceScope == nameof(ReferenceScopeType.Platform)
            ? filter.Where(row => row.TenantNId == null && row.ScopeType == "Platform")
            : filter.Where(row => row.TenantNId == tenantNId && row.ScopeType == "Tenant");
        var rows = await filter.Take(1).ToListAsync(cancellationToken);
        var loaded = await LoadAsync(rows, cancellationToken);
        return loaded.Count == 0 ? null : loaded[0];
    }

    public async Task<UnitDimension?> GetPublishedInScopeAsync(
        UnitDimension dimension, CancellationToken cancellationToken)
    {
        var rows = await Db.Queryable<UnitDimensionRow>().AS(Dimensions)
            .Where(row => !row.IsDeleted && row.TenantNId == dimension.TenantNId && row.NId == dimension.NId
                && row.Status == "Published")
            .Take(1).ToListAsync(cancellationToken);
        var loaded = await LoadAsync(rows, cancellationToken);
        return loaded.Count == 0 ? null : loaded[0];
    }

    public async Task<int> GetNextRevisionAsync(UnitDimension dimension, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var revision = await Db.Queryable<UnitDimensionRow>().AS(Dimensions)
            .Where(row => !row.IsDeleted && row.TenantNId == dimension.TenantNId && row.NId == dimension.NId)
            .MaxAsync(row => row.Revision);
        if (revision == int.MaxValue) throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
        return revision + 1;
    }

    public async Task CreateAsync(
        UnitDimension dimension, UnitDimensionChange? source, CancellationToken cancellationToken)
    {
        if (dimension.IsSystemDefined || source?.Dimension.IsSystemDefined == true)
            throw new ReferenceDataException("REF-UNIT-SYSTEM-DEFINED", 409);
        await Db.Ado.BeginTranAsync();
        try
        {
            if (source is not null)
            {
                var count = await Db.Updateable(RootRow(source.Dimension)).AS(Dimensions)
                    .Where(row => row.Id == source.Dimension.Id && !row.IsDeleted
                        && row.OptimisticVersion == source.ExpectedOptimisticVersion
                        && row.ConcurrencyVersion == source.ExpectedConcurrencyVersion)
                    .ExecuteCommandAsync(cancellationToken);
                if (count != 1) throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
            }
            await Db.Insertable(RootRow(dimension)).AS(Dimensions).ExecuteCommandAsync(cancellationToken);
            await InsertUnitsAsync(dimension, cancellationToken);
            await Db.Ado.CommitTranAsync();
        }
        catch (Exception exception)
        {
            await Db.Ado.RollbackTranAsync();
            ThrowKnownDatabaseError(exception, concurrencyOnUnique: source is not null);
            throw;
        }
    }

    public async Task SaveAsync(IReadOnlyList<UnitDimensionChange> changes, CancellationToken cancellationToken)
    {
        if (changes.Any(change => change.Dimension.IsSystemDefined))
            throw new ReferenceDataException("REF-UNIT-SYSTEM-DEFINED", 409);
        await Db.Ado.BeginTranAsync();
        try
        {
            foreach (var change in changes)
            {
                var dimension = change.Dimension;
                var count = await Db.Updateable(RootRow(dimension)).AS(Dimensions)
                    .Where(row => row.Id == dimension.Id && row.TenantNId == dimension.TenantNId && !row.IsDeleted
                        && row.OptimisticVersion == change.ExpectedOptimisticVersion
                        && row.ConcurrencyVersion == change.ExpectedConcurrencyVersion)
                    .ExecuteCommandAsync(cancellationToken);
                if (count != 1) throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
                if (dimension.Status == PublicationStatus.Draft)
                {
                    await Db.Deleteable<UnitDefinitionRow>().AS(Units)
                        .Where(row => row.UnitDimensionId == dimension.Id).ExecuteCommandAsync(cancellationToken);
                    await InsertUnitsAsync(dimension, cancellationToken);
                }
                if (dimension.Status is PublicationStatus.Published or PublicationStatus.Disabled)
                    await ReferenceDataOutboxWriter.InsertAsync(Db, "unit-of-measure",
                        new ReferenceUnitDimensionChangedV1(dimension.TenantNId,
                            dimension.ScopeType.ToString(), null, dimension.Id, dimension.NId,
                            dimension.Revision, dimension.Status.ToString(), dimension.OptimisticVersion),
                        cancellationToken);
            }
            await ReferenceDataCacheGenerationStore.AdvanceAsync(Db,
                changes.Where(change => change.Dimension.Status != PublicationStatus.Draft)
                    .SelectMany(change => CachePatterns(change.Dimension)), cancellationToken);
            await Db.Ado.CommitTranAsync();
        }
        catch (Exception exception)
        {
            await Db.Ado.RollbackTranAsync();
            ThrowKnownDatabaseError(exception);
            throw;
        }
    }

    private async Task<(IReadOnlyList<UnitDimensionSummaryDto> Items, long Total)> PageAsync(
        ISugarQueryable<UnitDimensionRow> filter, int pageIndex, int pageSize, CancellationToken cancellationToken)
    {
        var total = await filter.CountAsync(cancellationToken);
        var rows = await filter.ToPageListAsync(pageIndex, pageSize, cancellationToken);
        var loaded = await LoadAsync(rows, cancellationToken);
        return (loaded.Select(UnitDimensionService.ToSummary).ToArray(), total);
    }

    private static ISugarQueryable<UnitDimensionRow> ApplyKeyword(
        ISugarQueryable<UnitDimensionRow> filter, string? keyword)
    {
        if (keyword is not { Length: > 0 }) return filter;
        keyword = keyword.ToUpperInvariant();
        return filter.Where(row => row.NId.Contains(keyword) || SqlFunc.Contains(SqlFunc.ToUpper(row.Name), keyword));
    }

    private async Task<IReadOnlyList<UnitDimension>> LoadAsync(
        List<UnitDimensionRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return [];
        var ids = rows.Select(row => row.Id).ToArray();
        var units = await Db.Queryable<UnitDefinitionRow>().AS(Units)
            .Where(row => ids.Contains(row.UnitDimensionId) && !row.IsDeleted)
            .OrderBy(row => row.Sort).OrderBy(row => row.NId).ToListAsync(cancellationToken);
        return rows.Select(row => UnitDimension.Restore(new(
                row.Id, row.NId, row.Name, row.Description, Enum.Parse<ReferenceScopeType>(row.ScopeType), row.TenantNId,
                row.Revision, Enum.Parse<PublicationStatus>(row.Status), row.SourceRevision,
                row.PublishedOn is null ? null : Timestamp(row.PublishedOn.Value), row.PublishedBy,
                row.IsSystemDefined, Enum.Parse<UnitConversionKind>(row.ConversionKind), row.BaseUnitNId,
                row.IsFrozen, row.IsLocked, row.IsDeleted, Timestamp(row.CreatedOn), Timestamp(row.LastUpdatedOn),
                row.OptimisticVersion, row.ConcurrencyVersion),
            units.Where(unit => unit.UnitDimensionId == row.Id).Select(unit => UnitDefinition.Restore(
                unit.Id, unit.UnitDimensionId, unit.NId, unit.Name, unit.Symbol, unit.FactorToBase, unit.OffsetToBase,
                unit.DecimalPlaces, Enum.Parse<UnitRoundingMode>(unit.RoundingMode), unit.Enabled, unit.Sort,
                unit.IsFrozen, unit.IsLocked, unit.IsDeleted, Timestamp(unit.CreatedOn), Timestamp(unit.LastUpdatedOn),
                unit.OptimisticVersion, unit.ConcurrencyVersion)).ToArray())).ToArray();
    }

    private async Task InsertUnitsAsync(UnitDimension dimension, CancellationToken cancellationToken)
    {
        if (dimension.Units.Count == 0) return;
        await Db.Insertable(dimension.Units.Select(UnitRow).ToArray()).AS(Units).ExecuteCommandAsync(cancellationToken);
    }

    private DateTimeOffset Timestamp(DateTimeOffset value) =>
        Postgres ? value.ToUniversalTime() : new(value.DateTime, TimeSpan.Zero);

    private static UnitDimensionRow RootRow(UnitDimension dimension) => new()
    {
        Id = dimension.Id,
        TenantNId = dimension.TenantNId,
        ScopeType = dimension.ScopeType.ToString(),
        NId = dimension.NId,
        Name = dimension.Name,
        Description = dimension.Description,
        Revision = dimension.Revision,
        Status = dimension.Status.ToString(),
        SourceRevision = dimension.SourceRevision,
        PublishedOn = dimension.PublishedOn,
        PublishedBy = dimension.PublishedBy,
        IsSystemDefined = dimension.IsSystemDefined,
        ConversionKind = dimension.ConversionKind.ToString(),
        BaseUnitNId = dimension.BaseUnitNId,
        IsFrozen = dimension.IsFrozen,
        IsLocked = dimension.IsLocked,
        IsDeleted = dimension.IsDeleted,
        EntityType = dimension.EntityType,
        CreatedOn = dimension.CreatedOn,
        LastUpdatedOn = dimension.LastUpdatedOn,
        OptimisticVersion = dimension.OptimisticVersion,
        ConcurrencyVersion = dimension.ConcurrencyVersion,
    };

    private static UnitDefinitionRow UnitRow(UnitDefinition unit) => new()
    {
        Id = unit.Id,
        UnitDimensionId = unit.UnitDimensionId,
        NId = unit.NId,
        Name = unit.Name,
        Symbol = unit.Symbol,
        FactorToBase = unit.FactorToBase,
        OffsetToBase = unit.OffsetToBase,
        DecimalPlaces = unit.DecimalPlaces,
        RoundingMode = unit.RoundingMode.ToString(),
        Enabled = unit.Enabled,
        Sort = unit.Sort,
        IsFrozen = unit.IsFrozen,
        IsLocked = unit.IsLocked,
        IsDeleted = unit.IsDeleted,
        EntityType = unit.EntityType,
        CreatedOn = unit.CreatedOn,
        LastUpdatedOn = unit.LastUpdatedOn,
        OptimisticVersion = unit.OptimisticVersion,
        ConcurrencyVersion = unit.ConcurrencyVersion,
    };

    private static IEnumerable<ReferenceDataCachePattern> CachePatterns(UnitDimension dimension)
    {
        var source = dimension.ScopeType.ToString();
        var tenantKey = ReferenceDataCacheKeys.SourceTenantKey(source, dimension.TenantNId);
        yield return ReferenceDataCacheKeys.UnitOfMeasureState(source, tenantKey, dimension.NId, "current");
        yield return ReferenceDataCacheKeys.UnitOfMeasureState(source, tenantKey, dimension.NId,
            dimension.Revision.ToString(CultureInfo.InvariantCulture));
        yield return ReferenceDataCacheKeys.AvailableUnits();
    }

    private static void ThrowKnownDatabaseError(Exception exception, bool concurrencyOnUnique = false)
    {
        if (exception is ReferenceDataException) return;
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is Microsoft.Data.Sqlite.SqliteException { SqliteExtendedErrorCode: 2067 or 1555 }
                || current is Npgsql.PostgresException { SqlState: "23505" })
                throw new ReferenceDataException(
                    concurrencyOnUnique ? "REF-CONCURRENCY-CONFLICT" : "REF-UNIT-CONVERSION-INVALID",
                    concurrencyOnUnique ? 409 : 422);
        if (exception is DbException or SqlSugarException) throw new ReferenceDataException("503", 503);
    }
}
