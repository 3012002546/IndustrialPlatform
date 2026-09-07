namespace IndustrialPlatform.SystemData.Domain.Notifications;

public static class NotificationTargetRules
{
    public const int MaximumRecipients = 1_000;

    public static IReadOnlyList<string> Normalize(IEnumerable<string>? userNIds)
    {
        ArgumentNullException.ThrowIfNull(userNIds);
        var result = userNIds
            .Select(value => value?.Trim() ?? string.Empty)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (result.Length == 0)
        {
            throw new ArgumentException("通知至少需要一个收件人。", nameof(userNIds));
        }

        if (result.Length > MaximumRecipients)
        {
            throw new ArgumentException($"通知收件人不能超过 {MaximumRecipients} 人。", nameof(userNIds));
        }

        return result;
    }
}
