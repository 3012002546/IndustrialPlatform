using IndustrialPlatform.Identity.Api.Conventions;
using IndustrialPlatform.Identity.Api.Health;
using IndustrialPlatform.Identity.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.UseIndustrialSerilog();
builder.Services.AddIdentityInfrastructure(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddIndustrialApi(mvc => mvc.Conventions.Add(new RoutePrefixConvention()));
builder.Services.AddHttpClient();
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres", timeout: TimeSpan.FromSeconds(3))
    .AddCheck<RedisHealthCheck>("redis", timeout: TimeSpan.FromSeconds(3))
    .AddCheck<SeqHealthCheck>("seq", timeout: TimeSpan.FromSeconds(3));

var app = builder.Build();
app.UseIndustrialWeb();
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
app.Run();

public partial class Program;
