using IndustrialPlatform.Web.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class WebRegistrationTests
{
    [Fact]
    public void AddIndustrialApi_AddsResultFilterToMvcOptions()
    {
        var services = new ServiceCollection();
        services.AddIndustrialApi();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MvcOptions>>().Value;

        Assert.Contains(options.Filters, filter => filter is TypeFilterAttribute filterAttribute && filterAttribute.ImplementationType == typeof(ResultFilter));
    }
}
