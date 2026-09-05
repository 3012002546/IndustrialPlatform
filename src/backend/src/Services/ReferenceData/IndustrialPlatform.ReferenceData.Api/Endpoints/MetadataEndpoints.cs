using System.Diagnostics;
using IndustrialPlatform.ReferenceData.Api.Authorization;
using IndustrialPlatform.ReferenceData.Application.Metadata;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.Metadata;
using IndustrialPlatform.Web.Results;

namespace IndustrialPlatform.ReferenceData.Api.Endpoints;

public static class MetadataEndpoints
{
    public static IEndpointRouteBuilder MapMetadataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/reference-data")
            .AddEndpointFilter<ReferenceDataErrorFilter>();
        const string admin = "/admin/metadata-schemas";
        const string runtime = "/metadata-schemas";

        group.MapGet(admin, async ([AsParameters] MetadataSchemaListRequest query,
            MetadataSchemaService service, HttpContext http, CancellationToken cancellationToken) =>
        {
            var page = await service.SearchAsync(http.ReferenceDataActor(), new(query.PageIndex, query.PageSize,
                query.Keyword, query.Status, query.ScopeType, query.SortField, query.Descending), cancellationToken);
            return Success(http, PageResult.Create(page.Items, page.Total, query.PageIndex, query.PageSize));
        }).WithReferenceDataPermission(ReferenceDataPermissions.MetadataView);

        group.MapGet(admin + "/{id:guid}", async (Guid id, MetadataSchemaService service,
            HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.GetAsync(http.ReferenceDataActor(), id, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.MetadataView);

        group.MapPost(admin, async (CreateMetadataSchemaRequest request, MetadataSchemaService service,
            HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.CreateAsync(http.ReferenceDataActor(), request, cancellationToken), 201))
            .WithReferenceDataPermission(ReferenceDataPermissions.MetadataCreate);

        group.MapPut(admin + "/{id:guid}", async (Guid id, UpdateMetadataSchemaRequest request,
            MetadataSchemaService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.UpdateAsync(http.ReferenceDataActor(), id, request, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.MetadataUpdate);

        group.MapPost(admin + "/{id:guid}/clone", async (Guid id, PublishOrDisableRequest request,
            MetadataSchemaService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.CloneAsync(http.ReferenceDataActor(), id, request, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.MetadataCreate);

        group.MapGet(admin + "/{id:guid}/publication-check", async (Guid id,
            MetadataSchemaService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.CheckPublicationAsync(http.ReferenceDataActor(), id, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.MetadataView);

        group.MapPost(admin + "/{id:guid}/publish", async (Guid id, PublishOrDisableRequest request,
            MetadataSchemaService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.PublishAsync(http.ReferenceDataActor(), id, request, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.MetadataPublish);

        group.MapPost(admin + "/{id:guid}/disable", async (Guid id, PublishOrDisableRequest request,
            MetadataSchemaService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.DisableAsync(http.ReferenceDataActor(), id, request, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.MetadataDisable);

        group.MapGet(runtime + "/{nId}/revisions/{revision:int}", async (string nId, int revision,
            string? sourceScope, string? sourceTenantNId, MetadataSchemaService service, HttpContext http,
            CancellationToken cancellationToken) => Success(http, await service.GetRevisionAsync(
                http.ReferenceDataActor(), nId, revision, sourceScope, sourceTenantNId, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.MetadataView);

        group.MapGet(runtime + "/{nId}", async (string nId, string? factoryId,
            MetadataSchemaService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.GetEffectiveAsync(http.ReferenceDataActor(), nId, factoryId,
                cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.MetadataView);

        return endpoints;
    }

    private static IResult Success<T>(HttpContext http, T data, int status = 200)
    {
        var result = ApiResult.Ok(data);
        result.TraceId = Activity.Current?.Id ?? http.TraceIdentifier;
        return Results.Json(result, statusCode: status);
    }
}

public sealed record MetadataSchemaListRequest(
    int PageIndex = 1,
    int PageSize = 20,
    string? Keyword = null,
    string? Status = null,
    string? ScopeType = null,
    string? SortField = null,
    bool Descending = false);
