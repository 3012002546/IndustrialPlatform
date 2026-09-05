using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Application.CodingRule;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.CodingRule;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Infrastructure.CodingRule;
using IndustrialPlatform.ReferenceData.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Tests;

public sealed class CodingRulePersistenceTests : IAsyncLifetime, IDisposable
{
    private static readonly ReferenceDataActor Tenant = new("TENANT-A", "USER-A", false, "coding-test");
    private static readonly ReferenceDataActor Platform = Tenant with { CanManagePlatform = true };
    private static CancellationToken Ct => CancellationToken.None;
    private readonly string path = Path.Combine(Path.GetTempPath(), $"pf03-coding-{Guid.NewGuid():N}.db");
    private readonly MutableTimeProvider clock = new(new DateTimeOffset(2026, 9, 5, 12, 30, 0, TimeSpan.Zero));
    private readonly SqlSugarDbContext context;
    private readonly CodingRuleRepository repository;
    private readonly CodingRuleService service;

    public CodingRulePersistenceTests()
    {
        context = NewContext();
        repository = new CodingRuleRepository(context);
        service = new CodingRuleService(repository, clock);
    }

    public Task InitializeAsync() => context.SqlSugar.Ado.ExecuteCommandAsync(
        "PRAGMA foreign_keys=ON;" + CodingRuleMigration.Sql(false) + ReferenceDataSharedMigration.Sql(false)
        + ReferenceDataCacheGenerationMigration.Sql(false));

    [Fact]
    public async Task Explicit_platform_and_tenant_sources_keep_fixed_ever_published_revisions()
    {
        var tenant = await CreatePublishedAsync(service, Tenant, "Tenant", "LotNumber", "T1-{TENANT}-{SEQ:3}");
        var platform = await CreatePublishedAsync(service, Platform, "Platform", "LotNumber", "P-{TENANT}-{SEQ:3}");

        var tenantPreview = await service.PreviewAsync(Tenant, "LotNumber",
            new("Tenant", Tenant.TenantNId, 1), Ct);
        var platformPreview = await service.PreviewAsync(Tenant, "LotNumber", new("Platform", null, 1), Ct);
        Assert.Equal("T1-TENANT-A-001", tenantPreview.Code);
        Assert.Equal("P-TENANT-A-001", platformPreview.Code);
        Assert.Equal(tenant.Id, (await service.GetAsync(Tenant, tenant.Id, Ct)).Id);
        Assert.Equal(platform.Id, (await service.GetAsync(Tenant, platform.Id, Ct)).Id);

        var clone = await service.CloneAsync(Tenant, tenant.Id, Version(tenant), Ct);
        clone = await service.UpdateAsync(Tenant, clone.Id, new("Lot v2", "WorkOrder",
            "T2-{TENANT}-{SEQ:3}", "Never", clone.OptimisticVersion, clone.ConcurrencyVersion), Ct);
        clone = await service.PublishAsync(Tenant, clone.Id, Version(clone), Ct);

        Assert.Equal("T1-TENANT-A-001", (await service.PreviewAsync(Tenant, "LotNumber",
            new("Tenant", Tenant.TenantNId, 1), Ct)).Code);
        Assert.Equal("T2-TENANT-A-001", (await service.PreviewAsync(Tenant, "LotNumber",
            new("Tenant", Tenant.TenantNId, 2), Ct)).Code);
        clone = await service.DisableAsync(Tenant, clone.Id, Version(clone), Ct);
        Assert.Equal("T2-TENANT-A-001", (await service.PreviewAsync(Tenant, "LotNumber",
            new("Tenant", Tenant.TenantNId, clone.Revision), Ct)).Code);

        var hidden = await Assert.ThrowsAsync<ReferenceDataException>(() => service.PreviewAsync(
            Tenant with { TenantNId = "TENANT-B" }, "LotNumber", new("Tenant", "TENANT-A", 1), Ct));
        Assert.Equal("REF-CODING-RULE-NOT-FOUND", hidden.ErrorCode);
    }

    [Fact]
    public async Task Preview_has_no_side_effect_and_generate_is_idempotent_per_request_and_revision()
    {
        var rule = await CreatePublishedAsync(service, Tenant, "Tenant", "DailyLot",
            "{TENANT}-{YYYY}{MM}{DD}-{SEQ:3}", "Daily");
        var previewRequest = new PreviewCodeRequest("Tenant", Tenant.TenantNId, 1);
        var request = new GenerateCodeRequest("Tenant", Tenant.TenantNId, 1);

        var preview = await service.PreviewAsync(Tenant, rule.NId, previewRequest, Ct);
        Assert.Equal("TENANT-A-20260905-001", preview.Code);
        Assert.False(preview.ConsumesSequence);
        Assert.Equal(0, await CountAsync("reference_data_coding_rule_sequence"));
        Assert.Equal(0, await CountAsync("reference_data_coding_rule_idempotency_record"));
        var required = await Assert.ThrowsAsync<ReferenceDataException>(() =>
            service.GenerateAsync(Tenant, rule.NId, request, null, Ct));
        Assert.Equal("REF-IDEMPOTENCY-REQUIRED", required.ErrorCode);

        var first = await service.GenerateAsync(Tenant, rule.NId, request, "same-key", Ct);
        var replay = await service.GenerateAsync(Tenant, rule.NId, request, "same-key", Ct);
        var second = await service.GenerateAsync(Tenant, rule.NId, request, "second-key", Ct);
        Assert.Equal(first, replay);
        Assert.Equal(1, first.Sequence);
        Assert.Equal(2, second.Sequence);

        var clone = await service.CloneAsync(Tenant, rule.Id, Version(rule), Ct);
        clone = await service.PublishAsync(Tenant, clone.Id, Version(clone), Ct);
        var changedRequest = request with { RuleRevision = 2 };
        var conflict = await Assert.ThrowsAsync<ReferenceDataException>(() =>
            service.GenerateAsync(Tenant, rule.NId, changedRequest, "same-key", Ct));
        Assert.Equal("REF-IDEMPOTENCY-CONFLICT", conflict.ErrorCode);
        var newRevision = await service.GenerateAsync(Tenant, rule.NId, changedRequest, "revision-two", Ct);
        Assert.Equal(1, newRevision.Sequence);

        clock.UtcNow = clock.UtcNow.AddDays(1);
        var nextDay = await service.GenerateAsync(Tenant, rule.NId, changedRequest, "next-day", Ct);
        Assert.Equal(1, nextDay.Sequence);
        Assert.Equal("20260906", nextDay.PeriodKey);
        clock.UtcNow = clock.UtcNow.AddDays(1);
        Assert.Equal(nextDay, await service.GenerateAsync(Tenant, rule.NId, changedRequest, "next-day", Ct));
    }

    [Fact]
    public async Task Independent_sqlite_connections_generate_unique_atomic_sequences_and_one_same_key_result()
    {
        var rule = await CreatePublishedAsync(service, Tenant, "Tenant", "ConcurrentCode", "C-{SEQ:6}");
        var request = new GenerateCodeRequest("Tenant", Tenant.TenantNId, rule.Revision);

        var unique = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(index => GenerateWithFreshContextAsync(rule.NId, request, $"unique-{index}")));
        Assert.Equal(Enumerable.Range(1, 12).Select(value => (long)value).ToArray(),
            unique.Select(item => item.Sequence).Order().ToArray());
        Assert.Equal(12, unique.Select(item => item.Code).Distinct(StringComparer.Ordinal).Count());

        var replayed = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => GenerateWithFreshContextAsync(rule.NId, request, "one-shared-key")));
        Assert.Single(replayed.Select(item => item.Code).Distinct(StringComparer.Ordinal));
        Assert.All(replayed, item => Assert.Equal(13, item.Sequence));
    }

    [Fact]
    public async Task Sequence_overflow_fails_without_creating_an_idempotency_record()
    {
        var rule = await CreatePublishedAsync(service, Tenant, "Tenant", "OverflowCode", "O-{SEQ:1}");
        var request = new GenerateCodeRequest("Tenant", Tenant.TenantNId, rule.Revision);
        await service.GenerateAsync(Tenant, rule.NId, request, "first", Ct);
        await context.SqlSugar.Ado.ExecuteCommandAsync(
            "UPDATE reference_data_coding_rule_sequence SET last_value=9223372036854775807");

        var error = await Assert.ThrowsAsync<ReferenceDataException>(() =>
            service.GenerateAsync(Tenant, rule.NId, request, "overflow", Ct));

        Assert.Equal("REF-CONCURRENCY-CONFLICT", error.ErrorCode);
        Assert.Equal(1, await CountAsync("reference_data_coding_rule_idempotency_record"));
    }

    [Fact]
    public async Task Any_generation_removes_all_expired_idempotency_records_not_only_the_current_key()
    {
        var rule = await CreatePublishedAsync(service, Tenant, "Tenant", "ExpiryCleanup", "E-{SEQ:3}");
        var request = new GenerateCodeRequest("Tenant", Tenant.TenantNId, rule.Revision);
        await service.GenerateAsync(Tenant, rule.NId, request, "expired-key", Ct);
        Assert.Equal(1, await CountAsync("reference_data_coding_rule_idempotency_record"));

        clock.UtcNow = clock.UtcNow.AddDays(8);
        await service.GenerateAsync(Tenant, rule.NId, request, "different-current-key", Ct);

        Assert.Equal(1, await CountAsync("reference_data_coding_rule_idempotency_record"));
    }

    [Fact]
    public async Task Platform_manage_stale_CAS_and_database_foreign_keys_are_enforced()
    {
        var denied = await Assert.ThrowsAsync<ReferenceDataException>(() => service.CreateAsync(Tenant,
            Request("Platform", "DeniedPlatform", "P-{SEQ:2}"), Ct));
        Assert.Equal("ID_PERMISSION_DENIED", denied.ErrorCode);

        var draft = await service.CreateAsync(Tenant, Request("Tenant", "CasRule", "C-{SEQ:2}"), Ct);
        var stale = draft;
        draft = await service.UpdateAsync(Tenant, draft.Id, new("Changed", "WorkOrder", "D-{SEQ:2}",
            "Never", draft.OptimisticVersion, draft.ConcurrencyVersion), Ct);
        var conflict = await Assert.ThrowsAsync<ReferenceDataException>(() => service.UpdateAsync(Tenant,
            draft.Id, new("Stale", "WorkOrder", "E-{SEQ:2}", "Never",
                stale.OptimisticVersion, stale.ConcurrencyVersion), Ct));
        Assert.Equal("REF-CONCURRENCY-CONFLICT", conflict.ErrorCode);

        var foreignKey = await Assert.ThrowsAnyAsync<Exception>(() => context.SqlSugar.Ado.ExecuteCommandAsync($"""
            INSERT INTO reference_data_coding_rule_sequence
                (coding_rule_id,rule_revision,period_key,context_hash,last_value,created_on,last_updated_on)
            VALUES ('{Guid.NewGuid()}',1,'ALL','{new string('A', 64)}',1,
                    '2026-09-05T00:00:00Z','2026-09-05T00:00:00Z')
            """));
        Assert.Contains("FOREIGN KEY", foreignKey.ToString(), StringComparison.OrdinalIgnoreCase);
        var mismatchedRevision = await Assert.ThrowsAnyAsync<Exception>(() =>
            context.SqlSugar.Ado.ExecuteCommandAsync($"""
                INSERT INTO reference_data_coding_rule_sequence
                    (coding_rule_id,rule_revision,period_key,context_hash,last_value,created_on,last_updated_on)
                VALUES ('{draft.Id}',99,'ALL','{new string('B', 64)}',1,
                        '2026-09-05T00:00:00Z','2026-09-05T00:00:00Z')
                """));
        Assert.Contains("FOREIGN KEY", mismatchedRevision.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration_007_has_provider_compatible_constraints_and_minimal_technical_rows()
    {
        Assert.Equal("reference-data-2.7-007", CodingRuleMigration.Version);
        var postgres = CodingRuleMigration.Sql(true);
        var sqlite = CodingRuleMigration.Sql(false);
        Assert.Contains("REFERENCES reference_data.coding_rule_definition(id,revision)", postgres,
            StringComparison.Ordinal);
        Assert.Equal(2, postgres.Split("REFERENCES reference_data.coding_rule_definition(id,revision)",
            StringSplitOptions.None).Length - 1);
        Assert.Contains("UNIQUE(tenant_nid,rule_nid,idempotency_key_hash)", postgres, StringComparison.Ordinal);
        Assert.Contains("PRIMARY KEY(coding_rule_id,rule_revision,period_key,context_hash)", sqlite,
            StringComparison.Ordinal);
        var sequence = sqlite[(sqlite.IndexOf("CREATE TABLE reference_data_coding_rule_sequence", StringComparison.Ordinal))..
            sqlite.IndexOf("CREATE TABLE reference_data_coding_rule_idempotency_record", StringComparison.Ordinal)];
        Assert.DoesNotContain("is_deleted", sequence, StringComparison.Ordinal);
        Assert.DoesNotContain("optimistic_version", sequence, StringComparison.Ordinal);
    }

    private async Task<GeneratedCodeDto> GenerateWithFreshContextAsync(
        string nId, GenerateCodeRequest request, string key)
    {
        using var isolated = NewContext();
        var isolatedService = new CodingRuleService(new CodingRuleRepository(isolated), clock);
        return await isolatedService.GenerateAsync(Tenant, nId, request, key, Ct);
    }

    private async Task<int> CountAsync(string table) => Convert.ToInt32(
        await context.SqlSugar.Ado.GetScalarAsync($"SELECT COUNT(*) FROM {table}"),
        System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<CodingRuleDetailDto> CreatePublishedAsync(
        CodingRuleService target, ReferenceDataActor actor, string scope, string nId, string template,
        string resetPolicy = "Never")
    {
        var draft = await target.CreateAsync(actor, Request(scope, nId, template, resetPolicy), Ct);
        return await target.PublishAsync(actor, draft.Id, Version(draft), Ct);
    }

    private static CreateCodingRuleRequest Request(
        string scope, string nId, string template, string resetPolicy = "Never") =>
        new(scope, nId, nId, "WorkOrder", template, resetPolicy);

    private static PublishOrDisableRequest Version(CodingRuleDetailDto rule) =>
        new(rule.OptimisticVersion, rule.ConcurrencyVersion, "Coding rule test");

    private SqlSugarDbContext NewContext() => new(Options.Create(new SqlSugarOptions
    {
        DbType = DbType.Sqlite,
        ConnectionString = $"Data Source={path};Pooling=False",
    }));

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

    private sealed class MutableTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = value;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
