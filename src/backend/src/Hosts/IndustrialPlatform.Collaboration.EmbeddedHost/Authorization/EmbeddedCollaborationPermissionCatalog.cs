using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Identity.Application.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Authorization;

/// <summary>
/// Fixed collaboration capabilities granted by the independent host after it
/// establishes a page session. Standalone only requires the MES user list;
/// platform administration permissions are outside this host.
/// </summary>
public static class EmbeddedCollaborationPermissionCatalog
{
    public static readonly IReadOnlyCollection<string> Permissions =
    [
        CollaborationPermissions.MessagingRead,
        CollaborationPermissions.MessagingReadCursorUpdate,
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
