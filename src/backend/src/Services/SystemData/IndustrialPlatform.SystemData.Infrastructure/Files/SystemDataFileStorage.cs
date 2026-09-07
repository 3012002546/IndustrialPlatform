using System.Collections.Concurrent;
using System.Security.Cryptography;
using IndustrialPlatform.SystemData.Application.Files;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.SystemData.Infrastructure.Files;

public sealed class SystemDataFileStorageOptions
{
    public const string SectionName = "SystemData";
    public string? FileStorageRoot { get; set; }
}

/// <summary>
/// Core local file adapter. The storage key is always resolved below the configured root.
/// </summary>
public sealed class LocalFileContentStore : IFileContentStore
{
    private readonly string _root;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public LocalFileContentStore(IConfiguration configuration)
    {
        var configured = configuration["SystemData:FileStorageRoot"];
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "industrial-platform", "systemdata-files")
            : configured);
        Directory.CreateDirectory(_root);
    }

    public async Task<long> AppendAsync(string storageKey, long expectedOffset, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = Resolve(storageKey);
        var gate = _locks.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // FileShare.None makes the append boundary safe across application
            // instances; the database offset is still the authoritative commit.
            await using var target = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (target.Length != expectedOffset) throw new InvalidDataException("服务端上传偏移与存储偏移不一致。");
            target.Position = expectedOffset;
            await content.CopyToAsync(target, cancellationToken);
            await target.FlushAsync(cancellationToken);
            return target.Length;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<long> EnsureLengthAsync(string storageKey, long expectedLength, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedLength);
        var path = Resolve(storageKey);
        var gate = _locks.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path))
            {
                if (expectedLength == 0) return 0;
                throw new InvalidDataException("服务端上传偏移与存储偏移不一致。");
            }
            await using var target = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (target.Length < expectedLength) throw new InvalidDataException("服务端上传偏移超过存储内容长度。");
            if (target.Length > expectedLength)
            {
                target.SetLength(expectedLength);
                await target.FlushAsync(cancellationToken);
            }
            return expectedLength;
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        var path = Resolve(storageKey);
        if (!File.Exists(path)) throw new FileNotFoundException("文件内容不存在。", path);
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public async Task<string> ComputeSha256Async(string storageKey, CancellationToken cancellationToken)
    {
        await using var stream = await OpenReadAsync(storageKey, cancellationToken);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(storageKey);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string Resolve(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.Contains('\0', StringComparison.Ordinal)) throw new InvalidDataException("非法文件存储键。");
        var relative = storageKey.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_root, relative));
        var rootPrefix = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("文件存储键越界。");
        return fullPath;
    }
}
