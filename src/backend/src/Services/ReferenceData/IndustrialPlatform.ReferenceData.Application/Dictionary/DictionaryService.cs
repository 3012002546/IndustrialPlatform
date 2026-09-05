using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Application.Caching;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.Dictionary;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Domain.Dictionary;

namespace IndustrialPlatform.ReferenceData.Application.Dictionary;

public sealed class DictionaryService
{
    private readonly IDictionaryRepository repository;
    private readonly IReferenceDataCache cache;

    public DictionaryService(IDictionaryRepository repository) :
        this(repository, NullReferenceDataCache.Instance)
    { }

    public DictionaryService(IDictionaryRepository repository, IReferenceDataCache cache)
    {
        this.repository = repository;
        this.cache = cache;
    }

    public async Task<(IReadOnlyList<DictionarySummaryDto> Items, long Total)> SearchAsync(ReferenceDataActor actor, DictionaryQuery query, CancellationToken cancellationToken)
    {
        if (query.PageIndex < 1 || query.PageSize is < 1 or > 100 || query.Keyword?.Length > 200)
            throw new ReferenceDataException("REF-VALIDATION-FAILED");
        if (query.ScopeType is not null && !Enum.GetNames<ReferenceScopeType>().Contains(query.ScopeType, StringComparer.Ordinal))
            throw new ReferenceDataException("REF-SCOPE-INVALID");
        if (query.ScopeType == "Factory") throw new ReferenceDataException("REF-SCOPE-FACTORY-NOT-READY", 409);
        if (query.Status is not null && !Enum.GetNames<PublicationStatus>().Contains(query.Status, StringComparer.Ordinal))
            throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "status");
        return await repository.SearchAsync(actor.TenantNId, query, cancellationToken);
    }

    public async Task<DictionaryDetailDto> GetAsync(ReferenceDataActor actor, Guid id, CancellationToken cancellationToken) =>
        Map(await LoadAsync(actor, id, cancellationToken));

    public async Task<DictionaryDetailDto> CreateAsync(ReferenceDataActor actor, CreateDictionaryRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.GetNames<ReferenceScopeType>().Contains(request.ScopeType, StringComparer.Ordinal)) throw new ReferenceDataException("REF-SCOPE-INVALID");
        var scope = Enum.Parse<ReferenceScopeType>(request.ScopeType);
        if (scope == ReferenceScopeType.Platform && !actor.CanManagePlatform) throw new ReferenceDataException("ID_PERMISSION_DENIED", 403);
        var definition = new DictionaryDefinition(request.NId, request.Name, request.Description, scope,
            scope == ReferenceScopeType.Platform ? null : actor.TenantNId, request.ScopeId);
        definition.Update(request.Name, request.Description, Items(request.Items));
        await repository.CreateAsync(definition, null, cancellationToken);
        return Map(definition);
    }

    public async Task<DictionaryDetailDto> UpdateAsync(ReferenceDataActor actor, Guid id, UpdateDictionaryRequest request, CancellationToken cancellationToken)
    {
        var definition = await LoadWritableAsync(actor, id, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, cancellationToken);
        definition.Update(request.Name, request.Description, Items(request.Items));
        await repository.SaveAsync([new(definition, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion)], cancellationToken);
        await cache.InvalidateAsync(ReferenceDataCacheKeys.Dictionaries(definition.NId), CancellationToken.None);
        return Map(definition);
    }

    public async Task<DictionaryDetailDto> CloneAsync(ReferenceDataActor actor, Guid id, PublishOrDisableRequest request, CancellationToken cancellationToken)
    {
        var definition = await LoadWritableAsync(actor, id, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, cancellationToken);
        var clone = definition.Clone(await repository.GetNextRevisionAsync(definition, cancellationToken));
        await repository.CreateAsync(clone,
            new(definition, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion), cancellationToken);
        return Map(clone);
    }

    public async Task<DictionaryDetailDto> PublishAsync(ReferenceDataActor actor, Guid id, PublishOrDisableRequest request, CancellationToken cancellationToken)
    {
        var definition = await LoadWritableAsync(actor, id, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, cancellationToken);
        var check = CheckPublication(definition, await repository.GetLastPublishedAsync(definition, cancellationToken));
        if (check.Errors.Count > 0) throw new ReferenceDataException(check.Errors[0].Code, 409, check.Errors[0].Field);
        var previous = await repository.GetPublishedInScopeAsync(definition, cancellationToken);
        var changes = new List<DictionaryChange>();
        if (previous is not null && previous.Id != definition.Id)
        {
            changes.Add(new(previous, previous.OptimisticVersion, previous.ConcurrencyVersion));
            previous.Supersede();
        }
        definition.Publish(actor.UserNId);
        changes.Add(new(definition, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion));
        await repository.SaveAsync(changes, cancellationToken);
        await cache.InvalidateAsync(ReferenceDataCacheKeys.Dictionaries(definition.NId), CancellationToken.None);
        return Map(definition);
    }

    public async Task<DictionaryPublicationCheckDto> CheckPublicationAsync(ReferenceDataActor actor, Guid id, CancellationToken cancellationToken)
    {
        var definition = await LoadAsync(actor, id, cancellationToken);
        return CheckPublication(definition, await repository.GetLastPublishedAsync(definition, cancellationToken));
    }

    private static DictionaryPublicationCheckDto CheckPublication(DictionaryDefinition definition, DictionaryDefinition? previous)
    {
        var errors = new List<DictionaryPublicationIssue>();
        if (definition.Status != PublicationStatus.Draft || definition.IsFrozen || definition.IsLocked)
            errors.Add(new("REF-INVALID-STATE", "status"));
        if (previous is not null && previous.Revision >= definition.Revision)
            errors.Add(new("REF-CONCURRENCY-CONFLICT", "revision"));
        if (!definition.Items.Any(item => item.Enabled)) errors.Add(new("REF-VALIDATION-FAILED", "items"));
        var previousItems = previous?.Items ?? [];
        if (previousItems.Any(old => !definition.Items.Any(item => item.NId == old.NId)))
            errors.Add(new("REF-DICT-HISTORICAL-ITEM-REMOVED", "items"));
        return new(previous?.Revision,
            definition.Items.Where(item => !previousItems.Any(old => old.NId == item.NId)).Select(item => item.NId).ToArray(),
            definition.Items.Where(item => previousItems.Any(old => old.NId == item.NId && (old.Name != item.Name || old.Description != item.Description || old.Sort != item.Sort || old.Enabled != item.Enabled))).Select(item => item.NId).ToArray(),
            definition.Items.Where(item => !item.Enabled && previousItems.Any(old => old.NId == item.NId && old.Enabled)).Select(item => item.NId).ToArray(), errors);
    }

    public async Task<DictionaryDetailDto> DisableAsync(ReferenceDataActor actor, Guid id, PublishOrDisableRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ChangeReason) || request.ChangeReason.Length > 1000)
            throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "changeReason");
        var definition = await LoadWritableAsync(actor, id, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, cancellationToken);
        definition.Disable();
        await repository.SaveAsync([new(definition, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion)], cancellationToken);
        await cache.InvalidateAsync(ReferenceDataCacheKeys.Dictionaries(definition.NId), CancellationToken.None);
        return Map(definition);
    }

    public async Task<EffectiveDictionaryDto> GetEffectiveAsync(ReferenceDataActor actor, string nId, string? factoryId, CancellationToken cancellationToken)
    {
        if (factoryId is not null) throw new ReferenceDataException("REF-SCOPE-FACTORY-NOT-READY", 409);
        var normalizedNId = ReferenceValidation.NId(nId);
        var key = ReferenceDataCacheKeys.Dictionary(ReferenceDataCacheKeys.TenantKey(actor.TenantNId), normalizedNId);
        return await cache.GetOrLoadAsync(key, ReferenceDataCacheTtl.Dictionary, async () =>
        {
            var definition = await repository.GetEffectiveAsync(actor.TenantNId, normalizedNId, cancellationToken)
                ?? throw new ReferenceDataException("REF-DICT-NOT-FOUND", 404);
            return new EffectiveDictionaryDto(definition.NId, definition.Name,
                definition.Items.Where(item => item.Enabled).Select(MapItem).ToArray(),
                definition.ScopeType.ToString(), definition.TenantNId, definition.Revision,
                definition.PublishedOn!.Value);
        }, cancellationToken);
    }

    private async Task<DictionaryDefinition> LoadAsync(ReferenceDataActor actor, Guid id, CancellationToken cancellationToken) =>
        await repository.GetAsync(actor.TenantNId, id, cancellationToken) ?? throw new ReferenceDataException("REF-DICT-NOT-FOUND", 404);

    private async Task<DictionaryDefinition> LoadWritableAsync(ReferenceDataActor actor, Guid id, long version, Guid token, CancellationToken cancellationToken)
    {
        var definition = await LoadAsync(actor, id, cancellationToken);
        if (definition.ScopeType == ReferenceScopeType.Platform && !actor.CanManagePlatform) throw new ReferenceDataException("ID_PERMISSION_DENIED", 403);
        definition.CheckVersion(version, token);
        return definition;
    }

    private static DictionaryItem[] Items(IReadOnlyList<DictionaryItemDto> items)
    {
        if (items is null || items.Count > 1000) throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "items");
        var result = new DictionaryItem[items.Count];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item is null) throw new ReferenceDataException("REF-VALIDATION-FAILED", field: $"items[{index}]");
            try
            {
                result[index] = new DictionaryItem(item.NId, item.Name, item.Description, item.Sort, item.Enabled);
                if (!names.Add(result[index].NId)) throw new ReferenceDataException("REF-DICT-DUPLICATE-NID", 409, "nId");
            }
            catch (ReferenceDataException error)
            {
                throw new ReferenceDataException(error.ErrorCode, error.Status, $"items[{index}].{error.Field ?? "nId"}");
            }
        }
        return result;
    }

    private static DictionaryItemDto MapItem(DictionaryItem item) => new(item.NId, item.Name, item.Description, item.Sort, item.Enabled);
    private static DictionaryDetailDto Map(DictionaryDefinition definition) => new(definition.Id, definition.NId, definition.Name, definition.Description,
        definition.ScopeType.ToString(), definition.TenantNId, definition.Revision, definition.Status.ToString(), definition.Items.Select(MapItem).ToArray(),
        definition.OptimisticVersion, definition.ConcurrencyVersion, definition.LastUpdatedOn, definition.PublishedOn, definition.PublishedBy, definition.IsFrozen, definition.IsLocked);
}
