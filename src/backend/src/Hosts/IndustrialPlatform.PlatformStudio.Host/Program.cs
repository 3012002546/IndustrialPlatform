using System.Text;
using System.Threading.RateLimiting;
using IndustrialPlatform.PlatformStudio.Api;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Identity;
using IndustrialPlatform.PlatformStudio.Infrastructure.Initialization;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using IndustrialPlatform.PlatformStudio.Host;
using Microsoft.AspNetCore.RateLimiting;

var builder=WebApplication.CreateBuilder(args);
builder.Services.AddAuthentication();builder.Services.AddAuthorization();
builder.Services.AddPlatformStudio(builder.Configuration);
builder.Services.AddRateLimiter(options=>
{
    options.AddPolicy("studio-local-login",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions { PermitLimit=10,Window=TimeSpan.FromMinutes(1),QueueLimit=0,AutoReplenishment=true }));
    options.OnRejected=async (context,token)=>{context.HttpContext.Response.StatusCode=429;await context.HttpContext.Response.WriteAsJsonAsync(new {success=false,code="STUDIO_LOGIN_RATE_LIMIT",message="登录尝试过于频繁，请稍后重试。"},token);};
});
await using var app=builder.Build();
var profile=app.Services.GetService<StudioDeploymentProfile>();
if(profile is null) {Console.Error.WriteLine("PlatformStudio.Enabled未显式开启，独立宿主不启动。");return 1;}
if(profile.DeploymentMode!="ExternalMes") {Console.Error.WriteLine("独立宿主仅接收ExternalMes部署输入。");return 1;}
var serviceInitializer=app.Services.GetRequiredService<StudioServiceInitializer>();
var commandResult=await new StudioCommandLine(profile,app.Services.GetRequiredService<StudioDatabase>(),serviceInitializer,app.Services.GetRequiredService<TimeProvider>()).ExecuteAsync(args,Console.Out,CancellationToken.None);
if(commandResult is {} exitCode)return exitCode;
var initialState=await serviceInitializer.InspectLocalAsync(CancellationToken.None);
if(!initialState.Ready){Console.Error.WriteLine("STUDIO_NOT_READY：须先使用受控migration账号执行--studio-initialize，再以runtime账号启动。");return 1;}
var administration=args.FirstOrDefault(a=>a is "--studio-create-admin" or "--studio-reset-admin" or "--studio-disable-admin");
if(administration is not null)
{
    string? Argument(string name){var i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:null;}
    var tenant=Argument("--tenant");var login=Argument("--login");
    if(tenant is null||login is null){Console.Error.WriteLine("管理员命令需要--tenant和--login。");return 1;}
    if(profile.Identity.Provider!="LocalAdmin"){Console.Error.WriteLine("管理员命令仅用于显式LocalAdmin模式。");return 1;}
    string? password=null;
    if(administration!="--studio-disable-admin")
    {
        if(Console.IsInputRedirected){Console.Error.WriteLine("密码必须从受保护交互终端输入。");return 1;}
        Console.Write("一次性初始密码：");var secret=new StringBuilder();ConsoleKeyInfo key;
        while((key=Console.ReadKey(true)).Key!=ConsoleKey.Enter){if(key.Key==ConsoleKey.Backspace){if(secret.Length>0)secret.Length--;}else if(!char.IsControl(key.KeyChar)&&secret.Length<128)secret.Append(key.KeyChar);}
        Console.WriteLine();password=secret.ToString();secret.Clear();
    }
    await using var scope=app.Services.CreateAsyncScope();
    try{await scope.ServiceProvider.GetRequiredService<StudioExternalIdentity>().AdministerAsync(administration=="--studio-create-admin"?"create":administration=="--studio-reset-admin"?"reset":"disable",tenant,login,password,CancellationToken.None);Console.WriteLine("管理员操作已完成并记录审计。");return 0;}
    catch(StudioException ex){Console.Error.WriteLine(ex.Code+": "+ex.Message);return 1;}
}
if(!string.IsNullOrEmpty(profile.PathBase))app.UsePathBase(profile.PathBase);
app.Use(async(context,next)=>
{
    context.Response.Headers.XContentTypeOptions="nosniff";
    context.Response.Headers.ContentSecurityPolicy="frame-ancestors 'self' "+string.Join(' ',profile.AllowedFrameOrigins);
    if(context.Request.Path.StartsWithSegments("/api"))context.Response.Headers.CacheControl="no-store";
    await next(context);
});
app.UseRouting();app.UseRateLimiter();app.UseAuthentication();app.UseAuthorization();
app.MapGet("/health/live",()=>Results.Ok(new {status="Healthy",service="platform-studio"}));
app.MapGet("/health/ready",async(StudioServiceInitializer initializer,CancellationToken ct)=>{var state=await initializer.InspectLocalAsync(ct);return Results.Json(state,statusCode:state.Ready?200:503);});
app.MapPlatformStudio();
app.UseStaticFiles();app.MapFallback(async context=>
{
    if(context.Request.Path.StartsWithSegments("/api")||context.Request.Path.StartsWithSegments("/health")){context.Response.StatusCode=404;return;}
    var index=Path.Combine(app.Environment.WebRootPath??Path.Combine(app.Environment.ContentRootPath,"wwwroot"),"index.html");
    if(!File.Exists(index)){context.Response.StatusCode=404;return;}
    context.Response.ContentType="text/html";await context.Response.SendFileAsync(index);
});
await app.RunAsync();return 0;

public partial class Program;
