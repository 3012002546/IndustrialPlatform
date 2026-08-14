using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using IndustrialPlatform.SystemData.Contracts.DatabaseOrchestration;

namespace IndustrialPlatform.SystemData.Contract.Tests;

/// <summary>
/// 数据库编排公开契约测试(TASK-SD-002):线上 JSON 形状(默认小驼峰、无 JsonPropertyName 覆盖)、
/// 枚举以枚举名字符串传输、请求属性一律可空(防 [ApiController] Required 推断)、
/// 请求不含环境标识(环境由服务端可信拓扑解析),以及敏感字段隔离
/// (Secret/Password/Token/ConnectionString 一律不得出现在契约 DTO)。
/// </summary>
public sealed class DatabaseOrchestrationContractTests
{
    private const string Sha = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static readonly NullabilityInfoContext Nullability = new();

    private static IEnumerable<Type> AllContractTypes =>
        typeof(DatabaseRegistrationV1).Assembly.GetTypes()
            .Where(type => type.Namespace == "IndustrialPlatform.SystemData.Contracts.DatabaseOrchestration")
            .Where(type => type.IsClass)
            .OrderBy(type => type.Name);

    private static readonly Type[] RequestTypes =
    [
        typeof(DatabaseRegistrationManifestV1),
        typeof(DatabasePlanRequestV1),
        typeof(DatabaseApplyRequestV1),
        typeof(DatabaseApprovalRequestV1),
        typeof(DatabaseBackupEvidenceRequestV1),
    ];

    [Fact]
    public void SerializesWithDefaultCamelCaseAndEnumNameStrings()
    {
        var registration = new DatabaseRegistrationV1
        {
            TenantNId = "tenant-001",
            ServiceKey = "systemdata",
            Provider = "Sqlite",
            LogicalDatabaseName = "systemdata_db",
            DesiredState = "SourceOfTruth",
            Status = "Registered",
            TopologyRevision = Sha,
            ArtifactChecksum = Sha,
            ManifestChecksum = Sha,
            RegisteredOn = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(registration, WebJson));
        var root = document.RootElement;

        Assert.Equal("tenant-001", root.GetProperty("tenantNId").GetString());
        Assert.Equal("systemdata", root.GetProperty("serviceKey").GetString());
        Assert.Equal("SourceOfTruth", root.GetProperty("desiredState").GetString());
        Assert.Equal("Registered", root.GetProperty("status").GetString());
        Assert.Equal(Sha, root.GetProperty("topologyRevision").GetString());

        // 枚举名以字符串传输,不依赖 JSON 数字;无 PascalCase 键泄漏。
        Assert.Equal(JsonValueKind.String, root.GetProperty("desiredState").ValueKind);
        Assert.False(root.TryGetProperty("DesiredState", out _));
    }

    [Fact]
    public void EnqueueOperationV1_SerializesEnvelopeShape()
    {
        var enqueue = new EnqueueOperationV1
        {
            OperationNId = "OP-001",
            Kind = "Apply",
            Status = "Queued",
            Phase = "Validate",
            AcceptedOn = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(enqueue, WebJson));
        var root = document.RootElement;

        Assert.Equal("OP-001", root.GetProperty("operationNId").GetString());
        Assert.Equal("Apply", root.GetProperty("kind").GetString());
        Assert.Equal("Queued", root.GetProperty("status").GetString());
        Assert.Equal("Validate", root.GetProperty("phase").GetString());
        Assert.True(root.TryGetProperty("acceptedOn", out _));
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
    public void RequestDtosDoNotCarryEnvironmentNId()
    {
        // 环境(NId)由服务端可信拓扑解析,请求体不含环境标识(防客户端伪造)。
        foreach (var type in RequestTypes)
        {
            Assert.Null(type.GetProperty("EnvironmentNId", BindingFlags.Public | BindingFlags.Instance));
            Assert.Null(type.GetProperty("environmentNId", BindingFlags.Public | BindingFlags.Instance));
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
