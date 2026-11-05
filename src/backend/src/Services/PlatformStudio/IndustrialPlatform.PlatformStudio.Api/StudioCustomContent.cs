using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Data;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Api;

/// <summary>Short-lived single-use document transport; never carries a platform token or business API authority.</summary>
public sealed class StudioCustomContentTickets(TimeProvider clock)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTimeOffset Expires, string Html)> _tickets = new(StringComparer.Ordinal);
    public string Issue(string html)
    {
        lock (_gate)
        {
            foreach (var expired in _tickets.Where(t => t.Value.Expires <= clock.GetUtcNow()).Select(t => t.Key).ToArray()) _tickets.Remove(expired);
            if (_tickets.Count >= 64 || _tickets.Values.Sum(t => System.Text.Encoding.UTF8.GetByteCount(t.Html)) + System.Text.Encoding.UTF8.GetByteCount(html) > 16 * 1024 * 1024) throw new StudioException("STUDIO_LIMIT", "自定义页面内容加载并发超过预算，请稍后重试。", 429);
            var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _tickets.Add(key, (clock.GetUtcNow().AddSeconds(30), html));
            return key;
        }
    }
    public string Consume(string ticket)
    {
        lock (_gate)
        {
            if (!_tickets.Remove(ticket, out var content) || content.Expires <= clock.GetUtcNow()) throw new StudioException("STUDIO_NOT_FOUND", "内容票据已使用或过期，请刷新页面。", 404);
            return content.Html;
        }
    }
}

public sealed record StudioCustomContentInput(string? RevisionNId, string? DraftVersion, JsonObject? Parameters);

public sealed class StudioCustomContent(StudioPublishing publishing, StudioDeploymentProfile profile, StudioCustomContentTickets tickets)
{
    public async Task<object> PrepareAsync(StudioPrincipal principal, string nId, StudioCustomContentInput input, string? browserOrigin, CancellationToken ct)
    {
        JsonObject document;
        if (input.DraftVersion is { } draftVersion)
        {
            var resource = await publishing.RequireResourceAsync(principal, nId, "design", "Design", ct);
            if (resource.Kind != "LowCodePage" || resource.OptimisticVersion != StudioJson.Version(draftVersion, "draftVersion")) throw new StudioException("STUDIO_VERSION_CONFLICT", "草稿版本已变化，请重新保存并试运行。", 409);
            document = StudioPublishing.Parse(resource.DraftJson);
        }
        else
        {
            var (resource, revision) = await publishing.RequireRuntimeAsync(principal, nId, input.RevisionNId, ct);
            if (resource.Kind != "LowCodePage") throw new StudioException("STUDIO_NOT_FOUND", "页面不存在。", 404);
            document = StudioPublishing.Parse(revision.DocumentJson);
        }
        var diagnostics = StudioDocuments.Validate("LowCodePage", document);
        if (diagnostics.FirstOrDefault(d => d.Severity == "Error") is { } error) throw new StudioException(error.Code, error.Message, path: error.Path);
        var parameters = DatasetValues.Parameters(principal, document, input.Parameters ?? new());
        if (StudioJson.Text(document["mode"]) == "Link")
        {
            var target = StudioCustomPagePolicy.RequireLink(document, profile);
            if (Uri.TryCreate(browserOrigin, UriKind.Absolute, out var own) && target.GetLeftPart(UriPartial.Authority).Equals(own.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)) throw new StudioException("STUDIO_CUSTOM_URL_FORBIDDEN", "不能嵌入平台同源页面。", 422);
            return new { mode = "Link", url = StudioCustomPagePolicy.ResolveLink(document, profile, parameters) };
        }
        if (StudioJson.Text(document["mode"]) != "Html") throw new StudioException("STUDIO_VALIDATION", "页面没有自定义内容。");
        var bridgeId = Guid.NewGuid().ToString("N");
        var code = (JsonObject)document["customHtml"]!;
        var settings = JsonSerializer.Serialize(new { bridgeId, parameters }); // Default encoder escapes HTML delimiters.
        var bootstrap = """
            (()=>{'use strict';const settings=SETTINGS;let seq=0;const pending=new Map();
            let readyResolve;const ready=new Promise(resolve=>readyResolve=resolve);
            addEventListener('message',event=>{if(event.source!==parent||event.data?.bridgeId!==settings.bridgeId)return;
              const data=event.data;if(data.type==='init'){readyResolve();return}if(data.type!=='response')return;
              const item=pending.get(data.id);if(!item)return;pending.delete(data.id);clearTimeout(item.timer);data.error?item.reject(new Error(data.error)):item.resolve(data.result);});
            async function request(method,args){await ready;if(pending.size>=8)throw new Error('STUDIO_LIMIT');const id=String(++seq);
              return new Promise((resolve,reject)=>{const timer=setTimeout(()=>{pending.delete(id);reject(new Error('STUDIO_CUSTOM_CHANNEL_CLOSED'))},30000);pending.set(id,{resolve,reject,timer});parent.postMessage({bridgeId:settings.bridgeId,type:'request',id,method,args},'*')});}
            Object.defineProperty(window,'studio',{value:Object.freeze({ready,parameters:settings.parameters,query:(instanceId,pageParameters)=>request('query',{instanceId,pageParameters}),executeAction:(actionId,context)=>request('executeAction',{actionId,context})}),writable:false});})();
            """.Replace("SETTINGS", settings, StringComparison.Ordinal);
        // JSON-encoded script text preserves literal </script> inside user JS strings.
        var runCode = "(()=>{const style=document.createElement('style');style.textContent=" + JsonSerializer.Serialize(StudioJson.Text(code["css"])) + ";document.head.appendChild(style);const script=document.createElement('script');script.textContent=" + JsonSerializer.Serialize(StudioJson.Text(code["js"])) + ";document.body.appendChild(script)})();";
        var html = "<!doctype html><html><head><meta charset=\"utf-8\"><script>" + bootstrap + "</script></head><body>" + StudioJson.Text(code["html"]) + "<script>" + runCode + "</script></body></html>";
        var ticket = tickets.Issue(html);
        return new { mode = "Html", bridgeId, contentUrl = "/api/v1/studio/pages/custom-content/" + ticket };
    }
}
