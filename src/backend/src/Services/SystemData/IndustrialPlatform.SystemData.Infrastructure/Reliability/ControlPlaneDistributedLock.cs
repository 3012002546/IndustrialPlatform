using IndustrialPlatform.Infrastructure.Caching;
using IndustrialPlatform.SystemData.Application.Reliability;

namespace IndustrialPlatform.SystemData.Infrastructure.Reliability;

public sealed class ControlPlaneDistributedLock : IControlPlaneDistributedLock
{
    private readonly IDistributedLock _lock;

    public ControlPlaneDistributedLock(IDistributedLock @lock) => _lock = @lock;

    public Task<bool> TryAcquireAsync(string key, string token, TimeSpan expiry, CancellationToken cancellationToken) =>
        _lock.TryAcquireAsync(key, token, expiry, cancellationToken);

    public Task ReleaseAsync(string key, string token, CancellationToken cancellationToken) =>
        _lock.ReleaseAsync(key, token, cancellationToken);
}
