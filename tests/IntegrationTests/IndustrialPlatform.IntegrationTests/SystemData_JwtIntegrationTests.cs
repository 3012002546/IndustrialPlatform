extern alias SystemDataApi;

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Infrastructure.Security;
using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Contracts.ControlPlane;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.IntegrationTests.SystemData;

/// <summary>
/// 独立 SystemData 的真实 JwtBearer 协议回归:使用 Identity 的实际 AccessTokenFactory
/// 签发 RS256 令牌,正确公钥可命中 runtime,错误签名必须 401。
/// </summary>
public sealed class JwtIntegrationTests
{
    private const string Issuer = "industrial-platform-identity";
    private const string Audience = "industrial-platform-api";

    [Fact]
    public async Task IdentitySignedToken_ReachesRuntime_AndWrongSignatureIsRejected()
    {
        using var identityRsa = RSA.Create(2048);
        using var invalidRsa = RSA.Create(2048);
        using var identityKeys = new RsaSigningKeyProvider(
            Options.Create(new JwtOptions
            {
                Issuer = Issuer,
                Audience = Audience,
                KeyId = "identity-test",
                SigningKey = identityRsa.ExportPkcs8PrivateKeyPem(),
            }),
            NullLogger<RsaSigningKeyProvider>.Instance);
        using var invalidKeys = new RsaSigningKeyProvider(
            Options.Create(new JwtOptions
            {
                Issuer = Issuer,
                Audience = Audience,
                KeyId = "invalid-test",
                SigningKey = invalidRsa.ExportPkcs8PrivateKeyPem(),
            }),
            NullLogger<RsaSigningKeyProvider>.Instance);
        var tokenOptions = Options.Create(new JwtOptions
        {
            Issuer = Issuer,
            Audience = Audience,
            KeyId = "identity-test",
            AccessTokenMinutes = 30,
        });
        var invalidTokenOptions = Options.Create(new JwtOptions
        {
            Issuer = Issuer,
            Audience = Audience,
            KeyId = "invalid-test",
            AccessTokenMinutes = 30,
        });
        var identityTokenFactory = new AccessTokenFactory(identityKeys, tokenOptions);
        var invalidTokenFactory = new AccessTokenFactory(invalidKeys, invalidTokenOptions);
        var issuedAt = DateTimeOffset.UtcNow;
        var descriptor = new AccessTokenDescriptor(
            "user-1",
            "tester",
            "tenant-a",
            [],
            "sid-1",
            1,
            issuedAt);
        var databasePath = Path.Combine(Path.GetTempPath(), $"industrial-platform-systemdata-jwt-{Guid.NewGuid():N}.db");

        try
        {
            using var factory = new WebApplicationFactory<SystemDataApi::Program>()
                .WithWebHostBuilder(builder => builder
                    .UseEnvironment("Testing")
                    .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["IndustrialPlatform:DevelopmentInfrastructureMode"] = "Sqlite",
                        ["SqlSugar:ConnectionString"] = $"Data Source={databasePath}",
                        ["SqlSugar:DbType"] = "Sqlite",
                        ["Redis:ConnectionString"] = "localhost:6379,abortConnect=false",
                        ["Jwt:Issuer"] = Issuer,
                        ["Jwt:Audience"] = Audience,
                        ["Jwt:SigningKey"] = identityRsa.ExportSubjectPublicKeyInfoPem(),
                        ["Jwt:JwksUrl"] = "",
                    }))
                    .ConfigureTestServices(services =>
                    {
                        services.RemoveAll<IFeatureControlService>();
                        services.AddSingleton<IFeatureControlService>(new EmptyFeatureControlService());
                    }));
            using var client = factory.CreateClient();

            using var valid = await SendAsync(client, identityTokenFactory.Create(descriptor).Token);
            using var invalid = await SendAsync(client, invalidTokenFactory.Create(descriptor).Token);

            Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        }
        finally
        {
            try
            {
                File.Delete(databasePath);
            }
            catch (IOException)
            {
                // SqlSugar connection pools may release the temporary file shortly after host disposal.
            }
        }
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/runtime/features");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    private sealed class EmptyFeatureControlService : IFeatureControlService
    {
        public Task<FeatureDefinitionResponse> RegisterAsync(string tenantNId, RegisterFeatureDefinitionRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyCollection<FeatureDefinitionResponse>> ListAsync(string tenantNId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<FeatureDefinitionResponse>>([]);

        public Task<FeatureDefinitionResponse> SetOverrideAsync(string tenantNId, string featureNId, SetFeatureOverrideRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<FeatureRuntimeResponse> RuntimeAsync(string tenantNId, CancellationToken cancellationToken)
            => Task.FromResult(new FeatureRuntimeResponse());
    }
}
