using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Identity.Infrastructure.Bootstrap;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Seeds;

namespace IndustrialPlatform.Identity.Infrastructure.Tests;

public sealed class IdentityServiceInitializerReadinessTests
{
    [Fact]
    public async Task Ready_service_does_not_require_systemdata_to_be_online()
    {
        var constructor = typeof(IdentityServiceInitializer).GetConstructors().Single();

        Assert.DoesNotContain(
            constructor.GetParameters(),
            parameter => parameter.ParameterType.FullName?.Contains("SystemData", StringComparison.Ordinal) == true);

        var fake = new FakeBootstrapService
        {
            Readiness = new IdentityReadinessResult(
                "identity",
                "identity",
                "identity_db",
                "identity-v1",
                BootstrapState.Ready,
                true,
                true,
                true,
                true,
                null,
                [
                    new SeedVersionStatus(BootstrapSeedCatalog.SystemCatalogSeedKey, BootstrapSeedCatalog.SeedVersion, "Applied"),
                    new SeedVersionStatus(BootstrapSeedCatalog.TenantSecuritySeedKey, BootstrapSeedCatalog.SeedVersion, "Applied"),
                ]),
        };
        var initializer = new IdentityServiceInitializer(null!, fake);

        var state = await initializer.InspectAsync(CreateContext(), CancellationToken.None);

        Assert.True(state.Ready);
        Assert.Equal(1, fake.ReadinessCallCount);
    }

    [Fact]
    public async Task Inspect_maps_only_missing_local_table_to_not_ready()
    {
        var fake = new FakeBootstrapService
        {
            Failure = new InvalidOperationException("SQLite Error 1: no such table: identity_seed_ledger"),
        };
        var initializer = new IdentityServiceInitializer(null!, fake);

        var state = await initializer.InspectAsync(CreateContext(), CancellationToken.None);

        Assert.False(state.Ready);
        Assert.Contains("账本尚未创建", state.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inspect_propagates_unexpected_failure()
    {
        var fake = new FakeBootstrapService
        {
            Failure = new InvalidOperationException("connection refused"),
        };
        var initializer = new IdentityServiceInitializer(null!, fake);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            initializer.InspectAsync(CreateContext(), CancellationToken.None));

        Assert.Equal("connection refused", exception.Message);
    }

    [Fact]
    public async Task Standard_inspect_does_not_require_bootstrap_admin()
    {
        var fake = new FakeBootstrapService
        {
            Readiness = new IdentityReadinessResult(
                "identity",
                "identity",
                "identity_db",
                "identity-v1",
                BootstrapState.Pending,
                true,
                true,
                false,
                false,
                "admin pending",
                [
                    new SeedVersionStatus(BootstrapSeedCatalog.SystemCatalogSeedKey, BootstrapSeedCatalog.SeedVersion, "Applied"),
                    new SeedVersionStatus(BootstrapSeedCatalog.TenantSecuritySeedKey, BootstrapSeedCatalog.SeedVersion, "Applied"),
                ]),
        };
        var initializer = new IdentityServiceInitializer(null!, fake);

        var state = await initializer.InspectAsync(CreateContext(), CancellationToken.None);

        Assert.True(state.Ready);
        Assert.True(state.BootstrapReady);
        Assert.Null(state.Reason);
    }

    private static ServiceInitializationContext CreateContext() => new(
        "Test",
        "tenant-1",
        "operation-1",
        "identity",
        "identity",
        new IndustrialPlatform.SharedKernel.Topology.ResolvedDatabaseTarget(
            "Test",
            IndustrialPlatform.SharedKernel.Topology.DatabaseTopologyMode.Shared,
            "identity",
            IndustrialPlatform.SharedKernel.Topology.DatabaseProvider.Sqlite,
            "identity_db",
            "target",
            false),
        "identity-v1",
        ServiceInitializationPolicy.Standard,
        "trace-1");

    private sealed class FakeBootstrapService : IBootstrapService
    {
        public IdentityReadinessResult Readiness { get; set; } = new(
            "identity", "identity", "identity_db", string.Empty, BootstrapState.Pending, false, false, false, false, "pending", []);

        public Exception? Failure { get; set; }
        public int ReadinessCallCount { get; private set; }

        public Task<BootstrapStatusResult> GetStatusAsync(string tenantNId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BootstrapStatusResult(BootstrapState.Pending, string.Empty, [], false, false, false));

        public Task<IdentityReadinessResult> GetReadinessAsync(string tenantNId, CancellationToken cancellationToken = default)
        {
            ReadinessCallCount++;
            return Failure is null ? Task.FromResult(Readiness) : Task.FromException<IdentityReadinessResult>(Failure);
        }

        public Task<BootstrapRecoveryResult> RecoverAdminAsync(
            string tenantNId,
            string recoveryReference,
            string approvalReference,
            CancellationToken cancellationToken = default) =>
            Task.FromException<BootstrapRecoveryResult>(new InvalidOperationException("not used"));
    }
}
