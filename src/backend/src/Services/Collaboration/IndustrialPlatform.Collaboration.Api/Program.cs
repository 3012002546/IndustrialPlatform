using IndustrialPlatform.Identity.Api.Conventions;
using IndustrialPlatform.Identity.Api.Health;
using IndustrialPlatform.Identity.Api.Modules;
using IndustrialPlatform.SystemData.Api.Modules;
using IndustrialPlatform.Web.Configuration;
using IndustrialPlatform.Web.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using IndustrialPlatform.Collaboration.Api.Modules;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Collaboration:Identity:Mode"] = "Embedded",
    ["Collaboration:SystemData:Mode"] = "Embedded",
});
builder.UseIndustrialSerilog();
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddSystemDataModule(builder.Configuration);
builder.Services.AddCollaborationModule(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddIndustrialApi(mvc => mvc.Conventions.Add(new RoutePrefixConvention()));
var health = builder.Services.AddHealthChecks();
health.AddIdentityHealthChecks();
health.AddSystemDataHealthChecks("systemdata");
health.AddCollaborationHealthChecks();

var app = builder.Build();
app.UseIndustrialWeb();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { ResponseWriter = HealthCheckResponseWriter.Write });
app.MapControllers();
app.MapCollaborationModule();
app.Run();

public partial class Program;
