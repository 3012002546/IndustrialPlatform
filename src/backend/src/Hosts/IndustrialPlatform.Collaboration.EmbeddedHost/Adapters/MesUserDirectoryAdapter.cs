using System.Globalization;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Adapters;

/// <summary>独立协作的 MES 人员目录；Search/Get 共用同一个用户列表获取方法。</summary>
public sealed class MesUserDirectoryAdapter : IEmbeddedCollaborationAccessAdapter
{
    public Task<IReadOnlyList<MesUser>> LoadMesUsersAsync(CancellationToken cancellationToken)
    {
        // 临时人员数据。接入 MES 时只需将此方法体替换为用户列表接口调用。
        IReadOnlyList<MesUser> users =
        [
            new("operator-1", "操作员 1"),
            new("operator-2", "操作员 2"),
            new("operator-3", "操作员 3"),
        ];
        return Task.FromResult(users);
    }

    public async Task<DirectoryUser?> GetDirectoryUserAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
    {
        _ = tenantNId;
        var user = (await LoadMesUsersAsync(cancellationToken))
            .FirstOrDefault(item => string.Equals(item.UserId, userNId, StringComparison.Ordinal));
        return user is null ? null : ToDirectoryUser(user);
    }

    public async Task<DirectorySearchPage> SearchDirectoryAsync(
        string tenantNId, string actorUserNId, string keyword, string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        _ = tenantNId;
        var users = (await LoadMesUsersAsync(cancellationToken))
            .Where(item => !string.IsNullOrWhiteSpace(item.UserId)
                && !string.IsNullOrWhiteSpace(item.UserName)
                && !string.Equals(item.UserId, actorUserNId, StringComparison.Ordinal)
                && (string.IsNullOrWhiteSpace(keyword)
                    || item.UserId.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || item.UserName.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item.UserId, StringComparer.Ordinal)
            .ToArray();
        var offset = int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0 ? parsed : 0;
        var page = users.Skip(offset).Take(Math.Clamp(pageSize, 1, 50)).Select(ToDirectoryUser).ToArray();
        var next = offset + page.Length < users.Length ? (offset + page.Length).ToString(CultureInfo.InvariantCulture) : null;
        return new DirectorySearchPage(page, next);
    }

    private static DirectoryUser ToDirectoryUser(MesUser user) => new(user.UserId, user.UserName, "Active", "1");
}
