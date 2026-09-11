using System.Security.Cryptography;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Security;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.Infrastructure;

/// <summary>
/// Signs preparation bindings with the Collaboration service key. Missing or invalid
/// signing configuration fails closed instead of falling back to a browser-computable hash.
/// </summary>
public sealed class StepUpBindingIssuer : IStepUpBindingIssuer
{
    private readonly IConfiguration _configuration;

    public StepUpBindingIssuer(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string Issue(
        string tenantNId,
        string actorUserNId,
        string actorSessionNId,
        string actorSecurityVersion,
        string action,
        string requestNId,
        string scopeChecksum,
        string requestHash,
        DateTimeOffset issuedOn,
        TimeSpan lifetime)
    {
        var section = _configuration.GetSection("TrustedServiceCalls:Signers:collaboration");
        var privateKeyPath = section["PrivateKeyPath"];
        var privateKeyPem = section["PrivateKey"];
        var keyId = section["KeyId"] ?? "pf05-binding-v1";
        var issuer = section["Issuer"] ?? CollaborationServiceConstants.ServiceKey;
        var audience = section["Audience"] ?? CollaborationServiceConstants.InternalIdentityAudience;

        if (string.IsNullOrWhiteSpace(privateKeyPath) && string.IsNullOrWhiteSpace(privateKeyPem))
            throw Unavailable("Collaboration step-up binding 专用私钥未配置。");

        using var rsa = RSA.Create();
        try
        {
            if (!string.IsNullOrWhiteSpace(privateKeyPath))
                rsa.ImportFromPem(File.ReadAllText(privateKeyPath));
            else
                rsa.ImportFromPem(privateKeyPem!);
        }
        catch (Exception exception) when (exception is IOException or CryptographicException or ArgumentException)
        {
            throw Unavailable("Collaboration step-up binding 私钥无效。", exception);
        }

        return StepUpBinding.Create(
            rsa,
            keyId,
            issuer,
            audience,
            tenantNId,
            actorUserNId,
            actorSessionNId,
            actorSecurityVersion,
            action,
            requestNId,
            scopeChecksum,
            requestHash,
            issuedOn,
            lifetime);
    }

    private static CollaborationException Unavailable(string message, Exception? inner = null) =>
        new(503, "COLLAB_STEP_UP_BINDING_UNAVAILABLE", message);
}
