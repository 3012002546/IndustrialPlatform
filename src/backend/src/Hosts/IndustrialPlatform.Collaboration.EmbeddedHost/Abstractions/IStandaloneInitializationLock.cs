using IndustrialPlatform.SharedKernel.Topology;

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Abstractions;

public interface IStandaloneInitializationLock
{
    Task<IAsyncDisposable> AcquireAsync(ResolvedDatabaseTarget target, CancellationToken cancellationToken);
}
