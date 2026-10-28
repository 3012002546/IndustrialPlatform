using System.Text;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Data;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Contracts;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Application.Dashboard;

public sealed record DashboardNavigationRequest(
    string? RevisionNId,
    string? RuleId,
    JsonObject? RowKey,
    JsonObject? Parameters,
    JsonObject? Filters = null,
    JsonObject? Viewport = null,
    bool IncludeRuntime = false);

/// <summary>看板运行、试运行和行跳转的唯一应用入口；每个页面实例最多执行一次Dataset请求。</summary>
public sealed class DashboardNavigation(
    IStudioResourceStore store,
    StudioPublishing publishing,
    DatasetExecution datasets,
    IStudioAuthorization authorization)
{
    public async Task<JsonObject> DefinitionAsync(StudioPrincipal principal, string nId, string? revisionNId, CancellationToken ct)
    {
        var (resource, revision) = await RequireDashboardAsync(principal, nId, revisionNId, ct);
        return new JsonObject { ["resourceNId"] = resource.NId, ["revisionNId"] = revision.NId, ["document"] = StudioPublishing.Parse(revision.DocumentJson) };
    }
    public async Task<JsonObject> RuntimeAsync(StudioPrincipal principal, string nId, string? revisionNId, JsonObject parameters, CancellationToken ct, string[]? instanceIds = null)
    {
        var (resource, revision) = await RequireDashboardAsync(principal, nId, revisionNId, ct);
        return await RuntimeAuthorizedAsync(principal, resource, revision, parameters, ct, instanceIds);
    }

    /// <summary>仅供同次请求刚完成RequireRuntimeAsync的资源使用；不缓存授权，数据依赖仍逐项校验。</summary>
    internal async Task<JsonObject> RuntimeAuthorizedAsync(StudioPrincipal principal, StudioResource resource, StudioRevision revision,
        JsonObject parameters, CancellationToken ct, string[]? instanceIds = null)
    {
        if (resource.Kind != "Dashboard" || resource.TenantNId != principal.TenantNId || revision.ResourceId != resource.Id)
            throw new StudioException("STUDIO_NOT_FOUND", "看板不存在。", 404);
        var document = StudioPublishing.Parse(revision.DocumentJson);
        var instances = await ExecuteDocumentAsync(principal, document, parameters, ct, requestedIds: instanceIds);
        return await RuntimeEnvelopeAsync(resource, revision, document, instances, ct);
    }

    public async Task<JsonObject> PreviewAsync(StudioPrincipal principal, string? nId, JsonObject document, JsonObject parameters, string? selectedComponentId, JsonObject? sampleRow, CancellationToken ct, string[]? instanceIds = null)
    {
        authorization.RequirePermission(principal, "studio.dashboard.design");
        if (nId is not null) _ = await publishing.RequireResourceAsync(principal, nId, "design", "Design", ct);
        var diagnostics = StudioDocuments.Validate("Dashboard", document);
        if (diagnostics.FirstOrDefault(item => item.Severity == "Error") is { } error)
            throw new StudioException(error.Code, error.Message, path: error.Path);
        var instances = await ExecuteDocumentAsync(principal, document, parameters, ct, sampleRow, instanceIds);
        var result = new JsonObject
        {
            ["revisionNId"] = "draft",
            ["document"] = document.DeepClone(),
            ["instances"] = instances,
            ["preview"] = true,
            ["selectedComponentId"] = selectedComponentId,
            ["diagnostics"] = new JsonArray(),
        };
        return result;
    }

    public async Task<JsonObject> ResolveAsync(StudioPrincipal principal, string nId, DashboardNavigationRequest request, CancellationToken ct, bool dashboardOnly = false, string[]? targetInstanceIds = null, string? knownTargetRevisionNId = null)
    {
        var stage = Stopwatch.GetTimestamp();
        var (resource, revision) = await RequireDashboardAsync(principal, nId, request.RevisionNId, ct);
        StudioRuntimeTiming.Mark("source-access", stage);
        var document = StudioPublishing.Parse(revision.DocumentJson);
        return await ResolveDocumentAsync(principal, resource, revision, document, request, ct, dashboardOnly, targetInstanceIds, knownTargetRevisionNId);
    }

    /// <summary>仅复用同次RequireRuntimeAsync刚校验的根资源，继续执行原导航规则、行和目标权限校验。</summary>
    internal Task<JsonObject> ResolveAuthorizedScreenSourceAsync(StudioPrincipal principal, StudioResource resource, StudioRevision revision,
        DashboardNavigationRequest request, string[]? instanceIds, string? knownRevisionNId, CancellationToken ct)
    {
        if (resource.Kind != "Dashboard" || resource.TenantNId != principal.TenantNId || revision.ResourceId != resource.Id)
            throw new StudioException("STUDIO_NOT_FOUND", "看板不存在。", 404);
        return ResolveDocumentAsync(principal, resource, revision, StudioPublishing.Parse(revision.DocumentJson), request, ct, true, instanceIds, knownRevisionNId);
    }

    public async Task<JsonObject> PreviewResolveAsync(StudioPrincipal principal, string nId, JsonObject document, DashboardNavigationRequest request, CancellationToken ct)
    {
        var resource = await publishing.RequireResourceAsync(principal, nId, "design", "Design", ct);
        if (resource.Kind != "Dashboard") throw new StudioException("STUDIO_NOT_FOUND", "看板不存在。", 404);
        var plan = await publishing.ValidateAsync(principal, nId, document, ct);
        if (!plan.Valid) throw new StudioException(plan.Diagnostics[0].Code, plan.Diagnostics[0].Message, path: plan.Diagnostics[0].Path);
        var result = await ResolveDocumentAsync(principal, resource, null, document, request, ct);
        var target = (document["navigation"] as JsonArray ?? []).OfType<JsonObject>().Single(item => StudioJson.Text(item["id"]) == request.RuleId)["target"];
        if (StudioJson.Text(target?["kind"]) == "Dashboard")
        {
            var parameters = result["parameters"]!.AsObject();
            var runtime = result["targetRuntime"]?.DeepClone().AsObject() ?? await RuntimeAsync(principal, StudioJson.RequiredString(result, "targetResourceNId"), StudioJson.RequiredString(result, "targetRevisionNId"), parameters, ct);
            result["targetRuntime"] = runtime;
            var queryParameters = new JsonObject();
            var contexts = new Dictionary<string, JsonNode?> { ["PageParam"] = PageParameters(principal, runtime["document"]!.AsObject(), parameters), ["Context"] = new JsonObject { ["tenant"] = principal.TenantNId, ["user"] = principal.SubjectNId, ["authorizedFactoryIds"] = new JsonArray(principal.FactoryNIds.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray()) } };
            foreach (var instance in (runtime["document"]?["instances"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var mapped = new JsonObject();
                foreach (var mapping in (instance["parameters"] as JsonArray ?? []).OfType<JsonObject>())
                    mapped[StudioJson.RequiredString(mapping, "target")] = StudioBinding.Resolve(mapping["source"]!.AsObject(), contexts);
                var id = StudioJson.RequiredString(instance, "instanceId");
                queryParameters[id] = mapped;
                contexts["Instance:" + id] = runtime["instances"]?[id]?["data"]?.DeepClone();
            }
            result["targetQueryParameters"] = queryParameters;
        }
        return result;
    }

    private async Task<JsonObject> ResolveDocumentAsync(StudioPrincipal principal, StudioResource resource, StudioRevision? revision, JsonObject document, DashboardNavigationRequest request, CancellationToken ct, bool dashboardOnly = false, string[]? targetInstanceIds = null, string? knownTargetRevisionNId = null)
    {
        var ruleId = StudioJson.RequiredString(new JsonObject { ["ruleId"] = request.RuleId }, "ruleId");
        var rule = (document["navigation"] as JsonArray ?? []).OfType<JsonObject>().SingleOrDefault(item => StudioJson.Text(item["id"]) == ruleId)
            ?? throw new StudioException("STUDIO_NOT_FOUND", "导航规则不存在。", 404, "ruleId");
        var target = rule["target"] as JsonObject ?? throw new StudioException("STUDIO_VALIDATION", "导航目标缺失。", path: "target");
        if (dashboardOnly && StudioJson.Text(target["kind"]) != "Dashboard")
            throw new StudioException("STUDIO_FORBIDDEN", "独立大屏只允许进入已配置的看板明细。", 403);
        var pageParameters = PageParameters(principal, document, request.Parameters ?? new JsonObject());
        var stage = Stopwatch.GetTimestamp();
        var instances = await ExecuteDocumentAsync(principal, document, pageParameters, ct, requestedIds: NavigationInstanceIds(document, rule));
        stage = StudioRuntimeTiming.Mark("source-data", stage);
        var contexts = new Dictionary<string, JsonNode?>
        {
            ["PageParam"] = pageParameters,
            ["Context"] = new JsonObject
            {
                ["tenant"] = principal.TenantNId,
                ["user"] = principal.SubjectNId,
                ["authorizedFactoryIds"] = new JsonArray(principal.FactoryNIds.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray()),
            },
        };
        foreach (var (instanceId, execution) in instances)
            contexts["Instance:" + instanceId] = execution is JsonObject instance
                ? instance["data"]?.DeepClone() ?? instance.DeepClone()
                : execution?.DeepClone();
        var row = StudioJson.Text(rule["event"]) == "RowClick"
            ? ResolveRow(document, rule, request.RowKey, instances, contexts)
            : null;
        contexts["Row"] = row;
        StudioRuntimeTiming.Mark("row-check", stage);
        if (rule["enabledWhen"] is JsonObject condition && !StudioBinding.Evaluate(condition, contexts))
            throw new StudioException("STUDIO_FORBIDDEN", "当前条件不允许执行导航。", 403);
        var parameters = new JsonObject();
        foreach (var map in (rule["parameterMap"] as JsonArray ?? []).OfType<JsonObject>())
            parameters[StudioJson.RequiredString(map, "target")] = StudioBinding.Resolve(map["source"]?.AsObject() ?? throw new StudioException("STUDIO_VALIDATION", "导航参数来源缺失。"), contexts);
        if (Encoding.UTF8.GetByteCount(parameters.ToJsonString()) > 16 * 1024) throw new StudioException("STUDIO_LIMIT", "导航参数超过16KiB限制。", 429);

        var targetKind = StudioJson.RequiredString(target, "kind");
        var targetNId = StudioJson.RequiredString(target, "resourceNId");
        if (targetKind is "BusinessPage")
            throw new StudioException("STUDIO_MODULE_UNAVAILABLE", "该业务页面尚未登记Studio跳转目标。", 409);
        var fixedRevision = StudioJson.Text(target["revisionNId"]);
        if (string.IsNullOrWhiteSpace(fixedRevision)) fixedRevision = null;
        if (fixedRevision is null && revision is not null)
            fixedRevision = FixedDependencyRevision(revision, targetNId);
        if (fixedRevision is null) throw new StudioException("DEPENDENCY_CHANGED", "导航目标没有可用发布修订。", 409);
        stage = Stopwatch.GetTimestamp();
        var (validatedTarget, targetRevision) = await publishing.RequireRuntimeAsync(principal, targetNId, fixedRevision, ct);
        StudioRuntimeTiming.Mark("target-access", stage);
        if (validatedTarget.Kind != targetKind) throw new StudioException("DEPENDENCY_CHANGED", "导航目标资源类型已改变。", 409);
        JsonObject? targetRuntime = null;
        if (request.IncludeRuntime && targetKind == "Dashboard")
        {
            var targetDocument = StudioPublishing.Parse(targetRevision.DocumentJson);
            stage = Stopwatch.GetTimestamp();
            var targetInstances = await ExecuteDocumentAsync(principal, targetDocument, parameters, ct,
                requestedIds: knownTargetRevisionNId == targetRevision.NId ? targetInstanceIds : null);
            stage = StudioRuntimeTiming.Mark("target-data", stage);
            targetRuntime = await RuntimeEnvelopeAsync(validatedTarget, targetRevision, targetDocument, targetInstances, ct);
            StudioRuntimeTiming.Mark("target-envelope", stage);
        }
        var result = new JsonObject
        {
            ["targetResourceNId"] = targetNId,
            ["targetRevisionNId"] = fixedRevision,
            ["parameters"] = parameters,
            ["source"] = new JsonObject { ["resourceNId"] = resource.NId, ["revisionNId"] = revision?.NId ?? "draft", ["ruleId"] = ruleId, ["rowKey"] = request.RowKey?.DeepClone(), ["row"] = row?.DeepClone() },
            ["navigationState"] = new JsonObject
            {
                ["parameters"] = pageParameters.DeepClone(),
                ["filters"] = request.Filters?.DeepClone() ?? new JsonObject(),
                ["viewport"] = request.Viewport?.DeepClone() ?? new JsonObject(),
            },
            ["openMode"] = StudioJson.Text(rule["openMode"]) ?? "Current",
        };
        if (targetRuntime is not null) result["targetRuntime"] = targetRuntime;
        return result;
    }

    public async Task<JsonObject> PlaylistRuntimeAsync(StudioPrincipal principal, string nId, string? revisionNId, CancellationToken ct)
    {
        var (resource, revision) = await publishing.RequireRuntimeAsync(principal, nId, revisionNId, ct);
        if (resource.Kind != "Playlist") throw new StudioException("STUDIO_NOT_FOUND", "轮播不存在。", 404);
        var document = StudioPublishing.Parse(revision.DocumentJson);
        var diagnostics = StudioDocuments.Validate("Playlist", document);
        if (diagnostics.FirstOrDefault(item => item.Severity == "Error") is { } error) throw new StudioException(error.Code, error.Message, path: error.Path);
        var items = new JsonArray();
        foreach (var item in (document["items"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (item["enabled"]?.GetValue<bool>() == false) continue;
            var dashboardNId = StudioJson.RequiredString(item, "dashboardNId");
            var fixedRevision = StudioJson.Text(item["revisionNId"]);
            if (string.IsNullOrWhiteSpace(fixedRevision)) fixedRevision = null;
            if (fixedRevision is null) fixedRevision = FixedDependencyRevision(revision, dashboardNId);
            if (fixedRevision is null) throw new StudioException("DEPENDENCY_CHANGED", "轮播项目标没有可用发布修订。", 409);
            // Individual dashboard access/availability is checked when loading the item,
            // so one unavailable item can follow the playlist's Skip/Pause policy.
            var output = item.DeepClone().AsObject();
            output["revisionNId"] = fixedRevision;
            items.Add(output);
        }
        return new JsonObject
        {
            ["resource"] = JsonSerializer.SerializeToNode(await publishing.ToDtoAsync(resource, revision, ct), StudioCommands.JsonOptions),
            ["revisionNId"] = revision.NId,
            ["document"] = document,
            ["items"] = items,
            ["loop"] = document["loop"]?.DeepClone() ?? JsonValue.Create(true),
            ["onFailure"] = document["onFailure"]?.DeepClone() ?? JsonValue.Create("Pause"),
            ["preloadCount"] = document["preloadCount"]?.DeepClone() ?? JsonValue.Create(1),
        };
    }

    internal static JsonObject ScreenPlaylistParameters(StudioPrincipal principal, StudioRevision revision, JsonObject parameters) =>
        PageParameters(principal, StudioPublishing.Parse(revision.DocumentJson), parameters);

    internal static JsonObject ScreenPlaylistManifest(StudioResource resource, StudioRevision revision)
    {
        var document = StudioPublishing.Parse(revision.DocumentJson);
        return new JsonObject
        {
            ["kind"] = "Playlist",
            ["resource"] = new JsonObject { ["nId"] = resource.NId, ["name"] = resource.Name, ["kind"] = resource.Kind },
            ["revisionNId"] = revision.NId,
            ["loop"] = document["loop"]?.DeepClone() ?? JsonValue.Create(true),
            ["onFailure"] = document["onFailure"]?.DeepClone() ?? JsonValue.Create("Pause"),
            ["preloadCount"] = document["preloadCount"]?.DeepClone() ?? JsonValue.Create(1),
            ["items"] = new JsonArray((document["items"] as JsonArray ?? []).OfType<JsonObject>()
                .Where(item => item["enabled"]?.GetValue<bool>() != false)
                .Select(item => (JsonNode)new JsonObject { ["id"] = item["id"]?.DeepClone(), ["label"] = item["label"]?.DeepClone(), ["staySeconds"] = item["staySeconds"]?.DeepClone() }).ToArray()),
        };
    }

    internal async Task<(StudioResource Resource, StudioRevision Revision, JsonObject Parameters)> ResolveScreenPlaylistItemAsync(
        StudioPrincipal principal, StudioRevision playlistRevision, string itemId, JsonObject parameters, CancellationToken ct)
    {
        var document = StudioPublishing.Parse(playlistRevision.DocumentJson);
        var item = (document["items"] as JsonArray ?? []).OfType<JsonObject>()
            .SingleOrDefault(item => StudioJson.Text(item["id"]) == itemId && item["enabled"]?.GetValue<bool>() != false)
            ?? throw new StudioException("STUDIO_NOT_FOUND", "轮播项目不存在。", 404);
        var targetNId = StudioJson.RequiredString(item, "dashboardNId");
        var revisionNId = StudioJson.Text(item["revisionNId"]);
        if (string.IsNullOrWhiteSpace(revisionNId)) revisionNId = FixedDependencyRevision(playlistRevision, targetNId);
        if (revisionNId is null) throw new StudioException("DEPENDENCY_CHANGED", "轮播项目标没有可用发布修订。", 409);
        var target = await RequireDashboardAsync(principal, targetNId, revisionNId, ct);
        var mapped = new JsonObject();
        var contexts = new Dictionary<string, JsonNode?> { ["PageParam"] = parameters };
        foreach (var map in (item["parameterMap"] as JsonArray ?? []).OfType<JsonObject>())
            mapped[StudioJson.RequiredString(map, "target")] = StudioBinding.Resolve(map["source"]!.AsObject(), contexts);
        return (target.Resource, target.Revision, PageParameters(principal, StudioPublishing.Parse(target.Revision.DocumentJson), mapped));
    }

    private async Task<(StudioResource Resource, StudioRevision Revision)> RequireDashboardAsync(StudioPrincipal principal, string nId, string? revisionNId, CancellationToken ct)
    {
        var result = await publishing.RequireRuntimeAsync(principal, nId, revisionNId, ct);
        if (result.Resource.Kind != "Dashboard") throw new StudioException("STUDIO_NOT_FOUND", "看板不存在。", 404);
        return result;
    }

    private async Task<JsonObject> RuntimeEnvelopeAsync(StudioResource resource, StudioRevision revision, JsonObject document, JsonObject instances, CancellationToken ct) => new()
    {
        ["resource"] = JsonSerializer.SerializeToNode(await publishing.ToDtoAsync(resource, revision, ct), StudioCommands.JsonOptions),
        ["revisionNId"] = revision.NId,
        ["document"] = document,
        ["instances"] = instances,
        ["diagnostics"] = new JsonArray(),
        ["transport"] = "Connected",
        ["sourceFreshness"] = Freshness(instances),
    };

    private static JsonObject Freshness(JsonObject instances)
    {
        var result = new JsonObject();
        foreach (var (instanceId, value) in instances)
        {
            var execution = value as JsonObject;
            var quality = execution?["quality"] as JsonObject;
            var freshness = new JsonObject
            {
                ["status"] = quality?["status"]?.DeepClone() ?? JsonValue.Create("Unknown"),
                ["reasonCode"] = quality?["reasonCode"]?.DeepClone(),
                ["sourceObservedOn"] = quality?["sourceObservedOn"]?.DeepClone(),
                ["observedOn"] = execution?["observedOn"]?.DeepClone(),
                ["receivedOn"] = execution?["receivedOn"]?.DeepClone(),
            };
            result[instanceId] = freshness;
        }
        return result;
    }

    private static JsonObject PageParameters(StudioPrincipal principal, JsonObject document, JsonObject parameters) =>
        DatasetValues.Parameters(principal, new JsonObject { ["parameters"] = document["parameters"]?.DeepClone() ?? new JsonArray() }, parameters);

    private static string[] NavigationInstanceIds(JsonObject document, JsonObject rule)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Collect(JsonNode? node)
        {
            if (node is JsonArray array) { foreach (var item in array) Collect(item); return; }
            if (node is not JsonObject obj) return;
            if (StudioJson.Text(obj["from"]) is { } from)
            {
                if (from == "Instance") ids.Add(StudioJson.RequiredString(obj, "instanceId"));
                return; // Constant values/defaults are data, not executable bindings.
            }
            foreach (var property in obj) Collect(property.Value);
        }
        Collect(rule["enabledWhen"]);
        foreach (var map in (rule["parameterMap"] as JsonArray ?? []).OfType<JsonObject>()) Collect(map["source"]);
        if (StudioJson.Text(rule["event"]) == "RowClick")
        {
            var component = (document["components"] as JsonArray ?? []).OfType<JsonObject>()
                .SingleOrDefault(item => StudioJson.Text(item["id"]) == StudioJson.Text(rule["componentId"]));
            Collect(RowsBinding(component));
            if (StudioJson.Text(component?["props"]?["instanceId"]) is { } legacyId) ids.Add(legacyId);
        }
        return ids.ToArray();
    }

    private async Task<JsonObject> ExecuteDocumentAsync(StudioPrincipal principal, JsonObject document, JsonObject parameters, CancellationToken ct, JsonObject? sampleRow = null, string[]? requestedIds = null)
    {
        var pageParameters = PageParameters(principal, document, parameters);
        var contexts = new Dictionary<string, JsonNode?>
        {
            ["PageParam"] = pageParameters,
            ["Context"] = new JsonObject
            {
                ["tenant"] = principal.TenantNId,
                ["user"] = principal.SubjectNId,
                ["authorizedFactoryIds"] = new JsonArray(principal.FactoryNIds.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray()),
            },
        };
        if (sampleRow is not null) contexts["Row"] = sampleRow;
        var result = new JsonObject();
        var pending = (document["instances"] as JsonArray ?? []).OfType<JsonObject>().ToArray();
        if (pending.Length > 64) throw new StudioException("STUDIO_LIMIT", "页面数据实例超过64项。", 429);
        var definitions = pending.ToDictionary(item => StudioJson.RequiredString(item, "instanceId"), StringComparer.Ordinal);
        if (requestedIds is { Length: > 64 } || requestedIds?.Any(id => !definitions.ContainsKey(id)) == true) throw new StudioException("PATH_INVALID", "请求的数据实例不存在或超过预算。", path: "instanceIds");
        // Metadata-only screen heartbeats still recheck every data dependency's access and live source contract.
        // Reuse only this invocation's authorized immutable documents; never cache grants across requests.
        var authorizedDatasets = new Dictionary<(string NId, string RevisionNId), JsonObject>();
        foreach (var instance in pending.Where(item => StudioJson.Text(item["sourceType"]) != "Constant"))
        {
            var key = (StudioJson.RequiredString(instance, "datasetNId"), StudioJson.RequiredString(instance, "revisionNId"));
            if (!authorizedDatasets.TryGetValue(key, out var datasetDocument))
            {
                var (dataset, revision) = await publishing.RequireRuntimeAsync(principal, key.Item1, key.Item2, ct);
                if (dataset.Kind != "Dataset") throw new StudioException("TYPE_MISMATCH", "看板数据来源必须为数据集。");
                datasetDocument = StudioPublishing.Parse(revision.DocumentJson);
                var source = await store.FindSourceAsync(principal.TenantNId, StudioJson.RequiredString(datasetDocument, "sourceNId"), ct) ?? throw new StudioException("STUDIO_NOT_FOUND", "来源不存在。", 404);
                if (!source.IsEnabled || source.ConfigurationVersion != StudioJson.Version(StudioJson.Text(datasetDocument["sourceConfigurationVersion"]))) throw new StudioException("SOURCE_CHANGED", "来源未启用或合同已改变。", 409);
                publishing.ValidateSourceConfiguration(source);
                authorizedDatasets.Add(key, datasetDocument);
            }
            var query = datasetDocument["query"];
            if (StudioJson.Text(instance["sourceType"]) == "Api" && (StudioJson.Text(query?["kind"]) ?? StudioJson.Text(query?["type"])) != "Api") throw new StudioException("TYPE_MISMATCH", "接口来源必须使用已发布的接口数据集。");
        }
        var active = new HashSet<string>(StringComparer.Ordinal);
        var bytes = 0;
        async Task Execute(string id)
        {
            if (result.ContainsKey(id)) return;
            if (!active.Add(id)) throw new StudioException("STUDIO_VALIDATION", "数据实例存在循环依赖。", path: "instances." + id);
            if (!definitions.TryGetValue(id, out var instance)) throw new StudioException("PATH_INVALID", "依赖数据实例不存在。", path: "instances." + id);
            var dependencies = (instance["dependsOn"] as JsonArray ?? []).Select(StudioJson.Text).Where(value => value is not null).Select(value => value!)
                .Concat((instance["parameters"] as JsonArray ?? []).OfType<JsonObject>().Where(map => StudioJson.Text(map["source"]?["from"]) == "Instance").Select(map => StudioJson.RequiredString(map["source"]!.AsObject(), "instanceId"))).Distinct(StringComparer.Ordinal).ToArray();
            foreach (var dependency in dependencies) await Execute(dependency);
            if (dependencies.Any(dependency => result[dependency]?["error"] is not null))
            {
                result[id] = new JsonObject { ["data"] = null, ["error"] = new JsonObject { ["code"] = "DEPENDENCY_FAILED", ["message"] = "依赖数据源读取失败。" }, ["quality"] = new JsonObject { ["status"] = "Error" } };
                contexts["Instance:" + id] = null; active.Remove(id); return;
            }
            var mapped = new JsonObject();
            foreach (var map in (instance["parameters"] as JsonArray ?? []).OfType<JsonObject>())
                mapped[StudioJson.RequiredString(map, "target")] = StudioBinding.Resolve(map["source"]?.AsObject() ?? throw new StudioException("STUDIO_VALIDATION", "数据实例参数来源缺失。"), contexts);
            JsonObject execution;
            if (StudioJson.Text(instance["sourceType"]) == "Constant") execution = new JsonObject { ["data"] = instance["value"]?.DeepClone(), ["constant"] = true };
            else
            {
                var datasetNId = StudioJson.RequiredString(instance, "datasetNId");
                var datasetRevisionNId = StudioJson.RequiredString(instance, "revisionNId");
                // The full Run grant and source contract were just checked above in this invocation.
                try { execution = await datasets.ExecuteDocumentAsync(principal, datasetRevisionNId, authorizedDatasets[(datasetNId, datasetRevisionNId)], mapped, false, null, null, ct, bypassCache: true); }
                catch (StudioException exception) when (exception.StatusCode >= 500 || exception.StatusCode == 429)
                {
                    execution = new JsonObject { ["data"] = null, ["error"] = new JsonObject { ["code"] = exception.Code, ["message"] = "数据源读取失败，等待下一次刷新。" }, ["quality"] = new JsonObject { ["status"] = "Error" } };
                }
                catch (Exception exception) when (exception is TimeoutException or HttpRequestException || exception is OperationCanceledException && !ct.IsCancellationRequested)
                {
                    execution = new JsonObject { ["data"] = null, ["error"] = new JsonObject { ["code"] = "SOURCE_TIMEOUT", ["message"] = "数据源读取超时，等待下一次刷新。" }, ["quality"] = new JsonObject { ["status"] = "Error" } };
                }
            }
            bytes += Encoding.UTF8.GetByteCount(execution.ToJsonString());
            if (bytes > 8 * 1024 * 1024) throw new StudioException("STUDIO_LIMIT", "看板数据实例总结果超过8MiB保护预算。", 429);
            result[id] = execution;
            contexts["Instance:" + id] = execution["data"]?.DeepClone();
            active.Remove(id);
        }
        foreach (var id in requestedIds ?? definitions.Keys.ToArray()) await Execute(id);
        return result;
    }

    private static JsonObject ResolveRow(JsonObject document, JsonObject rule, JsonObject? rowKey, JsonObject instances, Dictionary<string, JsonNode?> contexts)
    {
        if (rowKey is null) throw new StudioException("PARAM_REQUIRED", "行跳转需要稳定行标识。", path: "rowKey");
        var componentId = StudioJson.RequiredString(rule, "componentId");
        var component = (document["components"] as JsonArray ?? []).OfType<JsonObject>().SingleOrDefault(item => StudioJson.Text(item["id"]) == componentId) ?? throw new StudioException("STUDIO_NOT_FOUND", "导航组件不存在。", 404);
        JsonNode? rows = ResolveRows(component, instances, contexts);
        if (rows is not JsonArray array) throw new StudioException("DEPENDENCY_CHANGED", "导航组件的数据不是行集合。", 409);
        var rowKeyPath = component["props"]?["rowKeyPath"];
        var expected = rowKey["key"]?.DeepClone() ?? throw new StudioException("PARAM_REQUIRED", "行标识缺失。", path: "rowKey.key");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        JsonObject? selected = null;
        foreach (var row in array.OfType<JsonObject>())
        {
            var value = ResolveJsonPath(row, rowKeyPath);
            if (value is null) continue;
            if (!keys.Add(StudioJson.Canonicalize(value))) throw new StudioException("STUDIO_ROW_KEY_AMBIGUOUS", "已授权表格结果含重复行标识，无法安全选行。", 409, "rowKey.key");
            if (JsonNode.DeepEquals(value, expected)) selected = row;
        }
        return selected ?? throw new StudioException("STUDIO_NOT_FOUND", "此行当前不可见。", 404, "rowKey.key");
    }

    private static JsonObject? RowsBinding(JsonObject? component) => (component?["bindings"] as JsonArray ?? [])
        .OfType<JsonObject>().FirstOrDefault(binding => StudioJson.Text(binding["property"]) == "rows")?["source"] as JsonObject;

    private static JsonNode? ResolveRows(JsonObject component, JsonObject instances, Dictionary<string, JsonNode?> contexts)
    {
        var rows = RowsBinding(component) is { } binding ? StudioBinding.Resolve(binding, contexts) : null;

        if (rows is null)
        {
            var props = component["props"] as JsonObject;
            var instanceId = StudioJson.Text(props?["instanceId"]);
            if (instanceId is null || instances[instanceId] is not JsonObject instance) return null;
            rows = instance["data"]?.DeepClone();
            foreach (var segment in (props?["collectionPath"] as JsonArray ?? []).Select(StudioJson.Text))
            {
                if (segment is null || rows is not JsonObject obj) return null;
                rows = obj[segment];
            }
        }
        return rows;
    }

    private static JsonNode? ResolveJsonPath(JsonNode? root, JsonNode? path)
    {
        var segments = path is JsonArray array ? array.Select(StudioJson.Text).ToArray() : [StudioJson.Text(path) ?? "nId"];
        var current = root;
        foreach (var segment in segments)
        {
            if (segment is null || current is not JsonObject obj) return null;
            current = obj[segment];
        }
        return current;
    }

    private static string? FixedDependencyRevision(StudioRevision revision, string resourceNId)
    {
        var revisions = (JsonNode.Parse(revision.DependenciesJson) as JsonArray ?? [])
            .OfType<JsonObject>()
            .Where(item => StudioJson.Text(item["resourceNId"]) == resourceNId)
            .Select(item => StudioJson.Text(item["revisionNId"]))
            .Where(value => value is not null).Distinct(StringComparer.Ordinal).ToArray();
        return revisions.Length == 1 ? revisions[0] : null;
    }
}
