using IndustrialPlatform.Collaboration.Api.Modules;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.EmbeddedHost;
using IndustrialPlatform.Identity.Api.Conventions;
using IndustrialPlatform.Identity.Api.Health;
using IndustrialPlatform.Identity.Api.Modules;
using IndustrialPlatform.ReferenceData.Api.Modules;
using IndustrialPlatform.SystemData.Api.Modules;
using IndustrialPlatform.Web.Configuration;
using IndustrialPlatform.Web.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using IndustrialPlatform.Collaboration.Api.Hubs;

// 独立协作宿主复用平台的身份、系统数据、参考数据和协作模块。
// 启动前将 appsettings.example.json 复制为本目录的 appsettings.json；
// CreateBuilder 不会自动读取示例文件，也不读取 UnifiedHost 的云数据库配置。
// 租户、用户和数据库连接均从本宿主配置取得。
// 一、加载本宿主配置。环境变量和命令行参数可覆盖配置文件中的值。
var builder = WebApplication.CreateBuilder(args);
StandaloneConfiguration.Apply(builder, args);
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    // 本宿主采用同进程模块调用，数据库类型仍由 SqlSugar/DatabaseTopology 配置选择。
    ["Collaboration:Identity:Mode"] = "Embedded",
    ["Collaboration:SystemData:Mode"] = "Embedded",
});
// 二、选择接入方式：FixedDemo 表示固定用户演示，ReferenceFixture 表示读取示例适配配置。
// 真实 MES 接入应关闭演示模式，并在下面替换当前用户、身份映射和权限适配器。
var embeddedMode = builder.Configuration["EmbeddedCollaboration:Mode"] ?? string.Empty;
var fixedDemoMode = string.Equals(embeddedMode, "FixedDemo", StringComparison.OrdinalIgnoreCase);
var sourceResolverMode = builder.Configuration["EmbeddedCollaboration:SourceResolver:Mode"];
var accessAdapterMode = builder.Configuration["EmbeddedCollaboration:AccessAdapter:Mode"];
var standaloneInitializationEnabled = builder.Configuration.GetValue("EmbeddedCollaboration:Initialization:Enabled", fixedDemoMode);
if (fixedDemoMode
    && (!string.Equals(sourceResolverMode, "ReferenceFixture", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(accessAdapterMode, "ReferenceFixture", StringComparison.OrdinalIgnoreCase)))
    throw new InvalidOperationException("FixedDemo 只能与 ReferenceFixture 适配器一起启用；真实 MES 接入必须使用独立适配器。");
// 允许访问的前端来源必须包含协议与端口，例如 https://localhost:5173。
// 这是前端页面地址，不是后端监听的 56364/56365 端口。
var allowedParentOrigins = builder.Configuration.GetSection("EmbeddedCollaboration:AllowedParentOrigins").GetChildren().Select(item => item.Value).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().ToArray();
if (standaloneInitializationEnabled)
{
    builder.Services.AddSingleton<IStandaloneInitializationLock, StandaloneInitializationLock>();
    builder.Services.AddHostedService<StandaloneDatabaseInitializationHostedService>();
}
// 三、注册身份握手：读取上游登录、映射用户、签发断言并持久化协作会话。
builder.Services.AddSingleton(new EmbeddedHostHandshakeOptions(allowedParentOrigins)
{
    SourceSessionCookieName = builder.Configuration["EmbeddedCollaboration:SourceSessionCookieName"] ?? "embedded_host_session",
});
if (string.Equals(sourceResolverMode, "ReferenceFixture", StringComparison.OrdinalIgnoreCase))
{
    // 该模式仅供仓库中的固定用户演示配置使用。
    builder.Services.AddSingleton<IEmbeddedSubjectIdentityMapper, ConfigurationEmbeddedSubjectIdentityMapper>();
    builder.Services.AddSingleton<IEmbeddedSourcePrincipalResolver, ConfigurationEmbeddedSourcePrincipalResolver>();
}
else
{
    // 接入真实 MES 时替换这些注册，在服务端调用 MES 的当前用户和登录会话接口。
    builder.Services.AddSingleton<IEmbeddedCurrentUserAdapter, NotConfiguredEmbeddedCurrentUserAdapter>();
    builder.Services.AddSingleton<IEmbeddedSourcePrincipalResolver, AdapterBackedEmbeddedSourcePrincipalResolver>();
    builder.Services.AddSingleton<IEmbeddedSubjectIdentityMapper, ServerDerivedEmbeddedSubjectIdentityMapper>();
}
builder.Services.AddSingleton<IEmbeddedIdentityAssertionIssuer, ConfigurationEmbeddedIdentityAssertionIssuer>();
builder.Services.AddSingleton<IEmbeddedHandshakeStore, SqlEmbeddedHandshakeStore>();
builder.Services.AddSingleton<EmbeddedHostHandshakeService>();
builder.Services.AddCors(options => options.AddPolicy("embedded-parent", policy =>
    policy.WithOrigins(allowedParentOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
// 四、装配已有模块。固定演示由统一初始化器按依赖顺序建表，避免模块各自抢先迁移。
builder.Services.AddIdentityModule(builder.Configuration, includeStartupMigrationService: false);
builder.Services.AddSystemDataModule(builder.Configuration, includeStartupMigrationService: false);
builder.Services.AddReferenceDataModule(builder.Configuration);
builder.Services.AddCollaborationModule(builder.Configuration);
if (standaloneInitializationEnabled)
    builder.Services.AddSingleton<IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Initialization.IServiceInitializationInvoker>(sp =>
        new IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Initialization.InProcessServiceInitializationInvoker(
            sp.GetServices<IndustrialPlatform.Application.Abstractions.Initialization.IServiceInitializer>()));
if (standaloneInitializationEnabled)
    builder.Services.AddSingleton<IndustrialPlatform.SystemData.Application.Reliability.IIdentityPermissionRegistry, FixedDemoIdentityPermissionRegistry>();
builder.Services.AddSingleton<FixedDemoAccountCatalog>();
// 五、注册人员目录和权限来源。真实适配器尚未接线时默认拒绝，不能自动放行。
if (string.Equals(accessAdapterMode, "ReferenceFixture", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IEmbeddedCollaborationAccessAdapter, ConfigurationEmbeddedCollaborationAccessAdapter>();
else
    builder.Services.AddSingleton<IEmbeddedCollaborationAccessAdapter, NotConfiguredEmbeddedCollaborationAccessAdapter>();
// 这些注册必须放在模块注册之后，让人员目录和权限判断统一走 MES 适配器。
// 只替换 ICollaborationPermissionEvaluator 不够：媒体权限判断优先使用 IPermissionEvaluator。
builder.Services.AddSingleton<ICollaborationIdentityDirectory, EmbeddedCollaborationIdentityDirectory>();
builder.Services.AddSingleton<IndustrialPlatform.Identity.Application.Authorization.IPermissionEvaluator, EmbeddedPermissionEvaluator>();
builder.Services.AddSingleton<ICollaborationHubSessionValidator, EmbeddedHubSessionValidator>();
builder.Services.AddOpenApi();
builder.Services.AddIndustrialApi(mvc => mvc.Conventions.Add(new RoutePrefixConvention()));

// 六、提供各模块的就绪检查，便于判断数据库和模块是否初始化成功。
var health = builder.Services.AddHealthChecks();
health.AddIdentityHealthChecks();
health.AddSystemDataHealthChecks("systemdata");
health.AddReferenceDataHealthChecks("referencedata");
health.AddCollaborationHealthChecks("collaboration");

// 七、配置请求处理顺序：嵌入会话 Cookie 先还原身份，再执行认证、授权和业务接口。
var app = builder.Build();
app.UseIndustrialWeb();
app.UseCors("embedded-parent");
app.UseStaticFiles();
app.UseRouting();
app.UseMiddleware<EmbeddedSessionAuthenticationMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { ResponseWriter = HealthCheckResponseWriter.Write });
app.MapControllers();
app.MapCollaborationModule();
// 只有显式 FixedDemo 配置才开放固定 A 登录接口；开发前端自动调用它建立真实 Cookie 会话。
if (fixedDemoMode)
    app.MapFixedDemoSession();
// 后端监听地址由 launchSettings.json/部署环境决定，前端 Vite 的 5173 端口单独配置。
app.Run();

// 为集成测试提供宿主入口，不包含额外业务逻辑。
public partial class Program;
