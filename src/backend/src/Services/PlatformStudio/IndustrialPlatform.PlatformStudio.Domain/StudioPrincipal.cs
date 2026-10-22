using System.Security.Cryptography;
using System.Text;

namespace IndustrialPlatform.PlatformStudio.Domain;

/// <summary>仅由可信身份适配器建立的请求上下文；浏览器不能覆盖租户、主体或工厂范围。</summary>
public sealed record StudioPrincipal(
    string TenantNId, string SubjectNId, string IssuerKey, string DisplayName,
    IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions, IReadOnlyList<string> FactoryNIds,
    string AuthVersion, string PolicyVersion, DateTimeOffset IssuedOn, DateTimeOffset ExpiresOn,
    string? ExternalSubjectId = null, string? ExternalTenantId = null)
{
    public bool HasPermission(string permission) => Permissions.Contains(permission, StringComparer.Ordinal);

    public static string ExternalNId(string issuerKey, string tenant, string subject, string prefix = "mes-")
    {
        var inputs = new[] { issuerKey, tenant, subject };
        if (inputs.Any(value => string.IsNullOrEmpty(value) || value.Contains('\0')))
            throw new StudioException("STUDIO_FORBIDDEN", "外部身份标识无效。", 403);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0', inputs)));
        return prefix + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

/// <summary>固定资源归属，客户端kind不能动态注册模块或执行任意代码。</summary>
public static class StudioKinds
{
    public static IReadOnlyDictionary<string, string> Modules { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Dataset"] = "Core", ["LedgerModel"] = "LowCode", ["LowCodePage"] = "LowCode",
        ["Dashboard"] = "Dashboard", ["Playlist"] = "Dashboard", ["Report"] = "Report",
        ["ReportBundle"] = "Report", ["TraceModel"] = "Traceability",
    };

    public static string PermissionPrefix(string kind) => kind switch
    {
        "Dataset" => "studio.dataset", "LedgerModel" or "LowCodePage" => "studio.lowcode",
        "Dashboard" or "Playlist" => "studio.dashboard", "Report" or "ReportBundle" => "studio.report",
        "TraceModel" => "studio.trace", _ => throw new StudioException("STUDIO_VALIDATION", "不支持的资源类型。", path: "kind"),
    };

    public static IReadOnlyList<string> Permissions { get; } =
        new[] { "source.view", "source.manage", "source.test", "dataset.view", "dataset.design", "dataset.publish", "dataset.run",
            "lowcode.view", "lowcode.design", "lowcode.publish", "lowcode.run", "ledger.read", "ledger.write",
            "dashboard.view", "dashboard.design", "dashboard.publish", "dashboard.run", "report.view", "report.design", "report.publish",
            "report.run", "report.export", "trace.view", "trace.design", "trace.publish", "trace.run", "workbook.view", "workbook.edit",
            "workbook.export", "resource.grant", "deployment.inspect", "audit.view", "audit.export", "report.reconcile" }
        .Select(permission => "studio." + permission).ToArray();
}
