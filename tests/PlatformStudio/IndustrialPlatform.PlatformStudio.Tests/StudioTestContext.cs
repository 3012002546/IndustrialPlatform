using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Initialization;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.PlatformStudio.Tests;

internal sealed class StudioTestContext : IAsyncDisposable
{
    private readonly string _folder;
    public StudioDatabase Database { get; }
    public StudioDeploymentProfile Profile { get; }
    public StudioResourceStore Store { get; }
    public StudioAuthorization Authorization { get; }
    public StudioCommands Commands { get; }
    public StudioPublishing Publishing { get; }
    public StudioTestSourceExecutor SourceExecutor { get; } = new();
    public StudioPrincipal Principal { get; } = new("tenant-a", "user-a", "test", "工程师", [], StudioKinds.Permissions, ["factory-a"], "1", "1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
    private StudioTestContext(string folder)
    {
        _folder = folder;
        Profile = StudioInitializationTests.Profile(Path.Combine(folder, "studio.db"));
        Database = new(Profile, new ConfigurationBuilder().Build());
        Store = new(Database, TimeProvider.System);
        Authorization = new(Store, TimeProvider.System);
        Commands = new(Store, TimeProvider.System);
        Publishing = new(Store, Authorization, Commands, Profile, TimeProvider.System, [], SourceExecutor);
    }
    public static async Task<StudioTestContext> CreateAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), "studio-app-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var context = new StudioTestContext(folder);
        try { await new StudioServiceInitializer(context.Database, context.Profile, TimeProvider.System).InitializeLocalAsync(CancellationToken.None); return context; }
        catch { await context.DisposeAsync(); throw; }
    }
    public ValueTask DisposeAsync()
    {
        Database.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        return ValueTask.CompletedTask;
    }
}
