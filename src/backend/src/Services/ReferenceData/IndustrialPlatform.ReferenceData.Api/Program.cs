using IndustrialPlatform.ReferenceData.Api.Health;
using IndustrialPlatform.ReferenceData.Api.Modules;
using IndustrialPlatform.Web.Configuration;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.AddOptionalLocalDevelopmentInfrastructure(DevelopmentService.ReferenceData);
builder.UseIndustrialSerilog();
builder.Services.AddReferenceDataModule(builder.Configuration);
builder.Services.AddHealthChecks().AddReferenceDataHealthChecks();

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
app.MapReferenceDataModule();

app.Run();
return 0;

public partial class Program;
