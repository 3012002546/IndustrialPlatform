using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Data;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Contracts;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Application.Report;

public sealed record ReportDraftDependency(string ResourceNId, string OptimisticVersion, Guid ConcurrencyVersion);
public sealed record ReportPreviewInput(string Kind, string? ResourceNId, JsonObject Document, JsonObject? Parameters, string? Orientation = null, ReportDraftDependency[]? DraftDependencies = null);
public sealed record ReportQueryInput(string? RevisionNId, JsonObject? Parameters, int Page = 1, int PageSize = 20, JsonArray? Sort = null, JsonObject? Document = null);

/// <summary>Authorizes and freezes design input before any source execution; production only reads immutable published definitions.</summary>
public sealed class ReportDesign(IStudioResourceStore store, IStudioAuthorization authorization, StudioPublishing publishing, DatasetExecution datasets)
{
    private static JsonObject Trusted(StudioPrincipal p) => new() { ["tenant"] = p.TenantNId, ["user"] = p.SubjectNId, ["authorizedFactoryIds"] = new JsonArray(p.FactoryNIds.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()) };
    public async Task<(ReportDefinitionSnapshot Definition, JsonObject Parameters, JsonObject Frozen)> FreezeAsync(StudioPrincipal principal, ReportPreviewInput input, CancellationToken token)
    {
        authorization.RequirePermission(principal, "studio.report.design");
        if (input.Kind is not ("Report" or "ReportBundle")) throw new StudioException("STUDIO_VALIDATION", "预览必须是报告或组合。", path: "kind");
        var document = input.Document.DeepClone().AsObject();
        if (input.Orientation is { } orientation && document["paper"] is JsonObject paper) paper["orientation"] = orientation;
        var resource = input.ResourceNId is { } rootNId ? await publishing.RequireResourceAsync(principal, rootNId, "design", "Design", token) : new StudioResource { Kind = input.Kind, NId = "unsaved", TenantNId = principal.TenantNId };
        if (resource.Kind != input.Kind) throw new StudioException("TYPE_MISMATCH", "资源类型不匹配。");
        var explicitDrafts = new Dictionary<string, (StudioResource Resource, JsonObject Document)>();
        foreach (var requested in input.DraftDependencies ?? [])
        {
            if (explicitDrafts.Count >= 200 || explicitDrafts.ContainsKey(requested.ResourceNId) || requested.ResourceNId == input.ResourceNId) throw new StudioException("STUDIO_VALIDATION", "子草稿清单重复或超限。");
            var child = await publishing.RequireResourceAsync(principal, requested.ResourceNId, "design", "Design", token);
            if (child.Kind != "Report") throw new StudioException("TYPE_MISMATCH", "只能显式选择单报表子草稿。");
            StudioPublishing.MatchVersion(child, new VersionCommand(requested.OptimisticVersion, requested.ConcurrencyVersion));
            explicitDrafts[child.NId] = (child, StudioPublishing.Parse(child.DraftJson));
        }
        // Bind explicit saved child drafts to temporary immutable keys within this snapshot, never publish them.
        var draftKeys = new Dictionary<string, string>();
        var childItems = input.Kind == "ReportBundle" ? (StudioJson.Text(document["mode"]) == "Simple" ? document["items"] as JsonArray : document["assembly"]?["rules"] as JsonArray) ?? [] : new JsonArray();
        var draftSnapshots = new JsonArray();
        foreach (var item in childItems.OfType<JsonObject>())
        {
            var nId = StudioJson.RequiredString(item, "reportNId");
            if (!explicitDrafts.TryGetValue(nId, out var draft)) continue;
            if (!draftKeys.TryGetValue(nId, out var key))
            {
                var plan = await publishing.PlanAsync(principal, draft.Resource, draft.Document, token); RequireValid(plan);
                key = "draft-" + draft.Resource.OptimisticVersion.ToString(CultureInfo.InvariantCulture) + "-" + draft.Resource.ConcurrencyVersion.ToString("N");
                draftKeys[nId] = key;
                draftSnapshots.Add(new JsonObject { ["resourceNId"] = nId, ["revisionNId"] = key, ["optimisticVersion"] = draft.Resource.OptimisticVersion.ToString(CultureInfo.InvariantCulture), ["concurrencyVersion"] = draft.Resource.ConcurrencyVersion, ["document"] = draft.Document.DeepClone(), ["dependencies"] = plan.DependencyPlan.DeepClone(), ["sourcePins"] = plan.SourcePins.DeepClone() });
            }
            item["revisionNId"] = key;
        }
        // The publisher validates all released references. Each explicit draft was validated once above.
        StudioValidation rootPlan;
        if (draftKeys.Count == 0) { rootPlan = await publishing.PlanAsync(principal, resource, document, token); RequireValid(rootPlan); }
        else
        {
            var validation = StudioDocuments.Validate(input.Kind, document);
            if (validation.FirstOrDefault(d => d.Severity == "Error") is { } d) throw new StudioException(d.Code, d.Message, 422, d.Path);
            var safeRoot = document.DeepClone().AsObject();
            var safeItems = StudioJson.Text(safeRoot["mode"]) == "Simple" ? safeRoot["items"]!.AsArray() : safeRoot["assembly"]!["rules"]!.AsArray();
            foreach (var item in safeItems.OfType<JsonObject>().Where(i => draftKeys.ContainsKey(StudioJson.RequiredString(i, "reportNId"))).ToArray()) safeItems.Remove(item);
            // Validate the remaining references through the same publisher; empty list diagnostic belongs only to this reduced planning projection.
            rootPlan = await publishing.PlanAsync(principal, resource, safeRoot, token);
            var errors = rootPlan.Diagnostics.Where(d => d.Severity == "Error" && d.Path is not ("items" or "assembly.rules")).ToArray();
            if (errors.FirstOrDefault() is { } e) throw new StudioException(e.Code, e.Message, 422, e.Path);
            var resolvedItems = StudioJson.Text(safeRoot["mode"]) == "Simple" ? safeRoot["items"]!.AsArray() : safeRoot["assembly"]!["rules"]!.AsArray();
            foreach (var original in childItems.OfType<JsonObject>()) if (!draftKeys.ContainsKey(StudioJson.RequiredString(original, "reportNId"))) original["revisionNId"] = resolvedItems.OfType<JsonObject>().Single(i => StudioJson.Text(i["id"]) == StudioJson.Text(original["id"]))["revisionNId"]?.DeepClone();
        }
        var dependencies = new Dictionary<string, ReportDefinitionSnapshot>(StringComparer.Ordinal); var frozenDependencies = new JsonArray();
        async Task AddReleased(JsonArray references)
        {
            foreach (var reference in references.OfType<JsonObject>())
            {
                var nId = StudioJson.RequiredString(reference, "resourceNId"); var revisionNId = StudioJson.RequiredString(reference, "revisionNId"); var key = nId + "/" + revisionNId;
                if (dependencies.ContainsKey(key)) continue;
                var child = await publishing.RequireResourceAsync(principal, nId, "view", "View", token);
                var revision = await store.FindRevisionAsync(principal.TenantNId, revisionNId, token) ?? throw new StudioException("DEPENDENCY_CHANGED", "固定依赖修订不存在。", 409);
                if (revision.ResourceId != child.Id || child.Kind is not ("Report" or "Dataset")) throw new StudioException("DEPENDENCY_CHANGED", "固定依赖类型不匹配。", 409);
                dependencies[key] = new(child.Kind, StudioPublishing.Parse(revision.DocumentJson), new Dictionary<string, ReportDefinitionSnapshot>(), revision.ContentHash, child.NId);
                if (child.Kind == "Report") await authorization.RequireAsync(principal, child, "studio.report.design", "Design", token);
                frozenDependencies.Add(new JsonObject { ["resourceNId"] = nId, ["revisionNId"] = revisionNId, ["kind"] = child.Kind, ["document"] = JsonNode.Parse(revision.DocumentJson), ["contentHash"] = revision.ContentHash });
            }
        }
        await AddReleased(rootPlan.DependencyPlan);
        foreach (var snapshot in draftSnapshots.OfType<JsonObject>())
        {
            var nId = StudioJson.RequiredString(snapshot, "resourceNId"); var key = nId + "/" + StudioJson.RequiredString(snapshot, "revisionNId");
            dependencies[key] = new("Report", snapshot["document"]!.AsObject(), new Dictionary<string, ReportDefinitionSnapshot>(), StudioJson.Hash(StudioJson.Canonicalize(snapshot["document"])), nId);
            await AddReleased(snapshot["dependencies"]!.AsArray()); frozenDependencies.Add(snapshot.DeepClone());
        }
        foreach (var draft in explicitDrafts.Values)
        {
            var current = await publishing.RequireResourceAsync(principal, draft.Resource.NId, "design", "Design", token);
            if (current.OptimisticVersion != draft.Resource.OptimisticVersion || current.ConcurrencyVersion != draft.Resource.ConcurrencyVersion) throw new StudioException("STUDIO_VERSION_CONFLICT", "子草稿在冻结时发生变化。", 409);
        }
        var parameters = DatasetValues.Parameters(principal, document, input.Parameters ?? new());
        var frozen = new JsonObject { ["kind"] = input.Kind, ["resourceNId"] = input.ResourceNId, ["document"] = document.DeepClone(), ["dependencySnapshots"] = frozenDependencies, ["sourcePins"] = rootPlan.SourcePins.DeepClone() };
        return (new(input.Kind, document, dependencies, StudioJson.Hash(StudioJson.Canonicalize(frozen)), input.ResourceNId), parameters, frozen);
    }
    private static void RequireValid(StudioValidation plan) { if (plan.Diagnostics.FirstOrDefault(d => d.Severity == "Error") is { } e) throw new StudioException(e.Code, e.Message, 422, e.Path); }
    public async Task AuthorizeFrozenAsync(StudioPrincipal principal, JsonObject frozen, CancellationToken token)
    {
        authorization.RequirePermission(principal, "studio.report.design");
        if (StudioJson.Text(frozen["resourceNId"]) is { } root) _ = await publishing.RequireResourceAsync(principal, root, "design", "Design", token);
        foreach (var child in (frozen["dependencySnapshots"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var dataset = StudioJson.Text(child["kind"]) == "Dataset";
            _ = await publishing.RequireResourceAsync(principal, StudioJson.RequiredString(child, "resourceNId"), dataset ? "run" : "design", dataset ? "Run" : "Design", token);
        }
    }
    public async Task<object> RuntimeAsync(StudioPrincipal principal, string nId, string? revisionNId, CancellationToken token)
    {
        var (resource, revision) = await publishing.RequireRuntimeAsync(principal, nId, revisionNId, token);
        if (resource.Kind is not ("Report" or "ReportBundle")) throw new StudioException("STUDIO_NOT_FOUND", "报告不存在。", 404);
        var document = StudioPublishing.Parse(revision.DocumentJson);
        // The runtime projection includes query columns and parameter forms, never source definitions or dataset SQL.
        var projection = new JsonObject { ["mode"] = document["mode"]?.DeepClone(), ["parameters"] = document["parameters"]?.DeepClone(), ["query"] = document["query"]?.DeepClone(), ["bands"] = document["bands"]?.DeepClone() };
        return new { resource = await publishing.ToDtoAsync(resource, revision, token), revisionNId = revision.NId, document = projection };
    }
    public async Task<JsonObject> PlanAsync(StudioPrincipal principal, ReportPreviewInput input, CancellationToken token)
    {
        var (definition, parameters, _) = await FreezeAsync(principal, input, token);
        var resolver = Resolver(principal); var plan = await new ReportPlanner(resolver, Trusted(principal), (d, p) => DatasetValues.Parameters(principal, d, p)).PlanAsync(definition, parameters, token);
        return new() { ["instances"] = new JsonArray(plan.Instances.Select(i => (JsonNode)new JsonObject { ["instanceKey"] = i.InstanceKey, ["ruleId"] = i.RuleId, ["workOrderKey"] = i.WorkOrderKey, ["operationKey"] = i.OperationKey, ["sourceRowKey"] = i.SourceRowKey, ["reportNId"] = i.ReportNId, ["entryBandId"] = i.EntryBandId, ["rowCount"] = i.RowCount, ["resolvedMappings"] = i.ResolvedMappings.DeepClone(), ["decision"] = i.Decision, ["reason"] = i.Reason }).ToArray()), ["diagnostics"] = new JsonArray(), ["sourceManifest"] = resolver.Manifest, ["sample"] = true };
    }
    public ReportDataResolver Resolver(StudioPrincipal principal) => new(async (revisionNId, parameters, token) =>
    {
        var revision = await store.FindRevisionAsync(principal.TenantNId, revisionNId, token) ?? throw new StudioException("DEPENDENCY_CHANGED", "数据集修订不存在。", 409);
        var resource = await store.FindResourceByIdAsync(principal.TenantNId, revision.ResourceId, token) ?? throw new StudioException("DEPENDENCY_CHANGED", "数据集不存在。", 409);
        if (resource.Kind != "Dataset") throw new StudioException("TYPE_MISMATCH", "来源必须是数据集。");
        var doc = StudioPublishing.Parse(revision.DocumentJson);
        var first = await datasets.ExecuteAsync(principal, resource.NId, new DatasetExecute(revisionNId, parameters, 1, null), token, bypassCache: true);
        if (doc["pagination"] is not JsonObject pagination) return first;
        var path = StudioJson.RequiredString(pagination["result"]!.AsObject(), "rowsPath");
        var rows = StudioJson.ReadPath(first["data"], path) as JsonArray ?? throw new StudioException("TYPE_MISMATCH", "分页来源集合不匹配。", 422);
        var all = rows.DeepClone().AsArray(); var page = 1; var total = StudioJson.Text(first["pageInfo"]?["totalCount"]); var seen = new HashSet<string>();
        while (StudioJson.Text(pagination["mode"]) == "Offset" ? all.Count < StudioJson.Version(total) : first["pageInfo"]?["nextCursor"] is { } c && StudioJson.Text(c) is { Length: > 0 })
        {
            if (++page > 200 || all.Count > 100000) throw new StudioException("STUDIO_LIMIT", "分页来源超过报告预算。", 429);
            if (StudioJson.Text(pagination["mode"]) == "Cursor")
            {
                var cursor = first["pageInfo"]!["nextCursor"]!;
                if (!seen.Add(cursor.ToJsonString())) throw new StudioException("SOURCE_FAILED", "来源游标循环。", 502);
                parameters[StudioJson.RequiredString(pagination["input"]!.AsObject(), "cursorParameter")] = cursor.DeepClone();
            }
            first = await datasets.ExecuteAsync(principal, resource.NId, new DatasetExecute(revisionNId, parameters, page, null), token, bypassCache: true);
            if (StudioJson.Text(pagination["mode"]) == "Offset" && StudioJson.Text(first["pageInfo"]?["totalCount"]) != total) throw new StudioException("SOURCE_CHANGED", "分页采集期间来源总数发生变化。", 409);
            var batch = StudioJson.ReadPath(first["data"], path) as JsonArray ?? throw new StudioException("TYPE_MISMATCH", "分页来源集合不匹配。", 422);
            if (batch.Count == 0) throw new StudioException("SOURCE_FAILED", "来源分页提前结束。", 502);
            foreach (var row in batch) all.Add(row?.DeepClone());
            if (Encoding.UTF8.GetByteCount(all.ToJsonString()) > 50 * 1024 * 1024) throw new StudioException("STUDIO_LIMIT", "分页采集超过50MiB。", 429);
        }
        if (path is "" or "$") first["data"] = all;
        else
        {
            var parts = path.TrimStart('$', '.').Split('.'); var parent = first["data"]!;
            foreach (var part in parts[..^1]) parent = parent[part]!;
            parent[parts[^1]] = all;
        }
        first["consistency"] = "ObservedAtDifferentTimes";
        return first;
    });
    public async Task<JsonObject> BindingContractAsync(StudioPrincipal principal, string nId, string revisionNId, CancellationToken token)
    {
        var revision = await publishing.GetRevisionDesignAsync(principal, nId, revisionNId, token);
        var schemas = new Dictionary<string, JsonObject>();
        foreach (var dependency in revision.Dependencies.OfType<JsonObject>())
        {
            var child = await store.FindRevisionAsync(principal.TenantNId, StudioJson.RequiredString(dependency, "revisionNId"), token);
            if (child is not null && StudioPublishing.Parse(child.DocumentJson)["result"] is JsonObject schema) schemas[child.NId] = schema;
        }
        var diagnostics = new List<StudioDiagnostic>();
        var contract = SchemaContract(revision.Document, schemas, diagnostics); contract["revisionNId"] = revisionNId; return contract;
    }
    public async Task<JsonObject> DraftBindingContractAsync(StudioPrincipal principal, ReportPreviewInput input, CancellationToken token)
    {
        authorization.RequirePermission(principal, "studio.report.design");
        if (input.Kind != "Report") throw new StudioException("TYPE_MISMATCH", "字段合同仅接受单报表。", 422);
        var resource = input.ResourceNId is { } id ? await publishing.RequireResourceAsync(principal, id, "design", "Design", token) : new StudioResource { Kind = "Report", NId = "unsaved", TenantNId = principal.TenantNId };
        if (resource.Kind != "Report") throw new StudioException("TYPE_MISMATCH", "资源类型不匹配。", 422);
        var plan = await publishing.PlanAsync(principal, resource, input.Document.DeepClone().AsObject(), token);
        var schemas = new Dictionary<string, JsonObject>();
        foreach (var dependency in plan.DependencyPlan.OfType<JsonObject>())
        {
            var revision = await store.FindRevisionAsync(principal.TenantNId, StudioJson.RequiredString(dependency, "revisionNId"), token);
            if (revision is not null && StudioPublishing.Parse(revision.DocumentJson)["result"] is JsonObject schema) schemas[revision.NId] = schema;
        }
        return SchemaContract(input.Document, schemas, plan.Diagnostics.ToList());
    }
    private static JsonObject SchemaContract(JsonObject document, IReadOnlyDictionary<string, JsonObject> datasets, List<StudioDiagnostic> diagnostics)
    {
        var instances = new JsonObject();
        foreach (var instance in (document["instances"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var id = StudioJson.RequiredString(instance, "instanceId");
            var schema = StudioJson.Text(instance["sourceType"]) == "Constant" ? StudioReportContracts.Infer(instance["value"]) : datasets.GetValueOrDefault(StudioJson.Text(instance["revisionNId"]) ?? "");
            if (schema is not null) instances[id] = schema.DeepClone();
        }
        var valid = diagnostics.All(diagnostic => diagnostic.Severity != "Error");
        var bands = valid ? StudioReportContracts.BandSchemas(document, datasets, diagnostics) : new Dictionary<string, JsonObject>();
        return new() { ["parameters"] = document["parameters"]?.DeepClone(), ["entryBands"] = valid ? StudioReportContracts.EntryBands(document, datasets, diagnostics) : new JsonArray(), ["instanceSchemas"] = instances, ["datasetSchemas"] = new JsonObject(datasets.Select(pair => KeyValuePair.Create<string, JsonNode?>(pair.Key, pair.Value.DeepClone()))), ["bandSchemas"] = new JsonObject(bands.Select(pair => KeyValuePair.Create<string, JsonNode?>(pair.Key, pair.Value.DeepClone()))), ["diagnostics"] = System.Text.Json.JsonSerializer.SerializeToNode(diagnostics, StudioCommands.JsonOptions) };
    }
    public async Task<JsonObject> QueryAsync(StudioPrincipal principal, string? nId, ReportQueryInput request, CancellationToken token)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > 200) throw new StudioException("STUDIO_VALIDATION", "查询分页无效。");
        JsonObject document;
        if (nId is null) { authorization.RequirePermission(principal, "studio.report.design"); document = request.Document ?? throw new StudioException("STUDIO_VALIDATION", "试查文档缺失。"); _ = await FreezeAsync(principal, new("Report", null, document, request.Parameters), token); }
        else { var (resource, revision) = await publishing.RequireRuntimeAsync(principal, nId, request.RevisionNId, token); if (resource.Kind != "Report") throw new StudioException("STUDIO_NOT_FOUND", "报告不存在。", 404); document = StudioPublishing.Parse(revision.DocumentJson); }
        if (StudioJson.Text(document["mode"]) != "Query" || document["query"] is not JsonObject query) throw new StudioException("TYPE_MISMATCH", "该报告不是查询报告。");
        var sort = ResolveSort(document, request.Sort);
        var parameters = DatasetValues.Parameters(principal, document, request.Parameters ?? new()); var resolver = Resolver(principal);
        var snapshot = await resolver.CaptureAsync(document, parameters, Trusted(principal), token); var context = ReportDataResolver.Context(parameters, snapshot.Instances, Trusted(principal));
        var band = (document["bands"] as JsonArray ?? []).OfType<JsonObject>().Single(b => StudioJson.Text(b["id"]) == StudioJson.Text(query["resultBandId"]));
        var rows = await resolver.CollectionAsync(band["data"]!.AsObject(), context, token);
        var columns = (band["controls"] as JsonArray ?? []).OfType<JsonObject>().Where(c => StudioJson.Text(c["type"]) == "Table").SelectMany(c => (c["table"]?["columns"] as JsonArray ?? []).OfType<JsonObject>().Select(column => (Control: c, Column: column))).ToDictionary(x => StudioJson.RequiredString(x.Control, "id") + "/" + StudioJson.RequiredString(x.Column, "id"));
        var allowed = (query["columns"] as JsonArray ?? []).OfType<JsonObject>().Where(c => c["visible"]?.GetValue<bool>() != false).ToArray();
        var output = new JsonArray();
        foreach (var row in rows)
        {
            var current = new Dictionary<string, JsonNode?>(context, StringComparer.Ordinal) { ["Row"] = row, ["Root"] = row };
            var projected = new JsonObject();
            foreach (var column in allowed)
            {
                var id = StudioJson.RequiredString(column, "columnId");
                if (!columns.TryGetValue(StudioJson.RequiredString(column, "controlId") + "/" + id, out var source) || StudioJson.Text(source.Control["id"]) != StudioJson.Text(column["controlId"])) throw new StudioException("PATH_INVALID", "查询列不在固定结果表中。", 422, "query.columns");
                projected[id] = ReportDataResolver.Resolve(ReportTableTemplates.Binding(source.Control["table"]!.AsObject(), source.Column), current);
            }
            projected["__rowKey"] = output.Count.ToString(CultureInfo.InvariantCulture) + ":" + StudioJson.Hash(StudioJson.Canonicalize(projected))[..16];
            output.Add(projected);
        }
        return new() { ["items"] = new JsonArray(ReportDataResolver.Sort(output, sort).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).Select(r => r?.DeepClone()).ToArray()), ["page"] = request.Page, ["pageSize"] = request.PageSize, ["totalCount"] = output.Count.ToString(CultureInfo.InvariantCulture), ["requestId"] = "query-" + Guid.NewGuid().ToString("N"), ["diagnostics"] = new JsonArray(), ["allResultsLoaded"] = true };
    }
    public async Task<JsonObject> QueryOptionsAsync(StudioPrincipal principal, string nId, ReportQueryInput request, CancellationToken token)
    {
        var (resource, revision) = await publishing.RequireRuntimeAsync(principal, nId, request.RevisionNId, token);
        var document = StudioPublishing.Parse(revision.DocumentJson);
        if (resource.Kind != "Report" || StudioJson.Text(document["mode"]) != "Query") throw new StudioException("TYPE_MISMATCH", "该资源不是查询报告。", 422);
        var definitions = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var resolver = new ReportDataResolver(async (fixedRevision, values, ct) =>
        {
            var dataset = await store.FindRevisionAsync(principal.TenantNId, fixedRevision, ct) ?? throw new StudioException("DEPENDENCY_CHANGED", "选项数据集修订缺失。", 409);
            var target = await store.FindResourceByIdAsync(principal.TenantNId, dataset.ResourceId, ct) ?? throw new StudioException("DEPENDENCY_CHANGED", "选项数据集缺失。", 409);
            if (target.Kind != "Dataset") throw new StudioException("TYPE_MISMATCH", "选项来源必须是数据集。", 422);
            var result = await datasets.ExecuteAsync(principal, target.NId, new(fixedRevision, values, 1, null), ct, bypassCache: true);
            if (result["data"] is not JsonArray rows || rows.Count > 1000 || result["truncated"]?.GetValue<bool>() == true || StudioJson.Text(result["pageInfo"]?["nextCursor"]) is { Length: > 0 } || long.TryParse(StudioJson.Text(result["pageInfo"]?["totalCount"]), out var total) && total > rows.Count) throw new StudioException("STUDIO_LIMIT", "选项来源必须完整返回最多1000行；未显示截断的选项。", 429);
            definitions[fixedRevision] = StudioPublishing.Parse(dataset.DocumentJson);
            return result;
        });
        var output = new JsonObject();
        foreach (var filter in (document["query"]?["filters"] as JsonArray ?? []).OfType<JsonObject>().Where(filter => StudioJson.Text(filter["control"]) is "Select" or "BusinessSelector"))
        {
            var fixedRevision = StudioJson.RequiredString(filter, "optionsDatasetRevisionNId");
            var dataset = await store.FindRevisionAsync(principal.TenantNId, fixedRevision, token) ?? throw new StudioException("DEPENDENCY_CHANGED", "选项修订不存在。", 409);
            var definition = StudioPublishing.Parse(dataset.DocumentJson);
            var values = new JsonObject(); foreach (var parameter in (definition["parameters"] as JsonArray ?? []).OfType<JsonObject>()) { var name = StudioJson.RequiredString(parameter, "name"); if (request.Parameters?.TryGetPropertyValue(name, out var value) == true) values[name] = value?.DeepClone(); }
            var rows = (await resolver.DatasetAsync(fixedRevision, values, token))!.AsArray();
            var item = definitions[fixedRevision]["result"]?["item"] as JsonObject ?? throw new StudioException("TYPE_MISMATCH", "选项数据集必须提供数组行Schema。", 422);
            JsonArray FieldPath(string id)
            {
                var matches = new List<JsonArray>();
                void Visit(JsonObject schema, JsonArray prefix) { foreach (var child in (schema["children"] as JsonArray ?? []).OfType<JsonObject>()) { var path = prefix.DeepClone().AsArray(); path.Add(StudioJson.RequiredString(child, "name")); if (StudioJson.Text(child["id"]) == id || !child.ContainsKey("id") && StudioJson.Text(child["name"]) == id) matches.Add(path); if (StudioJson.Text(child["type"]) == "Object") Visit(child, path); } }
                Visit(item, new()); return matches.Count == 1 ? matches[0] : throw new StudioException("PATH_INVALID", "选项值或显示字段不存在或不唯一。", 422);
            }
            var valuePath = FieldPath(StudioJson.RequiredString(filter, "valueFieldId")); var labelPath = FieldPath(StudioJson.RequiredString(filter, "labelFieldId"));
            output[StudioJson.RequiredString(filter, "parameterName")] = new JsonArray(rows.Select(row =>
            {
                var value = ReportDataResolver.Read(row, valuePath); var label = ReportDataResolver.Read(row, labelPath);
                if (value is not JsonValue || label is not JsonValue) throw new StudioException("TYPE_MISMATCH", "选项的值和显示内容必须是非空标量。", 422);
                return (JsonNode)new JsonObject { ["value"] = value.DeepClone(), ["label"] = StudioJson.Text(label) ?? label.ToJsonString() };
            }).ToArray());
        }
        return new() { ["options"] = output, ["sourceManifest"] = resolver.Manifest };
    }
    public static JsonObject ApplyQuerySort(JsonObject fixedDocument, JsonArray? requested)
    {
        var document = fixedDocument.DeepClone().AsObject();
        var sort = ResolveSort(document, requested);
        if (StudioJson.Text(document["mode"]) != "Query") return document;
        var query = document["query"]!.AsObject();
        var band = document["bands"]!.AsArray().OfType<JsonObject>().Single(b => StudioJson.Text(b["id"]) == StudioJson.Text(query["resultBandId"]));
        var resolved = new JsonArray();
        foreach (var field in sort.OfType<JsonObject>())
        {
            var id = StudioJson.RequiredString(field, "fieldId"); var direction = StudioJson.Text(field["direction"]) ?? "Asc"; var nulls = StudioJson.Text(field["nulls"]) ?? "Last";
            var column = (query["columns"] as JsonArray ?? []).OfType<JsonObject>().Single(c => StudioJson.Text(c["columnId"]) == id && c["visible"]?.GetValue<bool>() != false && c["sortable"]?.GetValue<bool>() == true);
            var table = band["controls"]!.AsArray().OfType<JsonObject>().Single(c => StudioJson.Text(c["id"]) == StudioJson.Text(column["controlId"]));
            var binding = ReportTableTemplates.Binding(table["table"]!.AsObject(), table["table"]!["columns"]!.AsArray().OfType<JsonObject>().Single(c => StudioJson.Text(c["id"]) == id));
            if (StudioJson.Text(binding["from"]) != "Row") throw new StudioException("REPORT_LAYOUT_UNSUPPORTED", "排序列必须绑定当前结果行。", 422, "sort");
            resolved.Add(new JsonObject { ["path"] = binding["path"]?.DeepClone() ?? new JsonArray(), ["direction"] = direction, ["nulls"] = nulls });
        }
        band["sort"] = resolved;
        return document;
    }
    // Query, preview and durable acceptance must preserve missing-vs-empty sort semantics.
    public static JsonArray ResolveSort(JsonObject document, JsonArray? requested)
    {
        if (StudioJson.Text(document["mode"]) == "Query") return QuerySort(document["query"]!.AsObject(), requested);
        if (requested?.Count > 0) throw new StudioException("STUDIO_VALIDATION", "固定报告不接受查询排序。", 422, "sort");
        return new();
    }
    private static JsonArray QuerySort(JsonObject query, JsonArray? requested)
    {
        // Missing sorting uses the document default; an explicit empty list clears it.
        var sort = requested ?? query["defaultSort"] as JsonArray ?? [];
        if (sort.Count > 20 || sort.Any(item => item is not JsonObject)) throw new StudioException("STUDIO_VALIDATION", "排序定义无效。", 422, "sort");
        var normalized = new JsonArray(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in sort.OfType<JsonObject>())
        {
            var id = StudioJson.RequiredString(field, "fieldId"); var direction = StudioJson.Text(field["direction"]) ?? "Asc"; var nulls = StudioJson.Text(field["nulls"]) ?? "Last";
            var allowed = (query["columns"] as JsonArray ?? []).OfType<JsonObject>().Any(c => StudioJson.Text(c["columnId"]) == id && c["visible"]?.GetValue<bool>() != false && c["sortable"]?.GetValue<bool>() == true);
            if (!allowed || !seen.Add(id) || direction is not ("Asc" or "Desc") || nulls is not ("First" or "Last")) throw new StudioException("STUDIO_FORBIDDEN", "排序列或方式未授权。", 403, "sort");
            normalized.Add(new JsonObject { ["fieldId"] = id, ["direction"] = direction, ["nulls"] = nulls });
        }
        return normalized;
    }
}
