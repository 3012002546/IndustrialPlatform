using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Builder;
using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Data;
using IndustrialPlatform.PlatformStudio.Application.Dashboard;
using IndustrialPlatform.PlatformStudio.Application.Integration;
using IndustrialPlatform.PlatformStudio.Application.Ledger;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Data;
using IndustrialPlatform.PlatformStudio.Infrastructure.Identity;
using IndustrialPlatform.PlatformStudio.Infrastructure.Initialization;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using IndustrialPlatform.PlatformStudio.Infrastructure.Integration.Embedded;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace IndustrialPlatform.PlatformStudio.Api;

/// <summary>按部署清单装配Studio；Enabled=false不注册API、Handler或后台组件。</summary>
public static class StudioServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformStudio(this IServiceCollection services,IConfiguration configuration)
    {
        var profile=configuration.GetSection("PlatformStudio").Get<StudioDeploymentProfile>()??new();
        if(!profile.Enabled)return services;
        profile.Validate();
        // Hosting request-start messages include raw query strings. Office protocol tickets must never enter those logs.
        if (profile.Office.Admitted) services.AddLogging(o => o.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning));
        foreach(var endpoint in profile.SourceRegistry)
        foreach(var declaration in endpoint.Value.Catalog)
        {
            if(declaration.ResultSchemaJson.Length>262144||declaration.ParametersJson?.Length>65536)throw new StudioException("STUDIO_VALIDATION","公开目录Schema超过安装预算。");
            var schema=System.Text.Json.Nodes.JsonNode.Parse(declaration.ResultSchemaJson)?.AsObject()??throw new StudioException("STUDIO_VALIDATION","公开目录Schema缺失。");
            var parameters=declaration.ParametersJson is null?null:System.Text.Json.Nodes.JsonNode.Parse(declaration.ParametersJson)?.AsArray();
            services.AddSingleton(new StudioSourceCatalogRegistration(endpoint.Key,declaration.ObjectName,declaration.Kind,declaration.PermissionKey,schema,parameters));
        }
        services.AddSingleton(profile);services.TryAddSingleton(TimeProvider.System);services.AddHttpContextAccessor();
        services.AddSingleton<StudioDatabase>();services.AddSingleton<StudioServiceInitializer>();services.AddSingleton<IServiceInitializer>(sp=>sp.GetRequiredService<StudioServiceInitializer>());
        services.AddScoped<IStudioResourceStore,StudioResourceStore>();services.AddScoped<IStudioAuthorization,StudioAuthorization>();services.AddScoped<StudioCommands>();
        services.AddScoped<StudioPublishing>();services.AddScoped<StudioGrants>();services.AddScoped<SourceAdministration>();services.AddScoped<SourceCatalog>();services.AddScoped<IStudioSourceCatalogReader,RegisteredSourceCatalog>();services.AddScoped<DatasetExecution>();services.AddSingleton<StudioDatasetCache>();services.AddSingleton<StudioSourceProbes>();services.AddSingleton<StudioSourceLimits>();
        if(profile.ActiveModules.Contains("LowCode",StringComparer.Ordinal))
        {
            services.AddScoped<IStudioAction,StudioLedgerSaveAction>();
            services.AddScoped<StudioLedger>();services.AddScoped<StudioPages>();
        }
        if(profile.ActiveModules.Contains("Dashboard",StringComparer.Ordinal))
        {
            services.AddRateLimiter(options => {
                options.RejectionStatusCode = 429;
                options.AddPolicy("studio-dashboard-screen", context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions {
                        PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
                    }));
            });
            services.AddScoped<DashboardNavigation>();
            services.AddScoped<DashboardScreens>();
            services.AddScoped<IStudioScreenKeys,StudioScreenKeys>();
            services.AddScoped<IStudioScreenStore,StudioScreenStore>();
            services.AddScoped<IStudioScreenIdentity>(sp => sp.GetRequiredService<IStudioIdentity>() as IStudioScreenIdentity
                ?? throw new StudioException("STUDIO_SCREEN_UNSUPPORTED", "当前身份适配器不支持匿名大屏发布。", 409));
        }
        if (profile.ActiveModules.Contains("Report", StringComparer.Ordinal))
        {
            services.AddScoped<IndustrialPlatform.PlatformStudio.Application.Report.IReportRunStore, IndustrialPlatform.PlatformStudio.Infrastructure.Report.ReportRunStore>();
            services.AddScoped<IndustrialPlatform.PlatformStudio.Application.Report.ReportRuns>();
            services.AddScoped<IndustrialPlatform.PlatformStudio.Application.Report.ReportDesign>();
            services.AddScoped<IndustrialPlatform.PlatformStudio.Infrastructure.Report.ReportCommandRenderer>();
            services.AddScoped<IndustrialPlatform.PlatformStudio.Infrastructure.Report.ReportPreview>();
            services.AddScoped<IndustrialPlatform.PlatformStudio.Infrastructure.Report.ReportExecution>();
            services.AddScoped<IndustrialPlatform.PlatformStudio.Infrastructure.Report.ReportOutputs>();
            services.AddScoped<IndustrialPlatform.PlatformStudio.Infrastructure.Report.ReportRetries>();
            services.AddScoped<IndustrialPlatform.PlatformStudio.Infrastructure.Report.ReportResources>();
            services.AddScoped<IndustrialPlatform.PlatformStudio.Infrastructure.Report.ReportReconciliation>();
            services.AddScoped<IndustrialPlatform.PlatformStudio.Infrastructure.Report.StudioWorkbooks>();
            services.AddHostedService<WorkbookMaintenanceWorker>();
            services.AddScoped<IndustrialPlatform.PlatformStudio.Infrastructure.Report.ReportOwnedFiles>();
        }
        if(profile.DeploymentMode=="ExternalMes") services.AddStudioEmbeddedFileAudit(profile); else services.AddStudioPlatformFileAudit(profile);
        services.AddStudioJobs(profile);
        services.AddHttpClient<RegisteredSourceExecutor>().ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler { AllowAutoRedirect=false });
        services.AddScoped<IStudioSourceExecutor>(sp=>sp.GetRequiredService<RegisteredSourceExecutor>());
        services.TryAddSingleton<IStudioFactoryScope,EmptyStudioFactoryScope>();
        services.AddAntiforgery(o=>{o.HeaderName="X-Studio-CSRF";o.Cookie.Name="__Host-StudioCsrf";o.Cookie.Path="/";o.Cookie.SecurePolicy=Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;});

        if(profile.DeploymentMode=="ExternalMes")
        {
            var keyPath=profile.Identity.DataProtectionKeyPath;
            if(string.IsNullOrWhiteSpace(keyPath)||!Path.IsPathFullyQualified(keyPath))throw new StudioException("STUDIO_VALIDATION","独立身份必须配置持久DataProtectionKeyPath，目录由运行账号保护。");
            services.AddDataProtection().SetApplicationName("IndustrialPlatform.PlatformStudio").PersistKeysToFileSystem(new DirectoryInfo(keyPath));
            services.AddScoped<StudioExternalIdentity>();services.AddScoped<IStudioIdentity>(sp=>sp.GetRequiredService<StudioExternalIdentity>());services.AddScoped<IStudioSubjectDirectory>(sp=>sp.GetRequiredService<StudioExternalIdentity>());
            services.AddScoped<IStudioMesBridge,StudioMesBridge>();services.AddScoped<StudioExternalLogin>();
            if(profile.Identity.Provider=="Oidc")AddOidc(services,profile,configuration);
        }
        return services;
    }
    private static void AddOidc(IServiceCollection services,StudioDeploymentProfile profile,IConfiguration configuration)
    {
        if(!profile.Identity.Authorities.TryGetValue(profile.Identity.AuthorityKey??"",out var authority)||!Uri.TryCreate(authority.MetadataAddress,UriKind.Absolute,out var metadata)||metadata.Scheme!="https"||string.IsNullOrWhiteSpace(authority.ClientId))throw new StudioException("STUDIO_VALIDATION","OIDC登记项不完整。");
        var secret=authority.ClientSecretRef is {} secretRef?configuration[secretRef]:null;
        if(string.IsNullOrWhiteSpace(secret))throw new StudioException("STUDIO_VALIDATION","OIDC机密客户端secret未配置。");
        services.AddAuthentication().AddCookie("StudioOidcTransient").AddOpenIdConnect("StudioOidc",options=>
        {
            options.SignInScheme="StudioOidcTransient";options.MetadataAddress=metadata.AbsoluteUri;options.Authority=authority.Issuer;options.ClientId=authority.ClientId;options.ClientSecret=secret;
            options.ResponseType="code";options.UsePkce=true;options.SaveTokens=false;options.MapInboundClaims=false;options.CallbackPath="/api/v1/studio/auth/callback";
            options.TokenValidationParameters=new TokenValidationParameters { ValidateIssuer=true,ValidIssuer=authority.Issuer,ValidateAudience=true,ValidAudience=authority.ClientId,ValidateLifetime=true,RequireSignedTokens=true,ValidAlgorithms=authority.AllowedSigningAlgorithms };
            options.Events=new OpenIdConnectEvents
            {
                OnTicketReceived=async context=>await context.HttpContext.RequestServices.GetRequiredService<StudioExternalLogin>().CompleteOidcAsync(context,context.HttpContext.RequestAborted),
                OnRemoteFailure=context=>{context.HandleResponse();context.Response.StatusCode=401;return context.Response.WriteAsJsonAsync(new { success=false,code="STUDIO_UNAUTHENTICATED",message="MES登录验证失败，请重新发起登录。" });}
            };
            var certificatePath=configuration[authority.ClientCertificatePathRef];
            if(string.IsNullOrWhiteSpace(certificatePath))throw new StudioException("STUDIO_VALIDATION","OIDC后通道双向TLS证书未配置。");
            var handler=new HttpClientHandler { AllowAutoRedirect=false };
            handler.ClientCertificates.Add(X509CertificateLoader.LoadPkcs12FromFile(certificatePath,authority.ClientCertificatePasswordRef is null?null:configuration[authority.ClientCertificatePasswordRef]));
            options.BackchannelHttpHandler=handler;options.BackchannelTimeout=TimeSpan.FromSeconds(5);
        });
    }
}
