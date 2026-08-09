namespace IndustrialPlatform.Logging.Options;

/// <summary>
/// Serilog 日志配置,对应配置节点 "Serilog"。
/// </summary>
public sealed class SerilogOptions
{
    /// <summary>服务名,写入每条日志的 Service 字段。</summary>
    public string ServiceName { get; set; } = "IndustrialPlatform";

    /// <summary>最低日志级别,默认 Information。</summary>
    public string MinimumLevel { get; set; } = "Information";

    /// <summary>控制台输出配置。</summary>
    public ConsoleOptions Console { get; set; } = new();

    /// <summary>文件输出配置。</summary>
    public FileOptions File { get; set; } = new();

    /// <summary>Seq 输出配置,启用后才写入。</summary>
    public SeqOptions? Seq { get; set; }
}

/// <summary>控制台输出配置。</summary>
public sealed class ConsoleOptions
{
    /// <summary>是否启用控制台输出,默认启用。</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>文件输出配置。</summary>
public sealed class FileOptions
{
    /// <summary>是否启用文件输出,默认启用。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>日志文件路径模板,支持 Serilog 滚动占位符。</summary>
    public string Path { get; set; } = "logs/industrial-platform-.log";

    /// <summary>单个日志文件大小上限,默认 100MB。</summary>
    public long FileSizeLimitBytes { get; set; } = 100L * 1024 * 1024;

    /// <summary>保留的日志文件数量,默认 30。</summary>
    public int RetainedFileCountLimit { get; set; } = 30;
}

/// <summary>Seq 输出配置。</summary>
public sealed class SeqOptions
{
    /// <summary>是否启用 Seq 输出,默认关闭。</summary>
    public bool Enabled { get; set; }

    /// <summary>Seq 服务地址。</summary>
    public string ServerUrl { get; set; } = "http://localhost:5341";

    /// <summary>Seq ApiKey。</summary>
    public string? ApiKey { get; set; }
}
