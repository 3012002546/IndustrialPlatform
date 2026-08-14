using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Domain.Topology;

namespace IndustrialPlatform.SystemData.Domain.Tests;

/// <summary>
/// 数据库拓扑解析器规则测试(05 方案 §2.3/§7.1):Shared 仅 Development、
/// Shared 必须提供目标名、PerService 优先显式映射缺失回退逻辑名。
/// </summary>
public sealed class DatabaseTopologyResolverTests
{
    private static DatabaseTopology SharedPostgres(string? sharedName = "industrial_platform_dev") =>
        new("Development", DatabaseTopologyMode.Shared, sharedName, null, new Dictionary<string, string>());

    private static DatabaseTopology SharedSqlite(string? sharedFile = "industrial-platform.db") =>
        new("Development", DatabaseTopologyMode.Shared, null, sharedFile, new Dictionary<string, string>());

    private static DatabaseTopology PerService(IReadOnlyDictionary<string, string> mapping) =>
        new("Production", DatabaseTopologyMode.PerService, null, null, mapping);

    [Fact]
    public void Resolve_SharedPostgres_UsesSharedDatabaseName()
    {
        var target = DatabaseTopologyResolver.Resolve(
            SharedPostgres(), "systemdata", DatabaseProvider.PostgreSQL, "systemdata_db");

        Assert.Equal("industrial_platform_dev", target.PhysicalDatabaseName);
        Assert.Equal("systemdata_db", target.LogicalDatabaseName);
        Assert.True(target.IsSharedPhysicalDatabase);
        Assert.Equal(DatabaseTopologyMode.Shared, target.Mode);
    }

    [Fact]
    public void Resolve_SharedSqlite_UsesSharedSqliteFile()
    {
        var target = DatabaseTopologyResolver.Resolve(
            SharedSqlite(), "systemdata", DatabaseProvider.Sqlite, "systemdata_db");

        Assert.Equal("industrial-platform.db", target.PhysicalDatabaseName);
        Assert.True(target.IsSharedPhysicalDatabase);
    }

    [Fact]
    public void Resolve_SharedInNonDevelopment_ThrowsBusinessException()
    {
        var topology = new DatabaseTopology(
            "Production", DatabaseTopologyMode.Shared, "industrial_platform", null, new Dictionary<string, string>());

        var exception = Assert.Throws<BusinessException>(() =>
            DatabaseTopologyResolver.Resolve(topology, "systemdata", DatabaseProvider.PostgreSQL, "systemdata_db"));

        Assert.Contains("仅允许 Development", exception.Message);
    }

    [Fact]
    public void Resolve_SharedMissingTarget_ThrowsValidationException()
    {
        var exception = Assert.Throws<ValidationException>(() =>
            DatabaseTopologyResolver.Resolve(SharedPostgres(null), "systemdata", DatabaseProvider.PostgreSQL, "systemdata_db"));

        Assert.Contains("缺少 PostgreSQL 目标库名", exception.Message);
    }

    [Fact]
    public void Resolve_PerServiceWithMapping_UsesMappedPhysicalName()
    {
        var topology = PerService(new Dictionary<string, string> { ["systemdata"] = "systemdata_prod" });

        var target = DatabaseTopologyResolver.Resolve(
            topology, "systemdata", DatabaseProvider.PostgreSQL, "systemdata_db");

        Assert.Equal("systemdata_prod", target.PhysicalDatabaseName);
        Assert.False(target.IsSharedPhysicalDatabase);
    }

    [Fact]
    public void Resolve_PerServiceWithoutMapping_FallsBackToLogicalName()
    {
        var topology = PerService(new Dictionary<string, string>());

        var target = DatabaseTopologyResolver.Resolve(
            topology, "systemdata", DatabaseProvider.PostgreSQL, "systemdata_db");

        Assert.Equal("systemdata_db", target.PhysicalDatabaseName);
    }

    [Fact]
    public void Resolve_UnknownMode_ThrowsValidationException()
    {
        var topology = new DatabaseTopology(
            "Development", (DatabaseTopologyMode)99, "x", null, new Dictionary<string, string>());

        var exception = Assert.Throws<ValidationException>(() =>
            DatabaseTopologyResolver.Resolve(topology, "systemdata", DatabaseProvider.PostgreSQL, "systemdata_db"));

        Assert.Contains("不支持的数据库拓扑模式", exception.Message);
    }

    [Fact]
    public void Resolve_NullServiceKey_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            DatabaseTopologyResolver.Resolve(SharedPostgres(), "", DatabaseProvider.PostgreSQL, "systemdata_db"));
    }
}
