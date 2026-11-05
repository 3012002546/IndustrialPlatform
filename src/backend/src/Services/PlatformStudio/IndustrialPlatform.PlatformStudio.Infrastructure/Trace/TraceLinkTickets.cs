using System.Security.Cryptography;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Trace;

/// <summary>Short-lived bounded server context keeps signed node tokens out of iframe URLs.</summary>
public sealed class TraceLinkTickets(TimeProvider clock)
{
    private readonly Dictionary<string, (string Json, DateTimeOffset Expires)> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    public string Add(string json, DateTimeOffset expires)
    {
        lock (_gate)
        {
            RemoveExpired();
            if (_entries.Count >= 128 || json.Length > 262144) throw new StudioException("STUDIO_LIMIT", "同时打开的追溯附件或上下文超过预算，请稍后重试。", 429);
            var key = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            _entries.Add(key, (json, expires)); return key;
        }
    }
    public string Read(string key)
    {
        lock (_gate)
        {
            RemoveExpired();
            return _entries.TryGetValue(key, out var value) ? value.Json : throw new StudioException("STUDIO_TRACE_LINK_INVALID", "附件读取凭证已失效，请重新打开。", 403);
        }
    }
    private void RemoveExpired()
    {
        foreach (var key in _entries.Where(x => x.Value.Expires <= clock.GetUtcNow()).Select(x => x.Key).ToArray()) _entries.Remove(key);
    }
}
