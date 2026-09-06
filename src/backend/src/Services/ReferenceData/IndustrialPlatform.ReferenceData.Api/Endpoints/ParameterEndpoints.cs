using System.Diagnostics;
using IndustrialPlatform.ReferenceData.Api.Authorization;
using IndustrialPlatform.ReferenceData.Application.Parameter;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.Parameter;
using IndustrialPlatform.Web.Results;

namespace IndustrialPlatform.ReferenceData.Api.Endpoints;

public static class ParameterEndpoints
{
    public static IEndpointRouteBuilder MapParameterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/reference-data").AddEndpointFilter<ReferenceDataErrorFilter>();
        const string admin = "/admin/configuration-domains";
        group.MapGet(admin, async ([AsParameters] ParameterListRequest query, ParameterService service, HttpContext http, CancellationToken cancellationToken) =>
        {
            var result = await service.SearchAsync(http.ReferenceDataActor(), new(query.PageIndex, query.PageSize, query.Keyword, query.Status, query.ScopeType, query.SortField, query.Descending), cancellationToken);
            return Success(http, PageResult.Create(result.Items, result.Total, query.PageIndex, query.PageSize));
        }).WithReferenceDataPermission(ReferenceDataPermissions.ParameterView);
        group.MapGet(admin + "/{id:guid}", async (Guid id, ParameterService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.GetAsync(http.ReferenceDataActor(), id, cancellationToken))).WithReferenceDataPermission(ReferenceDataPermissions.ParameterView);
        group.MapPost(admin, async (CreateConfigurationDomainRequest request, ParameterService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.CreateAsync(http.ReferenceDataActor(), request, cancellationToken), 201)).WithReferenceDataPermission(ReferenceDataPermissions.ParameterCreate);
        group.MapPut(admin + "/{id:guid}", async (Guid id, UpdateConfigurationDomainRequest request, ParameterService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.UpdateAsync(http.ReferenceDataActor(), id, request, cancellationToken))).WithReferenceDataPermission(ReferenceDataPermissions.ParameterUpdate);
        group.MapPost(admin + "/{id:guid}/keys", async (Guid id, ConfigurationKeyRequest request, ParameterService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.AddKeyAsync(http.ReferenceDataActor(), id, request, cancellationToken), 201)).WithReferenceDataPermission(ReferenceDataPermissions.ParameterCreate);
        group.MapPut(admin + "/{id:guid}/keys/{keyId:guid}", async (Guid id, Guid keyId, ConfigurationKeyRequest request, ParameterService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.UpdateKeyAsync(http.ReferenceDataActor(), id, keyId, request, cancellationToken))).WithReferenceDataPermission(ReferenceDataPermissions.ParameterUpdate);
        group.MapPost(admin + "/{id:guid}/keys/{keyId:guid}/values", async (Guid id, Guid keyId, ConfigurationValueRequest request, ParameterService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.AddValueAsync(http.ReferenceDataActor(), id, keyId, request, cancellationToken), 201)).WithReferenceDataPermission(ReferenceDataPermissions.ParameterCreate);
        group.MapPut(admin + "/{id:guid}/keys/{keyId:guid}/values/{valueId:guid}", async (Guid id, Guid keyId, Guid valueId, ConfigurationValueRequest request, ParameterService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.UpdateValueAsync(http.ReferenceDataActor(), id, keyId, valueId, request, cancellationToken))).WithReferenceDataPermission(ReferenceDataPermissions.ParameterUpdate);
        group.MapDelete(admin + "/{id:guid}/keys/{keyId:guid}/values/{valueId:guid}", async (Guid id, Guid keyId, Guid valueId, [Microsoft.AspNetCore.Mvc.FromBody] ConfigurationChildStateRequest request, ParameterService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.DeleteValueAsync(http.ReferenceDataActor(), id, keyId, valueId, request, cancellationToken))).WithReferenceDataPermission(ReferenceDataPermissions.ParameterUpdate);
        group.MapPost(admin + "/{id:guid}/enable", async (Guid id, ConfigurationDomainStateRequest request, ParameterService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.SetStatusAsync(http.ReferenceDataActor(), id, true, request, cancellationToken))).WithReferenceDataPermission(ReferenceDataPermissions.ParameterUpdate);
        group.MapPost(admin + "/{id:guid}/keys/{keyId:guid}/enable", async (Guid id, Guid keyId, ConfigurationChildStateRequest request, ParameterService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.SetKeyStatusAsync(http.ReferenceDataActor(), id, keyId, true, request, cancellationToken))).WithReferenceDataPermission(ReferenceDataPermissions.ParameterUpdate);
        group.MapPost(admin + "/{id:guid}/keys/{keyId:guid}/values/{valueId:guid}/enable", async (Guid id, Guid keyId, Guid valueId, ConfigurationChildStateRequest request, ParameterService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.SetValueEnabledAsync(http.ReferenceDataActor(), id, keyId, valueId, true, request, cancellationToken))).WithReferenceDataPermission(ReferenceDataPermissions.ParameterUpdate);
        group.MapPost(admin + "/{id:guid}/disable", async (Guid id, ConfigurationDomainStateRequest request, ParameterService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.SetStatusAsync(http.ReferenceDataActor(), id, false, request, cancellationToken))).WithReferenceDataPermission(ReferenceDataPermissions.ParameterDisable);
        group.MapPost(admin + "/{id:guid}/keys/{keyId:guid}/disable", async (Guid id, Guid keyId, ConfigurationChildStateRequest request, ParameterService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.SetKeyStatusAsync(http.ReferenceDataActor(), id, keyId, false, request, cancellationToken))).WithReferenceDataPermission(ReferenceDataPermissions.ParameterDisable);
        group.MapPost(admin + "/{id:guid}/keys/{keyId:guid}/values/{valueId:guid}/disable", async (Guid id, Guid keyId, Guid valueId, ConfigurationChildStateRequest request, ParameterService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.SetValueEnabledAsync(http.ReferenceDataActor(), id, keyId, valueId, false, request, cancellationToken))).WithReferenceDataPermission(ReferenceDataPermissions.ParameterDisable);
        group.MapGet(admin + "/{id:guid}/keys/{keyId:guid}/history", async (Guid id, Guid keyId, [AsParameters] ParameterHistoryRequest query, ParameterService service, HttpContext http, CancellationToken cancellationToken) =>
        {
            var result = await service.HistoryAsync(http.ReferenceDataActor(), id, keyId, query.PageIndex, query.PageSize, cancellationToken);
            return Success(http, PageResult.Create(result.Items, result.Total, query.PageIndex, query.PageSize));
        }).WithReferenceDataPermission(ReferenceDataPermissions.ParameterView);
        group.MapGet(admin + "/{id:guid}/history", async (Guid id, [AsParameters] ParameterHistoryRequest query, ParameterService service, HttpContext http, CancellationToken cancellationToken) =>
        {
            var result = await service.HistoryAsync(http.ReferenceDataActor(), id, null, query.PageIndex, query.PageSize, cancellationToken);
            return Success(http, PageResult.Create(result.Items, result.Total, query.PageIndex, query.PageSize));
        }).WithReferenceDataPermission(ReferenceDataPermissions.ParameterView);
        group.MapGet("/configuration-domains/{appDomainNId}", async (string appDomainNId, string? factoryId, ParameterService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.ResolveDomainAsync(http.ReferenceDataActor(), appDomainNId, factoryId, cancellationToken))).WithReferenceDataPermission(ReferenceDataPermissions.ParameterView);
        group.MapGet("/configuration-domains/{appDomainNId}/keys/{keyNId}", async (string appDomainNId, string keyNId, string? factoryId, ParameterService service, HttpContext http, CancellationToken cancellationToken) =>
            Success(http, await service.ResolveAsync(http.ReferenceDataActor(), appDomainNId, keyNId, factoryId, cancellationToken))).WithReferenceDataPermission(ReferenceDataPermissions.ParameterView);
        return endpoints;
    }

    private static IResult Success<T>(HttpContext http, T data, int status = 200)
    {
        var result = ApiResult.Ok(data);
        result.TraceId = Activity.Current?.Id ?? http.TraceIdentifier;
        return Results.Json(result, statusCode: status);
    }
}
public sealed record ParameterListRequest(int PageIndex = 1, int PageSize = 20, string? Keyword = null, string? Status = null, string? ScopeType = null, string? SortField = null, bool Descending = false);
public sealed record ParameterHistoryRequest(int PageIndex = 1, int PageSize = 20);
