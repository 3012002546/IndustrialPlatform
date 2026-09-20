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

public interface IEmbeddedIdentityAssertionIssuer
{
    string Issue(EmbeddedSourcePrincipal source, string nonce, DateTimeOffset issuedOn, TimeSpan lifetime);
    EmbeddedIdentityAssertion Validate(string assertion, DateTimeOffset now);
}
