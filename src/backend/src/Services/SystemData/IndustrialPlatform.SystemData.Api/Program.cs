using IndustrialPlatform.Security;
using IndustrialPlatform.SystemData.Api.Conventions;
using IndustrialPlatform.SystemData.Api.Health;
using IndustrialPlatform.SystemData.Application;
using IndustrialPlatform.SystemData.Infrastructure;
using IndustrialPlatform.Web.Configuration;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.AddOptionalLocalDevelopmentInfrastructure(DevelopmentService.SystemData);
builder.UseIndustrialSerilog();
builder.Services.AddSystemDataInfrastructure(builder.Configuration);
builder.Services.AddSystemDataApplication(builder.Configuration);
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

app.Run();

public partial class Program;
