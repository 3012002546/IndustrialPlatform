using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Data;
using IndustrialPlatform.PlatformStudio.Application.Report;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class QueryOutputAuthorizationTests
{
    [Fact]
    public async Task Temporary_output_rechecks_permission_session_resource_and_pinned_revision()
    {
        await using var f = await StudioTestContext.CreateAsync();
        f.Profile.InstalledModules = ["LowCode", "Report"];
        var doc = ReportPlanningTests.Report(new JsonArray(new JsonObject { ["id"] = "one" }));
        doc["mode"] = "Query";
        var table = doc["bands"]![0]!["controls"]![0]!.AsObject();
        table["type"] = "Table"; table.Remove("value");
        table["table"] = JsonNode.Parse("""{"columns":[{"id":"id","label":"ID","widthMm":50,"value":{"from":"Row","path":["id"],"nullPolicy":"Error"}}]}""");
        doc["query"] = JsonNode.Parse("""{"instanceId":"root","resultBandId":"root-band","filters":[],"columns":[{"controlId":"value","columnId":"id","visible":true,"sortable":true}],"defaultSort":[],"defaultPageSize":20}""");
        var resource = await f.Publishing.CreateAsync(f.Principal, new("output", "Report", "Query output", null, null, doc), "create", default);
        Assert.Equal("Query", resource.PageMode);
        var validation = await f.Publishing.ValidateAsync(f.Principal, resource.NId, doc, default);
        var revision = await f.Publishing.PublishAsync(f.Principal, resource.NId, new(resource.OptimisticVersion, resource.ConcurrencyVersion, "v1", true, validation.DependencyPlanHash), "publish", default);
        Assert.Equal("Query", Assert.Single((await f.Publishing.ListAsync(f.Principal, "Report", new(), default)).Items).PageMode);
        var draft = await f.Publishing.GetDraftAsync(f.Principal, resource.NId, default);
        var fixedDraft = doc.DeepClone().AsObject();
        fixedDraft["mode"] = "Fixed"; fixedDraft.Remove("query");
        await f.Publishing.SaveDraftAsync(f.Principal, resource.NId, new(draft["optimisticVersion"]!.GetValue<string>(), draft["concurrencyVersion"]!.GetValue<Guid>(), resource.Name, null, null, fixedDraft), default);
        Assert.Equal("Query", Assert.Single((await f.Publishing.ListAsync(f.Principal, "Report", new(), default)).Items).PageMode);
        var design = new ReportDesign(f.Store, f.Authorization, f.Publishing, new DatasetExecution(f.Store, f.Publishing, f.Authorization, f.SourceExecutor, new(), TimeProvider.System));
        var output = JsonSerializer.SerializeToNode(await design.AuthorizeQueryOutputAsync(f.Principal, resource.NId, revision.NId, default))!;
        Assert.True(output["allowed"]!.GetValue<bool>());
        Assert.Equal(revision.NId, output["revisionNId"]!.GetValue<string>());
        var noExport = f.Principal with { Permissions = f.Principal.Permissions.Where(p => p != "studio.report.export").ToArray() };
        var runtime = JsonSerializer.SerializeToNode(await design.RuntimeAsync(noExport, resource.NId, revision.NId, default))!;
        Assert.False(runtime["canExport"]!.GetValue<bool>());
        Assert.Equal("STUDIO_FORBIDDEN", (await Assert.ThrowsAsync<StudioException>(() => design.AuthorizeQueryOutputAsync(noExport, resource.NId, revision.NId, default))).Code);
        Assert.Equal("STUDIO_UNAUTHENTICATED", (await Assert.ThrowsAsync<StudioException>(() => design.AuthorizeQueryOutputAsync(f.Principal with { ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(-1) }, resource.NId, revision.NId, default))).Code);
        await Assert.ThrowsAsync<StudioException>(() => design.AuthorizeQueryOutputAsync(f.Principal, resource.NId, "missing-revision", default));
        await Assert.ThrowsAsync<StudioException>(() => design.AuthorizeQueryOutputAsync(f.Principal with { SubjectNId = "other-user" }, resource.NId, revision.NId, default));
        var stored = (await f.Store.FindResourceAsync(f.Principal.TenantNId, resource.NId, default))!;
        var grants = await f.Store.GetGrantsAsync(f.Principal.TenantNId, stored.Id, default);
        await f.Store.ReplaceGrantsAsync(f.Principal.TenantNId, stored.Id, grants.Where(g => g.Capability != "Export").ToArray(), default);
        await Assert.ThrowsAsync<StudioException>(() => design.AuthorizeQueryOutputAsync(f.Principal, resource.NId, revision.NId, default));
    }
}
