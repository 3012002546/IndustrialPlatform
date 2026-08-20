using IndustrialPlatform.Web.Initialization;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class InternalInitializationAuthenticationTests
{
    [Fact]
    public void Missing_or_invalid_initialization_key_is_rejected()
    {
        var missing = new DefaultHttpContext();
        var invalid = new DefaultHttpContext();
        invalid.Request.Headers[InternalInitializationAuthentication.HeaderName] = "wrong";

        Assert.False(InternalInitializationAuthentication.IsAuthorized(missing.Request, "expected"));
        Assert.False(InternalInitializationAuthentication.IsAuthorized(invalid.Request, "expected"));
    }

    [Fact]
    public void Valid_initialization_key_is_accepted_without_exposing_the_key()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[InternalInitializationAuthentication.HeaderName] = "expected";

        Assert.True(InternalInitializationAuthentication.IsAuthorized(context.Request, "expected"));
        Assert.DoesNotContain("expected", InternalInitializationAuthentication.HeaderName, StringComparison.Ordinal);
    }
}
