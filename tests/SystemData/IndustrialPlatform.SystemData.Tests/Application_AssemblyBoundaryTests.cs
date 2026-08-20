using IndustrialPlatform.SystemData.Application;

namespace IndustrialPlatform.SystemData.Application.Tests;

/// <summary>
/// 验证 SystemData.Application 程序集边界:可按预期名称加载。
/// (精确项目引用矩阵由 BuildingBlocks 架构测试锁定。)
/// </summary>
public sealed class AssemblyBoundaryTests
{
    [Fact]
    public void ApplicationAssemblyLoadsWithExpectedName()
    {
        Assert.Equal("IndustrialPlatform.SystemData.Application", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
