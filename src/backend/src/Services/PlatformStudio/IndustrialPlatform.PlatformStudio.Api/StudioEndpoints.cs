using System.Globalization;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.Json;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Data;
using IndustrialPlatform.PlatformStudio.Application.Dashboard;
using IndustrialPlatform.PlatformStudio.Application.Ledger;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Application.Report;
using IndustrialPlatform.PlatformStudio.Infrastructure.Report;
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
using Microsoft.Extensions.Hosting;

namespace IndustrialPlatform.PlatformStudio.Api;

/// <summary>Studio同一HTTP边界；所有业务入口取得可信身份，外部cookie写入必须通过CSRF。</summary>
public static class StudioEndpoints
{
    private sealed record PublicEndpoint;
    public sealed record LocalLogin(string TenantNId,string LoginName,string Password);
    public sealed record ChangePassword(string CurrentPassword,string NewPassword);
    public sealed record ReportCancelInput(string StateVersion);
    public sealed record ReportRetryInput(string StateVersion, string Mode);
    public sealed record ReportAssetInput(string FileNId, string ReferenceNId);
    public sealed record ReportResolutionInput(string StateVersion, string Reason);
    public sealed record WorkbookCreateInput(string Name, string? SourceFileNId, string? SourceArtifactNId);
    public sealed record WorkbookSessionInput(string Mode, string BaseContentVersion);
    public sealed record WorkbookCopyInput(string Name, string BaseContentVersion);
    public sealed record WorkbookEditInput(string Name, string OptimisticVersion, Guid ConcurrencyVersion);
    public sealed record WorkbookVersionInput(string BaseContentVersion, string? Note);
    public sealed record ReportRunInput(string? RevisionNId, JsonObject? Parameters, string Format, JsonArray? Sort = null);
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
        if (profile.ActiveModules.Contains("Report", StringComparer.Ordinal))
        {
            endpoints.MapStudioWopi(profile);
            group.MapPost("/reports/{nId}/assets", (string nId, ReportAssetInput input, HttpContext http, ReportResources assets, CancellationToken ct) => assets.ImportAsync(Principal(http), nId, input.FileNId, input.ReferenceNId, Key(http), ct));
            group.MapPost("/report-runs/{runNId}/resolve", (string runNId, ReportResolutionInput input, HttpContext http, ReportReconciliation resolution, CancellationToken ct) => resolution.ResolveAsync(Principal(http), runNId, input.StateVersion, input.Reason, ct));
            group.MapPost("/reports/plan", (ReportPreviewInput input, HttpContext http, ReportDesign reports, CancellationToken ct) => reports.PlanAsync(Principal(http), input, ct));
            group.MapGet("/reports/{nId}/runtime", (string nId, string? revisionNId, HttpContext http, ReportDesign reports, CancellationToken ct) => reports.RuntimeAsync(Principal(http), nId, revisionNId, ct));
            group.MapPost("/reports/query-preview", (ReportQueryInput input, HttpContext http, ReportDesign reports, CancellationToken ct) => reports.QueryAsync(Principal(http), null, input, ct));
            group.MapPost("/reports/{nId}/query", (string nId, ReportQueryInput input, HttpContext http, ReportDesign reports, CancellationToken ct) => reports.QueryAsync(Principal(http), nId, input, ct));
            group.MapPost("/reports/{nId}/query-options", (string nId, ReportQueryInput input, HttpContext http, ReportDesign reports, CancellationToken ct) => reports.QueryOptionsAsync(Principal(http), nId, input, ct));
            group.MapGet("/reports/{nId}/binding-contract", (string nId, string revisionNId, HttpContext http, ReportDesign reports, CancellationToken ct) => reports.BindingContractAsync(Principal(http), nId, revisionNId, ct));
            group.MapPost("/reports/binding-contract", (ReportPreviewInput input, HttpContext http, ReportDesign reports, CancellationToken ct) => reports.DraftBindingContractAsync(Principal(http), input, ct));
            group.MapGet("/workbooks", (HttpContext http, StudioWorkbooks books, CancellationToken ct) => books.ListAsync(Principal(http), ct));
            group.MapPost("/workbooks", (WorkbookCreateInput input, HttpContext http, StudioWorkbooks books, ReportOutputs reports, CancellationToken ct) => (input.SourceFileNId is not null, input.SourceArtifactNId is not null) switch { (true, false) => books.ImportAsync(Principal(http), input.Name, input.SourceFileNId!, Key(http), ct), (false, true) => books.ImportArtifactAsync(Principal(http), input.Name, input.SourceArtifactNId!, Key(http), reports, ct), _ => throw new StudioException("STUDIO_VALIDATION", "请选择一个xlsx文件或报告产物。") });
            group.MapPost("/workbooks/{nId}/copy", (string nId, WorkbookCopyInput input, HttpContext http, StudioWorkbooks books, CancellationToken ct) => books.CopyAsync(Principal(http), nId, input.Name, input.BaseContentVersion, Key(http), ct));
            group.MapGet("/workbooks/{nId}/versions", (string nId, HttpContext http, StudioWorkbooks books, CancellationToken ct) => books.VersionsAsync(Principal(http), nId, ct));
            group.MapPost("/workbooks/{nId}/edit", (string nId, WorkbookEditInput input, HttpContext http, StudioWorkbooks books, CancellationToken ct) => books.EditAsync(Principal(http), nId, input.Name, new(input.OptimisticVersion, input.ConcurrencyVersion), ct));
            group.MapPost("/workbooks/{nId}/versions", (string nId, WorkbookVersionInput input, HttpContext http, StudioWorkbooks books, CancellationToken ct) => books.SaveVersionAsync(Principal(http), nId, input.BaseContentVersion, input.Note, ct));
            group.MapGet("/workbooks/{nId}/sessions/{sessionNId}", (string nId, string sessionNId, HttpContext http, StudioWorkbooks books, CancellationToken ct) => books.StateAsync(Principal(http), nId, sessionNId, ct));
            group.MapGet("/workbooks/{nId}/versions/{versionNId}/content", async (string nId, string versionNId, HttpContext http, StudioWorkbooks books, CancellationToken ct) => Results.Stream(await books.ReadVersionAsync(Principal(http), nId, versionNId, ct), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nId + ".xlsx"));
            group.MapPost("/workbooks/{nId}/archive", (string nId, VersionCommand input, HttpContext http, StudioWorkbooks books, CancellationToken ct) => books.ArchiveAsync(Principal(http), nId, input, ct));
            group.MapPost("/workbooks/{nId}/sessions", async (string nId, WorkbookSessionInput input, HttpContext http, StudioWorkbooks books, CancellationToken ct) => profile.Office.Admitted ? Results.Ok(ApiResult.Ok(await books.CreateSessionAsync(Principal(http), nId, input.Mode, input.BaseContentVersion, new Uri(profile.Office.EditorAction!), new Uri(profile.Office.PublicWopiBase!), ct))) : Results.Json(ApiResult.Fail<object?>("OFFICE_UNAVAILABLE", "Office发行包及编辑器尚未准入；已有xlsx内容版本保留。"), statusCode: 503));
            group.MapPost("/workbooks/{nId}/sessions/{sessionNId}/close", async (string nId, string sessionNId, HttpContext http, StudioWorkbooks books, CancellationToken ct) => { await books.CloseAsync(Principal(http), nId, sessionNId, ct); return new { closed = true }; });
            group.MapPost("/workbooks/{nId}/sessions/{sessionNId}/renew", (string nId, string sessionNId, HttpContext http, StudioWorkbooks books, CancellationToken ct) => books.RenewAsync(Principal(http), nId, sessionNId, ct));
            group.MapGet("/report-runs/{runNId}", (string runNId, HttpContext http, ReportOutputs outputs, CancellationToken ct) => outputs.DetailAsync(Principal(http), runNId, ct));
            group.MapGet("/report-runs", (int? page, int? pageSize, string? resourceNId, HttpContext http, ReportOutputs outputs, CancellationToken ct) => outputs.ListAsync(Principal(http), page ?? 1, pageSize ?? 20, ct, resourceNId));
            group.MapGet("/report-runs/{runNId}/artifacts/{artifactNId}/content", async (string runNId, string artifactNId, HttpContext http, ReportOutputs outputs, CancellationToken ct) => { var result = await outputs.ArtifactAsync(Principal(http), runNId, artifactNId, ct); var extension = result.ContentType switch { "application/pdf" => ".pdf", "text/html" => ".html", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx", _ => ".json" }; http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; img-src data:; sandbox"; return Results.Stream(result.Content, result.ContentType, artifactNId + extension); });
            group.MapPost("/report-runs/{runNId}/cancel", (string runNId, ReportCancelInput input, HttpContext http, IndustrialPlatform.PlatformStudio.Application.Report.ReportRuns runs, CancellationToken ct) => runs.CancelAsync(Principal(http), runNId, input.StateVersion, ct));
            group.MapPost("/report-runs/{runNId}/retry", async (string runNId, ReportRetryInput input, HttpContext http, ReportRetries retries, CancellationToken ct) => { if (!profile.Renderer.Admitted) throw new StudioException("STUDIO_RENDERER_UNAVAILABLE", "报告分页引擎尚未准入。", 503); return Results.Json(ApiResult.Ok(await retries.RetryAsync(Principal(http), runNId, input.StateVersion, input.Mode, Key(http), http.TraceIdentifier, ct)), statusCode: 202); });
            group.MapPost("/reports/preview", async (ReportPreviewInput input, HttpContext http, ReportPreview preview, CancellationToken ct) =>
            {
                if (!profile.Renderer.Admitted) throw new StudioException("STUDIO_RENDERER_UNAVAILABLE", "报告分页引擎尚未准入。", 503);
                http.Response.Headers.CacheControl = "no-store";
                return Results.Json(ApiResult.Ok(await preview.RenderAsync(Principal(http), input, ct)));
            });
            group.MapGet("/reports/previews/{previewNId}", (string previewNId, HttpContext http, ReportOutputs outputs, CancellationToken ct) => outputs.PreviewAsync(Principal(http), previewNId, ct));
            group.MapPost("/reports/previews/{previewNId}/cancel", (string previewNId, ReportCancelInput input, HttpContext http, ReportRuns runs, CancellationToken ct) => runs.CancelAsync(Principal(http), previewNId, input.StateVersion, ct));
            group.MapGet("/reports/previews/{previewNId}/pages/{pageNo:int}/content", async (string previewNId, int pageNo, HttpContext http, ReportOutputs outputs, CancellationToken ct) => { http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; img-src data:; font-src data:; sandbox"; http.Response.Headers.CacheControl = "no-store"; return Results.Stream(await outputs.PreviewPageAsync(Principal(http), previewNId, pageNo, ct), "text/html; charset=utf-8"); });
            group.MapPost("/reports/{nId}/runs", async (string nId, ReportRunInput input, HttpContext http, StudioPublishing publishing, IStudioAuthorization authorization, ReportRuns runs, CancellationToken ct) =>
            {
                if (!profile.Renderer.Admitted) throw new StudioException("STUDIO_RENDERER_UNAVAILABLE", "报告分页引擎尚未准入。", 503);
                var (resource, revision) = await publishing.RequireRuntimeAsync(Principal(http), nId, input.RevisionNId, ct);
                if (resource.Kind is not ("Report" or "ReportBundle")) throw new StudioException("STUDIO_NOT_FOUND", "报告不存在。", 404);
                if (input.Format is not ("Pdf" or "Html" or "Xlsx")) throw new StudioException("REPORT_FORMAT_UNSUPPORTED", "输出格式未登记。", 422);
                await authorization.RequireAsync(Principal(http), resource, "studio.report.export", "Export", ct);
                var document = StudioPublishing.Parse(revision.DocumentJson);
                var parameters = DatasetValues.Parameters(Principal(http), document, input.Parameters ?? new());
                var sort = ReportDesign.ResolveSort(document, input.Sort);
                var result = await runs.AcceptFrozenAsync(Principal(http), new("Production", revision.Id, null, parameters.ToJsonString(), input.Format, http.TraceIdentifier, sort.ToJsonString()), Key(http), ct);
                return Results.Json(ApiResult.Ok(result), statusCode: 202);
            });
        }
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
        group.MapGet("/catalog/components",()=>StudioDocuments.ComponentTypes.Select(type=>(object)new {key=type,registryVersion="1",type}).Concat(DashboardComponentRegistry.Entries.Select(entry=>(object)new {key=entry.Key,registryVersion=entry.Version,type=entry.Key,label=entry.Label,category=entry.Category,propsSchema=entry.PropsSchema,bindableProperties=entry.BindableProperties,events=entry.Events})));
        group.MapGet("/catalog/data-policies",()=>new[] {new {key="public",description="服务器明确登记可公开的来源"},new {key="authorized-factories",description="可信工厂范围交集"}});
        group.MapGet("/registry/catalog",(HttpContext http,IEnumerable<IStudioAction> actions)=>new {components=StudioDocuments.ComponentTypes.Select(type=>(object)new {key=type,type,registryVersion="1"}).Concat(DashboardComponentRegistry.Entries.Select(entry=>(object)new {key=entry.Key,type=entry.Key,registryVersion=entry.Version,label=entry.Label,category=entry.Category,propsSchema=entry.PropsSchema,bindableProperties=entry.BindableProperties,events=entry.Events})),codePages=new[]{new {key="studio.summary",registryVersion="1"}},actions=actions.Where(a=>Principal(http).HasPermission(a.PermissionKey)).Select(a=>new {a.Key,a.ExecutorVersion,a.InputSchema,a.OutputSchema,a.PermissionKey,a.Mutating})});
        if(profile.ActiveModules.Contains("LowCode",StringComparer.Ordinal))MapLowCode(group);
        if(profile.ActiveModules.Contains("Dashboard",StringComparer.Ordinal))MapDashboard(group);
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
    private static void MapDashboard(RouteGroupBuilder group)
    {
        group.MapGet("/dashboards/{nId}/screens", (string nId, HttpContext http, DashboardScreens screens, CancellationToken ct) => {
            http.Response.Headers.CacheControl = "no-store";
            return screens.ListAsync(Principal(http), nId, ct);
        });
        group.MapPost("/dashboards/{nId}/screens", (string nId, CreateDashboardScreen request, HttpContext http, DashboardScreens screens, CancellationToken ct) => screens.CreateAsync(Principal(http), nId, request, ct));
        group.MapDelete("/dashboards/{nId}/screens/{id:guid}", async (string nId, Guid id, HttpContext http, DashboardScreens screens, CancellationToken ct) => {
            await screens.RevokeAsync(Principal(http), nId, id, ct);
            return new { revoked = true };
        });
        group.MapGet("/playlists/{nId}/screens", (string nId, HttpContext http, DashboardScreens screens, CancellationToken ct) => {
            http.Response.Headers.CacheControl = "no-store";
            return screens.ListAsync(Principal(http), nId, ct, "Playlist");
        });
        group.MapPost("/playlists/{nId}/screens", (string nId, CreateDashboardScreen request, HttpContext http, DashboardScreens screens, CancellationToken ct) => screens.CreateAsync(Principal(http), nId, request, ct, "Playlist"));
        group.MapDelete("/playlists/{nId}/screens/{id:guid}", async (string nId, Guid id, HttpContext http, DashboardScreens screens, CancellationToken ct) => {
            await screens.RevokeAsync(Principal(http), nId, id, ct, "Playlist");
            return new { revoked = true };
        });
        group.MapGet("/screens/{id:guid}/runtime", async (Guid id, HttpContext http, DashboardScreens screens, CancellationToken ct) =>
            await ScreenRuntimeAsync(id, http, screens, ct)).WithMetadata(new PublicEndpoint()).RequireRateLimiting("studio-dashboard-screen");
        group.MapGet("/dashboards/{nId}/definition",(string nId,string? revisionNId,HttpContext http,DashboardNavigation dashboards,CancellationToken ct)=>dashboards.DefinitionAsync(Principal(http),nId,revisionNId,ct));
        group.MapGet("/dashboards/{nId}/runtime",(string nId,string? revisionNId,HttpContext http,DashboardNavigation dashboards,CancellationToken ct)=>dashboards.RuntimeAsync(Principal(http),nId,revisionNId,Parameters(http),ct,InstanceIds(http)));
        group.MapPost("/dashboards/preview",(JsonObject request,HttpContext http,DashboardNavigation dashboards,CancellationToken ct)=>dashboards.PreviewAsync(Principal(http),StudioJson.Text(request["nId"]),request["document"]?.AsObject()??throw new StudioException("STUDIO_VALIDATION","看板文档缺失。"),request["parameters"]?.AsObject()??new JsonObject(),StudioJson.Text(request["selectedComponentId"]),request["sampleRow"]?.AsObject(),ct, request["instanceIds"] is JsonArray ids ? ids.Select(item => StudioJson.Text(item) ?? throw new StudioException("STUDIO_VALIDATION", "实例标识无效。")).ToArray() : null));
        group.MapPost("/dashboards/{nId}/navigation/resolve",(string nId,DashboardNavigationRequest request,HttpContext http,DashboardNavigation dashboards,CancellationToken ct)=>dashboards.ResolveAsync(Principal(http),nId,request,ct));
        group.MapPost("/dashboards/{nId}/preview/navigation",(string nId,JsonObject request,HttpContext http,DashboardNavigation dashboards,CancellationToken ct)=>dashboards.PreviewResolveAsync(Principal(http),nId,request["document"]?.AsObject()??throw new StudioException("STUDIO_VALIDATION","看板文档缺失。"),new(null,StudioJson.Text(request["ruleId"]),request["rowKey"] as JsonObject,request["parameters"] as JsonObject),ct));
        group.MapGet("/playlists/{nId}/runtime",(string nId,string? revisionNId,HttpContext http,DashboardNavigation dashboards,CancellationToken ct)=>dashboards.PlaylistRuntimeAsync(Principal(http),nId,revisionNId,ct));
    }
    private static async Task<JsonObject> ScreenRuntimeAsync(Guid id, HttpContext http, DashboardScreens screens, CancellationToken ct)
    {
        using var timing = http.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment() ? new Activity("studio.screen.runtime").Start() : null;
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        var text = http.Request.Query["navigation"].FirstOrDefault();
        if (text?.Length > 8192) throw new StudioException("STUDIO_LIMIT", "大屏明细路径超过预算。", 429);
        var navigation = text is null ? null : JsonNode.Parse(text) as JsonArray ?? throw new StudioException("STUDIO_VALIDATION", "大屏明细路径无效。");
        var result = await screens.RuntimeAsync(id, http.Request.Headers["X-Dashboard-Screen-Key"].FirstOrDefault(), ct, InstanceIds(http), http.Request.Query["knownRevisionNId"].FirstOrDefault(), navigation, http.Request.Query["playlistItemId"].FirstOrDefault());
        if (timing is not null)
        {
            timing.Stop();
            http.Response.Headers["Server-Timing"] = string.Join(", ", timing.Events.GroupBy(item => item.Name).Select(items =>
                items.Key + ";dur=" + items.Sum(item => Convert.ToDouble(item.Tags.Single(tag => tag.Key == "duration").Value, CultureInfo.InvariantCulture)).ToString("F2", CultureInfo.InvariantCulture))
                .Append("total;dur=" + timing.Duration.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture)));
        }
        return result;
    }
    private static string[]? InstanceIds(HttpContext http)
    {
        if (http.Request.Query["instanceIds"].FirstOrDefault() is not { } text) return null;
        if (text.Length > 8192 || JsonNode.Parse(text) is not JsonArray ids || ids.Count > 64) throw new StudioException("STUDIO_VALIDATION", "数据实例选择无效。");
        return ids.Select(item => StudioJson.Text(item) ?? throw new StudioException("STUDIO_VALIDATION", "实例标识无效。")).ToArray();
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
