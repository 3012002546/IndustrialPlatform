using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Querying.Descriptors;

namespace IndustrialPlatform.Collaboration.Infrastructure;

public sealed class InProcessIdentityDirectory : ICollaborationIdentityDirectory
{
    private readonly IUserManagementService _users;
    private readonly IAuthenticationStore _authentication;
    private readonly PageCursorCodec _cursor;

    public InProcessIdentityDirectory(IUserManagementService users, IAuthenticationStore authentication, PageCursorCodec cursor)
    {
        _users = users;
        _authentication = authentication;
        _cursor = cursor;
    }

    public async Task<DirectoryUser?> GetAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
    {
        var user = await _authentication.FindByNIdAsync(userNId, cancellationToken);
        if (user is null || user.User.IsDeleted || !string.Equals(user.User.TenantNId, tenantNId, StringComparison.Ordinal))
            return null;
        return new DirectoryUser(user.User.NId, $"{user.User.Name} ({user.User.NormalizedLoginName})", user.User.Status.ToString(), user.User.AuthVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public async Task<IReadOnlyDictionary<string, string>> GetDisplayNamesAsync(string tenantNId, IReadOnlyCollection<string> userNIds, CancellationToken cancellationToken)
    {
        if (userNIds.Count == 0) return new Dictionary<string, string>();
        var users = await _users.QueryUsersAsync(tenantNId,
            new QueryDescriptor([new QueryFilter("userNId", QueryOperator.In, userNIds.ToArray())], [], [], 1, userNIds.Count), cancellationToken);
        return users.Items.ToDictionary(user => user.UserNId, user => $"{user.Name} ({user.LoginName.Trim().ToUpperInvariant()})", StringComparer.Ordinal);
    }

    public async Task<DirectorySearchPage> SearchAsync(string tenantNId, string actorUserNId, string keyword, string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        var binding = $"directory:{tenantNId}:{actorUserNId}:{keyword}:{pageSize}";
        var page = _cursor.Decode(cursor, binding);
        var result = await _users.ListAsync(tenantNId, new UserListFilter(tenantNId, null, null, null, UserStatus.Active, page, pageSize, Keyword: keyword), cancellationToken);
        var items = new List<DirectoryUser>(result.Items.Count);
        foreach (var item in result.Items)
        {
            if (string.Equals(item.UserNId, actorUserNId, StringComparison.Ordinal))
                continue;
            var account = await _authentication.FindByNIdAsync(item.UserNId, cancellationToken);
            if (account is null || account.User.IsDeleted || account.User.Status != UserStatus.Active || !string.Equals(account.User.TenantNId, tenantNId, StringComparison.Ordinal))
                continue;
            items.Add(new DirectoryUser(item.UserNId, $"{item.Name} ({account.User.NormalizedLoginName})", item.Status, account.User.AuthVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        return new DirectorySearchPage(items, result.Items.Count == pageSize ? _cursor.Encode(page + 1, binding) : null);
    }
}
