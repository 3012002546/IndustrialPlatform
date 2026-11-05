using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Data;
using IndustrialPlatform.PlatformStudio.Application.Trace;
using IndustrialPlatform.PlatformStudio.Contracts;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class TraceRuntimeTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    [Fact]
    public void Full_identity_uses_unambiguous_tuple_and_all_scope_dimensions()
    {
        var key = TraceRuntime.StableKey("t", "mes", "F1", "lot", "A");
        Assert.NotEqual(key, TraceRuntime.StableKey("t", "mes", "F2", "lot", "A"));
        Assert.NotEqual(key, TraceRuntime.StableKey("t2", "mes", "F1", "lot", "A"));
        Assert.NotEqual(key, TraceRuntime.StableKey("t", "mes2", "F1", "lot", "A"));
        Assert.NotEqual(key, TraceRuntime.StableKey("t", "mes", "F1", "serial", "A"));
        Assert.NotEqual(TraceRuntime.StableKey("a|b", "c"), TraceRuntime.StableKey("a", "b|c"));
    }
    [Fact]
    public void Credentials_expire_are_scope_bound_and_reject_tampering_or_revoked_fingerprint()
    {
        var clock = new TestClock(); var service = new TraceCredentials(clock); var p = Principal(clock);
        var token = service.Issue(p, "r", "v", "Start", new() { ["rowKey"] = "A" });
        Assert.Equal("A", StudioJson.Text(service.Read(p, "r", "v", "Start", token)["rowKey"]));
        foreach (var bad in new[] { token + "x", token[..^8], "invented" }) Assert.Throws<StudioException>(() => service.Read(p, "r", "v", "Start", bad));
        Assert.Throws<StudioException>(() => service.Read(p, "r", "v", "Node", token));
        Assert.Throws<StudioException>(() => service.Read(p with { FactoryNIds = ["F1"] }, "r", "v", "Start", token));
        Assert.Throws<StudioException>(() => service.Read(p, "r", "other", "Start", token));
        clock.Now = clock.Now.AddMinutes(5);
        Assert.Equal("TRACE_CREDENTIAL_EXPIRED", Assert.Throws<StudioException>(() => service.Read(p, "r", "v", "Start", token)).Code);
    }
    [Fact]
    public void Layout_null_is_repairable_diagnostic_but_missing_geometry_is_structural_error()
    {
        var doc = Model(); Assert.Empty(StudioDocuments.Validate("TraceModel", doc));
        doc["layout"]!["edges"]![0]!["targetPort"] = null;
        Assert.Contains(StudioDocuments.Validate("TraceModel", doc), x => x.Path == "layout.edges[0].targetPort");
        doc["layout"]!["nodes"]!.AsArray().Add(new JsonObject { ["objectTypeId"] = "lot", ["x"] = 0, ["y"] = 0, ["width"] = 0, ["height"] = 80 });
        Assert.Throws<StudioException>(() => StudioDocuments.Validate("TraceModel", doc));
    }
    [Fact]
    public async Task Published_trace_preserves_multiparent_multievidence_cycles_and_cross_factory_identity()
    {
        await using var f = await Fixture.Create();
        var starts = await f.Runtime.StartsAsync(f.P, "trace", new() { ["revisionNId"] = f.Revision }, Ct);
        var keys = new JsonArray(starts["items"]!.AsArray().Where(x => StudioJson.Text(x!["identity"]!["businessKey"]) == "A").Select(x => x!["startKey"]!.DeepClone()).ToArray());
        var result = await f.Runtime.QueryAsync(f.P, "trace", new() { ["revisionNId"] = f.Revision, ["startKeys"] = keys, ["direction"] = "Both" }, Ct);
        Assert.Equal("Complete", StudioJson.Text(result["status"])); Assert.Equal(4, result["nodes"]!.AsArray().Count); Assert.Equal(4, result["edges"]!.AsArray().Count);
        Assert.Equal(4, result["edges"]!.AsArray().Select(e => StudioJson.Text(e!["key"])).Distinct().Count());
        Assert.All(result["sourceObservations"]!.AsArray(), x => { Assert.Equal("Unknown", StudioJson.Text(x!["quality"]!["status"])); Assert.Null(x["observedOn"]); });
        Assert.Contains(result["sourceObservations"]!.AsArray(), x => StudioJson.Text(x!["datasetRevisionNId"]) == StudioJson.Text(f.Document["objects"]![0]!["detail"]!["datasetRevisionNId"]));
        var node = result["nodes"]!.AsArray().Single(x => StudioJson.Text(x!["identity"]!["factory"]) == "F2")!;
        var detail = await f.Runtime.NodeDetailAsync(f.P, "trace", NodeInput(f.Revision, node), Ct);
        Assert.Equal("F2-A", StudioJson.Text(detail["fields"]![0]!["value"]));
        var forged = NodeInput(f.Revision, node); forged["nodeKey"] = "invented";
        Assert.Equal(403, (await Assert.ThrowsAsync<StudioException>(() => f.Runtime.NodeDetailAsync(f.P, "trace", forged, Ct))).StatusCode);
    }
    [Fact]
    public async Task Upstream_keeps_fact_arrow_and_expansion_does_not_duplicate_evidence()
    {
        await using var f = await Fixture.Create(); var result = await f.Query("C", "Upstream");
        var edge = result["edges"]!.AsArray().Single(x => StudioJson.Text(x!["evidence"]!["recordKey"]) == "M1")!;
        Assert.Equal(TraceRuntime.StableKey("tenant-a", "MES", "F1", "lot", "A"), StudioJson.Text(edge["from"]));
        Assert.Equal(TraceRuntime.StableKey("tenant-a", "MES", "F1", "lot", "C"), StudioJson.Text(edge["to"]));
        var input = NodeInput(f.Revision, result["nodes"]![0]!); input["direction"] = "Both";
        var once = await f.Runtime.ExpandAsync(f.P, "trace", input, Ct); var twice = await f.Runtime.ExpandAsync(f.P, "trace", input, Ct);
        Assert.Equal(once["edges"]!.AsArray().Select(x => StudioJson.Text(x!["key"])).Order(), twice["edges"]!.AsArray().Select(x => StudioJson.Text(x!["key"])).Order());
    }
    [Fact]
    public async Task Budget_returns_partial_with_signed_cursor_and_current_authorization_is_rechecked()
    {
        await using var f = await Fixture.Create(maxNodes: 2); var result = await f.Query("A", "Both");
        Assert.Equal("Partial", StudioJson.Text(result["status"])); Assert.Equal(2, result["nodes"]!.AsArray().Count); Assert.NotNull(result["cursor"]);
        var input = NodeInput(f.Revision, result["nodes"]![0]!); input["direction"] = "Both"; input["cursor"] = result["cursor"]!.DeepClone();
        var next = await f.Runtime.ExpandAsync(f.P, "trace", input, Ct); Assert.NotEmpty(next["nodes"]!.AsArray()); Assert.Equal("Complete", StudioJson.Text(next["status"])); Assert.Null(next["cursor"]);
        f.Identity.Current = f.P with { AuthVersion = "revoked" };
        Assert.Equal(403, (await Assert.ThrowsAsync<StudioException>(() => f.Runtime.NodeDetailAsync(f.P, "trace", input, Ct))).StatusCode);
    }
    [Fact]
    public async Task Removed_source_row_and_revoked_dataset_grant_reject_previously_signed_nodes()
    {
        await using var f = await Fixture.Create(); var result = await f.Query("A", "Both"); var input = NodeInput(f.Revision, result["nodes"]![0]!);
        f.Executor.HideDetails = true;
        Assert.Equal(403, (await Assert.ThrowsAsync<StudioException>(() => f.Runtime.NodeDetailAsync(f.P, "trace", input, Ct))).StatusCode);
        f.Executor.HideDetails = false;
        var dataset = await f.Context.Store.FindResourceAsync(f.P.TenantNId, "detail", Ct);
        await f.Context.Store.ReplaceGrantsAsync(f.P.TenantNId, dataset!.Id, [], Ct);
        await Assert.ThrowsAsync<StudioException>(() => f.Runtime.NodeDetailAsync(f.P, "trace", input, Ct));
    }
    [Fact]
    public async Task Preview_credentials_cannot_run_and_timeout_is_not_empty_complete()
    {
        await using var f = await Fixture.Create();
        var preview = await f.Runtime.PreviewStartAsync(f.P, new() { ["document"] = f.Document.DeepClone() }, Ct);
        await Assert.ThrowsAsync<StudioException>(() => f.Runtime.QueryAsync(f.P, "trace", new() { ["revisionNId"] = f.Revision, ["startKeys"] = new JsonArray(preview["items"]![0]!["startKey"]!.DeepClone()), ["direction"] = "Both" }, Ct));
        f.Executor.TimeoutRelations = true;
        var partial = await f.Query("A", "Both"); Assert.Equal("Partial", StudioJson.Text(partial["status"])); Assert.Contains(partial["partialReasons"]!.AsArray(), x => StudioJson.Text(x) == "SOURCE_TIMEOUT");
    }
    [Fact]
    public async Task Publication_rejects_inaccessible_field_parameter_and_unregistered_detail_link()
    {
        await using var f = await Fixture.Create(); var doc = f.Document.DeepClone().AsObject();
        doc["start"]!["identityMap"]!["businessKey"]!["path"] = new JsonArray("not-readable");
        var plan = await f.Context.Publishing.ValidateAsync(f.P, "trace", doc, Ct);
        Assert.False(plan.Valid); Assert.Contains(plan.Diagnostics, x => x.Code == "TRACE_MAPPING_INVALID" && x.Path.Contains("identityMap", StringComparison.Ordinal));
        doc = f.Document.DeepClone().AsObject();
        doc["objects"]![0]!["detail"]!["links"]!.AsArray().Add(new JsonObject { ["id"] = "file", ["label"] = "附件", ["kind"] = "File", ["openMode"] = "Modal", ["value"] = new JsonObject { ["from"] = "Row", ["path"] = new JsonArray("code"), ["nullPolicy"] = "Error" } });
        plan = await f.Context.Publishing.ValidateAsync(f.P, "trace", doc, Ct);
        Assert.False(plan.Valid); Assert.Contains(plan.Diagnostics, x => x.Code == "STUDIO_FILES_UNAVAILABLE");
        doc["objects"]![0]!["detail"]!["links"]![0]!["kind"] = "External"; doc["objects"]![0]!["detail"]!["links"]![0]!["allowedOriginKey"] = "unregistered";
        plan = await f.Context.Publishing.ValidateAsync(f.P, "trace", doc, Ct);
        Assert.Contains(plan.Diagnostics, x => x.Path.EndsWith("allowedOriginKey", StringComparison.Ordinal));
    }
    [Theory]
    [InlineData(1, 3000, "MAX_DEPTH")]
    [InlineData(12, 1, "MAX_EDGES")]
    public async Task Depth_and_edge_limits_return_explicit_partial(int depth, int edges, string reason)
    {
        await using var f = await Fixture.Create(maxDepth: depth, maxEdges: edges); var result = await f.Query("A", "Both");
        Assert.Equal("Partial", StudioJson.Text(result["status"])); Assert.Contains(result["partialReasons"]!.AsArray(), x => StudioJson.Text(x) == reason);
        Assert.Contains(result["nodes"]!.AsArray(), x => x!["boundary"]?.GetValue<bool>() == true);
    }
    [Fact]
    public async Task Runtime_contract_allows_run_only_without_exposing_design_bindings()
    {
        await using var f = await Fixture.Create(); var viewer = f.P with { Permissions = f.P.Permissions.Where(x => !x.EndsWith(".design", StringComparison.Ordinal)).ToArray() }; f.Identity.Current = viewer;
        var contract = await f.Runtime.ContractAsync(viewer, "trace", f.Revision, Ct);
        Assert.Equal(f.Revision, StudioJson.Text(contract["revisionNId"]));
        Assert.Null(contract["objects"]); Assert.Null(contract["sourcePins"]); Assert.Null(contract["start"]!["datasetRevisionNId"]); Assert.Null(contract["start"]!["identityMap"]);
        Assert.Single(contract["relations"]!.AsArray()); Assert.Null(contract["relations"]![0]!["forward"]); Assert.Null(contract["relations"]![0]!["reverse"]);
        Assert.Equal("LeftToRight", StudioJson.Text(contract["presentation"]!["layoutDirection"]));
    }
    [Fact]
    public async Task Configured_sections_are_lazy_scoped_to_authoritative_node_and_use_real_source_pagination()
    {
        await using var f = await Fixture.Create(sections: true);
        Assert.Equal("TopToBottom", StudioJson.Text((await f.Runtime.ContractAsync(f.P, "trace", f.Revision, Ct))["presentation"]!["layoutDirection"]));
        var result = await f.Query("A", "Downstream");
        var node = result["nodes"]!.AsArray().Single(n => StudioJson.Text(n!["identity"]!["factory"]) == "F1" && StudioJson.Text(n["identity"]!["businessKey"]) == "A")!;
        var input = NodeInput(f.Revision, node);
        var detail = await f.Runtime.NodeDetailAsync(f.P, "trace", input, Ct);
        Assert.Equal(2, detail["sections"]!.AsArray().Count);
        Assert.Null(detail["sections"]![0]!["datasetRevisionNId"]);
        Assert.Equal(0, f.Executor.SectionCalls);
        input["sectionId"] = "checks"; input["page"] = 2; input["pageSize"] = 2;
        // Browser-provided association values and dataset identifiers are deliberately ignored.
        input["parameters"] = new JsonObject { ["factory"] = "F2", ["id"] = "B" }; input["datasetRevisionNId"] = "untrusted";
        var section = await f.Runtime.NodeSectionAsync(f.P, "trace", input, Ct);
        Assert.Equal("5", StudioJson.Text(section["totalCount"]));
        Assert.Equal(2, section["page"]!.GetValue<int>()); Assert.Equal(2, section["items"]!.AsArray().Count);
        Assert.Equal("A-3", StudioJson.Text(section["items"]![0]!["key"]));
        Assert.Equal("F1", StudioJson.Text(f.Executor.LastSectionArguments!["factory"]));
        Assert.Equal("A", StudioJson.Text(f.Executor.LastSectionArguments["id"]));
        Assert.Equal("MAT-A", StudioJson.Text(f.Executor.LastSectionArguments["materialCode"]));
        Assert.Equal("检验项3", StudioJson.Text(section["items"]![0]!["values"]!["checkLabel"]));
        Assert.Null(section["items"]![0]!["values"]!["unconfiguredSecret"]);
        input["sectionId"] = "basic"; input["page"] = 1;
        var fields = await f.Runtime.NodeSectionAsync(f.P, "trace", input, Ct);
        Assert.Equal("Fields", StudioJson.Text(fields["kind"])); Assert.Equal("F1-A", StudioJson.Text(fields["fields"]![0]!["value"]));
        Assert.Equal(1, f.Executor.SectionCalls);
    }
    [Fact]
    public async Task Sections_reject_missing_context_duplicate_keys_invalid_paging_and_revoked_identity()
    {
        await using var f = await Fixture.Create(sections: true); var result = await f.Query("A", "Both");
        var input = NodeInput(f.Revision, result["nodes"]![0]!); input["sectionId"] = "checks";
        f.Executor.EmptyAssociation = true;
        Assert.Equal("TRACE_SECTION_CONTEXT_REQUIRED", (await Assert.ThrowsAsync<StudioException>(() => f.Runtime.NodeSectionAsync(f.P, "trace", input, Ct))).Code);
        Assert.Equal(0, f.Executor.SectionCalls); f.Executor.EmptyAssociation = false;
        foreach (var invalid in new JsonNode?[] { JsonValue.Create(0), JsonValue.Create(-1), JsonValue.Create("1"), JsonValue.Create(1.5), null })
        {
            input["page"] = invalid;
            Assert.Equal(422, (await Assert.ThrowsAsync<StudioException>(() => f.Runtime.NodeSectionAsync(f.P, "trace", input, Ct))).StatusCode);
        }
        input.Remove("page"); input["pageSize"] = 201;
        await Assert.ThrowsAsync<StudioException>(() => f.Runtime.NodeSectionAsync(f.P, "trace", input, Ct)); input["pageSize"] = 2;
        f.Executor.DuplicateSectionKeys = true;
        Assert.Equal("STUDIO_ROW_KEY_AMBIGUOUS", (await Assert.ThrowsAsync<StudioException>(() => f.Runtime.NodeSectionAsync(f.P, "trace", input, Ct))).Code);
        f.Executor.DuplicateSectionKeys = false; f.Executor.SectionExecuted = () => f.Identity.Current = f.P with { AuthVersion = "revoked" };
        Assert.Equal(403, (await Assert.ThrowsAsync<StudioException>(() => f.Runtime.NodeSectionAsync(f.P, "trace", input, Ct))).StatusCode);
    }
    [Fact]
    public async Task Sections_cannot_reuse_forged_or_other_factory_nodes_or_revoked_dataset_grants()
    {
        await using var f = await Fixture.Create(sections: true); var result = await f.Query("A", "Both");
        var input = NodeInput(f.Revision, result["nodes"]![0]!); input["sectionId"] = "checks";
        var forged = input.DeepClone().AsObject(); forged["nodeKey"] = "another-node";
        Assert.Equal(403, (await Assert.ThrowsAsync<StudioException>(() => f.Runtime.NodeSectionAsync(f.P, "trace", forged, Ct))).StatusCode);
        forged = input.DeepClone().AsObject(); forged["sectionId"] = "unconfigured";
        Assert.Equal(404, (await Assert.ThrowsAsync<StudioException>(() => f.Runtime.NodeSectionAsync(f.P, "trace", forged, Ct))).StatusCode);
        f.Identity.Current = f.P with { FactoryNIds = ["F2"] };
        Assert.Equal(403, (await Assert.ThrowsAsync<StudioException>(() => f.Runtime.NodeSectionAsync(f.P, "trace", input, Ct))).StatusCode);
        f.Identity.Current = f.P;
        var dataset = (await f.Context.Store.FindResourceAsync(f.P.TenantNId, "checks", Ct))!;
        await f.Context.Store.ReplaceGrantsAsync(f.P.TenantNId, dataset.Id, [], Ct);
        Assert.Equal(404, (await Assert.ThrowsAsync<StudioException>(() => f.Runtime.NodeSectionAsync(f.P, "trace", input, Ct))).StatusCode);
        Assert.Equal(0, f.Executor.SectionCalls);
    }
    [Fact]
    public async Task Publication_validates_section_own_fields_association_types_pagination_and_ids()
    {
        await using var f = await Fixture.Create(sections: true);
        async Task InvalidSection(Action<JsonObject> mutate, string path)
        {
            var doc = f.Document.DeepClone().AsObject(); mutate(doc["objects"]![0]!["detail"]!["sections"]![0]!.AsObject());
            var plan = await f.Context.Publishing.ValidateAsync(f.P, "trace", doc, Ct);
            Assert.False(plan.Valid); Assert.Contains(plan.Diagnostics, d => d.Path.Contains(path, StringComparison.Ordinal));
        }
        await InvalidSection(s => s["columns"]![0]!["fieldId"] = "code", "columns");
        await InvalidSection(s => s["parameterMap"]![0]!["source"]!["path"] = new JsonArray("not-readable"), "parameterMap");
        await InvalidSection(s => s["parameterMap"]![0]!["source"] = new JsonObject { ["from"] = "Constant", ["value"] = 123, ["nullPolicy"] = "Error" }, "parameterMap");
        await InvalidSection(s => s["parameterMap"]!.AsArray().RemoveAt(0), "parameterMap");
        await InvalidSection(s => s["parameterMap"] = new JsonArray(), "parameterMap");
        await InvalidSection(s => s["parameterMap"]![0]!["source"]!["nullPolicy"] = "Null", "parameterMap");
        await InvalidSection(s => s["collectionPath"] = new JsonArray("missing"), "collectionPath");
        await InvalidSection(s => s["rowKey"]!["from"] = "Node", "rowKey");
        await InvalidSection(s => s["datasetRevisionNId"] = f.Document["objects"]![0]!["detail"]!["datasetRevisionNId"]!.DeepClone(), "datasetRevisionNId");
        await InvalidSection(s => s["id"] = "basic", "id");
        var invalidDirection = f.Document.DeepClone().AsObject(); invalidDirection["presentation"]!["layoutDirection"] = "Sideways";
        Assert.Contains(StudioDocuments.Validate("TraceModel", invalidDirection), d => d.Path == "presentation.layoutDirection");
    }
    private static JsonObject NodeInput(string revision, JsonNode node) => new() { ["revisionNId"] = revision, ["nodeKey"] = node["key"]!.DeepClone(), ["nodeToken"] = node["nodeToken"]!.DeepClone() };
    [Fact]
    public async Task Execution_catalog_uses_run_grants_without_design_or_view_and_hides_unavailable_models()
    {
        await using var f = await Fixture.Create();
        var p = f.P with { Permissions = ["studio.trace.run", "studio.dataset.run"] };
        f.Identity.Current = p;
        var resource = (await f.Context.Store.FindResourceAsync(p.TenantNId, "trace", Ct))!;
        var grants = await f.Context.Store.GetGrantsAsync(p.TenantNId, resource.Id, Ct);
        await f.Context.Store.ReplaceGrantsAsync(p.TenantNId, resource.Id, grants.Where(g => g.Capability == "Run").ToArray(), Ct);
        var catalog = await f.Runtime.ModelsAsync(p, Ct);
        Assert.Single(catalog["items"]!.AsArray());
        Assert.Equal("trace", StudioJson.Text(catalog["items"]![0]!["nId"]));
        Assert.Null(catalog["items"]![0]!["draftDocument"]);
        var contract = await f.Runtime.ContractAsync(p, "trace", null, Ct);
        Assert.Equal("1", StudioJson.Text(contract["revisionNo"]));
        Assert.Equal(12, contract["maxDepth"]!.GetValue<int>());

        var version = resource.OptimisticVersion; var token = resource.ConcurrencyVersion;
        resource.IsEnabled = false;
        Assert.True(await f.Context.Store.UpdateResourceAsync(resource, version, token, Ct));
        Assert.Empty((await f.Runtime.ModelsAsync(p, Ct))["items"]!.AsArray());
        resource.IsEnabled = true;
        Assert.True(await f.Context.Store.UpdateResourceAsync(resource, version, token, Ct));
        f.Identity.Current = p with { FactoryNIds = ["OTHER"] };
        Assert.Empty((await f.Runtime.ModelsAsync(p, Ct))["items"]!.AsArray());
        f.Identity.Current = p;
        var detail = (await f.Context.Store.FindResourceAsync(p.TenantNId, "detail", Ct))!;
        await f.Context.Store.ReplaceGrantsAsync(p.TenantNId, detail.Id, [], Ct);
        Assert.Empty((await f.Runtime.ModelsAsync(p, Ct))["items"]!.AsArray());
        f.Identity.Current = p with { Permissions = [] };
        Assert.Equal(403, (await Assert.ThrowsAsync<StudioException>(() => f.Runtime.ModelsAsync(p, Ct))).StatusCode);
    }
    private static StudioPrincipal Principal(TimeProvider clock) => new("tenant-a", "user-a", "test", "test", [], StudioKinds.Permissions, ["F1", "F2"], "1", "1", clock.GetUtcNow(), clock.GetUtcNow().AddHours(1));
    private sealed class TestClock : TimeProvider { public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    internal static JsonObject Model()
    {
        var doc = JsonNode.Parse("""
        {"schemaVersion":1,"parameters":[],"scope":{"factoryIds":["F1","F2"],"industryTags":[]},
         "start":{"objectTypeIds":["lot"],"datasetRevisionNId":"starts","collectionPath":[],"parameterMap":[],"searchFields":[],"columns":[{"fieldId":"code","label":"批号","visible":true}],"rowKey":{},"objectType":{"from":"Constant","value":"lot","nullPolicy":"Error"},"identityMap":{}},
         "objects":[{"id":"lot","name":"批次","identity":{"grain":"Batch"},"summary":[],"detail":{"datasetRevisionNId":"detail","parameterMap":[],"fields":[{"label":"批号","value":{},"format":"Text"}],"links":[]},"style":{"width":180,"height":80}}],
         "relations":[{"id":"consume","name":"消耗","fromTypeId":"lot","toTypeId":"lot","kind":"Genealogy","semantic":"Consume","forward":{"datasetRevisionNId":"forward","inputMap":[],"result":{"collectionPath":[],"targetObjectTypeId":"lot","targetIdentity":{},"summaryMap":[]}},"reverse":{"datasetRevisionNId":"reverse","inputMap":[],"result":{"collectionPath":[],"targetObjectTypeId":"lot","targetIdentity":{},"summaryMap":[]}},"identity":{},"label":{},"evidence":{}}],
         "layout":{"nodes":[],"edges":[{"relationId":"consume","sourcePort":"out","targetPort":"in","vertices":[]}]},"limits":{"maxDepth":12,"maxNodes":1000,"maxEdges":3000,"timeoutSeconds":30}}
        """)!.AsObject();
        static JsonObject Row(string key) => new() { ["from"] = "Row", ["path"] = new JsonArray(key), ["nullPolicy"] = "Error" };
        static JsonArray Maps() => new(new JsonObject { ["target"] = "id", ["source"] = new JsonObject { ["from"] = "Node", ["path"] = new JsonArray("identity", "businessKey"), ["nullPolicy"] = "Error" } }, new JsonObject { ["target"] = "factory", ["source"] = new JsonObject { ["from"] = "Node", ["path"] = new JsonArray("identity", "factory"), ["nullPolicy"] = "Error" } });
        var id = new JsonObject { ["sourceSystem"] = Row("sourceSystem"), ["factory"] = Row("factory"), ["businessKey"] = Row("businessKey") };
        doc["start"]!["identityMap"] = id.DeepClone(); doc["start"]!["rowKey"] = Row("code");
        foreach (var field in id) doc["objects"]![0]!["identity"]![field.Key] = field.Value!.DeepClone();
        doc["objects"]![0]!["detail"]!["parameterMap"] = Maps(); doc["objects"]![0]!["detail"]!["fields"]![0]!["value"] = Row("code");
        foreach (var direction in new[] { "forward", "reverse" }) { doc["relations"]![0]![direction]!["inputMap"] = Maps(); doc["relations"]![0]![direction]!["result"]!["targetIdentity"] = id.DeepClone(); }
        doc["relations"]![0]!["identity"] = Row("movementId"); doc["relations"]![0]!["label"] = Row("movementId"); doc["relations"]![0]!["evidence"] = new JsonObject { ["recordKey"] = Row("movementId"), ["occurredOn"] = Row("occurredOn"), ["quantity"] = Row("quantity"), ["unit"] = Row("unit") };
        return doc;
    }
    private sealed class TestIdentity(StudioPrincipal p) : IStudioIdentity
    { public StudioPrincipal Current { get; set; } = p; public Task<StudioPrincipal> GetCurrentAsync(CancellationToken ct) => Task.FromResult(Current); public Task<StudioPrincipal> RefreshAsync(StudioPrincipal principal, CancellationToken ct) => Task.FromResult(Current); }
    private sealed class NoLinks : ITraceNodeLinks
    {
        public Task<TraceNodeLinkResult> ResolveAsync(StudioPrincipal p, string r, JsonObject l, JsonNode? v, JsonObject args, string? version, JsonObject context, CancellationToken ct) => throw new NotSupportedException();
        public JsonObject ReadTicketContext(StudioPrincipal p, string r, string ticket) => throw new NotSupportedException();
        public Task<TraceLinkContent> OpenAsync(StudioPrincipal p, string r, string ticket, TraceLinkInput input, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class TraceExecutor : IStudioSourceExecutor
    {
        public bool HideDetails { get; set; } public bool TimeoutRelations { get; set; }
        public int SectionCalls { get; private set; }
        public JsonObject? LastSectionArguments { get; private set; }
        public bool EmptyAssociation { get; set; }
        public bool DuplicateSectionKeys { get; set; }
        public Action? SectionExecuted { get; set; }
        public string ConfigurationHash(StudioSourceEndpoint endpoint) => SourceAdministration.EndpointHash(endpoint);
        public Task<JsonObject> TestAsync(StudioSource s, CancellationToken ct) => Task.FromResult(new JsonObject());
        public Task<StudioSourceResult> ExecuteAsync(StudioPrincipal p, StudioSource s, JsonObject doc, JsonObject args, bool preview, CancellationToken ct)
        {
            var operation = StudioJson.Text(doc["query"]!["operationKey"]); var rows = new JsonArray();
            JsonObject Row(string factory, string id) => new() { ["sourceSystem"] = "MES", ["factory"] = factory, ["businessKey"] = id, ["code"] = factory + "-" + id, ["materialCode"] = EmptyAssociation ? null : "MAT-" + id };
            if (operation == "checks")
            {
                SectionCalls++; LastSectionArguments = args.DeepClone().AsObject();
                var offset = args["offset"]!.GetValue<int>(); var size = args["limit"]!.GetValue<int>();
                for (var i = offset; i < Math.Min(offset + size, 5); i++)
                    rows.Add(new JsonObject { ["checkId"] = StudioJson.Text(args["id"]) + "-" + (DuplicateSectionKeys ? "same" : (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)), ["checkLabel"] = "检验项" + (i + 1), ["checkValue"] = "合格", ["unconfiguredSecret"] = "hidden" });
                SectionExecuted?.Invoke();
                return Task.FromResult(new StudioSourceResult(new JsonObject { ["rows"] = rows, ["totalCount"] = "5" }, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, rows.Count, 1));
            }
            if (operation is "starts" or "detail")
            {
                foreach (var (factory, id) in new[] { ("F1", "A"), ("F1", "B"), ("F1", "C"), ("F2", "A") })
                    if (p.FactoryNIds.Contains(factory) && (operation == "starts" || !HideDetails && StudioJson.Text(args["id"]) == id && StudioJson.Text(args["factory"]) == factory)) rows.Add(Row(factory, id));
            }
            else
            {
                if (TimeoutRelations) throw new StudioException("SOURCE_TIMEOUT", "timeout", 504);
                foreach (var (id, from, to) in new[] { ("M1", "A", "C"), ("M2", "A", "C"), ("M3", "B", "C"), ("M4", "C", "A") })
                    if (StudioJson.Text(args["factory"]) == "F1" && StudioJson.Text(args["id"]) == (operation == "forward" ? from : to))
                    { var row = Row("F1", operation == "forward" ? to : from); row["movementId"] = id; row["occurredOn"] = "2026-10-31T00:00:00Z"; row["quantity"] = "2.0"; row["unit"] = "kg"; rows.Add(row); }
            }
            return Task.FromResult(new StudioSourceResult(rows, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, rows.Count, 1));
        }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public required StudioTestContext Context { get; init; } public required TraceRuntime Runtime { get; init; } public required StudioPrincipal P { get; init; }
        public required string Revision { get; init; } public required JsonObject Document { get; init; } public required TestIdentity Identity { get; init; } public required TraceExecutor Executor { get; init; }
        public static async Task<Fixture> Create(int maxNodes = 1000, int maxDepth = 12, int maxEdges = 3000, bool sections = false)
        {
            var c = await StudioTestContext.CreateAsync(); c.Profile.InstalledModules = ["LowCode", "Traceability"];
            var p = Principal(TimeProvider.System); var executor = new TraceExecutor(); var endpoint = new StudioSourceEndpoint { ReadOnlyVerified = true };
            c.Profile.SourceRegistry["trace-source"] = endpoint;
            var source = new StudioSource { TenantNId = p.TenantNId, NId = "trace-source", Name = "fixture", Provider = "RegisteredApi", EndpointKey = "trace-source", EndpointConfigurationHash = executor.ConfigurationHash(endpoint), IsEnabled = true, ConfigurationVersion = 1 };
            await c.Store.InsertSourceAsync(source, Ct);
            var model = Model(); model["limits"]!["maxNodes"] = maxNodes; model["limits"]!["maxDepth"] = maxDepth; model["limits"]!["maxEdges"] = maxEdges;
            foreach (var operation in sections ? new[] { "starts", "detail", "forward", "reverse", "checks" } : new[] { "starts", "detail", "forward", "reverse" })
            {
                var fields = new[] { "sourceSystem", "factory", "businessKey", "code", "movementId", "occurredOn", "quantity", "unit", "materialCode" };
                var doc = new JsonObject { ["schemaVersion"] = 1, ["sourceNId"] = source.NId, ["sourceConfigurationVersion"] = "1", ["query"] = new JsonObject { ["kind"] = "Api", ["operationKey"] = operation },
                    ["parameters"] = operation == "starts" ? new JsonArray() : new JsonArray(new JsonObject { ["name"] = "id", ["type"] = "String", ["required"] = true, ["source"] = "Caller" }, new JsonObject { ["name"] = "factory", ["type"] = "String", ["required"] = true, ["source"] = "Caller" }),
                    ["result"] = new JsonObject { ["id"] = "root", ["name"] = "rows", ["type"] = "Array", ["item"] = new JsonObject { ["id"] = "row", ["name"] = "row", ["type"] = "Object", ["children"] = new JsonArray(fields.Select(x => (JsonNode)new JsonObject { ["id"] = x, ["name"] = x, ["type"] = "String", ["nullable"] = true }).ToArray()) } },
                    ["readableFieldIds"] = new JsonArray(fields.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()), ["rowPolicyKey"] = "authorized-factories", ["timeoutSeconds"] = 30, ["maxRows"] = 10000, ["maxBytes"] = "10485760", ["cacheSeconds"] = 0 };
                if (operation == "checks")
                {
                    doc["parameters"]!.AsArray().Add(new JsonObject { ["name"] = "materialCode", ["type"] = "String", ["required"] = true });
                    foreach (var name in new[] { "offset", "limit" }) doc["parameters"]!.AsArray().Add(new JsonObject { ["name"] = name, ["type"] = "Int32", ["required"] = true });
                    var checkFields = new[] { "checkId", "checkLabel", "checkValue" };
                    doc["result"] = new JsonObject { ["id"] = "root", ["name"] = "root", ["type"] = "Object", ["children"] = new JsonArray(
                        new JsonObject { ["id"] = "rows", ["name"] = "rows", ["type"] = "Array", ["item"] = new JsonObject { ["id"] = "row", ["name"] = "row", ["type"] = "Object", ["children"] = new JsonArray(checkFields.Select(x => (JsonNode)new JsonObject { ["id"] = x, ["name"] = x, ["type"] = "String" }).ToArray()) } },
                        new JsonObject { ["id"] = "total", ["name"] = "totalCount", ["type"] = "Int64" }) };
                    doc["readableFieldIds"] = new JsonArray(checkFields.Append("total").Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
                    doc["pagination"] = new JsonObject { ["mode"] = "Offset", ["defaultSize"] = 2, ["maxSize"] = 200, ["input"] = new JsonObject { ["offsetParameter"] = "offset", ["limitParameter"] = "limit" }, ["result"] = new JsonObject { ["totalCountPath"] = "totalCount" } };
                }
                var created = await c.Publishing.CreateAsync(p, new(operation, "Dataset", operation, null, null, doc), "create-" + operation, Ct);
                var plan = await c.Publishing.ValidateAsync(p, operation, doc, Ct);
                Assert.True(plan.Valid, string.Join(";", plan.Diagnostics.Select(x => x.Message)));
                var published = await c.Publishing.PublishAsync(p, operation, new(created.OptimisticVersion, created.ConcurrencyVersion, "fixture", true, plan.DependencyPlanHash), "publish-" + operation, Ct);
                if (operation == "starts") model["start"]!["datasetRevisionNId"] = published.NId;
                else if (operation == "detail") model["objects"]![0]!["detail"]!["datasetRevisionNId"] = published.NId;
                else if (operation == "checks")
                {
                    static JsonObject Row(string field) => new() { ["from"] = "Row", ["path"] = new JsonArray(field), ["nullPolicy"] = "Error" };
                    var mappings = model["objects"]![0]!["detail"]!["parameterMap"]!.DeepClone().AsArray();
                    mappings[0]!["source"] = Row("businessKey");
                    var tableMappings = mappings.DeepClone().AsArray(); tableMappings.Add(new JsonObject { ["target"] = "materialCode", ["source"] = Row("materialCode") });
                    model["presentation"] = new JsonObject { ["layoutDirection"] = "TopToBottom" };
                    model["objects"]![0]!["detail"]!["sections"] = new JsonArray(
                        new JsonObject { ["id"] = "checks", ["label"] = "关联检验", ["kind"] = "Table", ["datasetRevisionNId"] = published.NId, ["parameterMap"] = tableMappings, ["collectionPath"] = new JsonArray("rows"), ["fields"] = new JsonArray(), ["columns"] = new JsonArray(new JsonObject { ["fieldId"] = "checkLabel", ["label"] = "项目", ["visible"] = true, ["width"] = 180 }, new JsonObject { ["fieldId"] = "checkValue", ["label"] = "结果", ["visible"] = true, ["width"] = 120 }), ["rowKey"] = Row("checkId"), ["pageSize"] = 2 },
                        new JsonObject { ["id"] = "basic", ["label"] = "扩展信息", ["kind"] = "Fields", ["datasetRevisionNId"] = model["objects"]![0]!["detail"]!["datasetRevisionNId"]!.DeepClone(), ["parameterMap"] = mappings, ["collectionPath"] = new JsonArray(), ["fields"] = new JsonArray(new JsonObject { ["label"] = "批号", ["value"] = Row("code") }), ["columns"] = new JsonArray(), ["pageSize"] = 20 });
                }
                else model["relations"]![0]![operation]!["datasetRevisionNId"] = published.NId;
            }
            var resource = await c.Publishing.CreateAsync(p, new("trace", "TraceModel", "追溯", null, null, model), "create-trace", Ct);
            var tracePlan = await c.Publishing.ValidateAsync(p, "trace", model, Ct); Assert.True(tracePlan.Valid, string.Join(";", tracePlan.Diagnostics.Select(d => d.Path + ": " + d.Message))); Assert.Equal(sections ? 5 : 4, tracePlan.DependencyPlan.Count); Assert.Single(tracePlan.SourcePins);
            var trace = await c.Publishing.PublishAsync(p, "trace", new(resource.OptimisticVersion, resource.ConcurrencyVersion, "fixture", true, tracePlan.DependencyPlanHash), "publish-trace", Ct);
            var identity = new TestIdentity(p); var datasets = new DatasetExecution(c.Store, c.Publishing, c.Authorization, executor, new(), TimeProvider.System);
            return new() { Context = c, P = p, Revision = trace.NId, Document = model, Identity = identity, Executor = executor, Runtime = new(c.Store, c.Publishing, datasets, c.Authorization, identity, new(TimeProvider.System), new NoLinks()) };
        }
        public async Task<JsonObject> Query(string key, string direction)
        {
            var starts = await Runtime.StartsAsync(P, "trace", new() { ["revisionNId"] = Revision }, Ct);
            var row = starts["items"]!.AsArray().First(x => StudioJson.Text(x!["identity"]!["businessKey"]) == key);
            return await Runtime.QueryAsync(P, "trace", new() { ["revisionNId"] = Revision, ["startKeys"] = new JsonArray(row!["startKey"]!.DeepClone()), ["direction"] = direction }, Ct);
        }
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
}
