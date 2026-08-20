using IndustrialPlatform.SystemData.Domain;

namespace IndustrialPlatform.SystemData.Domain.Tests;

/// <summary>
/// 验证 SystemData.Domain 程序集边界:可按预期名称加载。
/// (精确项目引用矩阵由 BuildingBlocks 架构测试锁定。)
/// </summary>
public sealed class AssemblyBoundaryTests
{
    [Fact]
    public void DomainAssemblyLoadsWithExpectedName()
    {
        Assert.Equal("IndustrialPlatform.SystemData.Domain", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
