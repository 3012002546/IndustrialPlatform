using IndustrialPlatform.Collaboration.Api.Modules;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Api_HubAuthenticationTests
{
    [Theory]
    [InlineData("/hubs/collaboration-v1", "GET", true)]
    [InlineData("/collaboration/hubs/collaboration-v1", "GET", true)]
    [InlineData("/hubs/collaboration-v1/other", "GET", false)]
    [InlineData("/hubs/collaboration-v1", "POST", false)]
    [InlineData("/api/v1/users", "GET", false)]
    [InlineData("/collaboration/api/v1/users", "GET", false)]
    public async Task QueryToken_IsRestrictedToExactHubTransportPaths(string path, string method, bool accepted)
    {
        var services = new ServiceCollection();
        services.AddCollaborationModule(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var options = new JwtBearerOptions();
        var previousCalled = false;
        options.Events.OnMessageReceived = _ => { previousCalled = true; return Task.CompletedTask; };
        foreach (var configure in provider.GetServices<IPostConfigureOptions<JwtBearerOptions>>())
            configure.PostConfigure(JwtBearerDefaults.AuthenticationScheme, options);

        var http = new DefaultHttpContext();
        http.Request.Path = path;
        http.Request.Method = method;
        http.Request.QueryString = new QueryString("?access_token=test-token");
        var context = new MessageReceivedContext(http,
            new AuthenticationScheme("Bearer", null, typeof(JwtBearerHandler)), options);
        await options.Events.OnMessageReceived(context);
        Assert.True(previousCalled);
        Assert.Equal(accepted ? "test-token" : null, context.Token);

        // A query parameter must never replace an explicit header credential.
        context.Token = null;
        http.Request.Headers.Authorization = "Bearer header-token";
        await options.Events.OnMessageReceived(context);
        Assert.Null(context.Token);
    }
}
