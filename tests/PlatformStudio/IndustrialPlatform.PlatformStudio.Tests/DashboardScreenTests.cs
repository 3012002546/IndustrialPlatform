using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Application.Dashboard;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class DashboardScreenTests
{
    private static async Task<JsonObject> CreatePlaylistLinkAsync(StudioIdentityTests.ApiFixture f, bool follow = true, bool parameters = true)
    {
        await using var scope = f.Services.CreateAsyncScope();
        var publishing = scope.ServiceProvider.GetRequiredService<StudioPublishing>();
        var me = (await (await f.SendAsync(HttpMethod.Get, "/api/v1/studio/auth/me")).Content.ReadFromJsonAsync<JsonObject>())!;
        var principal = me["data"]!["principal"]!.Deserialize<StudioPrincipal>(StudioCommands.JsonOptions)!;
        JsonObject Item(string id, string value, bool enabled = true) => new() {
            ["id"] = id, ["label"] = id, ["dashboardNId"] = "public-dashboard", ["staySeconds"] = 10, ["enabled"] = enabled,
            ["parameterMap"] = new JsonArray(new JsonObject { ["target"] = "lineId", ["source"] = parameters && id == "one"
                ? new JsonObject { ["from"] = "PageParam", ["path"] = new JsonArray("line"), ["nullPolicy"] = "Error" }
                : new JsonObject { ["from"] = "Constant", ["value"] = value, ["nullPolicy"] = "Error" } }),
        };
        var document = new JsonObject { ["schemaVersion"] = 1, ["loop"] = true, ["onFailure"] = "Skip", ["preloadCount"] = 1,
            ["parameters"] = parameters ? new JsonArray(new JsonObject { ["name"] = "line", ["type"] = "String", ["source"] = "Caller", ["required"] = true, ["defaultValue"] = "L01" }) : new JsonArray(),
            ["items"] = new JsonArray(Item("one", "L01"), Item("two", "L02"), Item("disabled", "L03", false)) };
        var resource = await publishing.CreateAsync(principal, new("screen-playlist", "Playlist", "轮播大屏", null, null, document), "create-screen-playlist", Ct);
        var plan = await publishing.ValidateAsync(principal, resource.NId, document, Ct);
        Assert.True(plan.Valid, string.Join(';', plan.Diagnostics));
        var revision = await publishing.PublishAsync(principal, resource.NId, new(resource.OptimisticVersion, resource.ConcurrencyVersion, "screen", true, plan.DependencyPlanHash), "publish-screen-playlist", Ct);
        var response = await f.SendAsync(HttpMethod.Post, "/api/v1/studio/playlists/screen-playlist/screens", new { revisionNId = revision.NId, parameters = new { }, followPublished = follow });
        var payload = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.True(response.IsSuccessStatusCode, payload.ToJsonString());
        return payload["data"]!.AsObject();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Playlist_screen_uses_server_manifest_item_mapping_and_live_grants(bool parameters)
    {
        var (fixture, _) = await CreateAsync(null, true); await using var f = fixture;
        var link = await CreatePlaylistLinkAsync(f, parameters: parameters);
        if (parameters)
        {
            var invalid = await f.SendAsync(HttpMethod.Post, "/api/v1/studio/playlists/screen-playlist/screens", new { revisionNId = StudioJson.Text(link["revisionNId"]), parameters = new { line = 42 } });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        var manifestResponse = await PublicAsync(f, link);
        Assert.Equal(HttpStatusCode.OK, manifestResponse.StatusCode);
        var manifest = (await manifestResponse.Content.ReadFromJsonAsync<JsonObject>())!["data"]!;
        Assert.Equal("Playlist", StudioJson.Text(manifest["kind"]));
        Assert.Equal(2, manifest["items"]!.AsArray().Count);
        Assert.Null(manifest["document"]);
        Assert.Null(manifest["parameters"]);
        Assert.DoesNotContain("dashboardNId", manifest.ToJsonString());
        Assert.DoesNotContain("parameterMap", manifest.ToJsonString());
        foreach (var (item, expected) in new[] { ("one", "L01"), ("two", "L02") })
        {
            var response = await PublicAsync(f, link, suffix: "?playlistItemId=" + item + "&parameters=" + Uri.EscapeDataString("{\"lineId\":\"forged\",\"line\":\"forged\"}") + "&resourceNId=forged&revisionNId=forged");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var runtime = (await response.Content.ReadFromJsonAsync<JsonObject>())!["data"]!;
            Assert.Equal(expected, StudioJson.Text(runtime["parameters"]!["lineId"]));
            Assert.Equal("public-dashboard", StudioJson.Text(runtime["resource"]!["nId"]));
            Assert.Equal(manifest["revisionNId"]!.ToJsonString(), runtime["playlist"]!["revisionNId"]!.ToJsonString());
        }
        foreach (var item in new[] { "disabled", "missing", "public-dashboard" })
            Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(f, link, suffix: "?playlistItemId=" + item)).StatusCode);
        await using var scope = f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StudioDatabase>();
        var store = scope.ServiceProvider.GetRequiredService<IStudioResourceStore>();
        foreach (var (nId, capability) in new[] { ("screen-playlist", "Run"), ("screen-playlist", "Publish"), ("public-dashboard", "Run") })
        {
            var resource = (await store.FindResourceAsync("tenant-a", nId, Ct))!;
            await db.Client.Updateable<StudioGrant>().SetColumns(grant => new StudioGrant { RevokedOn = DateTimeOffset.UtcNow }).Where(grant => grant.ResourceId == resource.Id && grant.Capability == capability).ExecuteCommandAsync();
            Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(f, link, suffix: "?playlistItemId=one")).StatusCode);
            if (nId == "screen-playlist") Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(f, link)).StatusCode);
            await db.Client.Updateable<StudioGrant>().SetColumns(grant => new StudioGrant { RevokedOn = null }).Where(grant => grant.ResourceId == resource.Id && grant.Capability == capability).ExecuteCommandAsync();
        }
        var list = (await (await f.SendAsync(HttpMethod.Get, "/api/v1/studio/playlists/screen-playlist/screens")).Content.ReadFromJsonAsync<JsonObject>())!["data"]!.AsArray();
        Assert.Equal(HttpStatusCode.OK, (await PublicAsync(f, Assert.Single(list)!.AsObject())).StatusCode);
        await f.SendAsync(HttpMethod.Delete, $"/api/v1/studio/playlists/screen-playlist/screens/{link["id"]!.GetValue<string>()}");
        Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(f, link)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(f, link, suffix: "?playlistItemId=one")).StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Playlist_link_honors_fixed_or_latest_revision_and_expiry(bool follow)
    {
        var (fixture, _) = await CreateAsync(null, true); await using var f = fixture;
        var link = await CreatePlaylistLinkAsync(f, follow);
        await using var scope = f.Services.CreateAsyncScope();
        var publishing = scope.ServiceProvider.GetRequiredService<StudioPublishing>();
        var me = (await (await f.SendAsync(HttpMethod.Get, "/api/v1/studio/auth/me")).Content.ReadFromJsonAsync<JsonObject>())!;
        var principal = me["data"]!["principal"]!.Deserialize<StudioPrincipal>(StudioCommands.JsonOptions)!;
        var draft = await publishing.GetDraftAsync(principal, "screen-playlist", Ct);
        var document = draft["draftDocument"]!.DeepClone().AsObject();
        document["items"]![0]!["label"] = "new label";
        var saved = await publishing.SaveDraftAsync(principal, "screen-playlist", new(draft["optimisticVersion"]!.GetValue<string>(), Guid.Parse(draft["concurrencyVersion"]!.GetValue<string>()), draft["name"]!.GetValue<string>(), null, null, document), Ct);
        var plan = await publishing.ValidateAsync(principal, "screen-playlist", document, Ct);
        var revision = await publishing.PublishAsync(principal, "screen-playlist", new(saved["optimisticVersion"]!.GetValue<string>(), Guid.Parse(saved["concurrencyVersion"]!.GetValue<string>()), "updated", true, plan.DependencyPlanHash), "playlist-update", Ct);
        var manifest = (await (await PublicAsync(f, link)).Content.ReadFromJsonAsync<JsonObject>())!["data"]!;
        Assert.Equal(follow ? revision.NId : StudioJson.Text(link["revisionNId"]), StudioJson.Text(manifest["revisionNId"]));
        Assert.Equal(follow ? "new label" : "one", StudioJson.Text(manifest["items"]![0]!["label"]));
        var runtime = (await (await PublicAsync(f, link, suffix: "?playlistItemId=one")).Content.ReadFromJsonAsync<JsonObject>())!["data"]!;
        Assert.Equal(manifest["revisionNId"]!.ToJsonString(), runtime["playlist"]!["revisionNId"]!.ToJsonString());
        var db = scope.ServiceProvider.GetRequiredService<StudioDatabase>();
        var id = Guid.Parse(StudioJson.Text(link["id"])!);
        await db.Client.Updateable<StudioScreenLink>().SetColumns(item => new StudioScreenLink { NeverExpires = false, ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(-1) }).Where(item => item.Id == id).ExecuteCommandAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(f, link)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(f, link, suffix: "?playlistItemId=one")).StatusCode);
    }

    [Theory]
    [InlineData(false, "full")]
    [InlineData(false, "current")]
    [InlineData(false, "outdated")]
    [InlineData(true, "full")]
    [InlineData(true, "current")]
    [InlineData(true, "outdated")]
    public async Task Screen_runtime_reads_each_authorized_resource_and_revision_once_per_request(bool playlist, string refresh)
    {
        var (fixture, link) = await CreateAsync(null, true); await using var f = fixture;
        var dashboardRevisionNId = StudioJson.Text(link["revisionNId"]);
        if (playlist) link = await CreatePlaylistLinkAsync(f);
        await using var scope = f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StudioDatabase>();
        var screens = scope.ServiceProvider.GetRequiredService<DashboardScreens>();
        var statements = new List<string>();
        db.Client.Aop.OnLogExecuting = (sql, _) => statements.Add(sql);
        try
        {
            // Both requests must perform fresh resource/grant reads; only immutable values within one request are reused.
            for (var request = 0; request < 2; request++)
            {
                statements.Clear();
                var runtime = await screens.RuntimeAsync(Guid.Parse(StudioJson.Text(link["id"])!), StudioJson.Text(link["key"]), Ct,
                    instanceIds: refresh == "full" ? null : [], knownRevisionNId: refresh == "current" ? dashboardRevisionNId : refresh == "outdated" ? "old-revision" : null,
                    playlistItemId: playlist ? "one" : null);
                Assert.Equal(dashboardRevisionNId, StudioJson.Text(runtime["revisionNId"]));
                Assert.Equal("L01", StudioJson.Text(runtime["parameters"]!["lineId"]));
                Assert.Equal(playlist ? 2 : 1, statements.Count(sql => sql.Contains("studio_resource", StringComparison.OrdinalIgnoreCase)));
                Assert.Equal(playlist ? 2 : 1, statements.Count(sql => sql.Contains("studio_revision", StringComparison.OrdinalIgnoreCase)));
                // Root Run and Publish remain independent checks; playlist items additionally check target Run.
                Assert.Equal(playlist ? 3 : 2, statements.Count(sql => sql.Contains("studio_grant", StringComparison.OrdinalIgnoreCase)));
            }
        }
        finally { db.Client.Aop.OnLogExecuting = null; }
    }

    [Fact]
    public async Task Playlist_runtime_reuses_current_revision_summary_but_rechecks_run_access_each_request()
    {
        var (fixture, _) = await CreateAsync(null, true); await using var f = fixture;
        await CreatePlaylistLinkAsync(f);
        var me = (await (await f.SendAsync(HttpMethod.Get, "/api/v1/studio/auth/me")).Content.ReadFromJsonAsync<JsonObject>())!;
        var principal = me["data"]!["principal"]!.Deserialize<StudioPrincipal>(StudioCommands.JsonOptions)!;
        await using var scope = f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StudioDatabase>();
        var dashboards = scope.ServiceProvider.GetRequiredService<DashboardNavigation>();
        var statements = new List<string>();
        db.Client.Aop.OnLogExecuting = (sql, _) => statements.Add(sql);
        try
        {
            for (var request = 0; request < 2; request++)
            {
                statements.Clear();
                var runtime = await dashboards.PlaylistRuntimeAsync(principal, "screen-playlist", null, Ct);
                Assert.Equal(StudioJson.Text(runtime["revisionNId"]), StudioJson.Text(runtime["resource"]?["publishedRevisionNId"]));
                Assert.Equal(1, statements.Count(sql => sql.Contains("studio_resource", StringComparison.OrdinalIgnoreCase)));
                Assert.Equal(1, statements.Count(sql => sql.Contains("studio_revision", StringComparison.OrdinalIgnoreCase)));
                Assert.Equal(1, statements.Count(sql => sql.Contains("studio_grant", StringComparison.OrdinalIgnoreCase)));
            }
            var resource = (await scope.ServiceProvider.GetRequiredService<IStudioResourceStore>().FindResourceAsync("tenant-a", "screen-playlist", Ct))!;
            await db.Client.Updateable<StudioGrant>().SetColumns(grant => new StudioGrant { RevokedOn = DateTimeOffset.UtcNow })
                .Where(grant => grant.ResourceId == resource.Id && grant.Capability == "Run").ExecuteCommandAsync();
            var error = await Assert.ThrowsAsync<StudioException>(() => dashboards.PlaylistRuntimeAsync(principal, "screen-playlist", null, Ct));
            Assert.Equal(404, error.StatusCode);
        }
        finally { db.Client.Aop.OnLogExecuting = null; }
    }

    [Fact]
    public async Task Link_state_recheck_is_one_thin_query_and_observes_expiry_and_revocation()
    {
        var (fixture, link) = await CreateAsync(1); await using var f = fixture;
        await using var scope = f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StudioDatabase>();
        var store = new StudioScreenStore(db);
        var id = Guid.Parse(link["id"]!.GetValue<string>());
        var statements = new List<string>();
        db.Client.Aop.OnLogExecuting = (sql, _) => statements.Add(sql);
        Assert.True(await store.IsActiveAsync(id, DateTimeOffset.UtcNow, Ct));
        var sql = Assert.Single(statements);
        Assert.DoesNotContain("ParametersJson", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TokenHash", sql, StringComparison.OrdinalIgnoreCase);
        db.Client.Aop.OnLogExecuting = null;
        Assert.False(await store.IsActiveAsync(id, DateTimeOffset.UtcNow.AddHours(2), Ct));
        Assert.False(await store.IsActiveAsync(Guid.NewGuid(), DateTimeOffset.UtcNow, Ct));
        await store.RevokeAsync(id, "tenant-a", "public-dashboard", DateTimeOffset.UtcNow, Ct);
        Assert.False(await store.IsActiveAsync(id, DateTimeOffset.UtcNow, Ct));
    }

    private sealed class RevokeOnRecheck(IStudioScreenStore inner, string resource = "public-dashboard") : IStudioScreenStore
    {
        public Task InsertAsync(StudioScreenLink link,CancellationToken ct)=>inner.InsertAsync(link,ct);
        public Task<StudioScreenLink?> FindAsync(Guid id,CancellationToken ct)=>inner.FindAsync(id,ct);
        public Task<IReadOnlyList<StudioScreenLink>> ListAsync(string tenant,string resource,CancellationToken ct)=>inner.ListAsync(tenant,resource,ct);
        public Task RevokeAsync(Guid id,string tenant,string resource,DateTimeOffset now,CancellationToken ct)=>inner.RevokeAsync(id,tenant,resource,now,ct);
        public async Task<bool> IsActiveAsync(Guid id,DateTimeOffset now,CancellationToken ct)
        {
            await inner.RevokeAsync(id,"tenant-a",resource,now,ct);
            return await inner.IsActiveAsync(id,now,ct);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Revocation_during_runtime_is_rechecked_before_returning_data(bool playlist, bool item)
    {
        var (fixture, link) = await CreateAsync(null); await using var f = fixture;
        if (playlist) link = await CreatePlaylistLinkAsync(f);
        await using var scope = f.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var screens = new DashboardScreens(new RevokeOnRecheck(services.GetRequiredService<IStudioScreenStore>(), playlist ? "screen-playlist" : "public-dashboard"),
            services.GetRequiredService<IStudioScreenIdentity>(),services.GetRequiredService<IStudioResourceStore>(),
            services.GetRequiredService<StudioPublishing>(),services.GetRequiredService<IStudioAuthorization>(),
            services.GetRequiredService<DashboardNavigation>(),TimeProvider.System,services.GetRequiredService<IStudioScreenKeys>());
        var error = await Assert.ThrowsAsync<StudioException>(() => screens.RuntimeAsync(Guid.Parse(link["id"]!.GetValue<string>()),link["key"]!.GetValue<string>(),Ct, playlistItemId: item ? "one" : null));
        Assert.Equal("STUDIO_SCREEN_UNAVAILABLE",error.Code);
    }
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static async Task<(StudioIdentityTests.ApiFixture Fixture, JsonObject Link)> CreateAsync(int? lifetimeHours = 24, bool followPublished = false)
    {
        var f = await StudioIdentityTests.ApiFixture.CreateAsync(dashboard: true);
        try
        {
            await f.CreateAdminAsync();
            await using var scope = f.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<StudioDatabase>();
            await db.Client.Updateable<StudioLocalAdmin>().SetColumns(x => new StudioLocalAdmin { MustChangePassword = false }).Where(x => x.TenantNId == "tenant-a").ExecuteCommandAsync();
            Assert.Equal(HttpStatusCode.OK, (await f.SendAsync(HttpMethod.Post, "/api/v1/studio/auth/local-login", new { tenantNId = "tenant-a", loginName = "admin", password = "Initial!Studio123" })).StatusCode);
            var me = (await (await f.SendAsync(HttpMethod.Get, "/api/v1/studio/auth/me")).Content.ReadFromJsonAsync<JsonObject>())!;
            var principal = me["data"]!["principal"]!.Deserialize<StudioPrincipal>(StudioCommands.JsonOptions)!;
            var publishing = scope.ServiceProvider.GetRequiredService<StudioPublishing>();
            var document = new JsonObject {
                ["schemaVersion"] = 1, ["canvas"] = new JsonObject { ["width"] = 1920, ["height"] = 1080, ["fit"] = "Contain", ["background"] = "#081525" },
                ["parameters"] = new JsonArray(new JsonObject { ["name"] = "lineId", ["type"] = "String", ["source"] = "Caller", ["required"] = true, ["nullable"] = false }),
                ["instances"] = new JsonArray(), ["components"] = new JsonArray(), ["navigation"] = new JsonArray(),
            };
            var resource = await publishing.CreateAsync(principal, new("public-dashboard", "Dashboard", "匿名大屏测试", null, null, document), "create-public-dashboard", Ct);
            var plan = await publishing.ValidateAsync(principal, resource.NId, document, Ct);
            Assert.True(plan.Valid, string.Join(';', plan.Diagnostics));
            var revision = await publishing.PublishAsync(principal, resource.NId, new(resource.OptimisticVersion, resource.ConcurrencyVersion, "screen", true, plan.DependencyPlanHash), "publish-public-dashboard", Ct);
            var response = await f.SendAsync(HttpMethod.Post, "/api/v1/studio/dashboards/public-dashboard/screens", new { revisionNId = revision.NId, parameters = new { lineId = "L01" }, lifetimeHours, followPublished });
            var payload = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
            Assert.True(response.IsSuccessStatusCode, payload.ToJsonString());
            return (f, payload["data"]!.AsObject());
        }
        catch { await f.DisposeAsync(); throw; }
    }
    private static Task<HttpResponseMessage> PublicAsync(StudioIdentityTests.ApiFixture f, JsonObject link, string? key = null, string suffix = "")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/studio/screens/{link["id"]!.GetValue<string>()}/runtime{suffix}");
        if (key != "missing") request.Headers.Add("X-Dashboard-Screen-Key", key ?? link["key"]!.GetValue<string>());
        // 绕开Fixture的Cookie附加方法，证明真正匿名请求可用。
        return f.Client.SendAsync(request);
    }

    [Fact]
    public async Task Existing_hashed_links_can_be_copied_without_replacing_old_keys_and_all_copies_revoke_together()
    {
        var (fixture, original) = await CreateAsync(null, true); await using var f = fixture;
        var response = await f.SendAsync(HttpMethod.Get, "/api/v1/studio/dashboards/public-dashboard/screens");
        Assert.True(response.Headers.CacheControl!.NoStore);
        var listed = Assert.Single((await response.Content.ReadFromJsonAsync<JsonObject>())!["data"]!.AsArray())!.AsObject();
        var key = listed["key"]!.GetValue<string>();
        Assert.StartsWith("v1.", key);
        Assert.DoesNotContain(original["key"]!.GetValue<string>(), key);
        Assert.Equal(HttpStatusCode.OK, (await PublicAsync(f, listed)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PublicAsync(f, original)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(f, listed, key[..12] + "!" + key[13..])).StatusCode);
        var secondResponse = await f.SendAsync(HttpMethod.Post, "/api/v1/studio/dashboards/public-dashboard/screens", new {
            revisionNId = original["revisionNId"]!.GetValue<string>(), parameters = new { lineId = "L01" } });
        var second = (await secondResponse.Content.ReadFromJsonAsync<JsonObject>())!["data"]!.AsObject();
        Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(f, second, key)).StatusCode);
        await f.SendAsync(HttpMethod.Delete, $"/api/v1/studio/dashboards/public-dashboard/screens/{original["id"]!.GetValue<string>()}");
        Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(f, original)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(f, listed)).StatusCode);
        var history = (await (await f.SendAsync(HttpMethod.Get, "/api/v1/studio/dashboards/public-dashboard/screens")).Content.ReadFromJsonAsync<JsonObject>())!["data"]!.AsArray();
        var revoked = history.OfType<JsonObject>().Single(item => item["id"]!.GetValue<string>() == original["id"]!.GetValue<string>());
        Assert.NotNull(revoked["revokedOn"]);
        Assert.Null(revoked["key"]);
    }

    [Theory]
    [InlineData("Legacy", false)]
    [InlineData("Legacy", true)]
    [InlineData("Constant", false)]
    [InlineData("Constant", true)]
    [InlineData("Duplicate", false)]
    public async Task Anonymous_batch_navigation_is_derived_from_the_original_link_and_rechecks_revocation(string rowsSource, bool playlist)
    {
        var (fixture, link) = await CreateAsync(null, true); await using var f = fixture;
        await using var scope = f.Services.CreateAsyncScope();
        var publishing = scope.ServiceProvider.GetRequiredService<StudioPublishing>();
        var me = (await (await f.SendAsync(HttpMethod.Get, "/api/v1/studio/auth/me")).Content.ReadFromJsonAsync<JsonObject>())!;
        var principal = me["data"]!["principal"]!.Deserialize<StudioPrincipal>(StudioCommands.JsonOptions)!;
        var targetDocument = new JsonObject {
            ["schemaVersion"] = 1, ["canvas"] = new JsonObject { ["width"] = 1920, ["height"] = 1080, ["fit"] = "Contain" },
            ["parameters"] = new JsonArray(new JsonObject { ["name"] = "batchNo", ["type"] = "String", ["source"] = "Caller", ["required"] = true, ["nullable"] = false }),
            ["instances"] = new JsonArray(), ["components"] = new JsonArray(), ["navigation"] = new JsonArray(),
        };
        targetDocument["instances"] = new JsonArray(new JsonObject { ["instanceId"] = "detail-data", ["sourceType"] = "Constant", ["value"] = 0 });
        targetDocument["parameters"]!.AsArray().Add(new JsonObject { ["name"] = "plant", ["type"] = "String", ["source"] = "Caller", ["required"] = true });
        targetDocument["parameters"]!.AsArray().Add(new JsonObject { ["name"] = "viewer", ["type"] = "String", ["source"] = "Caller", ["required"] = true });
        var target = await publishing.CreateAsync(principal, new("screen-detail", "Dashboard", "批次明细", null, null, targetDocument), "create-screen-detail", Ct);
        var targetPlan = await publishing.ValidateAsync(principal, target.NId, targetDocument, Ct);
        Assert.True(targetPlan.Valid, string.Join(';', targetPlan.Diagnostics));
        var targetRevision = await publishing.PublishAsync(principal, target.NId, new(target.OptimisticVersion, target.ConcurrencyVersion, "detail", true, targetPlan.DependencyPlanHash), "publish-screen-detail", Ct);
        var source = await publishing.GetDraftAsync(principal, "public-dashboard", Ct);
        var sourceDocument = source["draftDocument"]!.DeepClone().AsObject();
        sourceDocument["parameters"]!.AsArray().Add(new JsonObject { ["name"] = "plant", ["type"] = "String", ["source"] = "Caller", ["defaultValue"] = "FAB-01", ["required"] = true });
        sourceDocument["parameters"]!.AsArray().Add(new JsonObject { ["name"] = "viewer", ["type"] = "String", ["source"] = "TrustedContext", ["contextKey"] = "user", ["required"] = true });
        sourceDocument["instances"] = new JsonArray(new JsonObject { ["instanceId"] = "batches", ["sourceType"] = "Constant", ["value"] = new JsonObject { ["rows"] = new JsonArray(new JsonObject { ["batch"] = "B-01" }) } });
        sourceDocument["components"] = new JsonArray(new JsonObject {
            ["id"] = "batch-table", ["type"] = "Table", ["registryVersion"] = "1", ["name"] = "批次", ["x"] = 0, ["y"] = 0, ["width"] = 600, ["height"] = 300, ["zIndex"] = 0, ["rotation"] = 0,
            ["props"] = new JsonObject { ["instanceId"] = "batches", ["collectionPath"] = new JsonArray("rows"), ["rowKeyPath"] = new JsonArray("batch"), ["columns"] = new JsonArray(new JsonObject { ["field"] = "batch", ["label"] = "批次" }) },
            ["bindings"] = new JsonArray(), ["events"] = new JsonArray("detail"), ["children"] = new JsonArray(),
        });
        if (rowsSource != "Legacy")
        {
            var rows = new JsonArray(new JsonObject { ["batch"] = "B-01" });
            if (rowsSource == "Duplicate") rows.Add(new JsonObject { ["batch"] = "B-01" });
            sourceDocument["components"]![0]!["props"]!.AsObject().Remove("instanceId");
            sourceDocument["components"]![0]!["props"]!.AsObject().Remove("collectionPath");
            sourceDocument["components"]![0]!["bindings"] = new JsonArray(new JsonObject { ["property"] = "rows", ["source"] = new JsonObject { ["from"] = "Constant", ["value"] = rows, ["nullPolicy"] = "Error" } });
            sourceDocument["instances"] = new JsonArray();
        }
        sourceDocument["navigation"] = new JsonArray(new JsonObject {
            ["id"] = "detail", ["componentId"] = "batch-table", ["event"] = "RowClick", ["openMode"] = "Current",
            ["target"] = new JsonObject { ["kind"] = "Dashboard", ["resourceNId"] = target.NId, ["revisionNId"] = targetRevision.NId },
            ["parameterMap"] = new JsonArray(
                new JsonObject { ["target"] = "batchNo", ["source"] = new JsonObject { ["from"] = "Row", ["path"] = new JsonArray("batch"), ["nullPolicy"] = "Error" } },
                new JsonObject { ["target"] = "plant", ["source"] = new JsonObject { ["from"] = "PageParam", ["path"] = new JsonArray("plant"), ["nullPolicy"] = "Error" } },
                new JsonObject { ["target"] = "viewer", ["source"] = new JsonObject { ["from"] = "PageParam", ["path"] = new JsonArray("viewer"), ["nullPolicy"] = "Error" } }),
        });
        var saved = await publishing.SaveDraftAsync(principal, "public-dashboard", new(source["optimisticVersion"]!.GetValue<string>(), Guid.Parse(source["concurrencyVersion"]!.GetValue<string>()), source["name"]!.GetValue<string>(), null, null, sourceDocument), Ct);
        var plan = await publishing.ValidateAsync(principal, "public-dashboard", sourceDocument, Ct);
        Assert.True(plan.Valid, string.Join(';', plan.Diagnostics));
        await publishing.PublishAsync(principal, "public-dashboard", new(saved["optimisticVersion"]!.GetValue<string>(), Guid.Parse(saved["concurrencyVersion"]!.GetValue<string>()), "batch link", true, plan.DependencyPlanHash), "publish-screen-navigation", Ct);
        if (playlist) link = await CreatePlaylistLinkAsync(f);
        var itemQuery = playlist ? "playlistItemId=one&" : "";
        var root = (await (await PublicAsync(f, link, suffix: playlist ? "?playlistItemId=one" : "")).Content.ReadFromJsonAsync<JsonObject>())!["data"]!;
        var rule = Assert.Single(root["document"]!["navigation"]!.AsArray())!;
        Assert.Null(rule["target"]);
        Assert.Null(rule["parameterMap"]);
        var steps = new JsonArray(new JsonObject { ["ruleId"] = "detail", ["rowKey"] = new JsonObject { ["key"] = "B-01", ["batch"] = "forged" }, ["targetResourceNId"] = "forged", ["parameters"] = new JsonObject { ["batchNo"] = "forged" } });
        string Suffix() => "?" + itemQuery + "navigation=" + Uri.EscapeDataString(steps.ToJsonString());
        var response = await PublicAsync(f, link, suffix: Suffix());
        if (rowsSource == "Duplicate")
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("STUDIO_ROW_KEY_AMBIGUOUS", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            return;
        }
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = (await response.Content.ReadFromJsonAsync<JsonObject>())!["data"]!;
        Assert.Equal(targetRevision.NId, detail["revisionNId"]!.GetValue<string>());
        Assert.Equal("B-01", detail["parameters"]!["batchNo"]!.GetValue<string>());
        Assert.Equal("FAB-01", detail["parameters"]!["plant"]!.GetValue<string>());
        Assert.Equal(principal.SubjectNId, detail["parameters"]!["viewer"]!.GetValue<string>());
        Assert.Single(detail["instances"]!.AsObject());
        var heartbeat = (await (await PublicAsync(f, link, suffix: Suffix() + "&instanceIds=[]&knownRevisionNId=" + targetRevision.NId)).Content.ReadFromJsonAsync<JsonObject>())!["data"]!;
        Assert.Empty(heartbeat["instances"]!.AsObject());
        var changed = (await (await PublicAsync(f, link, suffix: Suffix() + "&instanceIds=[]&knownRevisionNId=outdated")).Content.ReadFromJsonAsync<JsonObject>())!["data"]!;
        Assert.Single(changed["instances"]!.AsObject());
        steps[0]!["rowKey"]!["key"] = "HIDDEN";
        Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(f, link, suffix: Suffix())).StatusCode);
        steps[0]!["rowKey"]!["key"] = "B-01";
        var db = scope.ServiceProvider.GetRequiredService<StudioDatabase>();
        var store = scope.ServiceProvider.GetRequiredService<IStudioResourceStore>();
        var sourceResource = (await store.FindResourceAsync(principal.TenantNId, playlist ? "screen-playlist" : "public-dashboard", Ct))!;
        var targetResource = (await store.FindResourceAsync(principal.TenantNId, target.NId, Ct))!;
        foreach (var (resourceId, capability) in new[] { (sourceResource.Id, "Run"), (sourceResource.Id, "Publish"), (targetResource.Id, "Run") })
        {
            await db.Client.Updateable<StudioGrant>().SetColumns(grant => new StudioGrant { RevokedOn = DateTimeOffset.UtcNow })
                .Where(grant => grant.ResourceId == resourceId && grant.Capability == capability).ExecuteCommandAsync();
            Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(f, link, suffix: Suffix())).StatusCode);
            await db.Client.Updateable<StudioGrant>().SetColumns(grant => new StudioGrant { RevokedOn = null })
                .Where(grant => grant.ResourceId == resourceId && grant.Capability == capability).ExecuteCommandAsync();
            Assert.Equal(HttpStatusCode.OK, (await PublicAsync(f, link, suffix: Suffix())).StatusCode);
        }
        if (playlist) Assert.NotNull(detail["playlist"]);
        await f.SendAsync(HttpMethod.Delete, $"/api/v1/studio/{(playlist ? "playlists/screen-playlist" : "dashboards/public-dashboard")}/screens/{link["id"]!.GetValue<string>()}");
        Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(f, link, suffix: Suffix())).StatusCode);
    }

    [Fact]
    public async Task Unlimited_links_have_no_expiry_but_remain_revocable()
    {
        var (fixture, link) = await CreateAsync(null, true); await using var f = fixture;
        Assert.Null(link["expiresOn"]);
        Assert.True(link["followPublished"]!.GetValue<bool>());
        await using (var scope = f.Services.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<StudioDatabase>();
            await db.Client.Updateable<StudioScreenLink>().SetColumns(x => new StudioScreenLink { ExpiresOn = DateTimeOffset.UtcNow.AddYears(-1) }).Where(x => x.Id == Guid.Parse(link["id"]!.GetValue<string>())).ExecuteCommandAsync();
        }
        var response = await PublicAsync(f, link);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await response.Content.ReadFromJsonAsync<JsonObject>())!["data"]!["expiresOn"]);
        var list = (await (await f.SendAsync(HttpMethod.Get, "/api/v1/studio/dashboards/public-dashboard/screens")).Content.ReadFromJsonAsync<JsonObject>())!["data"]!.AsArray();
        Assert.Null(Assert.Single(list)!["expiresOn"]);
        await f.SendAsync(HttpMethod.Delete, $"/api/v1/studio/dashboards/public-dashboard/screens/{link["id"]!.GetValue<string>()}");
        Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(f, link)).StatusCode);
    }

    [Fact]
    public async Task New_links_default_to_unlimited_and_follow_published_but_owner_revocation_still_applies()
    {
        var (fixture, initial) = await CreateAsync(); await using var f = fixture;
        var response = await f.SendAsync(HttpMethod.Post, "/api/v1/studio/dashboards/public-dashboard/screens", new { revisionNId = initial["revisionNId"]!.GetValue<string>(), parameters = new { lineId = "L01" } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var link = (await response.Content.ReadFromJsonAsync<JsonObject>())!["data"]!.AsObject();
        Assert.Null(link["expiresOn"]);
        Assert.True(link["followPublished"]!.GetValue<bool>());
        await using var scope = f.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<StudioDatabase>().Client.Updateable<StudioLocalAdmin>().SetColumns(x => new StudioLocalAdmin { AuthVersion = 2 }).Where(x => x.TenantNId == "tenant-a").ExecuteCommandAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(f, link)).StatusCode);
    }

    [Fact]
    public async Task Same_following_link_ignores_draft_switches_on_publish_and_rollback_while_pinned_link_stays_fixed()
    {
        var (fixture, following) = await CreateAsync(null, true); await using var f = fixture;
        var pinnedResponse = await f.SendAsync(HttpMethod.Post, "/api/v1/studio/dashboards/public-dashboard/screens", new {
            revisionNId = following["revisionNId"]!.GetValue<string>(), parameters = new { lineId = "L01" }, lifetimeHours = 24, followPublished = false });
        var pinned = (await pinnedResponse.Content.ReadFromJsonAsync<JsonObject>())!["data"]!.AsObject();
        async Task<JsonObject> Runtime(JsonObject link) => (await (await PublicAsync(f, link)).Content.ReadFromJsonAsync<JsonObject>())!["data"]!.AsObject();
        var draft = (await (await f.SendAsync(HttpMethod.Get, "/api/v1/studio/resources/public-dashboard/draft")).Content.ReadFromJsonAsync<JsonObject>())!["data"]!;
        var document = draft["draftDocument"]!.DeepClone().AsObject();
        document["canvas"]!["background"] = "#123456";
        var savedResponse = await f.SendAsync(HttpMethod.Put, "/api/v1/studio/resources/public-dashboard/draft", new {
            optimisticVersion = draft["optimisticVersion"]!.GetValue<string>(), concurrencyVersion = draft["concurrencyVersion"]!.GetValue<string>(), name = "匿名大屏测试", document });
        Assert.Equal(HttpStatusCode.OK, savedResponse.StatusCode);
        var saved = (await savedResponse.Content.ReadFromJsonAsync<JsonObject>())!["data"]!;
        Assert.Equal("#081525", (await Runtime(following))["document"]!["canvas"]!["background"]!.GetValue<string>());
        var plan = (await (await f.SendAsync(HttpMethod.Post, "/api/v1/studio/resources/public-dashboard/validate", new { document })).Content.ReadFromJsonAsync<JsonObject>())!["data"]!;
        f.Client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var publishResponse = await f.SendAsync(HttpMethod.Post, "/api/v1/studio/resources/public-dashboard/publish", new {
            optimisticVersion = saved["optimisticVersion"]!.GetValue<string>(), concurrencyVersion = saved["concurrencyVersion"]!.GetValue<string>(), releaseNote = "调整已发布", enable = true, dependencyPlanHash = plan["dependencyPlanHash"]!.GetValue<string>() });
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        var revision = (await publishResponse.Content.ReadFromJsonAsync<JsonObject>())!["data"]!["nId"]!.GetValue<string>();
        var latest = await Runtime(following);
        Assert.Equal(revision, latest["revisionNId"]!.GetValue<string>());
        Assert.Equal("#123456", latest["document"]!["canvas"]!["background"]!.GetValue<string>());
        Assert.Equal("L01", latest["parameters"]!["lineId"]!.GetValue<string>());
        Assert.Equal(following["revisionNId"]!.GetValue<string>(), (await Runtime(pinned))["revisionNId"]!.GetValue<string>());
        var current = (await (await f.SendAsync(HttpMethod.Get, "/api/v1/studio/resources/public-dashboard/draft")).Content.ReadFromJsonAsync<JsonObject>())!["data"]!;
        f.Client.DefaultRequestHeaders.Remove("Idempotency-Key");
        f.Client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var rollback = await f.SendAsync(HttpMethod.Post, "/api/v1/studio/resources/public-dashboard/rollback", new {
            optimisticVersion = current["optimisticVersion"]!.GetValue<string>(), concurrencyVersion = current["concurrencyVersion"]!.GetValue<string>(), revisionNId = following["revisionNId"]!.GetValue<string>(), reason = "恢复版本" });
        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
        Assert.Equal(following["revisionNId"]!.GetValue<string>(), (await Runtime(following))["revisionNId"]!.GetValue<string>());
    }

    [Fact]
    public async Task Anonymous_link_is_pinned_cookie_free_hashed_persistent_and_revocable()
    {
        var (fixture, link) = await CreateAsync(); await using var f = fixture;
        Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(f, link, "missing")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(f, link, new string('0', 64))).StatusCode);
        var response = await PublicAsync(f, link, suffix: "?parameters=%7B%22lineId%22%3A%22L99%22%7D&revisionNId=foreign");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var runtime = (await response.Content.ReadFromJsonAsync<JsonObject>())!["data"]!;
        Assert.Equal(link["revisionNId"]!.GetValue<string>(), runtime["revisionNId"]!.GetValue<string>());
        Assert.Equal("L01", runtime["parameters"]!["lineId"]!.GetValue<string>());
        Assert.Empty(runtime["document"]!["navigation"]!.AsArray());
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client.GetAsync("/api/v1/studio/dashboards/public-dashboard/runtime")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client.GetAsync("/api/v1/studio/dashboards/public-dashboard/screens")).StatusCode);
        await using (var scope = f.Services.CreateAsyncScope())
        {
            var store = new StudioScreenStore(scope.ServiceProvider.GetRequiredService<StudioDatabase>());
            var saved = await store.FindAsync(Guid.Parse(link["id"]!.GetValue<string>()), Ct);
            Assert.Equal(StudioJson.Hash(link["key"]!.GetValue<string>()), saved!.TokenHash);
            Assert.NotEqual(link["key"]!.GetValue<string>(), saved.TokenHash);
        }
        Assert.Equal(HttpStatusCode.OK, (await f.SendAsync(HttpMethod.Post, "/api/v1/studio/auth/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PublicAsync(f, link)).StatusCode);
        await f.SendAsync(HttpMethod.Post, "/api/v1/studio/auth/local-login", new { tenantNId = "tenant-a", loginName = "admin", password = "Initial!Studio123" });
        Assert.Equal(HttpStatusCode.OK, (await f.SendAsync(HttpMethod.Delete, $"/api/v1/studio/dashboards/public-dashboard/screens/{link["id"]!.GetValue<string>()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PublicAsync(f, link)).StatusCode);
    }

    [Theory]
    [InlineData("expired", false)]
    [InlineData("expired", true)]
    [InlineData("disabled-publisher", false)]
    [InlineData("disabled-publisher", true)]
    [InlineData("changed-auth", false)]
    [InlineData("changed-auth", true)]
    [InlineData("revoked-run", false)]
    [InlineData("revoked-run", true)]
    [InlineData("revoked-publish", false)]
    [InlineData("revoked-publish", true)]
    [InlineData("disabled-dashboard", false)]
    [InlineData("disabled-dashboard", true)]
    [InlineData("deleted-dashboard", false)]
    [InlineData("deleted-dashboard", true)]
    public async Task Anonymous_refresh_fails_closed_after_authority_or_resource_changes(string change, bool playlist)
    {
        var (fixture, link) = await CreateAsync(); await using var f = fixture;
        if (playlist) link = await CreatePlaylistLinkAsync(f);
        var copied = Assert.Single((await (await f.SendAsync(HttpMethod.Get, playlist ? "/api/v1/studio/playlists/screen-playlist/screens" : "/api/v1/studio/dashboards/public-dashboard/screens")).Content.ReadFromJsonAsync<JsonObject>())!["data"]!.AsArray())!.AsObject();
        await using var scope = f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StudioDatabase>();
        if (change == "expired") await db.Client.Updateable<StudioScreenLink>().SetColumns(x => new StudioScreenLink { NeverExpires = false, ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(-1) }).Where(x => x.TenantNId == "tenant-a").ExecuteCommandAsync();
        if (change == "disabled-publisher") await db.Client.Updateable<StudioLocalAdmin>().SetColumns(x => new StudioLocalAdmin { Enabled = false }).Where(x => x.TenantNId == "tenant-a").ExecuteCommandAsync();
        if (change == "changed-auth") await db.Client.Updateable<StudioLocalAdmin>().SetColumns(x => new StudioLocalAdmin { AuthVersion = 2 }).Where(x => x.TenantNId == "tenant-a").ExecuteCommandAsync();
        if (change.StartsWith("revoked-", StringComparison.Ordinal)) {
            var capability = change == "revoked-run" ? "Run" : "Publish";
            await db.Client.Updateable<StudioGrant>().SetColumns(x => new StudioGrant { RevokedOn = DateTimeOffset.UtcNow }).Where(x => x.TenantNId == "tenant-a" && x.Capability == capability).ExecuteCommandAsync();
        }
        if (change == "disabled-dashboard") await db.Client.Updateable<StudioResource>().SetColumns(x => new StudioResource { IsEnabled = false }).Where(x => x.NId == "public-dashboard").ExecuteCommandAsync();
        if (change == "deleted-dashboard") {
            var resource = await db.Client.Queryable<StudioResource>().Where(x => x.NId == "public-dashboard").FirstAsync();
            resource.MarkDeleted();
            await db.Client.Updateable(resource).ExecuteCommandAsync();
        }
        Assert.False((await PublicAsync(f, link, suffix: playlist ? "?playlistItemId=one" : "")).IsSuccessStatusCode);
        Assert.False((await PublicAsync(f, copied, suffix: playlist ? "?playlistItemId=one" : "")).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Links_cannot_be_created_for_foreign_resource_revision_or_outside_lifetime_budget()
    {
        var (fixture, link) = await CreateAsync(); await using var f = fixture;
        foreach (var hours in new[] { 0, 2161 }) Assert.Equal(HttpStatusCode.BadRequest, (await f.SendAsync(HttpMethod.Post, "/api/v1/studio/dashboards/public-dashboard/screens", new { revisionNId = link["revisionNId"]!.GetValue<string>(), parameters = new { lineId = "L01" }, lifetimeHours = hours })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.SendAsync(HttpMethod.Post, "/api/v1/studio/dashboards/public-dashboard/screens", new { revisionNId = "foreign", parameters = new { lineId = "L01" }, lifetimeHours = 24 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.SendAsync(HttpMethod.Delete, $"/api/v1/studio/dashboards/foreign/screens/{link["id"]!.GetValue<string>()}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PublicAsync(f, link)).StatusCode);
    }
}
