using System.Reflection;
using System.Text.Json;
using IndustrialPlatform.EventBus.Abstractions;
using IndustrialPlatform.EventBus.Connection;
using IndustrialPlatform.EventBus.Consumer;
using IndustrialPlatform.EventBus.Events;
using IndustrialPlatform.EventBus.Options;
using IndustrialPlatform.EventBus.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class EventBusConsumerBusyRetryTests
{
    [Fact]
    public async Task Deferred_busy_failure_is_delayed_without_using_business_dlq_budget()
    {
        var channel = DispatchProxy.Create<IChannel, RecordingChannelProxy>();
        var proxy = (RecordingChannelProxy)(object)channel;
        var subscriptions = new EventBusSubscriptionsManager();
        subscriptions.AddSubscription<BusyTestEvent, BusyTestConsumer>();
        using var provider = new ServiceCollection()
            .AddScoped<BusyTestConsumer>()
            .BuildServiceProvider();
        using var service = new EventBusConsumerBackgroundService(
            new NoopRabbitMqConnection(),
            subscriptions,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new RabbitMqOptions()),
            NullLogger<EventBusConsumerBackgroundService>.Instance);
        typeof(EventBusConsumerBackgroundService)
            .GetField("_channel", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, channel);

        var eventBody = JsonSerializer.SerializeToUtf8Bytes(new BusyTestEvent());
        var args = new BasicDeliverEventArgs(
            "consumer",
            1,
            true,
            "industrial.business",
            "busy.test",
            new BasicProperties { Type = nameof(BusyTestEvent) },
            eventBody);
        var method = typeof(EventBusConsumerBackgroundService)
            .GetMethod("OnMessageReceivedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        await (Task)method.Invoke(service, new object?[] { null, args })!;

        Assert.Equal("industrial.business.busy-retry", proxy.PublishedExchange);
        Assert.Equal(1, proxy.PublishCount);
        Assert.Equal(1, proxy.AckCount);
        Assert.Equal(0, proxy.NackCount);
    }

    private sealed class BusyTestEvent : IntegrationEvent
    {
        public override string EventType => nameof(BusyTestEvent);
    }

    private sealed class BusyTestConsumer : IIntegrationEventConsumer<BusyTestEvent>
    {
        public Task HandleAsync(BusyTestEvent integrationEvent, CancellationToken cancellationToken = default) =>
            Task.FromException(new BusyFailureException());
    }

    private sealed class BusyFailureException : Exception, IEventBusDeferredRetryFailure
    {
    }

    private sealed class NoopRabbitMqConnection : IRabbitMqConnection
    {
        public Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<IChannel>(new NotSupportedException());

        public Task<IChannel> CreateChannelAsync(bool publisherConfirmationsEnabled, CancellationToken cancellationToken = default) =>
            Task.FromException<IChannel>(new NotSupportedException());
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance", "CA1852:Seal internal types",
        Justification = "DispatchProxy requires an inheritable proxy type.")]
    private class RecordingChannelProxy : DispatchProxy
    {
        public int AckCount { get; private set; }
        public int NackCount { get; private set; }
        public int PublishCount { get; private set; }
        public string? PublishedExchange { get; private set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability", "CA2012:Use ValueTasks correctly",
            Justification = "DispatchProxy requires the intercepted ValueTask to be returned as object.")]
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(IChannel.BasicPublishAsync):
                    PublishedExchange = args?[0] as string;
                    PublishCount++;
                    return ValueTask.CompletedTask;
                case nameof(IChannel.BasicAckAsync):
                    AckCount++;
                    return ValueTask.CompletedTask;
                case nameof(IChannel.BasicNackAsync):
                    NackCount++;
                    return ValueTask.CompletedTask;
                case nameof(IDisposable.Dispose):
                    return null;
                case nameof(IAsyncDisposable.DisposeAsync):
                    return ValueTask.CompletedTask;
                default:
                    throw new NotSupportedException(targetMethod?.Name);
            }
        }
    }
}
