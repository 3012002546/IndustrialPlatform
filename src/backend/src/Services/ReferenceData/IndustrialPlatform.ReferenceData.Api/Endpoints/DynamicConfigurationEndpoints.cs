using System.Diagnostics;
using IndustrialPlatform.ReferenceData.Api.Authorization;
using IndustrialPlatform.ReferenceData.Application.DynamicProperty;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.DynamicProperty;
using IndustrialPlatform.Web.Results;

namespace IndustrialPlatform.ReferenceData.Api.Endpoints;

public static class DynamicConfigurationEndpoints
{
    public static IEndpointRouteBuilder MapDynamicConfigurationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/reference-data").AddEndpointFilter<ReferenceDataErrorFilter>();
        const string admin = "/admin/dynamic-properties/configurations";
        const string runtime = "/dynamic-properties/configurations/{nId}";
        group.MapGet(admin, async ([AsParameters] DynamicConfigurationListRequest query, DynamicConfigurationService service, HttpContext http, CancellationToken cancellationToken) =>
        {
            var page = await service.SearchAsync(http.ReferenceDataActor(), new(query.PageIndex, query.PageSize, query.Keyword, query.Status, query.ScopeType, query.SortField, query.Descending), cancellationToken);
            return Success(http, PageResult.Create(page.Items, page.Total, query.PageIndex, query.PageSize));
        }).WithReferenceDataPermission(ReferenceDataPermissions.DynamicPropertyView);
        group.MapGet(admin + "/{id:guid}", async (Guid id, DynamicConfigurationService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.GetAsync(http.ReferenceDataActor(), id, cancellationToken))).WithReferenceDataPermission(ReferenceDataPermissions.DynamicPropertyView);
        group.MapPost(admin, async (CreateDynamicConfigurationRequest request, DynamicConfigurationService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.CreateAsync(http.ReferenceDataActor(), request, cancellationToken), 201)).WithReferenceDataPermission(ReferenceDataPermissions.DynamicPropertyCreate);
        group.MapPut(admin + "/{id:guid}", async (Guid id, UpdateDynamicConfigurationRequest request, DynamicConfigurationService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.UpdateAsync(http.ReferenceDataActor(), id, request, cancellationToken))).WithReferenceDataPermission(ReferenceDataPermissions.DynamicPropertyUpdate);
        group.MapPost(admin + "/{id:guid}/clone", async (Guid id, PublishOrDisableRequest request, DynamicConfigurationService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.CloneAsync(http.ReferenceDataActor(), id, request, cancellationToken))).WithReferenceDataPermission(ReferenceDataPermissions.DynamicPropertyCreate);
        group.MapGet(admin + "/{id:guid}/publication-check", async (Guid id, DynamicConfigurationService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.CheckPublicationAsync(http.ReferenceDataActor(), id, cancellationToken))).WithReferenceDataPermission(ReferenceDataPermissions.DynamicPropertyView);
        group.MapPost(admin + "/{id:guid}/publish", async (Guid id, PublishOrDisableRequest request, DynamicConfigurationService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.PublishAsync(http.ReferenceDataActor(), id, request, cancellationToken))).WithReferenceDataPermission(ReferenceDataPermissions.DynamicPropertyPublish);
        group.MapPost(admin + "/{id:guid}/disable", async (Guid id, PublishOrDisableRequest request, DynamicConfigurationService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.DisableAsync(http.ReferenceDataActor(), id, request, cancellationToken))).WithReferenceDataPermission(ReferenceDataPermissions.DynamicPropertyDisable);
        group.MapGet(admin + "/{id:guid}/records", async (Guid id, [AsParameters] DynamicRecordListRequest query, DynamicConfigurationService service, HttpContext http, CancellationToken cancellationToken) =>
        {
            var page = await service.SearchRecordsAsync(http.ReferenceDataActor(), id, new(query.PageIndex, query.PageSize, query.Keyword, query.NId, query.Category), cancellationToken);
            return Success(http, PageResult.Create(page.Items, page.Total, query.PageIndex, query.PageSize));
        }).WithReferenceDataPermission(ReferenceDataPermissions.DynamicPropertyView);
        group.MapPost(admin + "/{id:guid}/records", async (Guid id, DynamicRecordRequest request, DynamicConfigurationService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.AddRecordAsync(http.ReferenceDataActor(), id, request, cancellationToken), 201)).WithReferenceDataPermission(ReferenceDataPermissions.DynamicPropertyUpdate);
        group.MapPut(admin + "/{id:guid}/records/{recordId:guid}", async (Guid id, Guid recordId, DynamicRecordRequest request, DynamicConfigurationService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.UpdateRecordAsync(http.ReferenceDataActor(), id, recordId, request, cancellationToken))).WithReferenceDataPermission(ReferenceDataPermissions.DynamicPropertyUpdate);
        group.MapPost(admin + "/{id:guid}/records/{recordId:guid}/disable", async (Guid id, Guid recordId, PublishOrDisableRequest request, DynamicConfigurationService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.DisableRecordAsync(http.ReferenceDataActor(), id, recordId, request, cancellationToken))).WithReferenceDataPermission(ReferenceDataPermissions.DynamicPropertyDisable);
        group.MapGet(runtime + "/schema", async (string nId, string? factoryId, DynamicConfigurationService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.GetSchemaAsync(http.ReferenceDataActor(), nId, factoryId, cancellationToken))).WithReferenceDataPermission(ReferenceDataPermissions.DynamicPropertyView);
        group.MapGet(runtime + "/records", async (string nId, [AsParameters] DynamicRuntimeRecordRequest query, DynamicConfigurationService service, HttpContext http, CancellationToken cancellationToken) =>
        {
            var page = await service.GetRecordsAsync(http.ReferenceDataActor(), nId, query.Revision, query.SourceScope, query.SourceTenantNId, query.FactoryId,
                new(query.PageIndex, query.PageSize, NId: query.RecordNId, Category: query.Category), cancellationToken);
            return Success(http, PageResult.Create(page.Items, page.Total, query.PageIndex, query.PageSize));
        }).WithReferenceDataPermission(ReferenceDataPermissions.DynamicPropertyView);
        group.MapGet(runtime + "/records/{recordNId}", async (string nId, string recordNId, int? revision, string? sourceScope, string? sourceTenantNId,
            string? factoryId, DynamicConfigurationService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.GetRecordAsync(http.ReferenceDataActor(), nId, recordNId, revision, sourceScope, sourceTenantNId, factoryId, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.DynamicPropertyView);
        return endpoints;
    }
    private static IResult Success<T>(HttpContext http, T data, int status = 200)
    {
        var result = ApiResult.Ok(data);
        result.TraceId = Activity.Current?.Id ?? http.TraceIdentifier;
        return Results.Json(result, statusCode: status);
    }
}
public sealed record DynamicConfigurationListRequest(int PageIndex = 1, int PageSize = 20, string? Keyword = null,
    string? Status = null, string? ScopeType = null, string? SortField = null, bool Descending = false);
public sealed record DynamicRecordListRequest(int PageIndex = 1, int PageSize = 20, string? Keyword = null, string? NId = null, string? Category = null);
public sealed record DynamicRuntimeRecordRequest(int? Revision = null, string? SourceScope = null, string? SourceTenantNId = null,
    string? FactoryId = null, int PageIndex = 1, int PageSize = 20, string? RecordNId = null, string? Category = null);
