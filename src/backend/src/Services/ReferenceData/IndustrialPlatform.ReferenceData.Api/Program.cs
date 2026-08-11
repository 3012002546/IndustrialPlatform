using IndustrialPlatform.ReferenceData.Api.Health;
using IndustrialPlatform.ReferenceData.Infrastructure;
using IndustrialPlatform.Web.Configuration;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.AddOptionalLocalDevelopmentInfrastructure(DevelopmentService.ReferenceData);
builder.UseIndustrialSerilog();
builder.Services.AddReferenceDataInfrastructure(builder.Configuration);
builder.Services.AddHttpClient();
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres", timeout: TimeSpan.FromSeconds(3))
    .AddCheck<RedisHealthCheck>("redis", timeout: TimeSpan.FromSeconds(3))
    .AddCheck<RabbitMqHealthCheck>("rabbitmq", timeout: TimeSpan.FromSeconds(3))
    .AddCheck<SeqHealthCheck>("seq", timeout: TimeSpan.FromSeconds(3));

var app = builder.Build();
app.UseIndustrialWeb();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "ReferenceData" }));
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
