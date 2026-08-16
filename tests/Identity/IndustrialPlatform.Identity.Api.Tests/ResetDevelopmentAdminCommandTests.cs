using System.Text.Json;
using IndustrialPlatform.Identity.Api.Commands;
using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Identity.Infrastructure.Bootstrap;
using IndustrialPlatform.Identity.Infrastructure.Passwords;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Seeds;
using IndustrialPlatform.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SQLitePCL;
using SqlSugar;

namespace IndustrialPlatform.Identity.Api.Tests;

public sealed class ResetDevelopmentAdminCommandTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqlSugarDbContext _dbContext;

    static ResetDevelopmentAdminCommandTests() => Batteries_V2.Init();

    public ResetDevelopmentAdminCommandTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-reset-admin-{Guid.NewGuid():N}.db");
        _dbContext = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            ConnectionString = $"Data Source={_dbPath};Foreign Keys=True",
            DbType = DbType.Sqlite,
        }));
    }

    [Fact]
    public async Task RunAsync_ExistingAdmin_WritesNewCredentialAndAdvancesSecurityVersion()
    {
        var hasher = new BcryptPasswordHasher();
        var credentials = new BootstrapCredentialStore(_dbContext);
        var store = new BootstrapStore(_dbContext, new UserRepository(_dbContext));
        var initialization = new IdentityInitializationService(
            new SchemaMigrationRunner(_dbContext, IdentitySchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance),
            new IdentitySeedRunner(_dbContext, hasher, credentials),
            store);
        await initialization.InitializeAsync(new IdentitySeedContext("development", null, null));
        var before = await store.GetAdminIncludingDeletedAsync("development", BootstrapSeedCatalog.BootstrapUserNId);

        var services = new ServiceCollection();
        services.AddOptions<BootstrapOptions>().Configure(options => options.TenantNId = "development");
        services.AddSingleton(new DevelopmentAdminResetService(
            store,
            credentials,
            new TemporaryPasswordGenerator(),
            hasher));
        await using var provider = services.BuildServiceProvider();

        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"reset-admin-output-{Guid.NewGuid():N}"));
        var target = Path.Combine(directory.FullName, "reset-admin.json");
        var output = new StringWriter();
        try
        {
            var exitCode = await ResetDevelopmentAdminCommand.RunAsync(provider, output, target);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(target));
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(target));
            Assert.Equal("admin", json.RootElement.GetProperty("loginName").GetString());
            Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("temporaryPassword").GetString()));
            Assert.DoesNotContain(json.RootElement.GetProperty("temporaryPassword").GetString()!, output.ToString(), StringComparison.Ordinal);
            var after = await store.GetAdminIncludingDeletedAsync("development", BootstrapSeedCatalog.BootstrapUserNId);
            Assert.True(after.AuthVersion > before.AuthVersion);
            Assert.Equal(2, await _dbContext.SqlSugar.Ado.GetIntAsync("SELECT COUNT(*) FROM identity_bootstrap_credential"));
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }
}
