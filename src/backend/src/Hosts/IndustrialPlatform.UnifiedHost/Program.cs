using IndustrialPlatform.Identity.Api.Commands;
using IndustrialPlatform.Identity.Api.Conventions;
using IndustrialPlatform.Identity.Api.Health;
using IndustrialPlatform.Identity.Api.Modules;
using IndustrialPlatform.ReferenceData.Api.Modules;
using IndustrialPlatform.SystemData.Api.Modules;
using IndustrialPlatform.UnifiedHost;
using IndustrialPlatform.Web.Configuration;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

// UnifiedHost:单一 ASP.NET Core 进程组合当前平台基础模块(Identity/SystemData/ReferenceData)
// 与统一认证授权、异常、日志、OpenAPI、健康检查。Gateway 不作为第二个进程嵌入;
// 直接保持前端当前使用的外部 API 路径兼容(控制器经 api/v1 前缀与独立 Host 相同)。
// 模块只组合不复制;独立 API Host 继续可单独运行。
var builder = WebApplication.CreateBuilder(args);

// UnifiedHost 使用单连接承载全部模块:Shared 拓扑解析共享物理库(当前 Development 默认);
// PerService 拓扑需要 ServiceDatabases["unifiedhost"] 显式映射,否则启动明确失败。
builder.AddOptionalLocalDevelopmentInfrastructure(DevelopmentService.UnifiedHost);
builder.UseIndustrialSerilog();
// 模块迁移协调器注册在托管服务最前:启动时确定顺序执行 Identity → SystemData 迁移/种子,
// 完成后宿主才继续启动其余托管服务(Outbox 派发、数据库编排 Runner 等)。
// 因此 Identity/SystemData 的独立 SchemaMigrationBackgroundService 不在此注册(传 false),
// 避免两套迁移后台服务并行迁移同一 Shared 物理库。
builder.Services.AddHostedService<ModuleMigrationCoordinatorHostedService>();
builder.Services.AddIdentityModule(builder.Configuration, includeStartupMigrationService: false);
builder.Services.AddSystemDataModule(builder.Configuration, includeStartupMigrationService: false);
builder.Services.AddReferenceDataModule(builder.Configuration);
builder.Services.AddOpenApi();
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options => options.AddPolicy("UnifiedHostDevelopmentCors", policy => policy
        .WithOrigins(DevelopmentCorsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));
}
// 三个模块的控制器共用同一前缀约定(各独立 Host 的 RoutePrefixConvention 语义一致:api/v1),
// 只注册一次,避免重复前缀或重复 ResultFilter 包装。
builder.Services.AddIndustrialApi(mvc => mvc.Conventions.Add(new RoutePrefixConvention()));
// 统一健康检查:各模块检查按模块前缀命名,避免同名覆盖(identity./systemdata./referencedata.)。
builder.Services.AddHealthChecks()
    .AddIdentityHealthChecks("identity")
    .AddSystemDataHealthChecks("systemdata")
    .AddReferenceDataHealthChecks("referencedata");

var app = builder.Build();
app.UseIndustrialWeb();
// UnifiedHost 对齐 Gateway 的外部路径契约：剥离服务前缀后继续使用各模块原有 /api/v1 路由。
// 无前缀路由不受影响，独立 Gateway/Api Host 也无需改动。
app.Use(async (context, next) =>
{
    foreach (var prefix in GatewayServicePrefixes)
    {
        if (context.Request.Path.StartsWithSegments(prefix, out var remaining))
        {
            context.Request.Path = remaining.HasValue ? remaining : "/";
            break;
        }
    }

    await next(context);
});
app.UseRouting();
if (app.Environment.IsDevelopment())
{
    app.UseCors("UnifiedHostDevelopmentCors");
}
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "UnifiedHost" }));
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthCheckResponseWriter.Write,
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    ResponseWriter = HealthCheckResponseWriter.Write,
});
app.MapControllers();
app.MapIdentityModule();

// 生产静态文件:前端生产构建产物置于 wwwroot(由应用镜像提供,云端无需第二个前端容器)。
// SPA 回退仅对非 API/健康检查/JWKS 路径生效,未知 API 路径保持 404 语义;
// wwwroot/index.html 不存在(本地开发走 Vite)时回退同样 404。
app.UseStaticFiles();
app.MapFallback(async (HttpContext context) =>
{
    var path = context.Request.Path;
    if (path.StartsWithSegments("/api")
        || path.StartsWithSegments("/health")
        || path.StartsWithSegments("/.well-known"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var indexFile = Path.Combine(app.Environment.WebRootPath ?? string.Empty, "index.html");
    if (!File.Exists(indexFile))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    context.Response.ContentType = "text/html";
    await context.Response.SendFileAsync(indexFile);
});

// --initialize-admin:与 Identity.Api 相同的显式 admin 初始化命令(同一 IdentityInitializationService),
// 供部署容器内一次性初始化使用;完成即退出,不启动 Web Server。
var initializeAdminRequested = InitializeAdminCommand.IsRequested(args);
var resetAdminRequested = ResetDevelopmentAdminCommand.IsRequested(args);
if (initializeAdminRequested && resetAdminRequested)
{
    Console.Error.WriteLine("admin 初始化与重置命令不能同时执行。");
    return 1;
}

if (initializeAdminRequested)
{
    InitializeAdminCommand.EnsureDevelopmentEnvironment(app.Environment);
    if (!InitializeAdminCommand.TryGetCredentialOutputPath(args, out var credentialOutput, out var argumentError))
    {
        Console.Error.WriteLine($"[{InitializeAdminCommand.ArgumentName}] {argumentError}");
        return 1;
    }

    return await InitializeAdminCommand.RunAsync(app.Services, Console.Out, credentialOutput);
}

if (resetAdminRequested)
{
    InitializeAdminCommand.EnsureDevelopmentEnvironment(app.Environment);
    if (!InitializeAdminCommand.TryGetCredentialOutputPath(args, out var credentialOutput, out var argumentError)
        || credentialOutput is null)
    {
        Console.Error.WriteLine($"[{ResetDevelopmentAdminCommand.ArgumentName}] {argumentError ?? "必须提供 --credential-output <绝对路径>。"}");
        return 1;
    }

    return await ResetDevelopmentAdminCommand.RunAsync(app.Services, Console.Out, credentialOutput);
}

app.Run();
return 0;

public partial class Program
{
    internal static readonly string[] DevelopmentCorsOrigins =
        ["http://localhost:5173", "http://localhost:4173"];

    internal static readonly string[] GatewayServicePrefixes =
        ["/identity", "/systemdata", "/referencedata"];
}
