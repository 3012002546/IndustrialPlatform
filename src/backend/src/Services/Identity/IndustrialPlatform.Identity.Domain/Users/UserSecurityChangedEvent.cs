using IndustrialPlatform.SharedKernel.Events;

namespace IndustrialPlatform.Identity.Domain.Users;

/// <summary>
/// 用户安全状态变更领域事件(集成映射 <c>Identity.UserSecurityChanged.v1</c>)。
/// 密码/登录名等影响会话安全的状态变化时发布。
/// </summary>
public sealed class UserSecurityChangedEvent : IDomainEvent
{
    /// <summary>租户编码。</summary>
    public string TenantNId { get; }

    /// <summary>用户业务标识。</summary>
    public string UserNId { get; }

    /// <summary>安全变更原因。</summary>
    public UserSecurityChangeReason Reason { get; }

    /// <summary>变更后的安全版本。</summary>
    public int AuthVersion { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOn { get; }

    /// <summary>
    /// 初始化用户安全状态变更事件。
    /// </summary>
    /// <param name="tenantNId">租户编码。</param>
    /// <param name="userNId">用户业务标识。</param>
    /// <param name="reason">安全变更原因。</param>
    /// <param name="authVersion">安全版本。</param>
    public UserSecurityChangedEvent(string tenantNId, string userNId, UserSecurityChangeReason reason, int authVersion)
    {
        TenantNId = tenantNId;
        UserNId = userNId;
        Reason = reason;
        AuthVersion = authVersion;
        OccurredOn = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// 用户安全变更原因。
/// </summary>
public enum UserSecurityChangeReason
{
    /// <summary>登录名变更。</summary>
    LoginNameChanged = 0,

    /// <summary>密码变更。</summary>
    PasswordChanged = 1,
}
