using System.Text.Json;
using System.Text.Json.Serialization;
using IndustrialPlatform.SystemData.Contracts.DatabaseOrchestration;

namespace IndustrialPlatform.SystemData.Contract.Tests;

/// <summary>
/// 服务初始化契约 v2 测试(TASK-SD-004):清单/种子声明、plan/apply 请求、seed 观察与
/// 模块级 readiness 的线上 JSON 形状,以及 v2 契约不含敏感字段名/值。
/// </summary>
public sealed class ServiceInitializationContractTests
{
    private const string Sha = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ManifestV2_SerializesModuleAndSeedSets()
    {
        var manifest = new ServiceInitializationManifestV2
        {
            ServiceKey = "identity",
            ModuleKey = "identity-module",
            LogicalDatabaseName = "identity_db",
            MigrationArtifactId = "identity-migrations",
            RequestedVersion = "2026.08.1",
            ArtifactChecksum = Sha,
            DesiredState = "SourceOfTruth",
            AutoMigrate = true,
            SeedSets =
            [
                new SeedSetV1
                {
                    SeedKey = "sys-permission-catalog",
                    SeedVersion = "2026.08.1",
                    SeedClass = "SystemBaseline",
                    Scope = "system",
                    SeedArtifactId = "identity-permission-seeds",
                    SeedChecksum = Sha,
                    RequiredForReadiness = true,
                    AllowedEnvironments = "Development,Test,Staging,Production",
                },
            ],
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(manifest, WebJson));
        var root = document.RootElement;

        Assert.Equal("identity", root.GetProperty("serviceKey").GetString());
        Assert.Equal("identity-module", root.GetProperty("moduleKey").GetString());
        Assert.Equal("SourceOfTruth", root.GetProperty("desiredState").GetString());
        Assert.Equal(JsonValueKind.True, root.GetProperty("autoMigrate").ValueKind);

        var seed = root.GetProperty("seedSets")[0];
        Assert.Equal("sys-permission-catalog", seed.GetProperty("seedKey").GetString());
        Assert.Equal("SystemBaseline", seed.GetProperty("seedClass").GetString());
        Assert.Equal("Development,Test,Staging,Production", seed.GetProperty("allowedEnvironments").GetString());
        Assert.Equal(JsonValueKind.True, seed.GetProperty("requiredForReadiness").ValueKind);
        Assert.False(seed.TryGetProperty("SecretKey", out _));
    }

    [Fact]
    public void ManifestV2_DoesNotCarryEnvironmentNIdOrSensitiveFields()
    {
        foreach (var property in typeof(ServiceInitializationManifestV2).GetProperties())
        {
            Assert.NotEqual("EnvironmentNId", property.Name);
            Assert.False(
                property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("ConnectionString", StringComparison.OrdinalIgnoreCase),
                $"{property.Name} 疑似敏感字段,禁止出现在公开契约。");
        }
    }

    [Fact]
    public void ReadinessV2_SerializesModuleLevelBlocks()
    {
        var readiness = new ServiceInitializationReadinessV2
        {
            ServiceKey = "identity",
            ModuleKey = "identity-module",
            LogicalDatabaseName = "identity_db",
            PhysicalDatabaseTarget = "industrial***dev",
            DatabaseIdentityFingerprint = Sha,
            ArtifactChecksum = Sha,
            DesiredMigrationVersion = "2026.08.1",
            ObservedMigrationVersion = "2026.08.1",
            TopologyRevision = Sha,
            MigrationReady = true,
            RequiredSeedReady = true,
            BootstrapReady = true,
            BootstrapStatus = "Ready",
            Ready = true,
            Status = "Ready",
            Seeds =
            [
                new SeedReadinessV2
                {
                    SeedKey = "sys-permission-catalog",
                    SeedVersion = "2026.08.1",
                    Status = "Applied",
                },
            ],
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(readiness, WebJson));
        var root = document.RootElement;

        Assert.Equal("identity", root.GetProperty("serviceKey").GetString());
        Assert.Equal("identity-module", root.GetProperty("moduleKey").GetString());
        Assert.Equal(JsonValueKind.True, root.GetProperty("migrationReady").ValueKind);
        Assert.Equal(JsonValueKind.True, root.GetProperty("requiredSeedReady").ValueKind);
        Assert.Equal(JsonValueKind.True, root.GetProperty("bootstrapReady").ValueKind);
        Assert.Equal("Ready", root.GetProperty("bootstrapStatus").GetString());
        Assert.Equal("Ready", root.GetProperty("status").GetString());
        Assert.Equal("Applied", root.GetProperty("seeds")[0].GetProperty("status").GetString());
    }

    [Fact]
    public void ReadinessV2_BootstrapStatus_IsAdditiveAndNonSensitive()
    {
        // TASK-ID-019 最小兼容扩展:bootstrapStatus 只描述状态,不携带 Secret/引用。
        var pending = new ServiceInitializationReadinessV2
        {
            ServiceKey = "identity",
            ModuleKey = "identity",
            BootstrapReady = false,
            BootstrapStatus = "Pending",
            Ready = false,
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(pending, WebJson));
        var root = document.RootElement;

        Assert.Equal("Pending", root.GetProperty("bootstrapStatus").GetString());
        Assert.False(root.GetProperty("bootstrapReady").GetBoolean());

        // 属性名不含敏感词(契约扫描:Secret/Password/Token/ConnectionString)。
        Assert.False(
            root.TryGetProperty("recoveryReference", out _)
            || root.TryGetProperty("temporaryPassword", out _));
    }

    [Fact]
    public void ReadinessV2_BootstrapStatusAbsent_RemainsV1Compatible()
    {
        // 缺省(migration-only 场景)时 bootstrapStatus 为 null,字段是纯增量扩展,不改变 v1 消费语义。
        var readiness = new ServiceInitializationReadinessV2
        {
            ServiceKey = "systemdata",
            ModuleKey = "systemdata",
            Ready = true,
            Status = "Ready",
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(readiness, WebJson));
        var root = document.RootElement;

        if (root.TryGetProperty("bootstrapStatus", out var value))
        {
            Assert.Equal(JsonValueKind.Null, value.ValueKind);
        }
    }

    [Fact]
    public void SeedObservationV1_SerializesSanitizedObservation()
    {
        var observation = new SeedObservationV1
        {
            TenantNId = "tenant-001",
            EnvironmentNId = "development",
            ServiceKey = "identity",
            ModuleKey = "identity-module",
            SeedKey = "sys-permission-catalog",
            SeedVersion = "2026.08.1",
            Checksum = Sha,
            Scope = "system",
            Status = "Applied",
            OperationNId = "OP-001",
            VerificationStatus = "Verified",
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(observation, WebJson));
        var root = document.RootElement;

        Assert.Equal("OP-001", root.GetProperty("operationNId").GetString());
        Assert.Equal("Applied", root.GetProperty("status").GetString());
        Assert.Equal("Verified", root.GetProperty("verificationStatus").GetString());
        // 脱敏:不含种子内容、连接串、路径或 Secret 值。
        Assert.False(document.RootElement.TryGetProperty("connectionString", out _));
    }

    [Fact]
    public void RegistrationV1_CarriesAdditiveModuleAndSeedSets()
    {
        // 增量扩展不破坏既有 v1 形状:migration-only 场景 moduleKey=serviceKey、seedSets 可空。
        var registration = new DatabaseRegistrationV1
        {
            ServiceKey = "systemdata",
            ModuleKey = "systemdata",
            Status = "Registered",
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(registration, WebJson));
        var root = document.RootElement;

        Assert.Equal("systemdata", root.GetProperty("serviceKey").GetString());
        Assert.Equal("systemdata", root.GetProperty("moduleKey").GetString());
        Assert.Equal("Registered", root.GetProperty("status").GetString());
    }

    [Fact]
    public void NoNewDtoUsesJsonPropertyNameOrConverterOverrides()
    {
        Type[] v2Types =
        [
            typeof(ServiceInitializationManifestV2),
            typeof(SeedSetV1),
            typeof(ServiceInitializationPlanRequestV2),
            typeof(ServiceInitializationApplyRequestV2),
            typeof(SeedObservationV1),
            typeof(SeedReadinessV2),
            typeof(ServiceInitializationReadinessV2),
        ];

        foreach (var type in v2Types)
        {
            foreach (var property in type.GetProperties())
            {
                Assert.Null(property.GetCustomAttributes(typeof(JsonPropertyNameAttribute), inherit: true).SingleOrDefault());
                Assert.Null(property.GetCustomAttributes(typeof(JsonConverterAttribute), inherit: true).SingleOrDefault());
            }
        }
    }
}
