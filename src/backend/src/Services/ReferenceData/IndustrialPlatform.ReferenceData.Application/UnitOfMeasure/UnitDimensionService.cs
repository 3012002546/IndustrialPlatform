using System.Globalization;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Application.Caching;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.UnitOfMeasure;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure;

namespace IndustrialPlatform.ReferenceData.Application.UnitOfMeasure;

public sealed class UnitDimensionService
{
    private const decimal NumericMaximum = 9999999999999999.999999999999m;
    private static readonly NumberStyles DecimalStyles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
    private readonly IUnitDimensionRepository repository;
    private readonly IReferenceDataCache cache;

    public UnitDimensionService(IUnitDimensionRepository repository) :
        this(repository, NullReferenceDataCache.Instance)
    { }

    public UnitDimensionService(IUnitDimensionRepository repository, IReferenceDataCache cache)
    {
        this.repository = repository;
        this.cache = cache;
    }

    public async Task<(IReadOnlyList<UnitDimensionSummaryDto> Items, long Total)> SearchAsync(
        ReferenceDataActor actor, UnitDimensionQuery query, CancellationToken cancellationToken)
    {
        ValidatePage(query.PageIndex, query.PageSize, query.Keyword);
        if (query.ScopeType is not null)
        {
            if (!Enum.GetNames<ReferenceScopeType>().Contains(query.ScopeType, StringComparer.Ordinal))
                throw new ReferenceDataException("REF-SCOPE-INVALID");
            if (query.ScopeType == nameof(ReferenceScopeType.Factory))
                throw new ReferenceDataException("REF-SCOPE-FACTORY-NOT-READY", 409);
        }
        if (query.Status is not null && !Enum.GetNames<PublicationStatus>().Contains(query.Status, StringComparer.Ordinal))
            throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "status");
        return await repository.SearchAsync(actor.TenantNId, query, cancellationToken);
    }

    public async Task<UnitDimensionDetailDto> GetAsync(
        ReferenceDataActor actor, Guid id, CancellationToken cancellationToken) =>
        MapDetail(await LoadAsync(actor, id, cancellationToken));

    public async Task<UnitDimensionDetailDto> CreateAsync(
        ReferenceDataActor actor, CreateUnitDimensionRequest request, CancellationToken cancellationToken)
    {
        var scope = ParseScope(request.ScopeType);
        if (scope == ReferenceScopeType.Platform && !actor.CanManagePlatform)
            throw new ReferenceDataException("ID_PERMISSION_DENIED", 403);
        var dimension = new UnitDimension(request.NId, request.Name, request.Description, scope,
            scope == ReferenceScopeType.Platform ? null : actor.TenantNId, request.ScopeId);
        dimension.Update(request.Name, request.Description, ParseConversionKind(request.ConversionKind),
            request.BaseUnitNId, ParseUnits(request.Units));
        await repository.CreateAsync(dimension, null, cancellationToken);
        return MapDetail(dimension);
    }

    public async Task<UnitDimensionDetailDto> UpdateAsync(
        ReferenceDataActor actor, Guid id, UpdateUnitDimensionRequest request, CancellationToken cancellationToken)
    {
        var dimension = await LoadWritableAsync(actor, id, request.ExpectedOptimisticVersion,
            request.ExpectedConcurrencyVersion, cancellationToken);
        dimension.Update(request.Name, request.Description, ParseConversionKind(request.ConversionKind),
            request.BaseUnitNId, ParseUnits(request.Units));
        await repository.SaveAsync([new(dimension, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion)], cancellationToken);
        await InvalidateRuntimeStateAsync(dimension, null, CancellationToken.None);
        return MapDetail(dimension);
    }

    public async Task<UnitDimensionDetailDto> CloneAsync(
        ReferenceDataActor actor, Guid id, PublishOrDisableRequest request, CancellationToken cancellationToken)
    {
        ValidateReason(request.ChangeReason, required: false);
        var source = await LoadWritableAsync(actor, id, request.ExpectedOptimisticVersion,
            request.ExpectedConcurrencyVersion, cancellationToken);
        var clone = source.Clone(await repository.GetNextRevisionAsync(source, cancellationToken));
        await repository.CreateAsync(clone,
            new(source, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion), cancellationToken);
        return MapDetail(clone);
    }

    public async Task<UnitDimensionDetailDto> PublishAsync(
        ReferenceDataActor actor, Guid id, PublishOrDisableRequest request, CancellationToken cancellationToken)
    {
        ValidateReason(request.ChangeReason, required: false);
        var dimension = await LoadWritableAsync(actor, id, request.ExpectedOptimisticVersion,
            request.ExpectedConcurrencyVersion, cancellationToken);
        var previous = await repository.GetPublishedInScopeAsync(dimension, cancellationToken);
        var changes = new List<UnitDimensionChange>();
        if (previous is not null && previous.Id != dimension.Id)
        {
            var expectedVersion = previous.OptimisticVersion;
            var expectedToken = previous.ConcurrencyVersion;
            previous.Supersede();
            changes.Add(new(previous, expectedVersion, expectedToken));
        }
        dimension.Publish(actor.UserNId);
        changes.Add(new(dimension, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion));
        await repository.SaveAsync(changes, cancellationToken);
        await InvalidateRuntimeStateAsync(dimension, previous?.Revision, CancellationToken.None);
        return MapDetail(dimension);
    }

    public async Task<UnitDimensionDetailDto> DisableAsync(
        ReferenceDataActor actor, Guid id, PublishOrDisableRequest request, CancellationToken cancellationToken)
    {
        ValidateReason(request.ChangeReason, required: true);
        var dimension = await LoadWritableAsync(actor, id, request.ExpectedOptimisticVersion,
            request.ExpectedConcurrencyVersion, cancellationToken);
        dimension.Disable();
        await repository.SaveAsync([new(dimension, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion)], cancellationToken);
        await InvalidateRuntimeStateAsync(dimension, null, CancellationToken.None);
        return MapDetail(dimension);
    }

    public async Task<(IReadOnlyList<AvailableUnitDimensionDto> Items, long Total)> ListAvailableAsync(
        ReferenceDataActor actor, AvailableUnitDimensionQuery query, CancellationToken cancellationToken)
    {
        ValidatePage(query.PageIndex, query.PageSize, query.Keyword);
        var key = ReferenceDataCacheKeys.UnitOfMeasure("Effective",
            ReferenceDataCacheKeys.TenantKey(actor.TenantNId), "list",
            ReferenceDataCacheKeys.PageKey(query.PageIndex.ToString(CultureInfo.InvariantCulture),
                query.PageSize.ToString(CultureInfo.InvariantCulture), query.Keyword));
        var cached = await cache.GetOrLoadAsync(key, ReferenceDataCacheTtl.UnitCurrent, async () =>
        {
            var page = await repository.ListAvailableAsync(actor.TenantNId, query, cancellationToken);
            return new ReferenceDataCachePage<AvailableUnitDimensionDto>(page.Items, page.Total,
                page.Items.Count == 0 ? 0 : page.Items.Max(item => item.Revision));
        }, cancellationToken);
        return (cached.Items, cached.Total);
    }

    public async Task<UnitDimensionDto> GetCurrentAsync(ReferenceDataActor actor, string nId, string? sourceScope,
        string? sourceTenantNId, CancellationToken cancellationToken)
    {
        var source = ResolveSource(actor, sourceScope, sourceTenantNId);
        var normalizedNId = NormalizeDimensionNId(nId);
        var key = ReferenceDataCacheKeys.UnitOfMeasure(source,
            ReferenceDataCacheKeys.SourceTenantKey(source, sourceTenantNId), normalizedNId, "current");
        return await cache.GetOrLoadAsync(key, ReferenceDataCacheTtl.UnitCurrent, async () =>
        {
            var dimension = await repository.GetCurrentAsync(actor.TenantNId, normalizedNId, source,
                cancellationToken) ?? throw NotFound();
            return MapRuntime(dimension, enabledUnitsOnly: true);
        }, cancellationToken);
    }

    public async Task<UnitDimensionDto> GetRevisionAsync(ReferenceDataActor actor, string nId, int revision,
        string? sourceScope, string? sourceTenantNId, CancellationToken cancellationToken)
    {
        if (revision < 1) throw NotFound();
        var source = ResolveSource(actor, sourceScope, sourceTenantNId);
        var normalizedNId = NormalizeDimensionNId(nId);
        var key = ReferenceDataCacheKeys.UnitOfMeasure(source,
            ReferenceDataCacheKeys.SourceTenantKey(source, sourceTenantNId), normalizedNId,
            revision.ToString(CultureInfo.InvariantCulture));
        return await cache.GetOrLoadAsync(key, ReferenceDataCacheTtl.UnitRevision, async () =>
        {
            var dimension = await repository.GetRevisionAsync(actor.TenantNId, normalizedNId, source,
                revision, cancellationToken) ?? throw NotFound();
            return MapRuntime(dimension, enabledUnitsOnly: false);
        }, cancellationToken);
    }

    public async Task<UnitConversionResultDto> ConvertAsync(
        ReferenceDataActor actor, UnitConversionRequest request, CancellationToken cancellationToken)
    {
        if (request.UnitRevision < 1) throw NotFound();
        var source = ResolveSource(actor, request.SourceScope, request.SourceTenantNId);
        var dimension = await repository.GetRevisionAsync(actor.TenantNId,
            NormalizeDimensionNId(request.UnitDimensionNId), source, request.UnitRevision, cancellationToken) ?? throw NotFound();
        var input = ParseDecimal(request.Value, "value", allowFullDecimalRange: true);
        UnitConversion conversion;
        try
        {
            conversion = dimension.Convert(request.FromUnitNId, request.ToUnitNId, input);
        }
        catch (ReferenceDataException) { throw; }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new ReferenceDataException("REF-UNIT-CONVERSION-INVALID", 422, "unitNId");
        }
        return new(dimension.NId, dimension.Revision, dimension.ScopeType.ToString(), dimension.TenantNId,
            conversion.Source.NId, conversion.Target.NId, request.Value,
            FormatResult(conversion.ResultValue, conversion.Target.DecimalPlaces), conversion.WasRounded,
            new(FormatDecimal(conversion.Source.FactorToBase), FormatDecimal(conversion.Source.OffsetToBase),
                FormatDecimal(conversion.Target.FactorToBase), FormatDecimal(conversion.Target.OffsetToBase),
                conversion.Target.DecimalPlaces, conversion.Target.RoundingMode.ToString()));
    }

    private async Task InvalidateRuntimeStateAsync(UnitDimension dimension, int? previousRevision,
        CancellationToken cancellationToken)
    {
        var source = dimension.ScopeType.ToString();
        var tenantKey = ReferenceDataCacheKeys.SourceTenantKey(source, dimension.TenantNId);
        await cache.InvalidateAsync(ReferenceDataCacheKeys.UnitOfMeasureState(source, tenantKey,
            dimension.NId, "current"), cancellationToken);
        await cache.InvalidateAsync(ReferenceDataCacheKeys.UnitOfMeasureState(source, tenantKey,
            dimension.NId, dimension.Revision.ToString(CultureInfo.InvariantCulture)), cancellationToken);
        if (previousRevision is not null && previousRevision != dimension.Revision)
            await cache.InvalidateAsync(ReferenceDataCacheKeys.UnitOfMeasureState(source, tenantKey,
                dimension.NId, previousRevision.Value.ToString(CultureInfo.InvariantCulture)),
                cancellationToken);
        await cache.InvalidateAsync(ReferenceDataCacheKeys.AvailableUnits(), cancellationToken);
    }

    private async Task<UnitDimension> LoadAsync(ReferenceDataActor actor, Guid id, CancellationToken cancellationToken) =>
        await repository.GetAsync(actor.TenantNId, id, cancellationToken) ?? throw NotFound();

    private async Task<UnitDimension> LoadWritableAsync(ReferenceDataActor actor, Guid id, long version, Guid token,
        CancellationToken cancellationToken)
    {
        var dimension = await LoadAsync(actor, id, cancellationToken);
        if (dimension.ScopeType == ReferenceScopeType.Platform && !actor.CanManagePlatform)
            throw new ReferenceDataException("ID_PERMISSION_DENIED", 403);
        dimension.CheckVersion(version, token);
        return dimension;
    }

    private static UnitDefinition[] ParseUnits(IReadOnlyList<UnitDefinitionRequest> units)
    {
        if (units is null || units.Count is < 1 or > 200)
            throw new ReferenceDataException("REF-UNIT-CONVERSION-INVALID", 422, "units");
        var result = new UnitDefinition[units.Count];
        for (var index = 0; index < units.Count; index++)
        {
            var unit = units[index];
            if (unit is null) throw new ReferenceDataException("REF-UNIT-CONVERSION-INVALID", 422, $"units[{index}]");
            try
            {
                if (!Enum.TryParse<UnitRoundingMode>(unit.RoundingMode, ignoreCase: false, out var rounding)
                    || !Enum.IsDefined(rounding))
                    throw new ReferenceDataException("REF-UNIT-CONVERSION-INVALID", 422, "roundingMode");
                result[index] = new UnitDefinition(unit.NId, unit.Name, unit.Symbol,
                    ParseDecimal(unit.FactorToBase, "factorToBase"), ParseDecimal(unit.OffsetToBase, "offsetToBase"),
                    unit.DecimalPlaces, rounding, unit.Enabled, unit.Sort);
            }
            catch (ReferenceDataException error)
            {
                throw new ReferenceDataException("REF-UNIT-CONVERSION-INVALID", 422,
                    $"units[{index}].{error.Field ?? "nId"}");
            }
        }
        return result;
    }

    private static decimal ParseDecimal(string value, string field, bool allowFullDecimalRange = false)
    {
        if (string.IsNullOrEmpty(value)
            || !decimal.TryParse(value, DecimalStyles, CultureInfo.InvariantCulture, out var parsed)
            || (!allowFullDecimalRange && (decimal.Abs(parsed) > NumericMaximum || Scale(parsed) > 12)))
            throw new ReferenceDataException("REF-UNIT-CONVERSION-INVALID", 422, field);
        return parsed;
    }

    private static int Scale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0x7f;

    private static UnitConversionKind ParseConversionKind(string value)
    {
        if (!Enum.TryParse<UnitConversionKind>(value, ignoreCase: false, out var kind) || !Enum.IsDefined(kind))
            throw new ReferenceDataException("REF-UNIT-CONVERSION-INVALID", 422, "conversionKind");
        return kind;
    }

    private static ReferenceScopeType ParseScope(string value)
    {
        if (!Enum.TryParse<ReferenceScopeType>(value, ignoreCase: false, out var scope) || !Enum.IsDefined(scope))
            throw new ReferenceDataException("REF-SCOPE-INVALID");
        if (scope == ReferenceScopeType.Factory)
            throw new ReferenceDataException("REF-SCOPE-FACTORY-NOT-READY", 409);
        return scope;
    }

    private static string ResolveSource(ReferenceDataActor actor, string? sourceScope, string? sourceTenantNId)
    {
        if (sourceScope is null) throw new ReferenceDataException("REF-SCOPE-INVALID");
        var scope = ParseScope(sourceScope);
        if (scope == ReferenceScopeType.Platform)
        {
            if (sourceTenantNId is not null) throw new ReferenceDataException("REF-SCOPE-INVALID");
            return nameof(ReferenceScopeType.Platform);
        }
        if (string.IsNullOrWhiteSpace(sourceTenantNId)) throw new ReferenceDataException("REF-SCOPE-INVALID");
        if (!string.Equals(actor.TenantNId, sourceTenantNId, StringComparison.Ordinal)) throw NotFound();
        return nameof(ReferenceScopeType.Tenant);
    }

    private static string NormalizeDimensionNId(string nId)
    {
        try { return ReferenceValidation.NId(nId); }
        catch (ReferenceDataException) { throw NotFound(); }
    }

    private static void ValidatePage(int pageIndex, int pageSize, string? keyword)
    {
        if (pageIndex < 1 || pageSize is < 1 or > 100 || keyword?.Length > 200)
            throw new ReferenceDataException("REF-VALIDATION-FAILED");
    }

    private static void ValidateReason(string? reason, bool required)
    {
        if ((required && string.IsNullOrWhiteSpace(reason)) || reason?.Length > 1000)
            throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "changeReason");
    }

    private static UnitDimensionSummaryDto MapSummary(UnitDimension dimension) => new(
        dimension.Id, dimension.NId, dimension.Name, dimension.Description, dimension.ScopeType.ToString(),
        dimension.TenantNId, dimension.Revision, dimension.Status.ToString(), dimension.SourceRevision,
        dimension.IsSystemDefined, dimension.ConversionKind.ToString(), dimension.BaseUnitNId, dimension.Units.Count,
        dimension.OptimisticVersion, dimension.ConcurrencyVersion, dimension.LastUpdatedOn, dimension.PublishedOn,
        dimension.PublishedBy, dimension.IsFrozen, dimension.IsLocked);

    public static UnitDimensionSummaryDto ToSummary(UnitDimension dimension) => MapSummary(dimension);

    private static UnitDimensionDetailDto MapDetail(UnitDimension dimension) => new(
        dimension.Id, dimension.NId, dimension.Name, dimension.Description, dimension.ScopeType.ToString(),
        dimension.TenantNId, dimension.Revision, dimension.Status.ToString(), dimension.SourceRevision,
        dimension.PublishedOn, dimension.PublishedBy, dimension.IsSystemDefined, dimension.ConversionKind.ToString(),
        dimension.BaseUnitNId, dimension.Units.Select(MapUnit).ToArray(), dimension.OptimisticVersion,
        dimension.ConcurrencyVersion, dimension.LastUpdatedOn, dimension.IsFrozen, dimension.IsLocked);

    private static UnitDimensionDto MapRuntime(UnitDimension dimension, bool enabledUnitsOnly) => new(
        dimension.NId, dimension.Name, dimension.Description, dimension.ScopeType.ToString(),
        dimension.TenantNId, dimension.Revision, dimension.Status.ToString(), dimension.SourceRevision,
        dimension.PublishedOn, dimension.IsSystemDefined, dimension.ConversionKind.ToString(), dimension.BaseUnitNId,
        dimension.Units.Where(unit => !enabledUnitsOnly || unit.Enabled).Select(MapRuntimeUnit).ToArray());

    private static UnitDefinitionDto MapUnit(UnitDefinition unit) => new(
        unit.Id, unit.NId, unit.Name, unit.Symbol, FormatDecimal(unit.FactorToBase), FormatDecimal(unit.OffsetToBase),
        unit.DecimalPlaces, unit.RoundingMode.ToString(), unit.Enabled, unit.Sort, unit.IsFrozen, unit.IsLocked);

    private static RuntimeUnitDefinitionDto MapRuntimeUnit(UnitDefinition unit) => new(
        unit.NId, unit.Name, unit.Symbol, FormatDecimal(unit.FactorToBase), FormatDecimal(unit.OffsetToBase),
        unit.DecimalPlaces, unit.RoundingMode.ToString(), unit.Enabled, unit.Sort);

    private static string FormatDecimal(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
    private static string FormatResult(decimal value, int decimalPlaces) => value.ToString($"F{decimalPlaces}", CultureInfo.InvariantCulture);
    private static ReferenceDataException NotFound() => new("REF-UNIT-DIMENSION-NOT-FOUND", 404);
}
