using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using IndustrialPlatform.Infrastructure.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using SqlSugar;

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Adapters;

public sealed class ConfigurationEmbeddedSubjectIdentityMapper(IConfiguration configuration) : IEmbeddedSubjectIdentityMapper
{
    /// <summary>
    /// 演示配置根据“来源、外部租户、外部用户”三项生成稳定的平台用户标识。
    /// 真实 MES 适配器可以使用其他稳定标识，但必须保留这三项的绑定关系，
    /// 不能通过显示名称或邮箱匹配用户。
    /// </summary>
    public static string UserNId(string sourceNId, string externalTenantNId, string externalSubject) =>
        "ext_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{sourceNId}\0{externalTenantNId}\0{externalSubject}"))).ToLowerInvariant();

    public Task<EmbeddedIdentity?> MapAsync(EmbeddedIdentityAssertion assertion, CancellationToken cancellationToken)
    {
        var source = configuration.GetSection($"EmbeddedCollaboration:Sources:{assertion.SourceNId}");
        var expectedTenant = source[$"ExternalTenantMappings:{assertion.ExternalTenantNId}"];
        var subject = source.GetSection($"SubjectMappings:{assertion.ExternalSubject}");
        var configuredTenant = subject["ExternalTenantNId"];
        var configuredVersion = subject["SecurityVersion"];
        if (!IsActive(subject)
            || string.IsNullOrWhiteSpace(expectedTenant)
            || string.IsNullOrWhiteSpace(configuredTenant)
            || string.IsNullOrWhiteSpace(configuredVersion)
            || !string.Equals(configuredTenant, assertion.ExternalTenantNId, StringComparison.Ordinal)
            || !string.Equals(configuredVersion, assertion.SecurityVersion, StringComparison.Ordinal))
            return Task.FromResult<EmbeddedIdentity?>(null);

        var userNId = UserNId(assertion.SourceNId, assertion.ExternalTenantNId, assertion.ExternalSubject);
        return Task.FromResult<EmbeddedIdentity?>(new EmbeddedIdentity(expectedTenant, userNId, assertion.SourceSessionNId, assertion.SecurityVersion)
        {
            AccountNId = assertion.AccountNId,
            SourceNId = assertion.SourceNId,
            ExternalTenantNId = assertion.ExternalTenantNId,
            ExternalSubject = assertion.ExternalSubject,
            DisplayName = subject["DisplayName"] ?? assertion.DisplayName,
        });
    }

    public Task<bool> IsCurrentAsync(EmbeddedStoredSession session, CancellationToken cancellationToken)
    {
        var source = configuration.GetSection($"EmbeddedCollaboration:Sources:{session.Identity.SourceNId}");
        var expectedTenant = source[$"ExternalTenantMappings:{session.Identity.ExternalTenantNId}"];
        var subject = source.GetSection($"SubjectMappings:{session.Identity.ExternalSubject}");
        var configuredTenant = subject["ExternalTenantNId"];
        var configuredVersion = subject["SecurityVersion"];
        var userNId = "ext_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{session.Identity.SourceNId}\0{session.Identity.ExternalTenantNId}\0{session.Identity.ExternalSubject}"))).ToLowerInvariant();
        var current = IsActive(subject)
            && string.Equals(expectedTenant, session.Identity.TenantNId, StringComparison.Ordinal)
            && string.Equals(configuredTenant, session.Identity.ExternalTenantNId, StringComparison.Ordinal)
            && string.Equals(configuredVersion, session.Identity.SecurityVersion, StringComparison.Ordinal)
            && string.Equals(userNId, session.Identity.UserNId, StringComparison.Ordinal);

        // 配置键只是上游 cookie 的索引；SessionNId 才是已建立会话的标识。
        // 只按条目内显式 SessionNId 绑定，缺失、删除或轮换不匹配时统一拒绝。
        var sourceSession = configuration.GetSection("EmbeddedCollaboration:SourceSessions")
            .GetChildren()
            .FirstOrDefault(section =>
                string.Equals(section["SessionNId"], session.Identity.SessionNId, StringComparison.Ordinal)
                && IsActive(section)
                && string.Equals(section["SourceNId"], session.Identity.SourceNId, StringComparison.Ordinal)
                && string.Equals(section["ExternalTenantNId"], session.Identity.ExternalTenantNId, StringComparison.Ordinal)
                && string.Equals(section["ExternalSubject"], session.Identity.ExternalSubject, StringComparison.Ordinal)
                && string.Equals(section["SecurityVersion"], session.Identity.SecurityVersion, StringComparison.Ordinal));
        current = current
            && sourceSession is not null;

        return Task.FromResult(current);
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
