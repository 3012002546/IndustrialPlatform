using IndustrialPlatform.SystemData.Contracts.PlatformHealth;

namespace IndustrialPlatform.SystemData.Application.PlatformHealth;

/// <summary>健康新鲜度与依赖级别聚合；来源缺失不能变成健康。</summary>
public static class PlatformHealthAggregation
{
    public static PlatformHealthServiceDto Build(string serviceNId, string name,
        IReadOnlyList<PlatformHealthCheckDto> checks, DateTimeOffset now, int staleAfterSeconds = 90,
        bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(checks);
        var current = checks.Select(check => enabled ? Effective(check, now, staleAfterSeconds) : check with { EffectiveState = "Disabled" }).ToArray();
        var active = current.Where(check => check.EffectiveState != "Disabled").ToArray();
        var required = current.Where(check => check.Criticality == "Required").ToArray();
        var state = !enabled ? "Disabled"
            : active.Length == 0 ? "Unknown"
            : required.Any(check => check.EffectiveState == "Unavailable") ? "Unavailable"
            : required.Any(check => check.EffectiveState is "Unknown" or "Disabled") ? "Unknown"
            : active.Any(check => check.EffectiveState != "Healthy") ? "Degraded" : "Healthy";
        DateTimeOffset? oldest = active.Length == 0 || active.Any(check => check.CheckedOn is null)
            ? null : active.Min(check => check.CheckedOn);
        return new(serviceNId, name, state, oldest, current, current.Length, active.Length,
            current.Count(check => check.EffectiveState == "Healthy"),
            current.Count(check => check.EffectiveState == "Degraded"),
            current.Count(check => check.EffectiveState == "Unavailable"),
            current.Count(check => check.EffectiveState == "Unknown"),
            current.Count(check => check.EffectiveState == "Disabled"));
    }

    private static PlatformHealthCheckDto Effective(PlatformHealthCheckDto check, DateTimeOffset now, int staleAfterSeconds)
    {
        if (check.ReportedState == "Disabled") return check with { EffectiveState = "Disabled" };
        if (check.CheckedOn is null || check.ReceivedOn is null)
            return check with { EffectiveState = "Unknown", ReasonCode = check.ReasonCode ?? "SOURCE_TIME_MISSING" };
        if (check.CheckedOn > now.AddSeconds(5))
            return check with { EffectiveState = "Unknown", ReasonCode = "SOURCE_CLOCK_SKEW" };
        var observed = check.CheckedOn < check.ReceivedOn ? check.CheckedOn.Value : check.ReceivedOn.Value;
        if (now - observed > TimeSpan.FromSeconds(staleAfterSeconds))
            return check with { EffectiveState = "Unknown", ReasonCode = "STALE_OBSERVATION" };
        return check with { EffectiveState = check.ReportedState is "Healthy" or "Degraded" or "Unavailable" or "Unknown" ? check.ReportedState : "Unknown" };
    }
}
