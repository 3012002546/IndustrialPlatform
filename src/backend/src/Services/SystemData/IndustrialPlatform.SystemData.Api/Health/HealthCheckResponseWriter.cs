using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IndustrialPlatform.SystemData.Api.Health;

/// <summary>
/// 健康检查响应写出器,仅输出状态与检查摘要,不含异常详情,避免泄漏连接串或凭据。
/// </summary>
public static class HealthCheckResponseWriter
{
    /// <summary>将健康报告写为精简 JSON。</summary>
    public static async Task Write(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
            }),
        };

        await JsonSerializer.SerializeAsync(context.Response.Body, payload, options: null, cancellationToken: context.RequestAborted);
    }
}
