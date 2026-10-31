using System.Globalization;
using System.Text.Json;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Host;
using IndustrialPlatform.PlatformStudio.Infrastructure.Initialization;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class StudioCommandLineTests
{
    [Fact]
    public async Task Inspect_missing_database_is_read_only_and_explicit_initialize_verifies_without_exposing_sql()
    {
        using var fixture = new Fixture();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        Assert.Equal(1, await fixture.Command.ExecuteAsync(["--studio-inspect"], output, CancellationToken.None));
        Assert.False(File.Exists(fixture.Profile.Database!.SqliteFile));
        using var before = JsonDocument.Parse(output.ToString());
        Assert.False(before.RootElement.GetProperty("ok").GetBoolean());
        var plan = before.RootElement.GetProperty("migrationPlan");
        Assert.Equal(10, plan.GetArrayLength());
        Assert.Contains(plan.EnumerateArray(), step => step.GetRawText().Contains("STU-009-WorkbookWorkingCopy", StringComparison.Ordinal));
        output.GetStringBuilder().Clear();
        Assert.Equal(0, await fixture.Command.ExecuteAsync(["--studio-initialize"], output, CancellationToken.None));
        Assert.True(File.Exists(fixture.Profile.Database.SqliteFile));
        using var initialized = JsonDocument.Parse(output.ToString());
        Assert.All(initialized.RootElement.GetProperty("migrationPlan").EnumerateArray(), step => Assert.Equal("Applied", step.GetProperty("status").GetString()));
        Assert.DoesNotContain("CREATE TABLE", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Profile.Database.SqliteFile!, output.ToString(), StringComparison.Ordinal);
        output.GetStringBuilder().Clear();
        Assert.Equal(0, await fixture.Command.ExecuteAsync(["--studio-inspect"], output, CancellationToken.None));
    }

    [Fact]
    public async Task Check_change_rejects_unknown_action_and_does_not_modify_receipt_or_profile()
    {
        using var fixture = new Fixture();
        await fixture.Initializer.InitializeLocalAsync(CancellationToken.None);
        var receipt = new StudioOperationReceipt { NId = "uncertain-action", TenantNId = "t", CreatedOn = DateTimeOffset.UtcNow, Status = "Unknown", OperationKey = "op", OperationKind = "PageAction", ResourceNId = "page", ActorUserNId = "u", RequestHash = new string('a', 64) };
        await fixture.Database.Client.Insertable(receipt).ExecuteCommandAsync();
        var next = StudioInitializationTests.Profile(fixture.Profile.Database!.SqliteFile!);
        next.ProfileVersion = "2";
        next.EnabledModules = [];
        var error = await Assert.ThrowsAsync<StudioException>(() => fixture.Command.CheckChangeAsync(next, CancellationToken.None));
        Assert.Equal("STUDIO_MODULE_BUSY", error.Code);
        Assert.Equal("Unknown", (await fixture.Database.Client.Queryable<StudioOperationReceipt>().SingleAsync()).Status);
        Assert.Equal("1", fixture.Profile.ProfileVersion);
        await fixture.Database.Client.Deleteable<StudioOperationReceipt>().Where(row => row.Id == receipt.Id).ExecuteCommandAsync();
        await fixture.Command.CheckChangeAsync(next, CancellationToken.None);
        Assert.Equal(["LowCode"], fixture.Profile.ActiveModules);
    }

    [Fact]
    public async Task Check_change_blocks_retained_published_dependency_into_disabled_module()
    {
        using var fixture = new Fixture();
        await fixture.Initializer.InitializeLocalAsync(CancellationToken.None);
        var model = new StudioResource { NId = "model", TenantNId = "t", Kind = "LedgerModel", Name = "model", OwnerUserNId = "u", IsEnabled = true };
        var modelRevision = new StudioRevision { NId = "model-v1", TenantNId = "t", ResourceId = model.Id, RevisionNo = 1, ContentHash = new string('a', 64), PublishedBy = "u" };
        var dataset = new StudioResource { NId = "dataset", TenantNId = "t", Kind = "Dataset", Name = "dataset", OwnerUserNId = "u", IsEnabled = true };
        var datasetRevision = new StudioRevision { NId = "dataset-v1", TenantNId = "t", ResourceId = dataset.Id, RevisionNo = 1, ContentHash = new string('b', 64), PublishedBy = "u", DependenciesJson = "[{\"resourceNId\":\"model\",\"revisionNId\":\"model-v1\"}]" };
        await fixture.Database.Client.Insertable(new[] { model, dataset }).ExecuteCommandAsync();
        await fixture.Database.Client.Insertable(new[] { modelRevision, datasetRevision }).ExecuteCommandAsync();
        await fixture.Database.Client.Updateable<StudioResource>().SetColumns(row => row.PublishedRevisionId == modelRevision.Id).Where(row => row.Id == model.Id).ExecuteCommandAsync();
        await fixture.Database.Client.Updateable<StudioResource>().SetColumns(row => row.PublishedRevisionId == datasetRevision.Id).Where(row => row.Id == dataset.Id).ExecuteCommandAsync();
        var next = StudioInitializationTests.Profile(fixture.Profile.Database!.SqliteFile!);
        next.ProfileVersion = "2";
        next.EnabledModules = [];
        Assert.Equal("STUDIO_MODULE_BUSY", (await Assert.ThrowsAsync<StudioException>(() => fixture.Command.CheckChangeAsync(next, CancellationToken.None))).Code);
    }

    [Fact]
    public async Task Command_conflicts_and_invalid_change_files_return_structured_failure_without_initializing()
    {
        using var fixture = new Fixture();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        Assert.Equal(1, await fixture.Command.ExecuteAsync(["--studio-initialize", "--studio-inspect"], output, CancellationToken.None));
        Assert.False(File.Exists(fixture.Profile.Database!.SqliteFile));
        using var failed = JsonDocument.Parse(output.ToString());
        Assert.Equal("STUDIO_VALIDATION", failed.RootElement.GetProperty("checks")[0].GetProperty("reasonCode").GetString());
        await fixture.Initializer.InitializeLocalAsync(CancellationToken.None);
        output.GetStringBuilder().Clear();
        Assert.Equal(1, await fixture.Command.ExecuteAsync(["--studio-check-change", Path.Combine(fixture.Folder, "missing.json")], output, CancellationToken.None));
        Assert.DoesNotContain(fixture.Folder, output.ToString(), StringComparison.Ordinal);
        Assert.Null(await fixture.Command.ExecuteAsync([], output, CancellationToken.None));
    }

    private sealed class Fixture : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "studio-cli-" + Guid.NewGuid().ToString("N"));
        public StudioDeploymentProfile Profile { get; }
        public StudioDatabase Database { get; }
        public StudioServiceInitializer Initializer { get; }
        public StudioCommandLine Command { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Folder);
            Profile = StudioInitializationTests.Profile(Path.Combine(Folder, "studio.db"));
            Database = new(Profile, new ConfigurationBuilder().Build());
            Initializer = new(Database, Profile, TimeProvider.System);
            Command = new(Profile, Database, Initializer, TimeProvider.System);
        }
        public void Dispose()
        {
            Database.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(Folder, true);
        }
    }
}
