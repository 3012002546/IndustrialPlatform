using IndustrialPlatform.Identity.Domain.Identities;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.Identity.Domain.Tests;

/// <summary>
/// NId 值对象测试:规范化、大小写不敏感相等性与格式校验。
/// </summary>
public sealed class NIdTests
{
    [Fact]
    public void Create_TrimsSurroundingWhitespace()
    {
        var nid = NId.Create("  AbC  ");

        Assert.Equal("AbC", nid.Value);
        Assert.Equal("ABC", nid.Normalized);
    }

    [Fact]
    public void Create_PreservesOriginalCaseInValue()
    {
        var nid = NId.Create("aBc-01");

        Assert.Equal("aBc-01", nid.Value);
        Assert.Equal("ABC-01", nid.Normalized);
    }

    [Fact]
    public void Equality_IsCaseInsensitive()
    {
        var left = NId.Create("abc-01");
        var right = NId.Create("ABC-01");

        Assert.True(left.Equals(right));
        Assert.True(left == right);
        Assert.False(left != right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentValue_IsFalse()
    {
        var left = NId.Create("abc-01");
        var right = NId.Create("abc-02");

        Assert.False(left.Equals(right));
        Assert.True(left != right);
    }

    [Fact]
    public void Create_RejectsNull()
    {
        Assert.Throws<ValidationException>(() => NId.Create(null));
    }

    [Fact]
    public void Create_RejectsWhitespace()
    {
        Assert.Throws<ValidationException>(() => NId.Create("   "));
    }

    [Fact]
    public void Create_RejectsOverMaxLength()
    {
        var tooLong = new string('a', NId.MaxLength + 1);

        Assert.Throws<ValidationException>(() => NId.Create(tooLong));
    }

    [Fact]
    public void Create_AcceptsMaxLength()
    {
        var max = new string('a', NId.MaxLength);

        Assert.Equal(max, NId.Create(max).Value);
    }

    [Fact]
    public void Create_RejectsLeadingNonAlphanumeric()
    {
        Assert.Throws<ValidationException>(() => NId.Create("-abc"));
        Assert.Throws<ValidationException>(() => NId.Create("_abc"));
        Assert.Throws<ValidationException>(() => NId.Create(".abc"));
    }

    [Fact]
    public void Create_RejectsInvalidCharacters()
    {
        Assert.Throws<ValidationException>(() => NId.Create("abc def"));
        Assert.Throws<ValidationException>(() => NId.Create("a/b"));
        Assert.Throws<ValidationException>(() => NId.Create("a#b"));
    }

    [Fact]
    public void Create_AcceptsUnderscoreDotAndHyphenInBody()
    {
        var nid = NId.Create("a_1.2-3");

        Assert.Equal("A_1.2-3", nid.Normalized);
    }
}
