using IndustrialPlatform.Identity.Api.Commands;
using IndustrialPlatform.Identity.Api.Conventions;
using IndustrialPlatform.Identity.Api.Health;
using IndustrialPlatform.Identity.Api.Modules;
using IndustrialPlatform.Web.Configuration;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.AddOptionalLocalDevelopmentInfrastructure(DevelopmentService.Identity);
builder.UseIndustrialSerilog();
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddIndustrialApi(mvc => mvc.Conventions.Add(new RoutePrefixConvention()));
builder.Services.AddHealthChecks().AddIdentityHealthChecks();

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
app.MapIdentityModule();

// --initialize-admin:仅 Development 的显式 admin 初始化命令。
// 复用 IdentityInitializationService,输出可盘点账本;可选 --credential-output <绝对路径>
// 将首次创建的一次性凭据写入仅当前用户可访问的 JSON 文件(stdout 脱敏)。完成即退出,不启动 Web Server。
if (InitializeAdminCommand.IsRequested(args))
{
    InitializeAdminCommand.EnsureDevelopmentEnvironment(app.Environment);
    if (!InitializeAdminCommand.TryGetCredentialOutputPath(args, out var credentialOutput, out var argumentError))
    {
        Console.Error.WriteLine($"[{InitializeAdminCommand.ArgumentName}] {argumentError}");
        return 1;
    }

    return await InitializeAdminCommand.RunAsync(app.Services, Console.Out, credentialOutput);
}

app.Run();
return 0;

public partial class Program;
