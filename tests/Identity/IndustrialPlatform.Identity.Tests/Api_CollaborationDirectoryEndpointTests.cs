using System.Net;
using System.Net.Http.Headers;
using System.Globalization;
using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Contracts.Collaboration;
using IndustrialPlatform.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace IndustrialPlatform.Identity.Api.Tests;

public sealed class CollaborationDirectoryEndpointTests
{
    [Fact]
    public async Task Search_RejectsAnOrdinaryUserBearer()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<IAccessTokenFactory>().Create(new AccessTokenDescriptor(
            "user-a", "user-a", "development", [], "SES-directory", 1, DateTimeOffset.UtcNow)).Token;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/internal/pf05/identity/users?keyword=lin");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Search_AcceptsMultipleSignerIssuedTrustedAssertionsThenFailsClosedWhenDirectoryIsUnavailable()
    {
        using var key = RSA.Create(2048);
        var keyPath = Path.Combine(Path.GetTempPath(), $"pf05-{Guid.NewGuid():N}.pem");
        await File.WriteAllTextAsync(keyPath, key.ExportRSAPublicKeyPem());

        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TrustedServiceCalls:Callers:collaboration:Issuer"] = "https://collaboration.test",
                    ["TrustedServiceCalls:Callers:collaboration:KeyId"] = "pf05-test-key",
                    ["TrustedServiceCalls:Callers:collaboration:PublicKeyPath"] = keyPath,
                    ["TrustedServiceCalls:Callers:collaboration:AllowedTenantNIds:0"] = "development",
                    ["Collaboration:ServiceIdentity:Issuer"] = "https://collaboration.test",
                    ["Collaboration:ServiceIdentity:KeyId"] = "pf05-test-key",
                    ["Collaboration:ServiceIdentity:PrivateKey"] = key.ExportPkcs8PrivateKeyPem(),
                }))
                .ConfigureServices(services =>
                {
                    services.RemoveAll<ICollaborationIdentityDirectory>();
                    services.AddSingleton<ICollaborationIdentityDirectory, UnavailableDirectory>();
                }));
            using var signer = new TrustedServiceCallSigner(factory.Services.GetRequiredService<IConfiguration>());
            Assert.Equal("https://collaboration.test", factory.Services.GetRequiredService<IConfiguration>()["TrustedServiceCalls:Callers:collaboration:Issuer"]);
            using var client = factory.CreateClient();
            for (var index = 0; index < 6; index++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/internal/pf05/identity/users?keyword=lin");
                var assertion = signer.Sign(
                    HttpMethod.Get,
                    "/internal/pf05/identity/users?keyword=lin",
                    ReadOnlyMemory<byte>.Empty,
                    "identity.pf05",
                    "development",
                    "user-a",
                    "SES-directory",
                    "1",
                    "directory.search",
                    $"102030405060708090a0b0c0d0e0f{index:000}");
                var parsed = new JwtSecurityTokenHandler().ReadJwtToken(assertion);
                Assert.NotEqual(DateTime.MinValue, parsed.IssuedAt);
                Assert.IsType<long>(parsed.Payload[JwtRegisteredClaimNames.Iat]);
                Assert.Contains(parsed.Claims, claim => claim.Type == "nbf");
                Assert.Contains(parsed.Claims, claim => claim.Type == "exp");
                request.Headers.Add("X-Industrial-Service-Assertion", assertion);

                using var response = await client.SendAsync(request);

                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            }
        }
        finally
        {
            File.Delete(keyPath);
        }
    }

    [Fact]
    public async Task Search_ReturnsTheDirectoryCursorAndRejectsAssertionReplay()
    {
        using var key = RSA.Create(2048);
        var keyPath = Path.Combine(Path.GetTempPath(), $"pf05-{Guid.NewGuid():N}.pem");
        await File.WriteAllTextAsync(keyPath, key.ExportRSAPublicKeyPem());

        try
        {
            var directory = new RecordingDirectory();
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TrustedServiceCalls:Callers:collaboration:Issuer"] = "https://collaboration.test",
                    ["TrustedServiceCalls:Callers:collaboration:KeyId"] = "pf05-test-key",
                    ["TrustedServiceCalls:Callers:collaboration:PublicKeyPath"] = keyPath,
                    ["TrustedServiceCalls:Callers:collaboration:AllowedTenantNIds:0"] = "development",
                }))
                .ConfigureServices(services =>
                {
                    services.RemoveAll<ICollaborationIdentityDirectory>();
                    services.AddSingleton<ICollaborationIdentityDirectory>(directory);
                }));
            using var client = factory.CreateClient();
            const string cursor = "identity-opaque-cursor-v2";
            var path = $"internal/pf05/identity/users?cursor={cursor}&keyword=lin&limit=20";
            var assertion = CreateAssertion(key, path, jti: "b1c2d3e4f5a6478899aabbccddeeff01");

            using var request = new HttpRequestMessage(HttpMethod.Get, "/internal/pf05/identity/users?keyword=lin&cursor=identity-opaque-cursor-v2&limit=20");
            request.Headers.Add("X-Industrial-Service-Assertion", assertion);
            using var response = await client.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {responseBody}");
            Assert.Equal(cursor, directory.LastRequest?.Cursor);
            Assert.Equal("user-a", directory.LastCaller?.ActorUserNId);
            using var payload = JsonDocument.Parse(responseBody);
            Assert.Equal("identity-opaque-cursor-next", payload.RootElement.GetProperty("data").GetProperty("nextCursor").GetString());

            using var replay = new HttpRequestMessage(HttpMethod.Get, "/internal/pf05/identity/users?keyword=lin&cursor=identity-opaque-cursor-v2&limit=20");
            replay.Headers.Add("X-Industrial-Service-Assertion", assertion);
            using var replayResponse = await client.SendAsync(replay);

            Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);
        }
        finally
        {
            File.Delete(keyPath);
        }
    }

    [Fact]
    public async Task Search_WithTamperedSignatureReturns401()
    {
        using var key = RSA.Create(2048);
        using var wrongKey = RSA.Create(2048);
        var keyPath = Path.Combine(Path.GetTempPath(), $"pf05-{Guid.NewGuid():N}.pem");
        await File.WriteAllTextAsync(keyPath, key.ExportRSAPublicKeyPem());

        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TrustedServiceCalls:Callers:collaboration:Issuer"] = "https://collaboration.test",
                    ["TrustedServiceCalls:Callers:collaboration:KeyId"] = "pf05-test-key",
                    ["TrustedServiceCalls:Callers:collaboration:PublicKeyPath"] = keyPath,
                    ["TrustedServiceCalls:Callers:collaboration:AllowedTenantNIds:0"] = "development",
                })));
            using var client = factory.CreateClient();
            var assertion = CreateAssertion(wrongKey);
            using var request = new HttpRequestMessage(HttpMethod.Get, "/internal/pf05/identity/users?keyword=lin");
            request.Headers.Add("X-Industrial-Service-Assertion", assertion);

            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            File.Delete(keyPath);
        }
    }

    private static string CreateAssertion(
        RSA key,
        string path = "internal/pf05/identity/users?keyword=lin",
        string jti = "a1b2c3d4e5f6478899aabbccddeeff00")
    {
        var now = DateTimeOffset.UtcNow;
        var token = new JwtSecurityToken(
            issuer: "https://collaboration.test",
            audience: "identity.pf05",
            claims:
            [
                new("sub", "collaboration"),
            new("jti", jti),
                new("iat", now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
                new("tenant_id", "development"),
                new("actor_user_n_id", "user-a"),
                new("actor_session_n_id", "SES-directory"),
                new("actor_security_version", "1"),
                new("action", "directory.search"),
                new("method", "GET"),
            new("path", path),
                new("body_sha256", Convert.ToHexString(SHA256.HashData([])).ToLowerInvariant()),
                new("request_n_id", "102030405060708090a0b0c0d0e0f000"),
            ],
            notBefore: now.UtcDateTime,
            expires: now.AddSeconds(30).UtcDateTime,
            signingCredentials: new SigningCredentials(new RsaSecurityKey(key) { KeyId = "pf05-test-key" }, SecurityAlgorithms.RsaSha256));
        token.Header["typ"] = "industrial-service-call+jwt";
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class UnavailableDirectory : ICollaborationIdentityDirectory
    {
        public Task<CollaborationDirectoryPage> SearchAsync(TrustedCollaborationCall caller, CollaborationUserSearchRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("directory unavailable");

        public Task<CollaborationDirectoryUserDetail> GetAsync(TrustedCollaborationCall caller, string userNId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("directory unavailable");
    }

    private sealed class RecordingDirectory : ICollaborationIdentityDirectory
    {
        public TrustedCollaborationCall? LastCaller { get; private set; }
        public CollaborationUserSearchRequest? LastRequest { get; private set; }

        public Task<CollaborationDirectoryPage> SearchAsync(TrustedCollaborationCall caller, CollaborationUserSearchRequest request, CancellationToken cancellationToken)
        {
            LastCaller = caller;
            LastRequest = request;
            return Task.FromResult(new CollaborationDirectoryPage(
                [new CollaborationDirectoryUser("user-b", "Bob", true)],
                "identity-opaque-cursor-next"));
        }

        public Task<CollaborationDirectoryUserDetail> GetAsync(TrustedCollaborationCall caller, string userNId, CancellationToken cancellationToken) =>
            Task.FromResult(new CollaborationDirectoryUserDetail(userNId, "User", "Active", "1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1)));
    }
}
