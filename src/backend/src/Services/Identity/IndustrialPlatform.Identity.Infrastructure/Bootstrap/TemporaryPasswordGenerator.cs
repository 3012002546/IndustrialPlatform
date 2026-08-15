using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Identity.Infrastructure.Security;

namespace IndustrialPlatform.Identity.Infrastructure.Bootstrap;

/// <summary>
/// 安全随机临时密码生成端口实现(§29A.4):基于密码学安全 RNG,每次随机。
/// </summary>
public sealed class TemporaryPasswordGenerator : ITemporaryPasswordGenerator
{
    /// <inheritdoc />
    public string Generate(int minLength, string? loginName = null, string? nId = null) =>
        SecureRandomPasswordGenerator.Generate(minLength, loginName, nId);
}
