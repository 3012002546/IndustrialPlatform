using System.Globalization;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IndustrialPlatform.Web.Filters;

/// <summary>
/// API 结果包装过滤器:将控制器返回的成功结果统一包装为 <see cref="ApiResult"/> 负载,
/// 已包装的 <see cref="ApiResult"/> 或错误状态(≥400)保持原样透传。
/// </summary>
public sealed class ResultFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();
        var result = executed.Result;

        switch (result)
        {
            case EmptyResult:
                executed.Result = new ObjectResult(ApiResult.Ok());
                break;

            case ObjectResult objectResult:
                if (!IsApiResult(objectResult.Value))
                {
                    objectResult.Value = Wrap(objectResult.Value, objectResult.StatusCode ?? StatusCodes.Status200OK);
                }

                break;

            case JsonResult jsonResult:
                if (!IsApiResult(jsonResult.Value))
                {
                    jsonResult.Value = Wrap(jsonResult.Value, StatusCodes.Status200OK);
                }

                break;
        }
    }

    private static object Wrap(object? value, int statusCode)
    {
        if (statusCode is >= 200 and < 400)
        {
            return value is null ? ApiResult.Ok() : ApiResult.Ok(value);
        }

        return ApiResult.Fail<object?>(statusCode.ToString(CultureInfo.InvariantCulture), value as string ?? "Request failed");
    }

    private static bool IsApiResult(object? value)
    {
        if (value is null)
        {
            return false;
        }

        var type = value.GetType();
        return type == typeof(ApiResult)
            || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ApiResult<>));
    }
}
