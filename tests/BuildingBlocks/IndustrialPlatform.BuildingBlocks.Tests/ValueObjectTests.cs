using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class ValueObjectTests
{
    [Fact]
    public void ValueObjects_WithSameComponents_AreEqual()
    {
        var first = new Money(100m, "CNY");
        var second = new Money(100m, "CNY");

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void ValueObjects_WithDifferentComponents_AreNotEqual()
    {
        var hundredYuan = new Money(100m, "CNY");
        var differentAmount = new Money(200m, "CNY");
        var differentCurrency = new Money(100m, "USD");

        Assert.NotEqual(hundredYuan, differentAmount);
        Assert.NotEqual(hundredYuan, differentCurrency);
    }

    [Fact]
    public void ValueObject_EqualsNull_ReturnsFalse()
    {
        var money = new Money(100m, "CNY");

        Assert.False(money == null);
        Assert.True(money != null);
        Assert.False(money.Equals(null));
    }
}
