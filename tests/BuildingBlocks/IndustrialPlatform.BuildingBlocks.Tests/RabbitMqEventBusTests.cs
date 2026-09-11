using System.Reflection;
using IndustrialPlatform.EventBus.Connection;
using IndustrialPlatform.EventBus.Events;
using IndustrialPlatform.EventBus.Options;
using IndustrialPlatform.EventBus.Producer;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class RabbitMqEventBusTests
{
    [Fact]
    public async Task Concurrent_publishes_are_serialized_on_shared_channel()
    {
        var channel = DispatchProxy.Create<IChannel, TestChannelProxy>();
        var proxy = (TestChannelProxy)(object)channel;
        using var bus = new RabbitMqEventBus(
            new TestRabbitMqConnection(channel),
            Options.Create(new RabbitMqOptions()),
            NullLogger<RabbitMqEventBus>.Instance);

        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => bus.PublishAsync(new TestEvent())));

        Assert.Equal(16, proxy.PublishCount);
        Assert.Equal(1, proxy.MaximumConcurrentPublishes);
    }

    private sealed class TestEvent : IntegrationEvent
    {
        public override string EventType => "test.event.v1";
    }

    private sealed class TestRabbitMqConnection(IChannel channel) : IRabbitMqConnection
    {
        public Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(channel);

        public Task<IChannel> CreateChannelAsync(bool publisherConfirmationsEnabled, CancellationToken cancellationToken = default) =>
            Task.FromResult(channel);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance", "CA1852:Seal internal types",
        Justification = "DispatchProxy requires an inheritable proxy type.")]
    private class TestChannelProxy : DispatchProxy
    {
        private int _inFlight;
        private int _maximumConcurrentPublishes;
        private int _publishCount;

        public int PublishCount => _publishCount;
        public int MaximumConcurrentPublishes => _maximumConcurrentPublishes;

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability", "CA2012:Use ValueTasks correctly",
            Justification = "DispatchProxy requires the intercepted ValueTask to be returned as object.")]
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                nameof(IChannel.ExchangeDeclareAsync) => Task.CompletedTask,
                nameof(IChannel.BasicPublishAsync) => PublishAsync(),
                nameof(IDisposable.Dispose) => null,
                nameof(IAsyncDisposable.DisposeAsync) => ValueTask.CompletedTask,
                "get_IsOpen" => true,
                _ => throw new NotSupportedException(targetMethod?.Name),
            };
        }

        private ValueTask PublishAsync()
        {
            var current = Interlocked.Increment(ref _inFlight);
            Interlocked.Increment(ref _publishCount);
            while (current > Volatile.Read(ref _maximumConcurrentPublishes)
                && Interlocked.CompareExchange(ref _maximumConcurrentPublishes, current, Volatile.Read(ref _maximumConcurrentPublishes)) != Volatile.Read(ref _maximumConcurrentPublishes))
            {
            }

            return new ValueTask(CompletePublishAsync());
        }

        private async Task CompletePublishAsync()
        {
            try
            {
                await Task.Delay(10);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }
}
