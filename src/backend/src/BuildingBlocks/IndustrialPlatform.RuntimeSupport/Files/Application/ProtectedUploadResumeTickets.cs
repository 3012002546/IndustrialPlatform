using System.Security.Cryptography;
using System.Text.Json;
using IndustrialPlatform.SystemData.Application.Files;
using Microsoft.AspNetCore.DataProtection;

namespace IndustrialPlatform.RuntimeSupport.Files;

/// <summary>以宿主持久DataProtection密钥签发；不使用源码中的开发HMAC秘密。</summary>
public sealed class ProtectedUploadResumeTickets(IDataProtectionProvider protection) : IUploadResumeTicketService
{
    private readonly IDataProtector _protector = protection.CreateProtector("RuntimeSupport.UploadResume.v1");
    public string Issue(string tenantNId, string sessionNId, string userNId, int writerEpoch, long offset, DateTimeOffset expiresOn) =>
        _protector.Protect(JsonSerializer.Serialize(new Claim(tenantNId, sessionNId, userNId, writerEpoch, offset, expiresOn)));
    public bool Validate(string ticket, string tenantNId, string sessionNId, string userNId, int writerEpoch, long offset, DateTimeOffset now)
    {
        try { var claim = JsonSerializer.Deserialize<Claim>(_protector.Unprotect(ticket)); return claim is not null && claim == new Claim(tenantNId, sessionNId, userNId, writerEpoch, offset, claim.ExpiresOn) && claim.ExpiresOn > now; }
        catch (Exception exception) when (exception is CryptographicException or JsonException or ArgumentException) { return false; }
    }
    private sealed record Claim(string Tenant, string Session, string User, int Epoch, long Offset, DateTimeOffset ExpiresOn);
}
