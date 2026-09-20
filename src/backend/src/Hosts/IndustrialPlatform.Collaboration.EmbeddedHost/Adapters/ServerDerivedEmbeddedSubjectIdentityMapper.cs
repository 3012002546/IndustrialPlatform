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
/// Identity projection for the legacy assertion handshake. Standalone creates
/// its identity directly from a user-list match and does not call MapAsync.
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
