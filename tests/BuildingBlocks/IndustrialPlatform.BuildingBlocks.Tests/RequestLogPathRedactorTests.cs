using IndustrialPlatform.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class RequestLogPathRedactorTests
{
    [Fact]
    public void Redact_hides_access_tokens_and_preserves_non_secret_diagnostics()
    {
        var query = new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["access_token"] = "eyJhbGciOiJIUzI1NiJ9.secret",
            ["page"] = "2",
            ["client-secret"] = "do-not-log",
            ["filter"] = "open file",
        });

        var result = RequestLogPathRedactor.Redact("/systemdata/hub" , query);

        Assert.DoesNotContain("eyJhbGci", result, StringComparison.Ordinal);
        Assert.DoesNotContain("do-not-log", result, StringComparison.Ordinal);
        Assert.Contains("access_token=%5BREDACTED%5D", result, StringComparison.Ordinal);
        Assert.Contains("page=2", result, StringComparison.Ordinal);
        Assert.Contains("filter=open%20file", result, StringComparison.Ordinal);
    }
}
