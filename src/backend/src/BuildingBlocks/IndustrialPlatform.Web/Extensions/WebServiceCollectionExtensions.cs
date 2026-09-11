using IndustrialPlatform.Web.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Web 基础组件依赖注入扩展。
/// </summary>
public static class WebServiceCollectionExtensions
{
    /// <summary>
    /// 注册控制器并全局应用 <see cref="ResultFilter"/> 结果包装。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configureOptions">可选的 MVC 配置回调。</param>
    /// <returns>MVC 构建器。</returns>
    public static IMvcBuilder AddIndustrialApi(this IServiceCollection services, Action<MvcOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var mvc = services.AddControllers(options =>
        {
            options.Filters.Add<ResultFilter>();
            configureOptions?.Invoke(options);
        });
        // OData is used only as an input parser. Its convention-only metadata
        // controller would otherwise be discovered and mapped to the global API root.
        foreach (var part in mvc.PartManager.ApplicationParts.OfType<AssemblyPart>()
            .Where(part => part.Assembly == typeof(MetadataController).Assembly).ToArray())
        {
            mvc.PartManager.ApplicationParts.Remove(part);
        }
        return mvc;
    }
}
