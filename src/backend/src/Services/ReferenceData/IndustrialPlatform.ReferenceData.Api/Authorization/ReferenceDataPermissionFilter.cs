using System.Diagnostics;
using System.Globalization;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.Security;
using IndustrialPlatform.Web.Results;

namespace IndustrialPlatform.ReferenceData.Api.Authorization;

/// <summary>Dynamic Identity decisions for ReferenceData endpoints, without replacing other modules' authorization handlers.</summary>
public sealed class ReferenceDataPermissionFilter(IReferenceDataPermissionEvaluator evaluator) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var permission = http.GetEndpoint()?.Metadata.GetMetadata<ReferenceDataPermissionMetadata>();
        var user = http.User.FindFirst(ClaimConstants.UserNId)?.Value;
        var tenant = http.User.FindFirst(ClaimConstants.TenantId)?.Value;
        var session = http.User.FindFirst(ClaimConstants.SessionId)?.Value;
        if (permission is null || http.User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(user)
            || string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(session)
            || !int.TryParse(http.User.FindFirst(ClaimConstants.AuthVersion)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var version))
            return Failure(http, 401, "401");
        var header = http.Request.Headers.Authorization.ToString();
        var request = new ReferenceDataPermissionRequest(tenant, user, session, version, permission.NId,
            header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null);
        var decision = await evaluator.EvaluateAsync(request, http.RequestAborted);
        if (!decision.Allowed)
            return decision.Reason switch
            {
                ReferenceDataPermissionDenialReason.SessionInvalid => Failure(http, 401, "401"),
                ReferenceDataPermissionDenialReason.SecurityStoreUnavailable => Failure(http, 503, "ID_AUTH_SECURITY_STORE_UNAVAILABLE"),
                _ => Failure(http, 403, "ID_PERMISSION_DENIED"),
            };
        var platform = HttpMethods.IsGet(http.Request.Method) ? false
            : (await evaluator.EvaluateAsync(request with { PermissionNId = ReferenceDataPermissions.PlatformManage }, http.RequestAborted)).Allowed;
        var parameterDisable = permission.NId == ReferenceDataPermissions.ParameterUpdate
            ? await evaluator.EvaluateAsync(request with { PermissionNId = ReferenceDataPermissions.ParameterDisable }, http.RequestAborted) : null;
        var dynamicDisable = permission.NId == ReferenceDataPermissions.DynamicPropertyUpdate
            ? await evaluator.EvaluateAsync(request with { PermissionNId = ReferenceDataPermissions.DynamicPropertyDisable }, http.RequestAborted) : null;
        http.Items[typeof(ReferenceDataActor)] = new ReferenceDataActor(tenant, user, platform, Activity.Current?.Id ?? http.TraceIdentifier)
        {
            ParameterDisableDecision = parameterDisable,
            DynamicPropertyDisableDecision = dynamicDisable,
        };
        return await next(context);
    }

    internal static IResult Failure(HttpContext http, int status, string code)
    {
        var result = ApiResult.Fail(code, code);
        result.TraceId = Activity.Current?.Id ?? http.TraceIdentifier;
        return Results.Json(result, statusCode: status);
    }
}
public sealed record ReferenceDataPermissionMetadata(string NId);

public static class ReferenceDataEndpointAuthorization
{
    public static RouteHandlerBuilder WithReferenceDataPermission(this RouteHandlerBuilder endpoint, string permission) =>
        endpoint.RequireAuthorization().WithMetadata(new ReferenceDataPermissionMetadata(permission)).AddEndpointFilter<ReferenceDataPermissionFilter>();

    public static ReferenceDataActor ReferenceDataActor(this HttpContext context) =>
        (ReferenceDataActor)context.Items[typeof(ReferenceDataActor)]!;
}
