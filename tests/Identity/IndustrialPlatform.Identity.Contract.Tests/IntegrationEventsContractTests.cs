using System.Text.Json;
using System.Text.Json.Serialization;
using IndustrialPlatform.Identity.Contracts.Events;

namespace IndustrialPlatform.Identity.Contract.Tests;

/// <summary>
/// §20 集成事件契约测试:五类 v1 事件的线上形状(版本化 eventType + 公共信封字段)、
/// 敏感字段隔离(密码/Token/邮箱/电话/数据库主键/完整权限列表绝不入载荷)、
/// 与 BuildingBlocks RabbitMqEventBus 一致的序列化配置(小驼峰 + 忽略 null),以及 [JsonConstructor] 往返。
/// </summary>
public sealed class IntegrationEventsContractTests
{
    private const string Tenant = "development";

    private static readonly UserCreatedEvent UserCreated = new(Tenant, "alice.user", 3);
    private static readonly UserStatusChangedEvent UserStatusChanged = new(Tenant, "alice.user", "Active", "Disabled", 4);
    private static readonly UserSecurityChangedEvent UserSecurityChanged = new(Tenant, "alice.user", "PasswordChanged", 5);
    private static readonly UserRolesChangedEvent UserRolesChanged = new(Tenant, "alice.user", "role.operator");
    private static readonly RolePermissionsChangedEvent RolePermissionsChanged = new(Tenant, "role.operator", "permission.order.create");
    private static readonly UserGroupCreatedEvent UserGroupCreated = new(Tenant, "group.ops", "运维组", "Active");
    private static readonly UserGroupChangedEvent UserGroupChanged = new(Tenant, "group.ops", "运维组", "Disabled");
    private static readonly UserGroupMembershipChangedEvent UserGroupMembershipChanged = new(Tenant, "group.ops", "alice.user");
    private static readonly UserGroupRolesChangedEvent UserGroupRolesChanged = new(Tenant, "group.ops");

    public static TheoryData<IdentityIntegrationEvent, string> VersionedEventTypeSamples => new()
    {
        { UserCreated, "Identity.UserCreated.v1" },
        { UserStatusChanged, "Identity.UserStatusChanged.v1" },
        { UserSecurityChanged, "Identity.UserSecurityChanged.v1" },
        { UserRolesChanged, "Identity.UserRolesChanged.v1" },
        { RolePermissionsChanged, "Identity.RolePermissionsChanged.v1" },
        { UserGroupCreated, "Identity.UserGroupCreated.v1" },
        { UserGroupChanged, "Identity.UserGroupChanged.v1" },
        { UserGroupMembershipChanged, "Identity.UserGroupMembershipChanged.v1" },
        { UserGroupRolesChanged, "Identity.UserGroupRolesChanged.v1" },
    };

    public static TheoryData<IdentityIntegrationEvent> AllSampleEvents => new()
    {
        { UserCreated },
        { UserStatusChanged },
        { UserSecurityChanged },
        { UserRolesChanged },
        { RolePermissionsChanged },
        { UserGroupCreated },
        { UserGroupChanged },
        { UserGroupMembershipChanged },
        { UserGroupRolesChanged },
    };

    [Theory]
    [MemberData(nameof(VersionedEventTypeSamples))]
    public void SerializesWithVersionedEventTypeAndEnvelopeFields(IdentityIntegrationEvent integrationEvent, string expectedEventType)
    {
        var json = IntegrationEventJson.Serialize(integrationEvent);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(expectedEventType, root.GetProperty("eventType").GetString());
        Assert.Equal(1, root.GetProperty("eventVersion").GetInt32());
        Assert.Equal(Tenant, root.GetProperty("tenantNId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("subjectNId").GetString()));
        Assert.Equal(integrationEvent.EventId, root.GetProperty("eventId").GetGuid());
        Assert.True(root.TryGetProperty("createdTime", out _));
    }

    [Theory]
    [MemberData(nameof(AllSampleEvents))]
    public void DoesNotLeakSensitiveOrDatabaseFields(IdentityIntegrationEvent integrationEvent)
    {
        var json = IntegrationEventJson.Serialize(integrationEvent);
        var propertyNames = JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        // 禁止入载荷:密码/Token 相关、邮箱/电话、数据库主键、EventTypeName 泄漏与 CLR 事件类名。
        string[] forbidden = ["email", "phone", "password", "passwordHash", "token", "id", "userId", "roleId", "permissionId", "eventTypeName"];
        Assert.Empty(propertyNames.Intersect(forbidden, StringComparer.Ordinal));
        Assert.DoesNotContain(integrationEvent.GetType().Name, json);
    }

    [Fact]
    public void EventTypeNamesMatchSection20Contract()
    {
        string[] actual =
        [
            UserCreated.EventTypeName,
            UserStatusChanged.EventTypeName,
            UserSecurityChanged.EventTypeName,
            UserRolesChanged.EventTypeName,
            RolePermissionsChanged.EventTypeName,
        ];
        string[] expected =
        [
            "Identity.UserCreated.v1",
            "Identity.UserStatusChanged.v1",
            "Identity.UserSecurityChanged.v1",
            "Identity.UserRolesChanged.v1",
            "Identity.RolePermissionsChanged.v1",
        ];

        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void SerializerOptionsMatchEventBusContract()
    {
        Assert.Equal(JsonNamingPolicy.CamelCase, IntegrationEventJson.Options.PropertyNamingPolicy);
        Assert.Equal(JsonIgnoreCondition.WhenWritingNull, IntegrationEventJson.Options.DefaultIgnoreCondition);
    }

    [Fact]
    public void TraceIdSerializesWhenPresent()
    {
        var withTrace = new UserCreatedEvent(Tenant, "alice.user", 3) { TraceId = "test-trace-1" };
        using var document = JsonDocument.Parse(IntegrationEventJson.Serialize(withTrace));

        Assert.Equal("test-trace-1", document.RootElement.GetProperty("traceId").GetString());
    }

    [Fact]
    public void UserCreatedEvent_RoundTripsThroughJsonConstructor()
    {
        var restored = JsonSerializer.Deserialize<UserCreatedEvent>(IntegrationEventJson.Serialize(UserCreated), IntegrationEventJson.Options)!;

        Assert.Equal(UserCreated.EventId, restored.EventId);
        Assert.Equal(UserCreated.CreatedTime, restored.CreatedTime);
        Assert.Equal(UserCreated.TenantNId, restored.TenantNId);
        Assert.Equal(UserCreated.SubjectNId, restored.SubjectNId);
        Assert.Equal(UserCreated.AuthVersion, restored.AuthVersion);
    }

    [Fact]
    public void UserStatusChangedEvent_RoundTripsThroughJsonConstructor()
    {
        var restored = JsonSerializer.Deserialize<UserStatusChangedEvent>(IntegrationEventJson.Serialize(UserStatusChanged), IntegrationEventJson.Options)!;

        Assert.Equal(UserStatusChanged.TenantNId, restored.TenantNId);
        Assert.Equal(UserStatusChanged.SubjectNId, restored.SubjectNId);
        Assert.Equal(UserStatusChanged.OldStatus, restored.OldStatus);
        Assert.Equal(UserStatusChanged.NewStatus, restored.NewStatus);
        Assert.Equal(UserStatusChanged.AuthVersion, restored.AuthVersion);
    }

    [Fact]
    public void UserSecurityChangedEvent_RoundTripsThroughJsonConstructor()
    {
        var restored = JsonSerializer.Deserialize<UserSecurityChangedEvent>(IntegrationEventJson.Serialize(UserSecurityChanged), IntegrationEventJson.Options)!;

        Assert.Equal(UserSecurityChanged.TenantNId, restored.TenantNId);
        Assert.Equal(UserSecurityChanged.SubjectNId, restored.SubjectNId);
        Assert.Equal(UserSecurityChanged.Reason, restored.Reason);
        Assert.Equal(UserSecurityChanged.AuthVersion, restored.AuthVersion);
    }

    [Fact]
    public void UserRolesChangedEvent_RoundTripsThroughJsonConstructor()
    {
        var restored = JsonSerializer.Deserialize<UserRolesChangedEvent>(IntegrationEventJson.Serialize(UserRolesChanged), IntegrationEventJson.Options)!;

        Assert.Equal(UserRolesChanged.TenantNId, restored.TenantNId);
        Assert.Equal(UserRolesChanged.SubjectNId, restored.SubjectNId);
        Assert.Equal(UserRolesChanged.RoleNId, restored.RoleNId);
    }

    [Fact]
    public void RolePermissionsChangedEvent_RoundTripsThroughJsonConstructor()
    {
        var restored = JsonSerializer.Deserialize<RolePermissionsChangedEvent>(IntegrationEventJson.Serialize(RolePermissionsChanged), IntegrationEventJson.Options)!;

        Assert.Equal(RolePermissionsChanged.TenantNId, restored.TenantNId);
        Assert.Equal(RolePermissionsChanged.SubjectNId, restored.SubjectNId);
        Assert.Equal(RolePermissionsChanged.PermissionNId, restored.PermissionNId);
    }

    [Fact]
    public void UserGroupEventTypeNamesMatchContract()
    {
        string[] actual =
        [
            UserGroupCreated.EventTypeName,
            UserGroupChanged.EventTypeName,
            UserGroupMembershipChanged.EventTypeName,
            UserGroupRolesChanged.EventTypeName,
        ];
        string[] expected =
        [
            "Identity.UserGroupCreated.v1",
            "Identity.UserGroupChanged.v1",
            "Identity.UserGroupMembershipChanged.v1",
            "Identity.UserGroupRolesChanged.v1",
        ];

        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void UserGroupCreatedEvent_RoundTripsThroughJsonConstructor()
    {
        var restored = JsonSerializer.Deserialize<UserGroupCreatedEvent>(IntegrationEventJson.Serialize(UserGroupCreated), IntegrationEventJson.Options)!;

        Assert.Equal(UserGroupCreated.EventId, restored.EventId);
        Assert.Equal(UserGroupCreated.TenantNId, restored.TenantNId);
        Assert.Equal(UserGroupCreated.SubjectNId, restored.SubjectNId);
        Assert.Equal(UserGroupCreated.Name, restored.Name);
        Assert.Equal(UserGroupCreated.Status, restored.Status);
    }

    [Fact]
    public void UserGroupMembershipChangedEvent_RoundTripsThroughJsonConstructor()
    {
        var restored = JsonSerializer.Deserialize<UserGroupMembershipChangedEvent>(IntegrationEventJson.Serialize(UserGroupMembershipChanged), IntegrationEventJson.Options)!;

        Assert.Equal(UserGroupMembershipChanged.TenantNId, restored.TenantNId);
        Assert.Equal(UserGroupMembershipChanged.SubjectNId, restored.SubjectNId);
        Assert.Equal(UserGroupMembershipChanged.UserNId, restored.UserNId);
    }

    [Fact]
    public void UserGroupRolesChangedEvent_RoundTripsThroughJsonConstructor()
    {
        var restored = JsonSerializer.Deserialize<UserGroupRolesChangedEvent>(IntegrationEventJson.Serialize(UserGroupRolesChanged), IntegrationEventJson.Options)!;

        Assert.Equal(UserGroupRolesChanged.TenantNId, restored.TenantNId);
        Assert.Equal(UserGroupRolesChanged.SubjectNId, restored.SubjectNId);
    }
}
