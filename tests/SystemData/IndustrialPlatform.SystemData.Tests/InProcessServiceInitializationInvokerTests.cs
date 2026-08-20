using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Initialization;

namespace IndustrialPlatform.SystemData.Infrastructure.Tests;

public sealed class InProcessServiceInitializationInvokerTests
{
    [Fact]
    public async Task Replayed_operation_id_is_idempotent()
    {
        var initializer = new RecordingInitializer();
        var invoker = new InProcessServiceInitializationInvoker([initializer]);
        var context = CreateContext("operation-1");
        var plan = new ServiceInitializationPlan(
            "identity",
            "identity",
            null,
            "identity-v1",
            true,
            ["migration", "verify"]);

        var first = await invoker.ApplyAsync(context, plan, CancellationToken.None);
        var replay = await invoker.ApplyAsync(context, plan, CancellationToken.None);

        Assert.Equal(first, replay);
        Assert.Equal(1, initializer.ApplyCount);
    }

    [Fact]
    public async Task Concurrent_replays_for_one_operation_id_are_single_flight()
    {
        var initializer = new RecordingInitializer { ApplyDelay = TimeSpan.FromMilliseconds(50) };
        var invoker = new InProcessServiceInitializationInvoker([initializer]);
        var context = CreateContext("operation-concurrent");
        var plan = new ServiceInitializationPlan(
            "identity",
            "identity",
            null,
            "identity-v1",
            true,
            ["migration"]);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => invoker.ApplyAsync(context, plan, CancellationToken.None)));

        Assert.All(results, result => Assert.True(result.Ready));
        Assert.All(results, result => Assert.Equal(results[0], result));
        Assert.Equal(1, initializer.ApplyCount);
    }

    private static ServiceInitializationContext CreateContext(string operationNId) => new(
        "Test",
        "tenant-1",
        operationNId,
        "identity",
        "identity",
        new ResolvedDatabaseTarget(
            "Test",
            DatabaseTopologyMode.Shared,
            "identity",
            DatabaseProvider.Sqlite,
            "identity_db",
            "target",
            false),
        "identity-v1",
        ServiceInitializationPolicy.Standard,
        "trace-1");

    private sealed class RecordingInitializer : IServiceInitializer
    {
        private int _applyCount;

        public int ApplyCount => _applyCount;
        public TimeSpan ApplyDelay { get; init; }

        public string ServiceKey => "identity";
        public string ModuleKey => "identity";

        public Task<ServiceInitializationState> InspectAsync(ServiceInitializationContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceInitializationState("identity", "identity", null, false, false, false, false, "not initialized"));

        public Task<ServiceInitializationPlan> PlanAsync(ServiceInitializationContext context, ServiceInitializationState inspection, CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceInitializationPlan("identity", "identity", null, "identity-v1", true, ["migration"]));

        public Task<ServiceInitializationState> ApplyAsync(ServiceInitializationContext context, ServiceInitializationPlan plan, CancellationToken cancellationToken)
        {
            return ApplyCoreAsync(cancellationToken);
        }

        private async Task<ServiceInitializationState> ApplyCoreAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _applyCount);
            await Task.Delay(ApplyDelay, cancellationToken);
            return new ServiceInitializationState("identity", "identity", "identity-v1", true, true, true, true, null);
        }

        public Task<ServiceInitializationState> VerifyAsync(ServiceInitializationContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceInitializationState("identity", "identity", "identity-v1", true, true, true, true, null));
    }
}
