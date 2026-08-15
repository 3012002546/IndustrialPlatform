namespace IndustrialPlatform.SystemData.Domain.Assignments;

/// <summary>
/// 任职投影状态(由当前时间派生,05 方案 §7.4):
/// Cancelled / Scheduled / Current / Ended。区间边界使用左闭右开。
/// </summary>
public enum AssignmentProjection
{
    /// <summary>已取消(State=Cancelled)。</summary>
    Cancelled = 0,

    /// <summary>计划中(Enabled 且 now &lt; EffectiveFrom)。</summary>
    Scheduled = 1,

    /// <summary>当前(Enabled 且 EffectiveFrom &lt;= now &lt; EffectiveTo 或 EffectiveTo 为空)。</summary>
    Current = 2,

    /// <summary>已结束(Enabled 且 EffectiveTo &lt;= now)。</summary>
    Ended = 3,
}
