namespace IndustrialPlatform.SystemData.Domain.Assignments;

/// <summary>
/// 任职调度纯规则(05 方案 §7.4/§12.3):由应用层在按用户 advisory lock 内调用,
/// 用于裁决同用户/岗位 Enabled 区间重叠与主任职覆盖。区间边界左闭右开。
/// </summary>
public static class AssignmentScheduleRules
{
    /// <summary>同一 (租户, 用户, 岗位) 下两条 Enabled 任职区间重叠。</summary>
    /// <param name="AFrom">第一条区间开始。</param>
    /// <param name="ATo">第一条区间结束(可空)。</param>
    /// <param name="BFrom">第二条区间开始。</param>
    /// <param name="BTo">第二条区间结束(可空)。</param>
    public readonly record struct OverlappingInterval(
        string TenantNId,
        string UserNId,
        string PositionNId,
        DateTimeOffset AFrom,
        DateTimeOffset? ATo,
        DateTimeOffset BFrom,
        DateTimeOffset? BTo);

    /// <summary>时间段内任职覆盖与主任职覆盖不一致(覆盖且主任职数 != 1)。</summary>
    public readonly record struct PrimaryCoverageViolation(
        string TenantNId,
        string UserNId,
        DateTimeOffset From,
        DateTimeOffset? To,
        int ActiveCount,
        int PrimaryCount);

    /// <summary>找出同一 (租户, 用户, 岗位) 下两两重叠的 Enabled 区间。</summary>
    public static IReadOnlyList<OverlappingInterval> FindOverlappingIntervals(IEnumerable<UserAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);

        var enabled = assignments.Where(a => a.State == AssignmentState.Enabled).ToArray();
        var overlaps = new List<OverlappingInterval>();

        foreach (var group in enabled.GroupBy(a => (a.TenantNId, a.UserNId, a.PositionNId)))
        {
            var items = group.ToArray();
            for (var i = 0; i < items.Length; i++)
            {
                for (var j = i + 1; j < items.Length; j++)
                {
                    var left = EffectivePeriod.Create(items[i].EffectiveFrom, items[i].EffectiveTo);
                    var right = EffectivePeriod.Create(items[j].EffectiveFrom, items[j].EffectiveTo);
                    if (left.OverlapsWith(right))
                    {
                        overlaps.Add(new OverlappingInterval(
                            group.Key.TenantNId,
                            group.Key.UserNId,
                            group.Key.PositionNId,
                            left.EffectiveFrom,
                            left.EffectiveTo,
                            right.EffectiveFrom,
                            right.EffectiveTo));
                    }
                }
            }
        }

        return overlaps;
    }

    /// <summary>
    /// 找出主任职覆盖违规:同一 (租户, 用户) 任一时点存在 Enabled 任职但主任职数不为 1。
    /// 开放区间(EffectiveTo 为空)视为延伸到无限远。
    /// </summary>
    public static IReadOnlyList<PrimaryCoverageViolation> FindPrimaryCoverageViolations(IEnumerable<UserAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);

        var enabled = assignments.Where(a => a.State == AssignmentState.Enabled).ToArray();
        var violations = new List<PrimaryCoverageViolation>();

        foreach (var group in enabled.GroupBy(a => (a.TenantNId, a.UserNId)))
        {
            FindViolationsForUser(group.Key.TenantNId, group.Key.UserNId, group, violations);
        }

        return violations;
    }

    private static void FindViolationsForUser(
        string tenantNId,
        string userNId,
        IEnumerable<UserAssignment> assignments,
        List<PrimaryCoverageViolation> violations)
    {
        var events = new List<(DateTimeOffset Time, int ActiveDelta, int PrimaryDelta)>();
        foreach (var assignment in assignments)
        {
            events.Add((assignment.EffectiveFrom, 1, assignment.IsPrimary ? 1 : 0));
            if (assignment.EffectiveTo is { } to)
            {
                events.Add((to, -1, assignment.IsPrimary ? -1 : 0));
            }
        }

        events.Sort(static (x, y) => x.Time.CompareTo(y.Time));
        if (events.Count == 0)
        {
            return;
        }

        var active = 0;
        var primary = 0;
        DateTimeOffset? segmentStart = null;

        for (var i = 0; i < events.Count;)
        {
            var time = events[i].Time;
            if (segmentStart is { } start && start < time && active >= 1 && primary != 1)
            {
                violations.Add(new PrimaryCoverageViolation(tenantNId, userNId, start, time, active, primary));
            }

            while (i < events.Count && events[i].Time == time)
            {
                active += events[i].ActiveDelta;
                primary += events[i].PrimaryDelta;
                i++;
            }

            segmentStart = time;
        }

        // 最后事件之后的开放段(存在未结束任职时)
        if (segmentStart is { } tail && active >= 1 && primary != 1)
        {
            violations.Add(new PrimaryCoverageViolation(tenantNId, userNId, tail, null, active, primary));
        }
    }
}
