using System.Data.Common;
using System.Text.Json;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Application.Caching;
using IndustrialPlatform.ReferenceData.Application.Parameter;
using IndustrialPlatform.ReferenceData.Contracts.Parameter;
using IndustrialPlatform.ReferenceData.Contracts.Events;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Domain.Parameter;
using IndustrialPlatform.SharedKernel.Entities;
using IndustrialPlatform.ReferenceData.Infrastructure.Outbox;
using IndustrialPlatform.ReferenceData.Infrastructure.Caching;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Infrastructure.Parameter;

public sealed class ParameterRepository(SqlSugarDbContext context) : IParameterRepository
{
    private ISqlSugarClient Db => context.SqlSugar;
    private bool Postgres => Db.CurrentConnectionConfig.DbType == DbType.PostgreSQL;
    private string Table(string suffix) => (Postgres ? "reference_data.parameter_" : "reference_data_parameter_") + suffix;
    private ISugarQueryable<ParameterDomainRow> Visible(string tenant) => Db.Queryable<ParameterDomainRow>().AS(Table("app_domain"))
        .Where(row => !row.IsDeleted && (row.TenantNId == tenant || row.TenantNId == null));

    public async Task<(IReadOnlyList<ConfigurationDomainSummaryDto> Items, long Total)> SearchAsync(string tenantNId, ParameterQuery query, CancellationToken cancellationToken)
    {
        var filter = Visible(tenantNId);
        if (query.Keyword is { Length: > 0 } keyword)
        {
            keyword = keyword.ToUpperInvariant();
            filter = filter.Where(row => row.NId.Contains(keyword) || SqlFunc.Contains(SqlFunc.ToUpper(row.Name), keyword));
        }
        if (query.Status is not null) filter = filter.Where(row => row.Status == query.Status);
        if (query.ScopeType is not null) filter = filter.Where(row => row.ScopeType == query.ScopeType);
        var direction = query.Descending ? OrderByType.Desc : OrderByType.Asc;
        filter = query.SortField switch
        {
            "nId" => filter.OrderBy(row => row.NId, direction),
            "name" => filter.OrderBy(row => row.Name, direction),
            "revision" => filter.OrderBy(row => row.Revision, direction),
            "status" => filter.OrderBy(row => row.Status, direction),
            _ => filter.OrderBy(row => row.LastUpdatedOn, OrderByType.Desc),
        };
        var total = await filter.CountAsync(cancellationToken);
        var rows = await filter.OrderBy(row => row.Id).ToPageListAsync(query.PageIndex, query.PageSize, cancellationToken);
        var ids = rows.Select(row => row.Id).ToArray();
        var counts = ids.Length == 0 ? [] : await Db.Queryable<ParameterKeyRow>().AS(Table("key"))
            .Where(row => ids.Contains(row.ConfigurationAppDomainId) && !row.IsDeleted).GroupBy(row => row.ConfigurationAppDomainId)
            .Select(row => new ParameterKeyCount { AppDomainId = row.ConfigurationAppDomainId, Count = SqlFunc.AggregateCount(row.Id) }).ToListAsync(cancellationToken);
        return (rows.Select(row => new ConfigurationDomainSummaryDto(row.Id, row.NId, row.Name, row.ScopeType, row.TenantNId, row.Status, row.Revision,
            counts.Find(count => count.AppDomainId == row.Id)?.Count ?? 0, Timestamp(row.LastUpdatedOn), row.OptimisticVersion, row.ConcurrencyVersion, row.IsFrozen, row.IsLocked)).ToArray(), total);
    }

    public async Task<ConfigurationAppDomain?> GetAsync(string tenantNId, Guid id, CancellationToken cancellationToken)
    {
        var rows = await Visible(tenantNId).Where(row => row.Id == id).ToListAsync(cancellationToken);
        var loaded = await LoadAsync(rows, cancellationToken);
        return loaded.Count == 0 ? null : loaded[0];
    }

    public async Task<IReadOnlyList<ConfigurationAppDomain>> GetActiveScopesAsync(string tenantNId, string nId, CancellationToken cancellationToken) =>
        await LoadAsync(await Visible(tenantNId).Where(row => row.NId == nId && row.Status == "Active").ToListAsync(cancellationToken), cancellationToken);

    public async Task CreateAsync(ConfigurationAppDomain domain, ConfigurationHistoryDto history, CancellationToken cancellationToken)
    {
        await Db.Ado.BeginTranAsync();
        try
        {
            await Db.Insertable(DomainRow(domain)).AS(Table("app_domain")).ExecuteCommandAsync(cancellationToken);
            await Db.Insertable(HistoryRow(history)).AS(Table("history")).ExecuteCommandAsync(cancellationToken);
            await WriteOutboxAsync(domain, history, cancellationToken);
            await ReferenceDataCacheGenerationStore.AdvanceAsync(Db,
                ReferenceDataCacheKeys.ConfigurationDomainEntries(domain.NId), cancellationToken);
            await Db.Ado.CommitTranAsync();
        }
        catch (Exception error)
        {
            await Db.Ado.RollbackTranAsync(); ThrowKnown(error); throw;
        }
    }

    public async Task SaveAsync(ConfigurationAppDomain domain, long expectedVersion, Guid expectedToken, ConfigurationHistoryDto history, CancellationToken cancellationToken)
    {
        await Db.Ado.BeginTranAsync();
        try
        {
            var affected = await Db.Updateable(DomainRow(domain)).AS(Table("app_domain"))
                .Where(row => row.Id == domain.Id && row.TenantNId == domain.TenantNId && !row.IsDeleted
                    && row.OptimisticVersion == expectedVersion && row.ConcurrencyVersion == expectedToken).ExecuteCommandAsync(cancellationToken);
            if (affected != 1) throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
            if (history.KeyId is Guid keyId)
            {
                var key = domain.Key(keyId);
                await WriteKeyAsync(KeyRow(key), history.ObjectType == "Key" && history.ChangeType == "Created");
                if (history.ObjectType == "Key" && history.ChangeType == "Created")
                    foreach (var value in key.MultiValues) { cancellationToken.ThrowIfCancellationRequested(); await WriteValueAsync(ValueRow(value), true); }
                if (history.ObjectType == "MultiValue") await WriteValueAsync(ValueRow(key.Value(history.ObjectId)), history.ChangeType == "Created");
            }
            await Db.Insertable(HistoryRow(history)).AS(Table("history")).ExecuteCommandAsync(cancellationToken);
            await WriteOutboxAsync(domain, history, cancellationToken);
            await ReferenceDataCacheGenerationStore.AdvanceAsync(Db,
                ReferenceDataCacheKeys.ConfigurationDomainEntries(domain.NId), cancellationToken);
            await Db.Ado.CommitTranAsync();
        }
        catch (Exception error)
        {
            await Db.Ado.RollbackTranAsync(); ThrowKnown(error); throw;
        }
    }

    private Task WriteOutboxAsync(ConfigurationAppDomain domain, ConfigurationHistoryDto history,
        CancellationToken cancellationToken)
    {
        ConfigurationKey? key = history.KeyId is Guid keyId ? domain.Key(keyId) : null;
        return ReferenceDataOutboxWriter.InsertAsync(Db, "parameter",
            new ReferenceConfigurationChangedV1(domain.TenantNId, domain.ScopeType.ToString(), null,
                domain.Id, history.FullNId, domain.Revision, domain.NId, key?.NId,
                key?.Settings.ValueMode.ToString(), history.ChangeType), cancellationToken);
    }

    public async Task<(IReadOnlyList<ConfigurationHistoryDto> Items, long Total)> HistoryAsync(Guid domainId, Guid? keyId, int pageIndex, int pageSize, CancellationToken cancellationToken)
    {
        var query = Db.Queryable<ParameterHistoryRow>().AS(Table("history")).Where(row => row.AppDomainId == domainId);
        if (keyId is not null) query = query.Where(row => row.KeyId == keyId);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(row => row.Revision).OrderBy(row => row.Id).ToPageListAsync(pageIndex, pageSize, cancellationToken);
        return (rows.Select(row => new ConfigurationHistoryDto(row.Id, row.AppDomainId, row.KeyId, row.ObjectType, row.ObjectId, row.FullNId, row.ChangeType, row.ChangeReason, row.BeforeSummary, row.AfterSummary, row.Revision, row.UserNId, row.TraceId, Timestamp(row.CreatedOn))).ToArray(), total);
    }

    private async Task<IReadOnlyList<ConfigurationAppDomain>> LoadAsync(List<ParameterDomainRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return [];
        var ids = rows.Select(row => row.Id).ToArray();
        var keys = await Db.Queryable<ParameterKeyRow>().AS(Table("key")).Where(row => ids.Contains(row.ConfigurationAppDomainId)).ToListAsync(cancellationToken);
        var keyIds = keys.Select(row => row.Id).ToArray();
        var values = keyIds.Length == 0 ? [] : await Db.Queryable<ParameterValueRow>().AS(Table("multi_value"))
            .Where(row => keyIds.Contains(row.ConfigurationKeyId)).ToListAsync(cancellationToken);
        return rows.Select(row => ConfigurationAppDomain.Restore(new(Stamp(row), row.NId, row.Name, row.Description, Enum.Parse<ReferenceScopeType>(row.ScopeType),
            row.TenantNId, Enum.Parse<ConfigurationStatus>(row.Status), row.Revision), keys.Where(key => key.ConfigurationAppDomainId == row.Id).Select(key =>
            ConfigurationKey.Restore(Stamp(key), key.ConfigurationAppDomainId, key.NId,
                new(key.Name, key.Description, Enum.Parse<ReferenceDataType>(key.DataType), Enum.Parse<ConfigurationValueMode>(key.ValueMode),
                    Scalar(key.DataType, key.ValueJson), Scalar(key.DataType, key.DefaultValueJson), key.IsMandatory, key.IsReadOnly, key.DictionaryNId, key.ReferenceTarget,
                    Enum.Parse<ConfigurationStatus>(key.Status), key.Sort),
                values.Where(value => value.ConfigurationKeyId == key.Id).Select(value => ConfigurationKeyMultiValue.Restore(Stamp(value), key.Id, value.NId,
                    new(value.Name, Scalar(key.DataType, value.ValueJson)!, value.Sort, value.IsDefault, value.Enabled))).ToArray(), key.HasHadValue)).ToArray())).ToArray();
    }

    private static ConfigurationScalar? Scalar(string type, string? text)
    {
        if (text is null) return null;
        using var json = JsonDocument.Parse(text);
        return ConfigurationScalar.Parse(Enum.Parse<ReferenceDataType>(type), json.RootElement);
    }
    // SQLite has no offset type. SqlSugar reads our UTC wall-clock values with the process-local offset.
    private DateTimeOffset Timestamp(DateTimeOffset value) => Postgres ? value.ToUniversalTime() : new(value.DateTime, TimeSpan.Zero);
    private ConfigurationEntityState Stamp(ParameterEntityRow row) => new(row.Id, row.IsFrozen, row.IsLocked, row.IsDeleted, Timestamp(row.CreatedOn), Timestamp(row.LastUpdatedOn), row.OptimisticVersion, row.ConcurrencyVersion);
    private static void Stamp(ParameterEntityRow row, Entity entity)
    {
        row.Id = entity.Id;
        row.IsFrozen = entity.IsFrozen;
        row.IsLocked = entity.IsLocked;
        row.IsDeleted = entity.IsDeleted;
        row.EntityType = entity.EntityType;
        row.CreatedOn = entity.CreatedOn;
        row.LastUpdatedOn = entity.LastUpdatedOn;
        row.OptimisticVersion = entity.OptimisticVersion;
        row.ConcurrencyVersion = entity.ConcurrencyVersion;
    }
    private static ParameterDomainRow DomainRow(ConfigurationAppDomain domain)
    {
        var row = new ParameterDomainRow { NId = domain.NId, Name = domain.Name, Description = domain.Description, ScopeType = domain.ScopeType.ToString(),
            TenantNId = domain.TenantNId, Revision = domain.Revision, Status = domain.Status.ToString() };
        Stamp(row, domain); return row;
    }
    private static ParameterKeyRow KeyRow(ConfigurationKey key)
    {
        var settings = key.Settings;
        var row = new ParameterKeyRow { ConfigurationAppDomainId = key.ConfigurationAppDomainId, NId = key.NId, Name = settings.Name, Description = settings.Description,
            DataType = settings.DataType.ToString(), ValueMode = settings.ValueMode.ToString(), ValueJson = settings.Value?.CanonicalValue, DefaultValueJson = settings.DefaultValue?.CanonicalValue,
            IsMandatory = settings.IsMandatory, IsReadOnly = settings.IsReadOnly, HasHadValue = key.HasHadValue, DictionaryNId = settings.DictionaryNId, ReferenceTarget = settings.ReferenceTarget,
            Status = settings.Status.ToString(), Sort = settings.Sort };
        Stamp(row, key); return row;
    }
    private static ParameterValueRow ValueRow(ConfigurationKeyMultiValue value)
    {
        var row = new ParameterValueRow { ConfigurationKeyId = value.ConfigurationKeyId, NId = value.NId, Name = value.Settings.Name,
            ValueJson = value.Settings.Value.CanonicalValue, CanonicalValueHash = value.Settings.Value.CanonicalValueHash, Sort = value.Settings.Sort,
            IsDefault = value.Settings.IsDefault, Enabled = value.Settings.Enabled };
        Stamp(row, value); return row;
    }
    private static ParameterHistoryRow HistoryRow(ConfigurationHistoryDto history) => new()
    {
        Id = history.Id,
        AppDomainId = history.AppDomainId,
        KeyId = history.KeyId,
        ObjectType = history.ObjectType,
        ObjectId = history.ObjectId,
        FullNId = history.FullNId,
        ChangeType = history.ChangeType,
        ChangeReason = history.ChangeReason,
        BeforeSummary = history.BeforeSummary,
        AfterSummary = history.AfterSummary,
        Revision = history.Revision,
        UserNId = history.UserNId,
        TraceId = history.TraceId,
        CreatedOn = history.CreatedOn,
    };

    private string JsonParameter(string parameter) => Postgres ? $"CAST({parameter} AS jsonb)" : parameter;

    private async Task WriteKeyAsync(ParameterKeyRow row, bool create)
    {
        var sql = create
            ? $"INSERT INTO {Table("key")} (id,is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version,configuration_app_domain_id,n_id,name,description,data_type,value_mode,value_json,default_value_json,is_mandatory,is_read_only,has_had_value,dictionary_nid,reference_target,status,sort) VALUES (@Id,@IsFrozen,@IsLocked,@IsDeleted,@EntityType,@CreatedOn,@LastUpdatedOn,@OptimisticVersion,@ConcurrencyVersion,@ConfigurationAppDomainId,@NId,@Name,@Description,@DataType,@ValueMode,{JsonParameter("@ValueJson")},{JsonParameter("@DefaultValueJson")},@IsMandatory,@IsReadOnly,@HasHadValue,@DictionaryNId,@ReferenceTarget,@Status,@Sort)"
            : $"UPDATE {Table("key")} SET is_frozen=@IsFrozen,is_locked=@IsLocked,is_deleted=@IsDeleted,entity_type=@EntityType,created_on=@CreatedOn,last_updated_on=@LastUpdatedOn,optimistic_version=@OptimisticVersion,concurrency_version=@ConcurrencyVersion,configuration_app_domain_id=@ConfigurationAppDomainId,n_id=@NId,name=@Name,description=@Description,data_type=@DataType,value_mode=@ValueMode,value_json={JsonParameter("@ValueJson")},default_value_json={JsonParameter("@DefaultValueJson")},is_mandatory=@IsMandatory,is_read_only=@IsReadOnly,has_had_value=@HasHadValue,dictionary_nid=@DictionaryNId,reference_target=@ReferenceTarget,status=@Status,sort=@Sort WHERE id=@Id";
        var changed = await Db.Ado.ExecuteCommandAsync(sql,
            new SugarParameter("@Id", row.Id),
            new SugarParameter("@IsFrozen", row.IsFrozen),
            new SugarParameter("@IsLocked", row.IsLocked),
            new SugarParameter("@IsDeleted", row.IsDeleted),
            new SugarParameter("@EntityType", row.EntityType),
            new SugarParameter("@CreatedOn", row.CreatedOn),
            new SugarParameter("@LastUpdatedOn", row.LastUpdatedOn),
            new SugarParameter("@OptimisticVersion", row.OptimisticVersion),
            new SugarParameter("@ConcurrencyVersion", row.ConcurrencyVersion),
            new SugarParameter("@ConfigurationAppDomainId", row.ConfigurationAppDomainId),
            new SugarParameter("@NId", row.NId),
            new SugarParameter("@Name", row.Name),
            new SugarParameter("@Description", row.Description),
            new SugarParameter("@DataType", row.DataType),
            new SugarParameter("@ValueMode", row.ValueMode),
            new SugarParameter("@ValueJson", row.ValueJson),
            new SugarParameter("@DefaultValueJson", row.DefaultValueJson),
            new SugarParameter("@IsMandatory", row.IsMandatory),
            new SugarParameter("@IsReadOnly", row.IsReadOnly),
            new SugarParameter("@HasHadValue", row.HasHadValue),
            new SugarParameter("@DictionaryNId", row.DictionaryNId),
            new SugarParameter("@ReferenceTarget", row.ReferenceTarget),
            new SugarParameter("@Status", row.Status),
            new SugarParameter("@Sort", row.Sort));
        if (changed != 1) throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
    }

    private async Task WriteValueAsync(ParameterValueRow row, bool create)
    {
        var sql = create
            ? $"INSERT INTO {Table("multi_value")} (id,is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version,configuration_key_id,n_id,name,value_json,canonical_value_hash,sort,is_default,enabled) VALUES (@Id,@IsFrozen,@IsLocked,@IsDeleted,@EntityType,@CreatedOn,@LastUpdatedOn,@OptimisticVersion,@ConcurrencyVersion,@ConfigurationKeyId,@NId,@Name,{JsonParameter("@ValueJson")},@CanonicalValueHash,@Sort,@IsDefault,@Enabled)"
            : $"UPDATE {Table("multi_value")} SET is_frozen=@IsFrozen,is_locked=@IsLocked,is_deleted=@IsDeleted,entity_type=@EntityType,created_on=@CreatedOn,last_updated_on=@LastUpdatedOn,optimistic_version=@OptimisticVersion,concurrency_version=@ConcurrencyVersion,configuration_key_id=@ConfigurationKeyId,n_id=@NId,name=@Name,value_json={JsonParameter("@ValueJson")},canonical_value_hash=@CanonicalValueHash,sort=@Sort,is_default=@IsDefault,enabled=@Enabled WHERE id=@Id";
        var changed = await Db.Ado.ExecuteCommandAsync(sql,
            new SugarParameter("@Id", row.Id),
            new SugarParameter("@IsFrozen", row.IsFrozen),
            new SugarParameter("@IsLocked", row.IsLocked),
            new SugarParameter("@IsDeleted", row.IsDeleted),
            new SugarParameter("@EntityType", row.EntityType),
            new SugarParameter("@CreatedOn", row.CreatedOn),
            new SugarParameter("@LastUpdatedOn", row.LastUpdatedOn),
            new SugarParameter("@OptimisticVersion", row.OptimisticVersion),
            new SugarParameter("@ConcurrencyVersion", row.ConcurrencyVersion),
            new SugarParameter("@ConfigurationKeyId", row.ConfigurationKeyId),
            new SugarParameter("@NId", row.NId),
            new SugarParameter("@Name", row.Name),
            new SugarParameter("@ValueJson", row.ValueJson),
            new SugarParameter("@CanonicalValueHash", row.CanonicalValueHash),
            new SugarParameter("@Sort", row.Sort),
            new SugarParameter("@IsDefault", row.IsDefault),
            new SugarParameter("@Enabled", row.Enabled));
        if (changed != 1) throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
    }

    private static void ThrowKnown(Exception error)
    {
        for (var current = error; current is not null; current = current.InnerException!)
        {
            if (current is Npgsql.PostgresException { SqlState: "23505" } postgres)
                throw new ReferenceDataException(postgres.ConstraintName == "parameter_enabled_value_uq" ? "REF-CONFIG-DUPLICATE-VALUE" : "REF-CONFIG-DUPLICATE-NID", 409);
            if (current is Microsoft.Data.Sqlite.SqliteException { SqliteExtendedErrorCode: 2067 or 1555 } sqlite)
                throw new ReferenceDataException(sqlite.Message.Contains("canonical_value_hash", StringComparison.Ordinal) ? "REF-CONFIG-DUPLICATE-VALUE" : "REF-CONFIG-DUPLICATE-NID", 409);
        }
        if (error is DbException or SqlSugarException) throw new ReferenceDataException("503", 503);
    }
}

internal sealed class ParameterKeyCount
{
    public Guid AppDomainId { get; set; }
    public int Count { get; set; }
}
