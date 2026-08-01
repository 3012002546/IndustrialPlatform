using System.Reflection;

namespace IndustrialPlatform.ReferenceData.Tests;

public sealed class ServiceBoundaryTests
{
    [Theory]
    [InlineData("IndustrialPlatform.ReferenceData.Domain")]
    [InlineData("IndustrialPlatform.ReferenceData.Application")]
    [InlineData("IndustrialPlatform.ReferenceData.Infrastructure")]
    [InlineData("IndustrialPlatform.ReferenceData.Api")]
    public void LoadsExpectedReferenceDataAssembly(string assemblyName)
    {
        var assembly = Assembly.Load(new AssemblyName(assemblyName));

        Assert.Equal(assemblyName, assembly.GetName().Name);
    }
}
