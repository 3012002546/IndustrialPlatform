using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Application.Dictionary;
using IndustrialPlatform.ReferenceData.Application.Metadata;
using IndustrialPlatform.ReferenceData.Application.UnitOfMeasure;
using IndustrialPlatform.ReferenceData.Contracts.Dictionary;
using IndustrialPlatform.ReferenceData.Contracts.Metadata;
using IndustrialPlatform.ReferenceData.Contracts.UnitOfMeasure;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Domain.Dictionary;
using IndustrialPlatform.ReferenceData.Domain.Metadata;
using IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure;

namespace IndustrialPlatform.ReferenceData.Tests;

public sealed class MetadataDependencyFailureTests
{
    private static readonly ReferenceDataActor Actor = new("TENANT-A", "USER-A", false, "metadata-dependency");

    [Fact]
    public async Task Publication_check_preserves_dictionary_and_unit_dependency_503_errors()
    {
        var enumSchema = Schema(new("MODE", Settings(ReferenceDataType.Enum) with
        {
            DictionaryNId = "EquipmentMode",
        }));
        var enumService = Service(enumSchema, new FailingDictionaryRepository(), new UnusedUnitRepository());
        var dictionaryFailure = await Assert.ThrowsAsync<ReferenceDataException>(() =>
            enumService.CheckPublicationAsync(Actor, enumSchema.Id, CancellationToken.None));
        Assert.Equal(503, dictionaryFailure.Status);

        var unitSchema = Schema(new("WEIGHT", Settings(ReferenceDataType.Decimal) with
        {
            UnitDimensionNId = "Mass",
            UnitRevision = 1,
            UnitSourceScope = ReferenceScopeType.Tenant,
            UnitSourceTenantNId = "TENANT-A",
        }));
        var unitService = Service(unitSchema, new UnusedDictionaryRepository(), new FailingUnitRepository());
        var unitFailure = await Assert.ThrowsAsync<ReferenceDataException>(() =>
            unitService.CheckPublicationAsync(Actor, unitSchema.Id, CancellationToken.None));
        Assert.Equal(503, unitFailure.Status);
    }

    private static EntitySchema Schema(MetadataAttributeInput attribute)
    {
        var schema = new EntitySchema("Equipment", "Equipment", null, ReferenceScopeType.Tenant, "TENANT-A", null);
        schema.Update(schema.Name, null, [attribute]);
        return schema;
    }

    private static MetadataAttributeSettings Settings(ReferenceDataType type) => new(
        "Attribute", type, false, false, true, 0, null, null, null, null, null, null, null, null,
        null, null, null, null, null, null, null, null);

    private static MetadataSchemaService Service(EntitySchema schema, IDictionaryRepository dictionaries,
        IUnitDimensionRepository units) => new(new MetadataRepository(schema), new DictionaryService(dictionaries),
        new UnitDimensionService(units));

    private sealed class MetadataRepository(EntitySchema schema) : IMetadataSchemaRepository
    {
        public Task<EntitySchema?> GetAsync(string tenantNId, Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<EntitySchema?>(id == schema.Id ? schema : null);
        public Task<EntitySchema?> GetLastPublishedAsync(EntitySchema value, CancellationToken cancellationToken) =>
            Task.FromResult<EntitySchema?>(null);
        public Task<(IReadOnlyList<MetadataSchemaSummaryDto> Items, long Total)> SearchAsync(string tenantNId,
            MetadataSchemaQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EntitySchema?> GetEffectiveAsync(string tenantNId, string nId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EntitySchema?> GetRevisionAsync(string tenantNId, string nId, string sourceScope, int revision,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EntitySchema?> GetPublishedInScopeAsync(EntitySchema value,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> GetNextRevisionAsync(EntitySchema value,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CreateAsync(EntitySchema value, MetadataSchemaChange? source,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveAsync(IReadOnlyList<MetadataSchemaChange> changes,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FailingDictionaryRepository : DictionaryRepositoryStub
    {
        public override Task<DictionaryDefinition?> GetEffectiveAsync(string tenantNId, string nId,
            CancellationToken cancellationToken) => Task.FromException<DictionaryDefinition?>(Failure());
    }

    private sealed class UnusedDictionaryRepository : DictionaryRepositoryStub { }

    private abstract class DictionaryRepositoryStub : IDictionaryRepository
    {
        public virtual Task<DictionaryDefinition?> GetEffectiveAsync(string tenantNId, string nId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(IReadOnlyList<DictionarySummaryDto> Items, long Total)> SearchAsync(string tenantNId,
            DictionaryQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DictionaryDefinition?> GetAsync(string tenantNId, Guid id,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DictionaryDefinition?> GetPublishedInScopeAsync(DictionaryDefinition definition,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DictionaryDefinition?> GetLastPublishedAsync(DictionaryDefinition definition,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> GetNextRevisionAsync(DictionaryDefinition definition,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CreateAsync(DictionaryDefinition definition, DictionaryChange? source,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveAsync(IReadOnlyList<DictionaryChange> changes,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FailingUnitRepository : UnitRepositoryStub
    {
        public override Task<UnitDimension?> GetRevisionAsync(string tenantNId, string nId, string sourceScope,
            int revision, CancellationToken cancellationToken) => Task.FromException<UnitDimension?>(Failure());
    }

    private sealed class UnusedUnitRepository : UnitRepositoryStub { }

    private abstract class UnitRepositoryStub : IUnitDimensionRepository
    {
        public virtual Task<UnitDimension?> GetRevisionAsync(string tenantNId, string nId, string sourceScope,
            int revision, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(IReadOnlyList<UnitDimensionSummaryDto> Items, long Total)> SearchAsync(string tenantNId,
            UnitDimensionQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(IReadOnlyList<AvailableUnitDimensionDto> Items, long Total)> ListAvailableAsync(string tenantNId,
            AvailableUnitDimensionQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UnitDimension?> GetAsync(string tenantNId, Guid id,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UnitDimension?> GetCurrentAsync(string tenantNId, string nId, string sourceScope,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UnitDimension?> GetPublishedInScopeAsync(UnitDimension dimension,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> GetNextRevisionAsync(UnitDimension dimension,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CreateAsync(UnitDimension dimension, UnitDimensionChange? source,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveAsync(IReadOnlyList<UnitDimensionChange> changes,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static ReferenceDataException Failure() => new("503", 503);
}
