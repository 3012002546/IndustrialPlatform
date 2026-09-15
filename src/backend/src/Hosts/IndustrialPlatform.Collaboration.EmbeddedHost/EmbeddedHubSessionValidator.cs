using System.Security.Claims;
using IndustrialPlatform.Collaboration.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace IndustrialPlatform.Collaboration.EmbeddedHost;

/// <summary>
/// Revalidates an embedded page session for every SignalR connection and hub
/// invocation. A WebSocket must not outlive the HttpOnly session or the MES
/// source session that authorized it.
/// </summary>
public sealed class EmbeddedHubSessionValidator(
    IEmbeddedHandshakeStore store,
    EmbeddedHostHandshakeOptions options,
    IEmbeddedSourcePrincipalResolver sourceResolver) : ICollaborationHubSessionValidator
{
    public async Task<CollaborationHubSessionValidation> ValidateAsync(HubCallerContext context, CancellationToken cancellationToken)
    {
        if (!string.Equals(context.User?.FindFirstValue("embedded_session"), "true", StringComparison.Ordinal))
            return new CollaborationHubSessionValidation(true);

        var http = context.GetHttpContext();
        if (http is null
            || !EmbeddedHostHandshakeService.TryReadSessionCredential(
                http,
                options.CookieName,
                options.BrowserBindingCookieName,
                out var token,
                out var binding))
            return new CollaborationHubSessionValidation(false);

        var session = await store.GetSessionAsync(
            EmbeddedSessionToken.Hash(token),
            EmbeddedSessionBinding.Hash(binding),
            cancellationToken);
        if (session is null || session.RevokedOn is not null || session.ExpiresOn <= DateTimeOffset.UtcNow)
            return new CollaborationHubSessionValidation(false);

        try
        {
            var requestedAccount = EmbeddedAccountQuery.Read(http);
            if (requestedAccount is not null
                && !string.Equals(requestedAccount, session.Identity.AccountNId, StringComparison.Ordinal))
                return new CollaborationHubSessionValidation(false);
        }
        catch (EmbeddedHandshakeException)
        {
            return new CollaborationHubSessionValidation(false);
        }

        var source = await sourceResolver.ResolveAsync(http, session.Identity.AccountNId, cancellationToken);
        var current = source is not null
            && string.Equals(source.SourceNId, session.Identity.SourceNId, StringComparison.Ordinal)
            && string.Equals(source.ExternalTenantNId, session.Identity.ExternalTenantNId, StringComparison.Ordinal)
            && string.Equals(source.ExternalSubject, session.Identity.ExternalSubject, StringComparison.Ordinal)
            && string.Equals(source.SessionNId, session.Identity.SessionNId, StringComparison.Ordinal)
            && string.Equals(source.SecurityVersion, session.Identity.SecurityVersion, StringComparison.Ordinal)
            && (source.AccountNId is null || string.Equals(source.AccountNId, session.Identity.AccountNId, StringComparison.Ordinal));
        return new CollaborationHubSessionValidation(current, current ? session.ExpiresOn : null);
    }
}
