using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.SharedKernel.Entities;

namespace IndustrialPlatform.ReferenceData.Domain.Dictionary;

public sealed class DictionaryDefinition : AggregateRoot
{
    public string NId { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public ReferenceScopeType ScopeType { get; private set; }
    public string? TenantNId { get; private set; }
    public int Revision { get; private set; } = 1;
    public PublicationStatus Status { get; private set; } = PublicationStatus.Draft;
    public DateTimeOffset? PublishedOn { get; private set; }
    public string? PublishedBy { get; private set; }
    public IReadOnlyList<DictionaryItem> Items { get; private set; } = [];

    public DictionaryDefinition(string nId, string name, string? description, ReferenceScopeType scope, string? tenantNId, string? scopeNId)
    {
        ReferenceValidation.Scope(scope, tenantNId, scopeNId);
        NId = ReferenceValidation.NId(nId);
        Name = ReferenceValidation.Name(name);
        Description = ReferenceValidation.Description(description);
        ScopeType = scope;
        TenantNId = tenantNId;
    }

    public void Update(string name, string? description, IReadOnlyList<DictionaryItem> items)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(items);
        var nextName = ReferenceValidation.Name(name);
        var nextDescription = ReferenceValidation.Description(description);
        if (items.Count > 1000) throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "items");
        if (items.Select(item => item.NId).Distinct(StringComparer.Ordinal).Count() != items.Count)
            throw new ReferenceDataException("REF-DICT-DUPLICATE-NID", 409, "items");
        Name = nextName;
        Description = nextDescription;
        Items = items.OrderBy(item => item.Sort).ThenBy(item => item.NId, StringComparer.Ordinal).ToArray();
        Touch();
    }

    public void CheckVersion(long expectedOptimisticVersion, Guid expectedConcurrencyVersion)
    {
        if (OptimisticVersion != expectedOptimisticVersion || ConcurrencyVersion != expectedConcurrencyVersion)
            throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
    }

    public void Publish(string userNId)
    {
        EnsureDraft();
        if (!Items.Any(item => item.Enabled)) throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "items");
        Status = PublicationStatus.Published;
        PublishedOn = DateTimeOffset.UtcNow;
        PublishedBy = userNId;
        Touch();
    }

    public void Supersede()
    {
        EnsureCanModify();
        if (Status != PublicationStatus.Published) throw new ReferenceDataException("REF-INVALID-STATE", 409);
        Status = PublicationStatus.Superseded;
        Touch();
    }

    public void Disable()
    {
        EnsureCanModify();
        if (Status is not (PublicationStatus.Draft or PublicationStatus.Published)) throw new ReferenceDataException("REF-INVALID-STATE", 409);
        Status = PublicationStatus.Disabled;
        Touch();
    }

    public DictionaryDefinition Clone(int revision)
    {
        EnsureCanModify();
        if (PublishedOn is null || revision <= Revision) throw new ReferenceDataException("REF-INVALID-STATE", 409);
        var clone = new DictionaryDefinition(NId, Name, Description, ScopeType, TenantNId, null) { Revision = revision };
        clone.Update(Name, Description, Items.Select(item => new DictionaryItem(item.NId, item.Name, item.Description, item.Sort, item.Enabled)).ToArray());
        return clone;
    }

    private void EnsureDraft()
    {
        EnsureCanModify();
        if (Status != PublicationStatus.Draft) throw new ReferenceDataException("REF-INVALID-STATE", 409);
    }

    public static DictionaryDefinition Restore(DictionaryState state, IReadOnlyList<DictionaryItem> items) => new(state.NId, state.Name, state.Description, state.ScopeType, state.TenantNId, null)
    {
        Id = state.Id, Revision = state.Revision, Status = state.Status, PublishedOn = state.PublishedOn, PublishedBy = state.PublishedBy,
        IsFrozen = state.IsFrozen, IsLocked = state.IsLocked, IsDeleted = state.IsDeleted,
        CreatedOn = state.CreatedOn, LastUpdatedOn = state.LastUpdatedOn, OptimisticVersion = state.OptimisticVersion,
        ConcurrencyVersion = state.ConcurrencyVersion, Items = items,
    };
}

public sealed record DictionaryState(Guid Id, string NId, string Name, string? Description, ReferenceScopeType ScopeType, string? TenantNId,
    int Revision, PublicationStatus Status, DateTimeOffset? PublishedOn, string? PublishedBy, bool IsFrozen, bool IsLocked, bool IsDeleted,
    DateTimeOffset CreatedOn, DateTimeOffset LastUpdatedOn, long OptimisticVersion, Guid ConcurrencyVersion);

public sealed class DictionaryItem : Entity
{
    public string NId { get; }
    public string Name { get; }
    public string? Description { get; }
    public int Sort { get; }
    public bool Enabled { get; }

    public DictionaryItem(string nId, string name, string? description, int sort, bool enabled)
    {
        NId = ReferenceValidation.NId(nId, item: true);
        Name = ReferenceValidation.Name(name);
        Description = ReferenceValidation.Description(description);
        if (sort < 0) throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "sort");
        Sort = sort;
        Enabled = enabled;
    }

    public static DictionaryItem Restore(Guid id, string nId, string name, string? description, int sort, bool enabled,
        DateTimeOffset createdOn, DateTimeOffset lastUpdatedOn, long optimisticVersion, Guid concurrencyVersion) => new(nId, name, description, sort, enabled)
    {
        Id = id, CreatedOn = createdOn, LastUpdatedOn = lastUpdatedOn,
        OptimisticVersion = optimisticVersion, ConcurrencyVersion = concurrencyVersion,
    };
}
