using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Report;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.Json;

namespace IndustrialPlatform.PlatformStudio.Api;

/// <summary>Native WOPI protocol, intentionally outside the Studio ApiResult/CSRF session boundary.</summary>
public static class StudioWopiEndpoints
{
    // WOPI field names and numeric Size are protocol-defined, independent of platform JSON conventions.
    private static readonly JsonSerializerOptions WopiJson = new() { PropertyNamingPolicy = null };
    public static void MapStudioWopi(this IEndpointRouteBuilder endpoints, StudioDeploymentProfile profile)
    {
        var group = endpoints.MapGroup("/office/wopi/files");
        group.AddEndpointFilter(async (ctx, next) =>
        {
            try
            {
                if (!profile.Office.Admitted) return Results.StatusCode(503);
                ctx.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(ctx);
            }
            catch (WopiLockConflictException conflict) { ctx.HttpContext.Response.Headers["X-WOPI-Lock"] = conflict.CurrentLock; return Results.StatusCode(409); }
            catch (StudioException e) when (e.Code == "WORKBOOK_SCAN_PENDING")
            {
                ctx.HttpContext.Response.Headers.RetryAfter = "2";
                return Results.StatusCode(503);
            }
            catch (StudioException e) { return Results.StatusCode(e.StatusCode is 403 or 404 ? 404 : e.StatusCode); }
            catch (IndustrialPlatform.SystemData.Application.Operations.SystemDataOperationException e) { return Results.StatusCode(e.StatusCode is 403 or 404 ? 404 : e.StatusCode); }
        });
        group.MapGet("/{sessionNId}", async (string sessionNId, HttpContext http, StudioWorkbooks books, CancellationToken ct) => Results.Json(await books.CheckFileInfoAsync(sessionNId, Token(http), ct), WopiJson));
        group.MapGet("/{sessionNId}/contents", async (string sessionNId, HttpContext http, StudioWorkbooks books, CancellationToken ct) => Results.Stream(await books.GetFileAsync(sessionNId, Token(http), ct), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
        group.MapPost("/{sessionNId}", async (string sessionNId, HttpContext http, StudioWorkbooks books, CancellationToken ct) =>
        {
            var operation = http.Request.Headers["X-WOPI-Override"].ToString();
            if (operation is not ("LOCK" or "REFRESH_LOCK" or "UNLOCK" or "GET_LOCK")) return Results.StatusCode(501);
            var result = await books.LockAsync(sessionNId, Token(http), operation, http.Request.Headers["X-WOPI-Lock"].ToString(), ct, http.Request.Headers.TryGetValue("X-WOPI-OldLock", out var oldLock) ? oldLock.ToString() : null);
            if (operation == "GET_LOCK") http.Response.Headers["X-WOPI-Lock"] = result;
            return Results.Ok();
        });
        group.MapPost("/{sessionNId}/contents", async (string sessionNId, HttpContext http, StudioWorkbooks books, CancellationToken ct) =>
        {
            if (http.Request.Headers["X-WOPI-Override"] != "PUT") return Results.StatusCode(501);
            var result = await books.PutFileAsync(sessionNId, Token(http), http.Request.Headers["X-WOPI-Lock"].ToString(), http.Request.Body, ct, waitForConfirmation: true);
            http.Response.Headers["X-WOPI-ItemVersion"] = result.ContentVersion;
            if (!result.Saved) { http.Response.Headers["X-WOPI-Lock"] = http.Request.Headers["X-WOPI-Lock"]; return Results.Json(new { code = "WORKBOOK_CONFLICT", currentContentVersion = result.ContentVersion, recoveryFileNId = result.RecoveryFileNId }, statusCode: 409); }
            return Results.Ok();
        });
    }
    private static string Token(HttpContext http)
    {
        var authorization = http.Request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization[7..] : http.Request.Query["access_token"].ToString();
    }
}
