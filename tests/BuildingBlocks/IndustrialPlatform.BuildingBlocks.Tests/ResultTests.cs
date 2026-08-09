using IndustrialPlatform.SharedKernel.Results;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class ResultTests
{
    [Fact]
    public void Ok_ProducesSuccessfulResult()
    {
        var result = Result.Ok();

        Assert.True(result.Success);
        Assert.Null(result.Message);
    }

    [Fact]
    public void Ok_WithMessage_ProducesSuccessfulResult()
    {
        var result = Result.Ok("done");

        Assert.True(result.Success);
        Assert.Equal("done", result.Message);
    }

    [Fact]
    public void Fail_ProducesFailedResult()
    {
        var result = Result.Fail("boom");

        Assert.False(result.Success);
        Assert.Equal("boom", result.Message);
    }

    [Fact]
    public void Ok_WithData_ProducesSuccessfulResult()
    {
        Result<int> result = Result.Ok(42);

        Assert.True(result.Success);
        Assert.Equal(42, result.Data);
    }

    [Fact]
    public void Fail_Generic_ReferenceType_ProducesNullData()
    {
        Result<string> result = Result.Fail<string>("boom");

        Assert.False(result.Success);
        Assert.Equal("boom", result.Message);
        Assert.Null(result.Data);
    }

    [Fact]
    public void Fail_Generic_ValueType_ProducesDefaultData()
    {
        Result<int> result = Result.Fail<int>("boom");

        Assert.False(result.Success);
        Assert.Equal("boom", result.Message);
        Assert.Equal(0, result.Data);
    }
}
