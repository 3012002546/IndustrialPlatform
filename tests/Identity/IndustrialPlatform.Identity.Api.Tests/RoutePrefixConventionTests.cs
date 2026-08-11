using System.Reflection;
using IndustrialPlatform.Identity.Api.Conventions;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace IndustrialPlatform.Identity.Api.Tests;

/// <summary>
/// 验证 <see cref="RoutePrefixConvention"/> 为控制器路由统一添加 <c>api/v1</c> 前缀。
/// </summary>
public sealed class RoutePrefixConventionTests
{
    [Fact]
    public void Apply_PrependsApiV1PrefixToAttributeRoute()
    {
        var controller = CreateController(template: "users");

        Apply(controller);

        Assert.Equal("api/v1/users", controller.Selectors[0].AttributeRouteModel!.Template);
    }

    [Fact]
    public void Apply_StripsLeadingSlashToAvoidDoubleSlash()
    {
        var controller = CreateController(template: "/users");

        Apply(controller);

        Assert.Equal("api/v1/users", controller.Selectors[0].AttributeRouteModel!.Template);
    }

    [Fact]
    public void Apply_AssignsPrefixWhenControllerHasNoAttributeRoute()
    {
        var controller = new ControllerModel(typeof(FakeController).GetTypeInfo(), Array.Empty<object>());
        controller.Selectors.Add(new SelectorModel());

        Apply(controller);

        Assert.Equal("api/v1", controller.Selectors[0].AttributeRouteModel!.Template);
    }

    private static void Apply(ControllerModel controller)
    {
        var application = new ApplicationModel();
        application.Controllers.Add(controller);
        new RoutePrefixConvention().Apply(application);
    }

    private static ControllerModel CreateController(string? template)
    {
        var controller = new ControllerModel(typeof(FakeController).GetTypeInfo(), Array.Empty<object>());
        controller.Selectors.Add(new SelectorModel
        {
            AttributeRouteModel = template is null
                ? null
                : new AttributeRouteModel { Template = template },
        });
        return controller;
    }

    private sealed class FakeController
    {
    }
}
