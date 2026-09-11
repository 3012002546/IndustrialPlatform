using IndustrialPlatform.Collaboration.Api.Modules;
using IndustrialPlatform.Collaboration.EmbeddedHost;
using IndustrialPlatform.Identity.Api.Conventions;
using IndustrialPlatform.Identity.Api.Health;
using IndustrialPlatform.Identity.Api.Modules;
using IndustrialPlatform.ReferenceData.Api.Modules;
using IndustrialPlatform.SystemData.Api.Modules;
using IndustrialPlatform.Web.Configuration;
using IndustrialPlatform.Web.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

// This is a deliberately small reference host for an embedded deployment.
// It composes the same Identity, SystemData and Collaboration modules as the
// platform host; tenant/user data and the database are supplied by configuration.
var builder = WebApplication.CreateBuilder(args);
var allowedParentOrigins = builder.Configuration.GetSection("EmbeddedCollaboration:AllowedParentOrigins").GetChildren().Select(item => item.Value).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().ToArray();
builder.Services.AddSingleton(new EmbeddedHostHandshakeOptions(allowedParentOrigins)
{
    SourceSessionCookieName = builder.Configuration["EmbeddedCollaboration:SourceSessionCookieName"] ?? "embedded_host_session",
});
builder.Services.AddSingleton<IEmbeddedSubjectIdentityMapper, ConfigurationEmbeddedSubjectIdentityMapper>();
builder.Services.AddSingleton<IEmbeddedSourcePrincipalResolver, ConfigurationEmbeddedSourcePrincipalResolver>();
builder.Services.AddSingleton<IEmbeddedIdentityAssertionIssuer, ConfigurationEmbeddedIdentityAssertionIssuer>();
builder.Services.AddSingleton<IEmbeddedHandshakeStore, SqlEmbeddedHandshakeStore>();
builder.Services.AddSingleton<EmbeddedHostHandshakeService>();
builder.Services.AddCors(options => options.AddPolicy("embedded-parent", policy =>
    policy.WithOrigins(allowedParentOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddSystemDataModule(builder.Configuration);
builder.Services.AddReferenceDataModule(builder.Configuration);
builder.Services.AddCollaborationModule(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddIndustrialApi(mvc => mvc.Conventions.Add(new RoutePrefixConvention()));

var health = builder.Services.AddHealthChecks();
health.AddIdentityHealthChecks();
health.AddSystemDataHealthChecks("systemdata");
health.AddReferenceDataHealthChecks("referencedata");
health.AddCollaborationHealthChecks("collaboration");

var app = builder.Build();
app.UseIndustrialWeb();
app.UseCors("embedded-parent");
app.UseStaticFiles();
app.UseRouting();
app.UseMiddleware<EmbeddedSessionAuthenticationMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { ResponseWriter = HealthCheckResponseWriter.Write });
app.MapControllers();
app.MapCollaborationModule();
app.Run();

public partial class Program;
