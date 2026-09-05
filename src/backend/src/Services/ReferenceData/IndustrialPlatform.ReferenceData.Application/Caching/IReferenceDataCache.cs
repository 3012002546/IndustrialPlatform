using System.Security.Cryptography;
using System.Text;

namespace IndustrialPlatform.ReferenceData.Application.Caching;

public readonly record struct ReferenceDataCacheKey(string Module, string Value, string? GenerationScope = null)
{
    public string GenerationKey => string.IsNullOrWhiteSpace(GenerationScope)
        ? Module
        : $"{Module}\u001f{GenerationScope}";
}

public readonly record struct ReferenceDataCachePattern(string Module, string Value, string? GenerationScope = null)
{
    public string GenerationKey => string.IsNullOrWhiteSpace(GenerationScope)
        ? Module
        : $"{Module}\u001f{GenerationScope}";
}

public sealed record ReferenceDataCachePage<T>(IReadOnlyList<T> Items, long Total, int Revision);

/// <summary>
/// Service-wide cache-aside port. Implementations treat cache failures as misses and never fail a database-backed read.
/// </summary>
public interface IReferenceDataCache
{
    Task<T?> GetAsync<T>(ReferenceDataCacheKey key, CancellationToken cancellationToken = default)
        where T : class;

    Task SetAsync<T>(ReferenceDataCacheKey key, T value, TimeSpan ttl,
        CancellationToken cancellationToken = default) where T : class;

    Task<string?> GetGenerationAsync(string moduleKey, CancellationToken cancellationToken = default);

    Task<bool> SetIfGenerationAsync<T>(ReferenceDataCacheKey key, T value, TimeSpan ttl,
        string? expectedGeneration, CancellationToken cancellationToken = default) where T : class;

    Task InvalidateAsync(ReferenceDataCachePattern pattern, CancellationToken cancellationToken = default);
}

public sealed class NullReferenceDataCache : IReferenceDataCache
{
    public static NullReferenceDataCache Instance { get; } = new();

    private NullReferenceDataCache() { }

    public Task<T?> GetAsync<T>(ReferenceDataCacheKey key, CancellationToken cancellationToken = default)
        where T : class => Task.FromResult<T?>(null);

    public Task SetAsync<T>(ReferenceDataCacheKey key, T value, TimeSpan ttl,
        CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;

    public Task<string?> GetGenerationAsync(string moduleKey,
        CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

    public Task<bool> SetIfGenerationAsync<T>(ReferenceDataCacheKey key, T value, TimeSpan ttl,
        string? expectedGeneration, CancellationToken cancellationToken = default) where T : class =>
        Task.FromResult(false);

    public Task InvalidateAsync(ReferenceDataCachePattern pattern,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public static class ReferenceDataCacheTtl
{
    public static readonly TimeSpan Dictionary = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan Parameter = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan DynamicSchema = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan DynamicRecords = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Metadata = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan CodingRule = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan StateMachineCurrent = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan StateMachineRevision = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan UnitCurrent = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan UnitRevision = TimeSpan.FromMinutes(30);
}

public static class ReferenceDataCacheKeys
{
    public static ReferenceDataCacheKey Dictionary(string tenantKey, string normalizedNId) =>
        new("dictionary", $"referencedata:v1:{tenantKey}:dictionary:{normalizedNId}", normalizedNId);

    public static ReferenceDataCachePattern Dictionaries(string normalizedNId) =>
        new("dictionary", $"referencedata:v1:*:dictionary:{normalizedNId}", normalizedNId);

    public static ReferenceDataCacheKey Configuration(string tenantKey, string factoryKey,
        string normalizedAppDomainNId, string normalizedKeyNId) =>
        new("parameter", $"referencedata:v1:{tenantKey}:configuration:{factoryKey}:{normalizedAppDomainNId}:{normalizedKeyNId}",
            $"configuration:{normalizedAppDomainNId}");

    public static ReferenceDataCacheKey ConfigurationDomain(string tenantKey, string factoryKey,
        string normalizedAppDomainNId, string revision) =>
        new("parameter", $"referencedata:v1:{tenantKey}:configuration-domain:{factoryKey}:{normalizedAppDomainNId}:{revision}",
            $"configuration-domain:{normalizedAppDomainNId}");

    public static IReadOnlyList<ReferenceDataCachePattern> ConfigurationDomainEntries(
        string normalizedAppDomainNId) =>
        [
            new("parameter", $"referencedata:v1:*:configuration:*:{normalizedAppDomainNId}:*",
                $"configuration:{normalizedAppDomainNId}"),
            new("parameter", $"referencedata:v1:*:configuration-domain:*:{normalizedAppDomainNId}:*",
                $"configuration-domain:{normalizedAppDomainNId}"),
        ];

    public static ReferenceDataCacheKey DynamicConfiguration(string tenantKey, string normalizedNId,
        string revision, string pageKey) =>
        new("dynamic-property", $"referencedata:dynamic-property:v1:{tenantKey}:dynamic-config:{normalizedNId}:{revision}:{pageKey}",
            normalizedNId);

    public static ReferenceDataCachePattern DynamicConfigurations(string normalizedNId) =>
        new("dynamic-property", $"referencedata:dynamic-property:v1:*:dynamic-config:{normalizedNId}:*",
            normalizedNId);

    public static ReferenceDataCacheKey Metadata(string tenantKey, string normalizedNId) =>
        new("metadata", $"referencedata:v1:{tenantKey}:metadata:{normalizedNId}", normalizedNId);

    public static ReferenceDataCachePattern MetadataEntries(string normalizedNId) =>
        new("metadata", $"referencedata:v1:*:metadata:{normalizedNId}", normalizedNId);

    public static ReferenceDataCacheKey CodingRule(string tenantKey, string normalizedNId) =>
        new("coding-rule", $"referencedata:v1:{tenantKey}:coding-rule:{normalizedNId}", normalizedNId);

    public static ReferenceDataCachePattern CodingRules(string normalizedNId) =>
        new("coding-rule", $"referencedata:v1:*:coding-rule:{normalizedNId}", normalizedNId);

    public static ReferenceDataCacheKey StateMachine(string sourceScope, string sourceTenantKey,
        string normalizedNId, string currentOrRevision) =>
        new("state-machine", $"referencedata:v1:{sourceScope}:{sourceTenantKey}:state-machine:{normalizedNId}:{currentOrRevision}",
            StateOrUnitGenerationScope(sourceScope, sourceTenantKey, normalizedNId, currentOrRevision));

    public static ReferenceDataCachePattern StateMachineState(string sourceScope, string sourceTenantKey,
        string normalizedNId, string currentOrRevision) =>
        new("state-machine", $"referencedata:v1:{sourceScope}:{sourceTenantKey}:state-machine:{normalizedNId}:{currentOrRevision}",
            StateOrUnitGenerationScope(sourceScope, sourceTenantKey, normalizedNId, currentOrRevision));

    public static ReferenceDataCachePattern AvailableStateMachines() =>
        new("state-machine", "referencedata:v1:Effective:*:state-machine:list:*", "list");

    public static ReferenceDataCacheKey UnitOfMeasure(string sourceScope, string sourceTenantKey,
        string dimensionNId, string currentOrRevision) =>
        new("unit-of-measure", $"referencedata:v1:{sourceScope}:{sourceTenantKey}:unit-of-measure:{dimensionNId}:{currentOrRevision}",
            StateOrUnitGenerationScope(sourceScope, sourceTenantKey, dimensionNId, currentOrRevision));

    public static ReferenceDataCachePattern UnitOfMeasureState(string sourceScope, string sourceTenantKey,
        string dimensionNId, string currentOrRevision) =>
        new("unit-of-measure", $"referencedata:v1:{sourceScope}:{sourceTenantKey}:unit-of-measure:{dimensionNId}:{currentOrRevision}",
            StateOrUnitGenerationScope(sourceScope, sourceTenantKey, dimensionNId, currentOrRevision));

    public static ReferenceDataCachePattern AvailableUnits() =>
        new("unit-of-measure", "referencedata:v1:Effective:*:unit-of-measure:list:*", "list");

    public static string TenantKey(string? tenantNId) => string.IsNullOrWhiteSpace(tenantNId)
        ? "platform"
        : $"tenant-{Uri.EscapeDataString(tenantNId)}";

    public static string SourceTenantKey(string sourceScope, string? sourceTenantNId) =>
        sourceScope == "Platform" ? "platform" : TenantKey(sourceTenantNId);

    public static string PageKey(params string?[] parts)
    {
        var text = string.Join('\u001f', parts.Select(part => part ?? "\u0000"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static string StateOrUnitGenerationScope(string sourceScope, string sourceTenantKey,
        string normalizedNId, string currentOrRevision) =>
        sourceScope == "Effective" && normalizedNId == "list"
            ? "list"
            : $"{sourceScope}:{sourceTenantKey}:{normalizedNId}:{currentOrRevision}";
}

public static class ReferenceDataCacheExtensions
{
    public static async Task<T> GetOrLoadAsync<T>(this IReferenceDataCache cache,
        ReferenceDataCacheKey key, TimeSpan ttl, Func<Task<T>> load, CancellationToken cancellationToken)
        where T : class
    {
        var cached = await cache.GetAsync<T>(key, cancellationToken);
        if (cached is not null) return cached;
        var generation = await cache.GetGenerationAsync(key.GenerationKey, cancellationToken);
        var value = await load();
        await cache.SetIfGenerationAsync(key, value, ttl, generation, cancellationToken);
        return value;
    }

    public static async Task InvalidateAsync(this IReferenceDataCache cache,
        IEnumerable<ReferenceDataCachePattern> patterns, CancellationToken cancellationToken)
    {
        foreach (var pattern in patterns)
            await cache.InvalidateAsync(pattern, cancellationToken);
    }
}
