using System.Text.Json;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Application.Caching;
using IndustrialPlatform.ReferenceData.Application.Dictionary;
using IndustrialPlatform.ReferenceData.Contracts.Parameter;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Domain.Parameter;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.ReferenceData.Application.Parameter;

public sealed partial class ParameterService
{
    private readonly IParameterRepository repository;
    private readonly DictionaryService dictionaries;
    private readonly ILogger<ParameterService> logger;
    private readonly IReferenceDataCache cache;

    public ParameterService(IParameterRepository repository, DictionaryService dictionaries,
        ILogger<ParameterService> logger) : this(repository, dictionaries, logger, NullReferenceDataCache.Instance) { }

    public ParameterService(IParameterRepository repository, DictionaryService dictionaries,
        ILogger<ParameterService> logger, IReferenceDataCache cache)
    {
        this.repository = repository;
        this.dictionaries = dictionaries;
        this.logger = logger;
        this.cache = cache;
    }

    public async Task<(IReadOnlyList<ConfigurationDomainSummaryDto> Items, long Total)> SearchAsync(ReferenceDataActor actor, ParameterQuery query, CancellationToken cancellationToken)
    {
        Page(query.PageIndex, query.PageSize);
        if (query.Keyword?.Length > 200) throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "keyword");
        if (query.ScopeType is not null) { var scope = Named<ReferenceScopeType>(query.ScopeType, "scopeType"); Factory(scope == ReferenceScopeType.Factory); }
        if (query.Status is not null) Named<ConfigurationStatus>(query.Status, "status");
        return await repository.SearchAsync(actor.TenantNId, query, cancellationToken);
    }

    public async Task<ConfigurationDomainDetailDto> GetAsync(ReferenceDataActor actor, Guid id, CancellationToken cancellationToken) => Map(await LoadAsync(actor, id, cancellationToken));

    public async Task<ConfigurationDomainDetailDto> CreateAsync(ReferenceDataActor actor, CreateConfigurationDomainRequest request, CancellationToken cancellationToken)
    {
        var domain = ValidateInput(actor, () =>
        {
            var scope = Named<ReferenceScopeType>(request.ScopeType, "scopeType"); Factory(scope == ReferenceScopeType.Factory);
            Platform(actor, scope);
            ParameterValidation.Reason(request.ChangeReason);
            return new ConfigurationAppDomain(request.NId, request.Name, request.Description, scope, scope == ReferenceScopeType.Platform ? null : actor.TenantNId, request.ScopeId);
        });
        await repository.CreateAsync(domain, History(actor, domain, null, domain.Id, "AppDomain", "Created", request.ChangeReason, "absent", Summary(domain)), cancellationToken);
        await InvalidateDomainAsync(domain.NId, CancellationToken.None);
        return Map(domain);
    }

    public Task<ConfigurationDomainDetailDto> UpdateAsync(ReferenceDataActor actor, Guid id, UpdateConfigurationDomainRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(actor, id, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, request.ChangeReason, null, "AppDomain", "Updated",
            domain =>
            {
                var scope = Named<ReferenceScopeType>(request.ScopeType, "scopeType"); Factory(scope == ReferenceScopeType.Factory);
                if (scope != domain.ScopeType || request.ScopeId is not null) throw new ReferenceDataException("REF-SCOPE-INVALID");
                domain.Update(request.NId, request.Name, request.Description); return domain.Id;
            }, cancellationToken);

    public Task<ConfigurationDomainDetailDto> SetStatusAsync(ReferenceDataActor actor, Guid id, bool enabled, ConfigurationDomainStateRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(actor, id, request.ExpectedAppDomainOptimisticVersion, request.ExpectedAppDomainConcurrencyVersion, request.ChangeReason, null, "AppDomain", enabled ? "Enabled" : "Disabled",
            domain => { domain.SetStatus(enabled ? ConfigurationStatus.Active : ConfigurationStatus.Disabled); return domain.Id; }, cancellationToken);

    public Task<ConfigurationDomainDetailDto> AddKeyAsync(ReferenceDataActor actor, Guid id, ConfigurationKeyRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(actor, id, request.ExpectedAppDomainOptimisticVersion, request.ExpectedAppDomainConcurrencyVersion, request.ChangeReason, null, "Key", "Created",
            domain => domain.AddKey(request.NId, Settings(request), InitialValues(request)).Id, cancellationToken);

    public Task<ConfigurationDomainDetailDto> UpdateKeyAsync(ReferenceDataActor actor, Guid id, Guid keyId, ConfigurationKeyRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(actor, id, request.ExpectedAppDomainOptimisticVersion, request.ExpectedAppDomainConcurrencyVersion, request.ChangeReason, keyId, "Key", "Updated", domain =>
        {
            if (request.InitialValues is { Count: > 0 }) throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "initialValues");
            if (request.Status == "Disabled" && domain.Key(keyId).Settings.Status == ConfigurationStatus.Active) EnsureDisablePermission(actor);
            domain.UpdateKey(keyId, request.NId, Settings(request)); return keyId;
        }, cancellationToken);

    public Task<ConfigurationDomainDetailDto> SetKeyStatusAsync(ReferenceDataActor actor, Guid id, Guid keyId, bool enabled, ConfigurationChildStateRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(actor, id, request.ExpectedAppDomainOptimisticVersion, request.ExpectedAppDomainConcurrencyVersion, request.ChangeReason, keyId, "Key", enabled ? "Enabled" : "Disabled",
            domain => { domain.SetKeyStatus(keyId, enabled ? ConfigurationStatus.Active : ConfigurationStatus.Disabled); return keyId; }, cancellationToken);

    public Task<ConfigurationDomainDetailDto> AddValueAsync(ReferenceDataActor actor, Guid id, Guid keyId, ConfigurationValueRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(actor, id, request.ExpectedAppDomainOptimisticVersion, request.ExpectedAppDomainConcurrencyVersion, request.ChangeReason, keyId, "MultiValue", "Created",
            domain => domain.AddValue(keyId, request.NId, ValueSettings(domain.Key(keyId).Settings.DataType, request.Name, request.Value, request.Sort, request.IsDefault, request.Enabled)).Id, cancellationToken);

    public Task<ConfigurationDomainDetailDto> UpdateValueAsync(ReferenceDataActor actor, Guid id, Guid keyId, Guid valueId, ConfigurationValueRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(actor, id, request.ExpectedAppDomainOptimisticVersion, request.ExpectedAppDomainConcurrencyVersion, request.ChangeReason, keyId, "MultiValue", "Updated", domain =>
        {
            if (!request.Enabled && domain.Key(keyId).Value(valueId).Settings.Enabled) EnsureDisablePermission(actor);
            domain.UpdateValue(keyId, valueId, request.NId, ValueSettings(domain.Key(keyId).Settings.DataType, request.Name, request.Value, request.Sort, request.IsDefault, request.Enabled)); return valueId;
        }, cancellationToken);

    public Task<ConfigurationDomainDetailDto> SetValueEnabledAsync(ReferenceDataActor actor, Guid id, Guid keyId, Guid valueId, bool enabled, ConfigurationChildStateRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(actor, id, request.ExpectedAppDomainOptimisticVersion, request.ExpectedAppDomainConcurrencyVersion, request.ChangeReason, keyId, "MultiValue", enabled ? "Enabled" : "Disabled",
            domain => { domain.SetValueEnabled(keyId, valueId, enabled); return valueId; }, cancellationToken);

    public Task<ConfigurationDomainDetailDto> DeleteValueAsync(ReferenceDataActor actor, Guid id, Guid keyId, Guid valueId, ConfigurationChildStateRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(actor, id, request.ExpectedAppDomainOptimisticVersion, request.ExpectedAppDomainConcurrencyVersion, request.ChangeReason, keyId, "MultiValue", "Deleted",
            domain => { domain.RemoveValue(keyId, valueId); return valueId; }, cancellationToken);

    public async Task<(IReadOnlyList<ConfigurationHistoryDto> Items, long Total)> HistoryAsync(ReferenceDataActor actor, Guid id, Guid? keyId, int pageIndex, int pageSize, CancellationToken cancellationToken)
    {
        Page(pageIndex, pageSize);
        var domain = await LoadAsync(actor, id, cancellationToken);
        if (keyId is not null) domain.Key(keyId.Value);
        return await repository.HistoryAsync(id, keyId, pageIndex, pageSize, cancellationToken);
    }

    public async Task<EffectiveConfigurationDto> ResolveAsync(ReferenceDataActor actor, string appDomainNId, string keyNId, string? factoryId, CancellationToken cancellationToken)
    {
        Factory(factoryId is not null);
        appDomainNId = ParameterValidation.NId(appDomainNId);
        keyNId = ParameterValidation.NId(keyNId);
        var key = ReferenceDataCacheKeys.Configuration(ReferenceDataCacheKeys.TenantKey(actor.TenantNId),
            factoryId ?? "_", appDomainNId, keyNId);
        return await cache.GetOrLoadAsync(key, ReferenceDataCacheTtl.Parameter, async () =>
        {
            var scopes = await repository.GetActiveScopesAsync(actor.TenantNId, appDomainNId, cancellationToken);
            if (scopes.Count == 0) throw new ReferenceDataException("REF-CONFIG-DOMAIN-NOT-FOUND", 404);
            return Resolve(scopes, keyNId) ?? throw new ReferenceDataException("REF-CONFIG-KEY-NOT-FOUND", 404);
        }, cancellationToken);
    }

    public async Task<EffectiveConfigurationDomainDto> ResolveDomainAsync(ReferenceDataActor actor, string appDomainNId, string? factoryId, CancellationToken cancellationToken)
    {
        Factory(factoryId is not null);
        appDomainNId = ParameterValidation.NId(appDomainNId);
        var key = ReferenceDataCacheKeys.ConfigurationDomain(ReferenceDataCacheKeys.TenantKey(actor.TenantNId),
            factoryId ?? "_", appDomainNId, "current");
        return await cache.GetOrLoadAsync(key, ReferenceDataCacheTtl.Parameter, async () =>
        {
            var scopes = await repository.GetActiveScopesAsync(actor.TenantNId, appDomainNId, cancellationToken);
            if (scopes.Count == 0) throw new ReferenceDataException("REF-CONFIG-DOMAIN-NOT-FOUND", 404);
            var names = scopes.SelectMany(domain => domain.Keys)
                .Where(item => !item.IsDeleted && item.Settings.Status == ConfigurationStatus.Active)
                .Select(item => item.NId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (names.Length > 500) throw new ReferenceDataException("REF-CONFIG-KEY-LIMIT", 409);
            return new EffectiveConfigurationDomainDto(appDomainNId,
                names.Select(name => Resolve(scopes, name)!).ToArray());
        }, cancellationToken);
    }

    private static EffectiveConfigurationDto? Resolve(IReadOnlyList<ConfigurationAppDomain> scopes, string keyNId)
    {
        foreach (var domain in scopes.OrderByDescending(item => item.ScopeType == ReferenceScopeType.Tenant))
        {
            if (domain.Status != ConfigurationStatus.Active || domain.IsDeleted) continue;
            var key = domain.Keys.FirstOrDefault(item => item.NId == keyNId && !item.IsDeleted && item.Settings.Status == ConfigurationStatus.Active);
            if (key is null) continue;
            key.CheckMandatory();
            var settings = key.Settings;
            var single = settings.ValueMode == ConfigurationValueMode.Single;
            var value = single ? key.ResolveSingle() : null;
            var values = single ? [] : key.EnabledValues().Select(MapValue).ToArray();
            return new(domain.NId, key.NId, $"{domain.NId}.{key.NId}", settings.DataType.ToString(), settings.ValueMode.ToString(),
                value?.JsonValue, value?.CanonicalValue, values, single && settings.Value is null && settings.DefaultValue is not null,
                single ? value is null : values.Length == 0, domain.ScopeType.ToString(), domain.TenantNId, domain.Revision, domain.LastUpdatedOn);
        }
        return null;
    }

    private async Task<ConfigurationDomainDetailDto> ChangeAsync(ReferenceDataActor actor, Guid id, long version, Guid token, string reason,
        Guid? keyId, string objectType, string changeType, Func<ConfigurationAppDomain, Guid> mutate, CancellationToken cancellationToken)
    {
        var domain = await LoadAsync(actor, id, cancellationToken); Platform(actor, domain.ScopeType); domain.CheckVersion(version, token);
        var before = objectType == "Key" && changeType == "Created" ? "absent" : Summary(domain, keyId);
        var objectId = ValidateInput(actor, () => { ParameterValidation.Reason(reason); return mutate(domain); });
        if (domain.OptimisticVersion == version) return Map(domain);
        if (objectType == "Key") keyId = objectId;
        if (keyId is not null && changeType != "Disabled") await CheckEnumAsync(domain.ScopeType == ReferenceScopeType.Platform ? actor with { TenantNId = string.Empty } : actor, domain.Key(keyId.Value), cancellationToken);
        var history = History(actor, domain, keyId, objectId, objectType, changeType, reason, before, Summary(domain, keyId));
        await repository.SaveAsync(domain, version, token, history, cancellationToken);
        await InvalidateDomainAsync(domain.NId, CancellationToken.None);
        return Map(domain);
    }

    private Task InvalidateDomainAsync(string appDomainNId, CancellationToken cancellationToken) =>
        cache.InvalidateAsync(ReferenceDataCacheKeys.ConfigurationDomainEntries(appDomainNId), cancellationToken);

    private async Task CheckEnumAsync(ReferenceDataActor actor, ConfigurationKey key, CancellationToken cancellationToken)
    {
        if (key.Settings.DataType != ReferenceDataType.Enum) return;
        var dictionary = await dictionaries.GetEffectiveAsync(actor, key.Settings.DictionaryNId!, null, cancellationToken);
        var allowed = dictionary.Items.Where(item => item.Enabled).Select(item => item.NId).ToHashSet(StringComparer.Ordinal);
        var values = key.MultiValues.Where(item => !item.IsDeleted).Select(item => item.Settings.Value)
            .Concat(new[] { key.Settings.Value, key.Settings.DefaultValue }.OfType<ConfigurationScalar>());
        if (values.Any(value => !allowed.Contains(value.JsonValue.GetString()!))) throw new ReferenceDataException("REF-CONFIG-ENUM-VALUE-INVALID", field: "value");
    }

    private T ValidateInput<T>(ReferenceDataActor actor, Func<T> action)
    {
        try { return action(); }
        catch (ReferenceDataException error) when (error.ErrorCode == "REF-CONFIG-SENSITIVE-REJECTED")
        {
            SensitiveRejected(logger, actor.UserNId, actor.TenantNId, actor.TraceId);
            throw;
        }
    }

    private async Task<ConfigurationAppDomain> LoadAsync(ReferenceDataActor actor, Guid id, CancellationToken cancellationToken) =>
        await repository.GetAsync(actor.TenantNId, id, cancellationToken) ?? throw new ReferenceDataException("REF-CONFIG-DOMAIN-NOT-FOUND", 404);
    private static void Platform(ReferenceDataActor actor, ReferenceScopeType scope)
    {
        if (scope == ReferenceScopeType.Platform && !actor.CanManagePlatform) throw new ReferenceDataException("ID_PERMISSION_DENIED", 403);
    }
    private static void Factory(bool factory) { if (factory) throw new ReferenceDataException("REF-SCOPE-FACTORY-NOT-READY", 409); }
    private static void EnsureDisablePermission(ReferenceDataActor actor)
    {
        if (actor.ParameterDisableDecision?.Allowed == true) return;
        if (actor.ParameterDisableDecision?.Reason == ReferenceDataPermissionDenialReason.SecurityStoreUnavailable)
            throw new ReferenceDataException("ID_AUTH_SECURITY_STORE_UNAVAILABLE", 503);
        if (actor.ParameterDisableDecision?.Reason == ReferenceDataPermissionDenialReason.SessionInvalid) throw new ReferenceDataException("401", 401);
        throw new ReferenceDataException("ID_PERMISSION_DENIED", 403);
    }
    private static void Page(int index, int size) { if (index < 1 || size is < 1 or > 100) throw new ReferenceDataException("REF-VALIDATION-FAILED"); }
    private static T Named<T>(string value, string field) where T : struct, Enum => Enum.GetNames<T>().Contains(value, StringComparer.Ordinal)
        ? Enum.Parse<T>(value) : throw new ReferenceDataException("REF-VALIDATION-FAILED", field: field);
    private static ConfigurationScalar? Scalar(ReferenceDataType type, JsonElement? value, string field)
    {
        try { return ConfigurationScalar.Optional(type, value); }
        catch (ReferenceDataException error) { throw new ReferenceDataException(error.ErrorCode, error.Status, field); }
    }
    private static ConfigurationKeySettings Settings(ConfigurationKeyRequest request)
    {
        var type = Named<ReferenceDataType>(request.DataType, "dataType");
        return new(request.Name, request.Description, type, Named<ConfigurationValueMode>(request.ValueMode, "valueMode"),
            Scalar(type, request.Value, "value"), Scalar(type, request.DefaultValue, "defaultValue"), request.IsMandatory, request.IsReadOnly,
            request.DictionaryNId, request.ReferenceTarget, Named<ConfigurationStatus>(request.Status, "status"), request.Sort);
    }
    private static List<(string NId, ConfigurationValueSettings Settings)> InitialValues(ConfigurationKeyRequest request)
    {
        if (request.InitialValues is null) return [];
        if (request.InitialValues.Count > 1000) throw new ReferenceDataException("REF-CONFIG-MULTI-VALUE-LIMIT", 422);
        var result = new List<(string, ConfigurationValueSettings)>();
        for (var index = 0; index < request.InitialValues.Count; index++)
        {
            var item = request.InitialValues[index];
            if (item is null) throw new ReferenceDataException("REF-VALIDATION-FAILED", field: $"initialValues[{index}]");
            try { result.Add((item.NId, ValueSettings(Named<ReferenceDataType>(request.DataType, "dataType"), item.Name, item.Value, item.Sort, item.IsDefault, item.Enabled))); }
            catch (ReferenceDataException error) { throw new ReferenceDataException(error.ErrorCode, error.Status, $"initialValues[{index}].{error.Field ?? "value"}"); }
        }
        return result;
    }
    private static ConfigurationValueSettings ValueSettings(ReferenceDataType type, string? name, JsonElement value, int sort, bool isDefault, bool enabled) =>
        new(name, ConfigurationScalar.Parse(type, value), sort, isDefault, enabled);
    [LoggerMessage(3101, LogLevel.Warning, "ReferenceData.Parameter sensitive configuration rejected. UserNId={UserNId} TenantNId={TenantNId} TraceId={TraceId}")]
    private static partial void SensitiveRejected(ILogger logger, string userNId, string tenantNId, string traceId);
    private static string Summary(ConfigurationAppDomain domain, Guid? keyId = null)
    {
        if (keyId is null) return JsonSerializer.Serialize(new { status = domain.Status.ToString(), keyCount = domain.Keys.Count });
        var key = domain.Key(keyId.Value); var settings = key.Settings;
        return JsonSerializer.Serialize(new
        {
            status = settings.Status.ToString(),
            dataType = settings.DataType.ToString(),
            valueMode = settings.ValueMode.ToString(),
            settings.IsMandatory,
            settings.IsReadOnly,
            hasValue = settings.Value is not null,
            hasDefaultValue = settings.DefaultValue is not null,
            valueHash = settings.Value?.CanonicalValueHash,
            defaultValueHash = settings.DefaultValue?.CanonicalValueHash,
            valueCount = key.MultiValues.Count(item => !item.IsDeleted),
            enabledValueCount = key.EnabledValues().Count,
            multiValues = key.MultiValues.Where(item => !item.IsDeleted).OrderBy(item => item.NId, StringComparer.Ordinal).Select(item => new { item.NId, item.Settings.Enabled, item.Settings.IsDefault, item.Settings.Sort, valueHash = item.Settings.Value.CanonicalValueHash })
        });
    }
    private static ConfigurationHistoryDto History(ReferenceDataActor actor, ConfigurationAppDomain domain, Guid? keyId, Guid objectId,
        string objectType, string changeType, string reason, string before, string after) => new(Guid.NewGuid(), domain.Id, keyId, objectType, objectId,
            keyId is null ? domain.NId : $"{domain.NId}.{domain.Key(keyId.Value).NId}", changeType, ParameterValidation.Reason(reason), before, after,
            domain.Revision, actor.UserNId, actor.TraceId, DateTimeOffset.UtcNow);
    private static ConfigurationMultiValueDto MapValue(ConfigurationKeyMultiValue value) => new(value.Id, value.NId, value.Settings.Name,
        value.Settings.Value.JsonValue, value.Settings.Value.CanonicalValue, value.Settings.Sort, value.Settings.IsDefault, value.Settings.Enabled);
    private static ConfigurationDomainDetailDto Map(ConfigurationAppDomain domain) => new(domain.Id, domain.NId, domain.Name, domain.Description,
        domain.ScopeType.ToString(), domain.TenantNId, domain.Status.ToString(), domain.Revision, domain.Keys.OrderBy(key => key.Settings.Sort).ThenBy(key => key.NId, StringComparer.Ordinal).Select(key =>
            new ConfigurationKeyDto(key.Id, key.NId, $"{domain.NId}.{key.NId}", key.Settings.Name, key.Settings.Description,
                key.Settings.DataType.ToString(), key.Settings.ValueMode.ToString(), key.Settings.Value?.JsonValue, key.Settings.DefaultValue?.JsonValue,
                key.Settings.Value?.CanonicalValue, key.Settings.DefaultValue?.CanonicalValue, key.Settings.IsMandatory, key.Settings.IsReadOnly,
                key.Settings.DictionaryNId, key.Settings.ReferenceTarget, key.Settings.Status.ToString(), key.Settings.Sort,
                key.MultiValues.Where(item => !item.IsDeleted).OrderBy(item => item.Settings.Sort).ThenBy(item => item.NId, StringComparer.Ordinal).Select(MapValue).ToArray(), key.HasHadValue, key.IsFrozen, key.IsLocked)).ToArray(),
        domain.LastUpdatedOn, domain.OptimisticVersion, domain.ConcurrencyVersion, domain.IsFrozen, domain.IsLocked);
}
