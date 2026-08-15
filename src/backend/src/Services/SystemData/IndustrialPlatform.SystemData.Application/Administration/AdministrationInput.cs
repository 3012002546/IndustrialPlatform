using IndustrialPlatform.SystemData.Domain.Assignments;
using IndustrialPlatform.SystemData.Domain.Organizations;
using IndustrialPlatform.SystemData.Domain.Positions;

namespace IndustrialPlatform.SystemData.Application.Administration;

/// <summary>
/// 管理用例输入校验与解析助手(与编排层 <c>DatabaseOrchestrationInput</c> 同模式)。
/// 枚举一律以名字符串传输,非法值抛 <see cref="AdministrationValidationFailedException"/>。
/// </summary>
internal static class AdministrationInput
{
    /// <summary>非空文本校验并修剪。</summary>
    public static string Require(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new AdministrationValidationFailedException(message);
        }

        return value.Trim();
    }

    /// <summary>可空文本修剪;空白返回 null。</summary>
    public static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>解析组织类型枚举名。</summary>
    public static AdministrativeOrganizationType ParseOrganizationType(string? value) =>
        ParseEnum<AdministrativeOrganizationType>(value, nameof(AdministrativeOrganizationType));

    /// <summary>解析组织状态枚举名(可空)。</summary>
    public static OrganizationStatus? ParseOptionalOrganizationStatus(string? value) =>
        ParseOptionalEnum<OrganizationStatus>(value, nameof(OrganizationStatus));

    /// <summary>解析岗位状态枚举名(可空)。</summary>
    public static PositionStatus? ParseOptionalPositionStatus(string? value) =>
        ParseOptionalEnum<PositionStatus>(value, nameof(PositionStatus));

    /// <summary>解析时间(可空)。</summary>
    public static DateTimeOffset? ParseOptionalInstant(string? value, string paramName) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseInstant(value, paramName);

    /// <summary>解析时间。</summary>
    public static DateTimeOffset ParseInstant(string? value, string paramName)
    {
        if (!DateTimeOffset.TryParse(value, out var result))
        {
            throw new AdministrationValidationFailedException($"{paramName} 无效:{value}。");
        }

        return result;
    }

    /// <summary>解析非负显示顺序。</summary>
    public static int RequireNonNegative(int? value)
    {
        if (value is not { } order || order < 0)
        {
            throw new AdministrationValidationFailedException("显示顺序必须为非负整数。");
        }

        return order;
    }

    /// <summary>
    /// 解析非负版本号(双并发版本;新实体初始版本为 0,客户端读取响应回传,
    /// 因此 0 必须合法)。
    /// </summary>
    public static long RequireNonNegativeVersion(long? value, string paramName)
    {
        if (value is not { } version || version < 0)
        {
            throw new AdministrationValidationFailedException($"{paramName} 无效。");
        }

        return version;
    }

    /// <summary>解析并发版本 Guid。</summary>
    public static Guid RequireConcurrencyVersion(Guid? value, string paramName)
    {
        if (value is not { } version || version == Guid.Empty)
        {
            throw new AdministrationValidationFailedException($"{paramName} 无效。");
        }

        return version;
    }

    /// <summary>解析枚举名字符串;非法抛 SD_VALIDATION_FAILED。</summary>
    public static T ParseEnum<T>(string? value, string paramName)
        where T : struct, Enum
    {
        if (Enum.TryParse<T>(value, ignoreCase: true, out var result) && Enum.IsDefined(result))
        {
            return result;
        }

        throw new AdministrationValidationFailedException($"{paramName} 无效:{value}。");
    }

    /// <summary>可空枚举名解析;空白返回 null,非法仍抛 SD_VALIDATION_FAILED。</summary>
    public static T? ParseOptionalEnum<T>(string? value, string paramName)
        where T : struct, Enum =>
        string.IsNullOrWhiteSpace(value) ? null : ParseEnum<T>(value, paramName);
}
