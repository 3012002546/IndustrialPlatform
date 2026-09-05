using System.Text.Json;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Application.Caching;
using IndustrialPlatform.ReferenceData.Application.Dictionary;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.DynamicProperty;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Domain.DynamicProperty;

namespace IndustrialPlatform.ReferenceData.Application.DynamicProperty;

public sealed class DynamicConfigurationService
{
    private readonly IDynamicConfigurationRepository repository;
    private readonly DictionaryService dictionaries;
    private readonly IReferenceDataCache cache;

    public DynamicConfigurationService(IDynamicConfigurationRepository repository, DictionaryService dictionaries) :
        this(repository, dictionaries, NullReferenceDataCache.Instance)
    { }

    public DynamicConfigurationService(IDynamicConfigurationRepository repository, DictionaryService dictionaries,
        IReferenceDataCache cache)
    {
        this.repository = repository;
        this.dictionaries = dictionaries;
        this.cache = cache;
    }

    public Task<(IReadOnlyList<DynamicConfigurationSummaryDto> Items, long Total)> SearchAsync(ReferenceDataActor actor,
        DynamicConfigurationQuery query, CancellationToken cancellationToken)
    {
        Page(query.PageIndex, query.PageSize, query.Keyword);
        if (query.ScopeType is not null) Scope(query.ScopeType);
        if (query.Status is not null && !Enum.GetNames<PublicationStatus>().Contains(query.Status, StringComparer.Ordinal)) throw Invalid("status");
        return repository.SearchAsync(actor.TenantNId, query, cancellationToken);
    }

    public async Task<DynamicConfigurationDetailDto> GetAsync(ReferenceDataActor actor, Guid id, CancellationToken cancellationToken)
    {
        var definition = await LoadAsync(actor, id, false, cancellationToken);
        var counts = await repository.CountAsync(id, cancellationToken);
        return Map(definition, counts.Records, counts.Values);
    }

    public async Task<DynamicConfigurationDetailDto> CreateAsync(ReferenceDataActor actor, CreateDynamicConfigurationRequest request, CancellationToken cancellationToken)
    {
        var scope = Scope(request.ScopeType);
        if (scope == ReferenceScopeType.Platform && !actor.CanManagePlatform) throw Denied();
        var definition = new DynamicConfigDefinition(request.NId, request.Name, request.Description, scope,
            scope == ReferenceScopeType.Tenant ? actor.TenantNId : null, request.ScopeId);
        definition.Update(request.Name, request.Description, Fields(request.Fields));
        await ValidateEnumsAsync(actor, definition, cancellationToken);
        await repository.CreateAsync(definition, null, cancellationToken);
        await InvalidateAsync(definition.NId, CancellationToken.None);
        return Map(definition);
    }

    public async Task<DynamicConfigurationDetailDto> UpdateAsync(ReferenceDataActor actor, Guid id,
        UpdateDynamicConfigurationRequest request, CancellationToken cancellationToken)
    {
        var definition = await WritableAsync(actor, id, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, cancellationToken);
        definition.Update(request.Name, request.Description, Fields(request.Fields));
        await ValidateEnumsAsync(actor, definition, cancellationToken);
        await repository.SaveAsync([new(definition, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, SaveFields: true)], cancellationToken);
        await InvalidateAsync(definition.NId, CancellationToken.None);
        return Map(definition);
    }

    public async Task<DynamicConfigurationDetailDto> CloneAsync(ReferenceDataActor actor, Guid id, PublishOrDisableRequest request, CancellationToken cancellationToken)
    {
        var source = await WritableAsync(actor, id, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, cancellationToken);
        var clone = source.Clone(await repository.GetNextRevisionAsync(source, cancellationToken));
        await repository.CreateAsync(clone, new(source, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion), cancellationToken);
        return Map(clone);
    }

    public async Task<DynamicPublicationCheckDto> CheckPublicationAsync(ReferenceDataActor actor, Guid id, CancellationToken cancellationToken)
    {
        var definition = await LoadAsync(actor, id, true, cancellationToken);
        return await CheckPublicationAsync(actor, definition, cancellationToken);
    }

    private async Task<DynamicPublicationCheckDto> CheckPublicationAsync(ReferenceDataActor actor, DynamicConfigDefinition definition, CancellationToken cancellationToken)
    {
        var errors = new List<DynamicPublicationIssue>();
        try { definition.CheckPublication(); await ValidateEnumsAsync(actor, definition, cancellationToken); }
        catch (ReferenceDataException error) { errors.Add(new(error.ErrorCode, error.Field)); }
        var previous = await repository.GetLastPublishedAsync(definition, cancellationToken);
        var oldFields = previous?.Fields ?? [];
        if (oldFields.Any(old => definition.Fields.All(field => field.NId != old.NId || field.Settings.DataType != old.Settings.DataType)))
            errors.Add(new("REF-DYNAMIC-CONFIG-FIELD-INVALID", "fields"));
        return new(previous?.Revision,
            definition.Fields.Where(field => oldFields.All(old => old.NId != field.NId)).Select(field => field.NId).ToArray(),
            definition.Fields.Where(field => oldFields.Any(old => old.NId == field.NId && old.Settings != field.Settings)).Select(field => field.NId).ToArray(),
            definition.Fields.Where(field => !field.Settings.Enabled && oldFields.Any(old => old.NId == field.NId && old.Settings.Enabled)).Select(field => field.NId).ToArray(),
            definition.Records.Count, definition.Records.Sum(record => (long)record.Values.Count), errors);
    }

    public async Task<DynamicConfigurationDetailDto> PublishAsync(ReferenceDataActor actor, Guid id, PublishOrDisableRequest request, CancellationToken cancellationToken)
    {
        var definition = await WritableAsync(actor, id, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, cancellationToken);
        var check = await CheckPublicationAsync(actor, definition, cancellationToken);
        if (check.Errors.Count > 0) throw new ReferenceDataException(check.Errors[0].Code,
            check.Errors[0].Code == "REF-INVALID-STATE" ? 409 : 422, check.Errors[0].Field);
        var previous = await repository.GetPublishedInScopeAsync(definition, cancellationToken);
        var changes = new List<DynamicConfigurationChange>();
        if (previous is not null && previous.Id != id)
        {
            changes.Add(new(previous, previous.OptimisticVersion, previous.ConcurrencyVersion));
            previous.Supersede();
        }
        definition.Publish(actor.UserNId);
        changes.Add(new(definition, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, SaveFields: true));
        await repository.SaveAsync(changes, cancellationToken);
        await InvalidateAsync(definition.NId, CancellationToken.None);
        return Map(definition);
    }

    public async Task<DynamicConfigurationDetailDto> DisableAsync(ReferenceDataActor actor, Guid id, PublishOrDisableRequest request, CancellationToken cancellationToken)
    {
        Reason(request.ChangeReason);
        var definition = await WritableAsync(actor, id, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, cancellationToken);
        definition.Disable();
        await repository.SaveAsync([new(definition, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion)], cancellationToken);
        await InvalidateAsync(definition.NId, CancellationToken.None);
        return Map(definition);
    }

    public async Task<(IReadOnlyList<DynamicRecordDto> Items, long Total)> SearchRecordsAsync(ReferenceDataActor actor, Guid id,
        DynamicRecordQuery query, CancellationToken cancellationToken)
    {
        query = RecordQuery(query);
        var definition = await LoadAsync(actor, id, false, cancellationToken);
        var page = await repository.SearchRecordsAsync(id, query, false, cancellationToken);
        return (page.Items.Select(record => MapRecord(definition, record, false)).ToArray(), page.Total);
    }

    public async Task<DynamicRecordMutationDto> AddRecordAsync(ReferenceDataActor actor, Guid id, DynamicRecordRequest request, CancellationToken cancellationToken)
    {
        var definition = await WritableAsync(actor, id, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, cancellationToken);
        var record = definition.AddRecord(request.NId, Settings(request), Values(definition, request.Values));
        await ValidateEnumsAsync(actor, definition, cancellationToken);
        await repository.SaveAsync([new(definition, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, true, record.Id)], cancellationToken);
        await InvalidateAsync(definition.NId, CancellationToken.None);
        return new(MapRecord(definition, record, false), definition.OptimisticVersion, definition.ConcurrencyVersion);
    }

    public async Task<DynamicRecordMutationDto> UpdateRecordAsync(ReferenceDataActor actor, Guid id, Guid recordId,
        DynamicRecordRequest request, CancellationToken cancellationToken)
    {
        var definition = await WritableAsync(actor, id, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, cancellationToken);
        if (definition.Record(recordId).Settings.Enabled && !request.Enabled) RequireDisable(actor.DynamicPropertyDisableDecision);
        var record = definition.UpdateRecord(recordId, request.NId, Settings(request), Values(definition, request.Values));
        await ValidateEnumsAsync(actor, definition, cancellationToken);
        await repository.SaveAsync([new(definition, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, true, record.Id)], cancellationToken);
        await InvalidateAsync(definition.NId, CancellationToken.None);
        return new(MapRecord(definition, record, false), definition.OptimisticVersion, definition.ConcurrencyVersion);
    }

    public async Task<DynamicRecordMutationDto> DisableRecordAsync(ReferenceDataActor actor, Guid id, Guid recordId,
        PublishOrDisableRequest request, CancellationToken cancellationToken)
    {
        Reason(request.ChangeReason);
        var definition = await WritableAsync(actor, id, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, cancellationToken);
        var record = definition.DisableRecord(recordId);
        await repository.SaveAsync([new(definition, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, RecordId: record.Id)], cancellationToken);
        await InvalidateAsync(definition.NId, CancellationToken.None);
        return new(MapRecord(definition, record, false), definition.OptimisticVersion, definition.ConcurrencyVersion);
    }

    public async Task<DynamicSchemaDto> GetSchemaAsync(ReferenceDataActor actor, string nId, string? factoryId, CancellationToken cancellationToken)
    {
        RejectFactory(factoryId);
        var normalizedNId = ReferenceValidation.NId(nId);
        var key = ReferenceDataCacheKeys.DynamicConfiguration(
            ReferenceDataCacheKeys.TenantKey(actor.TenantNId), normalizedNId, "current", "schema");
        return await cache.GetOrLoadAsync(key, ReferenceDataCacheTtl.DynamicSchema, async () =>
        {
            var definition = await repository.GetEffectiveAsync(actor.TenantNId, normalizedNId, cancellationToken)
                ?? throw NotFound();
            return new DynamicSchemaDto(definition.Id, definition.NId, definition.Name,
                definition.Fields.Where(field => field.Settings.Enabled).Select(MapField).ToArray(),
                definition.ScopeType.ToString(), definition.TenantNId, definition.Revision,
                definition.PublishedOn!.Value);
        }, cancellationToken);
    }

    public async Task<(IReadOnlyList<DynamicRecordDto> Items, long Total)> GetRecordsAsync(ReferenceDataActor actor, string nId,
        int? revision, string? sourceScope, string? sourceTenantNId, string? factoryId, DynamicRecordQuery query, CancellationToken cancellationToken)
    {
        query = RecordQuery(query);
        var definition = await SnapshotAsync(actor, nId, revision, sourceScope, sourceTenantNId, factoryId, cancellationToken);
        var key = ReferenceDataCacheKeys.DynamicConfiguration(
            ReferenceDataCacheKeys.TenantKey(definition.TenantNId), definition.NId,
            definition.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ReferenceDataCacheKeys.PageKey(query.PageIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                query.PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture), query.Keyword,
                query.NId, query.Category));
        var cached = await cache.GetOrLoadAsync(key, ReferenceDataCacheTtl.DynamicRecords, async () =>
        {
            var page = await repository.SearchRecordsAsync(definition.Id, query, true, cancellationToken);
            return new ReferenceDataCachePage<DynamicRecordDto>(
                page.Items.Select(record => MapRecord(definition, record, true)).ToArray(), page.Total,
                definition.Revision);
        }, cancellationToken);
        return (cached.Items, cached.Total);
    }

    public async Task<DynamicRecordDto> GetRecordAsync(ReferenceDataActor actor, string nId, string recordNId,
        int? revision, string? sourceScope, string? sourceTenantNId, string? factoryId, CancellationToken cancellationToken)
    {
        var result = await GetRecordsAsync(actor, nId, revision, sourceScope, sourceTenantNId, factoryId,
            new(1, 1, NId: recordNId), cancellationToken);
        return result.Items.Count == 0 ? throw NotFound() : result.Items[0];
    }

    private async Task<DynamicConfigDefinition> SnapshotAsync(ReferenceDataActor actor, string nId, int? revision,
        string? sourceScope, string? sourceTenantNId, string? factoryId, CancellationToken cancellationToken)
    {
        RejectFactory(factoryId);
        if (revision is null) throw new ReferenceDataException("REF-DYNAMIC-CONFIG-REVISION-REQUIRED", 400, "revision");
        if (revision < 1) throw new ReferenceDataException("REF-DYNAMIC-CONFIG-REVISION-MISMATCH", 409, "revision");
        var scope = Scope(sourceScope);
        if (scope == ReferenceScopeType.Platform && sourceTenantNId is not null) throw Invalid("sourceTenantNId");
        if (scope == ReferenceScopeType.Tenant)
        {
            if (string.IsNullOrWhiteSpace(sourceTenantNId)) throw Invalid("sourceTenantNId");
            if (sourceTenantNId != actor.TenantNId) throw NotFound();
        }
        return await repository.GetSnapshotAsync(actor.TenantNId, ReferenceValidation.NId(nId), scope.ToString(), revision.Value, cancellationToken) ?? throw NotFound();
    }

    private Task InvalidateAsync(string nId, CancellationToken cancellationToken) =>
        cache.InvalidateAsync(ReferenceDataCacheKeys.DynamicConfigurations(nId), cancellationToken);

    private async Task ValidateEnumsAsync(ReferenceDataActor actor, DynamicConfigDefinition definition, CancellationToken cancellationToken)
    {
        var enumActor = definition.ScopeType == ReferenceScopeType.Platform ? actor with { TenantNId = string.Empty } : actor;
        foreach (var group in definition.Fields.Where(field => field.Settings.DataType == ReferenceDataType.Enum).GroupBy(field => field.Settings.DictionaryNId!))
        {
            var dictionary = await dictionaries.GetEffectiveAsync(enumActor, group.Key, null, cancellationToken);
            var valid = dictionary.Items.Select(item => item.NId).ToHashSet(StringComparer.Ordinal);
            foreach (var field in group)
            {
                var values = definition.Records.SelectMany(record => record.Values).Where(value => value.DynamicConfigFieldDefinitionId == field.Id).Select(value => value.Value);
                if (field.Settings.DefaultValue is not null) values = values.Append(field.Settings.DefaultValue);
                if (values.Any(value => !valid.Contains(value.JsonValue.GetString()!))) throw new ReferenceDataException("REF-DYNAMIC-CONFIG-FIELD-INVALID", 422, $"fields.{field.NId}");
            }
        }
    }

    private async Task<DynamicConfigDefinition> LoadAsync(ReferenceDataActor actor, Guid id, bool includeRecords, CancellationToken cancellationToken) =>
        await repository.GetAsync(actor.TenantNId, id, includeRecords, cancellationToken) ?? throw NotFound();

    private async Task<DynamicConfigDefinition> WritableAsync(ReferenceDataActor actor, Guid id, long version, Guid token, CancellationToken cancellationToken)
    {
        var definition = await LoadAsync(actor, id, true, cancellationToken);
        if (definition.ScopeType == ReferenceScopeType.Platform && !actor.CanManagePlatform) throw Denied();
        definition.CheckVersion(version, token);
        return definition;
    }

    private static DynamicFieldInput[] Fields(IReadOnlyList<DynamicFieldRequest> fields)
    {
        if (fields is null || fields.Count > 100) throw new ReferenceDataException("REF-DYNAMIC-CONFIG-LIMIT-EXCEEDED", 422, "fields");
        return fields.Select((field, index) =>
        {
            if (field is null || !Enum.GetNames<ReferenceDataType>().Contains(field.DataType, StringComparer.Ordinal)) throw FieldInvalid($"fields[{index}].dataType");
            var type = Enum.Parse<ReferenceDataType>(field.DataType);
            var value = field.DefaultValue is null || field.DefaultValue.Value.ValueKind == JsonValueKind.Null ? null : Scalar(type, field.DefaultValue.Value, $"fields[{index}].defaultValue");
            return new DynamicFieldInput(field.NId, new(field.Name, type, field.Required, field.Enabled, field.Sort, value,
                field.MinLength, field.MaxLength, field.MinValue, field.MaxValue, field.Scale, field.Pattern, field.DictionaryNId, field.ReferenceTarget, field.Description));
        }).ToArray();
    }

    private static DynamicValueInput[] Values(DynamicConfigDefinition definition, IReadOnlyDictionary<string, JsonElement> values)
    {
        if (values is null || values.Count > 100) throw new ReferenceDataException("REF-DYNAMIC-CONFIG-LIMIT-EXCEEDED", 422, "values");
        return values.Select(pair =>
        {
            var nId = ReferenceValidation.NId(pair.Key, item: true);
            var field = definition.Fields.FirstOrDefault(item => item.NId == nId) ?? throw new ReferenceDataException("REF-DYNAMIC-CONFIG-FIELD-INVALID", 422, $"values.{pair.Key}");
            return new DynamicValueInput(nId, Scalar(field.Settings.DataType, pair.Value, $"values.{pair.Key}"));
        }).ToArray();
    }

    private static ReferenceScalar Scalar(ReferenceDataType type, JsonElement value, string path)
    {
        try { return ReferenceScalar.Parse(type, value); }
        catch (ReferenceDataException) { throw new ReferenceDataException("REF-DYNAMIC-CONFIG-FIELD-INVALID", 422, path); }
    }
    private static DynamicRecordSettings Settings(DynamicRecordRequest request) => new(request.Name, request.Category, request.Sort, request.Enabled);
    private static DynamicConfigurationDetailDto Map(DynamicConfigDefinition definition, int? recordCount = null, long? valueCount = null) =>
        new(definition.Id, definition.NId, definition.Name, definition.Description, definition.ScopeType.ToString(), definition.TenantNId,
            definition.Revision, definition.Status.ToString(), definition.Fields.Select(MapField).ToArray(), recordCount ?? definition.Records.Count,
            valueCount ?? definition.Records.Sum(record => (long)record.Values.Count), definition.OptimisticVersion, definition.ConcurrencyVersion,
            definition.LastUpdatedOn, definition.PublishedOn, definition.PublishedBy, definition.IsFrozen, definition.IsLocked);
    private static DynamicFieldDto MapField(DynamicConfigFieldDefinition field)
    {
        var settings = field.Settings;
        return new(field.Id, field.NId, settings.Name, settings.DataType.ToString(), settings.Required, settings.Enabled, settings.Sort,
            settings.DefaultValue?.JsonValue, settings.DefaultValue?.CanonicalValue, settings.MinLength, settings.MaxLength, settings.MinValue,
            settings.MaxValue, settings.Scale, settings.Pattern, settings.DictionaryNId, settings.ReferenceTarget, settings.Description, field.HasHadValue, field.WasPublished,
            settings.MinValue?.ToString(System.Globalization.CultureInfo.InvariantCulture), settings.MaxValue?.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    private static DynamicRecordDto MapRecord(DynamicConfigDefinition definition, DynamicConfigRecord record, bool runtime)
    {
        var enabled = definition.Fields.Where(field => field.Settings.Enabled).Select(field => field.Id).ToHashSet();
        var values = record.Values.Where(value => !runtime || enabled.Contains(value.DynamicConfigFieldDefinitionId)).ToArray();
        return new(record.Id, record.NId, record.Settings.Name, record.Settings.Category, record.Settings.Sort, record.Settings.Enabled,
            values.ToDictionary(value => value.FieldNId, value => value.Value.JsonValue), values.ToDictionary(value => value.FieldNId, value => value.Value.CanonicalValue),
            definition.Revision, record.IsFrozen, record.IsLocked);
    }
    private static DynamicRecordQuery RecordQuery(DynamicRecordQuery query)
    {
        Page(query.PageIndex, query.PageSize, query.Keyword);
        if (query.Category?.Length > 100) throw Invalid("category");
        return query with { NId = query.NId is null ? null : ReferenceValidation.NId(query.NId, item: true) };
    }
    private static void Page(int pageIndex, int pageSize, string? keyword)
    {
        if (pageIndex < 1 || pageSize is < 1 or > 100 || keyword?.Length > 200) throw Invalid("pageIndex");
    }
    private static ReferenceScopeType Scope(string? scope)
    {
        if (scope is null || !Enum.GetNames<ReferenceScopeType>().Contains(scope, StringComparer.Ordinal)) throw new ReferenceDataException("REF-SCOPE-INVALID", 400, "scopeType");
        var result = Enum.Parse<ReferenceScopeType>(scope);
        if (result == ReferenceScopeType.Factory) throw new ReferenceDataException("REF-SCOPE-FACTORY-NOT-READY", 409);
        return result;
    }
    private static void RejectFactory(string? factoryId) { if (factoryId is not null) throw new ReferenceDataException("REF-SCOPE-FACTORY-NOT-READY", 409); }
    private static void Reason(string? reason) { if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000) throw Invalid("changeReason"); }
    private static ReferenceDataException Invalid(string field) => new("REF-VALIDATION-FAILED", 400, field);
    private static ReferenceDataException FieldInvalid(string field) => new("REF-DYNAMIC-CONFIG-FIELD-INVALID", 422, field);
    private static ReferenceDataException NotFound() => new("REF-DYNAMIC-CONFIG-NOT-FOUND", 404);
    private static ReferenceDataException Denied() => new("ID_PERMISSION_DENIED", 403);
    private static void RequireDisable(ReferenceDataPermissionDecision? decision)
    {
        if (decision?.Allowed == true) return;
        throw decision?.Reason switch
        {
            ReferenceDataPermissionDenialReason.SessionInvalid => new ReferenceDataException("401", 401),
            ReferenceDataPermissionDenialReason.SecurityStoreUnavailable => new ReferenceDataException("ID_AUTH_SECURITY_STORE_UNAVAILABLE", 503),
            _ => Denied(),
        };
    }
}
