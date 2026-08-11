using IndustrialPlatform.Identity.Infrastructure.Passwords;

namespace IndustrialPlatform.Identity.Infrastructure.Tests;

/// <summary>
/// BCrypt 密码哈希实现测试(工作因子 12,§10.1)。验证自包含哈希格式、
/// 校验往返与安全参数提升触发重哈希。
/// </summary>
public sealed class BcryptPasswordHasherTests
{
    private const string Password = "S3cure!Passw0rd";

    /// <summary>已知工作因子 10 的合法 BCrypt 哈希(经典 "password" 哈希),用于验证低因子哈希触发重哈希。</summary>
    private const string WorkFactorTenHash = "$2a$10$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy";

    private readonly BcryptPasswordHasher _hasher = new();

    [Fact]
    public void Hash_ProducesSelfContainedHash_WithWorkFactorTwelve()
    {
        var hash = _hasher.Hash(Password);

        // $2a$12$... 自包含哈希,工作因子 12,不含明文
        Assert.StartsWith("$2", hash, StringComparison.Ordinal);
        Assert.Equal("12", hash.Split('$')[2]);
        Assert.NotEqual(Password, hash);
    }

    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        var hash = _hasher.Hash(Password);

        Assert.True(_hasher.Verify(hash, Password));
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hash = _hasher.Hash(Password);

        Assert.False(_hasher.Verify(hash, "Wrong!Password1"));
    }

    [Fact]
    public void NeedsRehash_OwnFactorTwelveHash_ReturnsFalse()
    {
        var hash = _hasher.Hash(Password);

        Assert.False(_hasher.NeedsRehash(hash));
    }

    [Fact]
    public void NeedsRehash_LowerWorkFactorHash_ReturnsTrue()
    {
        // 安全参数提升(因子 12)后,旧的低因子哈希应标记需重新哈希
        Assert.True(_hasher.NeedsRehash(WorkFactorTenHash));
    }
}
