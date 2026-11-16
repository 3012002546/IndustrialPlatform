using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Application.Trace;

public sealed partial class TraceRuntime
{
    private static readonly string[] LinkResourceKinds = ["LowCodePage", "Dashboard", "Report", "ReportBundle"];

    /// <summary>仅列出当前主体可运行且部署实际启用的已发布目标；不暴露草稿或来源配置。</summary>
    public async Task<JsonObject> LinkResourcesAsync(StudioPrincipal p, CancellationToken ct)
    {
        p = await identity.RefreshAsync(p, ct);
        authorization.RequirePermission(p, "studio.trace.design");
        var items = new JsonArray();
        foreach (var kind in LinkResourceKinds)
        {
            if (deployment?.IsKindEnabled(kind) != true || !p.HasPermission(StudioKinds.PermissionPrefix(kind) + ".run")) continue;
            for (var page = 1; ; page++)
            {
                var resources = await store.ListRunnableResourcesAsync(p, kind, new(Page: page, PageSize: 100), ct);
                foreach (var candidate in resources)
                {
                    try
                    {
                        var (resource, revision) = await publishing.RequireRuntimeAsync(p, candidate.NId, null, ct);
                        items.Add(new JsonObject { ["kind"] = resource.Kind, ["resourceNId"] = resource.NId, ["name"] = resource.Name,
                            ["revisionNId"] = revision.NId, ["revisionNo"] = revision.RevisionNo.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["parameters"] = SafeParameters(StudioPublishing.Parse(revision.DocumentJson)["parameters"]) });
                    }
                    catch (StudioException e) when (e.StatusCode is 403 or 404 or 503 || e.Code == "SOURCE_CHANGED") { }
                }
                if (resources.Count < 100) break;
            }
        }
        if (TraceCredentials.Fingerprint(await identity.RefreshAsync(p, ct)) != TraceCredentials.Fingerprint(p)) throw Forbidden();
        return new() { ["items"] = items };
    }

    private async Task<JsonObject> ResourceTargetAsync(StudioPrincipal p, TraceLinkInput input, CancellationToken ct)
    {
        if (StudioJson.Text(input.Link["kind"]) != "Resource" || input.Link["target"] is not JsonObject target) throw Forbidden();
        var kind = StudioJson.RequiredString(target, "kind");
        if (!LinkResourceKinds.Contains(kind, StringComparer.Ordinal)) throw Invalid("target.kind", "该资源未登记内容宿主。");
        publishing.RequireKind(kind);
        var (resource, revision) = await publishing.RequireRuntimeAsync(p, StudioJson.RequiredString(target, "resourceNId"), StudioJson.RequiredString(target, "revisionNId"), ct);
        if (resource.Kind != kind) throw Invalid("target.kind", "目标资源类型不匹配。");
        var document = StudioPublishing.Parse(revision.DocumentJson);
        var parameters = DatasetValues.Parameters(p, document, input.Parameters);
        return new() { ["kind"] = kind, ["resourceNId"] = resource.NId, ["revisionNId"] = revision.NId, ["name"] = resource.Name, ["parameters"] = parameters };
    }

    private async Task<TraceNodeLinkResult> ResourceLinkAsync(StudioPrincipal p, string resource, JsonObject input, TraceLinkInput resolved, CancellationToken ct)
    {
        _ = await ResourceTargetAsync(p, resolved, ct);
        var revision = StudioJson.RequiredString(input, "revisionNId");
        await Recheck(p, resource, revision, false, ct);
        input["bindingHash"] = ResourceBindingHash(resolved);
        var (ticket, expires) = credentials.IssueResourceLink(p, resource, revision, input);
        var url = (deployment?.PathBase ?? "") + "/screen/trace-resource?nId=" + Uri.EscapeDataString(resource)
            + "&revisionNId=" + Uri.EscapeDataString(revision) + "&ticket=" + Uri.EscapeDataString(ticket)
            + "&expiresOn=" + Uri.EscapeDataString(expires.ToString("O"));
        return new("Resource", url, expires, true, StudioJson.Text(resolved.Link["label"]), url);
    }

    /// <summary>内容宿主消费短期票据时重新读取授权节点/记录并重算目标参数，票据不替代目标Run授权。</summary>
    public async Task<JsonObject> ResourceContentAsync(StudioPrincipal p, string resource, string revision, string ticket, CancellationToken ct)
    {
        p = await identity.RefreshAsync(p, ct);
        var context = credentials.ReadResourceLink(p, resource, revision, ticket);
        var resolved = await ResolveNodeLinkInputAsync(p, resource, context, ct);
        if (StudioJson.Text(context["bindingHash"]) != ResourceBindingHash(resolved)) throw new StudioException("STUDIO_TRACE_LINK_CHANGED", "记录的链接参数已变化，请返回追溯重新打开。", 409);
        var target = await ResourceTargetAsync(p, resolved, ct);
        await Recheck(p, resource, revision, false, ct);
        return target;
    }

    private static string ResourceBindingHash(TraceLinkInput input) => StudioJson.Hash(StudioJson.Canonicalize(new JsonObject
    { ["link"] = input.Link.DeepClone(), ["parameters"] = input.Parameters.DeepClone() }));
}
