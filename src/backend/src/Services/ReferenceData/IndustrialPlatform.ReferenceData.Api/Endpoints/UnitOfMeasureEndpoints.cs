using System.Diagnostics;
using IndustrialPlatform.ReferenceData.Api.Authorization;
using IndustrialPlatform.ReferenceData.Application.UnitOfMeasure;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.UnitOfMeasure;
using IndustrialPlatform.Web.Results;

namespace IndustrialPlatform.ReferenceData.Api.Endpoints;

public static class UnitOfMeasureEndpoints
{
    public static IEndpointRouteBuilder MapUnitOfMeasureEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/reference-data").AddEndpointFilter<ReferenceDataErrorFilter>();
        const string admin = "/admin/units-of-measure/dimensions";
        const string runtime = "/units-of-measure";

        group.MapGet(admin, async ([AsParameters] UnitDimensionListRequest query, UnitDimensionService service,
            HttpContext http, CancellationToken cancellationToken) =>
        {
            var page = await service.SearchAsync(http.ReferenceDataActor(),
                new(query.PageIndex, query.PageSize, query.Keyword, query.Status, query.ScopeType,
                    query.SortField, query.Descending), cancellationToken);
            return Success(http, PageResult.Create(page.Items, page.Total, query.PageIndex, query.PageSize));
        }).WithReferenceDataPermission(ReferenceDataPermissions.UnitOfMeasureView);

        group.MapGet(admin + "/{id:guid}", async (Guid id, UnitDimensionService service,
            HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.GetAsync(http.ReferenceDataActor(), id, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.UnitOfMeasureView);

        group.MapPost(admin, async (CreateUnitDimensionRequest request, UnitDimensionService service,
            HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.CreateAsync(http.ReferenceDataActor(), request, cancellationToken), 201))
            .WithReferenceDataPermission(ReferenceDataPermissions.UnitOfMeasureCreate);

        group.MapPut(admin + "/{id:guid}", async (Guid id, UpdateUnitDimensionRequest request,
            UnitDimensionService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.UpdateAsync(http.ReferenceDataActor(), id, request, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.UnitOfMeasureUpdate);

        group.MapPost(admin + "/{id:guid}/clone", async (Guid id, PublishOrDisableRequest request,
            UnitDimensionService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.CloneAsync(http.ReferenceDataActor(), id, request, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.UnitOfMeasureCreate);

        group.MapPost(admin + "/{id:guid}/publish", async (Guid id, PublishOrDisableRequest request,
            UnitDimensionService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.PublishAsync(http.ReferenceDataActor(), id, request, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.UnitOfMeasurePublish);

        group.MapPost(admin + "/{id:guid}/disable", async (Guid id, PublishOrDisableRequest request,
            UnitDimensionService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.DisableAsync(http.ReferenceDataActor(), id, request, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.UnitOfMeasureDisable);

        group.MapGet(runtime + "/dimensions", async ([AsParameters] AvailableUnitDimensionListRequest query,
            UnitDimensionService service, HttpContext http, CancellationToken cancellationToken) =>
        {
            var page = await service.ListAvailableAsync(http.ReferenceDataActor(),
                new(query.PageIndex, query.PageSize, query.Keyword), cancellationToken);
            return Success(http, PageResult.Create(page.Items, page.Total, query.PageIndex, query.PageSize));
        }).WithReferenceDataPermission(ReferenceDataPermissions.UnitOfMeasureView);

        group.MapGet(runtime + "/dimensions/{nId}/revisions/{revision:int}", async (string nId, int revision,
            string? sourceScope, string? sourceTenantNId, UnitDimensionService service, HttpContext http,
            CancellationToken cancellationToken) =>
            Success(http, await service.GetRevisionAsync(http.ReferenceDataActor(), nId, revision,
                sourceScope, sourceTenantNId, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.UnitOfMeasureView);

        group.MapGet(runtime + "/dimensions/{nId}", async (string nId, string? sourceScope,
            string? sourceTenantNId, UnitDimensionService service, HttpContext http,
            CancellationToken cancellationToken) =>
            Success(http, await service.GetCurrentAsync(http.ReferenceDataActor(), nId,
                sourceScope, sourceTenantNId, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.UnitOfMeasureView);

        group.MapPost(runtime + "/convert", async (UnitConversionRequest request, UnitDimensionService service,
            HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.ConvertAsync(http.ReferenceDataActor(), request, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.UnitOfMeasureView);

        return endpoints;
    }

    private static IResult Success<T>(HttpContext http, T data, int status = 200)
    {
        var result = ApiResult.Ok(data);
        result.TraceId = Activity.Current?.Id ?? http.TraceIdentifier;
        return Results.Json(result, statusCode: status);
    }
}

public sealed record UnitDimensionListRequest(
    int PageIndex = 1,
    int PageSize = 20,
    string? Keyword = null,
    string? Status = null,
    string? ScopeType = null,
    string? SortField = null,
    bool Descending = false);

public sealed record AvailableUnitDimensionListRequest(
    int PageIndex = 1,
    int PageSize = 20,
    string? Keyword = null);
