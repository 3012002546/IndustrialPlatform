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

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Abstractions;

public interface IEmbeddedHandshakeStore
{
    Task<EmbeddedStoredChallenge?> GetChallengeAsync(string nonce, CancellationToken cancellationToken);
    Task SaveChallengeAsync(EmbeddedStoredChallenge challenge, CancellationToken cancellationToken);
    Task<EmbeddedChallengeConsumeResult> ConsumeChallengeAsync(string nonce, string browserBindingHash, string jti, DateTimeOffset now, DateTimeOffset assertionExpiresOn, CancellationToken cancellationToken);
    Task<EmbeddedStoredSession?> GetSessionAsync(string sessionTokenHash, string browserBindingHash, CancellationToken cancellationToken);
    Task<EmbeddedStoredSession?> TouchSessionAsync(string sessionTokenHash, string browserBindingHash, DateTimeOffset now, DateTimeOffset expiresOn, CancellationToken cancellationToken);
    Task<EmbeddedStoredSession> CreateSessionAsync(EmbeddedIdentity identity, string browserBindingHash, string sessionTokenHash, DateTimeOffset expiresOn, CancellationToken cancellationToken);
    Task RevokeSessionAsync(string sessionTokenHash, string browserBindingHash, DateTimeOffset revokedOn, CancellationToken cancellationToken);
}
