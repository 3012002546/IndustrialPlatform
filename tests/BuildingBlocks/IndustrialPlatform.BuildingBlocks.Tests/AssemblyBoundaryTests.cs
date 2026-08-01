using System.Reflection;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class AssemblyBoundaryTests
{
    [Theory]
    [InlineData("IndustrialPlatform.SharedKernel")]
    [InlineData("IndustrialPlatform.Application.Abstractions")]
    [InlineData("IndustrialPlatform.Infrastructure")]
    [InlineData("IndustrialPlatform.EventBus")]
    [InlineData("IndustrialPlatform.Logging")]
    [InlineData("IndustrialPlatform.Web")]
    [InlineData("IndustrialPlatform.Security")]
    public void LoadsExpectedBuildingBlockAssembly(string assemblyName)
    {
        var assembly = Assembly.Load(new AssemblyName(assemblyName));

        Assert.Equal(assemblyName, assembly.GetName().Name);
    }
}
