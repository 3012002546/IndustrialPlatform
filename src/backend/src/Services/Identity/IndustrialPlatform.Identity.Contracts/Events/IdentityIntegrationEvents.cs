using System.Text.Json;
using System.Text.Json.Serialization;
using IndustrialPlatform.EventBus.Events;

namespace IndustrialPlatform.Identity.Contracts.Events;

/// <summary>
/// Identity v1 集成事件基类(§20)。公共信封字段:eventId / eventType / eventVersion /
/// createdTime / tenantNId / subjectNId / traceId。载荷只含业务标识、状态、版本与下游必要摘要,
/// 禁止数据库主键、密码、Token、邮箱、电话与完整权限列表。
/// <c>EventTypeName</c> 提供 §20 版本化事件名(如 <c>Identity.UserCreated.v1</c>);
/// 各具体事件 override 虚成员 <see cref="IntegrationEvent.EventType"/> 使线上 eventType 与
/// 发布路由键(RabbitMqEventBus 经基类引用读取)均携带版本化名称。
/// </summary>
public abstract class IdentityIntegrationEvent : IntegrationEvent
{
    /// <summary>v1 事件版本。</summary>
    public const int Version = 1;

    /// <summary>事件版本,序列化为 eventVersion。</summary>
    public int EventVersion => Version;

    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; init; }

    /// <summary>事件主体业务标识,按事件语义固定为 UserNId 或 RoleNId。</summary>
    public string SubjectNId { get; init; }

    /// <summary>§20 版本化事件名(如 <c>Identity.UserCreated.v1</c>),用于 Outbox 路由与线上 eventType。</summary>
    [JsonIgnore]
    public abstract string EventTypeName { get; }

    /// <summary>
    /// 初始化公共信封字段。eventId / createdTime / traceId 由 <see cref="IntegrationEvent"/> 生成。
    /// </summary>
    /// <param name="tenantNId">租户业务标识。</param>
    /// <param name="subjectNId">事件主体业务标识。</param>
    protected IdentityIntegrationEvent(string tenantNId, string subjectNId)
    {
        TenantNId = tenantNId;
        SubjectNId = subjectNId;
    }
}

/// <summary>
/// 用户创建集成事件(<c>Identity.UserCreated.v1</c>)。subjectNId 为 UserNId。
/// </summary>
public sealed class UserCreatedEvent : IdentityIntegrationEvent
{
    /// <inheritdoc/>
    [JsonIgnore]
    public override string EventTypeName => "Identity.UserCreated.v1";

    /// <summary>线上事件类型名,重写基类以携带版本化名称。</summary>
    public override string EventType => EventTypeName;

    /// <summary>创建时的安全版本。</summary>
    public int AuthVersion { get; }

    /// <summary>初始化用户创建事件。</summary>
    [JsonConstructor]
    public UserCreatedEvent(string tenantNId, string subjectNId, int authVersion)
        : base(tenantNId, subjectNId)
    {
        AuthVersion = authVersion;
    }
}

/// <summary>
/// 用户状态变更集成事件(<c>Identity.UserStatusChanged.v1</c>)。subjectNId 为 UserNId。
/// 状态值为 <c>Active</c> / <c>Disabled</c>,与领域用户状态字符串语义一致。
/// </summary>
public sealed class UserStatusChangedEvent : IdentityIntegrationEvent
{
    /// <inheritdoc/>
    [JsonIgnore]
    public override string EventTypeName => "Identity.UserStatusChanged.v1";

    /// <summary>线上事件类型名。</summary>
    public override string EventType => EventTypeName;

    /// <summary>变更前状态。</summary>
    public string OldStatus { get; }

    /// <summary>变更后状态。</summary>
    public string NewStatus { get; }

    /// <summary>变更后的安全版本。</summary>
    public int AuthVersion { get; }

    /// <summary>初始化用户状态变更事件。</summary>
    [JsonConstructor]
    public UserStatusChangedEvent(string tenantNId, string subjectNId, string oldStatus, string newStatus, int authVersion)
        : base(tenantNId, subjectNId)
    {
        OldStatus = oldStatus;
        NewStatus = newStatus;
        AuthVersion = authVersion;
    }
}

/// <summary>
/// 用户安全状态变更集成事件(<c>Identity.UserSecurityChanged.v1</c>)。subjectNId 为 UserNId。
/// 密码/登录名等影响会话安全的状态变化时发布;reason 值为 <c>LoginNameChanged</c> / <c>PasswordChanged</c>。
/// </summary>
public sealed class UserSecurityChangedEvent : IdentityIntegrationEvent
{
    /// <inheritdoc/>
    [JsonIgnore]
    public override string EventTypeName => "Identity.UserSecurityChanged.v1";

    /// <summary>线上事件类型名。</summary>
    public override string EventType => EventTypeName;

    /// <summary>安全变更原因。</summary>
    public string Reason { get; }

    /// <summary>变更后的安全版本。</summary>
    public int AuthVersion { get; }

    /// <summary>初始化用户安全状态变更事件。</summary>
    [JsonConstructor]
    public UserSecurityChangedEvent(string tenantNId, string subjectNId, string reason, int authVersion)
        : base(tenantNId, subjectNId)
    {
        Reason = reason;
        AuthVersion = authVersion;
    }
}

/// <summary>
/// 用户角色变更集成事件(<c>Identity.UserRolesChanged.v1</c>)。subjectNId 为 UserNId,roleNId 为角色业务标识。
/// 权限缓存失效信号,不含完整角色列表或数据库主键。
/// </summary>
public sealed class UserRolesChangedEvent : IdentityIntegrationEvent
{
    /// <inheritdoc/>
    [JsonIgnore]
    public override string EventTypeName => "Identity.UserRolesChanged.v1";

    /// <summary>线上事件类型名。</summary>
    public override string EventType => EventTypeName;

    /// <summary>角色业务标识。</summary>
    public string RoleNId { get; }

    /// <summary>初始化用户角色变更事件。</summary>
    [JsonConstructor]
    public UserRolesChangedEvent(string tenantNId, string subjectNId, string roleNId)
        : base(tenantNId, subjectNId)
    {
        RoleNId = roleNId;
    }
}

/// <summary>
/// 角色权限变更集成事件(<c>Identity.RolePermissionsChanged.v1</c>)。subjectNId 为 RoleNId,
/// permissionNId 为权限业务标识。权限缓存失效信号,不含完整权限列表或数据库主键。
/// </summary>
public sealed class RolePermissionsChangedEvent : IdentityIntegrationEvent
{
    /// <inheritdoc/>
    [JsonIgnore]
    public override string EventTypeName => "Identity.RolePermissionsChanged.v1";

    /// <summary>线上事件类型名。</summary>
    public override string EventType => EventTypeName;

    /// <summary>权限业务标识。</summary>
    public string PermissionNId { get; }

    /// <summary>初始化角色权限变更事件。</summary>
    [JsonConstructor]
    public RolePermissionsChangedEvent(string tenantNId, string subjectNId, string permissionNId)
        : base(tenantNId, subjectNId)
    {
        PermissionNId = permissionNId;
    }
}

/// <summary>
/// 用户组创建集成事件(<c>Identity.UserGroupCreated.v1</c>)。subjectNId 为 UserGroupNId。
/// 载荷只含租户、组 NId 与非敏感资料摘要,不含数据库主键。
/// </summary>
public sealed class UserGroupCreatedEvent : IdentityIntegrationEvent
{
    /// <inheritdoc/>
    [JsonIgnore]
    public override string EventTypeName => "Identity.UserGroupCreated.v1";

    /// <summary>线上事件类型名。</summary>
    public override string EventType => EventTypeName;

    /// <summary>用户组名称。</summary>
    public string Name { get; }

    /// <summary>用户组状态(<c>Active</c> / <c>Disabled</c>)。</summary>
    public string Status { get; }

    /// <summary>初始化用户组创建事件。</summary>
    [JsonConstructor]
    public UserGroupCreatedEvent(string tenantNId, string subjectNId, string name, string status)
        : base(tenantNId, subjectNId)
    {
        Name = name;
        Status = status;
    }
}

/// <summary>
/// 用户组资料/状态变更集成事件(<c>Identity.UserGroupChanged.v1</c>)。subjectNId 为 UserGroupNId。
/// 载荷只含租户、组 NId 与非敏感资料摘要,不含数据库主键。
/// </summary>
public sealed class UserGroupChangedEvent : IdentityIntegrationEvent
{
    /// <inheritdoc/>
    [JsonIgnore]
    public override string EventTypeName => "Identity.UserGroupChanged.v1";

    /// <summary>线上事件类型名。</summary>
    public override string EventType => EventTypeName;

    /// <summary>用户组名称。</summary>
    public string Name { get; }

    /// <summary>用户组状态(<c>Active</c> / <c>Disabled</c>)。</summary>
    public string Status { get; }

    /// <summary>初始化用户组资料/状态变更事件。</summary>
    [JsonConstructor]
    public UserGroupChangedEvent(string tenantNId, string subjectNId, string name, string status)
        : base(tenantNId, subjectNId)
    {
        Name = name;
        Status = status;
    }
}

/// <summary>
/// 用户组成员变更集成事件(<c>Identity.UserGroupMembershipChanged.v1</c>)。subjectNId 为 UserGroupNId,
/// userNId 为该次加入/移出的受影响用户业务标识。不含数据库主键。
/// </summary>
public sealed class UserGroupMembershipChangedEvent : IdentityIntegrationEvent
{
    /// <inheritdoc/>
    [JsonIgnore]
    public override string EventTypeName => "Identity.UserGroupMembershipChanged.v1";

    /// <summary>线上事件类型名。</summary>
    public override string EventType => EventTypeName;

    /// <summary>受影响的成员用户业务标识。</summary>
    public string UserNId { get; }

    /// <summary>初始化用户组成员变更事件。</summary>
    [JsonConstructor]
    public UserGroupMembershipChangedEvent(string tenantNId, string subjectNId, string userNId)
        : base(tenantNId, subjectNId)
    {
        UserNId = userNId;
    }
}

/// <summary>
/// 用户组角色变更集成事件(<c>Identity.UserGroupRolesChanged.v1</c>)。subjectNId 为 UserGroupNId。
/// 受影响用户为组内全部成员,事件以组 NId 作为批次引用;平台自身的授权版本推进/缓存失效/
/// 会话撤销由应用层同步完成。不含数据库主键。
/// </summary>
public sealed class UserGroupRolesChangedEvent : IdentityIntegrationEvent
{
    /// <inheritdoc/>
    [JsonIgnore]
    public override string EventTypeName => "Identity.UserGroupRolesChanged.v1";

    /// <summary>线上事件类型名。</summary>
    public override string EventType => EventTypeName;

    /// <summary>初始化用户组角色变更事件。</summary>
    [JsonConstructor]
    public UserGroupRolesChangedEvent(string tenantNId, string subjectNId)
        : base(tenantNId, subjectNId)
    {
    }
}

/// <summary>
/// 集成事件 JSON 序列化助手:与 BuildingBlocks <c>RabbitMqEventBus</c> 一致的小驼峰、忽略 null 配置。
/// 保证 Outbox 载荷与直发载荷字节语义一致(序列化兼容)。
/// </summary>
public static class IntegrationEventJson
{
    /// <summary>共享的序列化选项。</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>按运行时具体事件类型序列化为 JSON(保留各事件的遮蔽 eventType 与载荷字段)。</summary>
    public static string Serialize<TEvent>(TEvent integrationEvent)
        where TEvent : IntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return JsonSerializer.Serialize(integrationEvent, Options);
    }
}
