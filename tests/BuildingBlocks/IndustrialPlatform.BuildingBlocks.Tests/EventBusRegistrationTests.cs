using IndustrialPlatform.EventBus.Abstractions;
using IndustrialPlatform.EventBus.Connection;
using IndustrialPlatform.EventBus.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class EventBusRegistrationTests
{
    [Fact]
    public void AddEventBus_RegistersCoreServices()
    {
        var services = new ServiceCollection();
        services.AddEventBus(options => { });

        Assert.Contains(services, service => service.ServiceType == typeof(IEventBusSubscriptionsManager));
        Assert.Contains(services, service => service.ServiceType == typeof(IRabbitMqConnection));
        Assert.Contains(services, service => service.ServiceType == typeof(IEventBus));
        Assert.Contains(services, service => service.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void AddEventBus_FromConfiguration_BindsRabbitMqOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMQ:Host"] = "rabbit.example.com",
                ["RabbitMQ:Port"] = "5673",
                ["RabbitMQ:ExchangeName"] = "industrial.orders",
                ["RabbitMQ:QueueName"] = "orders.events",
                ["RabbitMQ:PrefetchCount"] = "20",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddEventBus(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value;

        Assert.Equal("rabbit.example.com", options.Host);
        Assert.Equal(5673, options.Port);
        Assert.Equal("industrial.orders", options.ExchangeName);
        Assert.Equal("orders.events", options.QueueName);
        Assert.Equal(20, options.PrefetchCount);
    }

    [Fact]
    public void AddIntegrationEventConsumer_RegistersConsumerAndSubscription()
    {
        var services = new ServiceCollection();
        services.AddIntegrationEventConsumer<TestIntegrationEvent, TestIntegrationEventConsumer>();
        services.AddEventBus(options => { });

        Assert.Contains(services, service =>
            service.ServiceType == typeof(TestIntegrationEventConsumer)
            && service.Lifetime == ServiceLifetime.Scoped);

        using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IEventBusSubscriptionsManager>();

        Assert.True(manager.HasSubscriptionsForEvent<TestIntegrationEvent>());
        Assert.Equal([typeof(TestIntegrationEventConsumer)], manager.GetConsumersForEvent<TestIntegrationEvent>());
    }
}
