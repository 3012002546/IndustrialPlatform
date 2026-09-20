using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Identity.Application.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Adapters;

/// <summary>
/// 演示人员目录。Search/Get 共用 LoadMesUsersAsync；正式 MES 只替换该方法的
/// 获取逻辑并在独立宿主注册此适配器，平台目录不受影响。
/// </summary>
public sealed class ConfigurationEmbeddedCollaborationAccessAdapter(IConfiguration configuration)
    : IEmbeddedCollaborationAccessAdapter
{
    public async Task<DirectoryUser?> GetDirectoryUserAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
    {
        var users = await LoadMesUsersAsync(cancellationToken);
        return users.FirstOrDefault(user => string.Equals(user.TenantNId, tenantNId, StringComparison.Ordinal)
            && string.Equals(user.User.UserNId, userNId, StringComparison.Ordinal))?.User;
    }

    public async Task<DirectorySearchPage> SearchDirectoryAsync(string tenantNId, string actorUserNId, string keyword, string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        var normalized = keyword.Trim();
        var users = (await LoadMesUsersAsync(cancellationToken))
            .Where(user => string.Equals(user.TenantNId, tenantNId, StringComparison.Ordinal)
                && !string.Equals(user.User.UserNId, actorUserNId, StringComparison.Ordinal)
                && (normalized.Length == 0 || user.User.DisplayName.Contains(normalized, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(user => user.User.UserNId, StringComparer.Ordinal)
            .ToArray();
        var offset = int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed : 0;
        var size = Math.Clamp(pageSize, 1, 50);
        var page = users.Skip(offset).Take(size).Select(user => user.User).ToArray();
        var nextCursor = offset + page.Length < users.Length ? (offset + page.Length).ToString(CultureInfo.InvariantCulture) : null;
        return new DirectorySearchPage(page, nextCursor);
    }

    /// <summary>
    /// MES 人员获取的唯一替换点。当前从演示配置组装人员；正式接线时将此方法体
    /// 换成 MES 用户接口调用，并保留稳定主体、租户、状态及安全版本的映射。
    /// 当前登录用户仍由 IEmbeddedCurrentUserAdapter 单独核验，不能从此列表推断。
    /// </summary>
    public Task<IReadOnlyList<DirectoryUserWithTenant>> LoadMesUsersAsync(CancellationToken cancellationToken)
    {
        var users = new List<DirectoryUserWithTenant>();
        foreach (var source in configuration.GetSection("EmbeddedCollaboration:Sources").GetChildren())
        {
            foreach (var subject in source.GetSection("SubjectMappings").GetChildren())
            {
                var externalTenant = subject["ExternalTenantNId"];
                var tenant = subject["PlatformTenantNId"]
                    ?? source[$"ExternalTenantMappings:{externalTenant}"];
                var securityVersion = subject["SecurityVersion"];
                if (string.IsNullOrWhiteSpace(externalTenant) || string.IsNullOrWhiteSpace(tenant)
                    || string.IsNullOrWhiteSpace(securityVersion) || !IsEnabled(subject))
                    continue;
                var userNId = ConfigurationEmbeddedSubjectIdentityMapper.UserNId(source.Key, externalTenant, subject.Key);
                var status = subject["Status"];
                users.Add(new DirectoryUserWithTenant(tenant,
                    new DirectoryUser(userNId, subject["DisplayName"] ?? subject.Key,
                        string.IsNullOrWhiteSpace(status) || string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)
                            ? "Active" : "Inactive",
                        securityVersion)));
            }
        }
        return Task.FromResult<IReadOnlyList<DirectoryUserWithTenant>>(users);
    }

    private static bool IsEnabled(IConfigurationSection section)
    {
        var enabled = section["Enabled"];
        return string.IsNullOrWhiteSpace(enabled) || bool.TryParse(enabled, out var value) && value;
    }
}
