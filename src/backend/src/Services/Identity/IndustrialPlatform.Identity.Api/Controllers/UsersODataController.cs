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
            using var queryCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            queryCancellation.CancelAfter(TimeSpan.FromSeconds(10));
            var page = await _service.QueryUsersAsync(tenantNId, descriptor, queryCancellation.Token);
            var projected = page.Items.Select(item => item.Project(descriptor.Select)).ToList();
            return PageResult.Create(projected, page.Total, page.PageIndex, page.PageSize);
        }
        catch (QueryValidationException ex)
        {
            return BadRequestEnvelope(
                ex.Error.Code,
                ex.Error.Message,
                ex.Error.Parameter is null
                    ? null
                    : new Dictionary<string, object?> { ["parameter"] = ex.Error.Parameter });
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
            var culture = ReadExportCulture(Request.Query["culture"].ToString());
            var timeZone = ReadExportTimeZone(Request.Query["timeZone"].ToString());
            return StreamingXlsxExport.Start(
                "users.xlsx",
                (output, ct) => ProduceExportAsync(output, tenantNId, descriptor, fields, quantity, culture, timeZone, ct),
                cancellationToken);
        }
        catch (QueryValidationException ex)
        {
            return BadRequestEnvelope(
                ex.Error.Code,
                ex.Error.Message,
                ex.Error.Parameter is null
                    ? null
                    : new Dictionary<string, object?> { ["parameter"] = ex.Error.Parameter });
        }
    }

    private async Task ProduceExportAsync(
        Stream output,
        string tenantNId,
        QueryDescriptor descriptor,
        IReadOnlyList<string> fields,
        int quantity,
        CultureInfo culture,
        TimeZoneInfo timeZone,
        CancellationToken requestCancellationToken)
    {
        using var queryCancellation = CancellationTokenSource.CreateLinkedTokenSource(requestCancellationToken);
        queryCancellation.CancelAfter(TimeSpan.FromSeconds(10));
        var cancellationToken = queryCancellation.Token;
        await using var workbook = await StreamingXlsxWriter.CreateAsync(output, cancellationToken);
        await workbook.WriteRowAsync(fields.Select(field => UserQueryExportResources.GetTitle(field, culture)), cancellationToken);
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
                    fields.Select(field => FormatExportValue(field, selected[field], culture, timeZone)),
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

    private static CultureInfo ReadExportCulture(string? raw)
    {
        var name = string.IsNullOrWhiteSpace(raw) ? "zh-CN" : raw.Trim();
        if (!name.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) &&
            !name.Equals("en-US", StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidExportLocale("culture");
        }

        return CultureInfo.GetCultureInfo(name);
    }

    private static TimeZoneInfo ReadExportTimeZone(string? raw)
    {
        var name = string.IsNullOrWhiteSpace(raw) ? "UTC" : raw.Trim();
        var candidates = name switch
        {
            "UTC" => new[] { "UTC" },
            "Asia/Taipei" => new[] { "Asia/Taipei", "Taipei Standard Time" },
            "Taipei Standard Time" => new[] { "Taipei Standard Time", "Asia/Taipei" },
            "America/Los_Angeles" => new[] { "America/Los_Angeles", "Pacific Standard Time" },
            "Pacific Standard Time" => new[] { "Pacific Standard Time", "America/Los_Angeles" },
            _ => Array.Empty<string>(),
        };

        foreach (var candidate in candidates)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidate);
            }
            catch (TimeZoneNotFoundException)
            {
                // Try the platform-specific alias next.
            }
            catch (InvalidTimeZoneException)
            {
                // Try the platform-specific alias next.
            }
        }

        throw InvalidExportLocale("timeZone");
    }

    private static string? FormatExportValue(
        string field,
        object? value,
        CultureInfo culture,
        TimeZoneInfo timeZone)
    {
        if (value is null) return null;
        if (field is "createdOn" or "lastLoginOn" && value is DateTimeOffset date)
        {
            var local = TimeZoneInfo.ConvertTime(date, timeZone);
            var format = culture.Name.Equals("en-US", StringComparison.OrdinalIgnoreCase)
                ? "MM/dd/yyyy HH:mm:ss zzz"
                : "yyyy年MM月dd日 HH:mm:ss zzz";
            return local.ToString(format, culture);
        }

        return Convert.ToString(value, culture);
    }

    private static QueryValidationException InvalidExportLocale(string parameter)
        => new(new QueryValidationError(
            "PLATFORM_EXPORT_LOCALE_INVALID",
            "导出 culture 或 timeZone 不受支持。",
            parameter));
}
