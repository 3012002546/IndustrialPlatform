using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Data;
using IndustrialPlatform.PlatformStudio.Application.Ledger;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Contracts;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Identity;
using IndustrialPlatform.PlatformStudio.Infrastructure.Initialization;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using IndustrialPlatform.SystemData.Application.Operations;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialPlatform.PlatformStudio.Api;

/// <summary>Studio同一HTTP边界；所有业务入口取得可信身份，外部cookie写入必须通过CSRF。</summary>
public static class StudioEndpoints
{
    private sealed record PublicEndpoint;
    public sealed record LocalLogin(string TenantNId,string LoginName,string Password);
    public sealed record ChangePassword(string CurrentPassword,string NewPassword);
    public static IEndpointRouteBuilder MapPlatformStudio(this IEndpointRouteBuilder endpoints)
    {
        var profile=endpoints.ServiceProvider.GetService<StudioDeploymentProfile>();
        if(profile is null||!profile.Enabled)return endpoints;
        var group=endpoints.MapGroup("/api/v1/studio");
        group.AddEndpointFilter(async (context,next)=>
        {
            var http=context.HttpContext;
            try
            {
                if(profile.DeploymentMode=="ExternalMes"&&!HttpMethods.IsGet(http.Request.Method)&&!HttpMethods.IsHead(http.Request.Method)&&!HttpMethods.IsOptions(http.Request.Method))
                {
                    await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http);
                    if(http.Request.Headers.Origin is {} origin&&!string.IsNullOrEmpty(origin)&&!string.Equals(origin,profile.PublicOrigin.TrimEnd('/'),StringComparison.Ordinal))throw new StudioException("STUDIO_FORBIDDEN","请求来源不在当前部署范围。",403);
                }
                if(http.GetEndpoint()?.Metadata.GetMetadata<PublicEndpoint>() is null)
                    http.Items["StudioPrincipal"]=await http.RequestServices.GetRequiredService<IStudioIdentity>().GetCurrentAsync(http.RequestAborted);
                var result=await next(context);
                return result is IResult ? result : Results.Ok(ApiResult.Ok(result));
            }
            catch(StudioException ex){return Error(http,ex.StatusCode,ex.Code,ex.Message,ex.Path);}
            catch(Exception ex) when(StudioStorageErrors.IsUnique(ex)){return Error(http,409,"STUDIO_VERSION_CONFLICT","记录或操作键已被并发请求使用，请重新读取。",null);}
            catch(SystemDataOperationException ex){return Error(http,ex.StatusCode,ex.Code,ex.Message,null);}
            catch(AntiforgeryValidationException){return Error(http,403,"STUDIO_CSRF_INVALID","会话相关性验证失败，请刷新页面。",null);}
            catch(IndustrialPlatform.SharedKernel.Exceptions.ValidationException ex){return Error(http,400,"STUDIO_VALIDATION",ex.Message,null);}
            catch(System.Text.Json.JsonException){return Error(http,400,"STUDIO_VALIDATION","请求JSON或字段类型无效。",null);}
            catch(InvalidOperationException ex) when(ex.Message.Contains("Json",StringComparison.OrdinalIgnoreCase)){return Error(http,400,"STUDIO_VALIDATION","配置结构或字段类型无效。",null);}
        });
        group.MapGet("/auth/csrf",(HttpContext http,IAntiforgery csrf)=>new { token=csrf.GetAndStoreTokens(http).RequestToken,headerName="X-Studio-CSRF" }).WithMetadata(new PublicEndpoint());
        group.MapGet("/auth/me",async (HttpContext http,StudioDatabase db,CancellationToken ct)=>
        {
            var principal=Principal(http);var mustChange=false;
            if(profile.Identity.Provider=="LocalAdmin")mustChange=await db.Client.Queryable<StudioLocalAdmin>().Where(a=>a.TenantNId==principal.TenantNId&&a.SubjectNId==principal.SubjectNId&&a.MustChangePassword).AnyAsync(ct);
            return new { principal,installedModules=profile.InstalledModules,enabledModules=profile.ActiveModules,mustChangePassword=mustChange };
        });
        if(profile.DeploymentMode=="ExternalMes")
        {
            group.MapPost("/auth/logout",async (StudioExternalIdentity identity,CancellationToken ct)=>{await identity.LogoutAsync(ct);return new { loggedOut=true };});
            if(profile.Identity.Provider=="LocalAdmin")
            {
                group.MapPost("/auth/local-login",(LocalLogin request,StudioExternalIdentity identity,CancellationToken ct)=>identity.LoginLocalAsync(request.TenantNId,request.LoginName,request.Password,ct)).WithMetadata(new PublicEndpoint()).RequireRateLimiting("studio-local-login");
                group.MapPost("/auth/local-change-password",async (ChangePassword request,StudioExternalIdentity identity,CancellationToken ct)=>{await identity.ChangePasswordAsync(request.CurrentPassword,request.NewPassword,ct);return new { reloginRequired=true };});
            }
            else
            {
                group.MapGet("/auth/login",async (HttpContext http,StudioExternalLogin login,string? returnPath,CancellationToken ct)=>
                {
                    if(profile.Identity.Provider=="Oidc")return Results.Challenge(new AuthenticationProperties { RedirectUri=StudioExternalIdentity.SafeReturnPath(returnPath) },["StudioOidc"]);
                    return Results.Redirect(await login.BeginMesCodeAsync(returnPath,ct));
                }).WithMetadata(new PublicEndpoint());
                if(profile.Identity.Provider=="MesCode")group.MapGet("/auth/callback",async (StudioExternalLogin login,string? code,string? state,CancellationToken ct)=>Results.Redirect(profile.PathBase+await login.CompleteMesCodeAsync(code,state,ct))).WithMetadata(new PublicEndpoint());
            }
        }
        group.MapGet("/deployment/profile",(HttpContext http)=>new { schemaVersion=1,profile.ProfileVersion,profile.DeploymentMode,profile.InstalledModules,enabledModules=profile.ActiveModules,identityProvider=profile.Identity.Provider,filesProvider=profile.Files.Provider,auditProvider=profile.Audit.Provider });
        group.MapGet("/health/capabilities",async(HttpContext http,IndustrialPlatform.PlatformStudio.Infrastructure.Integration.Embedded.StudioFileCapabilityProbe files,CancellationToken ct)=>new { files=await files.GetStatusAsync(ct),identity=new {provider=profile.Identity.Provider,status="Ready"},dataScope=new {status=Principal(http).FactoryNIds.Count>0?"Ready":profile.DeploymentMode=="PlatformNative"&&profile.Identity.NativeFactoryScopes.Length==0?"Unavailable":"Empty",reasonCode=Principal(http).FactoryNIds.Count>0?null:profile.DeploymentMode=="PlatformNative"&&profile.Identity.NativeFactoryScopes.Length==0?"STUDIO_FACTORY_SCOPE_UNAVAILABLE":"NO_AUTHORIZED_FACTORIES"} });
        group.MapGet("/deployment/inspect",async(HttpContext http,IStudioAuthorization auth,StudioServiceInitializer initializer,CancellationToken ct)=>{auth.RequirePermission(Principal(http),"studio.deployment.inspect");return await initializer.InspectLocalAsync(ct);});
        group.MapGet("/sources",([AsParameters] StudioQuery query,HttpContext http,SourceAdministration sources,CancellationToken ct)=>sources.ListAsync(Principal(http),query,ct));
        group.MapGet("/sources/{nId}/catalog",(string nId,string configurationVersion,HttpContext http,SourceCatalog catalog,CancellationToken ct)=>catalog.GetAsync(Principal(http),nId,configurationVersion,ct));
        group.MapPost("/sources",(SourceCommand request,HttpContext http,SourceAdministration sources,CancellationToken ct)=>sources.CreateAsync(Principal(http),request,Key(http),ct));
        group.MapPut("/sources/{nId}",(string nId,SourceCommand request,HttpContext http,SourceAdministration sources,CancellationToken ct)=>sources.UpdateAsync(Principal(http),nId,request,ct));
        group.MapPost("/sources/{nId}/test",(string nId,VersionCommand request,HttpContext http,SourceAdministration sources,CancellationToken ct)=>sources.TestAsync(Principal(http),nId,request,ct));
        group.MapPost("/sources/{nId}/enable",(string nId,VersionCommand request,HttpContext http,SourceAdministration sources,CancellationToken ct)=>sources.SetEnabledAsync(Principal(http),nId,true,request,ct));
        group.MapPost("/sources/{nId}/disable",(string nId,VersionCommand request,HttpContext http,SourceAdministration sources,CancellationToken ct)=>sources.SetEnabledAsync(Principal(http),nId,false,request,ct));
        group.MapPost("/datasets/preview",(DatasetPreview request,HttpContext http,DatasetExecution datasets,CancellationToken ct)=>datasets.PreviewAsync(Principal(http),request,ct));
        group.MapPost("/datasets/{nId}/execute",(string nId,DatasetExecute request,HttpContext http,DatasetExecution datasets,CancellationToken ct)=>datasets.ExecuteAsync(Principal(http),nId,request,ct));
        group.MapGet("/resources",([AsParameters] StudioQuery query,string kind,HttpContext http,StudioPublishing publishing,CancellationToken ct)=>publishing.ListAsync(Principal(http),kind,query,ct));
        group.MapPost("/resources",(ResourceCreate request,HttpContext http,StudioPublishing publishing,CancellationToken ct)=>publishing.CreateAsync(Principal(http),request,Key(http),ct));
        group.MapGet("/resources/{nId}/draft",(string nId,HttpContext http,StudioPublishing publishing,CancellationToken ct)=>publishing.GetDraftAsync(Principal(http),nId,ct));
        group.MapPut("/resources/{nId}/draft",(string nId,DraftSave request,HttpContext http,StudioPublishing publishing,CancellationToken ct)=>publishing.SaveDraftAsync(Principal(http),nId,request,ct));
        group.MapPost("/resources/{nId}/validate",(string nId,JsonObject request,HttpContext http,StudioPublishing publishing,CancellationToken ct)=>publishing.ValidateAsync(Principal(http),nId,request["document"] as JsonObject??request,ct));
        group.MapPost("/resources/{nId}/publish",(string nId,ResourcePublish request,HttpContext http,StudioPublishing publishing,CancellationToken ct)=>publishing.PublishAsync(Principal(http),nId,request,Key(http),ct));
        group.MapPost("/resources/{nId}/rollback",(string nId,ResourceRollback request,HttpContext http,StudioPublishing publishing,CancellationToken ct)=>publishing.RollbackAsync(Principal(http),nId,request,Key(http),ct));
        foreach(var state in new[] {"enable","disable","delete","restore"})
            group.MapPost("/resources/{nId}/"+state,(string nId,VersionCommand request,HttpContext http,StudioPublishing publishing,CancellationToken ct)=>publishing.SetStateAsync(Principal(http),nId,state,request,ct));
        group.MapGet("/resources/{nId}/revisions/{revisionNId}",(string nId,string revisionNId,HttpContext http,StudioPublishing publishing,CancellationToken ct)=>publishing.GetRevisionDesignAsync(Principal(http),nId,revisionNId,ct));
        group.MapGet("/resources/{nId}/revisions",(string nId,HttpContext http,StudioPublishing publishing,CancellationToken ct)=>publishing.RevisionsAsync(Principal(http),nId,ct));
        group.MapGet("/resources/{nId}/grants",(string nId,HttpContext http,StudioGrants grants,CancellationToken ct)=>grants.GetAsync(Principal(http),nId,ct));
        group.MapPut("/resources/{nId}/grants",(string nId,GrantSave request,HttpContext http,StudioGrants grants,CancellationToken ct)=>grants.SaveAsync(Principal(http),nId,request,ct));
        group.MapGet("/catalog/actions",(HttpContext http,IEnumerable<IStudioAction> actions)=>actions.Where(a=>Principal(http).HasPermission(a.PermissionKey)).Select(a=>new {a.Key,a.ExecutorVersion,a.InputSchema,a.OutputSchema,a.PermissionKey,a.Mutating,idempotency=a.Mutating?"Required":"None",preview=a.Mutating?"Mock":"ReadOnly"}));
        group.MapGet("/catalog/components",()=>StudioDocuments.ComponentTypes.Select(type=>new {key=type,registryVersion="1",type}));
        group.MapGet("/catalog/data-policies",()=>new[] {new {key="public",description="服务器明确登记可公开的来源"},new {key="authorized-factories",description="可信工厂范围交集"}});
        group.MapGet("/registry/catalog",(HttpContext http,IEnumerable<IStudioAction> actions)=>new {components=StudioDocuments.ComponentTypes.Select(type=>new {key=type,type,registryVersion="1"}),codePages=new[]{new {key="studio.summary",registryVersion="1"}},actions=actions.Where(a=>Principal(http).HasPermission(a.PermissionKey)).Select(a=>new {a.Key,a.ExecutorVersion,a.InputSchema,a.OutputSchema,a.PermissionKey,a.Mutating})});
        if(profile.ActiveModules.Contains("LowCode",StringComparer.Ordinal))MapLowCode(group);
        group.MapStudioFiles();
        group.MapStudioAudit(profile);
        return endpoints;
    }
    private static void MapLowCode(RouteGroupBuilder group)
    {
        group.MapPost("/ledger/{modelNId}/upgrade-preview",(string modelNId,JsonObject request,HttpContext http,StudioLedger ledger,CancellationToken ct)=>ledger.UpgradePreviewAsync(Principal(http),modelNId,(request["targetDraftVersion"]??request).Deserialize<VersionCommand>(StudioCommands.JsonOptions)!,ct));
        group.MapPost("/ledger/{modelNId}/records/query",(string modelNId,LedgerQuery request,string? revisionNId,HttpContext http,StudioLedger ledger,CancellationToken ct)=>ledger.QueryAsync(Principal(http),modelNId,revisionNId,request,ct));
        group.MapPost("/ledger/{modelNId}/query",(string modelNId,JsonObject request,HttpContext http,StudioLedger ledger,CancellationToken ct)=>ledger.QueryAsync(Principal(http),modelNId,StudioJson.Text(request["revisionNId"]),LedgerQueryContract.Parse(request),ct));
        group.MapPost("/ledger/{modelNId}/records",(string modelNId,LedgerWrite request,HttpContext http,StudioLedger ledger,CancellationToken ct)=>ledger.WriteAsync(Principal(http),modelNId,null,request,Key(http),ct));
        group.MapGet("/ledger/{modelNId}/records/{nId}",(string modelNId,string nId,string? revisionNId,HttpContext http,StudioLedger ledger,CancellationToken ct)=>ledger.GetAsync(Principal(http),modelNId,nId,revisionNId,ct));
        group.MapPut("/ledger/{modelNId}/records/{nId}",(string modelNId,string nId,LedgerWrite request,HttpContext http,StudioLedger ledger,CancellationToken ct)=>ledger.WriteAsync(Principal(http),modelNId,nId,request,Key(http),ct));
        foreach(var state in new[] {"delete","restore"})group.MapPost("/ledger/{modelNId}/records/{nId}/"+state,(string modelNId,string nId,VersionCommand request,HttpContext http,StudioLedger ledger,CancellationToken ct)=>ledger.SetStateAsync(Principal(http),modelNId,nId,state,request,Key(http),ct));
        group.MapGet("/pages/{nId}/contract",(string nId,string? revisionNId,HttpContext http,StudioPages pages,CancellationToken ct)=>pages.ContractAsync(Principal(http),nId,revisionNId,ct));
        group.MapGet("/pages/{nId}/runtime",(string nId,string? revisionNId,HttpContext http,StudioPages pages,CancellationToken ct)=>pages.RuntimeAsync(Principal(http),nId,new(revisionNId,Parameters(http)),ct));
        group.MapPost("/pages/{nId}/runtime",(string nId,StudioPageInput request,HttpContext http,StudioPages pages,CancellationToken ct)=>pages.RuntimeAsync(Principal(http),nId,request,ct));
        group.MapPost("/pages/{nId}/preview",(string nId,StudioPageInput request,HttpContext http,StudioPages pages,CancellationToken ct)=>pages.PreviewAsync(Principal(http),nId,request,ct));
        group.MapPost("/pages/preview",(StudioPageInput request,HttpContext http,StudioPages pages,CancellationToken ct)=>pages.PreviewAsync(Principal(http),null,request,ct));
        group.MapPost("/actions/preview",(JsonObject request,HttpContext http,StudioPages pages,CancellationToken ct)=>pages.PreviewActionAsync(Principal(http),(request["pageDocument"]??request["document"])!.AsObject(),StudioJson.RequiredString(request,"actionId"),request["context"] as JsonObject,ct));
        group.MapPost("/pages/{nId}/actions/{actionId}/execute",(string nId,string actionId,StudioPageInput request,HttpContext http,StudioPages pages,CancellationToken ct)=>pages.ExecuteActionAsync(Principal(http),nId,actionId,request,Key(http),ct));
        group.MapGet("/operations/{operationKey}",(string operationKey,HttpContext http,StudioPages pages,CancellationToken ct)=>pages.GetOperationAsync(Principal(http),operationKey,ct));
    }
    private static JsonObject Parameters(HttpContext http)
    {
        if(http.Request.Query["parameters"].FirstOrDefault() is not {} text)return new();
        return JsonNode.Parse(text)?.AsObject()??throw new StudioException("STUDIO_VALIDATION","页面参数无效。");
    }
    internal static StudioPrincipal Principal(HttpContext http)=>(StudioPrincipal)http.Items["StudioPrincipal"]!;
    internal static string Key(HttpContext http)=>http.Request.Headers["Idempotency-Key"].FirstOrDefault()??"";
    private static IResult Error(HttpContext http,int status,string code,string message,string? path)=>Results.Json(new ApiResult { Success=false,Code=code,Message=message,Data=new {errors=new[]{new {code,path,message}}},TraceId=http.TraceIdentifier },statusCode:status);
}
