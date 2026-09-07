using System.Net.Sockets;
using System.Text;
using IndustrialPlatform.SystemData.Application.Files;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.SystemData.Infrastructure.Files;

public sealed class ClamAvOptions
{
    public const string SectionName = "SystemData:ClamAv";
    public string? Host { get; set; }
    public int Port { get; set; } = 3310;
}

/// <summary>
/// Trusted scanner adapter using ClamAV's INSTREAM protocol. When no endpoint
/// is configured, it fails closed with Unknown; it never treats arbitrary
/// content as clean.
/// </summary>
public sealed class ClamAvFileScanner : IFileScanner
{
    private readonly ClamAvOptions _options;

    public ClamAvFileScanner(IConfiguration configuration)
    {
        _options = new ClamAvOptions();
        configuration.GetSection(ClamAvOptions.SectionName).Bind(_options);
    }

    public async Task<FileScanResult> ScanAsync(FileObjectRecord file, Stream content, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Host))
            return new FileScanResult("Unknown", "未配置可信 ClamAV 扫描器，文件保持禁止下载状态。");
        if (_options.Port is < 1 or > 65535)
            return new FileScanResult("Error", "ClamAV 端口配置无效。");

        try
        {
            return await ScanConfiguredAsync(content, cancellationToken);
        }
        catch (SocketException exception)
        {
            return new FileScanResult("Error", $"ClamAV 不可用：{exception.Message}");
        }
        catch (IOException exception)
        {
            return new FileScanResult("Error", $"ClamAV 通信失败：{exception.Message}");
        }
    }

    private async Task<FileScanResult> ScanConfiguredAsync(Stream content, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(_options.Host!, _options.Port, cancellationToken);
        await using var network = client.GetStream();
        await network.WriteAsync("zINSTREAM\0"u8.ToArray(), cancellationToken);

        var buffer = new byte[64 * 1024];
        var size = new byte[4];
        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(size, read);
            await network.WriteAsync(size, cancellationToken);
            await network.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        await network.WriteAsync(new byte[4], cancellationToken);
        await network.FlushAsync(cancellationToken);

        var response = await ReadLineAsync(network, cancellationToken);
        if (response.EndsWith("OK", StringComparison.OrdinalIgnoreCase))
            return new FileScanResult("Clean", "ClamAV stream scan passed.");
        if (response.Contains("FOUND", StringComparison.OrdinalIgnoreCase))
            return new FileScanResult("Malicious", response);
        return new FileScanResult("Error", string.IsNullOrWhiteSpace(response) ? "ClamAV 返回空响应。" : response);
    }

    private static async Task<string> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>(128);
        var one = new byte[1];
        while (bytes.Count < 4096)
        {
            var count = await stream.ReadAsync(one, cancellationToken);
            if (count == 0) break;
            if (one[0] == (byte)'\n') break;
            if (one[0] != (byte)'\r') bytes.Add(one[0]);
        }
        return Encoding.UTF8.GetString(bytes.ToArray()).Trim();
    }
}
