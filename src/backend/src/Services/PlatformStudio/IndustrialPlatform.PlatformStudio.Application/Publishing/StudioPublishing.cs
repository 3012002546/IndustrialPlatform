using System.Globalization;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Contracts;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Application.Publishing;

public sealed record StudioValidation(bool Valid, IReadOnlyList<StudioDiagnostic> Diagnostics, JsonArray DependencyPlan, JsonArray SourcePins, string DependencyPlanHash);

/// <summary>唯一发布器：服务器重算依赖/来源计划，在本地事务中追加不可变修订并CAS切指向。</summary>
public sealed class StudioPublishing(IStudioResourceStore store, IStudioAuthorization authorization, StudioCommands commands, StudioDeploymentProfile profile, TimeProvider clock, IEnumerable<IStudioAction> actions, IStudioSourceExecutor executor)
{
    private readonly IStudioAction[] _actions = actions.ToArray();
    private static readonly string[] OwnerCapabilities = ["View", "Design", "Publish", "Run", "Export"];
    public static void ValidateQuery(StudioQuery query)
    {
        if (query.Page < 1 || query.PageSize is < 1 or > 200 || query.Keyword?.Length > 128 || query.SortBy is not ("name" or "createdOn" or "lastUpdatedOn") || query.SortOrder is not ("asc" or "desc")) throw new StudioException("STUDIO_VALIDATION", "分页或排序参数不在允许范围。", path: "query");
    }
    private void RequireKind(string kind)
    {
        _ = StudioKinds.PermissionPrefix(kind);
        if (kind is not ("Dataset" or "LedgerModel" or "LowCodePage" or "Dashboard" or "Playlist" or "Report" or "ReportBundle") || !profile.IsKindEnabled(kind)) throw new StudioException("STUDIO_MODULE_UNAVAILABLE", "该产品未安装、未启用或尚未交付。", 409);
    }
    public void ValidateSourceConfiguration(StudioSource source)
    {
        if (!profile.SourceRegistry.TryGetValue(source.EndpointKey, out var endpoint) || endpoint.Provider != source.Provider || executor.ConfigurationHash(endpoint) != source.EndpointConfigurationHash) throw new StudioException("SOURCE_CHANGED", "登记来源实际配置已改变。", 409);
    }
    public async Task<StudioResource> RequireResourceAsync(StudioPrincipal principal, string nId, string permissionAction, string capability, CancellationToken cancellationToken, bool allowDeleted = false)
    {
        var resource = await store.FindResourceAsync(principal.TenantNId, nId, cancellationToken) ?? throw Missing();
        RequireKind(resource.Kind);
        await authorization.RequireAsync(principal, resource, StudioKinds.PermissionPrefix(resource.Kind) + "." + permissionAction, capability, cancellationToken);
        if (resource.IsDeleted && !allowDeleted) throw Missing();
        return resource;
    }
    public async Task<StudioPage<ResourceDto>> ListAsync(StudioPrincipal principal, string kind, StudioQuery query, CancellationToken cancellationToken)
    {
        RequireKind(kind); ValidateQuery(query);
        authorization.RequirePermission(principal, StudioKinds.PermissionPrefix(kind) + ".view");
        var resources = await store.ListResourcesAsync(principal, kind, query, cancellationToken);
        var result = new List<ResourceDto>();
        foreach (var resource in resources) result.Add(await ToDtoAsync(resource, cancellationToken));
        return new(result, query.Page, query.PageSize, (await store.CountResourcesAsync(principal, kind, query, cancellationToken)).ToString(CultureInfo.InvariantCulture));
    }
    public async Task<ResourceDto> CreateAsync(StudioPrincipal principal, ResourceCreate request, string operationKey, CancellationToken cancellationToken)
    {
        var kind = Required(request.Kind, "kind", 32); RequireKind(kind);
        authorization.RequirePermission(principal, StudioKinds.PermissionPrefix(kind) + ".design");
        var nId = Required(request.NId, "nId", 128);
        var name = Required(request.Name, "name", 128);
        var document = request.Document ?? throw Invalid("document");
        _ = StudioDocuments.Validate(kind, document);
        return await commands.ExecuteAsync(principal, "ResourceCreate", nId, operationKey, request, async () =>
        {
            if (await store.FindResourceAsync(principal.TenantNId, nId, cancellationToken) is not null) throw new StudioException("STUDIO_VALIDATION", "资源编号已存在，软删除也不能复用。", 409, "nId");
            var now = clock.GetUtcNow();
            var resource = new StudioResource(now) { NId = nId, TenantNId = principal.TenantNId, Kind = kind, Name = name, Description = Optional(request.Description, 1000), Folder = Optional(request.Folder, 256), DraftJson = StudioJson.Canonicalize(document), OwnerUserNId = principal.SubjectNId };
            await store.InsertResourceAsync(resource, cancellationToken);
            var grants = OwnerCapabilities.Select(capability => new StudioGrant { NId = "grant-" + Guid.NewGuid().ToString("N"), TenantNId = principal.TenantNId, ResourceId = resource.Id, SubjectKind = "User", SubjectNId = principal.SubjectNId, Capability = capability, GrantedBy = principal.SubjectNId, CreatedOn = now }).ToArray();
            await store.ReplaceGrantsAsync(principal.TenantNId, resource.Id, grants, cancellationToken);
            await store.AppendAuditAsync(principal, "studio.resource.create", nId, new() { ["kind"] = kind }, cancellationToken);
            return await ToDtoAsync(resource, cancellationToken);
        }, cancellationToken);
    }
    public async Task<JsonObject> GetDraftAsync(StudioPrincipal principal, string nId, CancellationToken cancellationToken)
    {
        var resource = await RequireResourceAsync(principal, nId, "design", "Design", cancellationToken);
        var result = System.Text.Json.JsonSerializer.SerializeToNode(await ToDtoAsync(resource, cancellationToken), StudioCommands.JsonOptions)!.AsObject();
        var document = Parse(resource.DraftJson);
        result["draftDocument"] = document;
        result["diagnostics"] = System.Text.Json.JsonSerializer.SerializeToNode(StudioDocuments.Validate(resource.Kind, document), StudioCommands.JsonOptions);
        return result;
    }
    public async Task<JsonObject> SaveDraftAsync(StudioPrincipal principal, string nId, DraftSave request, CancellationToken cancellationToken)
    {
        var resource = await RequireResourceAsync(principal, nId, "design", "Design", cancellationToken);
        GuardWritable(resource); MatchVersion(resource, request);
        var document = request.Document ?? throw Invalid("document");
        _ = StudioDocuments.Validate(resource.Kind, document);
        if (resource.Kind == "ReportBundle" && StudioJson.Text(document["mode"]) != StudioJson.Text(Parse(resource.DraftJson)["mode"])) throw new StudioException("STUDIO_VALIDATION", "组合模式创建后不可变，请复制为新资源。", path: "mode");
        var canonical = StudioJson.Canonicalize(document);
        var name = Required(request.Name, "name", 128);
        var description = Optional(request.Description, 1000); var folder = Optional(request.Folder, 256);
        if (canonical == resource.DraftJson && name == resource.Name && description == resource.Description && folder == resource.Folder) return await GetDraftAsync(principal, nId, cancellationToken);
        var version = resource.OptimisticVersion; var token = resource.ConcurrencyVersion;
        resource.Name = name; resource.Description = description; resource.Folder = folder; resource.DraftJson = canonical;
        resource.MarkChanged(clock.GetUtcNow());
        await store.TransactionAsync(async () =>
        {
            if (!await store.UpdateResourceAsync(resource, version, token, cancellationToken)) throw Conflict();
            await store.AppendAuditAsync(principal, "studio.resource.draft-save", nId, new() { ["version"] = resource.OptimisticVersion.ToString(CultureInfo.InvariantCulture) }, cancellationToken);
            return true;
        }, cancellationToken);
        return await GetDraftAsync(principal, nId, cancellationToken);
    }
    public async Task<StudioValidation> ValidateAsync(StudioPrincipal principal, string nId, JsonObject document, CancellationToken cancellationToken)
    {
        var resource = await RequireResourceAsync(principal, nId, "design", "Design", cancellationToken);
        return await PlanAsync(principal, resource, document, cancellationToken);
    }
    public async Task<StudioValidation> PlanAsync(StudioPrincipal principal, StudioResource resource, JsonObject document, CancellationToken cancellationToken)
    {
        var diagnostics = StudioDocuments.Validate(resource.Kind, document).ToList();
        bool HasFileNode(JsonNode? node) => node is JsonObject obj && (StudioJson.Text(obj["type"]) == "File" || obj.Any(p => HasFileNode(p.Value))) || node is JsonArray array && array.Any(HasFileNode);
        var needsFiles = resource.Kind == "LedgerModel" && (document["fields"] as JsonArray ?? []).OfType<JsonObject>().Any(f => StudioJson.Text(f["type"]) == "FileReference" && f["retired"]?.GetValue<bool>() != true) || resource.Kind == "LowCodePage" && HasFileNode(document["layout"]);
        if (needsFiles && profile.Files.Provider == "Disabled") diagnostics.Add(new("STUDIO_FILES_UNAVAILABLE", "files", "此文档依赖文件能力，当前部署未安装，不能发布。"));
        var dependencies = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var pins = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        async Task AddRevision(string revisionNId, string? expectedResourceNId = null)
        {
            var revision = await store.FindRevisionAsync(principal.TenantNId, revisionNId, cancellationToken) ?? throw new StudioException("STUDIO_VALIDATION", "引用的已发布修订不存在。", path: "dependencies");
            var target = await store.FindResourceByIdAsync(principal.TenantNId, revision.ResourceId, cancellationToken) ?? throw Missing();
            if (target.IsDeleted || !target.IsEnabled || expectedResourceNId is not null && target.NId != expectedResourceNId) throw new StudioException("DEPENDENCY_CHANGED", "依赖资源不可用或修订不属于该资源。", 409);
            RequireKind(target.Kind);
            await authorization.RequireAsync(principal, target, StudioKinds.PermissionPrefix(target.Kind) + ".view", "View", cancellationToken);
            var identity = target.NId + "/" + revision.NId;
            if (dependencies.ContainsKey(identity)) return;
            if (dependencies.Count >= 256) throw new StudioException("STUDIO_LIMIT", "发布依赖闭包超过256项。", 429);
            dependencies[identity] = new() { ["resourceNId"] = target.NId, ["revisionNId"] = revision.NId, ["contentHash"] = revision.ContentHash };
            foreach (var pin in (JsonNode.Parse(revision.SourcePinsJson) as JsonArray ?? []).OfType<JsonObject>()) await AddPin(StudioJson.RequiredString(pin, "sourceNId"), StudioJson.RequiredString(pin, "configurationVersion"));
            foreach (var dependency in (JsonNode.Parse(revision.DependenciesJson) as JsonArray ?? []).OfType<JsonObject>()) await AddRevision(StudioJson.RequiredString(dependency, "revisionNId"), StudioJson.RequiredString(dependency, "resourceNId"));
        }
        async Task AddPin(string sourceNId, string expected)
        {
            var source = await store.FindSourceAsync(principal.TenantNId, sourceNId, cancellationToken);
            if (source is null || !source.IsEnabled || !profile.SourceRegistry.TryGetValue(source.EndpointKey, out var endpoint) || executor.ConfigurationHash(endpoint) != source.EndpointConfigurationHash || source.ConfigurationVersion != StudioJson.Version(expected, "sourceConfigurationVersion")) throw new StudioException("SOURCE_CHANGED", "来源不可用或配置版本已改变，请重新试查并固定合同。", 409, "sourceConfigurationVersion");
            if (pins.TryGetValue(sourceNId, out var existing) && StudioJson.Text(existing["configurationVersion"]) != expected) throw new StudioException("SOURCE_CHANGED", "发布依赖要求互相冲突的来源版本。", 409);
            pins[sourceNId] = new() { ["sourceNId"] = sourceNId, ["configurationVersion"] = expected };
        }
        if (resource.Kind == "Dataset")
        {
            await AddPin(StudioJson.RequiredString(document, "sourceNId"), StudioJson.RequiredString(document, "sourceConfigurationVersion"));
            if (document["query"] is JsonObject query && (StudioJson.Text(query["kind"]) ?? StudioJson.Text(query["type"])) == "Sql")
            {
                var source = await store.FindSourceAsync(principal.TenantNId, StudioJson.RequiredString(document, "sourceNId"), cancellationToken) ?? throw Missing();
                var allowed = (JsonNode.Parse(source.AllowedObjectsJson) as JsonArray ?? []).OfType<JsonObject>().Select(item => StudioJson.RequiredString(item, "qualifiedName", 256)).ToArray();
                _ = SqlReadOnlyPolicy.Validate(StudioJson.Text(query["statement"]), allowed);
            }
        }
        if (resource.Kind == "LowCodePage")
        {
            if (StudioJson.Text(document["ledgerModelRevisionNId"]) is { Length: > 0 } modelRevision) await AddRevision(modelRevision);
            var instanceSchemas = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            foreach (var instance in (document["instances"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var revisionNId = StudioJson.RequiredString(instance, "revisionNId");
                await AddRevision(revisionNId, StudioJson.RequiredString(instance, "datasetNId"));
                var revision = await store.FindRevisionAsync(principal.TenantNId, revisionNId, cancellationToken) ?? throw Missing();
                var target = await store.FindResourceByIdAsync(principal.TenantNId, revision.ResourceId, cancellationToken) ?? throw Missing();
                if (target.Kind != "Dataset") diagnostics.Add(new("TYPE_MISMATCH", "instances", "数据实例必须引用Dataset修订。"));
                else if (Parse(revision.DocumentJson)["result"] is JsonObject schema) instanceSchemas[StudioJson.RequiredString(instance, "instanceId")] = schema;
            }
            ValidateInstancePaths(document, instanceSchemas, diagnostics);
            foreach (var action in (document["actions"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var key = StudioJson.RequiredString(action, "actionKey"); var version = StudioJson.RequiredString(action, "executorVersion", 64);
                var registered = _actions.SingleOrDefault(item => item.Key == key && item.ExecutorVersion == version);
                if (registered is null) diagnostics.Add(new("STUDIO_VALIDATION", "actions." + StudioJson.Text(action["id"]), "业务动作或该执行版本未注册。"));
                else
                {
                    var targets = (action["inputMap"] as JsonArray ?? []).OfType<JsonObject>().Select(map => StudioJson.RequiredString(map, "target")).ToHashSet(StringComparer.Ordinal);
                    var properties = registered.InputSchema["properties"] as JsonObject ?? new();
                    foreach (var target in targets)
                        if (!properties.ContainsKey(target) && registered.InputSchema["additionalProperties"]?.GetValue<bool>() != true) diagnostics.Add(new("PATH_INVALID", "actions.inputMap.target", "动作输入字段未在执行器合同登记。"));
                    foreach (var required in (registered.InputSchema["required"] as JsonArray ?? []).Select(StudioJson.Text))
                        if (required is not null && !targets.Contains(required)) diagnostics.Add(new("PARAM_REQUIRED", "actions.inputMap", "必填动作输入缺少映射：" + required));
                }
            }
            if (StudioJson.Text(document["mode"]) == "Code" && StudioJson.Text(document["codeComponentKey"]) != "studio.summary") diagnostics.Add(new("STUDIO_VALIDATION", "codeComponentKey", "整页扩展未登记。"));
        }
        if (resource.Kind is "Dashboard" or "Playlist")
        {
            if (resource.Kind == "Dashboard")
            {
                foreach (var instance in (document["instances"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    if (StudioJson.Text(instance["sourceType"]) == "Constant") continue;
                    var revisionNId = StudioJson.Text(instance["revisionNId"]);
                    var datasetNId = StudioJson.Text(instance["datasetNId"]);
                    if (revisionNId is null || datasetNId is null) continue;
                    await AddRevision(revisionNId, datasetNId);
                    var revision = await store.FindRevisionAsync(principal.TenantNId, revisionNId, cancellationToken);
                    var target = revision is null ? null : await store.FindResourceByIdAsync(principal.TenantNId, revision.ResourceId, cancellationToken);
                    if (target?.Kind != "Dataset") diagnostics.Add(new("TYPE_MISMATCH", "instances", "看板数据实例必须引用Dataset修订。"));
                    if (StudioJson.Text(instance["sourceType"]) == "Api" && revision is not null)
                    {
                        var query = Parse(revision.DocumentJson)["query"];
                        if ((StudioJson.Text(query?["kind"]) ?? StudioJson.Text(query?["type"])) != "Api") diagnostics.Add(new("TYPE_MISMATCH", "instances." + StudioJson.Text(instance["instanceId"]), "接口来源必须引用接口类型数据集。"));
                    }
                }
                foreach (var rule in (document["navigation"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    if (rule["target"] is not JsonObject target) continue;
                    var targetKind = StudioJson.Text(target["kind"]);
                    if (targetKind is not ("Dashboard" or "LowCodePage" or "Report")) continue;
                    var targetNId = StudioJson.Text(target["resourceNId"]);
                    if (targetNId is null) continue;
                    var targetRevisionNId = StudioJson.Text(target["revisionNId"]);
                    if (string.IsNullOrWhiteSpace(targetRevisionNId)) targetRevisionNId = null;
                    if (targetRevisionNId is null)
                    {
                        var targetResource = await store.FindResourceAsync(principal.TenantNId, targetNId, cancellationToken);
                        targetRevisionNId = targetResource?.PublishedRevisionId is { } id ? (await store.FindRevisionByIdAsync(principal.TenantNId, id, cancellationToken))?.NId : null;
                    }
                    if (targetRevisionNId is null) throw new StudioException("DEPENDENCY_CHANGED", "导航目标必须已发布并固定修订。", 409, "navigation.target.revisionNId");
                    // Persist the direct edge on the immutable published document. The dependency
                    // plan also contains the recursive closure, so selecting a revision from that
                    // flat list would be ambiguous when a resource is reached through two paths.
                    target["revisionNId"] = targetRevisionNId;
                    await AddRevision(targetRevisionNId, targetNId);
                }
            }
            else
            {
                foreach (var item in (document["items"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    var dashboardNId = StudioJson.Text(item["dashboardNId"]);
                    if (dashboardNId is null) continue;
                    var dashboardRevisionNId = StudioJson.Text(item["revisionNId"]);
                    if (string.IsNullOrWhiteSpace(dashboardRevisionNId)) dashboardRevisionNId = null;
                    if (dashboardRevisionNId is null)
                    {
                        var dashboard = await store.FindResourceAsync(principal.TenantNId, dashboardNId, cancellationToken);
                        dashboardRevisionNId = dashboard?.PublishedRevisionId is { } id ? (await store.FindRevisionByIdAsync(principal.TenantNId, id, cancellationToken))?.NId : null;
                    }
                    if (dashboardRevisionNId is null) throw new StudioException("DEPENDENCY_CHANGED", "轮播项必须固定已发布看板修订。", 409, "items.revisionNId");
                    item["revisionNId"] = dashboardRevisionNId;
                    await AddRevision(dashboardRevisionNId, dashboardNId);
                }
            }
        }
        if (resource.Kind is "Report" or "ReportBundle")
        {
            var datasetSchemas = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            async Task ReportDataset(string revisionNId, string? nId = null)
            {
                await AddRevision(revisionNId, nId);
                var revision = await store.FindRevisionAsync(principal.TenantNId, revisionNId, cancellationToken) ?? throw Missing();
                var target = await store.FindResourceByIdAsync(principal.TenantNId, revision.ResourceId, cancellationToken) ?? throw Missing();
                if (target.Kind != "Dataset") diagnostics.Add(new("TYPE_MISMATCH", "instances", "报告数据必须引用Dataset修订。"));
                else datasetSchemas[revisionNId] = Parse(revision.DocumentJson)["result"]!.AsObject();
            }
            foreach (var instance in (document["instances"] as JsonArray ?? []).OfType<JsonObject>())
                if (StudioJson.Text(instance["sourceType"]) != "Constant" && StudioJson.Text(instance["revisionNId"]) is { } revision) await ReportDataset(revision, StudioJson.Text(instance["datasetNId"]));
            IEnumerable<JsonObject> Collections(JsonNode? node)
            {
                if (node is JsonArray array) { foreach (var item in array) foreach (var match in Collections(item)) yield return match; }
                if (node is not JsonObject obj || StudioJson.Text(obj["from"]) == "Constant") yield break;
                if (StudioJson.Text(obj["mode"]) == "Dataset" && StudioJson.Text(obj["datasetRevisionNId"]) is not null) yield return obj;
                foreach (var property in obj) foreach (var match in Collections(property.Value)) yield return match;
            }
            foreach (var collection in Collections(document)) await ReportDataset(StudioJson.RequiredString(collection, "datasetRevisionNId"));
            foreach (var filter in (document["query"]?["filters"] as JsonArray ?? []).OfType<JsonObject>())
                if (StudioJson.Text(filter["optionsDatasetRevisionNId"]) is { } revision) await ReportDataset(revision);
            if (resource.Kind == "Report" && !diagnostics.Any(d => d.Severity == "Error")) StudioReportContracts.ValidateBindings(document, datasetSchemas, diagnostics);
            var children = resource.Kind == "ReportBundle" ? (StudioJson.Text(document["mode"]) == "Simple" ? document["items"] as JsonArray : document["assembly"]?["rules"] as JsonArray) ?? [] : new JsonArray();
            foreach (var child in children.OfType<JsonObject>())
            {
                var nId = StudioJson.RequiredString(child, "reportNId");
                if (nId == resource.NId) throw new StudioException("DEPENDENCY_CHANGED", "组合不能引用自己。", 409);
                var target = await store.FindResourceAsync(principal.TenantNId, nId, cancellationToken) ?? throw Missing();
                if (target.Kind != "Report") throw new StudioException("TYPE_MISMATCH", "组合项目必须引用单报表。", path: "reportNId");
                var revision = StudioJson.Text(child["revisionNId"]) is { Length: > 0 } pin ? await store.FindRevisionAsync(principal.TenantNId, pin, cancellationToken) : target.PublishedRevisionId is { } published ? await store.FindRevisionByIdAsync(principal.TenantNId, published, cancellationToken) : null;
                if (revision is null) throw new StudioException("DEPENDENCY_CHANGED", "子报表必须已发布并固定修订。", 409);
                await AddRevision(revision.NId, nId); child["revisionNId"] = revision.NId;
                if (child["content"] is JsonObject content && StudioJson.Text(content["mode"]) == "BandSubtree")
                {
                    var childDocument = Parse(revision.DocumentJson); var entry = StudioJson.RequiredString(content, "entryBandId");
                    if (!(childDocument["bands"] as JsonArray ?? []).OfType<JsonObject>().Any(b => StudioJson.Text(b["id"]) == entry && StudioJson.Text(b["kind"]) == "Detail")) diagnostics.Add(new("PATH_INVALID", "content.entryBandId", "固定子报表不存在该明细带。"));
                    var supplied = (content["ancestorMap"] as JsonArray ?? []).OfType<JsonObject>().Select(m => StudioJson.Text(m["bandId"])).ToHashSet(StringComparer.Ordinal);
                    foreach (var ancestor in StudioReportContracts.RequiredAncestors(childDocument, entry)) if (!supplied.Contains(ancestor)) diagnostics.Add(new("PATH_INVALID", "content.ancestorMap", "缺少子报表实际引用的祖先映射：" + ancestor));
                }
            }
        }
        if (resource.Kind == "LedgerModel" && resource.PublishedRevisionId is { } oldId)
        {
            var old = await store.FindRevisionByIdAsync(principal.TenantNId, oldId, cancellationToken) ?? throw Missing();
            diagnostics.AddRange(StudioDocuments.Compatibility(Parse(old.DocumentJson), document, await store.CountLedgerRecordsAsync(principal.TenantNId, resource.Id, cancellationToken)));
        }
        if (resource.Kind == "LedgerModel" && StudioJson.Text(document["numbering"]?["mode"]) == "Rule") diagnostics.Add(new("STUDIO_NOT_READY", "numbering.mode", "尚未装配真实幂等编号适配，Rule模式不可发布；请显式选择Manual。"));
        var dependencyArray = new JsonArray(dependencies.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => (JsonNode)pair.Value).ToArray());
        var pinArray = new JsonArray(pins.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => (JsonNode)pair.Value).ToArray());
        var hash = StudioJson.Hash(StudioJson.Canonicalize(new JsonObject { ["document"] = document.DeepClone(), ["dependencies"] = dependencyArray.DeepClone(), ["sourcePins"] = pinArray.DeepClone() }));
        return new(!diagnostics.Any(item => item.Severity == "Error"), diagnostics, dependencyArray, pinArray, hash);
    }
    private static void ValidateInstancePaths(JsonNode? node, Dictionary<string, JsonObject> schemas, List<StudioDiagnostic> diagnostics)
    {
        if (node is JsonArray array) { foreach (var item in array) ValidateInstancePaths(item, schemas, diagnostics); return; }
        if (node is not JsonObject obj) return;
        if (StudioJson.Text(obj["from"]) == "Instance" && StudioJson.Text(obj["instanceId"]) is { } id && schemas.TryGetValue(id, out var root) && obj["path"] is JsonArray path)
        {
            JsonObject? current = root;
            foreach (var segment in path)
            {
                if (StudioJson.Text(current?["type"]) == "Array" && int.TryParse(StudioJson.Text(segment), NumberStyles.None, CultureInfo.InvariantCulture, out _)) current = current?["item"] as JsonObject;
                else current = (current?["children"] as JsonArray ?? []).OfType<JsonObject>().SingleOrDefault(field => StudioJson.Text(field["name"]) == StudioJson.Text(segment));
                if (current is null) break;
            }
            if (current is null) diagnostics.Add(new("PATH_INVALID", "binding.path", "绑定路径不在固定Dataset结果Schema中。"));
        }
        foreach (var property in obj) ValidateInstancePaths(property.Value, schemas, diagnostics);
    }
    public async Task<RevisionDto> PublishAsync(StudioPrincipal principal, string nId, ResourcePublish request, string operationKey, CancellationToken cancellationToken)
    {
        _ = await RequireResourceAsync(principal, nId, "publish", "Publish", cancellationToken);
        return await commands.ExecuteAsync(principal, "ResourcePublish", nId, operationKey, request, async () =>
        {
            var resource = await RequireResourceAsync(principal, nId, "publish", "Publish", cancellationToken);
            GuardWritable(resource); MatchVersion(resource, request);
            var document = Parse(resource.DraftJson);
            var plan = await PlanAsync(principal, resource, document, cancellationToken);
            if (!plan.Valid) throw new StudioException(plan.Diagnostics[0].Code, plan.Diagnostics[0].Message, plan.Diagnostics[0].Code == "MODEL_SCHEMA_INCOMPATIBLE" ? 409 : 400, plan.Diagnostics[0].Path);
            if (request.DependencyPlanHash != plan.DependencyPlanHash) throw new StudioException("DEPENDENCY_CHANGED", "发布计划已改变，请重新校验并确认。", 409);
            var releaseNote = Required(request.ReleaseNote, "releaseNote", 1000);
            var expected = resource.OptimisticVersion; var token = resource.ConcurrencyVersion;
            var revision = new StudioRevision { NId = "revision-" + Guid.NewGuid().ToString("N"), TenantNId = principal.TenantNId, ResourceId = resource.Id, RevisionNo = checked(resource.RevisionSequence + 1), CreatedOn = clock.GetUtcNow(), DocumentJson = StudioJson.Canonicalize(document), DependenciesJson = StudioJson.Canonicalize(plan.DependencyPlan), SourcePinsJson = StudioJson.Canonicalize(plan.SourcePins), ContentHash = plan.DependencyPlanHash, PublishedBy = principal.SubjectNId, ReleaseNote = releaseNote };
            await store.InsertRevisionAsync(revision, cancellationToken);
            resource.RevisionSequence = revision.RevisionNo; resource.PublishedRevisionId = revision.Id; resource.DraftBaseRevisionId = revision.Id; resource.IsEnabled = request.Enable;
            resource.MarkChanged(clock.GetUtcNow());
            if (!await store.UpdateResourceAsync(resource, expected, token, cancellationToken)) throw Conflict();
            await store.AppendAuditAsync(principal, "studio.resource.publish", nId, new() { ["revisionNId"] = revision.NId, ["contentHash"] = revision.ContentHash }, cancellationToken);
            return ToDto(resource.NId, revision);
        }, cancellationToken);
    }
    public async Task<(StudioResource Resource, StudioRevision Revision)> RequireRuntimeAsync(StudioPrincipal principal, string nId, string? revisionNId, CancellationToken cancellationToken)
    {
        var resource = await RequireResourceAsync(principal, nId, "run", "Run", cancellationToken);
        if (!resource.IsEnabled || resource.PublishedRevisionId is null) throw new StudioException("STUDIO_NOT_READY", "资源尚未发布或已停用。", 503);
        var revision = revisionNId is null ? await store.FindRevisionByIdAsync(principal.TenantNId, resource.PublishedRevisionId.Value, cancellationToken) : await store.FindRevisionAsync(principal.TenantNId, revisionNId, cancellationToken);
        if (revision is null || revision.ResourceId != resource.Id) throw Missing();
        foreach (var pin in (JsonNode.Parse(revision.SourcePinsJson) as JsonArray ?? []).OfType<JsonObject>())
        {
            var source = await store.FindSourceAsync(principal.TenantNId, StudioJson.RequiredString(pin, "sourceNId"), cancellationToken);
            if (source is null || !source.IsEnabled || !profile.SourceRegistry.TryGetValue(source.EndpointKey, out var endpoint) || executor.ConfigurationHash(endpoint) != source.EndpointConfigurationHash || source.ConfigurationVersion != StudioJson.Version(StudioJson.Text(pin["configurationVersion"]))) throw new StudioException("SOURCE_CHANGED", "已发布来源合同已改变，不能静默运行新来源。", 409);
        }
        return (resource, revision);
    }
    public async Task<ResourceDto> RollbackAsync(StudioPrincipal principal, string nId, ResourceRollback request, string operationKey, CancellationToken cancellationToken)
    {
        _ = await RequireResourceAsync(principal, nId, "publish", "Publish", cancellationToken);
        _ = Required(request.Reason, "reason", 1000);
        return await commands.ExecuteAsync(principal, "ResourceRollback", nId, operationKey, request, async () =>
        {
            var resource = await RequireResourceAsync(principal, nId, "publish", "Publish", cancellationToken);
            GuardWritable(resource); MatchVersion(resource, request);
            var revision = await store.FindRevisionAsync(principal.TenantNId, Required(request.RevisionNId, "revisionNId", 128), cancellationToken);
            if (revision is null || revision.ResourceId != resource.Id) throw Missing();
            var plan = await PlanAsync(principal, resource, Parse(revision.DocumentJson), cancellationToken);
            if (!plan.Valid) throw new StudioException("MODEL_SCHEMA_INCOMPATIBLE", "目标修订与当前数据不兼容。", 409);
            var version = resource.OptimisticVersion; var token = resource.ConcurrencyVersion;
            resource.PublishedRevisionId = revision.Id; resource.MarkChanged(clock.GetUtcNow());
            if (!await store.UpdateResourceAsync(resource, version, token, cancellationToken)) throw Conflict();
            await store.AppendAuditAsync(principal, "studio.resource.rollback", nId, new() { ["revisionNId"] = revision.NId }, cancellationToken);
            return await ToDtoAsync(resource, cancellationToken);
        }, cancellationToken);
    }
    public async Task<ResourceDto> SetStateAsync(StudioPrincipal principal, string nId, string state, VersionCommand request, CancellationToken cancellationToken)
    {
        var resource = await RequireResourceAsync(principal, nId, "design", "Design", cancellationToken, allowDeleted: state == "restore");
        MatchVersion(resource, request);
        if (resource.IsFrozen || resource.IsLocked) throw new StudioException("STUDIO_VERSION_CONFLICT", "资源已冻结或锁定。", 409);
        return await store.TransactionAsync(async () =>
        {
            var version = resource.OptimisticVersion; var token = resource.ConcurrencyVersion;
            switch (state)
            {
                case "enable": if (resource.PublishedRevisionId is null || resource.IsDeleted) throw new StudioException("STUDIO_NOT_READY", "未发布或已删除资源不能启用。", 409); resource.IsEnabled = true; resource.MarkChanged(clock.GetUtcNow()); break;
                case "disable": resource.IsEnabled = false; resource.MarkChanged(clock.GetUtcNow()); break;
                case "delete": if (await store.IsResourceReferencedAsync(principal.TenantNId, nId, cancellationToken)) throw new StudioException("DEPENDENCY_CHANGED", "资源仍被已发布修订引用。", 409); resource.MarkDeleted(clock.GetUtcNow()); resource.IsEnabled = false; break;
                case "restore":
                    resource.Restore(clock.GetUtcNow());
                    // 已激活资源的restore仍保持原合同:关闭运行,实际变化必须推进版本。
                    if (resource.IsEnabled) { resource.IsEnabled = false; if (resource.OptimisticVersion == version) resource.MarkChanged(clock.GetUtcNow()); }
                    break;
                default: throw Invalid("state");
            }
            if (resource.OptimisticVersion == version) return await ToDtoAsync(resource, cancellationToken);
            if (!await store.UpdateResourceAsync(resource, version, token, cancellationToken)) throw Conflict();
            await store.AppendAuditAsync(principal, "studio.resource." + state, nId, new(), cancellationToken);
            return await ToDtoAsync(resource, cancellationToken);
        }, cancellationToken);
    }
    public async Task<RevisionDto> GetRevisionDesignAsync(StudioPrincipal principal,string nId,string revisionNId,CancellationToken cancellationToken)
    {
        var resource=await RequireResourceAsync(principal,nId,"design","Design",cancellationToken);
        var revision=await store.FindRevisionAsync(principal.TenantNId,Required(revisionNId,"revisionNId",128),cancellationToken);
        if(revision is null||revision.ResourceId!=resource.Id)throw Missing();
        return ToDto(resource.NId,revision);
    }
    public async Task<IReadOnlyList<RevisionSummaryDto>> RevisionsAsync(StudioPrincipal principal, string nId, CancellationToken cancellationToken)
    {
        var resource = await RequireResourceAsync(principal, nId, "view", "View", cancellationToken);
        return (await store.ListRevisionsAsync(principal.TenantNId, resource.Id, cancellationToken)).Select(revision => new RevisionSummaryDto(revision.NId, revision.RevisionNo.ToString(CultureInfo.InvariantCulture), revision.ContentHash, revision.PublishedBy, revision.CreatedOn, revision.ReleaseNote)).ToArray();
    }
    public async Task<ResourceDto> ToDtoAsync(StudioResource resource, CancellationToken cancellationToken)
    {
        var published = resource.PublishedRevisionId is { } id ? await store.FindRevisionByIdAsync(resource.TenantNId, id, cancellationToken) : null;
        return ResourceDto(resource, published);
    }
    /// <summary>只复用同次已读取的当前发布修订；历史运行版仍读取实际当前发布摘要。</summary>
    public Task<ResourceDto> ToDtoAsync(StudioResource resource, StudioRevision revision, CancellationToken cancellationToken) =>
        resource.PublishedRevisionId == revision.Id ? Task.FromResult(ResourceDto(resource, revision)) : ToDtoAsync(resource, cancellationToken);
    private static ResourceDto ResourceDto(StudioResource resource, StudioRevision? published)
    {
        return new(resource.NId, resource.Kind, resource.Name, resource.Description, resource.Folder, resource.IsEnabled, published?.NId, published?.RevisionNo.ToString(CultureInfo.InvariantCulture), published is null || !PublishedMatchesDraft(resource.Kind, published.DocumentJson, resource.DraftJson), resource.OwnerUserNId, resource.CreatedOn, resource.LastUpdatedOn, resource.OptimisticVersion.ToString(CultureInfo.InvariantCulture), resource.ConcurrencyVersion);
    }
    private static bool PublishedMatchesDraft(string kind, string publishedJson, string draftJson)
    {
        if (publishedJson == draftJson) return true;
        if (kind is not ("Dashboard" or "Playlist")) return false;
        var published = Parse(publishedJson);
        var draft = Parse(draftJson);
        var collection = kind == "Dashboard" ? "navigation" : "items";
        foreach (var entry in (published[collection] as JsonArray ?? []).OfType<JsonObject>())
        {
            var original = (draft[collection] as JsonArray ?? []).OfType<JsonObject>().SingleOrDefault(item => StudioJson.Text(item["id"]) == StudioJson.Text(entry["id"]));
            var target = kind == "Dashboard" ? entry["target"] as JsonObject : entry;
            var originalTarget = kind == "Dashboard" ? original?["target"] as JsonObject : original;
            // Auto-selected targets are pinned only in immutable revisions. Their generated
            // pin does not constitute a user edit and must not modify the editable draft.
            if (target is not null && originalTarget is not null && string.IsNullOrWhiteSpace(StudioJson.Text(originalTarget["revisionNId"])))
            {
                target.Remove("revisionNId");
                if (originalTarget.ContainsKey("revisionNId")) target["revisionNId"] = originalTarget["revisionNId"]?.DeepClone();
            }
        }
        return StudioJson.Canonicalize(published) == StudioJson.Canonicalize(draft);
    }
    public static RevisionDto ToDto(string resourceNId, StudioRevision revision) => new(revision.NId, resourceNId, revision.RevisionNo.ToString(CultureInfo.InvariantCulture), revision.DocumentSchemaVersion, Parse(revision.DocumentJson), JsonNode.Parse(revision.DependenciesJson)!.AsArray(), JsonNode.Parse(revision.SourcePinsJson)!.AsArray(), revision.ContentHash, revision.PublishedBy, revision.CreatedOn, revision.ReleaseNote);
    public static JsonObject Parse(string json) => JsonNode.Parse(json)?.AsObject() ?? throw Invalid("document");
    public static void MatchVersion(StudioEntity entity, VersionCommand request) { if (entity.OptimisticVersion != StudioJson.Version(request.OptimisticVersion) || entity.ConcurrencyVersion != request.ConcurrencyVersion) throw Conflict(); }
    public static void GuardWritable(StudioEntity entity)
    {
        try { entity.EnsureWritable(); }
        catch (IndustrialPlatform.SharedKernel.Exceptions.BusinessException)
        { throw new StudioException("STUDIO_VERSION_CONFLICT", "记录已删除、冻结或锁定。", 409); }
    }
    public static string Required(string? value, string path, int length) { if (string.IsNullOrWhiteSpace(value) || value.Length > length) throw Invalid(path); return value; }
    private static string? Optional(string? value, int length) { if (value?.Length > length) throw Invalid("description"); return value; }
    private static StudioException Invalid(string path) => new("STUDIO_VALIDATION", "字段缺失或超出允许范围。", path: path);
    private static StudioException Missing() => new("STUDIO_NOT_FOUND", "资源不存在。", 404);
    private static StudioException Conflict() => new("STUDIO_VERSION_CONFLICT", "资源已被其他操作修改，请重新读取并比较草稿。", 409);
}
