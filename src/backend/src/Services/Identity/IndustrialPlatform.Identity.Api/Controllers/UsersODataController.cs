using IndustrialPlatform.Identity.Api.Authorization;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Api.Export;
using IndustrialPlatform.Querying.Descriptors;
using IndustrialPlatform.Querying.Validation;
using System.Globalization;
using IndustrialPlatform.Security;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.Web.Querying;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.Identity.Api.Controllers;

/// <summary>
/// Identity-owned OData input adapter for Users. It returns the platform page
/// envelope and never exposes IQueryable or EnableQuery.
/// </summary>
[ApiController]
[Route("odata/users")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UsersODataController : ManagementControllerBase
{
    private readonly IUserManagementService _service;
    private readonly ODataQueryDescriptorParser _parser;
    public UsersODataController(
        IUserManagementService service,
        ODataQueryDescriptorParser parser,
        ICurrentUser currentUser,
        IIdempotencyStore idempotencyStore)
        : base(currentUser, idempotencyStore)
    {
        _service = service;
        _parser = parser;
    }

    [Authorize(Policy = PermissionPolicies.UserView)]
    [HttpGet]
    public async Task<ActionResult<PageResult<IReadOnlyDictionary<string, object?>>>> List(
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out _)) return UnauthorizedEnvelope();

        try
        {
            var descriptor = _parser.Parse(Request.Query, UserQueryResource.Definition);
            var page = await _service.QueryUsersAsync(tenantNId, descriptor, cancellationToken);
            var projected = page.Items.Select(item => item.Project(descriptor.Select)).ToList();
            return PageResult.Create(projected, page.Total, page.PageIndex, page.PageSize);
        }
        catch (QueryValidationException ex)
        {
            return BadRequestEnvelope(ex.Error.Code, ex.Error.Message);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    [Authorize(Policy = PermissionPolicies.UserView)]
    [HttpGet("export")]
    public IActionResult Export(CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out _)) return UnauthorizedEnvelope();

        QueryDescriptor descriptor;
        try
        {
            descriptor = _parser.Parse(Request.Query, UserQueryResource.Definition);
            var fields = ReadExportFields(Request.Query["columns"].ToString(), descriptor.Select);
            var quantity = StreamingXlsxExport.ParseQuantity(Request.Query["quantity"].ToString());
            return StreamingXlsxExport.Start(
                "users.xlsx",
                (output, ct) => ProduceExportAsync(output, tenantNId, descriptor, fields, quantity, ct),
                cancellationToken);
        }
        catch (QueryValidationException ex)
        {
            return BadRequestEnvelope(ex.Error.Code, ex.Error.Message);
        }
    }

    private async Task ProduceExportAsync(
        Stream output,
        string tenantNId,
        QueryDescriptor descriptor,
        IReadOnlyList<string> fields,
        int quantity,
        CancellationToken cancellationToken)
    {
        await using var workbook = await StreamingXlsxWriter.CreateAsync(output, cancellationToken);
        await workbook.WriteRowAsync(fields.Select(UserQueryResourceTitle), cancellationToken);
        var written = 0;
        var pageIndex = 1;
        while (written < quantity)
        {
            var page = await _service.QueryUsersAsync(
                tenantNId,
                descriptor with { PageIndex = pageIndex, PageSize = Math.Min(descriptor.PageSize, 100) },
                cancellationToken);
            if (page.Items.Count == 0) break;
            foreach (var item in page.Items)
            {
                if (written >= quantity) break;
                var selected = item.Project(fields);
                await workbook.WriteRowAsync(
                    fields.Select(field => Convert.ToString(selected[field], CultureInfo.InvariantCulture)),
                    cancellationToken);
                written++;
            }
            if (written >= page.Total || page.Items.Count < descriptor.PageSize) break;
            pageIndex++;
        }
    }

    private static IReadOnlyList<string> ReadExportFields(string? raw, IReadOnlyList<string> fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        var fields = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fields.Length == 0 || fields.Any(field => !UserQueryResource.Definition.Fields.ContainsKey(field)))
        {
            throw new QueryValidationException(new QueryValidationError(
                "PLATFORM_QUERY_FIELD_NOT_ALLOWED", "导出字段未注册。", "columns"));
        }
        return fields.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string UserQueryResourceTitle(string field)
        => field switch
        {
            "userNId" => "用户标识",
            "loginName" => "登录名",
            "name" => "姓名",
            "email" => "邮箱",
            "phone" => "手机号",
            "status" => "状态",
            "createdOn" => "创建时间",
            "lastLoginOn" => "最近登录",
            "mustChangePassword" => "需改密",
            "effectiveRoleCount" => "有效角色",
            _ => field,
        };
}
