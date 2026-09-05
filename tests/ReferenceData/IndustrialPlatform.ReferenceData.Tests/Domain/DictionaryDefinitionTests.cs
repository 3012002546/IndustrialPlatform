using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Domain.Dictionary;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.ReferenceData.Tests.Domain;

public sealed class DictionaryDefinitionTests
{
    [Fact]
    public void Publish_requires_enabled_items_and_published_content_stays_immutable_after_unfreeze()
    {
        var dictionary = Create();
        Assert.Equal("REF-VALIDATION-FAILED", Assert.Throws<ReferenceDataException>(() => dictionary.Publish("ADMIN")).ErrorCode);
        dictionary.Update("Kinds", null, [new DictionaryItem("ok", "Okay", null, 0, true)]);
        dictionary.Publish("ADMIN");
        dictionary.Unfreeze();
        Assert.Equal("REF-INVALID-STATE", Assert.Throws<ReferenceDataException>(() => dictionary.Update("Changed", null, [])).ErrorCode);
        Assert.Equal(PublicationStatus.Published, dictionary.Status);
        Assert.Equal("KINDS", dictionary.NId);
    }

    [Fact]
    public void Clone_is_complete_draft_with_new_entity_identity_and_fixed_old_revision()
    {
        var original = Create();
        original.Update("Kinds", null, [new DictionaryItem("ok", "Okay", null, 0, true)]);
        original.Publish("ADMIN");
        var clone = original.Clone(2);
        Assert.NotEqual(original.Id, clone.Id);
        Assert.Equal(PublicationStatus.Draft, clone.Status);
        Assert.Equal(2, clone.Revision);
        Assert.Equal(1, original.Revision);
        Assert.Null(clone.PublishedOn);
        Assert.Equal("OK", Assert.Single(clone.Items).NId);
        Assert.NotEqual(Assert.Single(original.Items).Id, clone.Items[0].Id);
    }

    [Fact]
    public void Duplicate_item_nids_are_rejected_without_changing_aggregate()
    {
        var dictionary = Create();
        var token = dictionary.ConcurrencyVersion;
        Assert.Equal("REF-DICT-DUPLICATE-NID", Assert.Throws<ReferenceDataException>(() =>
            dictionary.Update("Kinds", null, [new("ok", "One", null, 0, true), new("OK", "Two", null, 1, true)])).ErrorCode);
        Assert.Empty(dictionary.Items);
        Assert.Equal(token, dictionary.ConcurrencyVersion);
    }

    [Fact]
    public void Frozen_root_and_wrong_concurrency_token_block_changes()
    {
        var dictionary = Create();
        dictionary.Freeze();
        Assert.Throws<BusinessException>(() => dictionary.Update("Changed", null, []));
        Assert.Equal("REF-CONCURRENCY-CONFLICT", Assert.Throws<ReferenceDataException>(() => dictionary.CheckVersion(0, Guid.NewGuid())).ErrorCode);
    }

    [Theory]
    [InlineData(ReferenceScopeType.Factory, "T", "F", "REF-SCOPE-FACTORY-NOT-READY")]
    [InlineData(ReferenceScopeType.Platform, "T", null, "REF-SCOPE-INVALID")]
    [InlineData(ReferenceScopeType.Tenant, null, null, "REF-SCOPE-INVALID")]
    public void Invalid_scopes_are_rejected(ReferenceScopeType scope, string? tenant, string? factory, string code)
    {
        Assert.Equal(code, Assert.Throws<ReferenceDataException>(() => new DictionaryDefinition("Kinds", "Kinds", null, scope, tenant, factory)).ErrorCode);
    }

    private static DictionaryDefinition Create() => new("Kinds", "Kinds", null, ReferenceScopeType.Tenant, "TENANT-A", null);
}
