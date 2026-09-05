using System.Diagnostics;
using IndustrialPlatform.ReferenceData.Api.Authorization;
using IndustrialPlatform.ReferenceData.Application.Dictionary;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.Dictionary;
using IndustrialPlatform.Web.Results;

namespace IndustrialPlatform.ReferenceData.Api.Endpoints;

public static class DictionaryEndpoints
{
    public static IEndpointRouteBuilder MapDictionaryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/reference-data").AddEndpointFilter<ReferenceDataErrorFilter>();
        group.MapGet("/admin/dictionaries", async ([AsParameters] DictionaryListRequest query, DictionaryService service, HttpContext http, CancellationToken cancellationToken) =>
        {
            var result = await service.SearchAsync(http.ReferenceDataActor(), new(query.PageIndex, query.PageSize, query.Keyword, query.Status, query.ScopeType, query.SortField, query.Descending), cancellationToken);
            return Success(http, PageResult.Create(result.Items, result.Total, query.PageIndex, query.PageSize));
        }).WithReferenceDataPermission(ReferenceDataPermissions.DictionaryView);
        group.MapGet("/admin/dictionaries/{id:guid}", async (Guid id, DictionaryService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.GetAsync(http.ReferenceDataActor(), id, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.DictionaryView);
        group.MapPost("/admin/dictionaries", async (CreateDictionaryRequest request, DictionaryService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.CreateAsync(http.ReferenceDataActor(), request, cancellationToken), 201))
            .WithReferenceDataPermission(ReferenceDataPermissions.DictionaryCreate);
        group.MapPut("/admin/dictionaries/{id:guid}", async (Guid id, UpdateDictionaryRequest request, DictionaryService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.UpdateAsync(http.ReferenceDataActor(), id, request, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.DictionaryUpdate);
        group.MapPost("/admin/dictionaries/{id:guid}/clone", async (Guid id, PublishOrDisableRequest request, DictionaryService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.CloneAsync(http.ReferenceDataActor(), id, request, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.DictionaryCreate);
        group.MapGet("/admin/dictionaries/{id:guid}/publication-check", async (Guid id, DictionaryService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.CheckPublicationAsync(http.ReferenceDataActor(), id, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.DictionaryView);
        group.MapPost("/admin/dictionaries/{id:guid}/publish", async (Guid id, PublishOrDisableRequest request, DictionaryService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.PublishAsync(http.ReferenceDataActor(), id, request, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.DictionaryPublish);
        group.MapPost("/admin/dictionaries/{id:guid}/disable", async (Guid id, PublishOrDisableRequest request, DictionaryService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.DisableAsync(http.ReferenceDataActor(), id, request, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.DictionaryDisable);
        group.MapGet("/dictionaries/{nId}", async (string nId, string? factoryId, DictionaryService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.GetEffectiveAsync(http.ReferenceDataActor(), nId, factoryId, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.DictionaryView);
        return endpoints;
    }

    private static IResult Success<T>(HttpContext http, T data, int status = 200)
    {
        var result = ApiResult.Ok(data);
        result.TraceId = Activity.Current?.Id ?? http.TraceIdentifier;
        return Results.Json(result, statusCode: status);
    }
}

public sealed record DictionaryListRequest(int PageIndex = 1, int PageSize = 20, string? Keyword = null,
    string? Status = null, string? ScopeType = null, string? SortField = null, bool Descending = false);
