using System.Data.Common;
using System.Globalization;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Application.Caching;
using IndustrialPlatform.ReferenceData.Application.CodingRule;
using IndustrialPlatform.ReferenceData.Contracts.CodingRule;
using IndustrialPlatform.ReferenceData.Contracts.Events;
using IndustrialPlatform.ReferenceData.Domain.CodingRule;
using IndustrialPlatform.ReferenceData.Infrastructure.Outbox;
using IndustrialPlatform.ReferenceData.Infrastructure.Caching;
using IndustrialPlatform.ReferenceData.Domain.Common;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Infrastructure.CodingRule;

public sealed class CodingRuleRepository(SqlSugarDbContext context) : ICodingRuleRepository
{
    private ISqlSugarClient Db => context.SqlSugar;
    private bool Postgres => Db.CurrentConnectionConfig.DbType == DbType.PostgreSQL;
    private string Definitions => Postgres ? "reference_data.coding_rule_definition" : "reference_data_coding_rule_definition";
    private string Sequences => Postgres ? "reference_data.coding_rule_sequence" : "reference_data_coding_rule_sequence";
    private string IdempotencyRecords => Postgres
        ? "reference_data.coding_rule_idempotency_record"
        : "reference_data_coding_rule_idempotency_record";

    private ISugarQueryable<CodingRuleDefinitionRow> Visible(string tenantNId) =>
        Db.Queryable<CodingRuleDefinitionRow>().AS(Definitions)
            .Where(row => !row.IsDeleted && (row.TenantNId == tenantNId || row.TenantNId == null));

    public async Task<(IReadOnlyList<CodingRuleSummaryDto> Items, long Total)> SearchAsync(
        string tenantNId, CodingRuleQuery query, CancellationToken cancellationToken)
    {
        var filter = Visible(tenantNId);
        if (query.Keyword is { Length: > 0 } keyword)
        {
            keyword = keyword.ToUpperInvariant();
            filter = filter.Where(row => row.NId.Contains(keyword)
                || SqlFunc.Contains(SqlFunc.ToUpper(row.Name), keyword));
        }
        filter = query.Status is null
            ? filter.Where(row => row.Status == "Draft" || row.Status == "Published")
            : filter.Where(row => row.Status == query.Status);
        if (query.ScopeType is not null) filter = filter.Where(row => row.ScopeType == query.ScopeType);
        var direction = query.Descending ? OrderByType.Desc : OrderByType.Asc;
        filter = query.SortField switch
        {
            "nId" => filter.OrderBy(row => row.NId, direction),
            "name" => filter.OrderBy(row => row.Name, direction),
            "revision" => filter.OrderBy(row => row.Revision, direction),
            "status" => filter.OrderBy(row => row.Status, direction),
            "lastUpdatedOn" => filter.OrderBy(row => row.LastUpdatedOn, direction),
            _ => filter.OrderBy(row => row.LastUpdatedOn, OrderByType.Desc),
        };
        var total = await filter.CountAsync(cancellationToken);
        var rows = await filter.OrderBy(row => row.Id)
            .ToPageListAsync(query.PageIndex, query.PageSize, cancellationToken);
        return (rows.Select(row => CodingRuleService.ToSummary(Restore(row))).ToArray(), total);
    }

    public async Task<CodingRuleDefinition?> GetAsync(
        string tenantNId, Guid id, CancellationToken cancellationToken)
    {
        var row = await Visible(tenantNId).Where(item => item.Id == id).FirstAsync(cancellationToken);
        return row is null ? null : Restore(row);
    }

    public async Task<CodingRuleDefinition?> GetRevisionAsync(
        string tenantNId,
        string nId,
        string sourceScope,
        int revision,
        CancellationToken cancellationToken)
    {
        var filter = Db.Queryable<CodingRuleDefinitionRow>().AS(Definitions)
            .Where(row => !row.IsDeleted && row.NId == nId && row.Revision == revision
                && row.PublishedOn != null
                && (row.Status == "Published" || row.Status == "Superseded" || row.Status == "Disabled"));
        if (sourceScope == nameof(ReferenceScopeType.Platform))
            filter = filter.Where(row => row.ScopeType == "Platform" && row.TenantNId == null);
        else if (sourceScope == nameof(ReferenceScopeType.Tenant))
            filter = filter.Where(row => row.ScopeType == "Tenant" && row.TenantNId == tenantNId);
        else
            return null;
        var row = await filter.FirstAsync(cancellationToken);
        return row is null ? null : Restore(row);
    }

    public async Task<CodingRuleDefinition?> GetPublishedInScopeAsync(
        CodingRuleDefinition rule, CancellationToken cancellationToken)
    {
        var row = await Db.Queryable<CodingRuleDefinitionRow>().AS(Definitions)
            .Where(item => !item.IsDeleted && item.TenantNId == rule.TenantNId && item.NId == rule.NId
                && item.Status == "Published")
            .FirstAsync(cancellationToken);
        return row is null ? null : Restore(row);
    }

    public async Task<int> GetNextRevisionAsync(
        CodingRuleDefinition rule, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var revision = await Db.Queryable<CodingRuleDefinitionRow>().AS(Definitions)
            .Where(row => !row.IsDeleted && row.TenantNId == rule.TenantNId && row.NId == rule.NId)
            .MaxAsync(row => row.Revision);
        if (revision == int.MaxValue) throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
        return revision + 1;
    }

    public async Task CreateAsync(
        CodingRuleDefinition rule, CodingRuleChange? source, CancellationToken cancellationToken)
    {
        await Db.Ado.BeginTranAsync();
        try
        {
            if (source is not null)
            {
                var count = await Db.Updateable(Row(source.Rule)).AS(Definitions)
                    .Where(row => row.Id == source.Rule.Id && !row.IsDeleted
                        && row.OptimisticVersion == source.ExpectedOptimisticVersion
                        && row.ConcurrencyVersion == source.ExpectedConcurrencyVersion)
                    .ExecuteCommandAsync(cancellationToken);
                if (count != 1) throw Conflict();
            }
            await Db.Insertable(Row(rule)).AS(Definitions).ExecuteCommandAsync(cancellationToken);
            await Db.Ado.CommitTranAsync();
        }
        catch (Exception exception)
        {
            await Db.Ado.RollbackTranAsync();
            ThrowWriteError(exception, source is not null);
            throw;
        }
    }

    public async Task SaveAsync(
        IReadOnlyList<CodingRuleChange> changes, CancellationToken cancellationToken)
    {
        await Db.Ado.BeginTranAsync();
        try
        {
            foreach (var change in changes)
            {
                var rule = change.Rule;
                var count = await Db.Updateable(Row(rule)).AS(Definitions)
                    .Where(row => row.Id == rule.Id && row.TenantNId == rule.TenantNId && !row.IsDeleted
                        && row.OptimisticVersion == change.ExpectedOptimisticVersion
                        && row.ConcurrencyVersion == change.ExpectedConcurrencyVersion)
                    .ExecuteCommandAsync(cancellationToken);
                if (count != 1) throw Conflict();
                if (rule.Status == PublicationStatus.Published)
                    await ReferenceDataOutboxWriter.InsertAsync(Db, "coding-rule",
                        new ReferenceCodingRulePublishedV1(rule.TenantNId, rule.ScopeType.ToString(),
                            null, rule.Id, rule.NId, rule.Revision), cancellationToken);
            }
            await ReferenceDataCacheGenerationStore.AdvanceAsync(Db,
                changes.Where(change => change.Rule.Status != PublicationStatus.Draft)
                    .Select(change => ReferenceDataCacheKeys.CodingRules(change.Rule.NId)),
                cancellationToken);
            await Db.Ado.CommitTranAsync();
        }
        catch (Exception exception)
        {
            await Db.Ado.RollbackTranAsync();
            ThrowWriteError(exception, concurrencyOnUnique: true);
            throw;
        }
    }

    public async Task<CodingRuleGeneratedValue> GenerateAsync(
        CodingRuleGeneration generation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Postgres) await Db.Ado.ExecuteCommandAsync("PRAGMA busy_timeout=10000");
        await Db.Ado.BeginTranAsync();
        try
        {
            await DeleteExpiredIdempotencyAsync(generation, cancellationToken);
            var existing = await FindIdempotencyAsync(generation, cancellationToken);
            if (existing is not null)
            {
                var replay = Replay(existing, generation.RequestHash);
                await Db.Ado.CommitTranAsync();
                return replay;
            }

            var periodKey = generation.Rule.PeriodKey(generation.GeneratedOn);
            var sequence = await NextSequenceAsync(generation, periodKey, cancellationToken);
            var rendered = generation.Rule.Render(
                generation.GeneratedOn, generation.TenantNId, generation.FactoryId, sequence);
            var row = new CodingRuleIdempotencyRow
            {
                Id = Guid.NewGuid(),
                TenantNId = generation.TenantNId,
                RuleNId = generation.Rule.NId,
                IdempotencyKeyHash = generation.IdempotencyKeyHash,
                RequestHash = generation.RequestHash,
                CodingRuleId = generation.Rule.Id,
                RuleRevision = generation.Rule.Revision,
                SourceScope = generation.Rule.ScopeType.ToString(),
                SourceTenantNId = generation.Rule.TenantNId,
                Code = rendered.Code,
                Sequence = sequence,
                PeriodKey = rendered.PeriodKey,
                GeneratedOn = generation.GeneratedOn,
                ExpiresOn = generation.ExpiresOn,
            };
            await Db.Insertable(row).AS(IdempotencyRecords).ExecuteCommandAsync(cancellationToken);
            await Db.Ado.CommitTranAsync();
            return new(row.Code, row.Sequence, row.PeriodKey, row.GeneratedOn);
        }
        catch (Exception exception)
        {
            await Db.Ado.RollbackTranAsync();
            if (IsUniqueViolation(exception))
            {
                var existing = await FindIdempotencyAsync(generation, cancellationToken);
                if (existing is not null) return Replay(existing, generation.RequestHash);
                throw Conflict();
            }
            ThrowDatabaseError(exception);
            throw;
        }
    }

    private async Task DeleteExpiredIdempotencyAsync(
        CodingRuleGeneration generation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Db.Ado.ExecuteCommandAsync($"""
            DELETE FROM {IdempotencyRecords}
            WHERE expires_on<=@GeneratedOn
            """,
            new SugarParameter("@GeneratedOn", generation.GeneratedOn));
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task<CodingRuleIdempotencyRow?> FindIdempotencyAsync(
        CodingRuleGeneration generation, CancellationToken cancellationToken) =>
        await Db.Queryable<CodingRuleIdempotencyRow>().AS(IdempotencyRecords)
            .Where(row => row.TenantNId == generation.TenantNId
                && row.RuleNId == generation.Rule.NId
                && row.IdempotencyKeyHash == generation.IdempotencyKeyHash)
            .FirstAsync(cancellationToken);

    private async Task<long> NextSequenceAsync(
        CodingRuleGeneration generation, string periodKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sequenceTarget = Postgres ? $"{Sequences} AS sequence_state" : Sequences;
        var currentValue = Postgres ? "sequence_state.last_value" : "last_value";
        var raw = await Db.Ado.GetScalarAsync($"""
            INSERT INTO {sequenceTarget}
                (coding_rule_id,rule_revision,period_key,context_hash,last_value,created_on,last_updated_on)
            VALUES (@RuleId,@Revision,@PeriodKey,@ContextHash,1,@Now,@Now)
            ON CONFLICT(coding_rule_id,rule_revision,period_key,context_hash) DO UPDATE SET
                last_value={currentValue}+1,
                last_updated_on=excluded.last_updated_on
            WHERE {currentValue}<9223372036854775807
            RETURNING {currentValue}
            """,
            new SugarParameter("@RuleId", generation.Rule.Id),
            new SugarParameter("@Revision", generation.Rule.Revision),
            new SugarParameter("@PeriodKey", periodKey),
            new SugarParameter("@ContextHash", generation.ContextHash),
            new SugarParameter("@Now", generation.GeneratedOn));
        cancellationToken.ThrowIfCancellationRequested();
        if (raw is null or DBNull) throw Conflict();
        return Convert.ToInt64(raw, CultureInfo.InvariantCulture);
    }

    private CodingRuleDefinition Restore(CodingRuleDefinitionRow row) => CodingRuleDefinition.Restore(new(
        row.Id,
        row.NId,
        row.Name,
        row.TargetEntityNId,
        row.Template,
        Enum.Parse<CodingResetPolicy>(row.ResetPolicy),
        Enum.Parse<ReferenceScopeType>(row.ScopeType),
        row.TenantNId,
        row.Revision,
        Enum.Parse<PublicationStatus>(row.Status),
        row.SourceRevision,
        row.PublishedOn is null ? null : Timestamp(row.PublishedOn.Value),
        row.PublishedBy,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        Timestamp(row.CreatedOn),
        Timestamp(row.LastUpdatedOn),
        row.OptimisticVersion,
        row.ConcurrencyVersion));

    private DateTimeOffset Timestamp(DateTimeOffset value) =>
        Postgres ? value.ToUniversalTime() : new(value.DateTime, TimeSpan.Zero);

    private static CodingRuleDefinitionRow Row(CodingRuleDefinition rule) => new()
    {
        Id = rule.Id,
        TenantNId = rule.TenantNId,
        ScopeType = rule.ScopeType.ToString(),
        NId = rule.NId,
        Name = rule.Name,
        TargetEntityNId = rule.TargetEntityNId,
        Template = rule.Template,
        ResetPolicy = rule.ResetPolicy.ToString(),
        Revision = rule.Revision,
        Status = rule.Status.ToString(),
        SourceRevision = rule.SourceRevision,
        PublishedOn = rule.PublishedOn,
        PublishedBy = rule.PublishedBy,
        IsFrozen = rule.IsFrozen,
        IsLocked = rule.IsLocked,
        IsDeleted = rule.IsDeleted,
        EntityType = rule.EntityType,
        CreatedOn = rule.CreatedOn,
        LastUpdatedOn = rule.LastUpdatedOn,
        OptimisticVersion = rule.OptimisticVersion,
        ConcurrencyVersion = rule.ConcurrencyVersion,
    };

    private CodingRuleGeneratedValue Replay(CodingRuleIdempotencyRow row, string requestHash)
    {
        if (!string.Equals(row.RequestHash, requestHash, StringComparison.Ordinal))
            throw new ReferenceDataException("REF-IDEMPOTENCY-CONFLICT", 409);
        return new(row.Code, row.Sequence, row.PeriodKey, Timestamp(row.GeneratedOn));
    }

    private static ReferenceDataException Conflict() =>
        new("REF-CONCURRENCY-CONFLICT", 409);

    private static void ThrowWriteError(Exception exception, bool concurrencyOnUnique)
    {
        if (exception is ReferenceDataException) return;
        if (IsUniqueViolation(exception))
            throw concurrencyOnUnique
                ? Conflict()
                : new ReferenceDataException("REF-VALIDATION-FAILED", 409, "nId");
        ThrowDatabaseError(exception);
    }

    private static void ThrowDatabaseError(Exception exception)
    {
        if (exception is ReferenceDataException) return;
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is DbException or SqlSugarException)
                throw new ReferenceDataException("503", 503);
    }

    private static bool IsUniqueViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is Microsoft.Data.Sqlite.SqliteException { SqliteExtendedErrorCode: 2067 or 1555 }
                || current is Npgsql.PostgresException { SqlState: "23505" }) return true;
        return false;
    }
}
