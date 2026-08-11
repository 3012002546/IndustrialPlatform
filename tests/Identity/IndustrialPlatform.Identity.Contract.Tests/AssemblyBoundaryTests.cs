using IndustrialPlatform.Identity.Contracts;

namespace IndustrialPlatform.Identity.Contract.Tests;

/// <summary>
/// 验证 Identity.Contracts 程序集边界:可按预期名称加载。
/// Contracts 零业务引用;精确项目引用矩阵由 BuildingBlocks 架构测试锁定。
/// </summary>
public sealed class AssemblyBoundaryTests
{
    [Fact]
    public void ContractsAssemblyLoadsWithExpectedName()
    {
        Assert.Equal("IndustrialPlatform.Identity.Contracts", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
