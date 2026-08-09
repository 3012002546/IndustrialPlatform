using IndustrialPlatform.SharedKernel.Exceptions;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class ExceptionTests
{
    [Theory]
    [InlineData(typeof(BusinessException))]
    [InlineData(typeof(ValidationException))]
    [InlineData(typeof(UnauthorizedException))]
    [InlineData(typeof(NotFoundException))]
    public void AllDomainExceptions_DeriveFromDomainException(Type exceptionType)
    {
        Assert.True(typeof(DomainException).IsAssignableFrom(exceptionType));
    }

    [Theory]
    [InlineData(typeof(BusinessException))]
    [InlineData(typeof(ValidationException))]
    [InlineData(typeof(UnauthorizedException))]
    [InlineData(typeof(NotFoundException))]
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
}
