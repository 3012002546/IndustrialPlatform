using IndustrialPlatform.SharedKernel.Events;

namespace IndustrialPlatform.Identity.Domain.Users;

/// <summary>
/// 用户创建领域事件(集成映射 <c>Identity.UserCreated.v1</c>)。
/// </summary>
public sealed class UserCreatedEvent : IDomainEvent
{
    /// <summary>租户编码。</summary>
    public string TenantNId { get; }

    /// <summary>用户业务标识。</summary>
    public string UserNId { get; }

    /// <summary>登录名。</summary>
    public string LoginName { get; }

    /// <summary>创建时的安全版本。</summary>
    public int AuthVersion { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOn { get; }

    /// <summary>
    /// 初始化用户创建事件。
    /// </summary>
    /// <param name="tenantNId">租户编码。</param>
    /// <param name="userNId">用户业务标识。</param>
    /// <param name="loginName">登录名。</param>
    /// <param name="authVersion">安全版本。</param>
    public UserCreatedEvent(string tenantNId, string userNId, string loginName, int authVersion)
    {
        TenantNId = tenantNId;
        UserNId = userNId;
        LoginName = loginName;
        AuthVersion = authVersion;
        OccurredOn = DateTimeOffset.UtcNow;
    }
}
