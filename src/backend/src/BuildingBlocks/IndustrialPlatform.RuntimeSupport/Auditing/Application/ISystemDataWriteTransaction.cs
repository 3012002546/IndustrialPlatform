namespace IndustrialPlatform.SystemData.Application.Auditing;

/// <summary>管理写入与本地审计的同库事务边界。</summary>
public interface ISystemDataWriteTransaction
{
    Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken);
}

/// <summary>测试替身/无持久化宿主的直接执行实现。</summary>
public sealed class NoopSystemDataWriteTransaction : ISystemDataWriteTransaction
{
    public Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken) => action();
}
