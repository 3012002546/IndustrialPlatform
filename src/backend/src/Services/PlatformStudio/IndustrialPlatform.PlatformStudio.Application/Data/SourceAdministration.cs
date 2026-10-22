using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Contracts;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Application.Publishing;

namespace IndustrialPlatform.PlatformStudio.Application.Data;

/// <summary>本进程最近验证结果，仅用于同配置版本的显式启用；不把历史探针当永久健康事实。</summary>
public sealed class StudioSourceProbes
{
    private readonly ConcurrentDictionary<string, (long Version, string Hash, DateTimeOffset CheckedOn)> _checks = new(StringComparer.Ordinal);
    public void Record(string tenant, StudioSource source, DateTimeOffset now) => _checks[tenant + "/" + source.NId] = (source.ConfigurationVersion, source.EndpointConfigurationHash, now);
    public bool IsCurrent(string tenant, StudioSource source, DateTimeOffset now) => _checks.TryGetValue(tenant + "/" + source.NId, out var probe) && probe.Version == source.ConfigurationVersion && probe.Hash == source.EndpointConfigurationHash && now - probe.CheckedOn <= TimeSpan.FromMinutes(5);
}

/// <summary>数据源管理只绑定服务端登记目标，不接受连接串、URL或凭据作为运行输入。</summary>
public sealed class SourceAdministration(IStudioResourceStore store, IStudioAuthorization authorization, StudioCommands commands, IStudioSourceExecutor executor, StudioDeploymentProfile profile, StudioSourceProbes probes, TimeProvider clock)
{
    public static string EndpointHash(StudioSourceEndpoint endpoint) => StudioJson.Hash(StudioJson.Canonicalize(JsonSerializer.SerializeToNode(new { endpoint.Provider, endpoint.BaseAddress, endpoint.CatalogKey, endpoint.ProbeOperationKey, endpoint.ConnectionStringRef, endpoint.CredentialRef, endpoint.ClientCertificatePathRef, endpoint.ClientCertificatePasswordRef, endpoint.ReadOnlyVerified, endpoint.PublicQueriesAllowed, endpoint.RowsPath, endpoint.FactoryFieldPath, endpoint.AllowedObjects, endpoint.Operations, endpoint.AllowedProcedures, endpoint.ProcedureDefinitionHashes, endpoint.Catalog }, StudioCommands.JsonOptions)));
    public async Task<StudioPage<SourceDto>> ListAsync(StudioPrincipal principal, StudioQuery query, CancellationToken cancellationToken)
    {
        authorization.RequirePermission(principal, "studio.source.view"); StudioPublishing.ValidateQuery(query);
        return new((await store.ListSourcesAsync(principal.TenantNId, query, cancellationToken)).Select(ToDto).ToArray(), query.Page, query.PageSize, (await store.CountSourcesAsync(principal.TenantNId, query, cancellationToken)).ToString(CultureInfo.InvariantCulture));
    }
    public async Task<SourceDto> CreateAsync(StudioPrincipal principal, SourceCommand request, string operationKey, CancellationToken cancellationToken)
    {
        authorization.RequirePermission(principal, "studio.source.manage");
        var source = Make(principal, request);
        return await commands.ExecuteAsync(principal, "SourceCreate", source.NId, operationKey, request, async () =>
        {
            if (await store.FindSourceAsync(principal.TenantNId, source.NId, cancellationToken) is not null) throw new StudioException("STUDIO_VALIDATION", "来源编号已存在。", 409, "nId");
            await store.InsertSourceAsync(source, cancellationToken);
            await store.AppendAuditAsync(principal, "studio.source.create", source.NId, new() { ["provider"] = source.Provider }, cancellationToken);
            return ToDto(source);
        }, cancellationToken);
    }
    public async Task<SourceDto> UpdateAsync(StudioPrincipal principal, string nId, SourceCommand request, CancellationToken cancellationToken)
    {
        authorization.RequirePermission(principal, "studio.source.manage");
        var source = await Find(principal, nId, cancellationToken);
        StudioPublishing.GuardWritable(source); StudioPublishing.MatchVersion(source, new(request.OptimisticVersion, request.ConcurrencyVersion));
        if (source.Provider != request.Provider) throw new StudioException("STUDIO_VALIDATION", "创建后不能改变来源Provider。", path: "provider");
        var replacement = Make(principal, request with { NId = nId });
        var semanticChange = source.EndpointKey != replacement.EndpointKey || source.EndpointConfigurationHash != replacement.EndpointConfigurationHash || source.CatalogKey != replacement.CatalogKey || source.AllowedObjectsJson != replacement.AllowedObjectsJson || source.TimeoutSeconds != replacement.TimeoutSeconds || source.MaxRows != replacement.MaxRows || source.MaxBytes != replacement.MaxBytes;
        if (!semanticChange && source.Name == replacement.Name && source.Description == replacement.Description) return ToDto(source);
        var oldVersion = source.OptimisticVersion; var oldToken = source.ConcurrencyVersion;
        source.Name = replacement.Name; source.Description = replacement.Description; source.EndpointKey = replacement.EndpointKey; source.EndpointConfigurationHash = replacement.EndpointConfigurationHash; source.CatalogKey = replacement.CatalogKey; source.AllowedObjectsJson = replacement.AllowedObjectsJson; source.CredentialRef = replacement.CredentialRef; source.TimeoutSeconds = replacement.TimeoutSeconds; source.MaxRows = replacement.MaxRows; source.MaxBytes = replacement.MaxBytes;
        if (semanticChange) { source.ConfigurationVersion = checked(source.ConfigurationVersion + 1); source.IsEnabled = false; }
        source.MarkChanged(clock.GetUtcNow());
        return await store.TransactionAsync(async () =>
        {
            if (!await store.UpdateSourceAsync(source, oldVersion, oldToken, cancellationToken)) throw new StudioException("STUDIO_VERSION_CONFLICT", "来源已被其他操作修改。", 409);
            await store.AppendAuditAsync(principal, "studio.source.update", nId, new() { ["configurationVersion"] = source.ConfigurationVersion.ToString(CultureInfo.InvariantCulture) }, cancellationToken);
            return ToDto(source);
        }, cancellationToken);
    }
    public async Task<JsonObject> TestAsync(StudioPrincipal principal, string nId, VersionCommand version, CancellationToken cancellationToken)
    {
        authorization.RequirePermission(principal, "studio.source.test");
        var source = await Find(principal, nId, cancellationToken); StudioPublishing.MatchVersion(source, version);
        EnsureEndpoint(source);
        var result = await executor.TestAsync(source, cancellationToken);
        if (StudioJson.Text(result["status"]) == "Ready") probes.Record(principal.TenantNId, source, clock.GetUtcNow());
        result["configurationVersion"] = source.ConfigurationVersion.ToString(CultureInfo.InvariantCulture);
        await store.AppendAuditAsync(principal, "studio.source.test", nId, new() { ["configurationVersion"] = source.ConfigurationVersion.ToString(CultureInfo.InvariantCulture), ["status"] = result["status"]?.DeepClone() }, cancellationToken);
        return result;
    }
    public async Task<SourceDto> SetEnabledAsync(StudioPrincipal principal, string nId, bool enabled, VersionCommand version, CancellationToken cancellationToken)
    {
        authorization.RequirePermission(principal, "studio.source.manage");
        var source = await Find(principal, nId, cancellationToken); StudioPublishing.MatchVersion(source, version); StudioPublishing.GuardWritable(source);
        EnsureEndpoint(source);
        if (enabled && !probes.IsCurrent(principal.TenantNId, source, clock.GetUtcNow())) throw new StudioException("STUDIO_NOT_READY", "启用前须对当前配置完成只读连通核验。", 409);
        if (source.IsEnabled == enabled) return ToDto(source);
        var oldVersion = source.OptimisticVersion; var oldToken = source.ConcurrencyVersion;
        source.IsEnabled = enabled; source.MarkChanged(clock.GetUtcNow());
        return await store.TransactionAsync(async () =>
        {
            if (!await store.UpdateSourceAsync(source, oldVersion, oldToken, cancellationToken)) throw new StudioException("STUDIO_VERSION_CONFLICT", "来源已被修改。", 409);
            await store.AppendAuditAsync(principal, enabled ? "studio.source.enable" : "studio.source.disable", nId, new(), cancellationToken);
            return ToDto(source);
        }, cancellationToken);
    }
    public void EnsureEndpoint(StudioSource source)
    {
        if (!profile.SourceRegistry.TryGetValue(source.EndpointKey, out var endpoint) || executor.ConfigurationHash(endpoint) != source.EndpointConfigurationHash) throw new StudioException("SOURCE_CHANGED", "登记目标配置已改变，请显式保存来源、重新试查和发布。", 409);
    }
    private StudioSource Make(StudioPrincipal principal, SourceCommand request)
    {
        var nId = StudioPublishing.Required(request.NId, "nId", 128); var endpointKey = StudioPublishing.Required(request.EndpointKey, "endpointKey", 128);
        if (!profile.SourceRegistry.TryGetValue(endpointKey, out var endpoint) || request.Provider != endpoint.Provider) throw new StudioException("STUDIO_VALIDATION", "来源必须匹配服务器登记Provider。", path: "endpointKey");
        if (request.TimeoutSeconds is < 1 or > 120 || request.MaxRows is < 1 or > 100000 || request.Description?.Length > 1000) throw new StudioException("STUDIO_VALIDATION", "来源预算或说明超过允许范围。");
        var bytes = StudioJson.Version(request.MaxBytes, "maxBytes");
        if (bytes is < 1 or > 52428800) throw new StudioException("STUDIO_VALIDATION", "来源字节预算不在允许范围。", path: "maxBytes");
        if (request.Provider != "RegisteredApi" && (string.IsNullOrWhiteSpace(request.CatalogKey) || request.CatalogKey != endpoint.CatalogKey)) throw new StudioException("STUDIO_VALIDATION", "库来源必须声明catalog。", path: "catalogKey");
        var allowed = request.AllowedObjects ?? new JsonArray();
        if (allowed.Any(item => item is not JsonObject)) throw new StudioException("TYPE_MISMATCH", "白名单项须为对象。", path: "allowedObjects");
        foreach (var item in allowed.Cast<JsonObject>())
        {
            var name = StudioJson.RequiredString(item, "qualifiedName", 256);
            var kind = StudioJson.RequiredString(item, "kind", 32);
            var admitted = kind switch { "Api" or "ApiOperation" => endpoint.Provider == "RegisteredApi" && endpoint.Operations.ContainsKey(name), "Procedure" => endpoint.Provider != "RegisteredApi" && endpoint.AllowedProcedures.Contains(name, StringComparer.Ordinal), "Table" or "View" => endpoint.Provider != "RegisteredApi" && endpoint.AllowedObjects.Contains(name, StringComparer.Ordinal), _ => false };
            if (!admitted) throw new StudioException("STUDIO_FORBIDDEN", "对象种类或名称未在部署白名单登记。", 403, "allowedObjects");
        }
        var now = clock.GetUtcNow();
        return new(now) { NId = nId, TenantNId = principal.TenantNId, Name = StudioPublishing.Required(request.Name, "name", 128), Description = request.Description, Provider = request.Provider!, EndpointKey = endpointKey, EndpointConfigurationHash = executor.ConfigurationHash(endpoint), CredentialRef = endpoint.CredentialRef, CatalogKey = request.CatalogKey, AllowedObjectsJson = StudioJson.Canonicalize(allowed), TimeoutSeconds = request.TimeoutSeconds, MaxRows = request.MaxRows, MaxBytes = bytes };
    }
    private async Task<StudioSource> Find(StudioPrincipal principal, string nId, CancellationToken cancellationToken) => await store.FindSourceAsync(principal.TenantNId, nId, cancellationToken) ?? throw new StudioException("STUDIO_NOT_FOUND", "来源不存在。", 404);
    public static SourceDto ToDto(StudioSource source) => new(source.NId, source.Name, source.Description, source.Provider, source.EndpointKey, source.CatalogKey, JsonNode.Parse(source.AllowedObjectsJson)!.AsArray(), source.IsEnabled, source.CredentialRef is not null, source.ConfigurationVersion.ToString(CultureInfo.InvariantCulture), source.TimeoutSeconds, source.MaxRows, source.MaxBytes.ToString(CultureInfo.InvariantCulture), source.OptimisticVersion.ToString(CultureInfo.InvariantCulture), source.ConcurrencyVersion);
}
