using IndustrialPlatform.SharedKernel.Entities;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class EntityTests
{
    [Fact]
    public void NewEntity_AssignsIdAndCreateTime()
    {
        var entity = new TestEntity();

        Assert.NotEqual(Guid.Empty, entity.Id);
        Assert.NotEqual(default, entity.CreateTime);
        Assert.Null(entity.ModifyTime);
        Assert.Equal(0, entity.Version);
    }

    [Fact]
    public void Entity_WithProvidedId_PreservesId()
    {
        var id = Guid.NewGuid();
        var entity = new TestEntity(id);

        Assert.Equal(id, entity.Id);
    }

    [Fact]
    public void Update_TouchesModifyTimeAndIncrementsVersion()
    {
        var entity = new TestEntity();

        entity.Update();

        Assert.NotNull(entity.ModifyTime);
        Assert.Equal(1, entity.Version);
    }

    [Fact]
    public void AggregateRoot_RaiseAndClearDomainEvents()
    {
        var aggregate = new TestAggregateRoot();

        Assert.Empty(aggregate.DomainEvents);

        aggregate.Raise(new TestDomainEvent());
        aggregate.Raise(new TestDomainEvent());

        Assert.Equal(2, aggregate.DomainEvents.Count);

        aggregate.ClearDomainEvents();

        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void TwoEntities_AreNotEqualEvenWithSameId()
    {
        var id = Guid.NewGuid();
        var first = new TestEntity(id);
        var second = new TestEntity(id);

        Assert.NotEqual(first, second);
    }
}
