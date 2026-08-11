using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace IndustrialPlatform.Identity.Api.Conventions;

/// <summary>
/// 为所有 MVC 控制器路由统一添加全局前缀(默认 <c>api/v1</c>)。
/// 网关已剥离 <c>/identity</c> 前缀,因此 Identity 内部路由形如 <c>api/v1/auth/login</c>。
/// </summary>
public sealed class RoutePrefixConvention : IApplicationModelConvention
{
    private readonly string _prefix;

    /// <summary>
    /// 初始化路由前缀约定。
    /// </summary>
    /// <param name="prefix">全局路由前缀,默认 <c>api/v1</c>。</param>
    public RoutePrefixConvention(string prefix = "api/v1")
    {
        _prefix = prefix;
    }

    /// <inheritdoc />
    public void Apply(ApplicationModel application)
    {
        ArgumentNullException.ThrowIfNull(application);

        foreach (var controller in application.Controllers)
        {
            foreach (var selector in controller.Selectors)
            {
                var attributeRoute = selector.AttributeRouteModel;
                if (attributeRoute is null)
                {
                    selector.AttributeRouteModel = new AttributeRouteModel { Template = _prefix };
                    continue;
                }

                attributeRoute.Template = string.IsNullOrWhiteSpace(attributeRoute.Template)
                    ? _prefix
                    : $"{_prefix}/{attributeRoute.Template.TrimStart('/')}";
            }
        }
    }
}
