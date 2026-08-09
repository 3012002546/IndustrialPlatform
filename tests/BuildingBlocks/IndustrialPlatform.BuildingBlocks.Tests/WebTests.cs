using IndustrialPlatform.Web.Results;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class ApiResultTests
{
    [Fact]
    public void Ok_WithoutData_SetsSuccess()
    {
        var result = ApiResult.Ok();

        Assert.True(result.Success);
        Assert.Equal("200", result.Code);
        Assert.Equal("success", result.Message);
    }

    [Fact]
    public void Ok_WithData_SetsData()
    {
        var payload = new { Id = 1 };
        var result = ApiResult.Ok(payload);

        Assert.True(result.Success);
        Assert.Same(payload, result.Data);
    }

    [Fact]
    public void Fail_SetsCodeAndMessage()
    {
        var result = ApiResult.Fail("MAT001", "编码重复");

        Assert.False(result.Success);
        Assert.Equal("MAT001", result.Code);
        Assert.Equal("编码重复", result.Message);
    }

    [Fact]
    public void Fail_OfT_SetsCodeAndMessage()
    {
        var result = ApiResult.Fail<object?>("500", "Internal Server Error");

        Assert.False(result.Success);
        Assert.Equal("500", result.Code);
        Assert.Null(result.Data);
    }
}

public sealed class PageResultTests
{
    private static readonly int[] ExpectedItems = [1, 2, 3];

    [Fact]
    public void Create_ProjectsPagingMetadata()
    {
        var page = PageResult.Create(ExpectedItems, total: 42, pageIndex: 2, pageSize: 3);

        Assert.Equal(3, page.Items.Count);
        Assert.Equal(ExpectedItems, page.Items);
        Assert.Equal(42, page.Total);
        Assert.Equal(2, page.PageIndex);
        Assert.Equal(3, page.PageSize);
    }
}
