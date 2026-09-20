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

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Services;

public static class EmbeddedSessionEpochFilter
{
    public static bool Accept(EmbeddedStoredSession currentSession, long eventEpoch, EmbeddedIdentity eventIdentity)
    {
        if (currentSession.RevokedOn is not null || currentSession.ExpiresOn <= DateTimeOffset.UtcNow || currentSession.Epoch != eventEpoch)
            return false;

        return string.Equals(currentSession.Identity.SourceNId, eventIdentity.SourceNId, StringComparison.Ordinal)
            && string.Equals(currentSession.Identity.ExternalTenantNId, eventIdentity.ExternalTenantNId, StringComparison.Ordinal)
            && string.Equals(currentSession.Identity.ExternalSubject, eventIdentity.ExternalSubject, StringComparison.Ordinal)
            && string.Equals(currentSession.Identity.TenantNId, eventIdentity.TenantNId, StringComparison.Ordinal)
            && string.Equals(currentSession.Identity.UserNId, eventIdentity.UserNId, StringComparison.Ordinal)
            && string.Equals(currentSession.Identity.SessionNId, eventIdentity.SessionNId, StringComparison.Ordinal)
            && string.Equals(currentSession.Identity.SecurityVersion, eventIdentity.SecurityVersion, StringComparison.Ordinal);
    }
}
