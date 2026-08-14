using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;

/// <summary>
/// 种子声明集合校验(TASK-SD-004):唯一 SeedKey、依赖引用存在且无环、SecretBootstrap 不得
/// RequiredForReadiness。供 <see cref="DatabaseRegistration"/> 注册/重注册解析时复用。
/// 单条种子自身校验在 <see cref="SeedSet"/> 构造中完成(环境门禁/策略一致性)。
/// </summary>
internal static class SeedSetGuard
{
    /// <summary>校验集合;null 视为空,返回可枚举只读快照。</summary>
    public static IReadOnlyCollection<SeedSet> Validate(IReadOnlyCollection<SeedSet>? seedSets)
    {
        var seeds = seedSets ?? [];
        if (seeds.Count == 0)
        {
            return seeds.ToList();
        }

        var byKey = new Dictionary<string, SeedSet>(StringComparer.Ordinal);
        foreach (var seed in seeds)
        {
            if (!byKey.TryAdd(seed.SeedKey, seed))
            {
                throw new ValidationException($"种子键重复:{seed.SeedKey}。");
            }
        }

        foreach (var seed in seeds)
        {
            foreach (var dependency in seed.DependencySeedKeys)
            {
                if (!byKey.ContainsKey(dependency))
                {
                    throw new ValidationException($"种子 {seed.SeedKey} 依赖不存在的种子键:{dependency}。");
                }
            }
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seed in seeds)
        {
            Visit(seed.SeedKey, byKey, visited, new HashSet<string>(StringComparer.Ordinal));
        }

        foreach (var seed in seeds)
        {
            if (seed.SeedClass == SeedClass.SecretBootstrap && seed.RequiredForReadiness)
            {
                throw new ValidationException($"SecretBootstrap 种子 {seed.SeedKey} 不得声明 RequiredForReadiness。");
            }
        }

        return seeds.ToList();
    }

    private static void Visit(
        string key,
        IReadOnlyDictionary<string, SeedSet> byKey,
        HashSet<string> visited,
        HashSet<string> stack)
    {
        if (stack.Contains(key))
        {
            throw new ValidationException($"种子依赖存在环:{string.Join("→", stack)}→{key}。");
        }

        if (!visited.Add(key))
        {
            return;
        }

        stack.Add(key);
        if (byKey.TryGetValue(key, out var seed))
        {
            foreach (var dependency in seed.DependencySeedKeys)
            {
                Visit(dependency, byKey, visited, stack);
            }
        }

        stack.Remove(key);
    }
}
