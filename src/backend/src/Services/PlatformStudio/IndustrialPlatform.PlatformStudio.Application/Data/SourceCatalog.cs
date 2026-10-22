using System.Globalization;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Application.Data;

/// <summary>代码登记的公开结构；字段未登记或sensitive时不进入设计器目录。</summary>
public sealed record StudioSourceCatalogRegistration(string EndpointKey, string ObjectName, string Kind, string PermissionKey, JsonObject ResultSchema, JsonArray? Parameters = null);
public sealed record StudioSourceCatalogColumn(string Name, string DatabaseType, bool Nullable, bool PrimaryKey);
public sealed record StudioSourceCatalogObject(string QualifiedName, string Kind, JsonArray Parameters, JsonObject ResultSchema, IReadOnlyList<StudioSourceCatalogColumn> Columns);
public sealed record StudioSourceCatalogDto(string SourceNId, string Provider, string? CatalogKey, string ConfigurationVersion, IReadOnlyList<StudioSourceCatalogObject> Objects, DateTimeOffset CheckedOn);

public interface IStudioSourceCatalogReader
{
    Task<IReadOnlyList<StudioSourceCatalogObject>> ReadAsync(StudioPrincipal principal, StudioSource source, CancellationToken cancellationToken);
}

/// <summary>设计态受控目录，不接受数据库名、schema、SQL或连接串作为请求输入。</summary>
public sealed class SourceCatalog(IStudioResourceStore store, IStudioAuthorization authorization, SourceAdministration sources, IStudioSourceCatalogReader reader, TimeProvider clock)
{
    public async Task<StudioSourceCatalogDto> GetAsync(StudioPrincipal principal, string sourceNId, string configurationVersion, CancellationToken cancellationToken)
    {
        authorization.RequirePermission(principal, "studio.source.view");
        authorization.RequirePermission(principal, "studio.source.test");
        var source = await store.FindSourceAsync(principal.TenantNId, sourceNId, cancellationToken) ?? throw new StudioException("STUDIO_NOT_FOUND", "来源不存在。", 404);
        if (source.ConfigurationVersion != StudioJson.Version(configurationVersion, "configurationVersion")) throw new StudioException("SOURCE_CHANGED", "来源合同已改变，请重新读取来源。", 409);
        sources.EnsureEndpoint(source);
        var objects = await reader.ReadAsync(principal, source, cancellationToken);
        var version = source.ConfigurationVersion.ToString(CultureInfo.InvariantCulture);
        await store.AppendAuditAsync(principal, "studio.source.catalog", sourceNId, new() { ["configurationVersion"] = version, ["objectCount"] = objects.Count }, cancellationToken);
        return new(source.NId, source.Provider, source.CatalogKey, version, objects, clock.GetUtcNow());
    }
}
