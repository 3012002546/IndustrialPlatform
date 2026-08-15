namespace IndustrialPlatform.SystemData.Domain.Assignments;

/// <summary>任职持久化状态。</summary>
public enum AssignmentState
{
    /// <summary>有效(按时间区间投影出 Scheduled/Current/Ended)。</summary>
    Enabled = 0,

    /// <summary>已取消(仅计划中的任职可取消;取消即固化)。</summary>
    Cancelled = 1,
}
