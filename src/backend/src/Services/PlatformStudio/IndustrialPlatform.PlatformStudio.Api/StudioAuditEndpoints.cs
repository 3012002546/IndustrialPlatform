using System.Text;
using System.Text.Json;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IndustrialPlatform.PlatformStudio.Api;

/// <summary>独立模式的统一审计入口；查询范围固定为已验证主体的当前租户。</summary>
public static class StudioAuditEndpoints
{
    public sealed record AuditQuery(string? ProducerServiceKey = null, string? Action = null, DateTimeOffset? From = null, DateTimeOffset? Until = null, string? Actor = null, string? ObjectNId = null, int Page = 1, int PageSize = 50);
    public sealed record AuditExport(string? ProducerServiceKey = null, string? Action = null, DateTimeOffset? From = null, DateTimeOffset? Until = null, string? Actor = null, string? ObjectNId = null);
    private static JsonSerializerOptions Json => StudioHttpJson.Options;
    public static RouteGroupBuilder MapStudioAudit(this RouteGroupBuilder group, StudioDeploymentProfile profile)
    {
        if (profile.DeploymentMode != "ExternalMes") return group;
        group.MapGet("/audit", async ([AsParameters] AuditQuery query, HttpContext http, IStudioAuthorization authorization, IAuditFilteredQueries audit, CancellationToken cancellationToken) =>
        {
            var principal = StudioEndpoints.Principal(http);
            authorization.RequirePermission(principal, "studio.audit.view");
            NoCache(http);
            var result = await audit.QueryFilteredAsync(principal.TenantNId, principal.SubjectNId, query.ProducerServiceKey, query.Action, query.From, query.Until, query.Actor, query.ObjectNId, query.Page, query.PageSize, cancellationToken);
            return Results.Json(ApiResult.Ok(result), Json);
        });
        group.MapPost("/audit/export", async (AuditExport request, HttpContext http, IStudioAuthorization authorization, IAuditFilteredQueries audit, CancellationToken cancellationToken) =>
        {
            var principal = StudioEndpoints.Principal(http);
            authorization.RequirePermission(principal, "studio.audit.export");
            NoCache(http);
            var csv = await audit.ExportFilteredCsvAsync(principal.TenantNId, principal.SubjectNId, request.ProducerServiceKey, request.Action, request.From, request.Until, request.Actor, request.ObjectNId, cancellationToken);
            return Results.File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", "studio-audit.csv");
        });
        return group;
    }
    private static void NoCache(HttpContext http) { http.Response.Headers.CacheControl = "no-store"; http.Response.Headers.XContentTypeOptions = "nosniff"; }
}
