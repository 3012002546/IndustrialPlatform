using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Infrastructure;
using IndustrialPlatform.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Security_StepUpBindingTests
{
    [Fact]
    public void Compute_ReturnsSignedJwtBindingWithBoundRequestClaims()
    {
        using var rsa = RSA.Create(2048);
        var binding = StepUpBinding.Create(
            rsa,
            "kid-1",
            "collaboration",
            "identity.pf05",
            "tenant-1",
            "user-1",
            "session-1",
            "7",
            "compliance.view",
            "request-1",
            new string('a', 64),
            new string('b', 64),
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(2));

        var token = new JwtSecurityTokenHandler().ReadJwtToken(binding);

        Assert.Equal("industrial-step-up-binding+jwt", token.Header.Typ);
        Assert.Equal("RS256", token.Header.Alg);
        Assert.Equal("tenant-1", token.Claims.Single(item => item.Type == "tenant_id").Value);
        Assert.Equal("user-1", token.Claims.Single(item => item.Type == "actor_user_n_id").Value);
        Assert.Equal("session-1", token.Claims.Single(item => item.Type == "actor_session_n_id").Value);
        Assert.Equal("request-1", token.Claims.Single(item => item.Type == "request_n_id").Value);
        Assert.Equal(new string('b', 64), token.Claims.Single(item => item.Type == "request_hash").Value);
    }

    [Fact]
    public void Create_then_validate_accepts_the_bound_signer_and_rejects_wrong_or_expired_bindings()
    {
        using var signer = RSA.Create(2048);
        using var wrongSigner = RSA.Create(2048);
        var now = DateTimeOffset.UtcNow;
        var binding = CreateBinding(signer, now);

        var principal = StepUpBinding.Validate(binding, signer, "kid-1", "collaboration", "identity.pf05", now, out var token);

        Assert.Equal("collaboration", principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
        Assert.NotEqual(DateTime.MinValue, token.IssuedAt);
        Assert.ThrowsAny<SecurityTokenException>(() =>
            StepUpBinding.Validate(binding, wrongSigner, "kid-1", "collaboration", "identity.pf05", now, out _));
        Assert.ThrowsAny<SecurityTokenException>(() =>
            StepUpBinding.Validate(CreateBinding(signer, now.AddMinutes(-3)), signer, "kid-1", "collaboration", "identity.pf05", now, out _));
    }

    [Fact]
    public void Reimported_public_keys_validate_after_each_key_is_disposed()
    {
        using var signer = RSA.Create(2048);
        var publicKeyPem = signer.ExportSubjectPublicKeyInfoPem();
        var binding = CreateBinding(signer, DateTimeOffset.UtcNow);

        for (var index = 0; index < 4; index++)
        {
            using var publicKey = RSA.Create();
            publicKey.ImportFromPem(publicKeyPem);
            var principal = StepUpBinding.Validate(binding, publicKey, "kid-1", "collaboration", "identity.pf05", DateTimeOffset.UtcNow, out _);
            Assert.Equal("collaboration", principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
        }
    }

    private static string CreateBinding(RSA signer, DateTimeOffset issuedOn) => StepUpBinding.Create(
        signer,
        "kid-1",
        "collaboration",
        "identity.pf05",
        "tenant-1",
        "user-1",
        "session-1",
        "7",
        "compliance.view",
        "request-1",
        new string('a', 64),
        new string('b', 64),
        issuedOn,
        TimeSpan.FromMinutes(2));

    [Fact]
    public void Missing_dedicated_signing_key_fails_closed_instead_of_reusing_identity_key()
    {
        var issuer = new StepUpBindingIssuer(new ConfigurationBuilder().Build());

        var exception = Assert.Throws<CollaborationException>(() => issuer.Issue(
            "tenant-1",
            "user-1",
            "session-1",
            "7",
            "compliance.view",
            "request-1",
            new string('a', 64),
            new string('b', 64),
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(2)));

        Assert.Equal(503, exception.StatusCode);
        Assert.Equal("COLLAB_STEP_UP_BINDING_UNAVAILABLE", exception.Code);
    }
}
