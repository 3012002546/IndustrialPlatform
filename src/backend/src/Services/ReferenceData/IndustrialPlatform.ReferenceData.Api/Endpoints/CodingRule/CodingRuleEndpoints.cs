using System.Diagnostics;
using IndustrialPlatform.ReferenceData.Api.Authorization;
using IndustrialPlatform.ReferenceData.Application.CodingRule;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.CodingRule;
using IndustrialPlatform.Web.Results;

namespace IndustrialPlatform.ReferenceData.Api.Endpoints;

public static class CodingRuleEndpoints
{
    public static IEndpointRouteBuilder MapCodingRuleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/reference-data").AddEndpointFilter<ReferenceDataErrorFilter>();
        const string admin = "/admin/coding-rules";
        const string runtime = "/coding-rules";

        group.MapGet(admin, async ([AsParameters] CodingRuleListRequest query, CodingRuleService service,
            HttpContext http, CancellationToken cancellationToken) =>
        {
            var page = await service.SearchAsync(http.ReferenceDataActor(),
                new(query.PageIndex, query.PageSize, query.Keyword, query.Status, query.ScopeType,
                    query.SortField, query.Descending), cancellationToken);
            return Success(http, PageResult.Create(page.Items, page.Total, query.PageIndex, query.PageSize));
        }).WithReferenceDataPermission(ReferenceDataPermissions.CodingRuleView);

        group.MapGet(admin + "/{id:guid}", async (Guid id, CodingRuleService service,
            HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.GetAsync(http.ReferenceDataActor(), id, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.CodingRuleView);

        group.MapPost(admin, async (CreateCodingRuleRequest request, CodingRuleService service,
            HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.CreateAsync(http.ReferenceDataActor(), request, cancellationToken), 201))
            .WithReferenceDataPermission(ReferenceDataPermissions.CodingRuleCreate);

        group.MapPut(admin + "/{id:guid}", async (Guid id, UpdateCodingRuleRequest request,
            CodingRuleService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.UpdateAsync(http.ReferenceDataActor(), id, request, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.CodingRuleUpdate);

        group.MapPost(admin + "/{id:guid}/clone", async (Guid id, PublishOrDisableRequest request,
            CodingRuleService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.CloneAsync(http.ReferenceDataActor(), id, request, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.CodingRuleCreate);

        group.MapPost(admin + "/{id:guid}/publish", async (Guid id, PublishOrDisableRequest request,
            CodingRuleService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.PublishAsync(http.ReferenceDataActor(), id, request, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.CodingRulePublish);

        group.MapPost(admin + "/{id:guid}/disable", async (Guid id, PublishOrDisableRequest request,
            CodingRuleService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.DisableAsync(http.ReferenceDataActor(), id, request, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.CodingRuleDisable);

        group.MapPost(admin + "/{id:guid}/preview", async (Guid id, PreviewCodeRequest request,
            CodingRuleService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.PreviewByIdAsync(http.ReferenceDataActor(), id, request,
                cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.CodingRulePreview);

        group.MapPost(runtime + "/{nId}/preview", async (string nId, PreviewCodeRequest request,
            CodingRuleService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.PreviewAsync(http.ReferenceDataActor(), nId, request,
                cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.CodingRulePreview);

        group.MapPost(runtime + "/{nId}/generate", async (string nId, GenerateCodeRequest request,
            CodingRuleService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.GenerateAsync(http.ReferenceDataActor(), nId, request,
                IdempotencyKey(http), cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.CodingRuleGenerate);

        return endpoints;
    }

    private static IResult Success<T>(HttpContext http, T data, int status = 200)
    {
        var result = ApiResult.Ok(data);
        result.TraceId = Activity.Current?.Id ?? http.TraceIdentifier;
        return Results.Json(result, statusCode: status);
    }

    private static string? IdempotencyKey(HttpContext http) =>
        http.Request.Headers.TryGetValue("Idempotency-Key", out var values) && values.Count > 0
            ? values[0]
            : null;
}

public sealed record CodingRuleListRequest(
    int PageIndex = 1,
    int PageSize = 20,
    string? Keyword = null,
    string? Status = null,
    string? ScopeType = null,
    string? SortField = null,
    bool Descending = false);
