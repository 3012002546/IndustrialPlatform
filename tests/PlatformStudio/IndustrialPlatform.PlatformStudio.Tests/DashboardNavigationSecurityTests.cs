using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Data;
using IndustrialPlatform.PlatformStudio.Application.Dashboard;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Contracts;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class DashboardNavigationSecurityTests
{
    [Fact]
    public async Task Dashboard_reads_bypass_dataset_cache_and_isolate_transient_source_failures()
    {
        await using var harness = await DashboardHarness.CreateAsync(cacheSeconds: 60);
        var revision = await harness.PublishDashboardAsync("dashboard-cache-errors", Dashboard(harness, Components(Text("title", "fixture")), instances: new JsonArray(
            Instance(harness, "healthy", Binding("Constant", value: "L01")),
            Instance(harness, "failing", Binding("Constant", value: "L02")))));
        var before = harness.Fixture.SourceExecutor.Calls;
        await harness.Navigation.RuntimeAsync(harness.Fixture.Principal, "dashboard-cache-errors", revision.NId, new(), CancellationToken.None, ["healthy"]);
        await harness.Navigation.RuntimeAsync(harness.Fixture.Principal, "dashboard-cache-errors", revision.NId, new(), CancellationToken.None, ["healthy"]);
        Assert.Equal(before + 2, harness.Fixture.SourceExecutor.Calls);
        harness.Fixture.SourceExecutor.Failure = parameters => StudioJson.Text(parameters["lineCode"]) == "L02" ? new StudioException("SOURCE_TIMEOUT", "fixture timeout", 504) : null;
        var result = await harness.Navigation.RuntimeAsync(harness.Fixture.Principal, "dashboard-cache-errors", revision.NId, new(), CancellationToken.None);
        Assert.NotNull(result["instances"]!["healthy"]!["data"]);
        Assert.Null(result["instances"]!["failing"]!["data"]);
        Assert.Equal("SOURCE_TIMEOUT", StudioJson.Text(result["instances"]!["failing"]!["error"]!["code"]));
        Assert.DoesNotContain("fixture timeout", result.ToJsonString());
    }
    [Fact]
    public async Task Shared_constants_and_partial_reads_do_not_execute_unrequested_sources()
    {
        await using var harness = await DashboardHarness.CreateAsync();
        var api = Instance(harness, "fast", Binding("Constant", value: "L01"));
        api["sourceType"] = "Api";
        var slow = Instance(harness, "slow", Binding("Constant", value: "L01"));
        var constants = new JsonObject { ["instanceId"] = "shared", ["sourceType"] = "Constant", ["value"] = new JsonObject { ["output"] = 0, ["enabled"] = false }, ["refreshSeconds"] = 0 };
        var revision = await harness.PublishDashboardAsync("dashboard-partial", Dashboard(harness, Components(Text("title", "fixture")), instances: new JsonArray(api, slow, constants)));
        var before = harness.Fixture.SourceExecutor.Calls;
        var result = await harness.Navigation.RuntimeAsync(harness.Fixture.Principal, "dashboard-partial", revision.NId, new(), CancellationToken.None, ["fast", "shared"]);
        Assert.Equal(before + 1, harness.Fixture.SourceExecutor.Calls);
        Assert.False(result["instances"]!.AsObject().ContainsKey("slow"));
        Assert.Equal(0, result["instances"]!["shared"]!["data"]!["output"]!.GetValue<int>());
        Assert.False(result["instances"]!["shared"]!["data"]!["enabled"]!.GetValue<bool>());
        var heartbeat = await harness.Navigation.RuntimeAsync(harness.Fixture.Principal, "dashboard-partial", revision.NId, new(), CancellationToken.None, []);
        Assert.Empty(heartbeat["instances"]!.AsObject());
        Assert.Equal(before + 1, harness.Fixture.SourceExecutor.Calls);
        await Assert.ThrowsAsync<StudioException>(() => harness.Navigation.RuntimeAsync(harness.Fixture.Principal, "dashboard-partial", revision.NId, new(), CancellationToken.None, ["missing"]));
    }

    [Fact]
    public async Task Shared_constant_dependencies_resolve_out_of_order_and_cycles_are_rejected()
    {
        await using var harness = await DashboardHarness.CreateAsync();
        var dynamic = Instance(harness, "dependent", Binding("Instance", ["line"], null, "shared"));
        var constants = new JsonObject { ["instanceId"] = "shared", ["sourceType"] = "Constant", ["value"] = new JsonObject { ["line"] = "L01" } };
        var document = Dashboard(harness, Components(Text("title", "fixture")), instances: new JsonArray(dynamic, constants));
        var revision = await harness.PublishDashboardAsync("dashboard-constant-dependency", document);
        var result = await harness.Navigation.RuntimeAsync(harness.Fixture.Principal, "dashboard-constant-dependency", revision.NId, new(), CancellationToken.None, ["dependent"]);
        Assert.NotNull(result["instances"]!["dependent"]!["data"]);
        Assert.NotNull(result["instances"]!["shared"]);
        constants["dependsOn"] = new JsonArray("dependent");
        Assert.Contains(StudioDocuments.Validate("Dashboard", document), item => item.Path == "instances.dependsOn");
        constants["dependsOn"] = null;
        constants["refreshSeconds"] = 10;
        Assert.Contains(StudioDocuments.Validate("Dashboard", document), item => item.Path == "instances.shared.refreshSeconds");
    }

    [Fact]
    public async Task Canonical_chart_and_table_rows_share_one_instance_request_and_keep_empty_rows()
    {
        await using var harness = await DashboardHarness.CreateAsync();
        var document = Dashboard(
            harness,
            Components(
                Chart("trend-chart"),
                Table("line-table"),
                Tank("line-tank")),
            instances: new JsonArray(Instance(harness, "lineData", Binding("Constant", value: "L01"))));
        var revision = await harness.PublishDashboardAsync("dashboard-canonical", document);

        JsonObject? last = null;
        for (var index = 0; index < 10; index++)
            last = await harness.Navigation.RuntimeAsync(harness.Fixture.Principal, "dashboard-canonical", revision.NId, new(), CancellationToken.None);

        Assert.NotNull(last);
        Assert.Equal(10, harness.Fixture.SourceExecutor.Calls);
        Assert.Equal(2, last!["instances"]!["lineData"]!["data"]!["rows"]!.AsArray().Count);
        Assert.Equal("Chart", last["document"]!["components"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("Line", last["document"]!["components"]![0]!["props"]!["kind"]!.GetValue<string>());
        Assert.Equal("Table", last["document"]!["components"]![1]!["type"]!.GetValue<string>());
        Assert.Equal("Table", last["document"]!["components"]![1]!["props"]!["presentation"]!.GetValue<string>());
        Assert.Null(last["document"]!["components"]![1]!["props"]!["instanceId"]);

        harness.Fixture.SourceExecutor.Data = new JsonObject { ["rows"] = new JsonArray() };
        var empty = await harness.Navigation.RuntimeAsync(harness.Fixture.Principal, "dashboard-canonical", revision.NId, new(), CancellationToken.None);
        Assert.Empty(empty["instances"]!["lineData"]!["data"]!["rows"]!.AsArray());
        Assert.Equal(11, harness.Fixture.SourceExecutor.Calls);
    }

    [Fact]
    public async Task Source_time_quality_stays_stale_or_unknown_when_transport_succeeds()
    {
        await using var harness = await DashboardHarness.CreateAsync();
        var revision = await harness.PublishDashboardAsync(
            "dashboard-quality",
            Dashboard(harness, Components(Table("line-table")), instances: new JsonArray(Instance(harness, "lineData", Binding("Constant", value: "L01")))));

        harness.Fixture.SourceExecutor.Data = Data(sourceObservedOn: "2020-01-01T00:00:00Z");
        var stale = await harness.Navigation.RuntimeAsync(harness.Fixture.Principal, "dashboard-quality", revision.NId, new(), CancellationToken.None);
        Assert.Equal("Stale", stale["sourceFreshness"]!["lineData"]!["status"]!.GetValue<string>());
        Assert.Equal("2020-01-01T00:00:00.0000000+00:00", stale["sourceFreshness"]!["lineData"]!["sourceObservedOn"]!.GetValue<string>());
        Assert.NotNull(stale["sourceFreshness"]!["lineData"]!["observedOn"]);

        harness.Fixture.SourceExecutor.Data = Data(includeSourceObservedOn: false);
        var unknown = await harness.Navigation.RuntimeAsync(harness.Fixture.Principal, "dashboard-quality", revision.NId, new(), CancellationToken.None);
        Assert.Equal("Unknown", unknown["sourceFreshness"]!["lineData"]!["status"]!.GetValue<string>());
        Assert.Equal("SOURCE_TIME_INVALID", unknown["sourceFreshness"]!["lineData"]!["reasonCode"]!.GetValue<string>());
        Assert.Null(unknown["sourceFreshness"]!["lineData"]!["sourceObservedOn"]);
        Assert.NotNull(unknown["sourceFreshness"]!["lineData"]!["observedOn"]);
    }

    [Fact]
    public async Task Resolve_rechecks_server_row_key_and_uses_the_fixed_published_target_revision()
    {
        await using var harness = await DashboardHarness.CreateAsync();
        var target = TargetDashboard("v1");
        target["parameters"] = new JsonArray(new JsonObject { ["name"] = "lineId", ["type"] = "String", ["source"] = "Caller", ["required"] = true, ["nullable"] = false });
        var targetV1 = await harness.PublishDashboardAsync("dashboard-target", target);
        target["components"]![0]!["props"]!["text"] = "v2";
        var targetV2 = await harness.PublishDashboardAsync("dashboard-target", target);
        Assert.NotEqual(targetV1.NId, targetV2.NId);

        var sourceDocument = Dashboard(
            harness,
            Components(Table("line-table")),
            new JsonArray(new JsonObject
            {
                ["id"] = "line-click",
                ["componentId"] = "line-table",
                ["event"] = "RowClick",
                ["target"] = new JsonObject { ["kind"] = "Dashboard", ["resourceNId"] = "dashboard-target", ["revisionNId"] = targetV1.NId },
                ["parameterMap"] = new JsonArray(new JsonObject { ["target"] = "lineId", ["source"] = Binding("Row", "backupLineCode") }),
                ["openMode"] = "Current",
            }));
        var sourceRevision = await harness.PublishDashboardAsync("dashboard-source", sourceDocument);
        Assert.Contains(sourceRevision.Dependencies.OfType<JsonObject>(), dependency =>
            dependency["resourceNId"]?.GetValue<string>() == "dashboard-target" && dependency["revisionNId"]?.GetValue<string>() == targetV1.NId);

        var resolved = await harness.Navigation.ResolveAsync(
            harness.Fixture.Principal,
            "dashboard-source",
            new(
                sourceRevision.NId,
                "line-click",
                new JsonObject { ["key"] = "L02", ["backupLineCode"] = "client-forged" },
                new(),
                new JsonObject { ["lineCode"] = "L02" },
                new JsonObject { ["zoom"] = 1 }, IncludeRuntime: true),
            CancellationToken.None);
        Assert.Equal(targetV1.NId, resolved["targetRevisionNId"]!.GetValue<string>());
        Assert.Equal("backup-L02", resolved["parameters"]!["lineId"]!.GetValue<string>());
        Assert.Equal("L02", resolved["navigationState"]!["filters"]!["lineCode"]!.GetValue<string>());
        Assert.Equal(1, resolved["navigationState"]!["viewport"]!["zoom"]!.GetValue<int>());
        Assert.Equal(targetV1.NId, resolved["targetRuntime"]!["revisionNId"]!.GetValue<string>());
        Assert.NotNull(resolved["targetRuntime"]!["document"]);

        var invisible = await Assert.ThrowsAsync<StudioException>(() => harness.Navigation.ResolveAsync(
            harness.Fixture.Principal,
            "dashboard-source",
            new(sourceRevision.NId, "line-click", new JsonObject { ["key"] = "HIDDEN" }, new(), IncludeRuntime: true),
            CancellationToken.None));
        Assert.Equal(404, invisible.StatusCode);
        Assert.Equal("STUDIO_NOT_FOUND", invisible.Code);
    }

    [Fact]
    public async Task Published_navigation_document_keeps_the_direct_revision_when_dependency_closure_has_old_edges()
    {
        await using var harness = await DashboardHarness.CreateAsync();
        _ = await harness.PublishDashboardAsync("dashboard-b", TargetDashboard("B v1"));
        _ = await harness.PublishDashboardAsync("dashboard-a", LinkedDashboard(harness, "dashboard-b"));
        var bV2 = await harness.PublishDashboardAsync("dashboard-b", LinkedDashboard(harness, "dashboard-a"));
        var aV2 = await harness.PublishDashboardAsync("dashboard-a", LinkedDashboard(harness, "dashboard-b"));

        Assert.Equal(bV2.NId, aV2.Document["navigation"]![0]!["target"]!["revisionNId"]!.GetValue<string>());
        var resolved = await harness.Navigation.ResolveAsync(
            harness.Fixture.Principal,
            "dashboard-a",
            new(aV2.NId, "link", null, new()),
            CancellationToken.None);
        Assert.Equal(bV2.NId, resolved["targetRevisionNId"]!.GetValue<string>());
    }

    [Fact]
    public async Task Click_constant_and_page_parameter_mappings_do_not_require_a_row_key()
    {
        await using var harness = await DashboardHarness.CreateAsync();
        var target = await harness.PublishDashboardAsync("dashboard-target", TargetDashboard("target"));
        var source = await harness.PublishDashboardAsync(
            "dashboard-click",
            Dashboard(
                harness,
                Components(Button("constant-button"), Button("page-button")),
                new JsonArray(
                    Navigation("constant-click", "constant-button", "dashboard-target", "mode", Binding("Constant", value: "fixed")),
                    Navigation("page-click", "page-button", "dashboard-target", "lineId", Binding("PageParam", "lineId"))),
                parameters: new JsonArray(new JsonObject { ["name"] = "lineId", ["type"] = "String", ["required"] = false, ["nullable"] = true, ["source"] = "Caller" }),
                instances: new JsonArray()));

        var parameters = new JsonObject { ["lineId"] = "L02" };
        var constant = await harness.Navigation.ResolveAsync(
            harness.Fixture.Principal,
            "dashboard-click",
            new(source.NId, "constant-click", null, parameters),
            CancellationToken.None);
        Assert.Equal("fixed", constant["parameters"]!["mode"]!.GetValue<string>());

        var pageParameter = await harness.Navigation.ResolveAsync(
            harness.Fixture.Principal,
            "dashboard-click",
            new(source.NId, "page-click", null, parameters),
            CancellationToken.None);
        Assert.Equal("L02", pageParameter["parameters"]!["lineId"]!.GetValue<string>());
    }

    [Fact]
    public async Task Enabled_when_uses_the_authorized_row_and_trusted_page_parameter_context()
    {
        await using var harness = await DashboardHarness.CreateAsync();
        var target = await harness.PublishDashboardAsync("dashboard-target", TargetDashboard("target"));
        var rowCondition = new JsonObject
        {
            ["op"] = "Eq",
            ["left"] = Binding("Row", "lineCode"),
            ["right"] = Binding("Constant", value: "L02"),
        };
        var parameterCondition = new JsonObject
        {
            ["op"] = "Eq",
            ["left"] = Binding("PageParam", "allow"),
            ["right"] = Binding("Constant", value: true),
        };
        var source = await harness.PublishDashboardAsync(
            "dashboard-conditions",
            Dashboard(
                harness,
                Components(Table("line-table"), Button("allow-button")),
                new JsonArray(
                    Navigation("row-condition", "line-table", "dashboard-target", "lineId", Binding("Row", "backupLineCode"), "RowClick", rowCondition),
                    Navigation("parameter-condition", "allow-button", "dashboard-target", "mode", Binding("Constant", value: "allowed"), "Click", parameterCondition)),
                parameters: new JsonArray(new JsonObject { ["name"] = "allow", ["type"] = "Boolean", ["required"] = true, ["nullable"] = false, ["source"] = "Caller" })));

        var rowAllowed = await harness.Navigation.ResolveAsync(
            harness.Fixture.Principal,
            "dashboard-conditions",
            new(source.NId, "row-condition", new JsonObject { ["key"] = "L02" }, new JsonObject { ["allow"] = true }),
            CancellationToken.None);
        Assert.Equal("backup-L02", rowAllowed["parameters"]!["lineId"]!.GetValue<string>());

        var rowDenied = await Assert.ThrowsAsync<StudioException>(() => harness.Navigation.ResolveAsync(
            harness.Fixture.Principal,
            "dashboard-conditions",
            new(source.NId, "row-condition", new JsonObject { ["key"] = "L01" }, new JsonObject { ["allow"] = true }),
            CancellationToken.None));
        Assert.Equal(403, rowDenied.StatusCode);
        Assert.Equal("STUDIO_FORBIDDEN", rowDenied.Code);

        var parameterAllowed = await harness.Navigation.ResolveAsync(
            harness.Fixture.Principal,
            "dashboard-conditions",
            new(source.NId, "parameter-condition", null, new JsonObject { ["allow"] = true }),
            CancellationToken.None);
        Assert.Equal("allowed", parameterAllowed["parameters"]!["mode"]!.GetValue<string>());

        var parameterDenied = await Assert.ThrowsAsync<StudioException>(() => harness.Navigation.ResolveAsync(
            harness.Fixture.Principal,
            "dashboard-conditions",
            new(source.NId, "parameter-condition", null, new JsonObject { ["allow"] = false }),
            CancellationToken.None));
        Assert.Equal(403, parameterDenied.StatusCode);
        Assert.Equal("STUDIO_FORBIDDEN", parameterDenied.Code);
    }

    [Fact]
    public async Task Missing_required_page_parameter_fails_before_the_dataset_executor()
    {
        await using var harness = await DashboardHarness.CreateAsync();
        var revision = await harness.PublishDashboardAsync(
            "dashboard-required",
            Dashboard(
                harness,
                Components(Table("line-table")),
                parameters: new JsonArray(new JsonObject { ["name"] = "lineId", ["type"] = "String", ["required"] = true, ["nullable"] = false, ["source"] = "Caller" }),
                instances: new JsonArray(Instance(harness, "lineData", Binding("PageParam", "lineId")))));

        var before = harness.Fixture.SourceExecutor.Calls;
        var missing = await Assert.ThrowsAsync<StudioException>(() => harness.Navigation.RuntimeAsync(harness.Fixture.Principal, "dashboard-required", revision.NId, new(), CancellationToken.None));
        Assert.Equal("PARAM_REQUIRED", missing.Code);
        Assert.Equal(before, harness.Fixture.SourceExecutor.Calls);

        var resolved = await harness.Navigation.RuntimeAsync(harness.Fixture.Principal, "dashboard-required", revision.NId, new JsonObject { ["lineId"] = "L02" }, CancellationToken.None);
        Assert.Equal(before + 1, harness.Fixture.SourceExecutor.Calls);
        Assert.Equal(2, resolved["instances"]!["lineData"]!["data"]!["rows"]!.AsArray().Count);
    }

    [Fact]
    public async Task Dashboard_runtime_requires_function_permission_and_resource_grant()
    {
        await using var harness = await DashboardHarness.CreateAsync();
        var revision = await harness.PublishDashboardAsync("dashboard-permission", TargetDashboard("permission"));

        var withoutFunction = harness.Fixture.Principal with
        {
            Permissions = harness.Fixture.Principal.Permissions.Where(permission => permission != "studio.dashboard.run").ToArray(),
        };
        var forbidden = await Assert.ThrowsAsync<StudioException>(() => harness.Navigation.RuntimeAsync(withoutFunction, "dashboard-permission", revision.NId, new(), CancellationToken.None));
        Assert.Equal(403, forbidden.StatusCode);
        Assert.Equal("STUDIO_FORBIDDEN", forbidden.Code);

        var resource = await harness.Fixture.Store.FindResourceAsync(harness.Fixture.Principal.TenantNId, "dashboard-permission", CancellationToken.None);
        Assert.NotNull(resource);
        await harness.Fixture.Store.ReplaceGrantsAsync(harness.Fixture.Principal.TenantNId, resource!.Id, Array.Empty<StudioGrant>(), CancellationToken.None);
        var withoutGrant = await Assert.ThrowsAsync<StudioException>(() => harness.Navigation.RuntimeAsync(harness.Fixture.Principal, "dashboard-permission", revision.NId, new(), CancellationToken.None));
        Assert.Equal(404, withoutGrant.StatusCode);
        Assert.Equal("STUDIO_NOT_FOUND", withoutGrant.Code);
    }

    [Fact]
    public async Task Published_definition_needs_only_run_permission_and_does_not_execute_data()
    {
        await using var harness = await DashboardHarness.CreateAsync();
        var document = TargetDashboard("parameters");
        document["parameters"] = new JsonArray(new JsonObject { ["name"] = "lineId", ["type"] = "String", ["required"] = true, ["nullable"] = false, ["source"] = "Caller" });
        var revision = await harness.PublishDashboardAsync("definition", document);
        var runOnly = harness.Fixture.Principal with { Permissions = ["studio.dashboard.run"] };
        var result = await harness.Navigation.DefinitionAsync(runOnly, "definition", null, CancellationToken.None);
        Assert.Equal(revision.NId, result["revisionNId"]!.GetValue<string>());
        Assert.Equal("lineId", result["document"]!["parameters"]![0]!["name"]!.GetValue<string>());
        Assert.Equal(0, harness.Fixture.SourceExecutor.Calls);
    }

    [Fact]
    public async Task Automatic_navigation_pins_do_not_mark_an_unchanged_draft_dirty()
    {
        await using var harness = await DashboardHarness.CreateAsync();
        await harness.PublishDashboardAsync("clean-target", TargetDashboard("target"));
        await harness.PublishDashboardAsync("clean-source", LinkedDashboard(harness, "clean-target"));
        var draft = await harness.Fixture.Publishing.GetDraftAsync(harness.Fixture.Principal, "clean-source", CancellationToken.None);
        Assert.False(draft["hasDraftChanges"]!.GetValue<bool>());
        Assert.Null(draft["draftDocument"]!["navigation"]![0]!["target"]!["revisionNId"]);
    }

    [Theory]
    [InlineData("StackedBar")]
    [InlineData("Scatter")]
    [InlineData("Donut")]
    public void Canonical_chart_variants_are_valid_without_extra_palette_entries(string kind)
    {
        var document = TargetDashboard("chart");
        document["components"] = Components(Component("chart", "Chart", new JsonObject { ["kind"] = kind }));
        Assert.DoesNotContain(StudioDocuments.Validate("Dashboard", document), error => error.Severity == "Error");
        Assert.Equal(35, DashboardComponentRegistry.Entries.Count);
    }

    [Fact]
    public async Task Draft_preview_rechecks_row_and_exposes_target_query_mapping_and_actual_result()
    {
        await using var harness = await DashboardHarness.CreateAsync();
        var target = TargetDashboard("preview target");
        target["parameters"] = new JsonArray(new JsonObject { ["name"] = "lineId", ["type"] = "String", ["required"] = true, ["nullable"] = false, ["source"] = "Caller" });
        target["instances"] = new JsonArray(Instance(harness, "lineData", Binding("PageParam", "lineId")));
        await harness.PublishDashboardAsync("preview-target", target);
        var source = Dashboard(harness, Components(Table("line-table")), new JsonArray(Navigation("line-click", "line-table", "preview-target", "lineId", Binding("Row", "lineCode"), "RowClick")));
        await harness.PublishDashboardAsync("preview-source", source);
        source["navigation"]![0]!["parameterMap"]![0]!["source"]!["path"] = new JsonArray("backupLineCode");
        var result = await harness.Navigation.PreviewResolveAsync(harness.Fixture.Principal, "preview-source", source, new(null, "line-click", new JsonObject { ["key"] = "L02" }, new()), CancellationToken.None);
        Assert.Equal("backup-L02", result["parameters"]!["lineId"]!.GetValue<string>());
        Assert.Equal("backup-L02", result["targetQueryParameters"]!["lineData"]!["lineCode"]!.GetValue<string>());
        Assert.Equal("L02", result["source"]!["row"]!["lineCode"]!.GetValue<string>());
        Assert.NotNull(result["targetRuntime"]!["instances"]!["lineData"]!["data"]);
        Assert.Equal(2, harness.Fixture.SourceExecutor.Calls);
    }

    [Theory]
    [InlineData("Caller", false)]
    [InlineData("Caller", true)]
    [InlineData("TrustedContext", false)]
    [InlineData("TrustedContext", true)]
    public async Task Navigation_mappings_conditions_and_return_state_use_resolved_page_parameters(string parameterSource, bool preview)
    {
        await using var harness = await DashboardHarness.CreateAsync();
        var expected = parameterSource == "Caller" ? "L01" : harness.Fixture.Principal.SubjectNId;
        var target = TargetDashboard("target");
        target["parameters"] = new JsonArray(new JsonObject { ["name"] = "lineId", ["type"] = "String", ["source"] = "Caller", ["required"] = true });
        await harness.PublishDashboardAsync("parameter-target", target);
        var parameter = new JsonObject { ["name"] = "lineId", ["type"] = "String", ["source"] = parameterSource, ["required"] = true, ["nullable"] = false };
        if (parameterSource == "Caller") parameter["defaultValue"] = expected;
        else parameter["contextKey"] = "user";
        var condition = new JsonObject { ["op"] = "Eq", ["left"] = Binding("PageParam", "lineId"), ["right"] = Binding("Constant", value: expected) };
        var document = Dashboard(harness, Components(Button("open")),
            new JsonArray(Navigation("open", "open", "parameter-target", "lineId", Binding("PageParam", "lineId"), enabledWhen: condition)),
            new JsonArray(parameter), new JsonArray());
        var revision = await harness.PublishDashboardAsync("parameter-source", document);
        var supplied = parameterSource == "Caller" ? new JsonObject() : new JsonObject { ["lineId"] = "forged-user" };
        var request = new DashboardNavigationRequest(revision.NId, "open", null, supplied, IncludeRuntime: true);
        var result = preview
            ? await harness.Navigation.PreviewResolveAsync(harness.Fixture.Principal, "parameter-source", document, request, CancellationToken.None)
            : await harness.Navigation.ResolveAsync(harness.Fixture.Principal, "parameter-source", request, CancellationToken.None);
        Assert.Equal(expected, StudioJson.Text(result["parameters"]!["lineId"]));
        Assert.Equal(expected, StudioJson.Text(result["navigationState"]!["parameters"]!["lineId"]));
        Assert.NotNull(result["targetRuntime"]);
        Assert.Equal(0, harness.Fixture.SourceExecutor.Calls);
    }

    [Theory]
    [InlineData("Caller")]
    [InlineData("TrustedContext")]
    public async Task Preview_target_query_diagnostics_use_the_same_resolved_parameters_as_runtime(string parameterSource)
    {
        await using var harness = await DashboardHarness.CreateAsync();
        var expected = parameterSource == "Caller" ? "L01" : harness.Fixture.Principal.SubjectNId;
        var parameter = new JsonObject { ["name"] = "lineId", ["type"] = "String", ["source"] = parameterSource, ["required"] = true };
        if (parameterSource == "Caller") parameter["defaultValue"] = expected;
        else parameter["contextKey"] = "user";
        var target = Dashboard(harness, Components(Table("line-table")), parameters: new JsonArray(parameter),
            instances: new JsonArray(Instance(harness, "lineData", Binding("PageParam", "lineId"))));
        await harness.PublishDashboardAsync("diagnostic-target", target);
        var source = LinkedDashboard(harness, "diagnostic-target");
        source["navigation"]![0]!["parameterMap"] = new JsonArray();
        await harness.PublishDashboardAsync("diagnostic-source", source);
        var result = await harness.Navigation.PreviewResolveAsync(harness.Fixture.Principal, "diagnostic-source", source,
            new(null, "link", null, new()), CancellationToken.None);
        Assert.Equal(expected, StudioJson.Text(result["targetQueryParameters"]!["lineData"]!["lineCode"]));
        Assert.Equal(expected, StudioJson.Text(harness.Fixture.SourceExecutor.Parameters!["lineCode"]));
        Assert.Equal(1, harness.Fixture.SourceExecutor.Calls);
    }

    [Theory]
    [InlineData("Constant", false)]
    [InlineData("Instance", false)]
    [InlineData("Legacy", false)]
    [InlineData("Constant", true)]
    [InlineData("Instance", true)]
    [InlineData("Legacy", true)]
    public async Task Row_navigation_supports_constants_and_legacy_instances_but_rejects_duplicate_keys(string bindingKind, bool duplicate)
    {
        await using var harness = await DashboardHarness.CreateAsync();
        var target = TargetDashboard("target");
        target["parameters"] = new JsonArray(new JsonObject { ["name"] = "lineId", ["type"] = "String", ["source"] = "Caller", ["required"] = true });
        await harness.PublishDashboardAsync("row-target", target);
        var rows = new JsonArray(Row("L01", "1.0"), Row(duplicate ? "L01" : "L02", "2.0"));
        var table = Table("line-table");
        var instances = new JsonArray();
        if (bindingKind == "Constant") table["bindings"]![0]!["source"] = Binding("Constant", value: rows);
        else
        {
            instances.Add(new JsonObject { ["instanceId"] = "lineData", ["sourceType"] = "Constant", ["value"] = new JsonObject { ["rows"] = rows } });
            if (bindingKind == "Legacy")
            {
                table["bindings"] = new JsonArray();
                table["props"]!["instanceId"] = "lineData";
                table["props"]!["collectionPath"] = new JsonArray("rows");
            }
        }
        var document = Dashboard(harness, Components(table),
            new JsonArray(Navigation("line-click", "line-table", "row-target", "lineId", Binding("Row", "backupLineCode"), "RowClick")), instances: instances);
        var revision = await harness.PublishDashboardAsync("row-source", document);
        Task<JsonObject> Resolve(string key) => harness.Navigation.ResolveAsync(harness.Fixture.Principal, "row-source",
            new(revision.NId, "line-click", new JsonObject { ["key"] = key, ["backupLineCode"] = "forged" }, new(), IncludeRuntime: true), CancellationToken.None);
        if (duplicate)
        {
            var exception = await Assert.ThrowsAsync<StudioException>(() => Resolve("L01"));
            Assert.Equal("STUDIO_ROW_KEY_AMBIGUOUS", exception.Code);
            Assert.Equal(409, exception.StatusCode);
        }
        else
        {
            var result = await Resolve("L02");
            Assert.Equal("backup-L02", StudioJson.Text(result["parameters"]!["lineId"]));
            var hidden = await Assert.ThrowsAsync<StudioException>(() => Resolve("HIDDEN"));
            Assert.Equal(404, hidden.StatusCode);
        }
        Assert.Equal(0, harness.Fixture.SourceExecutor.Calls);
    }

    [Fact]
    public async Task Navigation_executes_only_row_condition_mapping_and_their_transitive_dependencies()
    {
        await using var harness = await DashboardHarness.CreateAsync();
        await harness.PublishDashboardAsync("dependency-target", TargetDashboard("target"));
        var row = Instance(harness, "lineData", Binding("Instance", ["line"], null, "shared"));
        row["dependsOn"] = new JsonArray("upstream");
        var mapped = Instance(harness, "mapped", Binding("Instance", ["rows", "0", "lineCode"], null, "lineData"));
        var condition = new JsonObject
        {
            ["op"] = "And", ["items"] = new JsonArray(
                new JsonObject { ["op"] = "Eq", ["left"] = Binding("Instance", ["rows", "0", "lineCode"], null, "mapped"), ["right"] = Binding("Constant", value: "L01") },
                new JsonObject { ["op"] = "Eq", ["left"] = Binding("Row", "lineCode"), ["right"] = Binding("Constant", value: "L02") }),
        };
        var document = Dashboard(harness, Components(Table("line-table")),
            new JsonArray(Navigation("line-click", "line-table", "dependency-target", "mode", Binding("Instance", ["rows", "0", "backupLineCode"], null, "mapped"), "RowClick", condition)),
            instances: new JsonArray(
                Instance(harness, "unrelated", Binding("Constant", value: "UNUSED")), mapped, row,
                new JsonObject { ["instanceId"] = "shared", ["sourceType"] = "Constant", ["value"] = new JsonObject { ["line"] = "L01" } },
                Instance(harness, "upstream", Binding("Constant", value: "L01"))));
        var revision = await harness.PublishDashboardAsync("dependency-source", document);
        harness.Fixture.SourceExecutor.Failure = input => StudioJson.Text(input["lineCode"]) == "UNUSED" ? new StudioException("SOURCE_TIMEOUT", "unrelated source must not run", 504) : null;
        var result = await harness.Navigation.ResolveAsync(harness.Fixture.Principal, "dependency-source",
            new(revision.NId, "line-click", new JsonObject { ["key"] = "L02" }, new()), CancellationToken.None);
        Assert.Equal("backup-L01", StudioJson.Text(result["parameters"]!["mode"]));
        Assert.Equal("L02", StudioJson.Text(result["source"]!["row"]!["lineCode"]));
        Assert.Equal(3, harness.Fixture.SourceExecutor.Calls); // upstream, lineData, mapped; mapped is shared by condition and mapping.
    }

    [Fact]
    public async Task Navigation_without_instance_references_skips_queries_and_deduplicates_live_dataset_checks()
    {
        await using var harness = await DashboardHarness.CreateAsync();
        await harness.PublishDashboardAsync("minimal-target", TargetDashboard("target"));
        // An object resembling a binding inside Constant.value must remain ordinary data.
        var value = new JsonObject { ["from"] = "Instance", ["instanceId"] = "not-a-dependency" };
        var document = Dashboard(harness, Components(Button("open")),
            new JsonArray(Navigation("open", "open", "minimal-target", "mode", Binding("Constant", value: value))),
            instances: new JsonArray(Enumerable.Range(1, 3).Select(index => (JsonNode)Instance(harness, "unused" + index, Binding("Constant", value: "L01"))).ToArray()));
        var revision = await harness.PublishDashboardAsync("minimal-source", document);
        var statements = new List<string>();
        harness.Fixture.Database.Client.Aop.OnLogExecuting = (sql, _) => statements.Add(sql);
        var result = await harness.Navigation.ResolveAsync(harness.Fixture.Principal, "minimal-source", new(revision.NId, "open", null, new()), CancellationToken.None);
        Assert.True(JsonNode.DeepEquals(value, result["parameters"]!["mode"]));
        Assert.Equal(0, harness.Fixture.SourceExecutor.Calls);
        // The former all-instance path needed 37 reads for this fixture. Retain live authorization, without repeating it per instance.
        Assert.InRange(statements.Count, 1, 12);
    }

    [Theory]
    [InlineData("dataset-grant", "STUDIO_NOT_FOUND")]
    [InlineData("dataset-function", "STUDIO_FORBIDDEN")]
    [InlineData("source-disabled", "SOURCE_CHANGED")]
    [InlineData("source-version", "SOURCE_CHANGED")]
    [InlineData("source-registry", "SOURCE_CHANGED")]
    [InlineData("root-grant", "STUDIO_NOT_FOUND")]
    [InlineData("target-grant", "STUDIO_NOT_FOUND")]
    public async Task Navigation_still_checks_all_dependencies_and_observes_permission_or_source_changes_on_each_request(string change, string code)
    {
        await using var harness = await DashboardHarness.CreateAsync();
        await harness.PublishDashboardAsync("guarded-target", TargetDashboard("target"));
        var revision = await harness.PublishDashboardAsync("guarded-source", Dashboard(harness, Components(Button("open")),
            new JsonArray(Navigation("open", "open", "guarded-target", "mode", Binding("Constant", value: "fixed")))));
        var principal = harness.Fixture.Principal;
        Task<JsonObject> Resolve() => harness.Navigation.ResolveAsync(principal, "guarded-source", new(revision.NId, "open", null, new()), CancellationToken.None);
        await Resolve();
        if (change.EndsWith("-grant", StringComparison.Ordinal))
        {
            var nId = change == "dataset-grant" ? harness.Dataset.NId : change == "root-grant" ? "guarded-source" : "guarded-target";
            var resource = (await harness.Fixture.Store.FindResourceAsync(principal.TenantNId, nId, CancellationToken.None))!;
            var grants = (await harness.Fixture.Store.GetGrantsAsync(principal.TenantNId, resource.Id, CancellationToken.None)).Where(grant => grant.Capability != "Run").ToArray();
            await harness.Fixture.Store.ReplaceGrantsAsync(principal.TenantNId, resource.Id, grants, CancellationToken.None);
        }
        else if (change == "dataset-function") principal = principal with { Permissions = principal.Permissions.Where(permission => permission != "studio.dataset.run").ToArray() };
        else if (change == "source-disabled") await harness.Fixture.Database.Client.Updateable<StudioSource>().SetColumns(source => new StudioSource { IsEnabled = false }).Where(source => source.NId == harness.Source.NId).ExecuteCommandAsync();
        else if (change == "source-version") await harness.Fixture.Database.Client.Updateable<StudioSource>().SetColumns(source => new StudioSource { ConfigurationVersion = source.ConfigurationVersion + 1 }).Where(source => source.NId == harness.Source.NId).ExecuteCommandAsync();
        else harness.Fixture.Profile.SourceRegistry["dashboard-fixture"].Operations["dashboard-rows"].Path = "changed-operation";
        var exception = await Assert.ThrowsAsync<StudioException>(Resolve);
        Assert.Equal(code, exception.Code);
        Assert.Equal(0, harness.Fixture.SourceExecutor.Calls); // Even unused dependencies are authorized and validated before navigation.
    }

    private static JsonObject Dashboard(
        DashboardHarness harness,
        JsonArray components,
        JsonArray? navigation = null,
        JsonArray? parameters = null,
        JsonArray? instances = null) => new()
    {
        ["schemaVersion"] = 1,
        ["canvas"] = new JsonObject { ["width"] = 1920, ["height"] = 1080, ["background"] = "#101820", ["fit"] = "Contain" },
        ["parameters"] = parameters ?? new JsonArray(),
        ["instances"] = instances ?? new JsonArray(Instance(harness, "lineData", Binding("Constant", value: "L01"))),
        ["components"] = components,
        ["navigation"] = navigation ?? new JsonArray(),
    };

    private static JsonObject TargetDashboard(string label) => new()
    {
        ["schemaVersion"] = 1,
        ["canvas"] = new JsonObject { ["width"] = 1920, ["height"] = 1080, ["background"] = "#101820", ["fit"] = "Contain" },
        ["parameters"] = new JsonArray(),
        ["instances"] = new JsonArray(),
        ["components"] = new JsonArray(Text("title", label)),
        ["navigation"] = new JsonArray(),
    };

    private static JsonObject LinkedDashboard(DashboardHarness harness, string targetNId) => Dashboard(
        harness,
        Components(Button("link")),
        new JsonArray(Navigation("link", "link", targetNId, "mode", Binding("Constant", value: "fixed"))),
        instances: new JsonArray());

    private static JsonArray Components(params JsonObject[] components) => new(components);

    private static JsonObject Chart(string id) => Component(
        id,
        "Chart",
        new JsonObject
        {
            ["kind"] = "Line",
            ["xField"] = "lineCode",
            ["series"] = new JsonArray(new JsonObject { ["name"] = "值", ["valueField"] = "value" }),
            ["legend"] = true,
            ["unit"] = "个",
        },
        new JsonArray(new JsonObject { ["property"] = "rows", ["source"] = Binding("Instance", "rows", instanceId: "lineData") }));

    private static JsonObject Table(string id) => Component(
        id,
        "Table",
        new JsonObject
        {
            ["presentation"] = "Table",
            ["rowKeyPath"] = new JsonArray("lineCode"),
            ["columns"] = new JsonArray(new JsonObject { ["field"] = "lineCode", ["label"] = "产线", ["widthPx"] = 120 }),
            ["scrollMode"] = "None",
        },
        new JsonArray(new JsonObject { ["property"] = "rows", ["source"] = Binding("Instance", "rows", instanceId: "lineData") }),
        ["line-click"]);

    private static JsonObject Tank(string id) => Component(
        id,
        "Tank",
        new JsonObject { ["shape"] = "Cylinder", ["min"] = 0, ["max"] = 100, ["unit"] = "%", ["label"] = "液位" },
        new JsonArray(new JsonObject { ["property"] = "level", ["source"] = Binding("Instance", "rows", "0", "value", instanceId: "lineData") }));

    private static JsonObject Button(string id) => Component(id, "Button", new JsonObject { ["text"] = id }, events: [id]);

    private static JsonObject Text(string id, string text) => Component(id, "Text", new JsonObject { ["text"] = text });

    private static JsonObject Component(string id, string type, JsonObject props, JsonArray? bindings = null, string[]? events = null) => new()
    {
        ["id"] = id,
        ["type"] = type,
        ["registryVersion"] = "1",
        ["name"] = id,
        ["x"] = 0,
        ["y"] = 0,
        ["width"] = 320,
        ["height"] = 180,
        ["rotation"] = 0,
        ["zIndex"] = 0,
        ["locked"] = false,
        ["visible"] = true,
        ["props"] = props,
        ["bindings"] = bindings ?? new JsonArray(),
        ["events"] = events is null ? new JsonArray() : new JsonArray(events.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray()),
    };

    private static JsonObject Navigation(
        string id,
        string componentId,
        string targetNId,
        string targetParameter,
        JsonObject source,
        string eventName = "Click",
        JsonObject? enabledWhen = null) => new()
    {
        ["id"] = id,
        ["componentId"] = componentId,
        ["event"] = eventName,
        ["target"] = new JsonObject { ["kind"] = "Dashboard", ["resourceNId"] = targetNId },
        ["parameterMap"] = new JsonArray(new JsonObject { ["target"] = targetParameter, ["source"] = source }),
        ["enabledWhen"] = enabledWhen,
        ["openMode"] = "Current",
    };

    private static JsonObject Instance(DashboardHarness harness, string instanceId, JsonObject source) => new()
    {
        ["instanceId"] = instanceId,
        ["datasetNId"] = harness.Dataset.NId,
        ["revisionNId"] = harness.DatasetRevision.NId,
        ["parameters"] = new JsonArray(new JsonObject { ["target"] = "lineCode", ["source"] = source }),
        ["refreshSeconds"] = 5,
        ["staleAfterSeconds"] = 15,
        ["dependsOn"] = new JsonArray(),
    };

    private static JsonObject Binding(string from, params string[] path) => Binding(from, path, null, null);

    private static JsonObject Binding(string from, string[] path, JsonNode? value, string? instanceId)
    {
        var result = new JsonObject { ["from"] = from, ["nullPolicy"] = "Error" };
        if (from == "Constant") result["value"] = value?.DeepClone();
        else
        {
            result["path"] = new JsonArray(path.Select(segment => (JsonNode)JsonValue.Create(segment)!).ToArray());
            if (from == "Instance") result["instanceId"] = instanceId;
        }
        return result;
    }

    private static JsonObject Binding(string from, object? value)
    {
        var node = value switch
        {
            JsonNode jsonNode => jsonNode.DeepClone(),
            string text => JsonValue.Create(text),
            bool flag => JsonValue.Create(flag),
            null => null,
            _ => JsonValue.Create(value),
        };
        return new JsonObject { ["from"] = from, ["value"] = node, ["nullPolicy"] = "Error" };
    }

    private static JsonObject Binding(string from, string path, string? secondPath = null, string? thirdPath = null, string? fourthPath = null, string? instanceId = null) =>
        Binding(from, new[] { path }.Concat(new[] { secondPath, thirdPath, fourthPath }.Where(value => value is not null).Select(value => value!)).ToArray(), null, instanceId);

    private static JsonObject Binding(string from, string path, JsonNode value) => Binding(from, new[] { path }, value, null);

    private static JsonObject Data(string? sourceObservedOn = null, bool includeSourceObservedOn = true)
    {
        var rows = new JsonArray(Row("L01", "1.0"), Row("L02", "2.0"));
        var result = new JsonObject { ["rows"] = rows };
        if (includeSourceObservedOn) result["sourceObservedOn"] = sourceObservedOn ?? DateTimeOffset.UtcNow.ToString("O");
        return result;
    }

    private static JsonObject Row(string lineCode, string value) => new()
    {
        ["lineCode"] = lineCode,
        ["backupLineCode"] = "backup-" + lineCode,
        ["factory"] = "factory-a",
        ["value"] = value,
    };

    private sealed class DashboardHarness : IAsyncDisposable
    {
        private DashboardHarness(StudioTestContext fixture, SourceDto source, ResourceDto dataset, RevisionDto datasetRevision, DashboardNavigation navigation)
        {
            Fixture = fixture;
            Source = source;
            Dataset = dataset;
            DatasetRevision = datasetRevision;
            Navigation = navigation;
        }

        public StudioTestContext Fixture { get; }
        public SourceDto Source { get; }
        public ResourceDto Dataset { get; }
        public RevisionDto DatasetRevision { get; }
        public DashboardNavigation Navigation { get; }

        public static async Task<DashboardHarness> CreateAsync(int cacheSeconds = 0)
        {
            var fixture = await StudioTestContext.CreateAsync();
            try
            {
                fixture.Profile.InstalledModules = ["LowCode", "Dashboard"];
                fixture.SourceExecutor.Data = Data();
                fixture.Profile.SourceRegistry["dashboard-fixture"] = new StudioSourceEndpoint
                {
                    Provider = "RegisteredApi",
                    ReadOnlyVerified = true,
                    Operations = new() { ["dashboard-rows"] = new() { Path = "dashboard-rows", Method = "GET", ReadOnly = true } },
                };
                var administration = new SourceAdministration(fixture.Store, fixture.Authorization, fixture.Commands, fixture.SourceExecutor, fixture.Profile, new(), TimeProvider.System);
                var source = await administration.CreateAsync(
                    fixture.Principal,
                    new("dashboard-source", "看板测试来源", null, "RegisteredApi", "dashboard-fixture", null, new JsonArray(new JsonObject { ["kind"] = "ApiOperation", ["qualifiedName"] = "dashboard-rows" })),
                    "create-dashboard-source",
                    CancellationToken.None);
                await administration.TestAsync(fixture.Principal, source.NId, new(source.OptimisticVersion, source.ConcurrencyVersion), CancellationToken.None);
                source = await administration.SetEnabledAsync(fixture.Principal, source.NId, true, new(source.OptimisticVersion, source.ConcurrencyVersion), CancellationToken.None);

                var datasetDocument = DatasetDocument(source.NId, source.ConfigurationVersion);
                datasetDocument["cacheSeconds"] = cacheSeconds;
                var dataset = await fixture.Publishing.CreateAsync(fixture.Principal, new("dashboard-data", "Dataset", "看板数据", null, null, datasetDocument), "create-dashboard-data", CancellationToken.None);
                var datasetPlan = await fixture.Publishing.ValidateAsync(fixture.Principal, dataset.NId, datasetDocument, CancellationToken.None);
                Assert.True(datasetPlan.Valid, string.Join(";", datasetPlan.Diagnostics.Select(diagnostic => diagnostic.Path + ":" + diagnostic.Message)));
                var datasetRevision = await fixture.Publishing.PublishAsync(
                    fixture.Principal,
                    dataset.NId,
                    new(dataset.OptimisticVersion, dataset.ConcurrencyVersion, "dashboard fixture", true, datasetPlan.DependencyPlanHash),
                    "publish-dashboard-data",
                    CancellationToken.None);
                var datasets = new DatasetExecution(fixture.Store, fixture.Publishing, fixture.Authorization, fixture.SourceExecutor, new(), TimeProvider.System);
                return new(fixture, source, dataset, datasetRevision, new DashboardNavigation(fixture.Store, fixture.Publishing, datasets, fixture.Authorization));
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public async Task<RevisionDto> PublishDashboardAsync(string nId, JsonObject document)
        {
            var existing = await Fixture.Store.FindResourceAsync(Fixture.Principal.TenantNId, nId, CancellationToken.None);
            ResourceDto resource;
            if (existing is null)
            {
                resource = await Fixture.Publishing.CreateAsync(Fixture.Principal, new(nId, "Dashboard", nId, null, null, document), "create-" + nId, CancellationToken.None);
            }
            else
            {
                var draft = await Fixture.Publishing.GetDraftAsync(Fixture.Principal, nId, CancellationToken.None);
                var saved = await Fixture.Publishing.SaveDraftAsync(
                    Fixture.Principal,
                    nId,
                    new(
                        draft["optimisticVersion"]!.GetValue<string>(),
                        draft["concurrencyVersion"]!.GetValue<Guid>(),
                        nId,
                        null,
                        null,
                        document),
                    CancellationToken.None);
                resource = new(
                    nId,
                    "Dashboard",
                    nId,
                    null,
                    null,
                    saved["isEnabled"]?.GetValue<bool>() ?? false,
                    saved["publishedRevisionNId"]?.GetValue<string>(),
                    saved["publishedRevisionNo"]?.GetValue<string>(),
                    saved["hasDraftChanges"]?.GetValue<bool>() ?? true,
                    Fixture.Principal.SubjectNId,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    saved["optimisticVersion"]!.GetValue<string>(),
                    saved["concurrencyVersion"]!.GetValue<Guid>());
            }

            var plan = await Fixture.Publishing.ValidateAsync(Fixture.Principal, nId, document, CancellationToken.None);
            Assert.True(plan.Valid, string.Join(";", plan.Diagnostics.Select(diagnostic => diagnostic.Path + ":" + diagnostic.Message)));
            return await Fixture.Publishing.PublishAsync(
                Fixture.Principal,
                nId,
                new(resource.OptimisticVersion, resource.ConcurrencyVersion, "dashboard regression", true, plan.DependencyPlanHash),
                "publish-" + nId + "-" + Guid.NewGuid().ToString("N"),
                CancellationToken.None);
        }

        public ValueTask DisposeAsync() => Fixture.DisposeAsync();
    }

    private static JsonObject DatasetDocument(string sourceNId, string sourceVersion) => new()
    {
        ["schemaVersion"] = 1,
        ["sourceNId"] = sourceNId,
        ["sourceConfigurationVersion"] = sourceVersion,
        ["query"] = new JsonObject { ["kind"] = "Api", ["operationKey"] = "dashboard-rows", ["method"] = "GET" },
        ["parameters"] = new JsonArray(new JsonObject { ["name"] = "lineCode", ["label"] = "产线", ["type"] = "String", ["required"] = true, ["nullable"] = false, ["source"] = "Caller" }),
        ["result"] = new JsonObject
        {
            ["id"] = "root",
            ["name"] = "payload",
            ["type"] = "Object",
            ["nullable"] = false,
            ["children"] = new JsonArray(
                new JsonObject
                {
                    ["id"] = "rows",
                    ["name"] = "rows",
                    ["type"] = "Array",
                    ["nullable"] = false,
                    ["item"] = new JsonObject
                    {
                        ["id"] = "row",
                        ["name"] = "row",
                        ["type"] = "Object",
                        ["nullable"] = false,
                        ["children"] = new JsonArray(
                            new JsonObject { ["id"] = "lineCode", ["name"] = "lineCode", ["type"] = "String", ["nullable"] = false },
                            new JsonObject { ["id"] = "backupLineCode", ["name"] = "backupLineCode", ["type"] = "String", ["nullable"] = false },
                            new JsonObject { ["id"] = "factory", ["name"] = "factory", ["type"] = "String", ["nullable"] = false },
                            new JsonObject { ["id"] = "value", ["name"] = "value", ["type"] = "Decimal", ["nullable"] = false }),
                    },
                },
                new JsonObject { ["id"] = "sourceObservedOn", ["name"] = "sourceObservedOn", ["type"] = "String", ["nullable"] = true }),
        },
        ["rowPolicyKey"] = "authorized-factories",
        ["readableFieldIds"] = new JsonArray("lineCode", "backupLineCode", "factory", "value", "sourceObservedOn"),
        ["timeoutSeconds"] = 10,
        ["maxRows"] = 100,
        ["maxBytes"] = "1048576",
        ["cacheSeconds"] = 0,
        ["freshness"] = new JsonObject { ["mode"] = "SourceTime", ["timePath"] = "sourceObservedOn", ["maxAgeSeconds"] = 60 },
    };
}
