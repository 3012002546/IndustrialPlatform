namespace IndustrialPlatform.SystemData.Contracts.Administration;

// =====================================================================
// 身份目录应用端口契约(TASK-SD-006,05 方案 §11.3)。
// 映射到真实 Identity UserSummary 契约(基线 48c5374):
//   UserNId / LoginName / Name / Status(枚举名字符串)。
// SystemData 不直接依赖 Identity.Contracts,本端口由宿主层(或测试替身)
// 从真实 Identity 数据源适配实现,避免跨服务强耦合与不稳定端点直连。
// =====================================================================

/// <summary>身份目录条目(与 Identity UserSummary 对齐的最小稳定视图)。</summary>
public sealed record IdentityUserDirectoryEntryV1
{
    /// <summary>用户业务标识(与任职 UserNId 一致)。</summary>
    public string UserNId { get; init; } = string.Empty;

    /// <summary>登录名。</summary>
    public string LoginName { get; init; } = string.Empty;

    /// <summary>显示名(任职 UserDisplayNameSnapshot 的来源)。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>用户状态枚举名(Active/Disabled/...)。</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>身份目录认证/缓存版本(用于失效判断,可空)。</summary>
    public string? AuthVersion { get; init; }
}
