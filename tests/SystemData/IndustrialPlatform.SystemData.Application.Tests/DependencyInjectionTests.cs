using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialPlatform.SystemData.Application.Tests;

/// <summary>
/// 应用层 DI 扩展点冒烟测试:TASK-SD-001 阶段为空扩展,验证可正常调用并返回同一集合。
/// </summary>
public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddSystemDataApplication_ReturnsSameCollectionWithoutException()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var result = DependencyInjection.AddSystemDataApplication(services, configuration);

        Assert.Same(services, result);
    }

    [Fact]
    public void AddSystemDataApplication_NullServices_ThrowsArgumentNullException()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Throws<ArgumentNullException>(() =>
            DependencyInjection.AddSystemDataApplication(null!, configuration));
    }
}
