using System.Text.Json;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Application.Caching;
using IndustrialPlatform.ReferenceData.Application.Dictionary;
using IndustrialPlatform.ReferenceData.Application.UnitOfMeasure;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.Metadata;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Domain.Metadata;

namespace IndustrialPlatform.ReferenceData.Application.Metadata;

public sealed class MetadataSchemaService
{
    private readonly IMetadataSchemaRepository repository;
    private readonly DictionaryService dictionaries;
    private readonly UnitDimensionService units;
    private readonly IReferenceDataCache cache;

    public MetadataSchemaService(IMetadataSchemaRepository repository, DictionaryService dictionaries,
        UnitDimensionService units) : this(repository, dictionaries, units, NullReferenceDataCache.Instance) { }

    public MetadataSchemaService(IMetadataSchemaRepository repository, DictionaryService dictionaries,
        UnitDimensionService units, IReferenceDataCache cache)
    {
        this.repository = repository;
        this.dictionaries = dictionaries;
        this.units = units;
        this.cache = cache;
    }

    public Task<(IReadOnlyList<MetadataSchemaSummaryDto> Items, long Total)> SearchAsync(
        ReferenceDataActor actor, MetadataSchemaQuery query, CancellationToken cancellationToken)
    {
        ValidatePage(query.PageIndex, query.PageSize, query.Keyword);
        if (query.ScopeType is not null) ParseScope(query.ScopeType);
        if (query.Status is not null
            && !Enum.GetNames<PublicationStatus>().Contains(query.Status, StringComparer.Ordinal))
            throw Invalid("status");
        return repository.SearchAsync(actor.TenantNId, query, cancellationToken);
    }

    public async Task<MetadataSchemaDetailDto> GetAsync(
        ReferenceDataActor actor, Guid id, CancellationToken cancellationToken) =>
        MapDetail(await LoadAsync(actor, id, cancellationToken));

    public async Task<MetadataSchemaDetailDto> CreateAsync(
        ReferenceDataActor actor, CreateMetadataSchemaRequest request, CancellationToken cancellationToken)
    {
        var scope = ParseScope(request.ScopeType);
        if (scope == ReferenceScopeType.Platform && !actor.CanManagePlatform) throw Denied();
        var schema = new EntitySchema(request.NId, request.Name, request.Description, scope,
            scope == ReferenceScopeType.Tenant ? actor.TenantNId : null, null);
        schema.Update(request.Name, request.Description, ParseAttributes(request.Attributes));
        await repository.CreateAsync(schema, null, cancellationToken);
        await InvalidateAsync(schema.NId, CancellationToken.None);
        return MapDetail(schema);
    }

    public async Task<MetadataSchemaDetailDto> UpdateAsync(ReferenceDataActor actor, Guid id,
        UpdateMetadataSchemaRequest request, CancellationToken cancellationToken)
    {
        var schema = await LoadWritableAsync(actor, id, request.ExpectedOptimisticVersion,
            request.ExpectedConcurrencyVersion, cancellationToken);
        schema.Update(request.Name, request.Description, ParseAttributes(request.Attributes));
        await repository.SaveAsync([new(schema, request.ExpectedOptimisticVersion,
            request.ExpectedConcurrencyVersion, SaveAttributes: true)], cancellationToken);
        await InvalidateAsync(schema.NId, CancellationToken.None);
        return MapDetail(schema);
    }

    public async Task<MetadataSchemaDetailDto> CloneAsync(ReferenceDataActor actor, Guid id,
        PublishOrDisableRequest request, CancellationToken cancellationToken)
    {
        ValidateReason(request.ChangeReason, required: false);
        var source = await LoadWritableAsync(actor, id, request.ExpectedOptimisticVersion,
            request.ExpectedConcurrencyVersion, cancellationToken);
        var clone = source.Clone(await repository.GetNextRevisionAsync(source, cancellationToken));
        await repository.CreateAsync(clone,
            new(source, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion), cancellationToken);
        return MapDetail(clone);
    }

    public async Task<MetadataPublicationCheckDto> CheckPublicationAsync(
        ReferenceDataActor actor, Guid id, CancellationToken cancellationToken)
    {
        var schema = await LoadAsync(actor, id, cancellationToken);
        return await CheckPublicationAsync(actor, schema, cancellationToken);
    }

    public async Task<MetadataSchemaDetailDto> PublishAsync(ReferenceDataActor actor, Guid id,
        PublishOrDisableRequest request, CancellationToken cancellationToken)
    {
        ValidateReason(request.ChangeReason, required: false);
        var schema = await LoadWritableAsync(actor, id, request.ExpectedOptimisticVersion,
            request.ExpectedConcurrencyVersion, cancellationToken);
        var check = await CheckPublicationAsync(actor, schema, cancellationToken);
        if (check.Errors.Count > 0)
        {
            var issue = check.Errors[0];
            throw new ReferenceDataException(issue.Code, issue.Code == "REF-INVALID-STATE" ? 409 : 422,
                issue.Field);
        }

        var previous = await repository.GetPublishedInScopeAsync(schema, cancellationToken);
        var changes = new List<MetadataSchemaChange>();
        if (previous is not null && previous.Id != schema.Id)
        {
            var version = previous.OptimisticVersion;
            var token = previous.ConcurrencyVersion;
            previous.Supersede();
            changes.Add(new(previous, version, token));
        }
        schema.Publish(actor.UserNId);
        changes.Add(new(schema, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion,
            SaveAttributes: true));
        await repository.SaveAsync(changes, cancellationToken);
        await InvalidateAsync(schema.NId, CancellationToken.None);
        return MapDetail(schema);
    }

    public async Task<MetadataSchemaDetailDto> DisableAsync(ReferenceDataActor actor, Guid id,
        PublishOrDisableRequest request, CancellationToken cancellationToken)
    {
        ValidateReason(request.ChangeReason, required: true);
        var schema = await LoadWritableAsync(actor, id, request.ExpectedOptimisticVersion,
            request.ExpectedConcurrencyVersion, cancellationToken);
        schema.Disable();
        await repository.SaveAsync([new(schema, request.ExpectedOptimisticVersion,
            request.ExpectedConcurrencyVersion)], cancellationToken);
        await InvalidateAsync(schema.NId, CancellationToken.None);
        return MapDetail(schema);
    }

    public async Task<EffectiveSchemaDto> GetEffectiveAsync(ReferenceDataActor actor, string nId,
        string? factoryId, CancellationToken cancellationToken)
    {
        if (factoryId is not null) throw new ReferenceDataException("REF-SCOPE-FACTORY-NOT-READY", 409);
        var normalizedNId = NormalizeNId(nId);
        var key = ReferenceDataCacheKeys.Metadata(ReferenceDataCacheKeys.TenantKey(actor.TenantNId), normalizedNId);
        return await cache.GetOrLoadAsync(key, ReferenceDataCacheTtl.Metadata, async () =>
        {
            var schema = await repository.GetEffectiveAsync(actor.TenantNId, normalizedNId, cancellationToken)
                ?? throw NotFound();
            return MapEffective(schema, enabledAttributesOnly: true);
        }, cancellationToken);
    }

    public async Task<EffectiveSchemaDto> GetRevisionAsync(ReferenceDataActor actor, string nId, int revision,
        string? sourceScope, string? sourceTenantNId, CancellationToken cancellationToken)
    {
        if (revision < 1) throw NotFound();
        var source = ResolveSource(actor, sourceScope, sourceTenantNId);
        var schema = await repository.GetRevisionAsync(actor.TenantNId, NormalizeNId(nId), source,
            revision, cancellationToken) ?? throw NotFound();
        return MapEffective(schema, enabledAttributesOnly: false);
    }

    private Task InvalidateAsync(string nId, CancellationToken cancellationToken) =>
        cache.InvalidateAsync(ReferenceDataCacheKeys.MetadataEntries(nId), cancellationToken);

    private async Task<MetadataPublicationCheckDto> CheckPublicationAsync(ReferenceDataActor actor,
        EntitySchema schema, CancellationToken cancellationToken)
    {
        var errors = new List<MetadataPublicationIssue>();
        try { schema.CheckPublication(); }
        catch (ReferenceDataException error) { errors.Add(new(error.ErrorCode, error.Field)); }
        errors.AddRange(await ValidateReferencesAsync(actor, schema, cancellationToken));

        var previous = await repository.GetLastPublishedAsync(schema, cancellationToken);
        var old = previous?.Attributes.ToDictionary(attribute => attribute.NId, StringComparer.Ordinal)
            ?? new Dictionary<string, AttributeDefinition>(StringComparer.Ordinal);
        var current = schema.Attributes.ToDictionary(attribute => attribute.NId, StringComparer.Ordinal);
        if (old.Keys.Any(nId => !current.ContainsKey(nId)))
            errors.Add(new("REF-VALIDATION-FAILED", "attributes"));

        var added = current.Keys.Where(nId => !old.ContainsKey(nId)).Order(StringComparer.Ordinal).ToArray();
        var disabled = current.Values.Where(attribute => old.TryGetValue(attribute.NId, out var before)
                && before.Settings.Enabled && !attribute.Settings.Enabled)
            .Select(attribute => attribute.NId).Order(StringComparer.Ordinal).ToArray();
        var tightened = current.Values.Where(attribute => old.TryGetValue(attribute.NId, out var before)
                && IsTightened(before.Settings, attribute.Settings))
            .Select(attribute => attribute.NId).Order(StringComparer.Ordinal).ToArray();
        var relaxed = current.Values.Where(attribute => old.TryGetValue(attribute.NId, out var before)
                && IsRelaxed(before.Settings, attribute.Settings))
            .Select(attribute => attribute.NId).Order(StringComparer.Ordinal).ToArray();
        var incompatible = new List<MetadataCompatibilityChange>();
        incompatible.AddRange(current.Values.Where(attribute => !old.ContainsKey(attribute.NId)
                && attribute.Settings.Enabled && attribute.Settings.Required)
            .Select(attribute => new MetadataCompatibilityChange(attribute.NId, "RequiredAttributeAdded")));
        foreach (var attribute in current.Values)
        {
            if (!old.TryGetValue(attribute.NId, out var before)) continue;
            var prior = before.Settings;
            var next = attribute.Settings;
            if (prior.DataType != next.DataType) incompatible.Add(new(attribute.NId, "DataTypeChanged"));
            if (prior.IsArray != next.IsArray) incompatible.Add(new(attribute.NId, "CardinalityChanged"));
            if (!prior.Required && next.Required) incompatible.Add(new(attribute.NId, "RequiredTightened"));
            if (PrecisionTightened(prior, next)) incompatible.Add(new(attribute.NId, "PrecisionTightened"));
            if (UnitDimensionChanged(prior, next)) incompatible.Add(new(attribute.NId, "UnitDimensionChanged"));
            if (OtherConstraintTightened(prior, next)) incompatible.Add(new(attribute.NId, "ConstraintTightened"));
        }
        return new(previous?.Revision, added, tightened, relaxed, disabled,
            incompatible.Distinct().OrderBy(change => change.AttributeNId, StringComparer.Ordinal)
                .ThenBy(change => change.Code, StringComparer.Ordinal).ToArray(), errors);
    }

    private async Task<IReadOnlyList<MetadataPublicationIssue>> ValidateReferencesAsync(
        ReferenceDataActor actor, EntitySchema schema, CancellationToken cancellationToken)
    {
        var errors = new List<MetadataPublicationIssue>();
        var dictionaryActor = schema.ScopeType == ReferenceScopeType.Platform
            ? actor with { TenantNId = string.Empty }
            : actor;
        foreach (var group in schema.Attributes.Where(attribute => attribute.Settings.DataType == ReferenceDataType.Enum)
                     .GroupBy(attribute => attribute.Settings.DictionaryNId!, StringComparer.Ordinal))
        {
            try
            {
                var dictionary = await dictionaries.GetEffectiveAsync(dictionaryActor, group.Key, null,
                    cancellationToken);
                var items = dictionary.Items.Select(item => item.NId).ToHashSet(StringComparer.Ordinal);
                foreach (var attribute in group.Where(attribute => attribute.Settings.DefaultValue is not null))
                {
                    var defaults = EnumDefaults(attribute.Settings);
                    if (defaults.Any(value => !items.Contains(value)))
                        errors.Add(new("REF-METADATA-DICTIONARY-INVALID",
                            $"attributes.{attribute.NId}.defaultValue"));
                }
            }
            catch (ReferenceDataException error) when (error.Status != 503)
            {
                errors.AddRange(group.Select(attribute => new MetadataPublicationIssue(
                    "REF-METADATA-DICTIONARY-INVALID", $"attributes.{attribute.NId}.dictionaryNId")));
            }
        }

        foreach (var attribute in schema.Attributes.Where(attribute => attribute.Settings.UnitDimensionNId is not null))
        {
            var settings = attribute.Settings;
            try
            {
                var dimension = await units.GetRevisionAsync(actor, settings.UnitDimensionNId!,
                    settings.UnitRevision!.Value, settings.UnitSourceScope!.Value.ToString(),
                    settings.UnitSourceTenantNId, cancellationToken);
                if (dimension.Status != nameof(PublicationStatus.Published)
                    || settings.DefaultUnitNId is not null
                    && dimension.Units.All(unit => unit.NId != settings.DefaultUnitNId || !unit.Enabled))
                    throw new ReferenceDataException("REF-METADATA-UNIT-INVALID", 422);
            }
            catch (ReferenceDataException error) when (error.Status != 503)
            {
                errors.Add(new("REF-METADATA-UNIT-INVALID",
                    $"attributes.{attribute.NId}.unitDimensionNId"));
            }
        }
        return errors;
    }

    private async Task<EntitySchema> LoadAsync(
        ReferenceDataActor actor, Guid id, CancellationToken cancellationToken) =>
        await repository.GetAsync(actor.TenantNId, id, cancellationToken) ?? throw NotFound();

    private async Task<EntitySchema> LoadWritableAsync(ReferenceDataActor actor, Guid id,
        long version, Guid token, CancellationToken cancellationToken)
    {
        var schema = await LoadAsync(actor, id, cancellationToken);
        if (schema.ScopeType == ReferenceScopeType.Platform && !actor.CanManagePlatform) throw Denied();
        schema.CheckVersion(version, token);
        return schema;
    }

    private static MetadataAttributeInput[] ParseAttributes(IReadOnlyList<MetadataAttributeRequest> attributes)
    {
        if (attributes is null || attributes.Count > 200) throw Invalid("attributes");
        return attributes.Select((attribute, index) =>
        {
            if (attribute is null || !Enum.TryParse<ReferenceDataType>(attribute.DataType, false, out var type)
                || !Enum.IsDefined(type) || type == ReferenceDataType.Json)
                throw Invalid($"attributes[{index}].dataType");
            ReferenceScopeType? unitScope = null;
            if (attribute.UnitSourceScope is not null)
            {
                if (!Enum.TryParse<ReferenceScopeType>(attribute.UnitSourceScope, false, out var parsed)
                    || !Enum.IsDefined(parsed)) throw Invalid($"attributes[{index}].unitSourceScope");
                unitScope = parsed;
            }
            return new MetadataAttributeInput(attribute.NId, new(attribute.Name, type, attribute.Required,
                attribute.IsArray, attribute.Enabled, attribute.Sort, attribute.DefaultValue,
                attribute.MinLength, attribute.MaxLength, attribute.MinValue, attribute.MaxValue,
                attribute.Pattern, attribute.DictionaryNId, attribute.ReferenceTarget, attribute.Precision,
                attribute.Scale, attribute.UnitDimensionNId, attribute.DefaultUnitNId, attribute.UnitRevision,
                unitScope, attribute.UnitSourceTenantNId, attribute.Description));
        }).ToArray();
    }

    private static bool IsTightened(MetadataAttributeSettings old, MetadataAttributeSettings next) =>
        old.DataType != next.DataType || old.IsArray != next.IsArray || !old.Required && next.Required
        || LowerTightened(old.MinLength, next.MinLength) || UpperTightened(old.MaxLength, next.MaxLength)
        || LowerTightened(old.MinValue, next.MinValue) || UpperTightened(old.MaxValue, next.MaxValue)
        || old.Pattern != next.Pattern && next.Pattern is not null || PrecisionTightened(old, next)
        || UnitDimensionChanged(old, next);

    private static bool IsRelaxed(MetadataAttributeSettings old, MetadataAttributeSettings next) =>
        old.Required && !next.Required || LowerRelaxed(old.MinLength, next.MinLength)
        || UpperRelaxed(old.MaxLength, next.MaxLength) || LowerRelaxed(old.MinValue, next.MinValue)
        || UpperRelaxed(old.MaxValue, next.MaxValue) || old.Pattern is not null && next.Pattern is null
        || PrecisionRelaxed(old, next);

    private static bool PrecisionTightened(MetadataAttributeSettings old, MetadataAttributeSettings next) =>
        UpperTightened(old.Precision, next.Precision) || UpperTightened(old.Scale, next.Scale);
    private static bool PrecisionRelaxed(MetadataAttributeSettings old, MetadataAttributeSettings next) =>
        UpperRelaxed(old.Precision, next.Precision) || UpperRelaxed(old.Scale, next.Scale);
    private static bool OtherConstraintTightened(MetadataAttributeSettings old, MetadataAttributeSettings next) =>
        LowerTightened(old.MinLength, next.MinLength) || UpperTightened(old.MaxLength, next.MaxLength)
        || LowerTightened(old.MinValue, next.MinValue) || UpperTightened(old.MaxValue, next.MaxValue)
        || old.Pattern != next.Pattern && next.Pattern is not null;
    private static bool UnitDimensionChanged(MetadataAttributeSettings old, MetadataAttributeSettings next) =>
        old.UnitDimensionNId != next.UnitDimensionNId || old.UnitSourceScope != next.UnitSourceScope
        || old.UnitSourceTenantNId != next.UnitSourceTenantNId;
    private static bool LowerTightened<T>(T? old, T? next) where T : struct, IComparable<T> =>
        next is not null && (old is null || next.Value.CompareTo(old.Value) > 0);
    private static bool UpperTightened<T>(T? old, T? next) where T : struct, IComparable<T> =>
        next is not null && (old is null || next.Value.CompareTo(old.Value) < 0);
    private static bool LowerRelaxed<T>(T? old, T? next) where T : struct, IComparable<T> =>
        old is not null && (next is null || next.Value.CompareTo(old.Value) < 0);
    private static bool UpperRelaxed<T>(T? old, T? next) where T : struct, IComparable<T> =>
        old is not null && (next is null || next.Value.CompareTo(old.Value) > 0);

    private static MetadataSchemaDetailDto MapDetail(EntitySchema schema) => new(
        schema.Id, schema.NId, schema.Name, schema.Description, schema.ScopeType.ToString(), schema.TenantNId,
        schema.Revision, schema.Status.ToString(), schema.SourceRevision, schema.Attributes.Select(MapAttribute).ToArray(),
        schema.OptimisticVersion, schema.ConcurrencyVersion, schema.LastUpdatedOn, schema.PublishedOn,
        schema.PublishedBy, schema.IsFrozen, schema.IsLocked);

    public static MetadataSchemaSummaryDto ToSummary(EntitySchema schema) => new(
        schema.Id, schema.NId, schema.Name, schema.Description, schema.ScopeType.ToString(), schema.TenantNId,
        schema.Revision, schema.Status.ToString(), schema.SourceRevision, schema.Attributes.Count,
        schema.OptimisticVersion, schema.ConcurrencyVersion, schema.LastUpdatedOn, schema.PublishedOn,
        schema.PublishedBy, schema.IsFrozen, schema.IsLocked);

    private static EffectiveSchemaDto MapEffective(EntitySchema schema, bool enabledAttributesOnly) => new(
        schema.NId, schema.Name, schema.Description,
        schema.Attributes.Where(attribute => !enabledAttributesOnly || attribute.Settings.Enabled)
            .Select(MapRuntimeAttribute).ToArray(),
        schema.ScopeType.ToString(), schema.TenantNId, schema.Revision, schema.PublishedOn!.Value);

    private static RuntimeMetadataAttributeDto MapRuntimeAttribute(AttributeDefinition attribute)
    {
        var settings = attribute.Settings;
        return new(attribute.NId, settings.Name, settings.DataType.ToString(), settings.Required,
            settings.IsArray, settings.Enabled, settings.Sort, settings.DefaultValue, settings.MinLength,
            settings.MaxLength, settings.MinValue, settings.MaxValue, settings.Pattern, settings.DictionaryNId,
            settings.ReferenceTarget, settings.Precision, settings.Scale, settings.UnitDimensionNId,
            settings.DefaultUnitNId, settings.UnitRevision, settings.UnitSourceScope?.ToString(),
            settings.UnitSourceTenantNId, settings.Description);
    }

    private static string[] EnumDefaults(MetadataAttributeSettings settings)
    {
        if (!settings.IsArray) return [settings.DefaultValue!];
        using var document = JsonDocument.Parse(settings.DefaultValue!);
        return document.RootElement.EnumerateArray().Select(item => item.GetString()!).ToArray();
    }

    private static MetadataAttributeDto MapAttribute(AttributeDefinition attribute)
    {
        var settings = attribute.Settings;
        return new(attribute.Id, attribute.NId, settings.Name, settings.DataType.ToString(), settings.Required,
            settings.IsArray, settings.Enabled, settings.Sort, settings.DefaultValue, settings.MinLength,
            settings.MaxLength, settings.MinValue, settings.MaxValue, settings.Pattern, settings.DictionaryNId,
            settings.ReferenceTarget, settings.Precision, settings.Scale, settings.UnitDimensionNId,
            settings.DefaultUnitNId, settings.UnitRevision, settings.UnitSourceScope?.ToString(),
            settings.UnitSourceTenantNId, settings.Description, attribute.WasPublished, attribute.IsFrozen,
            attribute.IsLocked);
    }

    private static ReferenceScopeType ParseScope(string? value)
    {
        if (value is null || !Enum.TryParse<ReferenceScopeType>(value, false, out var scope)
            || !Enum.IsDefined(scope)) throw new ReferenceDataException("REF-SCOPE-INVALID");
        if (scope == ReferenceScopeType.Factory)
            throw new ReferenceDataException("REF-SCOPE-FACTORY-NOT-READY", 409);
        return scope;
    }

    private static string ResolveSource(ReferenceDataActor actor, string? sourceScope, string? sourceTenantNId)
    {
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

    private static string NormalizeNId(string value)
    {
        try { return ReferenceValidation.NId(value); }
        catch (ReferenceDataException) { throw NotFound(); }
    }

    private static void ValidatePage(int pageIndex, int pageSize, string? keyword)
    {
        if (pageIndex < 1 || pageSize is < 1 or > 100 || keyword?.Length > 200) throw Invalid("pageIndex");
    }

    private static void ValidateReason(string? reason, bool required)
    {
        if (required && string.IsNullOrWhiteSpace(reason) || reason?.Length > 1000) throw Invalid("changeReason");
    }

    private static ReferenceDataException Invalid(string field) =>
        new("REF-VALIDATION-FAILED", 400, field);
    private static ReferenceDataException NotFound() => new("REF-METADATA-NOT-FOUND", 404);
    private static ReferenceDataException Denied() => new("ID_PERMISSION_DENIED", 403);
}
