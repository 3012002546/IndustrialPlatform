using IndustrialPlatform.Gateway.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialPlatform.Gateway.Tests;

public sealed class ConfigBindingTests
{
    [Fact]
    public void DevelopmentConfigurationBindsGatewayOptions()
    {
        using var factory = new WebApplicationFactory<Program>();

        var configuration = factory.Services.GetRequiredService<IConfiguration>();
        var options = configuration.GetSection(GatewayOptions.SectionName).Get<GatewayOptions>();

        Assert.NotNull(options);
        Assert.Equal(10, options.RequestTimeoutSeconds);
        Assert.Contains("http://localhost:5173", options.Cors.AllowedOrigins);

        var identity = Assert.Single(options.Services, service => service.Name == "identity");
        Assert.Equal("/identity", identity.PathPrefix);
        Assert.Equal("http://localhost:5041", identity.DestinationUrl);

        var referenceData = Assert.Single(options.Services, service => service.Name == "referencedata");
        Assert.Equal("/referencedata", referenceData.PathPrefix);
    }

    [Fact]
    public void RouteFactoryBuildsRoutesWithPrefixRemovalAndClusterTimeout()
    {
        var options = new GatewayOptions
        {
            RequestTimeoutSeconds = 5,
            Services = [new GatewayServiceOptions { Name = "identity", PathPrefix = "/identity", DestinationUrl = "http://localhost:5041" }],
        };

        var routes = GatewayRouteFactory.BuildRoutes(options);
        var clusters = GatewayRouteFactory.BuildClusters(options);

        var route = Assert.Single(routes);
        Assert.Equal("identity-route", route.RouteId);
        Assert.Equal("identity-cluster", route.ClusterId);
        Assert.Equal("/identity/{**catch-all}", route.Match.Path);
        Assert.Equal("PathRemovePrefix", route.Transforms!.Single().Keys.Single());

        var cluster = Assert.Single(clusters);
        Assert.Equal("identity-cluster", cluster.ClusterId);
        Assert.Equal("http://localhost:5041", cluster.Destinations!.Single().Value.Address);
        Assert.Equal(TimeSpan.FromSeconds(5), cluster.HttpRequest!.ActivityTimeout.GetValueOrDefault());
    }

    [Fact]
    public void RouteFactorySkipsInvalidServices()
    {
        var options = new GatewayOptions
        {
            Services =
            [
                new GatewayServiceOptions { Name = "ok", PathPrefix = "/ok", DestinationUrl = "http://localhost:1" },
                new GatewayServiceOptions { Name = "bad", PathPrefix = string.Empty, DestinationUrl = string.Empty },
            ],
        };

        var routes = GatewayRouteFactory.BuildRoutes(options);

        var route = Assert.Single(routes);
        Assert.Equal("ok-route", route.RouteId);
    }
}
