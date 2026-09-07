namespace IndustrialPlatform.SystemData.Domain.Notifications;

public static class NotificationContentRules
{
    public static string Title(string? value) => Normalize(value, 200, "通知标题不能为空且不能超过 200 个字符。", allowEmpty: false);

    public static string Body(string? value) => Normalize(value, 20_000, "通知内容不能为空且不能超过 20000 个字符。", allowEmpty: false);

    public static string? Route(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var route = value.Trim();
        if (route.Length == 0 || route[0] != '/' || route.StartsWith("//", StringComparison.Ordinal)
            || route.Contains('\r') || route.Contains('\n') || route.Contains("javascript:", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("通知跳转目标必须是站内路径。", nameof(value));
        return route;
    }

    private static string Normalize(string? value, int maxLength, string error, bool allowEmpty)
    {
        var text = value?.Trim() ?? string.Empty;
        if ((!allowEmpty && text.Length == 0) || text.Length > maxLength || text.Contains('\0', StringComparison.Ordinal))
            throw new ArgumentException(error, nameof(value));
        if (text.Contains("<script", StringComparison.OrdinalIgnoreCase)
            || text.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
            || text.Contains("data:text/html", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("通知内容包含不允许的脚本或跳转内容。", nameof(value));
        return text;
    }
}
