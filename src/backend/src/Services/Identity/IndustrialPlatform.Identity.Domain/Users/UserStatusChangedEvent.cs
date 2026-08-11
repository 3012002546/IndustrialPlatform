using IndustrialPlatform.SharedKernel.Events;

namespace IndustrialPlatform.Identity.Domain.Users;

/// <summary>
/// 用户状态变更领域事件(集成映射 <c>Identity.UserStatusChanged.v1</c>)。
/// </summary>
public sealed class UserStatusChangedEvent : IDomainEvent
{
    /// <summary>租户编码。</summary>
    public string TenantNId { get; }

    /// <summary>用户业务标识。</summary>
    public string UserNId { get; }

    /// <summary>变更前状态。</summary>
    public UserStatus OldStatus { get; }

    /// <summary>变更后状态。</summary>
    public UserStatus NewStatus { get; }

    /// <summary>变更后的安全版本。</summary>
    public int AuthVersion { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOn { get; }

    /// <summary>
    /// 初始化用户状态变更事件。
    /// </summary>
    /// <param name="tenantNId">租户编码。</param>
    /// <param name="userNId">用户业务标识。</param>
    /// <param name="oldStatus">变更前状态。</param>
    /// <param name="newStatus">变更后状态。</param>
    /// <param name="authVersion">安全版本。</param>
    public UserStatusChangedEvent(
        string tenantNId,
        string userNId,
        UserStatus oldStatus,
        UserStatus newStatus,
        int authVersion)
    {
        TenantNId = tenantNId;
        UserNId = userNId;
        OldStatus = oldStatus;
        NewStatus = newStatus;
        AuthVersion = authVersion;
        OccurredOn = DateTimeOffset.UtcNow;
    }
}
