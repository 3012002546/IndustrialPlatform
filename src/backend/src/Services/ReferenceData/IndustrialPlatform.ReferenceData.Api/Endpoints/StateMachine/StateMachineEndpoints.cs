using System.Diagnostics;
using IndustrialPlatform.ReferenceData.Api.Authorization;
using IndustrialPlatform.ReferenceData.Application.StateMachine;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.StateMachine;
using IndustrialPlatform.Web.Results;

namespace IndustrialPlatform.ReferenceData.Api.Endpoints;

public static class StateMachineEndpoints
{
    public static IEndpointRouteBuilder MapStateMachineEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/reference-data")
            .AddEndpointFilter<ReferenceDataErrorFilter>();
        const string admin = "/admin/state-machines";
        const string runtime = "/state-machines";

        group.MapGet(admin, async ([AsParameters] StateMachineListRequest query, StateMachineService service,
            HttpContext http, CancellationToken cancellationToken) =>
        {
            var page = await service.SearchAsync(http.ReferenceDataActor(),
                new(query.PageIndex, query.PageSize, query.Keyword, query.Status, query.ScopeType,
                    query.SortField, query.Descending), cancellationToken);
            return Success(http, PageResult.Create(page.Items, page.Total, query.PageIndex, query.PageSize));
        }).WithReferenceDataPermission(ReferenceDataPermissions.StateMachineView);

        group.MapGet(admin + "/{id:guid}", async (Guid id, StateMachineService service,
            HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.GetAsync(http.ReferenceDataActor(), id, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.StateMachineView);

        group.MapGet(admin + "/{id:guid}/publication-check", async (Guid id,
            StateMachineService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.CheckPublicationAsync(
                http.ReferenceDataActor(), id, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.StateMachineView);

        group.MapPost(admin, async (CreateStateMachineRequest request, StateMachineService service,
            HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.CreateAsync(http.ReferenceDataActor(), request, cancellationToken), 201))
            .WithReferenceDataPermission(ReferenceDataPermissions.StateMachineCreate);

        group.MapPut(admin + "/{id:guid}", async (Guid id, UpdateStateMachineRequest request,
            StateMachineService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.UpdateAsync(http.ReferenceDataActor(), id, request, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.StateMachineUpdate);

        group.MapPost(admin + "/{id:guid}/clone", async (Guid id, PublishOrDisableRequest request,
            StateMachineService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.CloneAsync(http.ReferenceDataActor(), id, request, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.StateMachineCreate);

        group.MapPost(admin + "/{id:guid}/publish", async (Guid id, PublishOrDisableRequest request,
            StateMachineService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.PublishAsync(http.ReferenceDataActor(), id, request, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.StateMachinePublish);

        group.MapPost(admin + "/{id:guid}/disable", async (Guid id, PublishOrDisableRequest request,
            StateMachineService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.DisableAsync(http.ReferenceDataActor(), id, request, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.StateMachineDisable);

        group.MapGet(runtime, async ([AsParameters] AvailableStateMachineListRequest query,
            StateMachineService service, HttpContext http, CancellationToken cancellationToken) =>
        {
            var page = await service.ListAvailableAsync(http.ReferenceDataActor(),
                new(query.PageIndex, query.PageSize, query.Keyword), cancellationToken);
            return Success(http, PageResult.Create(page.Items, page.Total, query.PageIndex, query.PageSize));
        }).WithReferenceDataPermission(ReferenceDataPermissions.StateMachineView);

        group.MapGet(runtime + "/{nId}/revisions/{revision:int}", async (
            string nId, int revision, string? sourceScope, string? sourceTenantNId,
            StateMachineService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.GetRevisionAsync(http.ReferenceDataActor(), nId, revision,
                sourceScope, sourceTenantNId, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.StateMachineView);

        group.MapGet(runtime + "/{nId}", async (
            string nId, string? sourceScope, string? sourceTenantNId,
            StateMachineService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.GetCurrentAsync(http.ReferenceDataActor(), nId,
                sourceScope, sourceTenantNId, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.StateMachineView);

        group.MapPost(runtime + "/{nId}/evaluate", async (
            string nId, TransitionEvaluationRequest request, StateMachineService service,
            HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.EvaluateAsync(
                http.ReferenceDataActor(), nId, request, cancellationToken)))
            .WithReferenceDataPermission(ReferenceDataPermissions.StateMachineView);

        return endpoints;
    }

    private static IResult Success<T>(HttpContext http, T data, int status = 200)
    {
        var result = ApiResult.Ok(data);
        result.TraceId = Activity.Current?.Id ?? http.TraceIdentifier;
        return Results.Json(result, statusCode: status);
    }
}

public sealed record StateMachineListRequest(
    int PageIndex = 1,
    int PageSize = 20,
    string? Keyword = null,
    string? Status = null,
    string? ScopeType = null,
    string? SortField = null,
    bool Descending = false);

public sealed record AvailableStateMachineListRequest(
    int PageIndex = 1,
    int PageSize = 20,
    string? Keyword = null);
