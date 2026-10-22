using System.Collections.Concurrent;
namespace IndustrialPlatform.PlatformStudio.Infrastructure.Data;

/// <summary>部署进程共享的租户查询预算；HTTP请求结束不释放其他请求正在持有的闸门。</summary>
public sealed class StudioSourceLimits : IDisposable
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
    public SemaphoreSlim ForTenant(string tenant, int maximum) => _gates.GetOrAdd(tenant, _ => new SemaphoreSlim(maximum));
    public void Dispose() { foreach (var gate in _gates.Values) gate.Dispose(); }
}
