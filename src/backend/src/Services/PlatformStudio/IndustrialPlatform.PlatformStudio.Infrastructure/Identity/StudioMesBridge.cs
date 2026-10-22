using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Domain;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Identity;

/// <summary>有限MES身份桥；端点和双向TLS仅来自服务器登记，不跟随重定向、不记录凭据。</summary>
public interface IStudioMesBridge
{
    Task<JsonObject> ExchangeAsync(JsonObject input,CancellationToken ct);
    Task<JsonObject> ContextAsync(JsonObject input,CancellationToken ct);
}
public sealed class StudioMesBridge(StudioDeploymentProfile profile,IConfiguration configuration) : IStudioMesBridge
{
    private StudioAuthority Authority=>profile.Identity.Authorities.TryGetValue(profile.Identity.AuthorityKey??"",out var authority)?authority:throw new StudioException("STUDIO_IDENTITY_UNAVAILABLE","身份登记项不可用。",503);
    public Task<JsonObject> ExchangeAsync(JsonObject input,CancellationToken ct)=>SendAsync(Authority,Authority.ExchangeEndpoint,input,ct);
    public Task<JsonObject> ContextAsync(JsonObject input,CancellationToken ct)
    {
        var authority=profile.Identity.Authorities.TryGetValue(profile.Identity.ContextEndpointKey??"",out var item)?item:throw new StudioException("STUDIO_IDENTITY_UNAVAILABLE","上下文端点登记项不可用。",503);
        if(authority.Issuer!=Authority.Issuer||authority.ClientId!=Authority.ClientId)throw new StudioException("STUDIO_IDENTITY_UNAVAILABLE","上下文端点未绑定当前客户端与发行者。",503);
        return SendAsync(authority,authority.ContextEndpoint,input,ct);
    }
    private async Task<JsonObject> SendAsync(StudioAuthority authority,string address,JsonObject input,CancellationToken ct)
    {
        if(!Uri.TryCreate(address,UriKind.Absolute,out var uri)||uri.Scheme!="https") throw new StudioException("STUDIO_IDENTITY_UNAVAILABLE","身份桥端点必须为登记的HTTPS地址。",503);
        var certificatePath=configuration[authority.ClientCertificatePathRef];
        if(string.IsNullOrWhiteSpace(certificatePath)||!Path.IsPathFullyQualified(certificatePath)) throw new StudioException("STUDIO_IDENTITY_UNAVAILABLE","身份桥双向TLS材料未配置。",503);
        using var certificate=X509CertificateLoader.LoadPkcs12FromFile(certificatePath,authority.ClientCertificatePasswordRef is null?null:configuration[authority.ClientCertificatePasswordRef]);
        if(!certificate.HasPrivateKey) throw new StudioException("STUDIO_IDENTITY_UNAVAILABLE","身份桥证书没有私钥。",503);
        using var handler=new HttpClientHandler { AllowAutoRedirect=false };
        handler.ClientCertificates.Add(certificate);
        using var client=new HttpClient(handler) { Timeout=TimeSpan.FromSeconds(5) };
        try
        {
            using var response=await client.PostAsJsonAsync(uri,input,ct);
            if(response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)throw new StudioException("STUDIO_UNAUTHENTICATED","MES来源会话或客户端身份已被撤销。",401);
            if(!response.IsSuccessStatusCode||response.Content.Headers.ContentLength>32768) throw new StudioException("STUDIO_IDENTITY_UNAVAILABLE","身份桥未返回可验证的上下文。",503);
            await using var stream=await response.Content.ReadAsStreamAsync(ct);
            using var limited=new MemoryStream();var bytes=new byte[4096];int n;
            while((n=await stream.ReadAsync(bytes,ct))>0){if(limited.Length+n>32768)throw new StudioException("STUDIO_IDENTITY_UNAVAILABLE","身份上下文超过预算。",503);await limited.WriteAsync(bytes.AsMemory(0,n),ct);}
            limited.Position=0;
            return (await JsonNode.ParseAsync(limited,cancellationToken:ct))?.AsObject()??throw new StudioException("STUDIO_IDENTITY_UNAVAILABLE","身份上下文为空。",503);
        }
        catch(StudioException){throw;}
        catch(Exception ex) when(ex is not OperationCanceledException||!ct.IsCancellationRequested){throw new StudioException("STUDIO_IDENTITY_UNAVAILABLE","身份桥暂不可用，请重新验证身份。",503);}
    }
}

/// <summary>只将已映射外部角色/工厂投影为规范上下文，未映射租户拒绝。</summary>
public static class StudioMesPrincipal
{
    public static StudioPrincipal Map(StudioDeploymentProfile profile,JsonObject context,string expectedSubject,string expectedTenant,DateTimeOffset now,IEnumerable<string>? actionPermissions=null)
    {
        if(context["active"]?.GetValue<bool>()!=true) throw new StudioException("STUDIO_UNAUTHENTICATED","MES会话已停用或注销。",401);
        var subject=StudioJson.RequiredString(context,"subjectId",256);var tenant=StudioJson.RequiredString(context,"tenantId",256);
        if(subject!=expectedSubject||tenant!=expectedTenant) throw new StudioException("STUDIO_UNAUTHENTICATED","MES上下文与来源会话不匹配。",401);
        var options=profile.Identity;
        var mapping=options.TenantMappings.SingleOrDefault(m=>m.ExternalTenantId==tenant)??throw new StudioException("STUDIO_FORBIDDEN","外部租户未映射。",403);
        var roleIds=Strings(context,"roles");var factoryIds=Strings(context,"factoryIds");
        var roles=options.RoleMappings.Where(m=>m.ExternalTenantId==tenant&&roleIds.Contains(m.ExternalRoleId,StringComparer.Ordinal)).ToArray();
        foreach(var role in roles)
            if(role.RoleNId!=StudioPrincipal.ExternalNId(options.IssuerKey,tenant,role.ExternalRoleId,"mes-role-")) throw new StudioException("STUDIO_IDENTITY_UNAVAILABLE","外部角色标识映射与稳定算法不一致。",503);
        var permissions=roles.SelectMany(m=>m.Permissions).Distinct(StringComparer.Ordinal).ToArray();
        if(context.ContainsKey("permissions")) permissions=permissions.Intersect(Strings(context,"permissions"),StringComparer.Ordinal).ToArray();
        var registered=StudioKinds.Permissions.Concat(actionPermissions??[]).ToHashSet(StringComparer.Ordinal);
        if(permissions.Any(p=>!registered.Contains(p))) throw new StudioException("STUDIO_IDENTITY_UNAVAILABLE","身份映射含未登记权限码。",503);
        var expiresText=StudioJson.Text(context["expiresOn"]);
        if(expiresText is null||!System.Text.RegularExpressions.Regex.IsMatch(expiresText,@"(Z|[+-]\d{2}:\d{2})$",System.Text.RegularExpressions.RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1))||!DateTimeOffset.TryParse(expiresText,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind,out var expires)||expires<=now) throw new StudioException("STUDIO_UNAUTHENTICATED","MES会话已过期。",401);
        var authVersion=StudioJson.RequiredString(context,"authVersion",128);
        return new(mapping.TenantNId,StudioPrincipal.ExternalNId(options.IssuerKey,tenant,subject),options.IssuerKey,StudioJson.Text(context["displayName"])??subject,roles.Select(m=>m.RoleNId).Distinct(StringComparer.Ordinal).ToArray(),permissions,options.FactoryMappings.Where(m=>m.ExternalTenantId==tenant&&factoryIds.Contains(m.ExternalFactoryId,StringComparer.Ordinal)).Select(m=>m.FactoryNId).Distinct(StringComparer.Ordinal).ToArray(),authVersion,options.PolicyVersion,now,expires,subject,tenant);
    }
    private static string[] Strings(JsonObject node,string key)
    {
        if(node[key] is not JsonArray array||array.Count>1000||array.Any(v=>StudioJson.Text(v) is null)) throw new StudioException("STUDIO_IDENTITY_UNAVAILABLE","身份集合结构无效。",503);
        return array.Select(v=>StudioJson.Text(v)!).Distinct(StringComparer.Ordinal).ToArray();
    }
}
