using IndustrialPlatform.Identity.Domain.Passwords;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.Identity.Domain.Tests;

/// <summary>
/// 明文密码复杂度策略测试。策略由应用层在哈希前调用,领域层不接收明文。
/// </summary>
public sealed class PasswordPolicyTests
{
    // 12 个字符:A、a、1、! + 8 个 a,恰好命中全部复杂度规则与最短长度。
    private const string Strong = "Aa1!aaaaaaaa";

    [Fact]
    public void Validate_StrongPassword_DoesNotThrow()
    {
        PasswordPolicy.Validate(Strong, "login", "uid-01");
    }

    [Fact]
    public void Validate_Null_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() => PasswordPolicy.Validate(null, "login", "uid-01"));
    }

    [Fact]
    public void Validate_Whitespace_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() => PasswordPolicy.Validate("   ", "login", "uid-01"));
    }

    [Fact]
    public void Validate_TooShort_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() => PasswordPolicy.Validate("Aa1!aaaaaa", "login", "uid-01"));
    }

    [Fact]
    public void Validate_MinLength_Accepted()
    {
        Assert.Equal(PasswordPolicy.MinLength, Strong.Length);

        PasswordPolicy.Validate(Strong, "login", "uid-01");
    }

    [Fact]
    public void Validate_TooLong_ThrowsValidationException()
    {
        var tooLong = "Aa1!" + new string('a', PasswordPolicy.MaxLength - 4 + 1);

        Assert.Throws<ValidationException>(() => PasswordPolicy.Validate(tooLong, "login", "uid-01"));
    }

    [Fact]
    public void Validate_MaxLength_Accepted()
    {
        var max = "Aa1!" + new string('a', PasswordPolicy.MaxLength - 4);

        PasswordPolicy.Validate(max, "login", "uid-01");
    }

    [Fact]
    public void Validate_MissingUppercase_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() => PasswordPolicy.Validate("aa1!aaaaaaaa", "login", "uid-01"));
    }

    [Fact]
    public void Validate_MissingLowercase_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() => PasswordPolicy.Validate("AA1!AAAAAAAA", "login", "uid-01"));
    }

    [Fact]
    public void Validate_MissingDigit_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() => PasswordPolicy.Validate("AaA!aaaaaaaa", "login", "uid-01"));
    }

    [Fact]
    public void Validate_MissingSpecial_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() => PasswordPolicy.Validate("Aa1aaaaaaaaa", "login", "uid-01"));
    }

    [Fact]
    public void Validate_EqualsLoginName_ThrowsValidationException_IgnoringCase()
    {
        const string password = "LoginName123!";

        Assert.Throws<ValidationException>(() => PasswordPolicy.Validate(password, "loginname123!", "uid-01"));
    }

    [Fact]
    public void Validate_EqualsNId_ThrowsValidationException_IgnoringCase()
    {
        const string password = "Aa1!Xyz12345";

        Assert.Throws<ValidationException>(() => PasswordPolicy.Validate(password, "someone", "aa1!xyz12345"));
    }

    [Fact]
    public void Validate_NullLoginNameAndNId_DoesNotThrow()
    {
        PasswordPolicy.Validate(Strong, null, null);
    }
}
