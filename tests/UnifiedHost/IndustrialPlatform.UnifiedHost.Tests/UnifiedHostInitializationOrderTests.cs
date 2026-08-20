using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Initialization;
using IndustrialPlatform.UnifiedHost;

namespace IndustrialPlatform.UnifiedHost.Tests;

public sealed class UnifiedHostInitializationOrderTests
{
    [Fact]
    public async Task UnifiedHost_runs_initializers_in_identity_systemdata_referencedata_order()
    {
        var calls = new List<string>();
        var initializers = new IServiceInitializer[]
        {
            new RecordingInitializer("referencedata", calls),
            new RecordingInitializer("systemdata", calls),
            new RecordingInitializer("identity", calls),
        };

        var invoker = new InProcessServiceInitializationInvoker(initializers);

        await ModuleMigrationCoordinatorHostedService.RunInitializersAsync(
            invoker,
            initializers,
            CreateContext(),
            CancellationToken.None);

        Assert.Equal(
            [
                "inspect:identity", "plan:identity", "apply:identity", "verify:identity",
                "inspect:systemdata", "plan:systemdata", "apply:systemdata", "verify:systemdata",
                "inspect:referencedata", "plan:referencedata", "apply:referencedata", "verify:referencedata",
            ],
            calls);
    }

    [Fact]
    public async Task UnifiedHost_fails_when_apply_succeeds_but_verify_is_not_ready()
    {
        var calls = new List<string>();
        var initializers = new IServiceInitializer[]
        {
            new RecordingInitializer("identity", calls),
            new RecordingInitializer("systemdata", calls),
            new RecordingInitializer("referencedata", calls, verifyReady: false),
        };
        var invoker = new InProcessServiceInitializationInvoker(initializers);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ModuleMigrationCoordinatorHostedService.RunInitializersAsync(
                invoker,
                initializers,
                CreateContext(),
                CancellationToken.None));

        Assert.Contains("referencedata", exception.Message, StringComparison.Ordinal);
        Assert.Contains("verify failed", exception.Message, StringComparison.Ordinal);
        Assert.Equal("verify failed", exception.Data["Reason"]);
    }

    [Fact]
    public async Task UnifiedHost_second_coordination_does_not_reapply_service_owned_target_versions()
    {
        var calls = new List<string>();
        var initializers = new IServiceInitializer[]
        {
            new RecordingInitializer("referencedata", calls),
            new RecordingInitializer("systemdata", calls),
            new RecordingInitializer("identity", calls),
        };
        var invoker = new InProcessServiceInitializationInvoker(initializers);

        await ModuleMigrationCoordinatorHostedService.RunInitializersAsync(
            invoker,
            initializers,
            CreateContext() with { OperationNId = "operation-1" },
            CancellationToken.None);
        await ModuleMigrationCoordinatorHostedService.RunInitializersAsync(
            invoker,
            initializers,
            CreateContext() with { OperationNId = "operation-2" },
            CancellationToken.None);

        Assert.All(initializers, initializer =>
            Assert.Equal(1, ((RecordingInitializer)initializer).ApplyCount));
    }

    [Fact]
    public async Task UnifiedHost_rejects_ready_verify_with_observed_version_different_from_explicit_target()
    {
        var calls = new List<string>();
        var initializers = new IServiceInitializer[]
        {
            new RecordingInitializer("identity", calls),
            new RecordingInitializer("systemdata", calls),
            new RecordingInitializer("referencedata", calls),
        };
        var invoker = new InProcessServiceInitializationInvoker(initializers);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ModuleMigrationCoordinatorHostedService.RunInitializersAsync(
                invoker,
                initializers,
                CreateContext("external-target-v1"),
                CancellationToken.None));

        Assert.Contains("ObservedVersion", exception.Message, StringComparison.Ordinal);
        Assert.Contains("external-target-v1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("external-target-v1", exception.Data["Reason"]?.ToString(), StringComparison.Ordinal);
    }

    private static ServiceInitializationContext CreateContext(string desiredVersion = "") => new(
        "Test",
        "tenant-1",
        "operation-1",
        "unifiedhost",
        "unifiedhost",
        new ResolvedDatabaseTarget(
            "Test",
            DatabaseTopologyMode.Shared,
            "unifiedhost",
            DatabaseProvider.Sqlite,
            "unifiedhost_db",
            "target",
            true),
        desiredVersion,
        ServiceInitializationPolicy.Standard,
        "trace-1");

    private sealed class RecordingInitializer(
        string serviceKey,
        List<string> calls,
        bool verifyReady = true) : IServiceInitializer
    {
        private bool _applied;

        public string ServiceKey => serviceKey;
        public string ModuleKey => serviceKey;
        public int ApplyCount { get; private set; }

        private string CurrentVersion => serviceKey switch
        {
            "identity" => "ID-020-01",
            "systemdata" => "SDM-006-01",
            "referencedata" => "reference-data-baseline-v1",
            _ => throw new InvalidOperationException($"unknown service: {serviceKey}"),
        };

        public Task<ServiceInitializationState> InspectAsync(ServiceInitializationContext context, CancellationToken cancellationToken)
        {
            calls.Add($"inspect:{serviceKey}");
            return Task.FromResult(new ServiceInitializationState(
                serviceKey,
                serviceKey,
                _applied ? CurrentVersion : null,
                _applied,
                _applied,
                _applied,
                _applied,
                _applied ? null : "not initialized"));
        }

        public Task<ServiceInitializationPlan> PlanAsync(ServiceInitializationContext context, ServiceInitializationState inspection, CancellationToken cancellationToken)
        {
            calls.Add($"plan:{serviceKey}");
            var desiredVersion = string.IsNullOrWhiteSpace(context.DesiredVersion)
                ? CurrentVersion
                : context.DesiredVersion;
            return Task.FromResult(new ServiceInitializationPlan(
                serviceKey,
                serviceKey,
                inspection.ObservedVersion,
                desiredVersion,
                !inspection.Ready || !string.Equals(inspection.ObservedVersion, desiredVersion, StringComparison.Ordinal),
                inspection.Ready ? [] : ["migration"]));
        }

        public Task<ServiceInitializationState> ApplyAsync(ServiceInitializationContext context, ServiceInitializationPlan plan, CancellationToken cancellationToken)
        {
            calls.Add(serviceKey);
            calls[^1] = $"apply:{serviceKey}";
            ApplyCount++;
            _applied = true;
            return Task.FromResult(new ServiceInitializationState(serviceKey, serviceKey, CurrentVersion, true, true, true, true, null));
        }

        public Task<ServiceInitializationState> VerifyAsync(ServiceInitializationContext context, CancellationToken cancellationToken)
        {
            calls.Add($"verify:{serviceKey}");
            return Task.FromResult(new ServiceInitializationState(
                serviceKey,
                serviceKey,
                _applied ? CurrentVersion : null,
                _applied && verifyReady,
                _applied && verifyReady,
                _applied && verifyReady,
                _applied && verifyReady,
                _applied && verifyReady ? null : "verify failed"));
        }
    }
}
