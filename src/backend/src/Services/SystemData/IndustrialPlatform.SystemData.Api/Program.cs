using IndustrialPlatform.SystemData.Api.Conventions;
using IndustrialPlatform.SystemData.Api.Health;
using IndustrialPlatform.SystemData.Api.Modules;
using IndustrialPlatform.Web.Configuration;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.AddOptionalLocalDevelopmentInfrastructure(DevelopmentService.SystemData);
builder.UseIndustrialSerilog();
builder.Services.AddSystemDataModule(builder.Configuration);
// JwtBearer 令牌校验是宿主级关注点:独立宿主显式注册(配置驱动、fail-closed)。
// UnifiedHost 复用 Identity 模块注册的统一 Bearer 方案,不再调用本扩展。
builder.Services.AddSystemDataAuthentication(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddIndustrialApi(mvc => mvc.Conventions.Add(new RoutePrefixConvention()));
builder.Services.AddHealthChecks().AddSystemDataHealthChecks();

var app = builder.Build();
app.UseIndustrialWeb();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "SystemData" }));
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
app.MapSystemDataModule();

app.Run();
return 0;

public partial class Program;
