using IndustrialPlatform.EventBus.Consumer;
using IndustrialPlatform.EventBus.Options;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class EventBusConsumerRetryPolicyTests
{
    [Fact]
    public void Delivery_attempt_counts_only_main_queue_rejected_history()
    {
        var headers = new Dictionary<string, object?>
        {
            ["x-death"] = new List<object?>
            {
                new Dictionary<string, object?> { ["queue"] = "industrial.events", ["reason"] = "rejected", ["count"] = 2L },
                new Dictionary<string, object?> { ["queue"] = "industrial.events.retry", ["reason"] = "expired", ["count"] = 2L },
            },
        };

        Assert.Equal(2, RabbitDeliveryPolicy.GetDeliveryAttempt(headers, "industrial.events"));
        Assert.Equal(0, RabbitDeliveryPolicy.GetDeliveryAttempt(headers, "other.queue"));
        Assert.Equal(0, RabbitDeliveryPolicy.GetDeliveryAttempt(null, "industrial.events"));
    }

    [Fact]
    public void Rabbit_options_define_bounded_retry_and_durable_dlq_defaults()
    {
        var options = new RabbitMqOptions();

        Assert.True(options.MaxDeliveryAttempts > 0);
        Assert.NotEqual(options.QueueName, options.RetryQueueName);
        Assert.NotEqual(options.QueueName, options.BusyRetryQueueName);
        Assert.True(options.BusyRetryDelayMilliseconds > 0);
        Assert.NotEqual(options.QueueName, options.DeadLetterQueueName);
        Assert.False(string.IsNullOrWhiteSpace(options.DeadLetterExchangeName));
    }
}
