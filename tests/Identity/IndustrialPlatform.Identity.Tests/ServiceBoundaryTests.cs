using System.Reflection;

namespace IndustrialPlatform.Identity.Tests;

public sealed class ServiceBoundaryTests
{
    [Theory]
    [InlineData("IndustrialPlatform.Identity.Domain")]
    [InlineData("IndustrialPlatform.Identity.Application")]
    [InlineData("IndustrialPlatform.Identity.Infrastructure")]
    [InlineData("IndustrialPlatform.Identity.Api")]
    public void LoadsExpectedIdentityAssembly(string assemblyName)
    {
        var assembly = Assembly.Load(new AssemblyName(assemblyName));

        Assert.Equal(assemblyName, assembly.GetName().Name);
    }
}
