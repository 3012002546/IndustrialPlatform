using IndustrialPlatform.EventBus.Subscriptions;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class EventBusSubscriptionsManagerTests
{
    private readonly EventBusSubscriptionsManager _manager = new();

    [Fact]
    public void IsEmpty_Initially_ReturnsTrue()
    {
        Assert.True(_manager.IsEmpty);
    }

    [Fact]
    public void AddSubscription_ThenIsEmpty_ReturnsFalse()
    {
        _manager.AddSubscription<TestIntegrationEvent, TestIntegrationEventConsumer>();

        Assert.False(_manager.IsEmpty);
    }

    [Fact]
    public void AddSubscription_RegistersEventTypeByName()
    {
        _manager.AddSubscription<TestIntegrationEvent, TestIntegrationEventConsumer>();

        Assert.True(_manager.HasSubscriptionsForEvent<TestIntegrationEvent>());
        Assert.True(_manager.HasSubscriptionsForEvent(nameof(TestIntegrationEvent)));
        Assert.Equal(typeof(TestIntegrationEvent), _manager.GetEventTypeByName(nameof(TestIntegrationEvent)));
    }

    [Fact]
    public void AddSubscription_RegistersConsumerType()
    {
        _manager.AddSubscription<TestIntegrationEvent, TestIntegrationEventConsumer>();

        Assert.Equal([typeof(TestIntegrationEventConsumer)], _manager.GetConsumersForEvent<TestIntegrationEvent>());
        Assert.Equal([typeof(TestIntegrationEventConsumer)], _manager.GetConsumersForEvent(nameof(TestIntegrationEvent)));
    }

    [Fact]
    public void AddSubscription_SameConsumerTwice_RegistersOnce()
    {
        _manager.AddSubscription<TestIntegrationEvent, TestIntegrationEventConsumer>();
        _manager.AddSubscription<TestIntegrationEvent, TestIntegrationEventConsumer>();

        Assert.Single(_manager.GetConsumersForEvent<TestIntegrationEvent>());
    }

    [Fact]
    public void UnknownEvent_HasNoSubscriptionsAndNoConsumers()
    {
        Assert.False(_manager.HasSubscriptionsForEvent<TestIntegrationEvent>());
        Assert.Empty(_manager.GetConsumersForEvent<TestIntegrationEvent>());
    }
}
