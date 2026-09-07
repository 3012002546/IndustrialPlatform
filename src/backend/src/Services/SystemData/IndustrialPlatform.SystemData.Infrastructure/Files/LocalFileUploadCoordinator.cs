using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.SystemData.Application.Files;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Infrastructure.Files;

/// <summary>
/// Cross-process mutex for the upload session. A lock file is intentionally
/// retained; only the exclusive handle is the lock state, so a crashed process
/// cannot leave a stale logical lock behind.
/// </summary>
public sealed class LocalFileUploadCoordinator : IFileUploadCoordinator
{
    private readonly string _lockRoot;

    public LocalFileUploadCoordinator(IOptions<SystemDataFileStorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var configured = string.IsNullOrWhiteSpace(options.Value.FileStorageRoot)
            ? Path.Combine(Path.GetTempPath(), "industrial-platform", "systemdata-files")
            : options.Value.FileStorageRoot;
        _lockRoot = Path.Combine(Path.GetFullPath(configured), ".upload-locks");
        Directory.CreateDirectory(_lockRoot);
    }

    public async Task<IAsyncDisposable> AcquireAsync(string tenantNId, string sessionNId, CancellationToken cancellationToken)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantNId}\n{sessionNId}"))).ToLowerInvariant();
        var path = Path.Combine(_lockRoot, key + ".lock");
        Directory.CreateDirectory(_lockRoot);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var handle = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.Asynchronous);
                return new FileLease(handle);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
            }
        }
    }

    private sealed class FileLease(FileStream handle) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => handle.DisposeAsync();
    }
}
