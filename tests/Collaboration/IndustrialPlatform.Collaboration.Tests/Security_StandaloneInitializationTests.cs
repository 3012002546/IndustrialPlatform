using IndustrialPlatform.Collaboration.EmbeddedHost;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SharedKernel.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Security_StandaloneInitializationTests
{
    [Fact]
    public void Missing_standalone_configuration_fails_closed_with_the_resolved_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "industrial-platform-standalone-config", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Development",
                ContentRootPath = root,
            });
            var expectedPath = StandaloneConfiguration.ResolveDevelopmentPath(root);
            builder.Configuration[StandaloneConfiguration.ConfigurationPathKey] = expectedPath;

            var exception = Assert.Throws<InvalidOperationException>(() => StandaloneConfiguration.Apply(builder, []));

            Assert.Contains(expectedPath, exception.Message, StringComparison.Ordinal);
            Assert.Contains("不能回退到平台配置", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Standalone_configuration_without_database_section_fails_with_a_clear_message()
    {
        var root = Path.Combine(Path.GetTempPath(), "industrial-platform-standalone-config", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "standalone.local.json");
        File.WriteAllText(path, "{\"Standalone\": {}}" );
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Development",
                ContentRootPath = root,
            });
            builder.Configuration[StandaloneConfiguration.ConfigurationPathKey] = path;

            var exception = Assert.Throws<InvalidOperationException>(() => StandaloneConfiguration.Apply(builder, []));

            Assert.Contains("Standalone:Database", exception.Message, StringComparison.Ordinal);
            Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Sqlite_lock_coordinates_two_process_like_initializers_for_the_same_absolute_target()
    {
        var root = Path.Combine(Path.GetTempPath(), "industrial-platform-pf06-lock", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var database = Path.Combine(root, "standalone.db");
        var target = new ResolvedDatabaseTarget("Production", DatabaseTopologyMode.Shared, "identity", DatabaseProvider.Sqlite, "identity_db", database, true);
        var locker = new StandaloneInitializationLock(Options.Create(new SqlSugarOptions
        {
            DbType = DbType.Sqlite,
            ConnectionString = $"Data Source={database}",
        }));

        await using var first = await locker.AcquireAsync(target, CancellationToken.None);
        var secondTask = locker.AcquireAsync(target, CancellationToken.None);
        await Task.Delay(50);
        Assert.False(secondTask.IsCompleted);

        await first.DisposeAsync();
        await using var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Postgres_lock_key_includes_the_physical_database_and_schema()
    {
        var target = new ResolvedDatabaseTarget("Production", DatabaseTopologyMode.Shared, "identity", DatabaseProvider.PostgreSQL, "identity_db", "mes_collaboration", true, "public");
        var key = StandaloneInitializationLock.BuildKey(target);

        Assert.Contains("mes_collaboration", key, StringComparison.Ordinal);
        Assert.Contains("public", key, StringComparison.Ordinal);
    }

    [Fact]
    public void Platform_shared_production_remains_rejected_without_explicit_standalone_semantics()
    {
        var topology = new DatabaseTopology("Production", DatabaseTopologyMode.Shared, "mes_collaboration", null, new Dictionary<string, string>());

        Assert.Throws<BusinessException>(() => DatabaseTopologyResolver.Resolve(topology, "identity", DatabaseProvider.PostgreSQL, "identity_db"));

        var standalone = topology with { IsStandalone = true };
        var resolved = DatabaseTopologyResolver.Resolve(standalone, "identity", DatabaseProvider.PostgreSQL, "identity_db");
        Assert.Equal("mes_collaboration", resolved.PhysicalDatabaseName);
    }
}
