using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Application.Dashboard;

public interface IStudioScreenStore
{
    Task InsertAsync(StudioScreenLink link, CancellationToken ct);
    Task<StudioScreenLink?> FindAsync(Guid id, CancellationToken ct);
    async Task<bool> IsActiveAsync(Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var link = await FindAsync(id, ct);
        return link is not null && link.RevokedOn is null && (link.NeverExpires || link.ExpiresOn > now);
    }
    Task<IReadOnlyList<StudioScreenLink>> ListAsync(string tenant, string resource, CancellationToken ct);
    Task RevokeAsync(Guid id, string tenant, string resource, DateTimeOffset now, CancellationToken ct);
}

/// <summary>通过权威目录重新解析发布者，不依赖观看设备的登录会话。</summary>
public interface IStudioScreenIdentity
{
    Task<StudioPrincipal> GetPublisherAsync(StudioScreenLink link, CancellationToken ct);
}

/// <summary>为既有授权生成可再次复制的受保护凭据；不保存或还原原始密钥。</summary>
public interface IStudioScreenKeys
{
    string Protect(StudioScreenLink link);
    bool Matches(StudioScreenLink link, string key);
}

public sealed record CreateDashboardScreen(string RevisionNId, JsonObject? Parameters, int? LifetimeHours = null, bool FollowPublished = true);

public sealed class DashboardScreens(IStudioScreenStore links, IStudioScreenIdentity identity,
    IStudioResourceStore resources, StudioPublishing publishing, IStudioAuthorization authorization,
    DashboardNavigation dashboards, TimeProvider clock, IStudioScreenKeys keys)
{
    public async Task<object> CreateAsync(StudioPrincipal principal, string nId, CreateDashboardScreen request, CancellationToken ct, string kind = "Dashboard")
    {
        var resource = await publishing.RequireResourceAsync(principal, nId, "publish", "Publish", ct);
        if (resource.Kind != kind || kind is not ("Dashboard" or "Playlist")) throw Missing();
        if (request.LifetimeHours is < 1 or > 2160) throw new StudioException("STUDIO_VALIDATION", "大屏链接有效期须为1小时至90天，或选择无限期。", path: "lifetimeHours");
        var parameters = request.Parameters ?? new JsonObject();
        if (Encoding.UTF8.GetByteCount(parameters.ToJsonString()) > 65536) throw new StudioException("STUDIO_LIMIT", "大屏参数超过64KiB。", 429);
        var selected = await resources.FindRevisionAsync(principal.TenantNId, request.RevisionNId, ct);
        if (selected is null || selected.ResourceId != resource.Id) throw Missing();
        var root = await publishing.RequireRuntimeAsync(principal, nId, request.FollowPublished ? null : request.RevisionNId, ct);
        if (kind == "Playlist") parameters = DashboardNavigation.ScreenPlaylistParameters(principal, root.Revision, parameters);
        else _ = await dashboards.RuntimeAuthorizedAsync(principal, root.Resource, root.Revision, parameters, ct);
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var now = clock.GetUtcNow();
        var link = new StudioScreenLink { Id = Guid.NewGuid(), TenantNId = principal.TenantNId, ResourceNId = nId,
            RevisionNId = root.Revision.NId, CreatedBy = principal.SubjectNId,
            IssuerKey = principal.IssuerKey, AuthVersion = principal.AuthVersion, ParametersJson = parameters.ToJsonString(),
            TokenHash = StudioJson.Hash(key), CreatedOn = now, NeverExpires = request.LifetimeHours is null,
            ExpiresOn = request.LifetimeHours is { } hours ? now.AddHours(hours) : now, FollowPublished = request.FollowPublished };
        // 确认宿主支持无会话的权威目录查询；不把浏览器会话复制成长期身份。
        _ = await identity.GetPublisherAsync(link, ct);
        await resources.TransactionAsync(async () => {
            await links.InsertAsync(link, ct);
            await resources.AppendAuditAsync(principal, StudioKinds.PermissionPrefix(kind) + ".screen.create", nId,
                new JsonObject { ["screenId"] = link.Id.ToString(), ["revisionNId"] = link.RevisionNId,
                    ["expiresOn"] = Expiry(link)?.ToString("O"), ["followPublished"] = link.FollowPublished }, ct);
            return true;
        }, ct);
        return new { link.Id, key, expiresOn = Expiry(link), link.RevisionNId, link.FollowPublished };
    }

    public async Task<object> ListAsync(StudioPrincipal principal, string nId, CancellationToken ct, string kind = "Dashboard")
    {
        var resource = await publishing.RequireResourceAsync(principal, nId, "publish", "Publish", ct);
        if (resource.Kind != kind || kind is not ("Dashboard" or "Playlist")) throw Missing();
        return (await links.ListAsync(principal.TenantNId, nId, ct)).Select(link => new { link.Id, link.RevisionNId, link.CreatedOn, expiresOn = Expiry(link), link.RevokedOn, link.FollowPublished,
            key = link.RevokedOn is null && (link.NeverExpires || link.ExpiresOn > clock.GetUtcNow()) ? keys.Protect(link) : null });
    }

    public async Task RevokeAsync(StudioPrincipal principal, string nId, Guid id, CancellationToken ct, string kind = "Dashboard")
    {
        var resource = await publishing.RequireResourceAsync(principal, nId, "publish", "Publish", ct);
        if (resource.Kind != kind || kind is not ("Dashboard" or "Playlist")) throw Missing();
        var link = await links.FindAsync(id, ct);
        if (link is null || link.TenantNId != principal.TenantNId || link.ResourceNId != nId) throw Missing();
        await resources.TransactionAsync(async () => {
            await links.RevokeAsync(id, principal.TenantNId, nId, clock.GetUtcNow(), ct);
            await resources.AppendAuditAsync(principal, StudioKinds.PermissionPrefix(kind) + ".screen.revoke", nId, new JsonObject { ["screenId"] = id.ToString() }, ct);
            return true;
        }, ct);
    }

    public async Task<JsonObject> RuntimeAsync(Guid id, string? key, CancellationToken ct, string[]? instanceIds = null, string? knownRevisionNId = null, JsonArray? navigation = null, string? playlistItemId = null)
    {
        if (navigation is { Count: > 8 } || navigation is not null && Encoding.UTF8.GetByteCount(navigation.ToJsonString()) > 8192)
            throw new StudioException("STUDIO_LIMIT", "大屏明细路径超过8级或8KiB。", 429);
        if (key is null || key.Length is < 64 or > 1024) throw Missing();
        var stage = Stopwatch.GetTimestamp();
        var link = await links.FindAsync(id, ct);
        stage = StudioRuntimeTiming.Mark("link-read", stage);
        if (link is null || !(key.Length == 64 && key.All(Uri.IsHexDigit)
                ? CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(link.TokenHash), Encoding.ASCII.GetBytes(StudioJson.Hash(key)))
                : keys.Matches(link, key))) throw Missing();
        RequireActive(link);
        var principal = await identity.GetPublisherAsync(link, ct);
        stage = StudioRuntimeTiming.Mark("publisher", stage);
        if (principal.TenantNId != link.TenantNId || principal.SubjectNId != link.CreatedBy || principal.IssuerKey != link.IssuerKey || principal.AuthVersion != link.AuthVersion) throw Missing();
        var root = await ReadRootAsync(principal, link, ct);
        var resource = root.Resource;
        stage = StudioRuntimeTiming.Mark("root-publish-access", stage);
        if (resource.Kind is not ("Dashboard" or "Playlist")) throw Missing();
        var parameters = StudioPublishing.Parse(link.ParametersJson);
        JsonObject? playlist = null;
        var currentNId = link.ResourceNId;
        var selectedRevision = root.Revision.NId;
        if (resource.Kind == "Playlist")
        {
            parameters = DashboardNavigation.ScreenPlaylistParameters(principal, root.Revision, parameters);
            playlist = DashboardNavigation.ScreenPlaylistManifest(resource, root.Revision);
            playlist["expiresOn"] = Expiry(link)?.ToString("O");
            playlist["followPublished"] = link.FollowPublished;
            if (playlistItemId is null)
            {
                if (navigation is { Count: > 0 }) throw Missing();
                if (!await links.IsActiveAsync(id, clock.GetUtcNow(), ct)) throw Missing();
                return playlist;
            }
            var item = await dashboards.ResolveScreenPlaylistItemAsync(principal, root.Revision, playlistItemId, parameters, ct);
            resource = item.Resource;
            root = (item.Resource, item.Revision);
            currentNId = resource.NId;
            selectedRevision = item.Revision.NId;
            parameters = item.Parameters;
        }
        else if (playlistItemId is not null) throw Missing();
        if (navigation is not { Count: > 0 } && knownRevisionNId is not null && root.Revision.NId != knownRevisionNId)
            instanceIds = null;
        JsonObject result;
        if (navigation is { Count: > 0 })
        {
            JsonObject? resolved = null;
            for (var i = 0; i < navigation.Count; i++)
            {
                var step = navigation[i] as JsonObject ?? throw Missing();
                // 客户端只提供规则和行键；目标修订与参数逐级由服务端从原授权看板推导。
                var request = new DashboardNavigationRequest(selectedRevision, StudioJson.RequiredString(step, "ruleId"), step["rowKey"] as JsonObject, parameters, IncludeRuntime: i == navigation.Count - 1);
                resolved = await ResolveStepAsync(principal, resource, root.Revision, currentNId, request, i == 0, instanceIds, knownRevisionNId, ct);
                currentNId = StudioJson.RequiredString(resolved, "targetResourceNId");
                selectedRevision = StudioJson.RequiredString(resolved, "targetRevisionNId");
                parameters = resolved["parameters"]!.DeepClone().AsObject();
            }
            result = resolved!["targetRuntime"]!.DeepClone().AsObject();
        }
        else result = await dashboards.RuntimeAuthorizedAsync(principal, resource, root.Revision, parameters, ct, instanceIds);
        // 查询期间发生撤销/过期时也不返回数据。
        stage = Stopwatch.GetTimestamp();
        if (!await links.IsActiveAsync(id, clock.GetUtcNow(), ct)) throw Missing();
        StudioRuntimeTiming.Mark("link-recheck", stage);
        if (playlist is not null) result["playlist"] = playlist;
        result["parameters"] = parameters;
        result["expiresOn"] = Expiry(link)?.ToString("O");
        result["followPublished"] = link.FollowPublished;
        // 只公开可点击的看板规则标识；目标、参数映射和来源上下文保留在服务端。
        result["document"]!["navigation"] = new JsonArray((result["document"]!["navigation"] as JsonArray ?? [])
            .OfType<JsonObject>().Where(rule => StudioJson.Text(rule["target"]?["kind"]) == "Dashboard")
            .Select(rule => (JsonNode)new JsonObject { ["id"] = rule["id"]?.DeepClone(), ["componentId"] = rule["componentId"]?.DeepClone(), ["event"] = rule["event"]?.DeepClone() }).ToArray());
        result.Remove("returnContext");
        return result;
    }
    private Task<JsonObject> ResolveStepAsync(StudioPrincipal principal, StudioResource root, StudioRevision revision, string nId,
        DashboardNavigationRequest request, bool first, string[]? instanceIds, string? knownRevisionNId, CancellationToken ct) => first
        ? dashboards.ResolveAuthorizedScreenSourceAsync(principal, root, revision, request, instanceIds, knownRevisionNId, ct)
        : dashboards.ResolveAsync(principal, nId, request, ct, dashboardOnly: true, targetInstanceIds: instanceIds, knownTargetRevisionNId: knownRevisionNId);
    private async Task<(StudioResource Resource, StudioRevision Revision)> ReadRootAsync(StudioPrincipal principal, StudioScreenLink link, CancellationToken ct)
    {
        var root = await publishing.RequireRuntimeAsync(principal, link.ResourceNId, link.FollowPublished ? null : link.RevisionNId, ct);
        await authorization.RequireAsync(principal, root.Resource, StudioKinds.PermissionPrefix(root.Resource.Kind) + ".publish", "Publish", ct);
        return root;
    }
    private void RequireActive(StudioScreenLink link)
    {
        if (link.RevokedOn is not null || !link.NeverExpires && link.ExpiresOn <= clock.GetUtcNow()) throw Missing();
    }
    private static DateTimeOffset? Expiry(StudioScreenLink link) => link.NeverExpires ? null : link.ExpiresOn;
    private static StudioException Missing() => new("STUDIO_SCREEN_UNAVAILABLE", "大屏链接无效、已失效或已撤销。", 404);
}
