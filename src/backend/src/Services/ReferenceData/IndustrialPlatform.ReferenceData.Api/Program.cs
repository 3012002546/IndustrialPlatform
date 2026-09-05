using IndustrialPlatform.ReferenceData.Api.Health;
using IndustrialPlatform.ReferenceData.Api.Modules;
using IndustrialPlatform.Web.Configuration;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.AddOptionalLocalDevelopmentInfrastructure(DevelopmentService.ReferenceData);
builder.UseIndustrialSerilog();
builder.Services.AddReferenceDataModule(builder.Configuration);
builder.Services.AddReferenceDataAuthentication(builder.Configuration);
builder.Services.AddIndustrialApi(options => options.Conventions.Add(new IndustrialPlatform.ReferenceData.Api.Conventions.RoutePrefixConvention()));
builder.Services.AddHostedService<IndustrialPlatform.ReferenceData.Api.Initialization.ReferenceDataStartupInitialization>();
builder.Services.AddHealthChecks().AddReferenceDataHealthChecks();

var app = builder.Build();
app.UseIndustrialWeb();
app.UseRouting();
IndustrialPlatform.ReferenceData.Api.Endpoints.ReferenceDataRequestErrors.UseReferenceDataRequestErrors(app);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "ReferenceData" }));
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthCheckResponseWriter.Write,
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => !registration.Tags.Contains("capability"),
    ResponseWriter = HealthCheckResponseWriter.Write,
});
app.MapHealthChecks("/health/capabilities", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("capability"),
    ResponseWriter = HealthCheckResponseWriter.Write,
});
app.MapReferenceDataModule();

app.Run();
return 0;

public partial class Program;
