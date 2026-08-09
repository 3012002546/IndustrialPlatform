using IndustrialPlatform.Logging.Internal;
using IndustrialPlatform.Logging.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Serilog;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Serilog 日志组件依赖注入扩展。
/// </summary>
public static class LoggingServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Serilog 选项与 Logger 单例,并将静态 Log.Logger 指向该实例。
    /// 在宿主场景下推荐改用 <see cref="UseIndustrialSerilog(Microsoft.AspNetCore.Builder.WebApplicationBuilder)"/>。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置源。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddIndustrialLogging(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<SerilogOptions>().Bind(configuration.GetSection("Serilog"));

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SerilogOptions>>().Value;
            var logger = SerilogConfigurationBuilder.Build(options).CreateLogger();
            Log.Logger = logger;
            return logger;
        });
        services.AddSingleton<Serilog.ILogger>(sp => sp.GetRequiredService<Serilog.Core.Logger>());

        return services;
    }

    /// <summary>
    /// 为 WebApplicationBuilder 接入 Serilog(宿主级,自动纳入 Microsoft.Extensions.Logging 管线)。
    /// </summary>
    /// <param name="builder">Web 应用构建器。</param>
    /// <returns>Web 应用构建器。</returns>
    public static WebApplicationBuilder UseIndustrialSerilog(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Host.UseSerilog((context, loggerConfiguration) =>
        {
            var options = context.Configuration.GetSection("Serilog").Get<SerilogOptions>() ?? new SerilogOptions();
            SerilogConfigurationBuilder.Configure(loggerConfiguration, options);
        });

        return builder;
    }
}
