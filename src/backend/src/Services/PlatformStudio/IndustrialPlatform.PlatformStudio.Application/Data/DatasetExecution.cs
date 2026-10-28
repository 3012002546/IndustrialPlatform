using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Contracts;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Application.Data;

/// <summary>最多32MiB的短期结果缓存；键包含租户、主体、授权/工厂指纹、修订及完整参数。</summary>
public sealed class StudioDatasetCache
{
    private sealed record Entry(JsonObject Result, DateTimeOffset ExpiresOn, int Bytes);
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private int _bytes;
    public JsonObject? Get(string key, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var value)) return null;
            if (value.ExpiresOn <= now) { _entries.Remove(key); _bytes -= value.Bytes; return null; }
            return value.Result.DeepClone().AsObject();
        }
    }
    public void Put(string key, JsonObject result, DateTimeOffset expires)
    {
        var bytes = Encoding.UTF8.GetByteCount(result.ToJsonString());
        if (bytes > 4 * 1024 * 1024) return;
        lock (_gate)
        {
            if (_entries.Remove(key, out var previous)) _bytes -= previous.Bytes;
            while (_entries.Count > 0 && (_entries.Count >= 64 || _bytes + bytes > 32 * 1024 * 1024))
            {
                var oldest = _entries.MinBy(pair => pair.Value.ExpiresOn);
                _entries.Remove(oldest.Key); _bytes -= oldest.Value.Bytes;
            }
            _entries[key] = new(result.DeepClone().AsObject(), expires, bytes); _bytes += bytes;
        }
    }
}

/// <summary>设计态试查与发布态执行共用参数/字段边界；运行端绝不接收SQL、URL或凭据。</summary>
public sealed class DatasetExecution(IStudioResourceStore store, StudioPublishing publishing, IStudioAuthorization authorization, IStudioSourceExecutor executor, StudioDatasetCache cache, TimeProvider clock)
{
    public async Task<JsonObject> PreviewAsync(StudioPrincipal principal, DatasetPreview request, CancellationToken cancellationToken)
    {
        authorization.RequirePermission(principal, "studio.dataset.design"); authorization.RequirePermission(principal, "studio.source.test");
        var document = request.Document ?? throw new StudioException("STUDIO_VALIDATION", "试查配置缺失。", path: "document");
        if (StudioJson.Text(document["sourceNId"]) != request.SourceNId || StudioJson.Text(document["sourceConfigurationVersion"]) != request.SourceConfigurationVersion) throw new StudioException("SOURCE_CHANGED", "试查来源与文档合同不一致。", 409);
        return await ExecuteDocumentAsync(principal, "draft", document, request.Parameters ?? new(), true, null, null, cancellationToken);
    }
    public async Task<JsonObject> ExecuteAsync(StudioPrincipal principal, string nId, DatasetExecute request, CancellationToken cancellationToken, bool bypassCache = false)
    {
        var (resource, revision) = await publishing.RequireRuntimeAsync(principal, nId, request.RevisionNId, cancellationToken);
        if (resource.Kind != "Dataset") throw new StudioException("STUDIO_NOT_FOUND", "数据集不存在。", 404);
        return await ExecuteDocumentAsync(principal, revision.NId, StudioPublishing.Parse(revision.DocumentJson), request.Parameters ?? new(), false, request.Page, request.PageSize, cancellationToken, bypassCache);
    }
    public async Task<JsonObject> ExecuteDocumentAsync(StudioPrincipal principal, string revisionNId, JsonObject document, JsonObject supplied, bool preview, int? page, int? pageSize, CancellationToken cancellationToken, bool bypassCache = false)
    {
        var diagnostics = StudioDocuments.Validate("Dataset", document);
        if (diagnostics.FirstOrDefault(item => item.Severity == "Error") is { } error) throw new StudioException(error.Code, error.Message, path: error.Path);
        var seconds = document["cacheSeconds"]?.GetValue<int>() ?? 0;
        var timeout = document["timeoutSeconds"]?.GetValue<int>() ?? 30;
        var maxRows = document["maxRows"]?.GetValue<int>() ?? 10000;
        var maxBytes = StudioJson.Version(StudioJson.Text(document["maxBytes"]) ?? "10485760", "maxBytes");
        if (seconds is < 0 or > 60 || timeout is < 1 or > 120 || maxRows is < 1 or > 100000 || maxBytes is < 1 or > 52428800) throw new StudioException("STUDIO_VALIDATION", "数据集预算不在允许范围。", path: "budget");
        if (Encoding.UTF8.GetByteCount(supplied.ToJsonString()) > 65536) throw new StudioException("STUDIO_LIMIT", "查询参数超过64KiB。", 429);
        var parameters = supplied.DeepClone().AsObject();
        JsonObject? pageInfo = null;
        if (document["pagination"] is JsonObject pagination)
        {
            var size = pageSize ?? pagination["defaultSize"]?.GetValue<int>() ?? 20;
            var maximum = pagination["maxSize"]?.GetValue<int>() ?? 200;
            if ((page ?? 1) < 1 || size < 1 || size > maximum || maximum > 200) throw new StudioException("STUDIO_VALIDATION", "分页超出声明预算。", path: "page");
            var input = pagination["input"] as JsonObject ?? throw new StudioException("STUDIO_VALIDATION", "分页输入映射缺失。", path: "pagination.input");
            var limitName = StudioJson.RequiredString(input, "limitParameter");
            void SetPageParameter(string name, long value)
            {
                var definition = (document["parameters"] as JsonArray ?? []).OfType<JsonObject>().SingleOrDefault(p => StudioJson.Text(p["name"]) == name) ?? throw new StudioException("STUDIO_VALIDATION", "分页参数未在合同声明。", path: "pagination.input");
                parameters[name] = StudioJson.Text(definition["type"]) switch
                {
                    "Int32" when value <= int.MaxValue => JsonValue.Create((int)value),
                    "Int64" => JsonValue.Create(value.ToString(CultureInfo.InvariantCulture)),
                    _ => throw new StudioException("TYPE_MISMATCH", "分页输入须声明可容纳该值的整数类型。", path: "pagination.input"),
                };
            }
            SetPageParameter(limitName, size);
            var mode = StudioJson.Text(pagination["mode"]);
            if (mode == "Offset")
            {
                var offsetName = StudioJson.RequiredString(input, "offsetParameter");
                SetPageParameter(offsetName, ((long)(page ?? 1) - 1) * size);
            }
            else if (mode != "Cursor") throw new StudioException("STUDIO_VALIDATION", "分页模式未登记。", path: "pagination.mode");
            pageInfo = new() { ["mode"] = mode, ["page"] = mode == "Offset" ? page ?? 1 : null, ["pageSize"] = size };
        }
        else if (page is < 1 || pageSize is < 1 or > 200) throw new StudioException("STUDIO_VALIDATION", "本地分页参数无效。", path: "page");
        parameters = DatasetValues.Parameters(principal, document, parameters);
        var source = await store.FindSourceAsync(principal.TenantNId, StudioJson.RequiredString(document, "sourceNId"), cancellationToken) ?? throw new StudioException("STUDIO_NOT_FOUND", "来源不存在。", 404);
        if (!source.IsEnabled || source.ConfigurationVersion != StudioJson.Version(StudioJson.Text(document["sourceConfigurationVersion"]))) throw new StudioException("SOURCE_CHANGED", "来源未启用或合同已改变。", 409);
        // Cache hits must pass the same live registry check as a source execution.
        if (!preview && seconds > 0) publishing.ValidateSourceConfiguration(source);
        var fingerprint = StudioJson.Hash(principal.TenantNId + "|" + principal.SubjectNId + "|" + principal.AuthVersion + "|" + principal.PolicyVersion + "|" + string.Join('|', principal.Permissions.Order(StringComparer.Ordinal)) + "|" + string.Join('|', principal.Roles.Order(StringComparer.Ordinal)) + "|" + string.Join('|', principal.FactoryNIds.Order(StringComparer.Ordinal)));
        var key = fingerprint + "/" + revisionNId + "/" + source.EndpointConfigurationHash + "/" + StudioJson.Hash(StudioJson.Canonicalize(document)) + "/" + StudioJson.Hash(StudioJson.Canonicalize(parameters));
        if (!preview && !bypassCache && seconds > 0 && cache.Get(key, clock.GetUtcNow()) is { } cached) { cached["requestId"] = "request-" + Guid.NewGuid().ToString("N"); return cached; }
        var query = document["query"]!.AsObject();
        var resolved = MapQuery(query, parameters, principal);
        var result = await executor.ExecuteAsync(principal, source, document, resolved, preview, cancellationToken);
        var fieldIds = (document["readableFieldIds"] as JsonArray ?? []).Select(StudioJson.Text).Where(value => value is not null).Select(value => value!).ToHashSet(StringComparer.Ordinal);
        var projected = DatasetValues.Project(result.Data, document["result"]!.AsObject(), fieldIds);
        if (Encoding.UTF8.GetByteCount(projected?.ToJsonString() ?? "null") > maxBytes) throw new StudioException("STUDIO_LIMIT", "结果超过数据集字节预算。", 429);
        if (pageInfo is not null)
        {
            var outputPagination = document["pagination"]!.AsObject();
            var output = outputPagination["result"] as JsonObject ?? throw new StudioException("STUDIO_VALIDATION", "分页输出映射缺失。", path: "pagination.result");
            var mode = StudioJson.Text(outputPagination["mode"]);
            var totalPath = StudioJson.Text(output["totalCountPath"]);
            if (mode == "Offset")
            {
                if (totalPath is null) throw new StudioException("STUDIO_VALIDATION", "页码分页必须声明真实totalCountPath。", path: "pagination.result");
                var totalValue = StudioJson.ReadPath(result.Data, totalPath);
                var total = StudioJson.Text(totalValue) ?? totalValue?.ToJsonString();
                pageInfo["totalCount"] = StudioJson.Version(total, "pageInfo.totalCount").ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                var cursorPath = StudioJson.RequiredString(output, "nextCursorPath");
                pageInfo["nextCursor"] = StudioJson.ReadPath(result.Data, cursorPath);
            }
        }
        var outputResult = new JsonObject
        {
            ["schemaVersion"] = 1, ["revisionNId"] = revisionNId, ["requestId"] = "request-" + Guid.NewGuid().ToString("N"),
            ["data"] = projected, ["observedOn"] = result.ObservedOn.ToString("O", CultureInfo.InvariantCulture), ["receivedOn"] = result.ReceivedOn.ToString("O", CultureInfo.InvariantCulture),
            ["sourceVersion"] = null, ["consistency"] = "ObservedAtDifferentTimes", ["quality"] = Quality(document, result.Data),
            ["elapsedMs"] = result.ElapsedMs, ["rowCount"] = result.RowCount.ToString(CultureInfo.InvariantCulture), ["truncated"] = false,
        };
        if (pageInfo is not null) outputResult["pageInfo"] = pageInfo;
        if (preview) outputResult["diagnostics"] = new JsonObject { ["resolvedParameters"] = resolved.DeepClone(), ["preview"] = true, ["maxSampleRows"] = 100 };
        if (!preview && !bypassCache && seconds > 0) cache.Put(key, outputResult, clock.GetUtcNow().AddSeconds(seconds));
        return outputResult;
    }
    private static JsonObject MapQuery(JsonObject query, JsonObject parameters, StudioPrincipal principal)
    {
        var mapping = query["parameterMap"] as JsonArray ?? query["parameters"] as JsonArray;
        if (mapping is null) return parameters.DeepClone().AsObject();
        var output = new JsonObject();
        var contexts = new Dictionary<string, JsonNode?> { ["PageParam"] = parameters, ["Context"] = new JsonObject { ["tenant"] = principal.TenantNId, ["user"] = principal.SubjectNId, ["authorizedFactoryIds"] = new JsonArray(principal.FactoryNIds.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray()) } };
        foreach (var item in mapping)
        {
            if (item is JsonObject row && row["source"] is JsonObject binding) output[StudioJson.RequiredString(row, "target")] = StudioBinding.Resolve(binding, contexts);
            else
            {
                var name = item is JsonObject definition ? StudioJson.RequiredString(definition, "name") : StudioJson.Text(item) ?? throw new StudioException("STUDIO_VALIDATION", "参数映射配置无效。");
                if (!parameters.ContainsKey(name)) throw new StudioException("PARAM_REQUIRED", "查询参数映射指向未声明参数。", path: "query.parameters." + name);
                output[name] = parameters[name]?.DeepClone();
            }
        }
        return output;
    }
    private JsonObject Quality(JsonObject document, JsonNode? data)
    {
        var freshness = document["freshness"] as JsonObject;
        if (StudioJson.Text(freshness?["mode"]) != "SourceTime") return new() { ["status"] = "Unknown", ["basis"] = "AcquisitionOnly", ["reasonCode"] = "SOURCE_TIME_NOT_PROVIDED" };
        var path = StudioJson.Text(freshness?["timePath"]);
        var timeText = path is null ? null : StudioJson.Text(StudioJson.ReadPath(data, path));
        if (timeText is null || !System.Text.RegularExpressions.Regex.IsMatch(timeText, @"(Z|[+-]\d{2}:\d{2})$", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) || !DateTimeOffset.TryParse(timeText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var observed)) return new() { ["status"] = "Unknown", ["basis"] = "SourceTime", ["reasonCode"] = "SOURCE_TIME_INVALID" };
        var age = freshness?["maxAgeSeconds"]?.GetValue<int>() ?? 60;
        if (age is < 1 or > 86400 || observed > clock.GetUtcNow().AddMinutes(1)) return new() { ["status"] = "Unknown", ["basis"] = "SourceTime", ["reasonCode"] = "SOURCE_TIME_INVALID" };
        return new() { ["status"] = clock.GetUtcNow() - observed > TimeSpan.FromSeconds(age) ? "Stale" : "Fresh", ["basis"] = "SourceTime", ["sourceObservedOn"] = observed.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) };
    }
}
