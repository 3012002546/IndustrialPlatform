using System.Text;
using System.Text.Json.Nodes;

namespace IndustrialPlatform.PlatformStudio.Domain;

public static partial class StudioDocuments
{
    private static void ValidateCustomPage(JsonObject page, List<StudioDiagnostic> errors)
    {
        var mode = StudioJson.Text(page["mode"]);
        if (mode == "Html")
        {
            if (page["customHtml"] is not JsonObject code) { errors.Add(new("STUDIO_VALIDATION", "customHtml", "缺少 HTML 配置。")); return; }
            Allowed(code, ["html", "css", "js", "formFields"], "customHtml", errors);
            foreach (var (key, limit) in new[] { ("html", 512 * 1024), ("css", 128 * 1024), ("js", 256 * 1024) })
                if (code[key] is not JsonValue value || !value.TryGetValue<string>(out var text) || Encoding.UTF8.GetByteCount(text) > limit)
                    errors.Add(new("STUDIO_VALIDATION", "customHtml." + key, "代码缺失、类型错误或超过容量。"));
            if (code["formFields"] is not JsonArray fields || fields.Count > 64 || fields.Select(StudioJson.Text).Any(f => string.IsNullOrWhiteSpace(f) || f.Length > 64 || f is "__proto__" or "prototype" or "constructor") || fields.Select(StudioJson.Text).Distinct(StringComparer.Ordinal).Count() != fields.Count)
                errors.Add(new("STUDIO_VALIDATION", "customHtml.formFields", "可写字段必须为最多 64 个唯一的稳定名称。"));
        }
        if (mode == "Link")
        {
            if (page["embeddedPage"] is not JsonObject link) { errors.Add(new("STUDIO_VALIDATION", "embeddedPage", "缺少链接配置。")); return; }
            Allowed(link, ["url", "parameterMap"], "embeddedPage", errors);
            if (StudioJson.Text(link["url"]) is not { Length: > 0 and <= 4096 } url || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0)
                errors.Add(new("STUDIO_CUSTOM_URL_FORBIDDEN", "embeddedPage.url", "嵌入地址必须为不含凭据的 HTTPS 地址。"));
            var targets = new HashSet<string>(StringComparer.Ordinal);
            if (link["parameterMap"] is not JsonArray { Count: <= 64 }) errors.Add(new("STUDIO_VALIDATION", "embeddedPage.parameterMap", "参数映射必须为最多 64 项的数组。"));
            foreach (var map in Objects(link["parameterMap"], "embeddedPage.parameterMap", errors))
            {
                Allowed(map, ["target", "source"], "embeddedPage.parameterMap", errors);
                if (!targets.Add(StudioJson.RequiredString(map, "target", 64))) errors.Add(new("STUDIO_VALIDATION", "embeddedPage.parameterMap.target", "参数目标重复。"));
                ValidateBinding(map["source"], "embeddedPage.parameterMap.source", [], errors);
                if (StudioJson.Text(map["source"]?["from"]) is not ("PageParam" or "Constant")) errors.Add(new("STUDIO_FORBIDDEN", "embeddedPage.parameterMap.source", "链接仅允许页面参数或常量映射。"));
            }
        }
    }
}

/// <summary>Only registered external HTTPS origins; no server-side fetch or raw parameter interpolation.</summary>
public static class StudioCustomPagePolicy
{
    public static Uri RequireLink(JsonObject document, StudioDeploymentProfile profile)
    {
        var url = StudioJson.Text(document["embeddedPage"]?["url"]);
        if (url is null || url.Length > 4096 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0
            || !profile.AllowedFrameOrigins.Any(value => Uri.TryCreate(value, UriKind.Absolute, out var allowed) && allowed.GetLeftPart(UriPartial.Authority).Equals(uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
            || Uri.TryCreate(profile.PublicOrigin, UriKind.Absolute, out var own) && own.GetLeftPart(UriPartial.Authority).Equals(uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
            throw new StudioException("STUDIO_CUSTOM_URL_FORBIDDEN", "嵌入目标必须是部署允许的外部 HTTPS 域，且不能是平台自身。", 422, "embeddedPage.url");
        return uri;
    }
    public static string ResolveLink(JsonObject document, StudioDeploymentProfile profile, JsonObject parameters)
    {
        var builder = new UriBuilder(RequireLink(document, profile));
        var query = builder.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach (var map in (document["embeddedPage"]?["parameterMap"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var binding = map["source"] as JsonObject ?? throw new StudioException("STUDIO_VALIDATION", "链接参数源缺失。");
            if (StudioJson.Text(binding["from"]) is not ("PageParam" or "Constant")) throw new StudioException("STUDIO_FORBIDDEN", "链接参数上下文未授权。", 403);
            var value = StudioBinding.Resolve(binding, new Dictionary<string, JsonNode?> { ["PageParam"] = parameters });
            if (value is JsonArray or JsonObject) throw new StudioException("TYPE_MISMATCH", "链接参数必须是标量。");
            var target = StudioJson.RequiredString(map, "target", 64);
            query.RemoveAll(item => Uri.UnescapeDataString(item.Split('=')[0].Replace('+', ' ')) == target);
            query.Add(Uri.EscapeDataString(target) + "=" + Uri.EscapeDataString(StudioJson.Text(value) ?? value?.ToJsonString() ?? ""));
        }
        builder.Query = string.Join('&', query);
        var url = builder.Uri.AbsoluteUri;
        if (url.Length > 16384) throw new StudioException("STUDIO_LIMIT", "映射后的嵌入地址超过16KiB。", 422);
        return url;
    }
}
