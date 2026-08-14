using IndustrialPlatform.SystemData.Contracts;

namespace IndustrialPlatform.SystemData.Contract.Tests;

/// <summary>
/// 验证 SystemData.Contracts 程序集边界:可按预期名称加载。
/// Contracts 零业务引用(TASK-SD-001);精确项目引用矩阵由 BuildingBlocks 架构测试锁定。
/// </summary>
public sealed class AssemblyBoundaryTests
{
    [Fact]
    public void ContractsAssemblyLoadsWithExpectedName()
    {
        Assert.Equal("IndustrialPlatform.SystemData.Contracts", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
