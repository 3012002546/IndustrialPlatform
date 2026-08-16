using IndustrialPlatform.Identity.Api.Commands;
using IndustrialPlatform.Identity.Api.Conventions;
using IndustrialPlatform.Identity.Api.Health;
using IndustrialPlatform.Identity.Application;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Infrastructure;
using IndustrialPlatform.Security;
using IndustrialPlatform.Web.Configuration;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.AddOptionalLocalDevelopmentInfrastructure(DevelopmentService.Identity);
builder.UseIndustrialSerilog();
builder.Services.AddIdentityInfrastructure(builder.Configuration);
builder.Services.AddIdentityApplication(builder.Configuration);
builder.Services.AddIdentityAuthentication();
builder.Services.AddIdentityAuthorization();
builder.Services.AddCurrentUser();
builder.Services.AddOpenApi();
builder.Services.AddIndustrialApi(mvc => mvc.Conventions.Add(new RoutePrefixConvention()));
builder.Services.AddHttpClient();
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres", timeout: TimeSpan.FromSeconds(3))
    .AddCheck<RedisHealthCheck>("redis", timeout: TimeSpan.FromSeconds(3))
    .AddCheck<SeqHealthCheck>("seq", timeout: TimeSpan.FromSeconds(3));

var app = builder.Build();
app.UseIndustrialWeb();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "Identity" }));
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

// §12 JWKS 公钥文档:仅供凭据校验,禁止缓存。minimal API,不经 ResultFilter/路由前缀。
app.MapGet("/.well-known/jwks.json", async (HttpContext http, IJwksProvider jwks, CancellationToken ct) =>
{
    http.Response.Headers.CacheControl = "no-store";
    var doc = await jwks.GetAsync(ct);
    return Results.Json(doc);
});

// --initialize-admin:仅 Development 的显式 admin 初始化命令。
// 复用 IdentityInitializationService,输出可盘点账本;完成即退出,不启动 Web Server。
if (InitializeAdminCommand.IsRequested(args))
{
    InitializeAdminCommand.EnsureDevelopmentEnvironment(app.Environment);
    await InitializeAdminCommand.RunAsync(app.Services, Console.Out);
    return;
}

app.Run();

public partial class Program;
