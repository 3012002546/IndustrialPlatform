using IndustrialPlatform.Identity.Domain.Passwords;

namespace IndustrialPlatform.Identity.Infrastructure.Passwords;

/// <summary>
/// 基于 BCrypt(工作因子 12)的密码哈希实现(§10.1)。
/// 哈希为自包含格式,校验不依赖额外盐存储;领域层只暴露哈希,禁止明文密码进出持久化、日志与事件。
/// </summary>
public sealed class BcryptPasswordHasher : IPasswordHasher
{
    /// <summary>BCrypt 工作因子,提升安全参数后旧哈希经 <see cref="NeedsRehash"/> 触发重哈希。</summary>
    public const int WorkFactor = 12;

    /// <inheritdoc/>
    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
    }

    /// <inheritdoc/>
    public bool Verify(string passwordHash, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentNullException.ThrowIfNull(password);
        return BCrypt.Net.BCrypt.Verify(password, passwordHash);
    }

    /// <inheritdoc/>
    public bool NeedsRehash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        try
        {
            return BCrypt.Net.BCrypt.PasswordNeedsRehash(passwordHash, WorkFactor);
        }
        catch (BCrypt.Net.HashInformationException)
        {
            // 无法解析的哈希一律视为需重新哈希,由上层触发重置密码流程
            return true;
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return true;
        }
    }
}
