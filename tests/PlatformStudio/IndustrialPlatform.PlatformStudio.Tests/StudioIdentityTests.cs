using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Api;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Identity;
using IndustrialPlatform.PlatformStudio.Infrastructure.Initialization;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class StudioIdentityTests
{
    internal sealed class ApiFixture : IAsyncDisposable
    {
        private readonly string _folder;
        private readonly WebApplication _app;
        public HttpClient Client { get; }
        public IServiceProvider Services=>_app.Services;
        private readonly Dictionary<string,string> _cookies=new(StringComparer.Ordinal);
        private ApiFixture(string folder,WebApplication app,HttpClient client){_folder=folder;_app=app;Client=client;}
        public static async Task<ApiFixture> CreateAsync(string provider="LocalAdmin",IStudioMesBridge? bridge=null,bool dashboard=false)
        {
            var folder=Path.Combine(Path.GetTempPath(),"studio-api-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            var builder=WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName="Testing" });builder.WebHost.UseTestServer();
            var config=new Dictionary<string,string?> { ["PlatformStudio:Enabled"]="true",["PlatformStudio:DeploymentMode"]="ExternalMes",["PlatformStudio:InstalledModules:0"]="LowCode",["PlatformStudio:Identity:Provider"]=provider,["PlatformStudio:Identity:LocalAdminEnabled"]=(provider=="LocalAdmin").ToString(CultureInfo.InvariantCulture),["PlatformStudio:Identity:IssuerKey"]="test-mes",["PlatformStudio:Identity:PolicyVersion"]="1",["PlatformStudio:Identity:DataProtectionKeyPath"]=Path.Combine(folder,"keys"),["PlatformStudio:Files:Provider"]="Disabled",["PlatformStudio:Audit:Provider"]="Embedded",["PlatformStudio:Jobs:Provider"]="Disabled",["PlatformStudio:PublicOrigin"]="https://localhost",["PlatformStudio:Database:Provider"]="Sqlite",["PlatformStudio:Database:SqliteFile"]=Path.Combine(folder,"studio.db"),["PlatformStudio:Identity:AuthorityKey"]="mes",["PlatformStudio:Identity:ContextEndpointKey"]="mes",["PlatformStudio:Identity:Authorities:mes:Issuer"]="https://issuer.example",["PlatformStudio:Identity:Authorities:mes:ClientId"]="studio",["PlatformStudio:Identity:Authorities:mes:AuthorizationEndpoint"]="https://issuer.example/login",["PlatformStudio:Identity:TenantMappings:0:ExternalTenantId"]="external-a",["PlatformStudio:Identity:TenantMappings:0:TenantNId"]="tenant-a",["PlatformStudio:Identity:RoleMappings:0:ExternalTenantId"]="external-a",["PlatformStudio:Identity:RoleMappings:0:ExternalRoleId"]="designer",["PlatformStudio:Identity:RoleMappings:0:RoleNId"]=StudioPrincipal.ExternalNId("test-mes","external-a","designer","mes-role-"),["PlatformStudio:Identity:RoleMappings:0:Permissions:0"]="studio.lowcode.view" };
            if(dashboard)config["PlatformStudio:InstalledModules:1"]="Dashboard";
            builder.Configuration.AddInMemoryCollection(config);builder.Services.AddAuthentication();builder.Services.AddAuthorization();builder.Services.AddRateLimiter(o=>o.AddConcurrencyLimiter("studio-local-login",policy=>{policy.PermitLimit=10;policy.QueueLimit=0;}));
            builder.Services.AddPlatformStudio(builder.Configuration);
            if(bridge is not null)builder.Services.AddSingleton(bridge);
            var app=builder.Build();await app.Services.GetRequiredService<StudioServiceInitializer>().InitializeLocalAsync(CancellationToken.None);
            app.MapGet("/fixture/execution-evidence", async (StudioExternalIdentity identity, CancellationToken ct) => await identity.CaptureExecutionEvidenceAsync(await identity.GetCurrentAsync(ct), ct));
            app.UseRouting();app.UseRateLimiter();app.UseAuthentication();app.UseAuthorization();app.MapPlatformStudio();await app.StartAsync();
            var client=app.GetTestClient();client.BaseAddress=new Uri("https://localhost");var fixture=new ApiFixture(folder,app,client);await fixture.CsrfAsync();return fixture;
        }
        public async Task<JsonObject> CsrfAsync()
        {
            var response=await SendAsync(HttpMethod.Get,"/api/v1/studio/auth/csrf");var json=(await response.Content.ReadFromJsonAsync<JsonObject>())!;
            Client.DefaultRequestHeaders.Remove("X-Studio-CSRF");Client.DefaultRequestHeaders.Add("X-Studio-CSRF",json["data"]!["token"]!.GetValue<string>());return json;
        }
        public async Task<HttpResponseMessage> SendAsync(HttpMethod method,string url,object? body=null)
        {
            using var request=new HttpRequestMessage(method,url);
            if(body is not null)request.Content=JsonContent.Create(body);
            if(_cookies.Count>0)request.Headers.Add("Cookie",string.Join("; ",_cookies.Select(p=>p.Key+"="+p.Value)));
            var response=await Client.SendAsync(request);
            if(response.Headers.TryGetValues("Set-Cookie",out var cookies))foreach(var cookie in cookies){var pair=cookie.Split(';')[0].Split('=',2);if(pair.Length==2)_cookies[pair[0]]=pair[1];}
            return response;
        }
        public async Task CreateAdminAsync()
        {
            await using var scope=Services.CreateAsyncScope();await scope.ServiceProvider.GetRequiredService<StudioExternalIdentity>().AdministerAsync("create","tenant-a","admin","Initial!Studio123",CancellationToken.None);
        }
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();await _app.DisposeAsync();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(_folder))Directory.Delete(_folder,true);
        }
    }
    [Fact]
    public async Task Background_execution_reuses_source_session_without_cookie_and_rejects_revocation()
    {
        await using var f = await ApiFixture.CreateAsync(); await f.CreateAdminAsync();
        await f.SendAsync(HttpMethod.Post,"/api/v1/studio/auth/local-login",new { tenantNId="tenant-a",loginName="admin",password="Initial!Studio123" });
        await f.SendAsync(HttpMethod.Post,"/api/v1/studio/auth/local-change-password",new { currentPassword="Initial!Studio123",newPassword="Changed!Studio456" });
        await f.SendAsync(HttpMethod.Post,"/api/v1/studio/auth/local-login",new { tenantNId="tenant-a",loginName="admin",password="Changed!Studio456" });
        var response = await f.SendAsync(HttpMethod.Get, "/fixture/execution-evidence"); response.EnsureSuccessStatusCode();
        var evidence = await response.Content.ReadAsStringAsync();
        await using var scope = f.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<StudioExternalIdentity>();
        Assert.Equal("tenant-a", (await identity.RefreshExecutionAsync(evidence, default)).TenantNId);
        await f.SendAsync(HttpMethod.Post, "/api/v1/studio/auth/logout");
        await Assert.ThrowsAsync<StudioException>(() => identity.RefreshExecutionAsync(evidence, default));
    }
    [Fact]
    public async Task Local_first_login_requires_password_change_and_revokes_old_sessions()
    {
        await using var f=await ApiFixture.CreateAsync();await f.CreateAdminAsync();
        var login=await f.SendAsync(HttpMethod.Post,"/api/v1/studio/auth/local-login",new {tenantNId="tenant-a",loginName="admin",password="Initial!Studio123"});
        Assert.Equal(HttpStatusCode.OK,login.StatusCode);
        Assert.Contains("__Host-StudioSession",string.Join(" ",login.Headers.GetValues("Set-Cookie")),StringComparison.Ordinal);
        var me=await f.SendAsync(HttpMethod.Get,"/api/v1/studio/auth/me");Assert.Equal(HttpStatusCode.OK,me.StatusCode);
        var json=(await me.Content.ReadFromJsonAsync<JsonObject>())!;Assert.True(json["data"]!["mustChangePassword"]!.GetValue<bool>());Assert.Empty(json["data"]!["principal"]!["factoryNIds"]!.AsArray());
        var forbidden=await f.SendAsync(HttpMethod.Get,"/api/v1/studio/resources?kind=LedgerModel");Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
        var change=await f.SendAsync(HttpMethod.Post,"/api/v1/studio/auth/local-change-password",new {currentPassword="Initial!Studio123",newPassword="Changed!Studio456"});Assert.Equal(HttpStatusCode.OK,change.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await f.SendAsync(HttpMethod.Get,"/api/v1/studio/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await f.SendAsync(HttpMethod.Post,"/api/v1/studio/auth/local-login",new {tenantNId="tenant-a",loginName="admin",password="Changed!Studio456"})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await f.SendAsync(HttpMethod.Get,"/api/v1/studio/resources?kind=LedgerModel")).StatusCode);
        var report=await f.SendAsync(HttpMethod.Post,"/api/v1/studio/reports/preview",new {});Assert.Equal(HttpStatusCode.NotFound,report.StatusCode);
        await using var scope=f.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<StudioDatabase>();
        var admin=await db.Client.Queryable<StudioLocalAdmin>().FirstAsync();Assert.StartsWith("$2",admin.PasswordHash,StringComparison.Ordinal);Assert.DoesNotContain("Changed!",admin.PasswordHash,StringComparison.Ordinal);
    }
    [Fact]
    public async Task Local_admin_dashboard_permissions_follow_the_enabled_module_boundary()
    {
        await using var f=await ApiFixture.CreateAsync(dashboard:true);await f.CreateAdminAsync();
        Assert.Equal(HttpStatusCode.OK,(await f.SendAsync(HttpMethod.Post,"/api/v1/studio/auth/local-login",new {tenantNId="tenant-a",loginName="admin",password="Initial!Studio123"})).StatusCode);
        var json=await (await f.SendAsync(HttpMethod.Get,"/api/v1/studio/auth/me")).Content.ReadFromJsonAsync<JsonObject>();
        var permissions=json!["data"]!["principal"]!["permissions"]!.AsArray().Select(item=>item!.GetValue<string>()).ToArray();
        Assert.Contains("studio.dashboard.view",permissions);Assert.Contains("studio.dashboard.design",permissions);Assert.Contains("studio.dashboard.publish",permissions);Assert.Contains("studio.dashboard.run",permissions);
        Assert.DoesNotContain(permissions,permission=>permission.StartsWith("studio.report.",StringComparison.Ordinal));Assert.DoesNotContain(permissions,permission=>permission.StartsWith("studio.trace.",StringComparison.Ordinal));Assert.DoesNotContain(permissions,permission=>permission.StartsWith("studio.workbook.",StringComparison.Ordinal));
    }
    [Fact]
    public async Task Wrong_password_locks_after_five_failures_and_csrf_is_required()
    {
        await using var f=await ApiFixture.CreateAsync();await f.CreateAdminAsync();
        f.Client.DefaultRequestHeaders.Remove("X-Studio-CSRF");
        Assert.Equal(HttpStatusCode.Forbidden,(await f.SendAsync(HttpMethod.Post,"/api/v1/studio/auth/local-login",new {tenantNId="tenant-a",loginName="admin",password="Initial!Studio123"})).StatusCode);
        await f.CsrfAsync();
        for(var i=0;i<5;i++)Assert.Equal(HttpStatusCode.Unauthorized,(await f.SendAsync(HttpMethod.Post,"/api/v1/studio/auth/local-login",new {tenantNId="tenant-a",loginName="admin",password="Wrong!Studio123"})).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,(await f.SendAsync(HttpMethod.Post,"/api/v1/studio/auth/local-login",new {tenantNId="tenant-a",loginName="admin",password="Initial!Studio123"})).StatusCode);
    }
    private sealed class Bridge : IStudioMesBridge
    {
        public int Exchanges {get;private set;}
        public bool Active {get;set;}=true;
        public bool Unavailable {get;set;}
        public Task<JsonObject> ExchangeAsync(JsonObject input,CancellationToken ct){Exchanges++;return Task.FromResult(new JsonObject {["issuer"]="https://issuer.example",["subjectId"]="subject-a",["tenantId"]="external-a",["contextHandle"]="secret-context-handle",["authVersion"]="a1",["expiresOn"]=DateTimeOffset.UtcNow.AddHours(1).ToString("O",CultureInfo.InvariantCulture)});}
        public Task<JsonObject> ContextAsync(JsonObject input,CancellationToken ct)
        {
            if(Unavailable)throw new StudioException("STUDIO_IDENTITY_UNAVAILABLE","fixture",503);
            return Task.FromResult(new JsonObject {["active"]=Active,["subjectId"]="subject-a",["tenantId"]="external-a",["roles"]=new JsonArray("designer"),["factoryIds"]=new JsonArray("unmapped-factory"),["authVersion"]="a1",["expiresOn"]=DateTimeOffset.UtcNow.AddHours(1).ToString("O",CultureInfo.InvariantCulture)});
        }
    }
    [Fact]
    public async Task MesCode_is_browser_bound_single_use_and_logout_revalidation_fails_closed()
    {
        var bridge=new Bridge();await using var f=await ApiFixture.CreateAsync("MesCode",bridge);
        var begin=await f.SendAsync(HttpMethod.Get,"/api/v1/studio/auth/login?returnPath=//evil.example");Assert.Equal(HttpStatusCode.Redirect,begin.StatusCode);
        var query=Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(begin.Headers.Location!.Query);var state=query["state"].ToString();
        var invalid=await f.SendAsync(HttpMethod.Get,"/api/v1/studio/auth/callback?code=test&state=invalid");Assert.Equal(HttpStatusCode.Unauthorized,invalid.StatusCode);Assert.Equal(0,bridge.Exchanges);
        // 每次失败清相关性cookie，必须重新发起登录。
        begin=await f.SendAsync(HttpMethod.Get,"/api/v1/studio/auth/login");state=Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(begin.Headers.Location!.Query)["state"].ToString();
        var callback=await f.SendAsync(HttpMethod.Get,"/api/v1/studio/auth/callback?code=test&state="+state);Assert.Equal(HttpStatusCode.Redirect,callback.StatusCode);Assert.Equal(1,bridge.Exchanges);
        Assert.Equal("/pc/studio/low-code",callback.Headers.Location!.OriginalString);
        var replay=await f.SendAsync(HttpMethod.Get,"/api/v1/studio/auth/callback?code=test&state="+state);Assert.Equal(HttpStatusCode.Unauthorized,replay.StatusCode);Assert.Equal(1,bridge.Exchanges);
        var me=await f.SendAsync(HttpMethod.Get,"/api/v1/studio/auth/me");Assert.Equal(HttpStatusCode.OK,me.StatusCode);var text=await me.Content.ReadAsStringAsync();Assert.DoesNotContain("secret-context-handle",text,StringComparison.Ordinal);
        await using(var scope=f.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<StudioDatabase>();var session=await db.Client.Queryable<StudioAuthSession>().FirstAsync();Assert.DoesNotContain("secret-context-handle",session.ContextHandleCipher!,StringComparison.Ordinal);
        }
        bridge.Unavailable=true;
        var unavailable=await f.SendAsync(HttpMethod.Post,"/api/v1/studio/pages/preview",new {document=new {schemaVersion=1}});Assert.Equal(HttpStatusCode.ServiceUnavailable,unavailable.StatusCode);
        bridge.Unavailable=false;bridge.Active=false;
        var revoked=await f.SendAsync(HttpMethod.Post,"/api/v1/studio/pages/preview",new {document=new {schemaVersion=1}});Assert.Equal(HttpStatusCode.Unauthorized,revoked.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await f.SendAsync(HttpMethod.Get,"/api/v1/studio/auth/me")).StatusCode);
    }
}
