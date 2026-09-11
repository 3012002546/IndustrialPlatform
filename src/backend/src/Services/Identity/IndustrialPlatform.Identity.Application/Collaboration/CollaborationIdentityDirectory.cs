using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Contracts.Collaboration;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Security;

namespace IndustrialPlatform.Identity.Application.Collaboration;

/// <summary>只投影 PF05 必需字段的 Identity 目录。</summary>
public sealed class CollaborationIdentityDirectory : ICollaborationIdentityDirectory
{
    private readonly IUserManagementService _users;
    private readonly IAuthenticationStore _authentication;
    private readonly CollaborationDirectoryCursorCodec _cursor;

    public CollaborationIdentityDirectory(IUserManagementService users, IAuthenticationStore authentication, CollaborationDirectoryCursorCodec cursor)
    {
        _users = users;
        _authentication = authentication;
        _cursor = cursor;
    }

    public async Task<CollaborationDirectoryPage> SearchAsync(TrustedCollaborationCall caller, CollaborationUserSearchRequest request, CancellationToken cancellationToken)
    {
        await EnsureCallerAsync(caller, cancellationToken);
        var keyword = request.Keyword?.Trim() ?? string.Empty;
        if (keyword.Length is < 1 or > 100 || request.Limit is < 1 or > 50)
            throw new ArgumentException("Invalid collaboration directory query.");
        var pageIndex = _cursor.Decode(request.Cursor, caller.TenantNId, caller.ActorUserNId, keyword, request.Limit);
        var page = await _users.ListAsync(caller.TenantNId,
            new UserListFilter(caller.TenantNId, null, null, null, UserStatus.Active, pageIndex, request.Limit, Keyword: keyword), cancellationToken);
        var items = page.Items.Where(user => !string.Equals(user.UserNId, caller.ActorUserNId, StringComparison.Ordinal))
            .Select(user => new CollaborationDirectoryUser(user.UserNId, $"{user.Name} ({user.LoginName.Trim().ToUpperInvariant()})", true)).ToList();
        var next = pageIndex * request.Limit < page.Total && items.Count > 0
            ? _cursor.Encode(pageIndex + 1, caller.TenantNId, caller.ActorUserNId, keyword, request.Limit, items[^1].UserNId)
            : null;
        return new CollaborationDirectoryPage(items, next);
    }

    public async Task<CollaborationDirectoryUserDetail> GetAsync(TrustedCollaborationCall caller, string userNId, CancellationToken cancellationToken)
    {
        await EnsureCallerAsync(caller, cancellationToken);
        if (string.IsNullOrWhiteSpace(userNId) || userNId.Length > 64)
            throw new ArgumentException("Invalid user NId.");
        var user = await _authentication.FindByNIdAsync(userNId.Trim(), cancellationToken);
        if (user is null || !string.Equals(user.User.TenantNId, caller.TenantNId, StringComparison.Ordinal)
            || user.User.IsDeleted)
            throw new KeyNotFoundException("User not found.");
        var now = DateTimeOffset.UtcNow;
        return new CollaborationDirectoryUserDetail(
            user.User.NId,
            $"{user.User.Name} ({user.User.NormalizedLoginName})",
            user.User.Status.ToString(),
            user.User.AuthVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            now,
            now.AddSeconds(60));
    }

    private async Task EnsureCallerAsync(TrustedCollaborationCall caller, CancellationToken cancellationToken)
    {
        var actor = await _authentication.FindByNIdAsync(caller.ActorUserNId, cancellationToken);
        if (actor is null
            || !string.Equals(actor.User.TenantNId, caller.TenantNId, StringComparison.Ordinal)
            || actor.User.IsDeleted
            || actor.User.Status != UserStatus.Active
            || !string.Equals(actor.User.AuthVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), caller.ActorSecurityVersion, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The trusted actor is no longer active.");
    }

}
