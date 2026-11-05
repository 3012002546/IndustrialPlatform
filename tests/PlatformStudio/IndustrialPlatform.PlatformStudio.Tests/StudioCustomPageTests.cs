using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Api;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class StudioCustomPageTests
{
    [Fact]
    public async Task Native_menu_detach_failure_preserves_page_and_independent_delete_never_calls_platform_lifecycle()
    {
        await using var f = await StudioTestContext.CreateAsync();
        var lifecycle = new UnavailableMenuLifecycle();
        var publishing = new StudioPublishing(f.Store, f.Authorization, f.Commands, f.Profile, TimeProvider.System, [], f.SourceExecutor, [lifecycle]);
        var created = await publishing.CreateAsync(f.Principal, new("delete-page", "LowCodePage", "Page", null, null, Page()), "delete-create", default);
        f.Profile.DeploymentMode = "PlatformNative";
        var command = new IndustrialPlatform.PlatformStudio.Contracts.VersionCommand(created.OptimisticVersion, created.ConcurrencyVersion);
        Assert.Equal("STUDIO_MENU_SYNC_FAILED", (await Assert.ThrowsAsync<StudioException>(() => publishing.SetStateAsync(f.Principal, created.NId, "delete", command, default))).Code);
        Assert.False((await f.Store.FindResourceAsync(f.Principal.TenantNId, created.NId, default))!.IsDeleted);
        f.Profile.DeploymentMode = "ExternalMes";
        await publishing.SetStateAsync(f.Principal, created.NId, "delete", command, default);
        Assert.True((await f.Store.FindResourceAsync(f.Principal.TenantNId, created.NId, default))!.IsDeleted);
        Assert.Equal(1, lifecycle.Calls);
    }
    internal static JsonObject Page(string mode = "Html") => new() {
        ["schemaVersion"] = 1, ["mode"] = mode, ["parameters"] = new JsonArray(), ["instances"] = new JsonArray(), ["fields"] = new JsonArray(), ["actions"] = new JsonArray(),
        ["layout"] = new JsonObject { ["id"] = "root", ["type"] = "Container", ["registryVersion"] = "1", ["props"] = new JsonObject { ["columns"] = 24, ["gapPx"] = 12 }, ["children"] = new JsonArray(), ["grid"] = new JsonObject { ["span"] = 24, ["order"] = 0, ["minWidth"] = 80 } },
        ["customHtml"] = new JsonObject { ["html"] = "<button id='run'>运行</button>", ["css"] = "body{margin:16px}", ["js"] = "run.onclick=()=>run.textContent='完成'", ["formFields"] = new JsonArray("name") }
    };

    [Fact]
    public async Task Independent_deployment_rejects_forged_menu_requests_even_with_a_registered_host()
    {
        await using var f = await StudioTestContext.CreateAsync();
        var host = new TestMenuHost();
        var menus = new StudioPageNavigation(f.Profile, f.Store, f.Authorization, [host]);
        Assert.Equal("ExternalMes", menus.Capabilities(f.Principal).DeploymentMode);
        Assert.False(menus.Capabilities(f.Principal).CanMountMenu);
        var error = await Assert.ThrowsAsync<StudioException>(() => menus.MountAsync(f.Principal, "page", new(null, "g", "label", null, 0, "0"), CancellationToken.None));
        Assert.Equal("STUDIO_MENU_UNSUPPORTED", error.Code); Assert.False(host.Called);
        f.Profile.DeploymentMode = "PlatformNative";
        Assert.False(menus.Capabilities(f.Principal).CanMountMenu);
        Assert.True(menus.Capabilities(f.Principal with { Permissions = f.Principal.Permissions.Concat(["systemdata.navigation.manage", "systemdata.navigation.publish"]).ToArray() }).CanMountMenu);
    }

    [Fact]
    public async Task Custom_content_uses_exact_draft_and_frozen_revision_with_single_use_tickets()
    {
        await using var f = await StudioTestContext.CreateAsync();
        var page = Page();
        Assert.Empty(StudioDocuments.Validate("LowCodePage", page));
        var resource = await f.Publishing.CreateAsync(f.Principal, new("html", "LowCodePage", "HTML", null, null, page), "html-create", CancellationToken.None);
        var tickets = new StudioCustomContentTickets(TimeProvider.System);
        var content = new StudioCustomContent(f.Publishing, f.Profile, tickets);
        var prepared = JsonSerializer.SerializeToNode(await content.PrepareAsync(f.Principal, "html", new(null, resource.OptimisticVersion, new()), "https://platform.test", CancellationToken.None))!;
        var key = prepared["contentUrl"]!.GetValue<string>().Split('/')[^1];
        Assert.Contains("run.onclick", tickets.Consume(key), StringComparison.Ordinal);
        Assert.Equal(404, Assert.Throws<StudioException>(() => tickets.Consume(key)).StatusCode);
        var error = await Assert.ThrowsAsync<StudioException>(() => content.PrepareAsync(f.Principal, "html", new(null, "999", null), null, CancellationToken.None));
        Assert.Equal("STUDIO_VERSION_CONFLICT", error.Code);
        var plan = await f.Publishing.ValidateAsync(f.Principal, "html", page, CancellationToken.None);
        var published = await f.Publishing.PublishAsync(f.Principal, "html", new(resource.OptimisticVersion, resource.ConcurrencyVersion, "v1", true, plan.DependencyPlanHash), "html-publish", CancellationToken.None);
        Assert.NotNull(await content.PrepareAsync(f.Principal, "html", new(published.NId, null, new()), null, CancellationToken.None));
        Assert.Equal(404, (await Assert.ThrowsAsync<StudioException>(() => content.PrepareAsync(f.Principal with { TenantNId = "other" }, "html", new(published.NId, null, new()), null, CancellationToken.None))).StatusCode);
    }

    [Fact]
    public void Link_origin_and_parameter_encoding_are_enforced_without_fetching_the_target()
    {
        var page = Page("Link");
        page["embeddedPage"] = new JsonObject { ["url"] = "https://mes.test/view?fixed=1&batch=old#tab", ["parameterMap"] = new JsonArray(new JsonObject { ["target"] = "batch", ["source"] = new JsonObject { ["from"] = "PageParam", ["path"] = new JsonArray("batch"), ["nullPolicy"] = "Error" } }) };
        Assert.Empty(StudioDocuments.Validate("LowCodePage", page));
        var profile = new StudioDeploymentProfile { AllowedFrameOrigins = ["https://mes.test/"], PublicOrigin = "https://platform.test" };
        var url = StudioCustomPagePolicy.ResolveLink(page, profile, new() { ["batch"] = "A&B / 中文" });
        Assert.Contains("batch=A%26B%20%2F%20", url, StringComparison.Ordinal); Assert.EndsWith("#tab", url, StringComparison.Ordinal);
        Assert.DoesNotContain("batch=old", url, StringComparison.Ordinal);
        profile.AllowedFrameOrigins = [];
        Assert.Equal("STUDIO_CUSTOM_URL_FORBIDDEN", Assert.Throws<StudioException>(() => StudioCustomPagePolicy.RequireLink(page, profile)).Code);
        profile.AllowedFrameOrigins = ["https://mes.test"]; profile.PublicOrigin = "https://mes.test";
        Assert.Throws<StudioException>(() => StudioCustomPagePolicy.RequireLink(page, profile));
        page["embeddedPage"]!["url"] = "javascript:alert(1)";
        Assert.Contains(StudioDocuments.Validate("LowCodePage", page), d => d.Code == "STUDIO_CUSTOM_URL_FORBIDDEN");
    }

    private sealed class TestMenuHost : IStudioPageNavigationHost
    {
        public bool Called { get; private set; }
        public Task<StudioMenuOptions> ReadAsync(StudioPrincipal principal, string resourceNId, CancellationToken ct) => throw new NotSupportedException();
        public Task<StudioMenuOptions> MountAsync(StudioPrincipal principal, string resourceNId, StudioMenuCommand command, CancellationToken ct) { Called = true; throw new NotSupportedException(); }
        public Task<StudioMenuOptions> DetachAsync(StudioPrincipal principal, string resourceNId, string? nodeNId, string? expectedRevision, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class UnavailableMenuLifecycle : IStudioPageLifecycle
    {
        public int Calls { get; private set; }
        public Task BeforeDeleteAsync(StudioPrincipal principal, string resourceNId, CancellationToken ct)
        { Calls++; throw new StudioException("STUDIO_MENU_SYNC_FAILED", "Fixture navigation unavailable.", 409); }
    }
}
