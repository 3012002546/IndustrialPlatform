using IndustrialPlatform.EventBus.Abstractions;
using IndustrialPlatform.EventBus.Events;
using IndustrialPlatform.SystemData.Application.Files;
using IndustrialPlatform.SystemData.Infrastructure.Reliability;
using Microsoft.Extensions.Logging.Abstractions;

namespace IndustrialPlatform.SystemData.Tests;

public sealed class Infrastructure_FileStatusOutboxTests
{
    [Fact]
    public async Task Dispatcher_records_failure_without_acknowledging_the_file_event()
    {
        var item = new FileStatusOutboxRecord(
            Guid.NewGuid(),
            "tenant-1",
            "file-1",
            "Clean",
            false,
            "Active",
            DateTimeOffset.UtcNow,
            0,
            null,
            null,
            null,
            null);
        var outbox = new TestFileStatusOutbox(item);
        var eventBus = new FailingEventBus();
        var dispatcher = new FileStatusOutboxDispatcher(outbox, eventBus, NullLogger<FileStatusOutboxDispatcher>.Instance);

        await dispatcher.DispatchOnceAsync(CancellationToken.None);

        Assert.Null(outbox.PublishedOn);
        Assert.Equal(1, outbox.RecordFailureCalls);
        Assert.Equal(1, outbox.RetryCount);
        Assert.NotNull(outbox.LastError);
    }

    [Fact]
    public async Task Dispatcher_marks_event_published_only_after_event_bus_returns()
    {
        var item = new FileStatusOutboxRecord(
            Guid.NewGuid(),
            "tenant-1",
            "file-1",
            "Clean",
            false,
            "Active",
            DateTimeOffset.UtcNow,
            0,
            null,
            null,
            null,
            null);
        var outbox = new TestFileStatusOutbox(item);
        var dispatcher = new FileStatusOutboxDispatcher(outbox, new SuccessfulEventBus(), NullLogger<FileStatusOutboxDispatcher>.Instance);

        await dispatcher.DispatchOnceAsync(CancellationToken.None);

        Assert.NotNull(outbox.PublishedOn);
        Assert.Equal(0, outbox.RecordFailureCalls);
    }

    private sealed class TestFileStatusOutbox : IFileStatusOutbox
    {
        private FileStatusOutboxRecord _item;

        public TestFileStatusOutbox(FileStatusOutboxRecord item) => _item = item;

        public DateTimeOffset? PublishedOn => _item.PublishedOn;
        public int RetryCount => _item.RetryCount;
        public int RecordFailureCalls { get; private set; }
        public string? LastError => _item.LastError;

        public Task EnqueueAsync(FileStatusChangeRecord item, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<FileStatusOutboxRecord>> GetPendingAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileStatusOutboxRecord>>(_item.PublishedOn is null && _item.DeadLetteredOn is null ? [_item] : []);

        public Task MarkPublishedAsync(Guid eventId, DateTimeOffset publishedOn, CancellationToken cancellationToken)
        {
            _item = _item with { PublishedOn = publishedOn };
            return Task.CompletedTask;
        }

        public Task<bool> RecordFailureAsync(Guid eventId, int retryCount, string lastError, bool deadLetter, DateTimeOffset nextAttemptOn, CancellationToken cancellationToken)
        {
            RecordFailureCalls++;
            _item = _item with { RetryCount = retryCount, LastError = lastError, DeadLetteredOn = deadLetter ? nextAttemptOn : null, NextAttemptOn = nextAttemptOn };
            return Task.FromResult(deadLetter);
        }
    }

    private sealed class FailingEventBus : IEventBus
    {
        public Task PublishAsync<TEvent>(TEvent integrationEvent, string? routingKey = null, CancellationToken cancellationToken = default)
            where TEvent : IntegrationEvent => Task.FromException(new InvalidOperationException("RabbitMQ unavailable"));
    }

    private sealed class SuccessfulEventBus : IEventBus
    {
        public Task PublishAsync<TEvent>(TEvent integrationEvent, string? routingKey = null, CancellationToken cancellationToken = default)
            where TEvent : IntegrationEvent => Task.CompletedTask;
    }
}
