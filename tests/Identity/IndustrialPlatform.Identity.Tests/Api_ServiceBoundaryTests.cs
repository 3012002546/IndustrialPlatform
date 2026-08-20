using System.Reflection;

namespace IndustrialPlatform.Identity.Api.Tests;

/// <summary>
/// 验证 Identity 五层程序集均可按预期名称加载(Contracts 自 TASK-ID-001 起纳入边界)。
/// </summary>
public sealed class ServiceBoundaryTests
{
    [Theory]
    [InlineData("IndustrialPlatform.Identity.Contracts")]
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
