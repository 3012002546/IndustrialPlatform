using System.Text;
using IndustrialPlatform.SystemData.Application.Files;
using IndustrialPlatform.SystemData.Infrastructure.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;

namespace IndustrialPlatform.SystemData.Infrastructure.Tests;

public sealed class FileScannerTests
{
    [Fact]
    public async Task Signature_fallback_is_fail_closed_for_unknown_content()
    {
        var scanner = new LocalSignatureFileScanner();
        var result = await scanner.ScanAsync(File(), new MemoryStream("ordinary content"u8.ToArray()), CancellationToken.None);

        Assert.Equal("Unknown", result.Status);
    }

    [Fact]
    public async Task Signature_fallback_still_blocks_the_known_test_marker()
    {
        var scanner = new LocalSignatureFileScanner();
        var result = await scanner.ScanAsync(File(), new MemoryStream(Encoding.ASCII.GetBytes("prefix EICAR-STANDARD-ANTIVIRUS-TEST-FILE suffix")), CancellationToken.None);

        Assert.Equal("Malicious", result.Status);
    }

    [Fact]
    public async Task ClamAv_protocol_adapter_returns_clean_for_a_real_stream_ok_response()
    {
        await using var server = new FakeClamServer("stream: OK\n");
        var scanner = new ClamAvFileScanner(Configuration(server.Port));

        var result = await scanner.ScanAsync(File(), new MemoryStream("ordinary content"u8.ToArray()), CancellationToken.None);

        Assert.Equal("Clean", result.Status);
        Assert.True(server.SawInstream);
        Assert.True(server.BytesReceived > 0);
    }

    [Fact]
    public async Task ClamAv_protocol_adapter_returns_malicious_for_found_response()
    {
        await using var server = new FakeClamServer("stream: Eicar-Test-Signature FOUND\n");
        var scanner = new ClamAvFileScanner(Configuration(server.Port));

        var result = await scanner.ScanAsync(File(), new MemoryStream("ordinary content"u8.ToArray()), CancellationToken.None);

        Assert.Equal("Malicious", result.Status);
    }

    [Fact]
    public async Task ClamAv_without_endpoint_is_fail_closed()
    {
        var scanner = new ClamAvFileScanner(new ConfigurationBuilder().Build());

        var result = await scanner.ScanAsync(File(), new MemoryStream("ordinary content"u8.ToArray()), CancellationToken.None);

        Assert.Equal("Unknown", result.Status);
    }

    [Fact]
    public async Task Local_upload_coordinator_serializes_same_session_across_instances()
    {
        var root = Path.Combine(Path.GetTempPath(), "industrial-platform-tests", Guid.NewGuid().ToString("N"));
        var options = Options.Create(new SystemDataFileStorageOptions { FileStorageRoot = root });
        var first = new LocalFileUploadCoordinator(options);
        var second = new LocalFileUploadCoordinator(options);
        await using var firstLease = await first.AcquireAsync("tenant-1", "session-1", CancellationToken.None);
        var waiting = second.AcquireAsync("tenant-1", "session-1", CancellationToken.None);

        Assert.NotSame(waiting, await Task.WhenAny(waiting, Task.Delay(100)));
        await firstLease.DisposeAsync();
        await using var secondLease = await waiting;
    }

    private static IConfiguration Configuration(int port) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SystemData:ClamAv:Host"] = "127.0.0.1",
            ["SystemData:ClamAv:Port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
        })
        .Build();

    private sealed class FakeClamServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly string _response;
        private readonly Task _worker;

        public FakeClamServer(string response)
        {
            _response = response;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _worker = AcceptAsync();
        }

        public int Port { get; }
        public bool SawInstream { get; private set; }
        public int BytesReceived { get; private set; }

        private async Task AcceptAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            var command = new byte[10];
            var read = await stream.ReadAsync(command);
            SawInstream = Encoding.ASCII.GetString(command, 0, read).StartsWith("zINSTREAM", StringComparison.Ordinal);
            var size = new byte[4];
            while (await ReadExactlyAsync(stream, size))
            {
                var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(size);
                if (length == 0) break;
                var payload = new byte[length];
                await ReadExactlyAsync(stream, payload);
                BytesReceived += length;
            }
            await stream.WriteAsync(Encoding.UTF8.GetBytes(_response));
        }

        private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset));
                if (read == 0) return false;
                offset += read;
            }
            return true;
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            await _worker;
        }
    }

    private static FileObjectRecord File() => new(
        "tenant-1", "file-1", "session-1", "file.txt", "text/plain", 16, "hash", "tenant-1/session-1.bin",
        "PendingScan", false, "Active", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, "user-1");
}
