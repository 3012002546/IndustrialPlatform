using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Identity;

/// <summary>MesCode的浏览器相关性与一次交换；OIDC由标准中间件完成协议验证。</summary>
public sealed class StudioExternalLogin(StudioDatabase database,StudioDeploymentProfile profile,StudioExternalIdentity identity,IStudioMesBridge bridge,IHttpContextAccessor accessor,TimeProvider clock,IServiceProvider services)
{
    private HttpContext Http=>accessor.HttpContext!;
    private StudioAuthority Authority=>profile.Identity.Authorities.TryGetValue(profile.Identity.AuthorityKey??"",out var authority)?authority:throw new StudioException("STUDIO_IDENTITY_UNAVAILABLE","身份登记项未配置。",503);
    private string Callback=>profile.PublicOrigin.TrimEnd('/')+profile.PathBase+"/api/v1/studio/auth/callback";
    public async Task<string> BeginMesCodeAsync(string? returnPath,CancellationToken ct)
    {
        if(profile.Identity.Provider!="MesCode")throw new StudioException("STUDIO_NOT_FOUND","身份入口未启用。",404);
        var authority=Authority;
        if(!Uri.TryCreate(authority.AuthorizationEndpoint,UriKind.Absolute,out var url)||url.Scheme!="https")throw new StudioException("STUDIO_IDENTITY_UNAVAILABLE","身份登录端点无效。",503);
        var state=StudioExternalIdentity.RandomToken();var nonce=StudioExternalIdentity.RandomToken();var verifier=StudioExternalIdentity.RandomToken();var now=clock.GetUtcNow();
        await database.Client.Insertable(new StudioAuthRequest { StateHash=StudioJson.Hash(state),NonceHash=StudioJson.Hash(nonce),VerifierCipher=identity.Protect(verifier),ReturnPath=StudioExternalIdentity.SafeReturnPath(returnPath),Provider="MesCode",IssuerKey=profile.Identity.IssuerKey,CreatedOn=now,ExpiresOn=now.AddMinutes(5) }).ExecuteCommandAsync(ct);
        Http.Response.Cookies.Append(StudioExternalIdentity.CorrelationCookie,identity.Protect(nonce),new CookieOptions { Secure=true,HttpOnly=true,SameSite=SameSiteMode.Lax,Path="/",Expires=now.AddMinutes(5),IsEssential=true });
        var challenge=WebEncoders.Base64UrlEncode(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));
        return QueryHelpers.AddQueryString(url.AbsoluteUri,new Dictionary<string,string?> { ["state"]=state,["clientId"]=authority.ClientId,["redirectUri"]=Callback,["codeChallenge"]=challenge,["codeChallengeMethod"]="S256" });
    }
    public async Task<string> CompleteMesCodeAsync(string? code,string? state,CancellationToken ct)
    {
        if(profile.Identity.Provider!="MesCode")throw new StudioException("STUDIO_NOT_FOUND","身份回调未启用。",404);
        var cookie=Http.Request.Cookies[StudioExternalIdentity.CorrelationCookie];Http.Response.Cookies.Delete(StudioExternalIdentity.CorrelationCookie,new CookieOptions { Secure=true,Path="/" });
        if(string.IsNullOrWhiteSpace(code)||code.Length>2048||string.IsNullOrWhiteSpace(state)||state.Length>128||cookie is null)throw Invalid();
        var hash=StudioJson.Hash(state);var now=clock.GetUtcNow();var db=database.Client;
        var request=await db.Queryable<StudioAuthRequest>().Where(r=>r.StateHash==hash).FirstUtcAsync(database.Provider,ct);
        string nonce;try{nonce=identity.Unprotect(cookie);}catch(System.Security.Cryptography.CryptographicException){throw Invalid();}
        if(request is null||request.Provider!="MesCode"||request.IssuerKey!=profile.Identity.IssuerKey||request.Status!="Created"||request.ExpiresOn<=now||request.NonceHash!=StudioJson.Hash(nonce))throw Invalid();
        var version=request.OptimisticVersion;
        if(await db.Updateable<StudioAuthRequest>().SetColumns(r=>new StudioAuthRequest { Status="Exchanging",OptimisticVersion=version+1 }).Where(r=>r.Id==request.Id&&r.Status=="Created"&&r.OptimisticVersion==version).ExecuteCommandAsync(ct)!=1)throw Invalid();
        try
        {
            var authority=Authority;
            var exchange=await bridge.ExchangeAsync(new() { ["clientId"]=authority.ClientId,["code"]=code,["redirectUri"]=Callback,["codeVerifier"]=identity.Unprotect(request.VerifierCipher) },ct);
            if(StudioJson.Text(exchange["issuer"])!=authority.Issuer)throw Invalid();
            var subject=StudioJson.RequiredString(exchange,"subjectId",256);var tenant=StudioJson.RequiredString(exchange,"tenantId",256);var handle=StudioJson.RequiredString(exchange,"contextHandle",2048);
            if(!DateTimeOffset.TryParse(StudioJson.Text(exchange["expiresOn"]),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var exchangeExpires)||exchangeExpires<=now)throw Invalid();
            var context=await bridge.ContextAsync(new() { ["contextHandle"]=handle,["operation"]="Login" },ct);
            var principal=StudioMesPrincipal.Map(profile,context,subject,tenant,now,services.GetServices<IStudioAction>().Select(a=>a.PermissionKey));
            principal=principal with { ExpiresOn=principal.ExpiresOn<exchangeExpires?principal.ExpiresOn:exchangeExpires };
            await identity.CreateSessionAsync(principal,StudioJson.Text(context["contextHandle"])??handle,ct);
            await db.Updateable<StudioAuthRequest>().SetColumns(r=>new StudioAuthRequest { Status="Consumed",ConsumedOn=now }).Where(r=>r.Id==request.Id&&r.Status=="Exchanging").ExecuteCommandAsync(ct);
            return request.ReturnPath;
        }
        catch
        {
            await db.Updateable<StudioAuthRequest>().SetColumns(r=>new StudioAuthRequest { Status="Failed",ConsumedOn=now }).Where(r=>r.Id==request.Id&&r.Status=="Exchanging").ExecuteCommandAsync(CancellationToken.None);
            throw;
        }
    }
    public async Task CompleteOidcAsync(TicketReceivedContext ticket,CancellationToken ct)
    {
        var claims=ticket.Principal??throw Invalid();
        var issuer=claims.FindFirst("iss")?.Value;var subject=claims.FindFirst("sub")?.Value;var tenant=claims.FindFirst("tenant_id")?.Value;var sid=claims.FindFirst("sid")?.Value;var authTime=claims.FindFirst("auth_time")?.Value;
        if(issuer!=Authority.Issuer||subject is null||tenant is null||sid is null||!long.TryParse(authTime,NumberStyles.None,CultureInfo.InvariantCulture,out _))throw Invalid();
        var context=await bridge.ContextAsync(new() { ["issuer"]=issuer,["validatedSubjectId"]=subject,["validatedTenantId"]=tenant,["issuerSessionId"]=sid,["authTime"]=authTime,["operation"]="Login" },ct);
        var principal=StudioMesPrincipal.Map(profile,context,subject,tenant,clock.GetUtcNow(),services.GetServices<IStudioAction>().Select(a=>a.PermissionKey));
        var handle=StudioJson.RequiredString(context,"contextHandle",2048);
        await identity.CreateSessionAsync(principal,handle,ct);
        Http.Response.Redirect(profile.PathBase+StudioExternalIdentity.SafeReturnPath(ticket.Properties?.RedirectUri));ticket.HandleResponse();
    }
    private static StudioException Invalid()=>new("STUDIO_UNAUTHENTICATED","登录相关性或来源会话验证失败，请重新发起登录。",401);
}
