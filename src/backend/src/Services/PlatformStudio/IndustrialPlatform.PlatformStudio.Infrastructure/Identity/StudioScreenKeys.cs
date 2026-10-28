using System.Security.Cryptography;
using IndustrialPlatform.PlatformStudio.Application.Dashboard;
using IndustrialPlatform.PlatformStudio.Domain;
using Microsoft.AspNetCore.DataProtection;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Identity;

/// <summary>复用宿主持久密钥环；凭据绑定原链接和摘要，沿用其权限、期限及撤销状态。</summary>
public sealed class StudioScreenKeys(IDataProtectionProvider protection) : IStudioScreenKeys
{
    private readonly IDataProtector _protector = protection.CreateProtector("IndustrialPlatform.Studio.ScreenLink.v1");
    public string Protect(StudioScreenLink link) => "v1." + _protector.Protect(Payload(link));
    public bool Matches(StudioScreenLink link, string key)
    {
        if (!key.StartsWith("v1.", StringComparison.Ordinal)) return false;
        try { return _protector.Unprotect(key[3..]) == Payload(link); }
        catch (CryptographicException) { return false; }
    }
    private static string Payload(StudioScreenLink link) => $"{link.Id:D}:{link.TenantNId}:{link.TokenHash}";
}
