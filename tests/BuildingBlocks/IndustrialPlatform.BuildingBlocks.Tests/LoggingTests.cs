using System.Diagnostics;
using System.Globalization;
using IndustrialPlatform.Logging.Enrichers;
using IndustrialPlatform.Logging.Internal;
using IndustrialPlatform.Logging.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class SerilogOptionsTests
{
    [Fact]
    public void Options_Binding_MapsConfigurationValues()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Serilog:ServiceName"] = "ReferenceData.Api",
                ["Serilog:MinimumLevel"] = "Debug",
                ["Serilog:Console:Enabled"] = "false",
                ["Serilog:File:Enabled"] = "true",
                ["Serilog:File:Path"] = "logs/refdata-.log",
                ["Serilog:File:RetainedFileCountLimit"] = "15",
                ["Serilog:Seq:Enabled"] = "true",
                ["Serilog:Seq:ServerUrl"] = "http://seq.example.com:5341",
            })
            .Build();

        var options = configuration.GetSection("Serilog").Get<SerilogOptions>();

        Assert.NotNull(options);
        Assert.Equal("ReferenceData.Api", options!.ServiceName);
        Assert.Equal("Debug", options.MinimumLevel);
        Assert.False(options.Console.Enabled);
        Assert.True(options.File.Enabled);
        Assert.Equal("logs/refdata-.log", options.File.Path);
        Assert.Equal(15, options.File.RetainedFileCountLimit);
        Assert.NotNull(options.Seq);
        Assert.True(options.Seq.Enabled);
        Assert.Equal("http://seq.example.com:5341", options.Seq.ServerUrl);
    }

    [Fact]
    public void AddIndustrialLogging_RegistersLogger()
    {
        var services = new ServiceCollection();
        services.AddIndustrialLogging(new ConfigurationBuilder().Build());

        Assert.Contains(services, service => service.ServiceType == typeof(Logger));
        Assert.Contains(services, service => service.ServiceType == typeof(Serilog.ILogger));
    }

    [Fact]
    public void Configure_Suppresses_AspNetHosting_RequestTargetLogs()
    {
        var sink = new CollectingSink();
        var options = new SerilogOptions
        {
            MinimumLevel = "Information",
            Console = new() { Enabled = false },
            File = new() { Enabled = false },
            Seq = null,
        };

        using var logger = SerilogConfigurationBuilder.Configure(new Serilog.LoggerConfiguration(), options)
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.ForContext(Constants.SourceContextPropertyName, "Microsoft.AspNetCore.Hosting.Diagnostics")
            .Information("Request starting {Target}", "/hub/notifications?access_token=eyJhbGciOiJIUzI1NiJ9.secret");
        logger.ForContext(Constants.SourceContextPropertyName, "IndustrialPlatform.Web.RequestLoggingMiddleware")
            .Information("Request completed {Target}", "/hub/notifications?page=1");

        Assert.Single(sink.Events);
        Assert.DoesNotContain("access_token", sink.Events[0].RenderMessage(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("page=1", sink.Events[0].RenderMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}

public sealed class TraceIdEnricherTests
{
    private readonly TestPropertyFactory _propertyFactory = new();

    [Fact]
    public void Enrich_WithActiveActivity_AddsTraceIdProperty()
    {
        using var activity = new Activity("test").Start();

        var logEvent = CreateLogEvent();
        var enricher = new TraceIdEnricher();

        enricher.Enrich(logEvent, _propertyFactory);

        Assert.True(logEvent.Properties.TryGetValue("TraceId", out var property));
        var scalar = Assert.IsAssignableFrom<ScalarValue>(property);
        Assert.Equal(activity.TraceId.ToString(), scalar.Value);
    }

    [Fact]
    public void Enrich_WithoutActiveActivity_AddsNothing()
    {
        var previous = Activity.Current;
        Activity.Current = null;
        try
        {
            var logEvent = CreateLogEvent();
            var enricher = new TraceIdEnricher();

            enricher.Enrich(logEvent, _propertyFactory);

            Assert.False(logEvent.Properties.ContainsKey("TraceId"));
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    private static LogEvent CreateLogEvent()
        => new(DateTimeOffset.Now, LogEventLevel.Information, exception: null, MessageTemplate.Empty, properties: []);

    private sealed class TestPropertyFactory : ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false)
            => new(name, new ScalarValue(value));
    }
}
