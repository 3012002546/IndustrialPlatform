using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Api.Tests;

/// <summary>
/// 生产 JwtBearer 配置回归:独立 SystemData 必须用配置的 RSA 公钥校验签名,
/// 不能因为 Gateway 转发而退化成仅校验 iss/aud/lifetime。
/// </summary>
public sealed class SystemDataAuthenticationOptionsTests
{
    [Fact]
    public void AddSystemDataAuthentication_EnablesIssuerSigningKeyValidation()
    {
        using var rsa = RSA.Create(2048);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "industrial-platform-identity",
                ["Jwt:Audience"] = "industrial-platform-api",
                ["Jwt:SigningKey"] = rsa.ExportSubjectPublicKeyInfoPem(),
            })
            .Build();
        var services = new ServiceCollection();

        services.AddSystemDataAuthentication(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.True(options.TokenValidationParameters.ValidateIssuerSigningKey);
        Assert.True(options.TokenValidationParameters.RequireSignedTokens);
        Assert.NotNull(options.TokenValidationParameters.IssuerSigningKey);
    }

    [Fact]
    public void AddSystemDataAuthentication_UsesIdentityJwksWhenPublicKeyIsNotInjected()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "industrial-platform-identity",
                ["Jwt:Audience"] = "industrial-platform-api",
                ["Jwt:SigningKey"] = "",
                ["Jwt:JwksUrl"] = "http://localhost:5041/.well-known/jwks.json",
                ["Jwt:RequireHttpsMetadata"] = "false",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddSystemDataAuthentication(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.NotNull(options.ConfigurationManager);
        Assert.Null(options.TokenValidationParameters.IssuerSigningKey);
        Assert.True(options.TokenValidationParameters.RequireSignedTokens);
    }
}
