using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Identity.Application.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.EmbeddedHost;

/// <summary>
/// MES 当前登录适配入口。实现者应在服务端读取现有 MES 的当前用户和会话接口，
/// 再映射稳定的来源、外部租户、用户主体、会话标识和安全版本。
/// 浏览器不能从请求体自报这些字段；未实现时必须保持失败关闭。
/// </summary>
public interface IEmbeddedCurrentUserAdapter
{
    /// <summary>
    /// Reads the already authenticated MES user server-side. If an account was
    /// supplied, the adapter must compare it with that MES user's account and
    /// return a principal carrying the verified value; it must not trust the
    /// browser's value as an identity claim.
    /// </summary>
    Task<EmbeddedSourcePrincipal?> ResolveAsync(HttpContext context, string? requestedAccountNId, CancellationToken cancellationToken);
}

/// <summary>把可替换的 MES 当前登录适配器接入既有握手协议。</summary>
public sealed class AdapterBackedEmbeddedSourcePrincipalResolver(IEmbeddedCurrentUserAdapter adapter)
    : IEmbeddedSourcePrincipalResolver
{
    public Task<EmbeddedSourcePrincipal?> ResolveAsync(HttpContext context, string? requestedAccountNId, CancellationToken cancellationToken) =>
        adapter.ResolveAsync(context, requestedAccountNId, cancellationToken);
}

/// <summary>
/// 默认实现故意不猜测 MES API，也不接受客户端自报身份。部署者应替换 DI 注册；
/// 没有真实当前登录适配器时，挑战不能签发断言。
/// </summary>
public sealed class NotConfiguredEmbeddedCurrentUserAdapter : IEmbeddedCurrentUserAdapter
{
    public Task<EmbeddedSourcePrincipal?> ResolveAsync(HttpContext context, CancellationToken cancellationToken) =>
        ResolveAsync(context, null, cancellationToken);

    public Task<EmbeddedSourcePrincipal?> ResolveAsync(HttpContext context, string? requestedAccountNId, CancellationToken cancellationToken) =>
        Task.FromException<EmbeddedSourcePrincipal?>(new EmbeddedHandshakeException(
            StatusCodes.Status503ServiceUnavailable,
            "EMBEDDED_SOURCE_ADAPTER_NOT_CONFIGURED",
            "MES 当前登录适配器尚未配置。"));
}

/// <summary>
/// Production-safe default identity projection. The MES adapter supplies the
/// verified external subject; the host derives a stable platform user key from
/// that subject and the configured platform tenant. Currentness is checked by
/// the source resolver on each HTTP/Hub operation, so deployments only need to
/// provide the MES current-user and directory adapters.
/// </summary>
public sealed class ServerDerivedEmbeddedSubjectIdentityMapper(IConfiguration configuration) : IEmbeddedSubjectIdentityMapper
{
    public Task<EmbeddedIdentity?> MapAsync(EmbeddedIdentityAssertion assertion, CancellationToken cancellationToken)
    {
        var tenant = configuration["EmbeddedCollaboration:PlatformTenantNId"];
        if (string.IsNullOrWhiteSpace(tenant))
            return Task.FromResult<EmbeddedIdentity?>(null);

        var userNId = ConfigurationEmbeddedSubjectIdentityMapper.UserNId(
            assertion.SourceNId,
            assertion.ExternalTenantNId,
            assertion.ExternalSubject);
        return Task.FromResult<EmbeddedIdentity?>(new EmbeddedIdentity(
            tenant,
            userNId,
            assertion.SourceSessionNId,
            assertion.SecurityVersion)
        {
            AccountNId = assertion.AccountNId,
            SourceNId = assertion.SourceNId,
            ExternalTenantNId = assertion.ExternalTenantNId,
            ExternalSubject = assertion.ExternalSubject,
            DisplayName = assertion.DisplayName,
        });
    }

    public Task<bool> IsCurrentAsync(EmbeddedStoredSession session, CancellationToken cancellationToken) =>
        Task.FromResult(true);
}

/// <summary>
/// Collaboration 需要的外部用户目录契约。实现者可调用 MES 用户接口，
/// 也可调用独立的受信任后端适配服务；当前登录身份由单独的登录适配器核验。
/// </summary>
public interface IEmbeddedCollaborationAccessAdapter
{
    Task<DirectoryUser?> GetDirectoryUserAsync(
        string tenantNId,
        string userNId,
        CancellationToken cancellationToken);

    Task<DirectorySearchPage> SearchDirectoryAsync(
        string tenantNId,
        string actorUserNId,
        string keyword,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken);
}

/// <summary>用户目录未接线时返回空结果，不推断 MES 中的用户。</summary>
public sealed class NotConfiguredEmbeddedCollaborationAccessAdapter : IEmbeddedCollaborationAccessAdapter
{
    public Task<DirectoryUser?> GetDirectoryUserAsync(string tenantNId, string userNId, CancellationToken cancellationToken) =>
        Task.FromResult<DirectoryUser?>(null);

    public Task<DirectorySearchPage> SearchDirectoryAsync(string tenantNId, string actorUserNId, string keyword, string? cursor, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult(new DirectorySearchPage([], null));

}

/// <summary>
/// 仅供参考宿主使用的用户目录配置夹具。
/// 真实 MES 应替换此适配器，通过用户查询接口提供目录。
/// </summary>
public sealed class ConfigurationEmbeddedCollaborationAccessAdapter(IConfiguration configuration)
    : IEmbeddedCollaborationAccessAdapter
{
    public Task<DirectoryUser?> GetDirectoryUserAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
    {
        foreach (var candidate in SubjectMappings())
        {
            var identity = TryReadIdentity(candidate.SourceNId, candidate.ExternalTenantNId, candidate.Subject, candidate.Section);
            if (identity is not null && string.Equals(identity.TenantNId, tenantNId, StringComparison.Ordinal) && string.Equals(identity.UserNId, userNId, StringComparison.Ordinal))
                return Task.FromResult<DirectoryUser?>(new DirectoryUser(userNId, identity.DisplayName, "Active", identity.SecurityVersion));
        }
        return Task.FromResult<DirectoryUser?>(null);
    }

    public Task<DirectorySearchPage> SearchDirectoryAsync(string tenantNId, string actorUserNId, string keyword, string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        var normalized = keyword.Trim();
        var users = SubjectMappings()
            .Select(item => TryReadIdentity(item.SourceNId, item.ExternalTenantNId, item.Subject, item.Section))
            .Where(identity => identity is not null && string.Equals(identity.TenantNId, tenantNId, StringComparison.Ordinal) && !string.Equals(identity.UserNId, actorUserNId, StringComparison.Ordinal))
            .Select(identity => new DirectoryUser(identity!.UserNId, identity.DisplayName, "Active", identity.SecurityVersion))
            .Where(user => normalized.Length == 0 || user.DisplayName.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .Take(Math.Clamp(pageSize, 1, 50))
            .ToArray();
        return Task.FromResult(new DirectorySearchPage(users, null));
    }

    private IEnumerable<(string SourceNId, string ExternalTenantNId, string Subject, IConfigurationSection Section)> SubjectMappings()
    {
        foreach (var source in configuration.GetSection("EmbeddedCollaboration:Sources").GetChildren())
        {
            foreach (var subject in source.GetSection("SubjectMappings").GetChildren())
            {
                var externalTenant = subject["ExternalTenantNId"];
                if (!string.IsNullOrWhiteSpace(externalTenant) && IsActive(subject))
                    yield return (source.Key, externalTenant, subject.Key, subject);
            }
        }
    }

    private EmbeddedIdentity? TryReadIdentity(string sourceNId, string externalTenantNId, string subject, IConfigurationSection section)
    {
        var securityVersion = section["SecurityVersion"];
        var displayName = section["DisplayName"] ?? subject;
        var tenantNId = section["PlatformTenantNId"]
            ?? configuration[$"EmbeddedCollaboration:Sources:{sourceNId}:ExternalTenantMappings:{externalTenantNId}"];
        if (string.IsNullOrWhiteSpace(securityVersion) || string.IsNullOrWhiteSpace(tenantNId))
            return null;
        return new EmbeddedIdentity(tenantNId, ConfigurationEmbeddedSubjectIdentityMapper.UserNId(sourceNId, externalTenantNId, subject), $"configured:{subject}", securityVersion)
        {
            SourceNId = sourceNId,
            ExternalTenantNId = externalTenantNId,
            ExternalSubject = subject,
            DisplayName = displayName,
        };
    }

    private static bool IsActive(IConfigurationSection section)
    {
        var status = section["Status"];
        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase))
            return false;
        var enabled = section["Enabled"];
        return string.IsNullOrWhiteSpace(enabled) || bool.TryParse(enabled, out var value) && value;
    }
}

/// <summary>独立宿主使用固定协作权限目录；由独立宿主注册，不改变平台默认权限判断器。</summary>
public sealed class EmbeddedPermissionEvaluator : IPermissionEvaluator
{
    public Task<PermissionEvaluation> EvaluateAsync(string tenantNId, string userNId, string? sessionNId, int authVersion, string requiredPermissionNId, CancellationToken cancellationToken)
    {
        _ = tenantNId;
        _ = userNId;
        _ = cancellationToken;
        var allowed = authVersion > 0 && !string.IsNullOrWhiteSpace(sessionNId)
            && EmbeddedCollaborationPermissionCatalog.IsAllowed(requiredPermissionNId);
        return Task.FromResult(new PermissionEvaluation(allowed, allowed ? AuthorizationDenialReason.None : AuthorizationDenialReason.MissingPermission));
    }
}

/// <summary>
/// Permissions granted by the host after the MES current-user/session has been
/// verified. The MES adapter only needs people Search/Get; platform admin and
/// compliance permissions are deliberately outside this embedded minimum.
/// </summary>
public static class EmbeddedCollaborationPermissionCatalog
{
    public static readonly IReadOnlyCollection<string> Permissions =
    [
        CollaborationPermissions.MessagingRead,
        CollaborationPermissions.MessagingConversationStart,
        CollaborationPermissions.MessagingWrite,
        CollaborationPermissions.PresenceConnect,
        CollaborationPermissions.PresenceRead,
        CollaborationPermissions.PresenceWrite,
        CollaborationPermissions.RemoteAssistanceSessionShare,
        CollaborationPermissions.RemoteAssistanceSessionJoin,
        CollaborationPermissions.RemoteAssistanceVoiceCall,
    ];

    public static bool IsAllowed(string permission) => Permissions.Contains(permission, StringComparer.Ordinal);
}

/// <summary>聊天目录也必须走同一个外部能力边界，不能直接读 MES 表。</summary>
public sealed class EmbeddedCollaborationIdentityDirectory(IEmbeddedCollaborationAccessAdapter adapter) : ICollaborationIdentityDirectory
{
    public Task<DirectoryUser?> GetAsync(string tenantNId, string userNId, CancellationToken cancellationToken) => adapter.GetDirectoryUserAsync(tenantNId, userNId, cancellationToken);

    public Task<DirectorySearchPage> SearchAsync(string tenantNId, string actorUserNId, string keyword, string? cursor, int pageSize, CancellationToken cancellationToken) =>
        adapter.SearchDirectoryAsync(tenantNId, actorUserNId, keyword, cursor, pageSize, cancellationToken);
}
