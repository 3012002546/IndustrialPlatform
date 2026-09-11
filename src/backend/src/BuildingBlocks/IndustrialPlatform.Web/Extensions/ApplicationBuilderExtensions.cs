using IndustrialPlatform.Web.Middleware;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Web 基础组件应用管道扩展。
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// 依次接入请求日志与全局异常处理中间件(日志在外、异常处理在内,
    /// 使日志可观察到异常处理后归一化的状态码)。
    /// </summary>
    /// <param name="app">应用构建器。</param>
    /// <returns>应用构建器。</returns>
    public static IApplicationBuilder UseIndustrialWeb(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Internal PF05 endpoints validate the exact request body after MVC model binding.
        // Buffer only requests carrying the service assertion so the validator can rewind
        // the body without imposing a memory cost on ordinary API traffic.
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers.ContainsKey("X-Industrial-Service-Assertion"))
                context.Request.EnableBuffering();
            await next();
        });
        app.UseMiddleware<RequestLoggingMiddleware>();
        app.UseMiddleware<ExceptionMiddleware>();

        return app;
    }
}
