using IndustrialPlatform.SharedKernel.Entities;
using IndustrialPlatform.SharedKernel.Exceptions;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class EntityTests
{
    [Fact]
    public void NewEntity_HasAllDefaultLifecycleFields()
    {
        var entity = new TestEntity();

        Assert.NotEqual(Guid.Empty, entity.Id);
        Assert.False(entity.IsFrozen);
        Assert.False(entity.IsLocked);
        Assert.False(entity.IsDeleted);
        Assert.False(string.IsNullOrWhiteSpace(entity.EntityType));
        Assert.NotEqual(default, entity.CreatedOn);
        Assert.NotEqual(default, entity.LastUpdatedOn);
        Assert.Equal(entity.CreatedOn, entity.LastUpdatedOn);
        Assert.Equal(0, entity.OptimisticVersion);
        Assert.NotEqual(Guid.Empty, entity.ConcurrencyVersion);
    }

    [Fact]
    public void NewEntity_EntityType_IsFullNameOfDerivedType()
    {
        var entity = new TestEntity();

        Assert.Equal(typeof(TestEntity).FullName, entity.EntityType);
    }

    [Fact]
    public void Entity_WithProvidedId_PreservesId()
    {
        var id = Guid.NewGuid();
        var entity = new TestEntity(id);

        Assert.Equal(id, entity.Id);
    }

    [Fact]
    public void Touch_AdvancesLastUpdatedOptimisticAndConcurrency()
    {
        var entity = new TestEntity();
        var createdOn = entity.CreatedOn;
        var initialConcurrency = entity.ConcurrencyVersion;

        entity.TouchUpdate();

        Assert.Equal(createdOn, entity.CreatedOn);
        Assert.True(entity.LastUpdatedOn >= createdOn);
        Assert.Equal(1, entity.OptimisticVersion);
        Assert.NotEqual(initialConcurrency, entity.ConcurrencyVersion);
        Assert.False(entity.IsFrozen);
        Assert.False(entity.IsLocked);
        Assert.False(entity.IsDeleted);
    }

    [Fact]
    public void Freeze_SetsStateAndIsIdempotent()
    {
        var entity = new TestEntity();

        entity.Freeze();

        Assert.True(entity.IsFrozen);
        Assert.Equal(1, entity.OptimisticVersion);

        var concurrencyAfterFirstFreeze = entity.ConcurrencyVersion;
        var lastUpdatedAfterFirstFreeze = entity.LastUpdatedOn;

        entity.Freeze();

        Assert.True(entity.IsFrozen);
        Assert.Equal(1, entity.OptimisticVersion);
        Assert.Equal(concurrencyAfterFirstFreeze, entity.ConcurrencyVersion);
        Assert.Equal(lastUpdatedAfterFirstFreeze, entity.LastUpdatedOn);
    }

    [Fact]
    public void FreezeThenUnfreeze_TogglesStateAndAdvancesVersion()
    {
        var entity = new TestEntity();

        entity.Freeze();
        entity.Unfreeze();

        Assert.False(entity.IsFrozen);
        Assert.Equal(2, entity.OptimisticVersion);
    }

    [Fact]
    public void Unfreeze_WhenNotFrozen_IsIdempotent()
    {
        var entity = new TestEntity();

        entity.Unfreeze();

        Assert.Equal(0, entity.OptimisticVersion);
        Assert.False(entity.IsFrozen);
    }

    [Fact]
    public void Lock_SetsStateAndIsIdempotent()
    {
        var entity = new TestEntity();

        entity.Lock();

        Assert.True(entity.IsLocked);
        Assert.Equal(1, entity.OptimisticVersion);

        entity.Lock();

        Assert.Equal(1, entity.OptimisticVersion);
    }

    [Fact]
    public void LockThenUnlock_TogglesStateAndAdvancesVersion()
    {
        var entity = new TestEntity();

        entity.Lock();
        entity.Unlock();

        Assert.False(entity.IsLocked);
        Assert.Equal(2, entity.OptimisticVersion);
    }

    [Fact]
    public void Unlock_WhenNotLocked_IsIdempotent()
    {
        var entity = new TestEntity();

        entity.Unlock();

        Assert.Equal(0, entity.OptimisticVersion);
        Assert.False(entity.IsLocked);
    }

    [Fact]
    public void MarkDeleted_SetsStateAndIsIdempotent()
    {
        var entity = new TestEntity();

        entity.MarkDeleted();

        Assert.True(entity.IsDeleted);
        Assert.Equal(1, entity.OptimisticVersion);

        entity.MarkDeleted();

        Assert.True(entity.IsDeleted);
        Assert.Equal(1, entity.OptimisticVersion);
    }

    [Fact]
    public void MarkDeletedThenRestore_TogglesStateAndAdvancesVersion()
    {
        var entity = new TestEntity();

        entity.MarkDeleted();
        entity.Restore();

        Assert.False(entity.IsDeleted);
        Assert.False(entity.IsFrozen);
        Assert.False(entity.IsLocked);
        Assert.Equal(2, entity.OptimisticVersion);
    }

    [Fact]
    public void Restore_WhenNotDeleted_IsIdempotent()
    {
        var entity = new TestEntity();

        entity.Restore();

        Assert.Equal(0, entity.OptimisticVersion);
        Assert.False(entity.IsDeleted);
    }

    [Fact]
    public void Modify_WhenDeleted_ThrowsBusinessException()
    {
        var entity = new TestEntity();
        entity.MarkDeleted();

        var exception = Assert.Throws<BusinessException>(() => entity.Rename("new"));

        Assert.Contains("删除", exception.Message);
        Assert.Contains(typeof(TestEntity).FullName!, exception.Message);
    }

    [Fact]
    public void Modify_WhenLocked_ThrowsBusinessException()
    {
        var entity = new TestEntity();
        entity.Lock();

        var exception = Assert.Throws<BusinessException>(() => entity.Rename("new"));

        Assert.Contains("锁定", exception.Message);
    }

    [Fact]
    public void Modify_WhenFrozen_ThrowsBusinessException()
    {
        var entity = new TestEntity();
        entity.Freeze();

        var exception = Assert.Throws<BusinessException>(() => entity.Rename("new"));

        Assert.Contains("冻结", exception.Message);
    }

    [Theory]
    [InlineData("Freeze")]
    [InlineData("Unfreeze")]
    [InlineData("Lock")]
    [InlineData("Unlock")]
    public void StateOperations_WhenDeleted_ThrowBusinessException(string operation)
    {
        var entity = new TestEntity();
        entity.MarkDeleted();

        Action action = operation switch
        {
            "Freeze" => () => entity.Freeze(),
            "Unfreeze" => () => entity.Unfreeze(),
            "Lock" => () => entity.Lock(),
            "Unlock" => () => entity.Unlock(),
            _ => () => entity.Restore(),
        };

        var exception = Assert.Throws<BusinessException>(action);

        Assert.Contains("删除", exception.Message);
        Assert.Contains(typeof(TestEntity).FullName!, exception.Message);
    }

    [Fact]
    public void Freeze_WhenLocked_ThrowsBusinessException()
    {
        var entity = new TestEntity();
        entity.Lock();

        Assert.Throws<BusinessException>(() => entity.Freeze());
    }

    [Fact]
    public void Unfreeze_WhenLocked_ThrowsBusinessException()
    {
        var entity = new TestEntity();
        entity.Lock();

        Assert.Throws<BusinessException>(() => entity.Unfreeze());
    }

    [Fact]
    public void Lock_WhenFrozen_ThrowsBusinessException()
    {
        var entity = new TestEntity();
        entity.Freeze();

        Assert.Throws<BusinessException>(() => entity.Lock());
    }

    [Fact]
    public void MarkDeleted_WhenLocked_ThrowsBusinessException()
    {
        var entity = new TestEntity();
        entity.Lock();

        Assert.Throws<BusinessException>(() => entity.MarkDeleted());
    }

    [Fact]
    public void MarkDeleted_WhenFrozen_ThrowsBusinessException()
    {
        var entity = new TestEntity();
        entity.Freeze();

        Assert.Throws<BusinessException>(() => entity.MarkDeleted());
    }

    [Fact]
    public void DeletedEntity_AllowsOnlyRestore()
    {
        var entity = new TestEntity();
        entity.MarkDeleted();

        entity.Restore();

        Assert.False(entity.IsDeleted);
        Assert.Equal(2, entity.OptimisticVersion);
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
