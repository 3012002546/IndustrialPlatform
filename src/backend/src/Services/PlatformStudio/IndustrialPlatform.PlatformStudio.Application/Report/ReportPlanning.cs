using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Application.Report;

public sealed record ReportDefinitionSnapshot(string Kind, JsonObject Document, IReadOnlyDictionary<string, ReportDefinitionSnapshot> Dependencies, string ContentHash, string? ResourceNId = null);
public sealed record ReportDataSnapshot(IReadOnlyDictionary<string, JsonNode?> Instances, JsonArray SourceManifest);
public sealed record ReportPlanInstance(string InstanceKey, string? RuleId, string? WorkOrderKey, string? OperationKey, string? SourceRowKey, string ReportNId, string? EntryBandId, int RowCount, JsonObject ResolvedMappings, string Decision, string? Reason, IReadOnlyList<ReportFlowBlock> Blocks);
public sealed record ReportAssemblyPlan(IReadOnlyList<ReportPlanInstance> Instances, IReadOnlyList<StudioDiagnostic> Diagnostics);
public sealed record ReportFlowBlock(string Marker, string BandId, string ControlId, string Type, JsonObject Definition, JsonNode? Value, string ParentInstanceKey, bool Repeat = false);
public sealed record ReportContentFlow(IReadOnlyList<ReportFlowBlock> Blocks, IReadOnlyList<string> ExpectedMarkers);

/// <summary>One run owns this resolver. Source observations are collected once per fixed revision/parameter set.</summary>
public sealed class ReportDataResolver(Func<string, JsonObject, CancellationToken, Task<JsonObject>> execute)
{
    private readonly Dictionary<string, JsonObject> _observations = new(StringComparer.Ordinal);
    private long _bytes;
    private bool _snapshotOnly;
    public void RestoreSnapshot(JsonArray snapshot)
    {
        if (_observations.Count != 0 || snapshot.Count > 200 || Encoding.UTF8.GetByteCount(snapshot.ToJsonString()) > 50 * 1024 * 1024) throw new StudioException("REPORT_SNAPSHOT_INVALID", "采集快照无效或超限。", 422);
        foreach (var item in snapshot.OfType<JsonObject>())
        {
            var key = StudioJson.RequiredString(item, "key");
            if (key.Length > 65536 || item["result"] is not JsonObject result || !_observations.TryAdd(key, result.DeepClone().AsObject())) throw new StudioException("REPORT_SNAPSHOT_INVALID", "采集快照有重复或缺失项。", 422);
        }
        if (_observations.Count != snapshot.Count) throw new StudioException("REPORT_SNAPSHOT_INVALID", "采集快照类型无效。", 422);
        _snapshotOnly = true;
    }
    public JsonArray SnapshotDocument => new(_observations.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => (JsonNode)new JsonObject { ["key"] = p.Key, ["result"] = p.Value.DeepClone() }).ToArray());
    public JsonArray Manifest => new(_observations.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => (JsonNode)new JsonObject { ["observationKey"] = StudioJson.Hash(p.Key), ["revisionNId"] = p.Value["revisionNId"]?.DeepClone(), ["observedOn"] = p.Value["observedOn"]?.DeepClone(), ["receivedOn"] = p.Value["receivedOn"]?.DeepClone(), ["sourceVersion"] = p.Value["sourceVersion"]?.DeepClone(), ["consistency"] = "ObservedAtDifferentTimes", ["sha256"] = StudioJson.Hash(StudioJson.Canonicalize(p.Value["data"])) }).ToArray());

    public async Task<JsonNode?> DatasetAsync(string revision, JsonObject parameters, CancellationToken token)
    {
        var key = revision + "/" + StudioJson.Canonicalize(parameters);
        if (_observations.TryGetValue(key, out var existing)) return existing["data"]?.DeepClone();
        if (_snapshotOnly) throw new StudioException("REPORT_SNAPSHOT_MISSING", "原快照未保存该固定查询，不重新访问业务数据。", 422);
        if (_observations.Count >= 200) throw new StudioException("STUDIO_LIMIT", "报告来源查询超过200次预算。", 429, "report.queries");
        var result = await execute(revision, parameters, token);
        if (result["truncated"]?.GetValue<bool>() == true) throw new StudioException("STUDIO_LIMIT", "来源结果被截断，不能生成完整报告。", 429);
        _bytes += Encoding.UTF8.GetByteCount(result.ToJsonString());
        if (_bytes > 50 * 1024 * 1024) throw new StudioException("STUDIO_LIMIT", "报告采集超过50MiB预算。", 429, "report.bytes");
        _observations[key] = result.DeepClone().AsObject();
        return result["data"]?.DeepClone();
    }
    public async Task<ReportDataSnapshot> CaptureAsync(JsonObject document, JsonObject parameters, JsonObject trustedContext, CancellationToken token, IReadOnlySet<string>? wanted = null)
    {
        var definitions = (document["instances"] as JsonArray ?? []).OfType<JsonObject>().ToDictionary(x => StudioJson.RequiredString(x, "instanceId"), StringComparer.Ordinal);
        var values = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        async Task Visit(string id)
        {
            if (values.ContainsKey(id)) return;
            if (!definitions.TryGetValue(id, out var definition) || !visiting.Add(id)) throw new StudioException("PATH_INVALID", "实例依赖缺失或循环。", 422, "instances." + id);
            foreach (var dependency in (definition["dependsOn"] as JsonArray ?? []).Select(StudioJson.Text).Concat((definition["parameters"] as JsonArray ?? []).OfType<JsonObject>().Where(m => StudioJson.Text(m["source"]?["from"]) == "Instance").Select(m => StudioJson.Text(m["source"]?["instanceId"]))).Where(x => x is not null).Distinct()) await Visit(dependency!);
            var context = Context(parameters, values, trustedContext);
            values[id] = StudioJson.Text(definition["sourceType"]) == "Constant" ? definition["value"]?.DeepClone() : await DatasetAsync(StudioJson.RequiredString(definition, "revisionNId"), Map(definition["parameters"], context), token);
            visiting.Remove(id);
        }
        foreach (var id in definitions.Keys.Where(id => wanted is null || wanted.Contains(id))) await Visit(id);
        return new(values, Manifest);
    }
    public async Task<JsonArray> CollectionAsync(JsonObject collection, IReadOnlyDictionary<string, JsonNode?> context, CancellationToken token)
    {
        JsonNode? root = StudioJson.Text(collection["mode"]) switch
        {
            "RootPath" => context.GetValueOrDefault("Instance:" + StudioJson.RequiredString(collection, "instanceId")),
            "ParentPath" => context.GetValueOrDefault("Row"),
            "Dataset" => await DatasetAsync(StudioJson.RequiredString(collection, "datasetRevisionNId"), Map(collection["parameterMap"], context), token),
            _ => throw new StudioException("PATH_INVALID", "集合模式不存在。", 422),
        };
        var value = Read(root, collection["path"] as JsonArray ?? []);
        if (value is not JsonArray rows) throw new StudioException("TYPE_MISMATCH", "集合路径必须返回数组，缺路径及null不是空集合。", 422, "collection.path");
        var result = new JsonArray();
        foreach (var row in rows)
        {
            var rowContext = new Dictionary<string, JsonNode?>(context, StringComparer.Ordinal) { ["ParentRow"] = context.GetValueOrDefault("Row"), ["Row"] = row };
            if (collection["filter"] is not JsonObject filter || StudioBinding.Evaluate(filter, rowContext)) result.Add(row?.DeepClone());
        }
        return result;
    }
    public static Dictionary<string, JsonNode?> Context(JsonObject parameters, IReadOnlyDictionary<string, JsonNode?> instances, JsonObject trusted) => new(instances.Select(p => KeyValuePair.Create("Instance:" + p.Key, p.Value)).Concat(new Dictionary<string, JsonNode?> { ["PageParam"] = parameters, ["Context"] = trusted }), StringComparer.Ordinal);
    public static JsonObject Map(JsonNode? mappings, IReadOnlyDictionary<string, JsonNode?> context)
    {
        var result = new JsonObject();
        foreach (var map in (mappings as JsonArray ?? []).OfType<JsonObject>()) result[StudioJson.RequiredString(map, "target")] = Resolve(map["source"]!.AsObject(), context);
        return result;
    }
    public static JsonNode? Resolve(JsonObject binding, IReadOnlyDictionary<string, JsonNode?> context)
    {
        var from = StudioJson.Text(binding["from"]);
        if (from != "Constant")
        {
            var key = StudioJson.Text(binding["contextBandId"]) is { } band ? "Band:" + band : from == "Instance" ? "Instance:" + StudioJson.RequiredString(binding, "instanceId") : from ?? "";
            if (!context.TryGetValue(key, out var root)) throw new StudioException("PATH_INVALID", "绑定上下文不存在。", 422, "binding.from");
            _ = Read(root, binding["path"] as JsonArray ?? throw new StudioException("PATH_INVALID", "字段路径缺失。", 422));
            if (binding.ContainsKey("contextBandId"))
            {
                var copy = binding.DeepClone().AsObject(); copy.Remove("contextBandId"); copy["from"] = "Row";
                return StudioBinding.Resolve(copy, new Dictionary<string, JsonNode?>(context, StringComparer.Ordinal) { ["Row"] = root });
            }
        }
        return StudioBinding.Resolve(binding, context);
    }
    public static JsonNode? Read(JsonNode? root, JsonArray path)
    {
        var value = root;
        foreach (var segment in path)
        {
            var key = StudioJson.Text(segment);
            if (key is null or "__proto__" or "prototype" or "constructor") throw new StudioException("PATH_INVALID", "字段路径非法。", 422);
            if (value is JsonObject obj && obj.ContainsKey(key)) value = obj[key];
            else if (value is JsonArray array && int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < array.Count) value = array[index];
            else throw new StudioException("PATH_INVALID", "数据快照中不存在配置路径。", 422, "binding.path");
        }
        return value?.DeepClone();
    }
    public static IEnumerable<JsonNode?> Sort(JsonArray rows, JsonNode? sort)
    {
        var fields = sort as JsonArray ?? (sort as JsonObject)?["fields"] as JsonArray ?? [];
        return rows.Select((row, i) => (row, i)).OrderBy(x => x, Comparer<(JsonNode? row, int i)>.Create((a, b) =>
        {
            foreach (var f in fields.OfType<JsonObject>())
            {
                var path = f["path"] as JsonArray ?? new JsonArray(StudioJson.Text(f["field"]) ?? StudioJson.Text(f["fieldId"]) ?? "");
                var av = Read(a.row, path); var bv = Read(b.row, path);
                if (av is null || bv is null)
                {
                    if (av is null && bv is null) continue;
                    return (av is null ? -1 : 1) * (StudioJson.Text(f["nulls"]) == "First" ? 1 : -1);
                }
                var left = StudioJson.Text(av) ?? av?.ToJsonString() ?? ""; var right = StudioJson.Text(bv) ?? bv?.ToJsonString() ?? "";
                var comparison = decimal.TryParse(left, CultureInfo.InvariantCulture, out var l) && decimal.TryParse(right, CultureInfo.InvariantCulture, out var r) ? l.CompareTo(r) : string.CompareOrdinal(left, right);
                if (comparison != 0) return StudioJson.Text(f["direction"]) is "Desc" or "desc" ? -comparison : comparison;
            }
            return a.i.CompareTo(b.i);
        })).Select(x => x.row);
    }
}

/// <summary>Pure assembly semantics over fixed definitions and run-local observations; no renderer or Dashboard dependency.</summary>
public sealed class ReportPlanner(ReportDataResolver data, JsonObject trustedContext, Func<JsonObject, JsonObject, JsonObject>? parameterResolver = null)
{
    private int _blocks;
    private int _sequence;
    public async Task<ReportAssemblyPlan> PlanAsync(ReportDefinitionSnapshot definition, JsonObject parameters, CancellationToken token)
    {
        var diagnostics = StudioDocuments.Validate(definition.Kind, definition.Document);
        if (diagnostics.FirstOrDefault(d => d.Severity == "Error") is { } error) throw new StudioException(error.Code, error.Message, 422, error.Path);
        parameters = parameterResolver?.Invoke(definition.Document, parameters) ?? parameters;
        var snapshot = await data.CaptureAsync(definition.Document, parameters, trustedContext, token);
        var context = ReportDataResolver.Context(parameters, snapshot.Instances, trustedContext);
        var instances = new List<ReportPlanInstance>();
        if (definition.Kind == "Report")
        {
            var blocks = await ExpandAsync(definition, context, "report", null, null, null, token);
            instances.Add(new("report", null, null, null, null, "", null, Count(blocks), new(), "Include", null, blocks));
            return new(instances, []);
        }
        ReportDefinitionSnapshot Child(JsonObject item)
        {
            var key = StudioJson.RequiredString(item, "reportNId") + "/" + StudioJson.RequiredString(item, "revisionNId");
            if (!definition.Dependencies.TryGetValue(key, out var child) || child.Kind != "Report") throw new StudioException("DEPENDENCY_CHANGED", "固定子报告定义不存在。", 409, key);
            return StudioJson.Text(child.Document["mode"]) == "Query" ? child with { Document = ReportDesign.ApplyQuerySort(child.Document, null) } : child;
        }
        if (StudioJson.Text(definition.Document["mode"]) == "Simple")
        {
            foreach (var item in (definition.Document["items"] as JsonArray ?? []).OfType<JsonObject>().Where(i => i["enabled"]?.GetValue<bool>() != false))
            {
                token.ThrowIfCancellationRequested();
                var child = Child(item); var key = StudioJson.RequiredString(item, "id"); var values = ReportDataResolver.Map(item["parameterMap"], context);
                values = parameterResolver?.Invoke(child.Document, values) ?? values;
                var childSnapshot = await data.CaptureAsync(child.Document, values, trustedContext, token);
                var blocks = await ExpandAsync(child, ReportDataResolver.Context(values, childSnapshot.Instances, trustedContext), key, null, null, null, token);
                foreach (var block in blocks) block.Definition["paginationPart"] = new JsonObject { ["key"] = key, ["paper"] = child.Document["paper"]?.DeepClone(), ["pageNumberMode"] = StudioJson.Text(definition.Document["pageNumberMode"]) ?? "Preserve" };
                var count = Count(blocks);
                if (count == 0 && item["required"]?.GetValue<bool>() != false) throw new StudioException("DATA_REQUIRED", "必需组合项没有正文记录。", 422, "items." + key);
                instances.Add(new(key, key, null, null, null, StudioJson.RequiredString(item, "reportNId"), null, count, values, count > 0 ? "Include" : "Skip", count > 0 ? null : "SkippedEmpty", count > 0 ? blocks : []));
            }
            return new(instances, []);
        }
        var assembly = definition.Document["assembly"]!.AsObject();
        var orders = await data.CollectionAsync(assembly["root"]!.AsObject(), context, token);
        var wi = 0;
        foreach (var order in ReportDataResolver.Sort(orders, assembly["order"]))
        {
            var orderKey = Key(order, wi++); var orderContext = new Dictionary<string, JsonNode?>(context, StringComparer.Ordinal) { ["Root"] = order, ["Row"] = order };
            var operation = assembly["operation"]!.AsObject(); var operations = await data.CollectionAsync(operation["collection"]!.AsObject(), orderContext, token); var oi = 0;
            foreach (var op in ReportDataResolver.Sort(operations, operation["order"]))
            {
                var operationKey = Key(op, oi++); var opContext = new Dictionary<string, JsonNode?>(orderContext, StringComparer.Ordinal) { ["Row"] = op, ["ParentRow"] = op };
                foreach (var rule in (assembly["rules"] as JsonArray ?? []).OfType<JsonObject>().OrderBy(r => r["priority"]?.GetValue<int>() ?? 0).ThenBy(r => StudioJson.Text(r["id"]), StringComparer.Ordinal))
                {
                    token.ThrowIfCancellationRequested();
                    var ruleId = StudioJson.RequiredString(rule, "id"); var baseKey = orderKey + "/" + operationKey + "/" + ruleId;
                    void Skip(string reason) => instances.Add(new(baseKey, ruleId, orderKey, operationKey, null, StudioJson.RequiredString(rule, "reportNId"), null, 0, new(), "Skip", reason, []));
                    if (rule["when"] is JsonObject when && !StudioBinding.Evaluate(when, opContext)) { Skip("SkippedCondition"); continue; }
                    var repeat = rule["repeat"] is JsonObject;
                    var rows = await data.CollectionAsync((rule[repeat ? "repeat" : "presence"]!).AsObject(), opContext, token);
                    if (rows.Count == 0)
                    {
                        if (rule["required"]?.GetValue<bool>() == true) throw new StudioException("DATA_REQUIRED", "必需规则没有实际数据。", 422, "assembly.rules." + ruleId);
                        Skip("SkippedEmpty"); continue;
                    }
                    var child = Child(rule); var content = rule["content"]!.AsObject(); var ri = 0;
                    foreach (var row in repeat ? rows.AsEnumerable() : new[] { op })
                    {
                        var sourceKey = Key(row, ri++); var key = baseKey + "/" + sourceKey;
                        var rowContext = new Dictionary<string, JsonNode?>(opContext, StringComparer.Ordinal) { ["Row"] = row };
                        var values = ReportDataResolver.Map(rule["parameterMap"], rowContext);
                        values = parameterResolver?.Invoke(child.Document, values) ?? values;
                        var childSnapshot = await data.CaptureAsync(child.Document, values, trustedContext, token, StudioJson.Text(content["mode"]) == "BandSubtree" ? RequiredInstances(child.Document, StudioJson.RequiredString(content, "entryBandId")) : null);
                        var childContext = ReportDataResolver.Context(values, childSnapshot.Instances, trustedContext);
                        JsonNode? mappedRow = null; var ancestors = new Dictionary<string, JsonNode?>(); string? entry = null;
                        var mappings = new JsonObject { ["parameters"] = values.DeepClone() };
                        if (StudioJson.Text(content["mode"]) == "BandSubtree")
                        {
                            entry = StudioJson.RequiredString(content, "entryBandId");
                            var mapped = new JsonObject();
                            foreach (var map in (content["rowMap"] as JsonArray ?? []).OfType<JsonObject>()) Set(mapped, map["targetPath"]!.AsArray(), ReportDataResolver.Resolve(map["source"]!.AsObject(), rowContext));
                            mappedRow = mapped; mappings["row"] = mapped.DeepClone();
                            foreach (var map in (content["ancestorMap"] as JsonArray ?? []).OfType<JsonObject>()) ancestors[StudioJson.RequiredString(map, "bandId")] = ReportDataResolver.Resolve(map["source"]!.AsObject(), rowContext);
                            mappings["ancestors"] = new JsonObject(ancestors.Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone())));
                        }
                        var blocks = await ExpandAsync(child, childContext, key, entry, mappedRow, ancestors, token, electronic: true);
                        var count = Count(blocks);
                        if (count == 0 && rule["required"]?.GetValue<bool>() == true) throw new StudioException("DATA_REQUIRED", "映射后的必需模板没有正文记录。", 422, ruleId);
                        if (count > 0 && StudioJson.Text(rule["breakBefore"]) == "Page") blocks[0].Definition["bandBreakBefore"] = "Page";
                        instances.Add(new(key, ruleId, orderKey, operationKey, sourceKey, StudioJson.RequiredString(rule, "reportNId"), entry, count, mappings, count > 0 ? "Include" : "Skip", count > 0 ? null : "SkippedEmpty", count > 0 ? blocks : []));
                    }
                }
            }
        }
        return new(instances, []);
    }
    private static string Key(JsonNode? row, int ordinal) => ordinal.ToString(CultureInfo.InvariantCulture) + ":" + (StudioJson.Text((row as JsonObject)?["id"]) ?? StudioJson.Hash(StudioJson.Canonicalize(row))[..12]);
    private static HashSet<string> RequiredInstances(JsonObject document, string entry)
    {
        var result = new HashSet<string>(StringComparer.Ordinal); var ids = new HashSet<string>(StringComparer.Ordinal) { entry }; var bands = (document["bands"] as JsonArray ?? []).OfType<JsonObject>().ToArray(); var count = 0;
        while (count != ids.Count) { count = ids.Count; foreach (var band in bands.Where(b => ids.Contains(StudioJson.Text(b["parentBandId"]) ?? ""))) ids.Add(StudioJson.RequiredString(band, "id")); }
        void Visit(JsonNode? node)
        {
            if (node is JsonArray array) { foreach (var item in array) Visit(item); return; }
            if (node is not JsonObject obj || StudioJson.Text(obj["from"]) == "Constant") return;
            if (StudioJson.Text(obj["from"]) == "Instance" || StudioJson.Text(obj["mode"]) == "RootPath") result.Add(StudioJson.RequiredString(obj, "instanceId"));
            foreach (var property in obj) Visit(property.Value);
        }
        foreach (var band in bands.Where(b => ids.Contains(StudioJson.RequiredString(b, "id")))) { Visit(band["controls"]); if (StudioJson.Text(band["id"]) != entry) Visit(band["data"]); }
        return result;
    }
    private static int Count(IReadOnlyList<ReportFlowBlock> blocks) => blocks.Where(b => b.Definition["detailRow"]?.GetValue<bool>() == true).Select(b => b.ParentInstanceKey).Distinct(StringComparer.Ordinal).Count();
    private static void Set(JsonObject target, JsonArray path, JsonNode? value)
    {
        if (path.Count == 0) throw new StudioException("PATH_INVALID", "目标路径不能为空。", 422);
        var current = target;
        for (var i = 0; i < path.Count; i++)
        {
            var key = StudioJson.Text(path[i]);
            if (key is null or "__proto__" or "prototype" or "constructor") throw new StudioException("PATH_INVALID", "目标路径非法。", 422);
            if (i == path.Count - 1) { if (current.ContainsKey(key)) throw new StudioException("PATH_INVALID", "映射目标重叠。", 422); current[key] = value; }
            else { current[key] ??= new JsonObject(); current = current[key] as JsonObject ?? throw new StudioException("TYPE_MISMATCH", "对象映射与标量映射重叠。", 422); }
        }
    }
    private async Task<IReadOnlyList<ReportFlowBlock>> ExpandAsync(ReportDefinitionSnapshot snapshot, Dictionary<string, JsonNode?> context, string instance, string? entry, JsonNode? entryRow, Dictionary<string, JsonNode?>? ancestors, CancellationToken token, bool electronic = false)
    {
        var document = snapshot.Document;
        var bands = (document["bands"] as JsonArray ?? []).OfType<JsonObject>().Select(b => b.DeepClone().AsObject()).ToArray();
        var result = new List<ReportFlowBlock>(); var headers = new HashSet<string>(); var scopes = new Dictionary<string, JsonArray>(StringComparer.Ordinal);
        // Sibling groups have one unambiguous Detail between paired header/footer. Normalize them to that Detail's group boundary.
        foreach (var header in bands.Where(b => StudioJson.Text(b["kind"]) == "GroupHeader"))
        {
            var paired = bands.SingleOrDefault(b => StudioJson.Text(b["id"]) == StudioJson.Text(header["group"]?["pairedBandId"]) && StudioJson.Text(b["kind"]) == "GroupFooter");
            if (paired is null || StudioJson.Text(paired["parentBandId"]) != StudioJson.Text(header["parentBandId"])) throw new StudioException("REPORT_LAYOUT_UNSUPPORTED", "分组头尾必须在相同父实例内明确配对。", 422);
            var parentId = StudioJson.Text(header["parentBandId"]);
            if (bands.Any(b => StudioJson.Text(b["id"]) == parentId && StudioJson.Text(b["kind"]) == "Detail")) continue;
            var start = Array.IndexOf(bands, header); var end = Array.IndexOf(bands, paired);
            var details = bands.Where((b, i) => i > start && i < end && StudioJson.Text(b["parentBandId"]) == parentId && StudioJson.Text(b["kind"]) == "Detail").ToArray();
            if (end <= start || details.Length != 1) throw new StudioException("REPORT_LAYOUT_UNSUPPORTED", "同级分组头尾之间必须有唯一目标明细带。", 422);
            header["parentBandId"] = details[0]["id"]?.DeepClone(); paired["parentBandId"] = details[0]["id"]?.DeepClone();
        }
        async Task Emit(JsonObject band, Dictionary<string, JsonNode?> ctx, string rowKey, string parentKey, bool detail)
        {
            foreach (var control in (band["controls"] as JsonArray ?? []).OfType<JsonObject>())
            {
                token.ThrowIfCancellationRequested();
                if (control["visibleWhen"] is JsonObject when && !StudioBinding.Evaluate(when, ctx)) continue;
                var type = StudioJson.RequiredString(control, "type"); var controlId = StudioJson.RequiredString(control, "id");
                var definition = control.DeepClone().AsObject(); definition["detailRow"] = detail; definition["bandHeightMm"] = band["heightMm"]?.DeepClone(); definition["bandKind"] = band["kind"]?.DeepClone(); definition["bandKeepWithNext"] = band["keepWithNext"]?.DeepClone(); definition["paper"] = document["paper"]?.DeepClone(); definition["resolvedStyle"] = (document["styles"] as JsonArray ?? []).OfType<JsonObject>().SingleOrDefault(s => StudioJson.Text(s["id"]) == StudioJson.Text(control["styleId"]))?.DeepClone();
                JsonNode? value = !control.ContainsKey("aggregate") && control["value"] is JsonObject binding ? ReportDataResolver.Resolve(binding, ctx) : null;
                if (type == "CheckBox" && value is not null && (value is not JsonValue check || !check.TryGetValue<bool>(out _))) throw new StudioException("TYPE_MISMATCH", "复选框绑定必须返回布尔值或空值。", 422, controlId);
                if (type == "Html") definition["assetResourceNId"] = snapshot.ResourceNId;
                if (type == "Image") { var assetId = StudioJson.Text(value) ?? StudioJson.Text(value?["assetId"]); definition["frozenAsset"] = (document["assets"] as JsonArray ?? []).OfType<JsonObject>().SingleOrDefault(a => StudioJson.Text(a["id"]) == assetId)?.DeepClone() ?? throw new StudioException("REPORT_RESOURCE_UNAVAILABLE", "图片必须选择固定资产。", 422, controlId); definition["assetResourceNId"] = snapshot.ResourceNId; }
                if (control["aggregate"] is JsonObject aggregate)
                {
                    var scopeId = StudioJson.RequiredString(aggregate, "scopeBandId");
                    var rows = ctx.GetValueOrDefault("Scope:" + scopeId) as JsonArray ?? scopes.GetValueOrDefault(parentKey + "/" + scopeId) ?? throw new StudioException("PATH_INVALID", "当前父实例没有该合计作用域。", 422, scopeId);
                    var function = StudioJson.RequiredString(aggregate, "function");
                    if (function == "Count") value = JsonValue.Create(rows.Count.ToString(CultureInfo.InvariantCulture));
                    else
                    {
                        var numbers = new List<decimal>();
                        foreach (var row in rows)
                        {
                            var aggregateContext = new Dictionary<string, JsonNode?>(ctx, StringComparer.Ordinal) { ["Row"] = row };
                            var number = ReportDataResolver.Resolve(aggregate["field"]!.AsObject(), aggregateContext);
                            if (number is null) continue;
                            if (!decimal.TryParse(StudioJson.Text(number) ?? number.ToJsonString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var numeric)) throw new StudioException("TYPE_MISMATCH", "合计字段需要数字，空值不当零。", 422, controlId + ".aggregate.field");
                            numbers.Add(numeric);
                        }
                        decimal? calculated = function switch { "Sum" => numbers.Sum(), "Avg" => numbers.Count == 0 ? null : numbers.Average(), "Min" => numbers.Count == 0 ? null : numbers.Min(), "Max" => numbers.Count == 0 ? null : numbers.Max(), _ => throw new StudioException("TYPE_MISMATCH", "合计函数未登记。", 422) };
                        value = calculated is { } calculatedNumber ? JsonValue.Create(calculatedNumber.ToString(CultureInfo.InvariantCulture)) : null;
                    }
                }
                if (type == "Table")
                {
                    var table = control["table"]!.AsObject();
                    definition["tableStyles"] = new JsonObject((document["styles"] as JsonArray ?? []).OfType<JsonObject>().Select(s => KeyValuePair.Create<string, JsonNode?>(StudioJson.RequiredString(s, "id"), s.DeepClone())));
                    definition["columnStyles"] = new JsonObject((table["columns"] as JsonArray ?? []).OfType<JsonObject>().Select(column => KeyValuePair.Create<string, JsonNode?>(StudioJson.RequiredString(column, "id"), (document["styles"] as JsonArray ?? []).OfType<JsonObject>().SingleOrDefault(style => StudioJson.Text(style["id"]) == StudioJson.Text(column["styleId"]))?.DeepClone())));
                    var collection = StudioJson.Text(table["rowMode"]) == "Collection";
                    var rows = collection ? value as JsonArray ?? throw new StudioException("TYPE_MISMATCH", "表格集合绑定必须返回数组。", 422, controlId) : new JsonArray(ctx.GetValueOrDefault("Row")?.DeepClone());
                    var cells = new JsonArray();
                    foreach (var row in rows)
                    {
                        var columnContext = new Dictionary<string, JsonNode?>(ctx, StringComparer.Ordinal) { ["Row"] = row, ["ParentRow"] = collection ? ctx.GetValueOrDefault("Row") : ctx.GetValueOrDefault("ParentRow") };
                        var templates = table["rows"] as JsonArray;
                        for (var template = 0; template < (templates?.Count ?? 1); template++)
                            cells.Add(new JsonArray((table["columns"] as JsonArray ?? []).OfType<JsonObject>().Select(c => ReportDataResolver.Resolve(ReportTableTemplates.Binding(table, c, template), columnContext)).ToArray()));
                    }
                    value = cells;
                    var headerMode = StudioJson.Text(table["headerMode"]) ?? "OncePerParent";
                    definition["showHeader"] = headerMode != "Hidden" && (headerMode == "EveryInstance" || headers.Add(parentKey + "/" + controlId));
                }
                if (++_blocks > 100000) throw new StudioException("STUDIO_LIMIT", "展开块数超过100000预算。", 429);
                result.Add(new(instance + "/" + (++_sequence).ToString(CultureInfo.InvariantCulture), StudioJson.RequiredString(band, "id"), controlId, type, definition, value, rowKey, StudioJson.Text(band["kind"]) is "PageHeader" or "PageFooter"));
            }
            await Task.CompletedTask;
        }
        async Task Visit(JsonObject band, Dictionary<string, JsonNode?> parent, string parentKey, int depth, bool supplied = false)
        {
            if (depth > 32) throw new StudioException("STUDIO_LIMIT", "明细深度超过32预算。", 429);
            var id = StudioJson.RequiredString(band, "id"); var detail = StudioJson.Text(band["kind"]) == "Detail";
            var rows = detail ? supplied ? new JsonArray(entryRow?.DeepClone()) : await data.CollectionAsync(band["data"]!.AsObject(), parent, token) : new JsonArray((JsonNode?)null);
            var orderedRows = new JsonArray(ReportDataResolver.Sort(rows, band["sort"]).Select(r => r?.DeepClone()).ToArray());
            scopes[parentKey + "/" + id] = orderedRows;
            if (detail && rows.Count == 0 && !electronic && StudioJson.Text(band["emptyBehavior"]) == "Label")
            {
                result.Add(new(instance + "/empty/" + (++_sequence), id, "empty", "Text", new() { ["id"] = "empty", ["type"] = "Text", ["xMm"] = 0, ["yMm"] = 0, ["widthMm"] = StudioDocuments.ReportPaper(document["paper"]!.AsObject()).BodyWidth, ["heightMm"] = band["heightMm"]?.DeepClone(), ["bandHeightMm"] = band["heightMm"]?.DeepClone(), ["bandKind"] = band["kind"]?.DeepClone(), ["detailRow"] = false }, band["emptyText"]?.DeepClone(), parentKey + "/" + id + "/empty"));
            }
            var groupHeaders = bands.Where(b => StudioJson.Text(b["parentBandId"]) == id && StudioJson.Text(b["kind"]) == "GroupHeader").ToArray();
            if (groupHeaders.Length > 1) throw new StudioException("REPORT_LAYOUT_UNSUPPORTED", "同一明细的多组边界含糊，请使用多键或嵌套明细分组。", 422, id);
            var groupHeader = groupHeaders.SingleOrDefault(); var groupFooter = groupHeader is null ? null : bands.Single(b => StudioJson.Text(b["id"]) == StudioJson.Text(groupHeader["group"]?["pairedBandId"]));
            Dictionary<string, JsonNode?> RowContext(JsonNode? row)
            {
                var current = new Dictionary<string, JsonNode?>(parent, StringComparer.Ordinal);
                if (detail)
                {
                    current["ParentRow"] = parent.GetValueOrDefault("Row"); current["Row"] = row; current["Band:" + id] = row;
                    if (!current.ContainsKey("Root")) current["Root"] = row;
                }
                current["Scope:" + id] = orderedRows;
                return current;
            }
            var groupedRows = groupHeader is null ? [orderedRows] : orderedRows.GroupBy(row => StudioJson.Canonicalize(new JsonArray((groupHeader["group"]?["keys"] as JsonArray ?? []).OfType<JsonObject>().Select(key => ReportDataResolver.Resolve(key, RowContext(row))).ToArray())), StringComparer.Ordinal).Select(group => new JsonArray(group.Select(row => row?.DeepClone()).ToArray())).ToArray();
            var i = 0;
            foreach (var group in groupedRows)
            {
                if (group.Count == 0) continue;
                var groupContext = RowContext(group[0]); groupContext["Scope:" + id] = group;
                if (groupHeader is not null) { groupContext["Scope:" + StudioJson.RequiredString(groupHeader, "id")] = group; await Emit(groupHeader, groupContext, parentKey + "/group/" + i, parentKey, false); }
                Dictionary<string, JsonNode?>? last = null;
                foreach (var row in group)
                {
                    var key = parentKey + "/" + id + "/" + i++; var current = RowContext(row); current["Scope:" + id] = group;
                    await Emit(band, current, key, parentKey, detail);
                    foreach (var child in bands.Where(b => StudioJson.Text(b["parentBandId"]) == id && StudioJson.Text(b["kind"]) is not ("GroupHeader" or "GroupFooter"))) await Visit(child, current, key, depth + 1);
                    last = current;
                }
                if (groupFooter is not null && last is not null) { last["Scope:" + StudioJson.RequiredString(groupFooter, "id")] = group; await Emit(groupFooter, last, parentKey + "/group-footer/" + i, parentKey, false); }
            }
        }
        if (entry is not null)
        {
            var band = bands.SingleOrDefault(b => StudioJson.Text(b["id"]) == entry && StudioJson.Text(b["kind"]) == "Detail") ?? throw new StudioException("PATH_INVALID", "子模板入口明细带不存在。", 422, entry);
            var chain = new List<JsonObject>(); var cursor = band;
            while (StudioJson.Text(cursor["parentBandId"]) is { Length: > 0 } parent) { cursor = bands.Single(b => StudioJson.Text(b["id"]) == parent); chain.Insert(0, cursor); }
            foreach (var ancestor in chain)
            {
                var id = StudioJson.RequiredString(ancestor, "id");
                if (ancestors?.TryGetValue(id, out var row) == true) { context["Band:" + id] = row; context["Row"] = row; if (!context.ContainsKey("Root")) context["Root"] = row; }
            }
            await Visit(band, context, instance, chain.Count, supplied: true);
        }
        else foreach (var band in bands.Where(b => string.IsNullOrEmpty(StudioJson.Text(b["parentBandId"])) && (!electronic || StudioJson.Text(b["kind"]) is not ("PageHeader" or "PageFooter")))) await Visit(band, context, instance, 0);
        return result;
    }
}

public static class ReportExpander
{
    public static ReportContentFlow Expand(ReportAssemblyPlan plan)
    {
        var blocks = plan.Instances.Where(i => i.Decision == "Include").SelectMany(i => i.Blocks).ToArray();
        var markers = blocks.Where(b => !b.Repeat).Select(b => b.Marker).ToArray();
        if (markers.Distinct(StringComparer.Ordinal).Count() != markers.Length) throw new StudioException("REPORT_MARKER_DUPLICATE", "正文标记重复。", 422);
        return new(blocks, markers);
    }
}
