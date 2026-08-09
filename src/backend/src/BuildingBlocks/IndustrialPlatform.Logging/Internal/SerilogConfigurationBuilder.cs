using System.Globalization;
using IndustrialPlatform.Logging.Enrichers;
using IndustrialPlatform.Logging.Options;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace IndustrialPlatform.Logging.Internal;

/// <summary>
/// Serilog 日志配置构建器,统一 Console/File/Seq 输出与 Service/TraceId 增强。
/// </summary>
internal static class SerilogConfigurationBuilder
{
    private const string OutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Service} {TraceId} {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// 依据选项构建日志配置。
    /// </summary>
    /// <param name="options">日志选项。</param>
    /// <returns>日志配置。</returns>
    public static LoggerConfiguration Build(SerilogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Configure(new LoggerConfiguration(), options);
    }

    /// <summary>
    /// 在既有配置上追加增强与输出目标。
    /// </summary>
    /// <param name="configuration">既有日志配置。</param>
    /// <param name="options">日志选项。</param>
    /// <returns>日志配置。</returns>
    public static LoggerConfiguration Configure(LoggerConfiguration configuration, SerilogOptions options)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        configuration
            .MinimumLevel.Is(ParseLevel(options.MinimumLevel))
            .Enrich.FromLogContext()
            .Enrich.With(new TraceIdEnricher())
            .Enrich.WithProperty("Service", options.ServiceName);

        if (options.Console.Enabled)
        {
            configuration.WriteTo.Console(outputTemplate: OutputTemplate, formatProvider: CultureInfo.InvariantCulture, theme: SystemConsoleTheme.Literate);
        }

        if (options.File.Enabled)
        {
            configuration.WriteTo.File(
                path: options.File.Path,
                outputTemplate: OutputTemplate,
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: options.File.FileSizeLimitBytes,
                retainedFileCountLimit: options.File.RetainedFileCountLimit,
                rollOnFileSizeLimit: true);
        }

        if (options.Seq is { Enabled: true } seq)
        {
            configuration.WriteTo.Seq(seq.ServerUrl, apiKey: seq.ApiKey, formatProvider: CultureInfo.InvariantCulture);
        }

        return configuration;
    }

    private static LogEventLevel ParseLevel(string minimumLevel)
        => Enum.TryParse<LogEventLevel>(minimumLevel, ignoreCase: true, out var level)
            ? level
            : LogEventLevel.Information;
}
