using System.Collections.Concurrent;
using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Initialization;

namespace IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Initialization;

/// <summary>统一部署中的进程内初始化适配器。</summary>
public sealed class InProcessServiceInitializationInvoker : IServiceInitializationInvoker
{
    private readonly Dictionary<string, IServiceInitializer> _initializers;
    private readonly ConcurrentDictionary<string, Lazy<Task<ServiceInitializationState>>> _appliedOperations = new(StringComparer.Ordinal);

    public InProcessServiceInitializationInvoker(IEnumerable<IServiceInitializer> initializers)
    {
        ArgumentNullException.ThrowIfNull(initializers);
        _initializers = initializers.ToDictionary(Key, StringComparer.OrdinalIgnoreCase);
    }

    public Task<ServiceInitializationState> InspectAsync(ServiceInitializationContext context, CancellationToken cancellationToken) =>
        Resolve(context).InspectAsync(context, cancellationToken);

    public Task<ServiceInitializationPlan> PlanAsync(
        ServiceInitializationContext context,
        ServiceInitializationState inspection,
        CancellationToken cancellationToken) =>
        Resolve(context).PlanAsync(context, inspection, cancellationToken);

    public Task<ServiceInitializationState> ApplyAsync(
        ServiceInitializationContext context,
        ServiceInitializationPlan plan,
        CancellationToken cancellationToken)
    {
        var key = $"{context.OperationNId}|{context.ServiceKey}|{context.ModuleKey}|apply";
        var lazy = new Lazy<Task<ServiceInitializationState>>(
            () => ApplyOnceAsync(Resolve(context), context, plan, cancellationToken),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var existing = _appliedOperations.GetOrAdd(key, lazy);
        return AwaitAndEvictOnFailureAsync(key, existing);
    }

    public Task<ServiceInitializationState> VerifyAsync(ServiceInitializationContext context, CancellationToken cancellationToken) =>
        Resolve(context).VerifyAsync(context, cancellationToken);

    private static async Task<ServiceInitializationState> ApplyOnceAsync(
        IServiceInitializer initializer,
        ServiceInitializationContext context,
        ServiceInitializationPlan plan,
        CancellationToken cancellationToken)
    {
        return await initializer.ApplyAsync(context, plan, cancellationToken);
    }

    private async Task<ServiceInitializationState> AwaitAndEvictOnFailureAsync(
        string key,
        Lazy<Task<ServiceInitializationState>> operation)
    {
        try
        {
            var state = await operation.Value;
            if (!state.Ready)
            {
                // NotReady 不是可重放的成功结果；允许下一次调用重新尝试。
                EvictIfSame(key, operation);
            }

            return state;
        }
        catch
        {
            EvictIfSame(key, operation);
            throw;
        }
    }

    private void EvictIfSame(string key, Lazy<Task<ServiceInitializationState>> operation) =>
        ((ICollection<KeyValuePair<string, Lazy<Task<ServiceInitializationState>>>>)_appliedOperations)
            .Remove(new KeyValuePair<string, Lazy<Task<ServiceInitializationState>>>(key, operation));

    private IServiceInitializer Resolve(ServiceInitializationContext context)
    {
        if (_initializers.TryGetValue(Key(context.ServiceKey, context.ModuleKey), out var initializer))
        {
            return initializer;
        }

        throw new InvalidOperationException(
            $"未注册服务初始化器: {context.ServiceKey}/{context.ModuleKey}。");
    }

    private static string Key(IServiceInitializer initializer) => Key(initializer.ServiceKey, initializer.ModuleKey);

    private static string Key(string serviceKey, string moduleKey) => $"{serviceKey}|{moduleKey}";
}
