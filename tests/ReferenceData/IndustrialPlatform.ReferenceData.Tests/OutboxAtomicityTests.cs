using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Application.Dictionary;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.Dictionary;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Infrastructure.Dictionary;
using IndustrialPlatform.ReferenceData.Infrastructure.Initialization;
using IndustrialPlatform.SharedKernel.Topology;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Tests;

public sealed class OutboxAtomicityTests : IAsyncLifetime, IDisposable
{
    private static readonly ReferenceDataActor Actor =
        new("TENANT-OUTBOX", "USER-OUTBOX", false, "outbox-atomicity");
    private readonly string path = Path.Combine(Path.GetTempPath(), $"pf03-outbox-atomic-{Guid.NewGuid():N}.db");
    private readonly SqlSugarDbContext context;
    private readonly DictionaryService service;

    public OutboxAtomicityTests()
    {
        context = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            DbType = DbType.Sqlite,
            ConnectionString = $"Data Source={path};Pooling=False",
        }));
        service = new DictionaryService(new DictionaryRepository(context));
    }

    public async Task InitializeAsync()
    {
        var initializer = new ReferenceDataServiceInitializer(new ReferenceDataInitializationLedger(context));
        var initialization = new ServiceInitializationContext(
            "Test", Actor.TenantNId, "OP-OUTBOX", "referencedata", "referencedata",
            new ResolvedDatabaseTarget("Test", DatabaseTopologyMode.PerService, "referencedata",
                DatabaseProvider.Sqlite, "referencedata_db", path, false),
            ReferenceDataServiceInitializer.CurrentVersion, ServiceInitializationPolicy.Standard, Actor.TraceId);
        var inspection = await initializer.InspectAsync(initialization, CancellationToken.None);
        var applied = await initializer.ApplyAsync(initialization,
            await initializer.PlanAsync(initialization, inspection, CancellationToken.None),
            CancellationToken.None);
        Assert.True(applied.Ready);
    }

    [Fact]
    public async Task Publication_and_event_commit_or_rollback_together()
    {
        var draft = await service.CreateAsync(Actor,
            new("Tenant", null, "OUTBOX-ATOMIC", "Outbox atomicity", null,
                [new("OPEN", "Open", null, 0, true)]), CancellationToken.None);
        Assert.Equal(0, await OutboxCountAsync());
        await context.SqlSugar.Ado.ExecuteCommandAsync("""
            CREATE TRIGGER pf03_outbox_failure BEFORE INSERT ON reference_data_outbox_message
            BEGIN SELECT RAISE(ABORT, 'pf03-outbox-write-failure'); END;
            """);

        var error = await Assert.ThrowsAsync<ReferenceDataException>(() => service.PublishAsync(
            Actor, draft.Id, Version(draft), CancellationToken.None));
        Assert.Equal(503, error.Status);
        Assert.Equal("Draft", (await service.GetAsync(Actor, draft.Id, CancellationToken.None)).Status);
        Assert.Equal(0, await OutboxCountAsync());

        await context.SqlSugar.Ado.ExecuteCommandAsync("DROP TRIGGER pf03_outbox_failure");
        var published = await service.PublishAsync(
            Actor, draft.Id, Version(draft), CancellationToken.None);
        Assert.Equal("Published", published.Status);
        var rows = await context.SqlSugar.Ado.SqlQueryAsync<OutboxProbe>("""
            SELECT module_key AS ModuleKey,event_name AS EventName,payload AS Payload,status AS Status
            FROM reference_data_outbox_message
            """);
        var row = Assert.Single(rows);
        Assert.Equal("dictionary", row.ModuleKey);
        Assert.Equal("industrial.reference-data.dictionary.published.v1", row.EventName);
        Assert.Equal("Pending", row.Status);
        Assert.Contains("\"subjectNId\":\"OUTBOX-ATOMIC\"", row.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("Open", row.Payload, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        context.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(path)) File.Delete(path);
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    private Task<int> OutboxCountAsync() =>
        context.SqlSugar.Ado.GetIntAsync("SELECT COUNT(*) FROM reference_data_outbox_message");

    private static PublishOrDisableRequest Version(DictionaryDetailDto value) =>
        new(value.OptimisticVersion, value.ConcurrencyVersion, "Outbox atomicity test");

    private sealed class OutboxProbe
    {
        public string ModuleKey { get; set; } = string.Empty;
        public string EventName { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
