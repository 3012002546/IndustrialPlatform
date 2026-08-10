using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.Gateway.Tests;

/// <summary>
/// 测试用下游桩服务:真实监听随机端口,供 Gateway 转发目标使用。
/// </summary>
public sealed class DownstreamStub : IAsyncDisposable
{
    private readonly WebApplication _app;

    private DownstreamStub(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    /// <summary>桩服务基地址,如 http://127.0.0.1:xxxxx。</summary>
    public string BaseUrl { get; }

    /// <summary>启动桩服务,监听回环随机端口。</summary>
    public static async Task<DownstreamStub> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));

        var app = builder.Build();

        app.MapGet("/health/ready", (HttpContext context) =>
            Results.Json(new { status = "Healthy", receivedPath = context.Request.Path.Value }));

        app.MapGet("/slow", async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            return Results.Ok(new { done = true });
        });

        await app.StartAsync();

        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
        return new DownstreamStub(app, addresses.First());
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _app.DisposeAsync();

    /// <summary>获取一个当前空闲的回环端口(不占用,供不可达目标测试使用)。</summary>
    public static int GetUnusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
