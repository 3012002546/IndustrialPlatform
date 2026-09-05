using System.Reflection;
using System.Text.Json;
using IndustrialPlatform.EventBus.Connection;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Contracts.Events;
using IndustrialPlatform.ReferenceData.Infrastructure.Outbox;
using IndustrialPlatform.ReferenceData.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Tests;

public sealed class OutboxTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private readonly string path = Path.Combine(Path.GetTempPath(), $"pf03-outbox-{Guid.NewGuid():N}.db");
    private readonly SqlSugarDbContext context;
    private readonly ReferenceDataOutboxStore store;

    public OutboxTests()
    {
        context = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            DbType = DbType.Sqlite,
            ConnectionString = $"Data Source={path};Pooling=False",
        }));
        context.SqlSugar.Ado.ExecuteCommand(ReferenceDataSharedMigration.Sql(false));
        store = new ReferenceDataOutboxStore(context);
    }

    [Fact]
    public void Migration_has_one_service_outbox_and_no_inbox()
    {
        var sql = ReferenceDataSharedMigration.Sql(false);
        Assert.Equal(1, Count(sql, "CREATE TABLE reference_data_outbox_message"));
        Assert.DoesNotContain("inbox", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("module_key", sql, StringComparison.Ordinal);
        Assert.Contains("attempt_count BETWEEN 0 AND 10", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Seven_v1_events_have_stable_routing_keys_and_safe_payloads()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        ReferenceDataIntegrationEvent[] events =
        [
            new ReferenceDictionaryPublishedV1("TENANT-A", "Tenant", "TENANT-A", id, "STATUS", 2),
            new ReferenceConfigurationChangedV1("TENANT-A", "Tenant", "TENANT-A", id,
                "MES/WORKORDER/STATUS", 3, "MES", "STATUS", "Single", "Updated"),
            new ReferenceDynamicConfigurationPublishedV1("TENANT-A", "Tenant", "TENANT-A", id,
                "EQUIPMENT-PARAMETERS", 4),
            new ReferenceMetadataPublishedV1(null, "Platform", null, id, "MATERIAL", 5),
            new ReferenceCodingRulePublishedV1(null, "Platform", null, id, "LOT-NUMBER", 6),
            new ReferenceStateMachineChangedV1("TENANT-A", "Tenant", "TENANT-A", id,
                "WORK-ORDER", 7, "Disabled", 9),
            new ReferenceUnitDimensionChangedV1(null, "Platform", null, id,
                "MASS", 8, "Published", 10),
        ];
        var routingKeys = events.Select(value => value.RoutingKey).ToArray();
        Assert.Equal(
        [
            "industrial.reference-data.dictionary.published.v1",
            "industrial.reference-data.configuration.changed.v1",
            "industrial.reference-data.dynamic-configuration.published.v1",
            "industrial.reference-data.metadata.published.v1",
            "industrial.reference-data.coding-rule.published.v1",
            "industrial.reference-data.state-machine.changed.v1",
            "industrial.reference-data.unit-of-measure.changed.v1",
        ], routingKeys);

        foreach (var item in events)
        {
            var json = JsonSerializer.Serialize(item, item.GetType(), JsonOptions);
            using var payload = JsonDocument.Parse(json);
            Assert.Equal(1, payload.RootElement.GetProperty("eventVersion").GetInt32());
            Assert.Equal(item.SubjectNId, payload.RootElement.GetProperty("subjectNId").GetString());
            Assert.False(payload.RootElement.TryGetProperty("routingKey", out _));
            Assert.DoesNotContain("defaultValue", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("multiValue", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Store_retries_on_exact_schedule_then_retains_terminal_failure()
    {
        var now = new DateTimeOffset(2026, 9, 5, 1, 2, 3, TimeSpan.Zero);
        var @event = new ReferenceDictionaryPublishedV1(
            "TENANT-A", "Tenant", "TENANT-A", Guid.NewGuid(), "STATUS", 1)
        {
            CreatedTime = now,
        };
        await ReferenceDataOutboxWriter.InsertAsync(
            context.SqlSugar, "dictionary", @event, CancellationToken.None);

        var pending = await store.GetPendingAsync(now, 100, CancellationToken.None);
        Assert.Single(pending);
        Assert.Equal(@event.EventId, pending[0].EventId);
        Assert.DoesNotContain("routingKey", pending[0].Payload, StringComparison.OrdinalIgnoreCase);

        int[] delays = [1, 2, 4, 8, 16, 30, 60, 120, 300];
        for (var attempt = 0; attempt < delays.Length; attempt++)
        {
            var outcome = await store.RecordFailureAsync(@event.EventId, attempt, "temporary", now,
                CancellationToken.None);
            Assert.True(outcome.Updated);
            Assert.False(outcome.Terminal);
            var actual = await ReadAsync(@event.EventId);
            Assert.Equal(attempt + 1, actual.AttemptCount);
            Assert.Equal(now.AddSeconds(delays[attempt]), actual.NextAttemptOn);
        }

        var terminalOutcome = await store.RecordFailureAsync(@event.EventId, 9, new string('x', 600), now,
            CancellationToken.None);
        Assert.True(terminalOutcome.Updated);
        Assert.True(terminalOutcome.Terminal);
        var terminal = await ReadAsync(@event.EventId);
        Assert.Equal("Failed", terminal.Status);
        Assert.Equal(10, terminal.AttemptCount);
        Assert.Null(terminal.NextAttemptOn);
        Assert.Equal(500, terminal.LastError!.Length);
    }

    [Fact]
    public async Task Published_rows_leave_pending_scan_and_module_backlog()
    {
        var now = DateTimeOffset.UtcNow;
        var @event = new ReferenceMetadataPublishedV1(
            null, "Platform", null, Guid.NewGuid(), "MATERIAL", 3) { CreatedTime = now };
        await ReferenceDataOutboxWriter.InsertAsync(
            context.SqlSugar, "metadata", @event, CancellationToken.None);

        var counts = await store.CountPendingByModuleAsync(CancellationToken.None);
        Assert.Equal(1, counts["metadata"]);
        Assert.True(await store.MarkPublishedAsync(
            @event.EventId, now.AddSeconds(1), CancellationToken.None));
        Assert.Empty(await store.GetPendingAsync(now.AddMinutes(1), 100, CancellationToken.None));
        Assert.Empty(await store.CountPendingByModuleAsync(CancellationToken.None));
        var row = await ReadAsync(@event.EventId);
        Assert.Equal("Published", row.Status);
        Assert.Equal(now.AddSeconds(1), row.PublishedOn);
    }

    [Fact]
    public async Task Confirmed_delivery_wins_over_a_competing_terminal_failure_and_stale_failures_are_ignored()
    {
        var now = DateTimeOffset.UtcNow;
        var @event = new ReferenceDictionaryPublishedV1(
            "TENANT-A", "Tenant", "TENANT-A", Guid.NewGuid(), "STATUS", 1) { CreatedTime = now };
        await ReferenceDataOutboxWriter.InsertAsync(
            context.SqlSugar, "dictionary", @event, CancellationToken.None);
        await context.SqlSugar.Ado.ExecuteCommandAsync(
            "UPDATE reference_data_outbox_message SET attempt_count=9 WHERE event_id=@eventId",
            new SugarParameter("@eventId", @event.EventId));

        var terminal = await store.RecordFailureAsync(
            @event.EventId, 9, "broker unavailable", now, CancellationToken.None);
        Assert.True(terminal.Updated);
        Assert.True(terminal.Terminal);
        Assert.True(await store.MarkPublishedAsync(
            @event.EventId, now.AddSeconds(1), CancellationToken.None));
        var staleFailure = await store.RecordFailureAsync(
            @event.EventId, 9, "late failure", now.AddSeconds(2), CancellationToken.None);

        Assert.False(staleFailure.Updated);
        Assert.False(staleFailure.Terminal);
        Assert.Equal("Published", (await ReadAsync(@event.EventId)).Status);
        Assert.False(await store.MarkPublishedAsync(
            @event.EventId, now.AddSeconds(3), CancellationToken.None));
    }

    [Fact]
    public async Task Dispatcher_waits_for_publisher_confirmation_before_marking_a_row_published()
    {
        var now = new DateTimeOffset(2026, 9, 5, 3, 0, 0, TimeSpan.Zero);
        var @event = new ReferenceDictionaryPublishedV1(
            "TENANT-A", "Tenant", "TENANT-A", Guid.NewGuid(), "STATUS", 1) { CreatedTime = now };
        await ReferenceDataOutboxWriter.InsertAsync(
            context.SqlSugar, "dictionary", @event, CancellationToken.None);
        var (channel, channelProxy) = CreateChannel();
        var connection = new TestRabbitMqConnection(channel);
        var timeProvider = new MutableTimeProvider(now);
        using var services = CreateDispatcherServices();
        var dispatcher = CreateDispatcher(services, connection, timeProvider);

        var dispatch = dispatcher.DispatchBatchAsync(CancellationToken.None);
        await channelProxy.PublishStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(connection.PublisherConfirmationsEnabled);
        Assert.Equal("Pending", (await ReadAsync(@event.EventId)).Status);

        channelProxy.PublishConfirmation.SetResult();
        await dispatch.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Published", (await ReadAsync(@event.EventId)).Status);
    }

    [Fact]
    public async Task Dispatcher_records_a_failed_publisher_confirmation_instead_of_marking_published()
    {
        var now = new DateTimeOffset(2026, 9, 5, 3, 0, 0, TimeSpan.Zero);
        var @event = new ReferenceMetadataPublishedV1(
            null, "Platform", null, Guid.NewGuid(), "MATERIAL", 1) { CreatedTime = now };
        await ReferenceDataOutboxWriter.InsertAsync(
            context.SqlSugar, "metadata", @event, CancellationToken.None);
        var (channel, channelProxy) = CreateChannel();
        channelProxy.PublishConfirmation.SetException(new InvalidOperationException("publisher nack"));
        var connection = new TestRabbitMqConnection(channel);
        var timeProvider = new MutableTimeProvider(now);
        using var services = CreateDispatcherServices();

        await CreateDispatcher(services, connection, timeProvider)
            .DispatchBatchAsync(CancellationToken.None);

        var row = await ReadAsync(@event.EventId);
        Assert.True(connection.PublisherConfirmationsEnabled);
        Assert.Equal("Pending", row.Status);
        Assert.Equal(1, row.AttemptCount);
        Assert.Equal(now.AddSeconds(1), row.NextAttemptOn);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Dispatcher_persists_batch_setup_failures_through_backoff_and_terminal_attempt(
        bool failConnection)
    {
        var now = new DateTimeOffset(2026, 9, 5, 4, 0, 0, TimeSpan.Zero);
        var first = new ReferenceDictionaryPublishedV1(
            "TENANT-A", "Tenant", "TENANT-A", Guid.NewGuid(), "STATUS", 1) { CreatedTime = now };
        var second = new ReferenceMetadataPublishedV1(
            null, "Platform", null, Guid.NewGuid(), "MATERIAL", 1) { CreatedTime = now };
        await ReferenceDataOutboxWriter.InsertAsync(
            context.SqlSugar, "dictionary", first, CancellationToken.None);
        await ReferenceDataOutboxWriter.InsertAsync(
            context.SqlSugar, "metadata", second, CancellationToken.None);
        var (channel, channelProxy) = CreateChannel();
        if (!failConnection)
            channelProxy.ExchangeFailure = new InvalidOperationException("exchange unavailable");
        var connection = new TestRabbitMqConnection(
            channel, failConnection ? new InvalidOperationException("connection unavailable") : null);
        var timeProvider = new MutableTimeProvider(now);
        using var services = CreateDispatcherServices();
        var dispatcher = CreateDispatcher(services, connection, timeProvider);
        int[] delays = [1, 2, 4, 8, 16, 30, 60, 120, 300];

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            await dispatcher.DispatchBatchAsync(CancellationToken.None);
            var rows = await ReadAllAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, row => Assert.Equal(attempt, row.AttemptCount));
            Assert.All(rows, row => Assert.Equal(attempt == 10 ? "Failed" : "Pending", row.Status));
            if (attempt < 10)
            {
                var next = now.AddSeconds(delays[attempt - 1]);
                Assert.All(rows, row => Assert.Equal(next, row.NextAttemptOn));
                now = next;
                timeProvider.UtcNow = now;
            }
            else
            {
                Assert.All(rows, row => Assert.Null(row.NextAttemptOn));
            }
        }

        Assert.True(connection.PublisherConfirmationsEnabled);
        Assert.Empty(await store.GetPendingAsync(now.AddDays(1), 100, CancellationToken.None));
    }

    public void Dispose()
    {
        context.Dispose();
        if (File.Exists(path))
            File.Delete(path);
    }

    private async Task<OutboxProbe> ReadAsync(Guid id)
    {
        var rows = await context.SqlSugar.Ado.SqlQueryAsync<OutboxProbe>(
            "SELECT status AS Status,attempt_count AS AttemptCount,next_attempt_on AS NextAttemptOn,"
            + "last_error AS LastError,published_on AS PublishedOn FROM reference_data_outbox_message "
            + "WHERE event_id=@id", new SugarParameter("@id", id));
        return Assert.Single(rows);
    }

    private Task<List<OutboxProbe>> ReadAllAsync() => context.SqlSugar.Ado.SqlQueryAsync<OutboxProbe>(
        "SELECT status AS Status,attempt_count AS AttemptCount,next_attempt_on AS NextAttemptOn,"
        + "last_error AS LastError,published_on AS PublishedOn FROM reference_data_outbox_message ORDER BY event_id");

    private ServiceProvider CreateDispatcherServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddScoped<ReferenceDataOutboxStore>();
        return services.BuildServiceProvider();
    }

    private static ReferenceDataOutboxDispatcher CreateDispatcher(
        ServiceProvider services, IRabbitMqConnection connection, TimeProvider timeProvider) =>
        new(services.GetRequiredService<IServiceScopeFactory>(), connection, timeProvider,
            NullLogger<ReferenceDataOutboxDispatcher>.Instance);

    private static (IChannel Channel, TestChannelProxy Proxy) CreateChannel()
    {
        var channel = DispatchProxy.Create<IChannel, TestChannelProxy>();
        return (channel, (TestChannelProxy)(object)channel);
    }

    private static int Count(string value, string fragment)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0;
             index += fragment.Length)
            count++;
        return count;
    }

    private sealed class OutboxProbe
    {
        public string Status { get; set; } = string.Empty;
        public int AttemptCount { get; set; }
        public DateTimeOffset? NextAttemptOn { get; set; }
        public string? LastError { get; set; }
        public DateTimeOffset? PublishedOn { get; set; }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class TestRabbitMqConnection(
        IChannel channel,
        Exception? connectionFailure = null) : IRabbitMqConnection
    {
        public bool PublisherConfirmationsEnabled { get; private set; }

        public Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default) =>
            CreateChannelAsync(false, cancellationToken);

        public Task<IChannel> CreateChannelAsync(
            bool publisherConfirmationsEnabled,
            CancellationToken cancellationToken = default)
        {
            PublisherConfirmationsEnabled = publisherConfirmationsEnabled;
            return connectionFailure is null
                ? Task.FromResult(channel)
                : Task.FromException<IChannel>(connectionFailure);
        }
    }

    public class TestChannelProxy : DispatchProxy
    {
        public Exception? ExchangeFailure { get; set; }
        public TaskCompletionSource PublishStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PublishConfirmation { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability", "CA2012:Use ValueTasks correctly",
            Justification = "DispatchProxy requires the intercepted ValueTask to be returned as object.")]
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                nameof(IChannel.ExchangeDeclareAsync) => ExchangeFailure is null
                    ? Task.CompletedTask
                    : Task.FromException(ExchangeFailure),
                nameof(IChannel.BasicPublishAsync) => CompletePublishInvocation(),
                nameof(IDisposable.Dispose) => null,
                nameof(IAsyncDisposable.DisposeAsync) => ValueTask.CompletedTask,
                "get_IsOpen" => true,
                _ => throw new NotSupportedException(targetMethod?.Name),
            };
        }

        private ValueTask CompletePublishInvocation()
        {
            PublishStarted.TrySetResult();
            return new ValueTask(PublishConfirmation.Task);
        }
    }
}
