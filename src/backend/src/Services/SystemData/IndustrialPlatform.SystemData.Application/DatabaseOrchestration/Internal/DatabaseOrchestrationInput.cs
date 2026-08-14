using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration.Identities;

namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Internal;

/// <summary>编排用例输入校验与标识生成助手(供各服务复用)。</summary>
internal static class DatabaseOrchestrationInput
{
    /// <summary>非空文本校验并修剪。</summary>
    public static string Require(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationFailedException(message);
        }

        return value.Trim();
    }

    /// <summary>可空文本修剪;空白返回 null。</summary>
    public static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>解析枚举名字符串;非法抛 SD_VALIDATION_FAILED。</summary>
    public static T ParseEnum<T>(string value, string paramName)
        where T : struct, Enum
    {
        if (Enum.TryParse<T>(value, ignoreCase: true, out var result) && Enum.IsDefined(result))
        {
            return result;
        }

        throw new ValidationFailedException($"{paramName} 无效:{value}。");
    }

    /// <summary>生成带前缀的业务标识(如 <c>OP-&lt;guid&gt;</c>)。</summary>
    public static string NewNId(string prefix) => NId.Create($"{prefix}-{Guid.NewGuid():N}").Value;
}
