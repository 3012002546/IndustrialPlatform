using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialPlatform.Identity.Domain.LoginSecurity;
using IndustrialPlatform.Identity.Domain.Passwords;
using IndustrialPlatform.Identity.Infrastructure.Passwords;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Integration;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using SqlSugar;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Identity;

/// <summary>独立宿主持久会话；cookie只含随机句柄，写操作每次重验来源身份。</summary>
public sealed class StudioExternalIdentity(StudioDatabase database,IStudioResourceStore store,StudioDeploymentProfile profile,IHttpContextAccessor accessor,IDataProtectionProvider protection,IStudioMesBridge bridge,TimeProvider clock,IServiceProvider services) : IStudioIdentity,IStudioSubjectDirectory
{
    private sealed record RequestEvidence(StudioAuthSession Session,StudioPrincipal Principal,bool Forced);
    public const string CookieName="__Host-StudioSession";
    public const string CorrelationCookie="__Host-StudioCorrelation";
    private ISqlSugarClient Db=>database.Client;
    private readonly IDataProtector _protector=protection.CreateProtector("IndustrialPlatform.Studio.Identity.v1");
    private readonly BcryptPasswordHasher _hasher=new();
    private static readonly string DummyHash=new BcryptPasswordHasher().Hash("Dummy!Studio-NoAccount-2026");
    public static string RandomToken()=>Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
    public static string SafeReturnPath(string? value)=>value is {Length:>0 and <=512}&&value.StartsWith("/pc/studio/",StringComparison.Ordinal)&&!value.Any(c=>c<32||c==(char)92||c=='#'||c=='%')?value:"/pc/studio/low-code";
    public string Protect(string value)=>_protector.Protect(value);
    public string Unprotect(string value)=>_protector.Unprotect(value);
    private HttpContext Http=>accessor.HttpContext??throw new StudioException("STUDIO_UNAUTHENTICATED","缺少浏览器会话。",401);
    public async Task<StudioPrincipal> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var force=!HttpMethods.IsGet(Http.Request.Method)||Http.Request.Path.Value?.Contains("/files/",StringComparison.Ordinal)==true;
        var (session,principal)=await ReadSessionAsync(force,cancellationToken);
        if(session.Provider=="LocalAdmin")
        {
            var admin=await Db.Queryable<StudioLocalAdmin>().Where(a=>a.TenantNId==session.TenantNId&&a.SubjectNId==session.SubjectNId).FirstUtcAsync(database.Provider,cancellationToken);
            var authPath=Http.Request.Path.Value??"";
            if(admin.MustChangePassword&&!authPath.EndsWith("/auth/me",StringComparison.Ordinal)&&!authPath.EndsWith("/auth/local-change-password",StringComparison.Ordinal)&&!authPath.EndsWith("/auth/logout",StringComparison.Ordinal)) throw new StudioException("STUDIO_PASSWORD_CHANGE_REQUIRED","首次登录必须修改密码。",403);
        }
        Http.Items["StudioSession"]=session;
        return principal;
    }
    public async Task<StudioPrincipal> RefreshAsync(StudioPrincipal principal,CancellationToken cancellationToken)
    {
        var (_,current)=await ReadSessionAsync(true,cancellationToken);
        if(current.TenantNId!=principal.TenantNId||current.SubjectNId!=principal.SubjectNId) throw new StudioException("STUDIO_UNAUTHENTICATED","会话主体已改变。",401);
        return current;
    }
    private async Task<(StudioAuthSession,StudioPrincipal)> ReadSessionAsync(bool force,CancellationToken cancellationToken,int attempt=0)
    {
        if(Http.Items["StudioIdentityEvidence"] is RequestEvidence evidence&&(!force||evidence.Forced))return (evidence.Session,evidence.Principal);
        var cookie=Http.Request.Cookies[CookieName];
        if(cookie is null||cookie.Length>128) throw new StudioException("STUDIO_UNAUTHENTICATED","请登录Studio。",401);
        var hash=StudioJson.Hash(cookie);var now=clock.GetUtcNow();
        var session=await Db.Queryable<StudioAuthSession>().Where(s=>s.SessionHash==hash).FirstUtcAsync(database.Provider,cancellationToken);
        if(session is null||session.RevokedOn is not null||session.ExpiresOn<=now||session.LastSeenOn<=now.AddMinutes(-30)||session.Provider!=profile.Identity.Provider||session.IssuerKey!=profile.Identity.IssuerKey) throw new StudioException("STUDIO_UNAUTHENTICATED","会话已过期或撤销。",401);
        var refreshed=false;
        var principal=JsonSerializer.Deserialize<StudioPrincipal>(session.PrincipalJson,StudioCommands.JsonOptions)??throw new StudioException("STUDIO_UNAUTHENTICATED","会话上下文无效。",401);
        if(session.Provider=="LocalAdmin")
        {
            var admin=await Db.Queryable<StudioLocalAdmin>().Where(a=>a.TenantNId==session.TenantNId&&a.SubjectNId==session.SubjectNId).FirstUtcAsync(database.Provider,cancellationToken);
            if(admin is null||!admin.Enabled||admin.AuthVersion.ToString(CultureInfo.InvariantCulture)!=session.AuthVersion) throw new StudioException("STUDIO_UNAUTHENTICATED","管理员状态或安全版本已改变。",401);
            principal=LocalPrincipal(admin,now,session.ExpiresOn);
        }
        else if(force||session.ContextCheckedOn<=now.AddSeconds(-30)||session.PolicyVersion!=StudioJson.Version(profile.Identity.PolicyVersion)||principal.ExpiresOn<=now)
        {
            JsonObject context;
            try {context=await bridge.ContextAsync(new() { ["contextHandle"]=_protector.Unprotect(session.ContextHandleCipher!),["operation"]=force?"Write":"Read" },cancellationToken);}
            catch(StudioException ex) when(ex.StatusCode==401) {await RevokeAsync(session,cancellationToken);throw;}
            try {principal=StudioMesPrincipal.Map(profile,context,session.ExternalSubjectId!,session.ExternalTenantId!,now,services.GetServices<IStudioAction>().Select(a=>a.PermissionKey));}
            catch(StudioException ex) when(ex.StatusCode==401) {await RevokeAsync(session,cancellationToken);throw;}
            if(StudioJson.Text(context["contextHandle"]) is {Length:>0} replacement)session.ContextHandleCipher=_protector.Protect(replacement);
            refreshed=true;session.ContextCheckedOn=now;session.PrincipalJson=JsonSerializer.Serialize(principal,StudioCommands.JsonOptions);session.AuthVersion=principal.AuthVersion;session.PolicyVersion=StudioJson.Version(principal.PolicyVersion);
            session.ExpiresOn=session.ExpiresOn<principal.ExpiresOn?session.ExpiresOn:principal.ExpiresOn;
        }
        var expected=session.OptimisticVersion;session.LastSeenOn=now;if(refreshed)session.OptimisticVersion++;
        var affected=refreshed
            ? await Db.Updateable(session).Where(s=>s.Id==session.Id&&s.OptimisticVersion==expected&&s.RevokedOn==null).ExecuteCommandAsync(cancellationToken)
            : await Db.Updateable<StudioAuthSession>().SetColumns(s=>new StudioAuthSession { LastSeenOn=now }).Where(s=>s.Id==session.Id&&s.RevokedOn==null&&s.ExpiresOn>now).ExecuteCommandAsync(cancellationToken);
        if(affected!=1){if(attempt<2)return await ReadSessionAsync(force,cancellationToken,attempt+1);throw new StudioException("STUDIO_IDENTITY_UNAVAILABLE","会话并发刷新未完成，请重试。",503);}
        principal=principal with { ExpiresOn=session.ExpiresOn };
        Http.Items["StudioIdentityEvidence"]=new RequestEvidence(session,principal,force);
        return (session,principal);
    }
    public async Task<object> LoginLocalAsync(string tenant,string login,string password,CancellationToken cancellationToken)
    {
        RequireLocal();
        if(string.IsNullOrWhiteSpace(tenant)||string.IsNullOrWhiteSpace(login)||string.IsNullOrEmpty(password)||tenant.Length>128||login.Length>128||password.Length>128) throw InvalidLogin();
        var now=clock.GetUtcNow();
        var result=await store.TransactionAsync(async ()=>
        {
            var admin=await Db.Queryable<StudioLocalAdmin>().Where(a=>a.TenantNId==tenant&&a.LoginName==login).FirstUtcAsync(database.Provider,cancellationToken);
            if(admin is null){_=_hasher.Verify(DummyHash,password);return (Admin:(StudioLocalAdmin?)null,Locked:false,Success:false);}
            if(admin.LockedUntil>now) return (Admin:(StudioLocalAdmin?)admin,Locked:true,Success:false);
            var correct=_hasher.Verify(admin.PasswordHash,password)&&admin.Enabled;
            var old=admin.OptimisticVersion;admin.OptimisticVersion++;admin.LastUpdatedOn=now;
            admin.FailedCount=correct?0:admin.FailedCount+1;
            admin.LockedUntil=admin.FailedCount>=LoginAttemptPolicy.Default.MaxFailures?now+LoginAttemptPolicy.Default.LockDuration:null;
            if(await Db.Updateable(admin).Where(a=>a.Id==admin.Id&&a.OptimisticVersion==old).ExecuteCommandAsync(cancellationToken)!=1) throw new StudioException("STUDIO_IDENTITY_UNAVAILABLE","登录状态已改变，请重试。",503);
            await store.AppendAuditAsync(LocalPrincipal(admin,now,now.AddMinutes(1)),correct?"studio.auth.login":"studio.auth.login-failed",admin.SubjectNId,new(),cancellationToken);
            return (Admin:(StudioLocalAdmin?)admin,Locked:false,Success:correct);
        },cancellationToken);
        if(result.Locked) throw new StudioException("STUDIO_LOGIN_LOCKED","登录暂时锁定。",429);
        if(!result.Success||result.Admin is null) throw InvalidLogin();
        var principal=LocalPrincipal(result.Admin,now,now.AddHours(8));
        await CreateSessionAsync(principal,null,cancellationToken);
        return new { principal,mustChangePassword=result.Admin.MustChangePassword };
    }
    public async Task ChangePasswordAsync(string currentPassword,string newPassword,CancellationToken cancellationToken)
    {
        RequireLocal();var (session,_)=await ReadSessionAsync(true,cancellationToken);
        await store.TransactionAsync(async ()=>
        {
            var admin=await Db.Queryable<StudioLocalAdmin>().Where(a=>a.TenantNId==session.TenantNId&&a.SubjectNId==session.SubjectNId).FirstUtcAsync(database.Provider,cancellationToken);
            if(string.IsNullOrEmpty(currentPassword)||currentPassword.Length>128||!_hasher.Verify(admin.PasswordHash,currentPassword)) throw InvalidLogin();
            PasswordPolicy.Validate(newPassword,admin.LoginName,admin.SubjectNId);
            if(_hasher.Verify(admin.PasswordHash,newPassword))throw new StudioException("STUDIO_VALIDATION","新密码必须与当前密码不同。");
            var expected=admin.OptimisticVersion;admin.OptimisticVersion++;admin.PasswordHash=_hasher.Hash(newPassword);admin.MustChangePassword=false;admin.AuthVersion++;admin.FailedCount=0;admin.LockedUntil=null;admin.LastUpdatedOn=clock.GetUtcNow();
            if(await Db.Updateable(admin).Where(a=>a.Id==admin.Id&&a.OptimisticVersion==expected).ExecuteCommandAsync(cancellationToken)!=1) throw new StudioException("STUDIO_VERSION_CONFLICT","管理员状态已改变。",409);
            await RevokeSubjectAsync(admin.TenantNId,admin.SubjectNId,cancellationToken);
            await store.AppendAuditAsync(LocalPrincipal(admin,clock.GetUtcNow(),clock.GetUtcNow().AddMinutes(1)),"studio.auth.password-change",admin.SubjectNId,new(),cancellationToken);
            return true;
        },cancellationToken);
        Http.Response.Cookies.Delete(CookieName,new CookieOptions { Secure=true,Path="/" });
    }
    public async Task AdministerAsync(string action,string tenant,string login,string? password,CancellationToken cancellationToken)
    {
        RequireLocal();_ = StudioPublishingRequired(tenant);_=StudioPublishingRequired(login);
        await store.TransactionAsync(async ()=>
        {
            var now=clock.GetUtcNow();var admin=await Db.Queryable<StudioLocalAdmin>().Where(a=>a.TenantNId==tenant).FirstUtcAsync(database.Provider,cancellationToken);
            if(action=="create"&&admin is not null)throw new StudioException("STUDIO_VERSION_CONFLICT","此租户管理员已存在，创建不会覆盖。",409);
            if(action!="create"&&(admin is null||admin.LoginName!=login))throw new StudioException("STUDIO_NOT_FOUND","管理员不存在。",404);
            admin??=new() { TenantNId=tenant,SubjectNId="local-"+Guid.NewGuid().ToString("N"),LoginName=login,CreatedOn=now,LastUpdatedOn=now };
            if(action is "create" or "reset") {PasswordPolicy.Validate(password,login,admin.SubjectNId);admin.PasswordHash=_hasher.Hash(password!);admin.Enabled=true;admin.MustChangePassword=true;admin.FailedCount=0;admin.LockedUntil=null;}
            else if(action=="disable") admin.Enabled=false;
            else throw new StudioException("STUDIO_VALIDATION","管理员操作未登记。");
            if(action=="create")await Db.Insertable(admin).ExecuteCommandAsync(cancellationToken);
            else {var expected=admin.OptimisticVersion;admin.OptimisticVersion++;admin.AuthVersion++;admin.LastUpdatedOn=now;if(await Db.Updateable(admin).Where(a=>a.Id==admin.Id&&a.OptimisticVersion==expected).ExecuteCommandAsync(cancellationToken)!=1)throw new StudioException("STUDIO_VERSION_CONFLICT","管理员已改变。",409);await RevokeSubjectAsync(tenant,admin.SubjectNId,cancellationToken);}
            await store.AppendAuditAsync(LocalPrincipal(admin,now,now.AddMinutes(1)),"studio.admin."+action,admin.SubjectNId,new(),cancellationToken);
            return true;
        },cancellationToken);
    }
    public async Task CreateSessionAsync(StudioPrincipal principal,string? contextHandle,CancellationToken cancellationToken)
    {
        var now=clock.GetUtcNow();var token=RandomToken();var expires=principal.ExpiresOn<now.AddHours(8)?principal.ExpiresOn:now.AddHours(8);
        var session=new StudioAuthSession {SessionHash=StudioJson.Hash(token),TenantNId=principal.TenantNId,SubjectNId=principal.SubjectNId,IssuerKey=principal.IssuerKey,Provider=profile.Identity.Provider,ExternalSubjectId=principal.ExternalSubjectId,ExternalTenantId=principal.ExternalTenantId,ContextHandleCipher=contextHandle is null?null:_protector.Protect(contextHandle),PrincipalJson=JsonSerializer.Serialize(principal,StudioCommands.JsonOptions),AuthVersion=principal.AuthVersion,PolicyVersion=StudioJson.Version(principal.PolicyVersion),CreatedOn=now,LastSeenOn=now,ContextCheckedOn=now,ExpiresOn=expires };
        if(System.Text.Encoding.UTF8.GetByteCount(session.PrincipalJson)>32768)throw new StudioException("STUDIO_IDENTITY_UNAVAILABLE","身份上下文超过预算。",503);
        await Db.Insertable(session).ExecuteCommandAsync(cancellationToken);
        Http.Response.Cookies.Append(CookieName,token,new CookieOptions { Secure=true,HttpOnly=true,SameSite=SameSiteMode.Lax,Path="/",Expires=expires,IsEssential=true });
    }
    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        if(Http.Request.Cookies[CookieName] is { } cookie){var hash=StudioJson.Hash(cookie);var now=clock.GetUtcNow();await Db.Updateable<StudioAuthSession>().SetColumns(s=>new StudioAuthSession { RevokedOn=now }).Where(s=>s.SessionHash==hash&&s.RevokedOn==null).ExecuteCommandAsync(cancellationToken);}
        Http.Response.Cookies.Delete(CookieName,new CookieOptions { Secure=true,Path="/" });
    }
    public async Task<bool> ExistsAsync(StudioPrincipal principal,string subjectKind,string subjectNId,CancellationToken cancellationToken)
    {
        if(subjectKind=="Role")return profile.Identity.RoleMappings.Any(m=>m.RoleNId==subjectNId&&profile.Identity.TenantMappings.Any(t=>t.ExternalTenantId==m.ExternalTenantId&&t.TenantNId==principal.TenantNId));
        if(subjectKind!="User")return false;
        if(subjectNId==principal.SubjectNId)return true;
        if(profile.Identity.Provider=="LocalAdmin")return await Db.Queryable<StudioLocalAdmin>().Where(a=>a.TenantNId==principal.TenantNId&&a.SubjectNId==subjectNId&&a.Enabled).AnyAsync(cancellationToken);
        var now=clock.GetUtcNow();return await Db.Queryable<StudioAuthSession>().Where(s=>s.TenantNId==principal.TenantNId&&s.SubjectNId==subjectNId&&s.RevokedOn==null&&s.ExpiresOn>now).AnyAsync(cancellationToken);
    }
    private StudioPrincipal LocalPrincipal(StudioLocalAdmin admin,DateTimeOffset now,DateTimeOffset expires)=>new(admin.TenantNId,admin.SubjectNId,profile.Identity.IssuerKey,admin.LoginName,[],StudioKinds.Permissions.Where(p=>!p.StartsWith("studio.dashboard.",StringComparison.Ordinal)&&!p.StartsWith("studio.report.",StringComparison.Ordinal)&&!p.StartsWith("studio.trace.",StringComparison.Ordinal)&&!p.StartsWith("studio.workbook.",StringComparison.Ordinal)).ToArray(),profile.Identity.LocalAdminFactoryNIds,admin.AuthVersion.ToString(CultureInfo.InvariantCulture),profile.Identity.PolicyVersion,now,expires);
    private Task<int> RevokeSubjectAsync(string tenant,string subject,CancellationToken cancellationToken){var now=clock.GetUtcNow();return Db.Updateable<StudioAuthSession>().SetColumns(s=>new StudioAuthSession { RevokedOn=now }).Where(s=>s.TenantNId==tenant&&s.SubjectNId==subject&&s.RevokedOn==null).ExecuteCommandAsync(cancellationToken);}
    private Task<int> RevokeAsync(StudioAuthSession session,CancellationToken cancellationToken){var now=clock.GetUtcNow();return Db.Updateable<StudioAuthSession>().SetColumns(s=>new StudioAuthSession { RevokedOn=now }).Where(s=>s.Id==session.Id).ExecuteCommandAsync(cancellationToken);}
    private void RequireLocal(){if(profile.DeploymentMode!="ExternalMes"||profile.Identity.Provider!="LocalAdmin"||!profile.Identity.LocalAdminEnabled)throw new StudioException("STUDIO_NOT_FOUND","此登录方式未启用。",404);}
    private static string StudioPublishingRequired(string value)=>IndustrialPlatform.PlatformStudio.Application.Publishing.StudioPublishing.Required(value,"admin",128);
    private static StudioException InvalidLogin()=>new("STUDIO_UNAUTHENTICATED","账号或密码无效。",401);
}
