using System.Globalization;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Data;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Contracts;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Application.Trace;

/// <summary>授权Dataset上的只读有界追溯，不存储物料事实；所有身份来自重查的源端数据。</summary>
public sealed class TraceRuntime(IStudioResourceStore store, StudioPublishing publishing, DatasetExecution datasets,
    IStudioAuthorization authorization, IStudioIdentity identity, TraceCredentials credentials,
    ITraceNodeLinks links)
{
    public async Task<JsonObject> ModelsAsync(StudioPrincipal p, CancellationToken ct)
    {
        p = await identity.RefreshAsync(p, ct);
        publishing.RequireKind("TraceModel");
        authorization.RequirePermission(p, "studio.trace.run");
        var items = new JsonArray();
        for (var page = 1; ; page++)
        {
            var candidates = await store.ListRunnableResourcesAsync(p, "TraceModel", new(Page: page, PageSize: 100), ct);
            foreach (var candidate in candidates)
            {
                try
                {
                    var (resource, revision) = await publishing.RequireRuntimeAsync(p, candidate.NId, null, ct);
                    var doc = StudioPublishing.Parse(revision.DocumentJson);
                    foreach (var dataset in DatasetReferences(doc)) await Dataset(p, dataset, ct);
                    if (!(doc["scope"]?["factoryIds"] as JsonArray ?? []).Any(f => p.FactoryNIds.Contains(StudioJson.Text(f) ?? "", StringComparer.Ordinal))) continue;
                    items.Add(new JsonObject { ["nId"] = resource.NId, ["name"] = resource.Name, ["revisionNId"] = revision.NId, ["revisionNo"] = revision.RevisionNo.ToString(CultureInfo.InvariantCulture) });
                }
                catch (StudioException e) when (e.StatusCode is 403 or 404 or 503 || e.Code == "SOURCE_CHANGED")
                { /* Unavailable models/dependencies must not be exposed as executable choices. */ }
            }
            if (candidates.Count < 100) break;
        }
        var fresh = await identity.RefreshAsync(p, ct);
        if (TraceCredentials.Fingerprint(fresh) != TraceCredentials.Fingerprint(p)) throw Forbidden();
        return new() { ["items"] = items };
    }
    public static IEnumerable<string> DatasetReferences(JsonObject document)
    {
        var refs = new List<string?> { StudioJson.Text(document["start"]?["datasetRevisionNId"]) };
        refs.AddRange(Objects(document["start"]?["searchFields"]).Select(f => StudioJson.Text(f["optionsDatasetRevisionNId"])));
        refs.AddRange(Objects(document["objects"]).Select(o => StudioJson.Text(o["detail"]?["datasetRevisionNId"])));
        refs.AddRange(Objects(document["objects"]).SelectMany(o => Objects(o["detail"]?["sections"])).Select(s => StudioJson.Text(s["datasetRevisionNId"])));
        foreach (var relation in Objects(document["relations"])) foreach (var key in new[] { "forward", "reverse" }) refs.Add(StudioJson.Text(relation[key]?["datasetRevisionNId"]));
        return refs.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct(StringComparer.Ordinal);
    }
    private async Task<(StudioPrincipal Principal, StudioResource Resource, StudioRevision Revision, JsonObject Document)> Runtime(StudioPrincipal p, string resource, string? revision, CancellationToken ct)
    {
        p = await identity.RefreshAsync(p, ct);
        var (r, v) = await publishing.RequireRuntimeAsync(p, resource, revision, ct);
        if (r.Kind != "TraceModel") throw Missing();
        var doc = StudioPublishing.Parse(v.DocumentJson); Validate(doc);
        foreach (var dataset in DatasetReferences(doc)) await Dataset(p, dataset, ct);
        return (p, r, v, doc);
    }
    private async Task<(StudioResource Resource, JsonObject Document)> Dataset(StudioPrincipal p, string revision, CancellationToken ct)
    {
        var v = await store.FindRevisionAsync(p.TenantNId, revision, ct) ?? throw Missing();
        var r = await store.FindResourceByIdAsync(p.TenantNId, v.ResourceId, ct) ?? throw Missing();
        if (r.Kind != "Dataset") throw Invalid("datasetRevisionNId", "来源必须为Dataset。");
        await publishing.RequireRuntimeAsync(p, r.NId, revision, ct);
        return (r, StudioPublishing.Parse(v.DocumentJson));
    }
    private async Task<JsonObject> Execute(StudioPrincipal p, string revision, JsonObject parameters, CancellationToken ct, int? page = null, int? size = null)
    {
        var (r, _) = await Dataset(p, revision, ct);
        return await datasets.ExecuteAsync(p, r.NId, new DatasetExecute(revision, parameters, page, size), ct, bypassCache: true);
    }
    private static void Validate(JsonObject doc)
    {
        if (StudioDocuments.Validate("TraceModel", doc).FirstOrDefault(x => x.Severity == "Error") is { } e) throw new StudioException(e.Code, e.Message, 422, e.Path);
    }
    public async Task<JsonObject> ContractAsync(StudioPrincipal p, string resource, string? revision, CancellationToken ct)
    {
        var context = await Runtime(p, resource, revision, ct); var d = context.Document;
        var search = new JsonArray();
        foreach (var f in Objects(d["start"]?["searchFields"]))
        {
            var projected = new JsonObject();
            foreach (var key in new[] { "parameterName", "label", "control", "required", "valueFieldId", "labelFieldId" }) projected[key] = f[key]?.DeepClone();
            projected["hasOptions"] = f["optionsDatasetRevisionNId"] is not null;
            search.Add(projected);
        }
        return new() { ["resource"] = new JsonObject { ["nId"] = resource, ["name"] = context.Resource.Name, ["publishedRevisionNId"] = context.Revision.NId }, ["revisionNId"] = context.Revision.NId,
            ["revisionNo"] = context.Revision.RevisionNo.ToString(CultureInfo.InvariantCulture), ["maxDepth"] = d["limits"]?["maxDepth"]?.DeepClone(),
            ["presentation"] = new JsonObject { ["layoutDirection"] = StudioJson.Text(d["presentation"]?["layoutDirection"]) ?? "LeftToRight" },
            ["relations"] = new JsonArray(Objects(d["relations"]).Select(r => (JsonNode)new JsonObject { ["id"] = r["id"]?.DeepClone(), ["name"] = r["name"]?.DeepClone(), ["kind"] = r["kind"]?.DeepClone() }).ToArray()),
            ["parameters"] = SafeParameters(d["parameters"]), ["start"] = new JsonObject { ["searchFields"] = search, ["columns"] = d["start"]?["columns"]?.DeepClone() } };
    }
    private static JsonArray SafeParameters(JsonNode? node)
    {
        var result = new JsonArray();
        foreach (var parameter in Objects(node))
        {
            var item = new JsonObject();
            foreach (var key in new[] { "name", "label", "type", "required", "nullable", "defaultValue", "maxLength", "enumValues", "min", "max", "source", "contextKey", "itemType" }) if (parameter.ContainsKey(key)) item[key] = parameter[key]?.DeepClone();
            result.Add(item);
        }
        return result;
    }
    public async Task<JsonObject> OptionsAsync(StudioPrincipal p, string? resource, JsonObject input, CancellationToken ct)
    {
        JsonObject doc; string revision;
        if (resource is null)
        {
            p = await identity.RefreshAsync(p, ct); authorization.RequirePermission(p, "studio.trace.design");
            doc = input["document"] as JsonObject ?? throw Invalid("document"); Validate(doc); revision = "preview";
        }
        else { var c = await Runtime(p, resource, StudioJson.Text(input["revisionNId"]), ct); p = c.Principal; doc = c.Document; revision = c.Revision.NId; }
        var field = Objects(doc["start"]?["searchFields"]).SingleOrDefault(x => StudioJson.Text(x["parameterName"]) == StudioJson.Text(input["parameterName"])) ?? throw Invalid("parameterName");
        var datasetRevision = StudioJson.RequiredString(field, "optionsDatasetRevisionNId");
        // 选项参数由其Dataset合同校验，可信上下文仍由DatasetValues重建。
        var (_, optionsDataset) = await Dataset(p, datasetRevision, ct);
        var supplied = input["parameters"] as JsonObject ?? new(); var mapped = new JsonObject();
        foreach (var definition in Objects(optionsDataset["parameters"]))
        { var name = StudioJson.RequiredString(definition, "name"); if (supplied.ContainsKey(name)) mapped[name] = supplied[name]?.DeepClone(); }
        var output = await Execute(p, datasetRevision, mapped, ct, 1, 200);
        var data = output["data"]; var rows = data is JsonArray array ? array : data?["rows"] as JsonArray ?? throw Invalid("options", "选项结果须为数组或rows集合。");
        var optionPath = data is JsonArray ? new JsonArray() : new JsonArray("rows");
        var valueKey = FieldName(optionsDataset, optionPath, StudioJson.RequiredString(field, "valueFieldId")); var labelKey = FieldName(optionsDataset, optionPath, StudioJson.RequiredString(field, "labelFieldId"));
        var items = new JsonArray(); foreach (var row in rows.OfType<JsonObject>().Take(200)) items.Add(new JsonObject { ["value"] = row[valueKey]?.DeepClone(), ["label"] = row[labelKey]?.DeepClone() });
        await Recheck(p, resource ?? "preview", revision, resource is null, ct);
        return new() { ["items"] = items };
    }
    public async Task<JsonObject> StartsAsync(StudioPrincipal p, string resource, JsonObject input, CancellationToken ct)
    {
        var c = await Runtime(p, resource, StudioJson.Text(input["revisionNId"]), ct);
        return await Starts(c.Principal, resource, c.Revision.NId, c.Document, input, false, ct);
    }
    public async Task<JsonObject> PreviewStartAsync(StudioPrincipal p, JsonObject input, CancellationToken ct)
    {
        p = await identity.RefreshAsync(p, ct); authorization.RequirePermission(p, "studio.trace.design");
        var doc = input["document"] as JsonObject ?? throw Invalid("document"); Validate(doc);
        return await Starts(p, "preview", StudioJson.Hash(StudioJson.Canonicalize(doc)), doc, input, true, ct);
    }
    private async Task<JsonObject> Starts(StudioPrincipal p, string resource, string revision, JsonObject doc, JsonObject input, bool preview, CancellationToken ct)
    {
        var parameters = DatasetValues.Parameters(p, doc, input["parameters"] as JsonObject ?? new());
        var page = input["page"]?.GetValue<int>() ?? 1; var size = input["pageSize"]?.GetValue<int>() ?? 20;
        if (page < 1 || size is < 1 or > 200) throw Invalid("page");
        var start = doc["start"]!.AsObject(); var map = Map(p, start["parameterMap"], parameters);
        var datasetRevision = StudioJson.RequiredString(start, "datasetRevisionNId");
        var (_, dataset) = await Dataset(p, datasetRevision, ct);
        var result = await Execute(p, datasetRevision, map, ct, page, size);
        var all = Collection(result["data"], start["collectionPath"]);
        var paged = dataset["pagination"] is not null;
        var total = paged ? StudioJson.Text(result["pageInfo"]?["totalCount"]) : all.Length.ToString(CultureInfo.InvariantCulture);
        if (total is null) throw Invalid("start", "起点必须提供页码总数。");
        var rows = paged ? all : all.Skip(checked((page - 1) * size)).Take(size).ToArray();
        var items = new JsonArray(); var rowKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var contexts = Contexts(p, parameters, row: row);
            var type = Scalar(Resolve(start["objectType"], contexts), "start.objectType");
            if (!(start["objectTypeIds"] as JsonArray ?? []).Any(x => StudioJson.Text(x) == type)) throw Invalid("start.objectType");
            var node = Node(p, doc, type, start["identityMap"]!, contexts, null);
            var rowKey = Scalar(Resolve(start["rowKey"], contexts), "start.rowKey");
            if (!rowKeys.Add(rowKey)) throw new StudioException("STUDIO_ROW_KEY_AMBIGUOUS", "起点行标识重复。", 409);
            var values = new JsonObject();
            foreach (var col in Objects(start["columns"]).Where(c => c["visible"]?.GetValue<bool>() != false))
            {
                var field = StudioJson.RequiredString(col, "fieldId"); values[field] = row[FieldName(dataset, start["collectionPath"]!.AsArray(), field)]?.DeepClone();
            }
            var data = new JsonObject { ["node"] = node, ["parameters"] = parameters.DeepClone(), ["rowKey"] = rowKey, ["queryHash"] = StudioJson.Hash(StudioJson.Canonicalize(map)), ["datasetRevisionNId"] = datasetRevision };
            items.Add(new JsonObject { ["startKey"] = credentials.Issue(p, resource, revision, preview ? "PreviewStart" : "Start", data), ["objectTypeId"] = type, ["identity"] = node["identity"]!.DeepClone(), ["values"] = values });
        }
        await Recheck(p, resource, revision, preview, ct);
        var output = new JsonObject { ["items"] = items, ["page"] = page, ["pageSize"] = size, ["totalCount"] = total };
        if (preview) { output["resolvedParameters"] = parameters; output["collectionPath"] = start["collectionPath"]!.DeepClone(); output["diagnostics"] = new JsonArray(); output["scope"] = "Preview"; }
        return output;
    }
    private async Task Recheck(StudioPrincipal p, string resource, string revision, bool preview, CancellationToken ct)
    {
        var fresh = await identity.RefreshAsync(p, ct);
        if (TraceCredentials.Fingerprint(fresh) != TraceCredentials.Fingerprint(p)) throw Forbidden();
        if (preview) authorization.RequirePermission(fresh, "studio.trace.design");
        else await publishing.RequireRuntimeAsync(fresh, resource, revision, ct);
    }
    public async Task<JsonObject> QueryAsync(StudioPrincipal p, string resource, JsonObject input, CancellationToken ct)
    {
        var c = await Runtime(p, resource, StudioJson.Text(input["revisionNId"]), ct); p = c.Principal;
        if (input["startKeys"] is not JsonArray starts || starts.Count is < 1 or > 100) throw Invalid("startKeys");
        TraceQueryState? state = null;
        var direction = Direction(input); var filter = Relations(c.Document, input); var depth = Depth(c.Document, input);
        foreach (var key in starts)
        {
            var token = credentials.Read(p, resource, c.Revision.NId, "Start", StudioJson.Text(key));
            var parameters = token["parameters"]!.AsObject();
            state ??= new(parameters.DeepClone().AsObject(), direction, filter, depth);
            if (!JsonNode.DeepEquals(parameters, state.Parameters)) throw Invalid("startKeys", "起点须来自同一参数查询。");
            var node = token["node"]!.AsObject();
            state.Pending.Enqueue(new(node.DeepClone().AsObject(), 0));
        }
        return await Traverse(p, resource, c.Revision.NId, c.Document, state!, ct);
    }
    public async Task<JsonObject> ExpandAsync(StudioPrincipal p, string resource, JsonObject input, CancellationToken ct)
    {
        var c = await Runtime(p, resource, StudioJson.Text(input["revisionNId"]), ct); p = c.Principal;
        var (node, parameters) = ReadNode(p, resource, c.Revision.NId, input);
        TraceQueryState state;
        if (StudioJson.Text(input["cursor"]) is { Length: > 0 } cursor)
        {
            state = credentials.Resume(p, resource, c.Revision.NId, cursor);
            var nodeCredential = credentials.Read(p, resource, c.Revision.NId, "Node", StudioJson.Text(input["nodeToken"]));
            if (StudioJson.Text(nodeCredential["queryHash"]) != state.QueryHash) throw Invalid("cursor", "节点不属于该继续查询。");
            if (state.Direction != Direction(input) || !state.RelationIds.SequenceEqual(Relations(c.Document, input)) || !JsonNode.DeepEquals(state.Parameters, parameters)) throw Invalid("cursor", "继续状态与查询范围不符。");
        }
        else { state = new(parameters, Direction(input), Relations(c.Document, input), Depth(c.Document, input)); state.Pending.Enqueue(new(node, 0)); }
        return await Traverse(p, resource, c.Revision.NId, c.Document, state, ct);
    }
    private async Task<JsonObject> Traverse(StudioPrincipal p, string resource, string revision, JsonObject doc, TraceQueryState state, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(doc["limits"]!["timeoutSeconds"]!.GetValue<int>()));
        var nodes = new Dictionary<string, JsonObject>(StringComparer.Ordinal); var edges = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var reasons = new HashSet<string>(StringComparer.Ordinal); var observations = new JsonArray();
        var maxNodes = doc["limits"]!["maxNodes"]!.GetValue<int>(); var maxEdges = doc["limits"]!["maxEdges"]!.GetValue<int>();
        bool stopped = false;
        void AddNode(JsonObject node)
        {
            var copy = node.DeepClone().AsObject();
            copy["nodeToken"] = credentials.Issue(p, resource, revision, "Node", new() { ["node"] = node.DeepClone(), ["parameters"] = state.Parameters.DeepClone(), ["queryHash"] = state.QueryHash });
            nodes[StudioJson.RequiredString(node, "key")] = copy;
        }
        while (state.Pending.Count > 0 && !stopped)
        {
            var work = state.Pending.Dequeue(); var node = work.Node; var nodeKey = StudioJson.RequiredString(node, "key");
            if (state.CompletedNodes.Contains(nodeKey)) continue;
            if (!nodes.ContainsKey(nodeKey) && nodes.Count >= maxNodes) { state.Pending.Enqueue(work); reasons.Add("MAX_NODES"); break; }
            AddNode(node);
            if (work.Depth >= state.MaxDepth) { nodes[nodeKey]["boundary"] = true; reasons.Add("MAX_DEPTH"); continue; }
            try
            {
                var (rootDefinition, _, rootOutput) = await Detail(p, doc, node, state.Parameters, timeout.Token);
                observations.Add(Observation(rootDefinition["detail"]!.AsObject(), rootOutput));
                AddNode(node);
                foreach (var relation in Objects(doc["relations"]).Where(r => state.RelationIds.Contains(StudioJson.Text(r["id"]), StringComparer.Ordinal)))
                foreach (var direction in state.Direction == "Both" ? new[] { "forward", "reverse" } : new[] { state.Direction == "Downstream" ? "forward" : "reverse" })
                {
                    if (stopped) break;
                    if (StudioJson.Text(node["objectTypeId"]) != StudioJson.Text(relation[direction == "forward" ? "fromTypeId" : "toTypeId"])) continue;
                    var relationId = StudioJson.RequiredString(relation, "id"); var queryKey = nodeKey + "/" + relationId + "/" + direction;
                    if (state.Visited.Contains(queryKey)) continue;
                    var binding = relation[direction]!.AsObject();
                    var output = await Execute(p, StudioJson.RequiredString(binding, "datasetRevisionNId"), Map(p, binding["inputMap"], state.Parameters, node), timeout.Token);
                    observations.Add(Observation(binding, output));
                    var result = binding["result"]!.AsObject(); var rows = Collection(output["data"], result["collectionPath"]);
                    var offset = state.Offsets.GetValueOrDefault(queryKey);
                    for (var i = offset; i < rows.Length; i++)
                    {
                        timeout.Token.ThrowIfCancellationRequested();
                        var contexts = Contexts(p, state.Parameters, node, rows[i]);
                        var target = Node(p, doc, StudioJson.RequiredString(result, "targetObjectTypeId"), result["targetIdentity"]!, contexts, result["summaryMap"]);
                        var targetKey = StudioJson.RequiredString(target, "key");
                        var evidence = new JsonObject(); foreach (var field in relation["evidence"]!.AsObject()) evidence[field.Key] = Resolve(field.Value, contexts);
                        _ = Scalar(evidence["recordKey"], "evidence.recordKey");
                        var from = direction == "forward" ? nodeKey : targetKey; var to = direction == "forward" ? targetKey : nodeKey;
                        var factSource = direction == "forward" ? node : target;
                        var edgeKey = StableKey(p.TenantNId, Scalar(factSource["identity"]!["sourceSystem"], "sourceSystem"), Scalar(factSource["identity"]!["factory"], "factory"), relationId, Scalar(evidence["recordKey"], "recordKey"), from, to);
                        if ((!nodes.ContainsKey(targetKey) && nodes.Count >= maxNodes) || (!edges.ContainsKey(edgeKey) && edges.Count >= maxEdges))
                        {
                            state.Offsets[queryKey] = i; state.Pending.Enqueue(work); reasons.Add(nodes.Count >= maxNodes ? "MAX_NODES" : "MAX_EDGES"); nodes[nodeKey]["boundary"] = true; stopped = true; break;
                        }
                        var (targetDefinition, _, targetOutput) = await Detail(p, doc, target, state.Parameters, timeout.Token);
                        observations.Add(Observation(targetDefinition["detail"]!.AsObject(), targetOutput));
                        AddNode(target);
                        edges[edgeKey] = new() { ["key"] = edgeKey, ["relationId"] = relationId, ["from"] = from, ["to"] = to, ["kind"] = relation["kind"]!.DeepClone(), ["evidence"] = evidence, ["label"] = Resolve(relation["label"], contexts) };
                        if (StudioJson.Text(relation["kind"]) == "Genealogy" && !state.CompletedNodes.Contains(targetKey) && !state.Pending.Any(x => StudioJson.Text(x.Node["key"]) == targetKey && x.Depth <= work.Depth + 1)) state.Pending.Enqueue(new(target, work.Depth + 1));
                    }
                    if (stopped) break;
                    state.Offsets.Remove(queryKey); state.Visited.Add(queryKey);
                    if (state.Visited.Count > 100000) throw new StudioException("STUDIO_LIMIT", "继续状态超过预算。", 429);
                }
                if (!stopped) state.CompletedNodes.Add(nodeKey);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { reasons.Add("SOURCE_TIMEOUT"); nodes[nodeKey]["boundary"] = true; state.Pending.Enqueue(work); stopped = true; }
            catch (StudioException e) when (e.StatusCode is 429 or 502 or 504)
            { reasons.Add(e.Code); nodes[nodeKey]["boundary"] = true; state.Pending.Enqueue(work); stopped = true; }
        }
        await Recheck(p, resource, revision, false, ct);
        foreach (var reference in DatasetReferences(doc)) await Dataset(p, reference, ct);
        var outputResult = new JsonObject { ["requestId"] = "trace-" + Guid.NewGuid().ToString("N"), ["revisionNId"] = revision,
            ["nodes"] = new JsonArray(nodes.Values.Select(x => (JsonNode)x).ToArray()), ["edges"] = new JsonArray(edges.Values.Select(x => (JsonNode)x).ToArray()),
            ["status"] = reasons.Count == 0 ? "Complete" : "Partial", ["partialReasons"] = new JsonArray(reasons.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["observedOn"] = null, ["sourceObservations"] = observations };
        if (state.Pending.Count > 0) outputResult["cursor"] = credentials.Save(p, resource, revision, state);
        return outputResult;
    }
    public async Task<JsonObject> NodeDetailAsync(StudioPrincipal p, string resource, JsonObject input, CancellationToken ct)
    {
        var c = await Runtime(p, resource, StudioJson.Text(input["revisionNId"]), ct); p = c.Principal;
        var (node, parameters) = ReadNode(p, resource, c.Revision.NId, input);
        var (definition, row, output) = await Detail(p, c.Document, node, parameters, ct);
        var fields = new JsonArray(); var context = Contexts(p, parameters, node, row);
        foreach (var f in Objects(definition["detail"]?["fields"])) fields.Add(new JsonObject { ["label"] = f["label"]?.DeepClone(), ["value"] = Resolve(f["value"], context), ["format"] = f["format"]?.DeepClone() });
        var linkItems = new JsonArray(); foreach (var link in Objects(definition["detail"]?["links"])) linkItems.Add(new JsonObject { ["id"] = link["id"]?.DeepClone(), ["label"] = link["label"]?.DeepClone(), ["kind"] = link["kind"]?.DeepClone() });
        var sections = new JsonArray(Objects(definition["detail"]?["sections"]).Select(s => (JsonNode)new JsonObject { ["id"] = s["id"]?.DeepClone(), ["label"] = s["label"]?.DeepClone(), ["kind"] = s["kind"]?.DeepClone(), ["pageSize"] = s["pageSize"]?.DeepClone() }).ToArray());
        await Recheck(p, resource, c.Revision.NId, false, ct);
        return new() { ["fields"] = fields, ["links"] = linkItems, ["sections"] = sections, ["observedOn"] = output["quality"]?["sourceObservedOn"]?.DeepClone(), ["quality"] = output["quality"]?.DeepClone() };
    }
    /// <summary>按固定模型分组重取已授权节点上下文，再执行关联查询；调用方不能传入数据集或关联值。</summary>
    public async Task<JsonObject> NodeSectionAsync(StudioPrincipal p, string resource, JsonObject input, CancellationToken ct)
    {
        var c = await Runtime(p, resource, StudioJson.Text(input["revisionNId"]), ct); p = c.Principal;
        var (node, parameters) = ReadNode(p, resource, c.Revision.NId, input);
        var (definition, detailRow, _) = await Detail(p, c.Document, node, parameters, ct);
        var section = Objects(definition["detail"]?["sections"]).SingleOrDefault(s => StudioJson.Text(s["id"]) == StudioJson.Text(input["sectionId"])) ?? throw Missing();
        static int PageValue(JsonObject input, string name, int fallback)
        {
            if (!input.ContainsKey(name)) return fallback;
            return input[name] is JsonValue value && value.TryGetValue<int>(out var number) && number > 0 ? number : throw Invalid(name, "分页参数须为正整数。");
        }
        var page = PageValue(input, "page", 1); var size = PageValue(input, "pageSize", section["pageSize"]!.GetValue<int>());
        if (size > 200) throw Invalid("pageSize", "分页大小不能超过200。");
        var kind = StudioJson.RequiredString(section, "kind");
        if (kind == "Fields" && page != 1) throw Invalid("page", "字段分组仅有第一页。");
        var revision = StudioJson.RequiredString(section, "datasetRevisionNId");
        var (_, dataset) = await Dataset(p, revision, ct);
        if (kind == "Table" && StudioJson.Text(dataset["pagination"]?["mode"]) != "Offset") throw Invalid("sectionId", "表格分组的数据集须提供页码分页与真实总数。");
        var context = Contexts(p, parameters, node, detailRow);
        var mapped = new JsonObject();
        foreach (var map in Objects(section["parameterMap"]))
        {
            var binding = map["source"]!.AsObject();
            JsonNode? value;
            if (StudioJson.Text(binding["from"]) is "Row" or "Node")
            {
                // 先读取原始关联值，不能用默认值或可空参数把缺失的业务条件变成全表查询。
                var requiredBinding = binding.DeepClone().AsObject(); requiredBinding["nullPolicy"] = "Null"; requiredBinding.Remove("transform");
                value = Resolve(requiredBinding, context);
                if (value is null || value is JsonValue scalar && scalar.TryGetValue<string>(out var text) && string.IsNullOrWhiteSpace(text))
                    throw new StudioException("TRACE_SECTION_CONTEXT_REQUIRED", "缺少关联条件，无法读取该明细分组。", 422, "section.parameterMap." + StudioJson.Text(map["target"]));
                value = Resolve(binding, context);
            }
            else value = Resolve(binding, context);
            mapped[StudioJson.RequiredString(map, "target")] = value;
        }
        var output = await Execute(p, revision, mapped, ct, kind == "Table" ? page : null, kind == "Table" ? size : null);
        var fields = new JsonArray(); var columns = new JsonArray(); var items = new JsonArray(); string total;
        if (kind == "Table")
        {
            total = StudioJson.Text(output["pageInfo"]?["totalCount"]) ?? throw Invalid("sectionId", "表格分组未返回真实总数。");
            var rows = Collection(output["data"], section["collectionPath"]);
            if (rows.Length > size) throw Invalid("sectionId", "数据集返回行数超过当前分页大小。");
            var visible = Objects(section["columns"]).Where(column => column["visible"]?.GetValue<bool>() != false).ToArray();
            var columnFields = visible.Select(column => (Id: StudioJson.RequiredString(column, "fieldId"), Name: FieldName(dataset, section["collectionPath"]!.AsArray(), StudioJson.RequiredString(column, "fieldId")))).ToArray();
            foreach (var column in visible) columns.Add(column.DeepClone());
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var keyValue = Resolve(section["rowKey"], Contexts(p, parameters, node, row));
                var key = keyValue is JsonValue scalarKey && scalarKey.GetValueKind() is System.Text.Json.JsonValueKind.Number or System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False ? scalarKey.ToJsonString() : Scalar(keyValue, "section.rowKey");
                if (!keys.Add(key)) throw new StudioException("STUDIO_ROW_KEY_AMBIGUOUS", "明细分组行标识重复。", 409);
                var values = new JsonObject(); foreach (var column in columnFields) values[column.Id] = row[column.Name]?.DeepClone();
                items.Add(new JsonObject { ["key"] = key, ["values"] = values });
            }
        }
        else
        {
            if (StudioJson.Text(output["pageInfo"]?["totalCount"]) is { } count && count != "0" && count != "1") throw Invalid("sectionId", "字段分组的数据集返回了多行，请使用表格分组。");
            var result = StudioBinding.Resolve(new JsonObject { ["from"] = "Root", ["path"] = section["collectionPath"]!.DeepClone(), ["nullPolicy"] = "Error" }, new Dictionary<string, JsonNode?> { ["Root"] = output["data"] });
            JsonObject? row = result switch { JsonObject single => single, JsonArray { Count: 0 } => null, JsonArray { Count: 1 } array when array[0] is JsonObject single => single, _ => throw Invalid("sectionId", "字段分组须返回单个对象或至多一行。") };
            total = row is null ? "0" : "1";
            if (row is not null)
                foreach (var field in Objects(section["fields"])) fields.Add(new JsonObject { ["label"] = field["label"]?.DeepClone(), ["value"] = Resolve(field["value"], Contexts(p, parameters, node, row)), ["format"] = field["format"]?.DeepClone() });
        }
        await Recheck(p, resource, c.Revision.NId, false, ct);
        await Dataset(p, revision, ct);
        return new() { ["sectionId"] = section["id"]!.DeepClone(), ["kind"] = kind, ["fields"] = fields, ["columns"] = columns, ["items"] = items, ["totalCount"] = total, ["page"] = page, ["pageSize"] = size, ["observedOn"] = output["quality"]?["sourceObservedOn"]?.DeepClone() };
    }
    public async Task<TraceLinkInput> ResolveNodeLinkInputAsync(StudioPrincipal p, string resource, JsonObject input, CancellationToken ct)
    {
        var c = await Runtime(p, resource, StudioJson.Text(input["revisionNId"]), ct); p = c.Principal;
        var (node, parameters) = ReadNode(p, resource, c.Revision.NId, input);
        var (definition, row, _) = await Detail(p, c.Document, node, parameters, ct);
        var link = Objects(definition["detail"]?["links"]).SingleOrDefault(x => StudioJson.Text(x["id"]) == StudioJson.Text(input["linkId"])) ?? throw Missing();
        var context = Contexts(p, parameters, node, row); var value = Resolve(link["value"], context);
        var version = link["fileSource"]?["version"] is { } v ? Scalar(Resolve(v, context), "fileSource.version") : null;
        await Recheck(p, resource, c.Revision.NId, false, ct);
        return new(link.DeepClone().AsObject(), value, Map(p, link["parameterMap"], parameters, node, row), version);
    }
    public async Task<TraceNodeLinkResult> NodeLinkAsync(StudioPrincipal p, string resource, JsonObject input, CancellationToken ct)
    {
        p = await identity.RefreshAsync(p, ct);
        var resolved = await ResolveNodeLinkInputAsync(p, resource, input, ct);
        return await links.ResolveAsync(p, resource, resolved.Link, resolved.Value, resolved.Parameters, resolved.Version, input, ct);
    }
    private (JsonObject Node, JsonObject Parameters) ReadNode(StudioPrincipal p, string resource, string revision, JsonObject input)
    {
        var token = credentials.Read(p, resource, revision, "Node", StudioJson.Text(input["nodeToken"]));
        var node = token["node"]!.AsObject();
        if (StudioJson.Text(node["key"]) != StudioJson.Text(input["nodeKey"])) throw Forbidden();
        return (node, token["parameters"]!.AsObject());
    }
    private async Task<(JsonObject Definition, JsonObject Row, JsonObject Output)> Detail(StudioPrincipal p, JsonObject doc, JsonObject node, JsonObject parameters, CancellationToken ct)
    {
        var definition = Objects(doc["objects"]).SingleOrDefault(x => StudioJson.Text(x["id"]) == StudioJson.Text(node["objectTypeId"])) ?? throw Missing();
        var detail = definition["detail"]!.AsObject();
        var output = await Execute(p, StudioJson.RequiredString(detail, "datasetRevisionNId"), Map(p, detail["parameterMap"], parameters, node), ct);
        // 明细合同支持Dataset根对象或标准rows集合；只取完整身份严格匹配的唯一行。
        var data = output["data"]; IEnumerable<JsonObject> rows = data is JsonObject obj && obj["rows"] is JsonArray array ? array.OfType<JsonObject>() : data is JsonArray a ? a.OfType<JsonObject>() : data is JsonObject single ? [single] : [];
        var matches = new List<JsonObject>();
        foreach (var row in rows)
        {
            var candidate = Node(p, doc, StudioJson.RequiredString(definition, "id"), definition["identity"]!, Contexts(p, parameters, node, row), definition["summary"]);
            if (StudioJson.Text(candidate["key"]) == StudioJson.Text(node["key"]))
            {
                matches.Add(row);
                if (node["summary"] is JsonArray summary && summary.Count == 0) node["summary"] = candidate["summary"]!.DeepClone();
            }
        }
        if (matches.Count != 1) throw Forbidden();
        return (definition, matches[0], output);
    }
    public async Task<JsonObject> PreviewRelationAsync(StudioPrincipal p, JsonObject input, CancellationToken ct)
    {
        p = await identity.RefreshAsync(p, ct); authorization.RequirePermission(p, "studio.trace.design");
        var doc = input["document"] as JsonObject ?? throw Invalid("document"); Validate(doc);
        var relation = Objects(doc["relations"]).SingleOrDefault(x => StudioJson.Text(x["id"]) == StudioJson.Text(input["relationId"])) ?? throw Invalid("relationId");
        var direction = StudioJson.Text(input["direction"]) switch { "Upstream" or "reverse" => "reverse", "Downstream" or "forward" => "forward", _ => throw Invalid("direction") };
        var sample = input["nodeSample"] as JsonObject ?? throw Invalid("nodeSample");
        var parameters = DatasetValues.Parameters(p, doc, input["parameters"] as JsonObject ?? new());
        var expectedType = StudioJson.RequiredString(relation, direction == "forward" ? "fromTypeId" : "toTypeId");
        var sampleIdentity = sample["identity"] as JsonObject ?? throw Invalid("nodeSample.identity");
        var identityMap = new JsonObject(); foreach (var key in new[] { "sourceSystem", "factory", "businessKey" }) identityMap[key] = new JsonObject { ["from"] = "Constant", ["value"] = sampleIdentity[key]?.DeepClone(), ["nullPolicy"] = "Error" };
        var source = Node(p, doc, expectedType, identityMap, Contexts(p, parameters), null);
        await Detail(p, doc, source, parameters, ct);
        var traversal = relation[direction]!.AsObject(); var map = Map(p, traversal["inputMap"], parameters, source);
        var output = await Execute(p, StudioJson.RequiredString(traversal, "datasetRevisionNId"), map, ct);
        var result = traversal["result"]!.AsObject(); var rows = Collection(output["data"], result["collectionPath"]);
        var nodes = new JsonArray(); var edges = new JsonArray();
        foreach (var row in rows.Take(100))
        {
            var context = Contexts(p, parameters, source, row);
            var node = Node(p, doc, StudioJson.RequiredString(result, "targetObjectTypeId"), result["targetIdentity"]!, context, result["summaryMap"]);
            await Detail(p, doc, node, parameters, ct); nodes.Add(node);
            var evidence = new JsonObject(); foreach (var f in relation["evidence"]!.AsObject()) evidence[f.Key] = Resolve(f.Value, context);
            edges.Add(new JsonObject { ["relationId"] = relation["id"]!.DeepClone(), ["from"] = (direction == "forward" ? source : node)["key"]!.DeepClone(), ["to"] = (direction == "forward" ? node : source)["key"]!.DeepClone(), ["evidence"] = evidence });
        }
        await Recheck(p, "preview", "preview", true, ct);
        return new() { ["resolvedInput"] = map, ["sourceRows"] = new JsonArray(rows.Take(100).Select(x => (JsonNode)x.DeepClone()).ToArray()), ["nodes"] = nodes, ["edges"] = edges, ["diagnostics"] = new JsonArray(), ["simulated"] = true };
    }
    public static string StableKey(params string[] parts) => "trace-" + StudioJson.Hash(new JsonArray(parts.Select(x => (JsonNode?)JsonValue.Create(x.Normalize())).ToArray()).ToJsonString());
    private static JsonObject Node(StudioPrincipal p, JsonObject doc, string type, JsonNode identityMap, IReadOnlyDictionary<string, JsonNode?> context, JsonNode? summaryMap)
    {
        var definition = Objects(doc["objects"]).SingleOrDefault(x => StudioJson.Text(x["id"]) == type) ?? throw Invalid("objectTypeId");
        var id = new JsonObject { ["tenantNId"] = p.TenantNId };
        foreach (var key in new[] { "sourceSystem", "factory", "businessKey" }) id[key] = Scalar(Resolve(identityMap[key], context), key);
        var factory = StudioJson.RequiredString(id, "factory");
        if (!p.FactoryNIds.Contains(factory, StringComparer.Ordinal) || !(doc["scope"]!["factoryIds"] as JsonArray ?? []).Any(x => StudioJson.Text(x) == factory)) throw Forbidden();
        var summary = new JsonArray();
        foreach (var field in Objects(summaryMap)) summary.Add(new JsonObject { ["label"] = (field["label"] ?? field["target"])?.DeepClone(), ["value"] = Resolve(field["value"] ?? field["source"], context) });
        return new() { ["key"] = StableKey(p.TenantNId, StudioJson.RequiredString(id, "sourceSystem"), factory, type, StudioJson.RequiredString(id, "businessKey")), ["objectTypeId"] = type, ["identity"] = id, ["summary"] = summary, ["grain"] = definition["identity"]!["grain"]!.DeepClone() };
    }
    private static JsonObject Observation(JsonObject binding, JsonObject output) => new() { ["datasetRevisionNId"] = binding["datasetRevisionNId"]?.DeepClone(), ["observedOn"] = output["quality"]?["sourceObservedOn"]?.DeepClone(), ["receivedOn"] = output["receivedOn"]?.DeepClone(), ["quality"] = output["quality"]?.DeepClone() };
    private static Dictionary<string, JsonNode?> Contexts(StudioPrincipal p, JsonObject parameters, JsonObject? node = null, JsonObject? row = null) => new()
    { ["PageParam"] = parameters, ["Node"] = node, ["Row"] = row, ["Root"] = row, ["Context"] = new JsonObject { ["tenant"] = p.TenantNId, ["user"] = p.SubjectNId, ["authorizedFactoryIds"] = new JsonArray(p.FactoryNIds.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()) } };
    private static JsonObject Map(StudioPrincipal p, JsonNode? maps, JsonObject parameters, JsonObject? node = null, JsonObject? row = null)
    { var result = new JsonObject(); foreach (var map in Objects(maps)) result[StudioJson.RequiredString(map, "target")] = Resolve(map["source"], Contexts(p, parameters, node, row)); return result; }
    private static JsonNode? Resolve(JsonNode? binding, IReadOnlyDictionary<string, JsonNode?> contexts) => StudioBinding.Resolve(binding as JsonObject ?? throw Invalid("binding"), contexts);
    private static string Scalar(JsonNode? value, string path) => StudioJson.Text(value) is { Length: > 0 and <= 512 } text && !text.Contains('\0') ? text : throw Invalid(path, "身份与证据键必须是非空字符串。");
    private static JsonObject[] Collection(JsonNode? data, JsonNode? path)
    {
        var binding = new JsonObject { ["from"] = "Root", ["path"] = path?.DeepClone(), ["nullPolicy"] = "Error" };
        var result = StudioBinding.Resolve(binding, new Dictionary<string, JsonNode?> { ["Root"] = data });
        if (result is not JsonArray array || array.Any(x => x is not JsonObject)) throw Invalid("collectionPath", "追溯集合必须是对象数组。");
        return array.OfType<JsonObject>().ToArray();
    }
    private static JsonObject[] Objects(JsonNode? node) => (node as JsonArray ?? []).OfType<JsonObject>().ToArray();
    private static string FieldName(JsonObject dataset, JsonArray collectionPath, string fieldId)
    {
        var schema = dataset["result"] as JsonObject;
        foreach (var segment in collectionPath) schema = Objects(schema?["children"]).SingleOrDefault(x => StudioJson.Text(x["name"]) == StudioJson.Text(segment));
        var field = Objects(schema?["item"]?["children"]).SingleOrDefault(x => StudioJson.Text(x["id"]) == fieldId);
        if (field is null || !(dataset["readableFieldIds"] as JsonArray ?? []).Any(x => StudioJson.Text(x) == fieldId)) throw Invalid("fieldId", "字段未在可读结果合同声明。");
        return StudioJson.RequiredString(field, "name");
    }
    private static string Direction(JsonObject input) => StudioJson.Text(input["direction"]) is "Upstream" or "Downstream" or "Both" ? StudioJson.Text(input["direction"])! : throw Invalid("direction");
    private static int Depth(JsonObject doc, JsonObject input) { var maximum = doc["limits"]!["maxDepth"]!.GetValue<int>(); var requested = input["maxDepth"]?.GetValue<int>() ?? maximum; return requested >= 1 && requested <= maximum ? requested : throw Invalid("maxDepth"); }
    private static string[] Relations(JsonObject doc, JsonObject input)
    {
        var all = Objects(doc["relations"]); var selected = input["relationIds"] is JsonArray array ? array.Select(x => StudioJson.Text(x) ?? "").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() : all.Where(x => StudioJson.Text(x["kind"]) == "Genealogy").Select(x => StudioJson.RequiredString(x, "id")).Order(StringComparer.Ordinal).ToArray();
        if (selected.Any(x => !all.Any(r => StudioJson.Text(r["id"]) == x))) throw Invalid("relationIds"); return selected;
    }
    private static StudioException Invalid(string path, string message = "追溯参数或映射无效。") => new("TRACE_MAPPING_INVALID", message, 422, path);
    private static StudioException Missing() => new("STUDIO_NOT_FOUND", "追溯资源或依赖不存在。", 404);
    private static StudioException Forbidden() => new("STUDIO_FORBIDDEN", "节点已不可见或超出当前授权范围。", 403);
}
