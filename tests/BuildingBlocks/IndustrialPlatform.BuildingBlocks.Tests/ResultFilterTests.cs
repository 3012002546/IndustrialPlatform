using IndustrialPlatform.Web.Filters;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class ResultFilterTests
{
    [Fact]
    public async Task ObjectResult_SuccessStatus_IsWrappedInApiResult()
    {
        var executed = await RunAsync(new ObjectResult("payload"));

        var objectResult = Assert.IsType<ObjectResult>(executed.Result);
        var apiResult = Assert.IsType<ApiResult<object>>(objectResult.Value);
        Assert.True(apiResult.Success);
        Assert.Equal("payload", apiResult.Data);
    }

    [Fact]
    public async Task ObjectResult_WithDeclaredType_IsWrappedAndDeclaredTypeCleared()
    {
        // 模拟 ActionResult<T> 隐式转换:DeclaredType 被设置为 T,包装为 ApiResult 后必须清空,
        // 否则输出格式化器按 T 反序列化 ApiResult 抛出 InvalidCastException。
        var executed = await RunAsync(new ObjectResult("payload") { DeclaredType = typeof(string) });

        var objectResult = Assert.IsType<ObjectResult>(executed.Result);
        Assert.Null(objectResult.DeclaredType);
        var apiResult = Assert.IsType<ApiResult<object>>(objectResult.Value);
        Assert.True(apiResult.Success);
        Assert.Equal("payload", apiResult.Data);
    }

    [Fact]
    public async Task ObjectResult_AlreadyWrapped_RemainsUntouched()
    {
        var original = ApiResult.Ok(new { Id = 7 });
        var executed = await RunAsync(new ObjectResult(original));

        var objectResult = Assert.IsType<ObjectResult>(executed.Result);
        Assert.Same(original, objectResult.Value);
    }

    [Fact]
    public async Task ObjectResult_ErrorStatus_IsWrappedAsFailure()
    {
        var executed = await RunAsync(new ObjectResult("not found") { StatusCode = StatusCodes.Status404NotFound });

        var objectResult = Assert.IsType<ObjectResult>(executed.Result);
        var apiResult = Assert.IsType<ApiResult<object?>>(objectResult.Value);
        Assert.False(apiResult.Success);
        Assert.Equal("404", apiResult.Code);
        Assert.Equal("not found", apiResult.Message);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    [Fact]
    public async Task EmptyResult_IsReplacedWithSuccessApiResult()
    {
        var executed = await RunAsync(new EmptyResult());

        var objectResult = Assert.IsType<ObjectResult>(executed.Result);
        var apiResult = Assert.IsType<ApiResult>(objectResult.Value);
        Assert.True(apiResult.Success);
    }

    [Fact]
    public async Task JsonResult_IsWrappedInApiResult()
    {
        var executed = await RunAsync(new JsonResult("payload"));

        var jsonResult = Assert.IsType<JsonResult>(executed.Result);
        var apiResult = Assert.IsType<ApiResult<object>>(jsonResult.Value);
        Assert.True(apiResult.Success);
        Assert.Equal("payload", apiResult.Data);
    }

    private static async Task<ActionExecutedContext> RunAsync(ActionResult initialResult)
    {
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        var filters = new List<IFilterMetadata>();
        var executingContext = new ActionExecutingContext(
            actionContext,
            filters,
            new Dictionary<string, object?>(),
            controller: new object());
        var executedContext = new ActionExecutedContext(actionContext, filters, controller: new object())
        {
            Result = initialResult,
        };

        var filter = new ResultFilter();
        await filter.OnActionExecutionAsync(executingContext, () => Task.FromResult(executedContext));

        return executedContext;
    }
}
