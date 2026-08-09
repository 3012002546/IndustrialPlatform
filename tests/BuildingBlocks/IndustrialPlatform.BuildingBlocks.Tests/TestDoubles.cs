using IndustrialPlatform.EventBus.Abstractions;
using IndustrialPlatform.EventBus.Events;
using IndustrialPlatform.SharedKernel.Entities;
using IndustrialPlatform.SharedKernel.Events;
using IndustrialPlatform.SharedKernel.ValueObjects;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class TestEntity : Entity
{
    public TestEntity()
    {
    }

    public TestEntity(Guid id)
        : base(id)
    {
    }

    public void Update()
        => Touch();
}

public sealed class TestAggregateRoot : AggregateRoot
{
    public void Raise(IDomainEvent domainEvent)
        => AddDomainEvent(domainEvent);
}

public sealed class TestDomainEvent : IDomainEvent
{
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}

public sealed class Money : ValueObject
{
    public Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }
}

public sealed class TestIntegrationEvent : IntegrationEvent
{
}

public sealed class TestIntegrationEventConsumer : IIntegrationEventConsumer<TestIntegrationEvent>
{
    public Task HandleAsync(TestIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
