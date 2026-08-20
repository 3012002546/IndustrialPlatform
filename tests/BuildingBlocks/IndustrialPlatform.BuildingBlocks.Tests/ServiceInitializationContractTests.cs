using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.Application.Abstractions.Initialization;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class ServiceInitializationContractTests
{
    [Fact]
    public void Service_initializer_context_contains_no_connection_password_or_seed_secret()
    {
        var context = new ServiceInitializationContext(
            "Test",
            "tenant-1",
            "operation-1",
            "identity",
            "identity",
            new ResolvedDatabaseTarget(
                "Test",
                DatabaseTopologyMode.Shared,
                "identity",
                DatabaseProvider.Sqlite,
                "identity_db",
                "test-target",
                false),
            "identity-v1",
            ServiceInitializationPolicy.Standard,
            "trace-1");

        var propertyNames = typeof(ServiceInitializationContext)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(propertyNames, name =>
            name.Contains("password", StringComparison.OrdinalIgnoreCase)
            || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || name.Contains("connectionstring", StringComparison.OrdinalIgnoreCase)
            || name.Contains("path", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("password", context.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("seed-secret", context.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
