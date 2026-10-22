namespace IndustrialPlatform.Identity.Domain.Passwords;

/// <summary>
/// 密码哈希端口。真实 BCrypt 实现(工作因子 12)由 TASK-ID-004 提供;
/// 领域层只依赖此抽象,禁止明文密码进出持久化、日志与领域事件。
/// </summary>
public interface IPasswordHasher
{
    /// <summary>计算密码哈希。</summary>
    /// <param name="password">明文密码。</param>
    /// <returns>带哈希参数的自包含哈希串。</returns>
    string Hash(string password);

    /// <summary>校验明文密码与已存哈希是否匹配。</summary>
    /// <param name="passwordHash">已存哈希。</param>
    /// <param name="password">待校验的明文密码。</param>
    /// <returns>匹配返回 <c>true</c>。</returns>
    bool Verify(string passwordHash, string password);

    /// <summary>哈希是否已过时(如安全参数提升后需要重新哈希)。</summary>
    /// <param name="passwordHash">已存哈希。</param>
    /// <returns>需要重新哈希返回 <c>true</c>。</returns>
    bool NeedsRehash(string passwordHash);
}
