using IndustrialPlatform.Collaboration.Application;
using Xunit;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class MessageTextRulesTests
{
    [Fact]
    public void NormalizeAndValidate_UsesNfcAndCrLfNormalization()
    {
        var value = MessageTextRules.NormalizeAndValidate("e\u0301\r\nnext");

        Assert.Equal("é\nnext", value);
    }

    [Fact]
    public void NormalizeAndValidate_CountsUnicodeScalarsAndRejectsUnsafeControls()
    {
        Assert.Throws<ArgumentException>(() => MessageTextRules.NormalizeAndValidate(new string('a', 4001)));
        Assert.Throws<ArgumentException>(() => MessageTextRules.NormalizeAndValidate("ok\u202Etx"));
        Assert.Throws<ArgumentException>(() => MessageTextRules.NormalizeAndValidate("ok\u0001tx"));
        Assert.Equal("🙂", MessageTextRules.NormalizeAndValidate("🙂"));
    }
}
