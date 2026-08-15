using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using IndustrialPlatform.SystemData.Contracts.Administration;

namespace IndustrialPlatform.SystemData.Contract.Tests;

/// <summary>
/// 行政组织/岗位/任职公开契约测试(TASK-SD-006,05 方案 §9.3):
/// 线上 JSON 形状(默认小驼峰、无 JsonPropertyName 覆盖)、状态/类型以枚举名字符串传输
/// (不依赖 JSON 数字)、请求属性一律可空(防 [ApiController] Required 推断)、
/// 请求体不携带租户/执行者标识(只从当前用户上下文读取,防租户伪造)、
/// 响应只暴露稳定 NId 与双并发版本(不暴露数据库 Guid 主键),以及敏感字段隔离。
/// </summary>
public sealed class AdministrationContractTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static readonly NullabilityInfoContext Nullability = new();

    private static IEnumerable<Type> AllContractTypes =>
        typeof(CreateOrganizationRequest).Assembly.GetTypes()
            .Where(type => type.Namespace == "IndustrialPlatform.SystemData.Contracts.Administration")
            .Where(type => type.IsClass)
            .OrderBy(type => type.Name);

    /// <summary>请求类型:属性一律可空,防 [ApiController] Required 推断破坏统一信封。</summary>
    private static readonly Type[] RequestTypes =
    [
        typeof(CreateOrganizationRequest),
        typeof(UpdateOrganizationRequest),
        typeof(MoveOrganizationRequest),
        typeof(MoveOrganizationPreviewRequest),
        typeof(SetOrganizationStatusRequest),
        typeof(CreatePositionRequest),
        typeof(UpdatePositionRequest),
        typeof(SetPositionStatusRequest),
        typeof(CreateAssignmentRequest),
        typeof(UpdateScheduledAssignmentRequest),
        typeof(CancelAssignmentRequest),
        typeof(SetPrimaryAssignmentRequest),
    ];

    /// <summary>响应类型:只暴露稳定 NId 与双并发版本,不暴露数据库 Guid 主键。</summary>
    private static readonly Type[] ResponseTypes =
    [
        typeof(OrganizationNodeV1),
        typeof(OrganizationDetailV1),
        typeof(OrganizationMovePreviewV1),
        typeof(PositionV1),
        typeof(AssignmentV1),
        typeof(IdentityUserDirectoryEntryV1),
    ];

    [Fact]
    public void OrganizationDetailV1_SerializesCamelCaseWithEnumNameStrings()
    {
        var detail = new OrganizationDetailV1
        {
            TenantNId = "tenant-001",
            NId = "org-001",
            Name = "总部",
            Type = "Company",
            Status = "Active",
            ParentOrganizationNId = null,
            DisplayOrder = 0,
            OrganizationRevision = 3,
            OptimisticVersion = 12,
            ConcurrencyVersion = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(detail, WebJson));
        var root = document.RootElement;

        Assert.Equal("tenant-001", root.GetProperty("tenantNId").GetString());
        Assert.Equal("org-001", root.GetProperty("nId").GetString());
        Assert.Equal("Company", root.GetProperty("type").GetString());
        Assert.Equal("Active", root.GetProperty("status").GetString());
        Assert.Equal(3, root.GetProperty("organizationRevision").GetInt64());
        Assert.Equal(12, root.GetProperty("optimisticVersion").GetInt64());
        Assert.Equal("11111111-2222-3333-4444-555555555555", root.GetProperty("concurrencyVersion").GetString());

        // 枚举以字符串传输,不依赖 JSON 数字;无 PascalCase 键泄漏。
        Assert.Equal(JsonValueKind.String, root.GetProperty("type").ValueKind);
        Assert.Equal(JsonValueKind.String, root.GetProperty("status").ValueKind);
        Assert.False(root.TryGetProperty("ConcurrencyVersion", out _));
    }

    [Fact]
    public void PositionV1_SerializesCamelCaseWithEnumNameString()
    {
        var position = new PositionV1
        {
            TenantNId = "tenant-001",
            NId = "pos-001",
            OrganizationNId = "org-001",
            OrganizationName = "总部",
            Name = "系统架构师",
            Description = "负责平台架构",
            Status = "Active",
            DisplayOrder = 1,
            OptimisticVersion = 5,
            ConcurrencyVersion = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(position, WebJson));
        var root = document.RootElement;

        Assert.Equal("pos-001", root.GetProperty("nId").GetString());
        Assert.Equal("org-001", root.GetProperty("organizationNId").GetString());
        Assert.Equal("系统架构师", root.GetProperty("name").GetString());
        Assert.Equal("Active", root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("status").ValueKind);
    }

    [Fact]
    public void AssignmentV1_SerializesTimeWindowWithInstantPreservation()
    {
        var from = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
        var assignment = new AssignmentV1
        {
            TenantNId = "tenant-001",
            NId = "asn-001",
            UserNId = "user-001",
            UserDisplayNameSnapshot = "张三",
            OrganizationNId = "org-001",
            PositionNId = "pos-001",
            PositionName = "系统架构师",
            IsPrimary = true,
            EffectiveFrom = from,
            EffectiveTo = to,
            State = "Current",
            OptimisticVersion = 7,
            ConcurrencyVersion = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(assignment, WebJson));
        var root = document.RootElement;

        Assert.Equal("asn-001", root.GetProperty("nId").GetString());
        Assert.Equal("user-001", root.GetProperty("userNId").GetString());
        Assert.Equal(JsonValueKind.True, root.GetProperty("isPrimary").ValueKind);
        Assert.Equal("Current", root.GetProperty("state").GetString());

        // 时间以 ISO 8601 偏移传输并保留瞬时(GetDateTimeOffset 不转换为本地墙钟)。
        var effectiveFrom = root.GetProperty("effectiveFrom").GetDateTimeOffset();
        Assert.Equal(from, effectiveFrom);
        var effectiveTo = root.GetProperty("effectiveTo").GetDateTimeOffset();
        Assert.Equal(to, effectiveTo);
    }

    [Fact]
    public void AssignmentV1_UnboundedEffectiveToSerializesAsNull()
    {
        // API 使用 AddControllers 默认 JsonOptions(不忽略 null 写出),无界区间
        // 线上为 effectiveTo:null,客户端按可空语义解读(与 Api.Tests 实测一致)。
        var assignment = new AssignmentV1
        {
            TenantNId = "tenant-001",
            NId = "asn-002",
            UserNId = "user-001",
            UserDisplayNameSnapshot = "张三",
            OrganizationNId = "org-001",
            PositionNId = "pos-001",
            PositionName = "系统架构师",
            IsPrimary = false,
            EffectiveFrom = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            EffectiveTo = null,
            State = "Current",
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(assignment, WebJson));
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("effectiveFrom", out _));
        Assert.True(root.TryGetProperty("effectiveTo", out var effectiveTo));
        Assert.Equal(JsonValueKind.Null, effectiveTo.ValueKind);
    }

    [Fact]
    public void OrganizationMovePreviewV1_CarriesRevisionAndDualVersions()
    {
        // 移动提交必须回传预览的修订与双版本,契约上锁定该载荷。
        var preview = new OrganizationMovePreviewV1
        {
            NId = "org-001",
            OrganizationRevision = 4,
            SubtreeOrganizationCount = 3,
            SubtreePositionCount = 5,
            SubtreeAssignmentCount = 7,
            AffectedCount = 15,
            PreviewedOn = new DateTimeOffset(2026, 8, 15, 8, 0, 0, TimeSpan.Zero),
            ExpectedOptimisticVersion = 12,
            ExpectedConcurrencyVersion = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(preview, WebJson));
        var root = document.RootElement;

        Assert.Equal(4, root.GetProperty("organizationRevision").GetInt64());
        Assert.Equal(3, root.GetProperty("subtreeOrganizationCount").GetInt64());
        Assert.Equal(15, root.GetProperty("affectedCount").GetInt64());
        Assert.Equal(12, root.GetProperty("expectedOptimisticVersion").GetInt64());
        Assert.Equal("11111111-2222-3333-4444-555555555555", root.GetProperty("expectedConcurrencyVersion").GetString());
    }

    [Fact]
    public void NoDtoUsesJsonPropertyNameOrConverterOverrides()
    {
        foreach (var type in AllContractTypes)
        {
            foreach (var property in PublicInstanceProperties(type))
            {
                Assert.Null(property.GetCustomAttribute<JsonPropertyNameAttribute>());
                Assert.Null(property.GetCustomAttribute<JsonConverterAttribute>());
            }
        }
    }

    [Fact]
    public void RequestDtoPropertiesAreNullableToAvoidRequiredInference()
    {
        foreach (var type in RequestTypes)
        {
            foreach (var property in PublicInstanceProperties(type))
            {
                var nullability = Nullability.Create(property);
                Assert.True(
                    nullability.ReadState == NullabilityState.Nullable,
                    $"{type.Name}.{property.Name} 必须声明为可空,防止 [ApiController] Required 推断破坏统一信封。");
            }
        }
    }

    [Fact]
    public void RequestDtosDoNotCarryTenantOrActorIdentifiers()
    {
        // 租户与执行者标识只从当前用户上下文读取,请求体禁止携带(防客户端伪造)。
        foreach (var type in RequestTypes)
        {
            foreach (var property in PublicInstanceProperties(type))
            {
                Assert.True(
                    property.Name is not ("TenantNId" or "ActorUserNId" or "tenantNId" or "actorUserNId"),
                    $"{type.Name}.{property.Name} 疑似携带租户/执行者标识,禁止出现在请求契约。");
            }
        }
    }

    [Fact]
    public void ResponseDtosExposeOnlyNIdAndDualVersionsNoDatabaseGuid()
    {
        // 响应不暴露数据库 Guid 主键:Guid 属性只允许 ConcurrencyVersion 双版本之一,
        // 且不得出现 Id/PersistenceId 命名的数据库主键属性。
        foreach (var type in ResponseTypes)
        {
            foreach (var property in PublicInstanceProperties(type))
            {
                if (property.PropertyType == typeof(Guid))
                {
                    // Guid 只允许双并发版本(ConcurrencyVersion / 移动预览 ExpectedConcurrencyVersion)。
                    Assert.True(
                        property.Name.EndsWith("Version", StringComparison.Ordinal),
                        $"{type.Name}.{property.Name} 疑似暴露数据库 Guid 主键,禁止出现在公开契约。");
                }

                Assert.True(
                    property.Name is not ("Id" or "PersistenceId"),
                    $"{type.Name}.{property.Name} 疑似暴露数据库主键,禁止出现在公开契约。");
            }
        }
    }

    [Fact]
    public void ResponseEnumLikeFieldsAreStringsNotEnums()
    {
        // 状态/类型/任职状态以枚举名字符串传输,禁止值类型枚举泄漏 JSON 数字。
        var enumLikeFields = new (Type Type, string[] Fields)[]
        {
            (typeof(OrganizationNodeV1), ["Type", "Status"]),
            (typeof(OrganizationDetailV1), ["Type", "Status"]),
            (typeof(PositionV1), ["Status"]),
            (typeof(AssignmentV1), ["State"]),
        };

        foreach (var (type, fields) in enumLikeFields)
        {
            foreach (var field in fields)
            {
                Assert.Equal(typeof(string), type.GetProperty(field)!.PropertyType);
            }
        }
    }

    [Fact]
    public void NoDtoExposesSensitiveFieldNames()
    {
        string[] forbidden = ["Secret", "Password", "Token", "ConnectionString"];

        foreach (var type in AllContractTypes)
        {
            foreach (var property in PublicInstanceProperties(type))
            {
                Assert.True(
                    forbidden.All(needle => !property.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)),
                    $"{type.Name}.{property.Name} 疑似敏感字段,禁止出现在公开契约。");
            }
        }
    }

    [Fact]
    public void SerializedPayloadsDoNotLeakSensitiveValues()
    {
        string[] forbidden = ["password", "secret", "token", "connectionstring"];

        foreach (var type in AllContractTypes)
        {
            var instance = Activator.CreateInstance(type);
            if (instance is null)
            {
                continue;
            }

            foreach (var property in PublicInstanceProperties(type).Where(property => property.PropertyType == typeof(string)))
            {
                property.SetValue(instance, "REDACT-FOR-SCAN");
            }

            var json = JsonSerializer.Serialize(instance, WebJson).ToLowerInvariant();
            foreach (var needle in forbidden)
            {
                Assert.False(json.Contains(needle, StringComparison.Ordinal), $"{type.Name} 序列化结果疑似泄漏敏感值:{needle}。");
            }
        }
    }

    private static PropertyInfo[] PublicInstanceProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
}
