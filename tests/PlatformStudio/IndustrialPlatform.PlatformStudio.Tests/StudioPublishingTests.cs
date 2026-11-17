using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Contracts;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class StudioPublishingTests
{
    [Fact]
    public async Task Revision_history_reads_only_metadata_and_keeps_order_and_access_checks()
    {
        await using var fixture = await StudioTestContext.CreateAsync();
        var created = await fixture.Publishing.CreateAsync(fixture.Principal,
            new("history", "LedgerModel", "历史", null, null, Model()), "history-create", default);
        var resource = await fixture.Store.FindResourceAsync(fixture.Principal.TenantNId, created.NId, default);
        var now = DateTimeOffset.UtcNow;
        for (var i = 1; i <= 3; i++)
            await fixture.Store.InsertRevisionAsync(new StudioRevision
            {
                NId = "history-" + i, TenantNId = fixture.Principal.TenantNId, ResourceId = resource!.Id,
                RevisionNo = i, CreatedOn = now, PublishedBy = "publisher", ContentHash = "hash-" + i,
                ReleaseNote = "说明-" + i, DocumentJson = new string('x', 1_000_000),
                DependenciesJson = new string('y', 1_000_000), SourcePinsJson = new string('z', 1_000_000)
            }, default);
        var queries = new List<string>();
        fixture.Database.Client.Aop.OnLogExecuting = (sql, _) => queries.Add(sql);
        var history = await fixture.Publishing.RevisionsAsync(fixture.Principal, created.NId, default);
        Assert.Equal(["3", "2", "1"], history.Select(row => row.RevisionNo));
        Assert.Equal("hash-3", history[0].ContentHash);
        Assert.Equal("说明-3", history[0].ReleaseNote);
        Assert.Equal("publisher", history[0].PublishedBy);
        Assert.Equal(now.ToUnixTimeMilliseconds(), history[0].CreatedOn.ToUnixTimeMilliseconds());
        var sql = Assert.Single(queries, sql => sql.Contains("studio_revision", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("document_json", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dependencies_json", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("source_pins_json", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Store.ListRevisionSummariesAsync("other", resource!.Id, default));
        Assert.Equal(404, (await Assert.ThrowsAsync<StudioException>(() => fixture.Publishing.RevisionsAsync(
            fixture.Principal with { SubjectNId = "outsider" }, created.NId, default))).StatusCode);
    }
    [Fact]
    public async Task Resource_list_batches_published_revisions_and_keeps_visibility_and_draft_flags()
    {
        await using var fixture = await StudioTestContext.CreateAsync();
        for (var i = 0; i < 12; i++)
        {
            var created = await fixture.Publishing.CreateAsync(fixture.Principal, new("batch-" + i, "LedgerModel", "List " + i, null, null, Model()), "create-" + i, default);
            var validation = await fixture.Publishing.ValidateAsync(fixture.Principal, created.NId, Model(), default);
            await fixture.Publishing.PublishAsync(fixture.Principal, created.NId, new(created.OptimisticVersion, created.ConcurrencyVersion, "v1", true, validation.DependencyPlanHash), "publish-" + i, default);
        }
        var draft = await fixture.Publishing.GetDraftAsync(fixture.Principal, "batch-0", default);
        await fixture.Publishing.SaveDraftAsync(fixture.Principal, "batch-0", new(draft["optimisticVersion"]!.GetValue<string>(), draft["concurrencyVersion"]!.GetValue<Guid>(), "List 0", null, null, Model("Changed")), default);
        var queries = new List<string>();
        fixture.Database.Client.Aop.OnLogExecuting = (sql, _) => queries.Add(sql);
        var list = await fixture.Publishing.ListAsync(fixture.Principal, "LedgerModel", new() { PageSize = 200 }, default);
        Assert.Equal(12, list.Items.Count); Assert.Equal("12", list.TotalCount);
        Assert.All(list.Items, item => Assert.Equal("1", item.PublishedRevisionNo));
        Assert.Single(list.Items, item => item.HasDraftChanges);
        Assert.True(list.Items.Single(item => item.NId == "batch-0").HasDraftChanges);
        Assert.Equal(3, queries.Count); // Resource page, one revision batch, count; independent of page length.
        Assert.Empty((await fixture.Publishing.ListAsync(fixture.Principal with { SubjectNId = "outsider" }, "LedgerModel", new(), default)).Items);
        Assert.Empty((await fixture.Publishing.ListAsync(fixture.Principal with { TenantNId = "other" }, "LedgerModel", new(), default)).Items);
        var revision = await fixture.Store.FindResourceAsync(fixture.Principal.TenantNId, "batch-0", default);
        Assert.Empty(await fixture.Store.FindRevisionsByIdsAsync("other", [revision!.PublishedRevisionId!.Value], default));
    }
    internal static JsonObject Model(string label = "名称") => new()
    {
        ["schemaVersion"] = 1,
        ["fields"] = new JsonArray(new JsonObject { ["id"] = "name", ["name"] = "name", ["label"] = label, ["type"] = "String", ["nullable"] = true }),
        ["numbering"] = new JsonObject { ["mode"] = "Manual" }, ["displayFieldId"] = "name",
    };
    [Fact]
    public async Task Create_idempotency_and_two_browser_draft_CAS_preserve_the_first_edit()
    {
        await using var fixture = await StudioTestContext.CreateAsync();
        var request = new ResourceCreate("ledger-a", "LedgerModel", "台账", null, null, Model());
        var first = await fixture.Publishing.CreateAsync(fixture.Principal, request, "create-1", CancellationToken.None);
        var replay = await fixture.Publishing.CreateAsync(fixture.Principal, request, "create-1", CancellationToken.None);
        Assert.Equal(first, replay);
        var edited = await fixture.Publishing.SaveDraftAsync(fixture.Principal, "ledger-a", new(first.OptimisticVersion, first.ConcurrencyVersion, "首个编辑", null, null, Model("新名称")), CancellationToken.None);
        var error = await Assert.ThrowsAsync<StudioException>(() => fixture.Publishing.SaveDraftAsync(fixture.Principal, "ledger-a", new(first.OptimisticVersion, first.ConcurrencyVersion, "迟到覆盖", null, null, Model()), CancellationToken.None));
        Assert.Equal("STUDIO_VERSION_CONFLICT", error.Code);
        Assert.Equal("首个编辑", (await fixture.Publishing.GetDraftAsync(fixture.Principal, "ledger-a", CancellationToken.None))["name"]!.GetValue<string>());
        Assert.Equal("1", edited["optimisticVersion"]!.GetValue<string>());
        var otherTenant = fixture.Principal with { TenantNId = "tenant-b" };
        Assert.Equal(404, (await Assert.ThrowsAsync<StudioException>(() => fixture.Publishing.GetDraftAsync(otherTenant, "ledger-a", CancellationToken.None))).StatusCode);
    }
    [Fact]
    public async Task Publishing_freezes_revision_and_draft_edits_do_not_pollute_running_revision()
    {
        await using var fixture = await StudioTestContext.CreateAsync();
        var created = await fixture.Publishing.CreateAsync(fixture.Principal, new("ledger-a", "LedgerModel", "台账", null, null, Model()), "create-1", CancellationToken.None);
        var plan = await fixture.Publishing.ValidateAsync(fixture.Principal, created.NId, Model(), CancellationToken.None);
        Assert.True(plan.Valid);
        var published = await fixture.Publishing.PublishAsync(fixture.Principal, created.NId, new(created.OptimisticVersion, created.ConcurrencyVersion, "首版", true, plan.DependencyPlanHash), "publish-1", CancellationToken.None);
        var draft = await fixture.Publishing.GetDraftAsync(fixture.Principal, created.NId, CancellationToken.None);
        await fixture.Publishing.SaveDraftAsync(fixture.Principal, created.NId, new(draft["optimisticVersion"]!.GetValue<string>(), draft["concurrencyVersion"]!.GetValue<Guid>(), "台账", null, null, Model("草稿名称")), CancellationToken.None);
        var runtime = await fixture.Publishing.RequireRuntimeAsync(fixture.Principal, created.NId, published.NId, CancellationToken.None);
        Assert.Equal("名称", StudioPublishing.Parse(runtime.Revision.DocumentJson)["fields"]![0]!["label"]!.GetValue<string>());
        Assert.Equal(published.ContentHash, runtime.Revision.ContentHash);
        var changedKey = await Assert.ThrowsAsync<StudioException>(() => fixture.Publishing.CreateAsync(fixture.Principal, new("ledger-b", "LedgerModel", "别的台账", null, null, Model()), "create-1", CancellationToken.None));
        Assert.Equal("STUDIO_IDEMPOTENCY_CONFLICT", changedKey.Code);
    }
    [Fact]
    public async Task Functional_permission_and_resource_grant_are_both_required()
    {
        await using var fixture = await StudioTestContext.CreateAsync();
        var created = await fixture.Publishing.CreateAsync(fixture.Principal, new("ledger-a", "LedgerModel", "台账", null, null, Model()), "create-1", CancellationToken.None);
        var outsider = fixture.Principal with { SubjectNId = "user-b" };
        Assert.Equal(404, (await Assert.ThrowsAsync<StudioException>(() => fixture.Publishing.GetDraftAsync(outsider, created.NId, CancellationToken.None))).StatusCode);
        var ownerWithoutFunction = fixture.Principal with { Permissions = [] };
        Assert.Equal(403, (await Assert.ThrowsAsync<StudioException>(() => fixture.Publishing.GetDraftAsync(ownerWithoutFunction, created.NId, CancellationToken.None))).StatusCode);
        var visible = await fixture.Publishing.ListAsync(outsider, "LedgerModel", new(), CancellationToken.None);
        Assert.Empty(visible.Items);
        Assert.Equal("0", visible.TotalCount);
    }
    [Fact]
    public async Task Revision_detail_requires_design_and_cannot_cross_resource_or_tenant()
    {
        await using var f=await StudioTestContext.CreateAsync();
        var created=await f.Publishing.CreateAsync(f.Principal,new("m","LedgerModel","模型",null,null,Model()),"create",CancellationToken.None);
        var plan=await f.Publishing.ValidateAsync(f.Principal,"m",Model(),CancellationToken.None);
        var revision=await f.Publishing.PublishAsync(f.Principal,"m",new(created.OptimisticVersion,created.ConcurrencyVersion,"v1",true,plan.DependencyPlanHash),"publish",CancellationToken.None);
        Assert.NotNull((await f.Publishing.GetRevisionDesignAsync(f.Principal,"m",revision.NId,CancellationToken.None)).Document);
        var viewer=f.Principal with {Permissions=f.Principal.Permissions.Where(p=>p!="studio.lowcode.design").ToArray()};
        Assert.Equal(403,(await Assert.ThrowsAsync<StudioException>(()=>f.Publishing.GetRevisionDesignAsync(viewer,"m",revision.NId,CancellationToken.None))).StatusCode);
        Assert.Equal(404,(await Assert.ThrowsAsync<StudioException>(()=>f.Publishing.GetRevisionDesignAsync(f.Principal with {TenantNId="other"},"m",revision.NId,CancellationToken.None))).StatusCode);
        await f.Publishing.CreateAsync(f.Principal,new("other","LedgerModel","其他",null,null,Model()),"other",CancellationToken.None);
        Assert.Equal(404,(await Assert.ThrowsAsync<StudioException>(()=>f.Publishing.GetRevisionDesignAsync(f.Principal,"other",revision.NId,CancellationToken.None))).StatusCode);
    }
}
