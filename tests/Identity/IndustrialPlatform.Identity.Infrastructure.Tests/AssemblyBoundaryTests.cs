using IndustrialPlatform.Identity.Infrastructure;

namespace IndustrialPlatform.Identity.Infrastructure.Tests;

/// <summary>
/// 验证 Identity.Infrastructure 程序集边界:可按预期名称加载。
/// (精确项目引用矩阵由 BuildingBlocks 架构测试锁定。)
/// </summary>
public sealed class AssemblyBoundaryTests
{
    [Fact]
    public void InfrastructureAssemblyLoadsWithExpectedName()
    {
        Assert.Equal("IndustrialPlatform.Identity.Infrastructure", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
