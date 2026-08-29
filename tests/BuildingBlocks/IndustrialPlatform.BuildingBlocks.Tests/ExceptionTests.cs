using System.Text.Json;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.Web.Results;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class ExceptionTests
{
    [Theory]
    [InlineData(typeof(BusinessException))]
    [InlineData(typeof(ValidationException))]
    [InlineData(typeof(UnauthorizedException))]
    [InlineData(typeof(NotFoundException))]
    [InlineData(typeof(ConcurrencyException))]
    public void AllDomainExceptions_DeriveFromDomainException(Type exceptionType)
    {
        Assert.True(typeof(DomainException).IsAssignableFrom(exceptionType));
    }

    [Theory]
    [InlineData(typeof(BusinessException))]
    [InlineData(typeof(ValidationException))]
    [InlineData(typeof(UnauthorizedException))]
    [InlineData(typeof(NotFoundException))]
    [InlineData(typeof(ConcurrencyException))]
    public void AllDomainExceptions_CarryMessage(Type exceptionType)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "oops")!;

        Assert.Equal("oops", exception.Message);
    }

    [Fact]
    public void DomainExceptions_SupportInnerException()
    {
        var inner = new InvalidOperationException("root cause");
        var exception = new BusinessException("wrapped", inner);

        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public void ApiResult_OmitsOptionalErrorFieldsWhenUnset()
    {
        var json = JsonSerializer.Serialize(ApiResult.Ok());

        Assert.DoesNotContain("parameters", json, StringComparison.Ordinal);
        Assert.DoesNotContain("traceId", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiResult_CarriesOptionalErrorFields()
    {
        var result = ApiResult.Fail<object?>("PLATFORM_QUERY_INVALID", "fallback");
        result.Parameters = new Dictionary<string, object?> { ["parameter"] = "$filter" };
        result.TraceId = "trace-1";

        var json = JsonSerializer.Serialize(result);

        Assert.Contains("PLATFORM_QUERY_INVALID", json, StringComparison.Ordinal);
        Assert.Contains("$filter", json, StringComparison.Ordinal);
        Assert.Contains("trace-1", json, StringComparison.Ordinal);
    }
}
