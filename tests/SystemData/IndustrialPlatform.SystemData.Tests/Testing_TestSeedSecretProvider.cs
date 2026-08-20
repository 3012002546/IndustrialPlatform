using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;

namespace IndustrialPlatform.SystemData.Testing;

/// <summary>
/// 假 Secret 解析器(隔离 sink):按 secretRef(即 SeedKey,见 Runner 解析约定)预设并解析秘密值,
/// 同时记录所有解析请求。供 ServiceInitializer 的 bootstrap 种子解析与「Secret 不泄露/调用次数」断言;
/// 值只存内存,绝不写入任何账本/观察/日志。
/// </summary>
public sealed class TestSeedSecretProvider : ISeedSecretResolver
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);
    private readonly List<string> _resolvedRefs = [];

    /// <summary>已请求解析的 secretRef 集合(解析顺序;供不泄露与调用次数断言)。</summary>
    public IReadOnlyList<string> ResolvedRefs => _resolvedRefs;

    /// <summary>预设秘密值(secretRef 即 SeedKey;覆盖式设置)。</summary>
    public void SetSecret(string secretRef, string value) => _secrets[secretRef] = value;

    /// <inheritdoc />
    public Task<string?> TryResolveAsync(
        string secretRef,
        string environmentNId,
        string serviceKey,
        string moduleKey,
        CancellationToken cancellationToken)
    {
        _resolvedRefs.Add(secretRef);
        return Task.FromResult(_secrets.TryGetValue(secretRef, out var value) ? value : null);
    }
}
