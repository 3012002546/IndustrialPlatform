using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Application.Caching;
using IndustrialPlatform.ReferenceData.Application.DynamicProperty;
using IndustrialPlatform.ReferenceData.Contracts.DynamicProperty;
using IndustrialPlatform.ReferenceData.Contracts.Events;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Domain.DynamicProperty;
using IndustrialPlatform.ReferenceData.Infrastructure.Caching;
using IndustrialPlatform.ReferenceData.Infrastructure.Outbox;
using IndustrialPlatform.SharedKernel.Entities;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Infrastructure.DynamicProperty;

public sealed class DynamicConfigurationRepository(SqlSugarDbContext context) : IDynamicConfigurationRepository
{
    private ISqlSugarClient Db => context.SqlSugar;
    private bool Postgres => Db.CurrentConnectionConfig.DbType == DbType.PostgreSQL;
    private string Table(string suffix) => (Postgres ? "reference_data." : "reference_data_") + "dynamic_property_" + suffix;
    private ISugarQueryable<DynamicDefinitionRow> Visible(string tenantNId) => Db.Queryable<DynamicDefinitionRow>().AS(Table("definition"))
        .Where(row => !row.IsDeleted && (row.TenantNId == null || row.TenantNId == tenantNId));

    public async Task<(IReadOnlyList<DynamicConfigurationSummaryDto> Items, long Total)> SearchAsync(string tenantNId, DynamicConfigurationQuery query, CancellationToken cancellationToken)
    {
        var filter = Visible(tenantNId);
        if (query.Keyword is { Length: > 0 } keyword)
        {
            keyword = keyword.ToUpperInvariant();
            filter = filter.Where(row => row.NId.Contains(keyword) || SqlFunc.Contains(SqlFunc.ToUpper(row.Name), keyword));
        }
        filter = query.Status is null ? filter.Where(row => row.Status == "Draft" || row.Status == "Published") : filter.Where(row => row.Status == query.Status);
        if (query.ScopeType is not null) filter = filter.Where(row => row.ScopeType == query.ScopeType);
        var direction = query.Descending ? OrderByType.Desc : OrderByType.Asc;
        filter = query.SortField switch
        {
            "nId" => filter.OrderBy(row => row.NId, direction), "name" => filter.OrderBy(row => row.Name, direction),
            "revision" => filter.OrderBy(row => row.Revision, direction), "status" => filter.OrderBy(row => row.Status, direction),
            "lastUpdatedOn" => filter.OrderBy(row => row.LastUpdatedOn, direction),
            _ => filter.OrderByDescending(row => row.LastUpdatedOn),
        };
        var total = await filter.CountAsync(cancellationToken);
        var rows = await filter.OrderBy(row => row.Id).ToPageListAsync(query.PageIndex, query.PageSize, cancellationToken);
        var ids = rows.Select(row => row.Id).ToArray();
        if (ids.Length == 0) return ([], total);
        var fields = await Db.Queryable<DynamicFieldRow>().AS(Table("field")).Where(row => ids.Contains(row.DefinitionId) && !row.IsDeleted)
            .GroupBy(row => row.DefinitionId).Select(row => new DynamicChildCount { DefinitionId = row.DefinitionId, Count = SqlFunc.AggregateCount(row.Id) }).ToListAsync(cancellationToken);
        var records = await Db.Queryable<DynamicRecordRow>().AS(Table("record")).Where(row => ids.Contains(row.DefinitionId) && !row.IsDeleted)
            .GroupBy(row => row.DefinitionId).Select(row => new DynamicChildCount { DefinitionId = row.DefinitionId, Count = SqlFunc.AggregateCount(row.Id) }).ToListAsync(cancellationToken);
        var values = await Db.Queryable<DynamicValueRow>().AS(Table("value")).Where(row => ids.Contains(row.DefinitionId) && !row.IsDeleted)
            .GroupBy(row => row.DefinitionId).Select(row => new DynamicChildCount { DefinitionId = row.DefinitionId, Count = SqlFunc.AggregateCount(row.Id) }).ToListAsync(cancellationToken);
        return (rows.Select(row => new DynamicConfigurationSummaryDto(row.Id, row.NId, row.Name, row.ScopeType, row.TenantNId, row.Revision,
            row.Status, fields.Find(item => item.DefinitionId == row.Id)?.Count ?? 0, records.Find(item => item.DefinitionId == row.Id)?.Count ?? 0,
            values.Find(item => item.DefinitionId == row.Id)?.Count ?? 0, row.OptimisticVersion, row.ConcurrencyVersion, Timestamp(row.LastUpdatedOn), row.IsFrozen, row.IsLocked,
            row.PublishedOn is null ? null : Timestamp(row.PublishedOn.Value))).ToArray(), total);
    }

    public async Task<DynamicConfigDefinition?> GetAsync(string tenantNId, Guid id, bool includeRecords, CancellationToken cancellationToken) =>
        await LoadAsync(await Visible(tenantNId).Where(row => row.Id == id).FirstAsync(cancellationToken), includeRecords, cancellationToken);

    public async Task<(int Records, long Values)> CountAsync(Guid definitionId, CancellationToken cancellationToken) =>
        (await Db.Queryable<DynamicRecordRow>().AS(Table("record")).Where(row => row.DefinitionId == definitionId && !row.IsDeleted).CountAsync(cancellationToken),
         await Db.Queryable<DynamicValueRow>().AS(Table("value")).Where(row => row.DefinitionId == definitionId && !row.IsDeleted).CountAsync(cancellationToken));

    public async Task<DynamicConfigDefinition?> GetEffectiveAsync(string tenantNId, string nId, CancellationToken cancellationToken)
    {
        var rows = await Visible(tenantNId).Where(row => row.NId == nId && row.Status == "Published").ToListAsync(cancellationToken);
        return await LoadAsync(rows.Find(row => row.TenantNId == tenantNId) ?? rows.Find(row => row.TenantNId is null), false, cancellationToken);
    }

    public async Task<DynamicConfigDefinition?> GetSnapshotAsync(string tenantNId, string nId, string sourceScope, int revision, CancellationToken cancellationToken)
    {
        var sourceTenant = sourceScope == "Platform" ? null : tenantNId;
        var row = await Visible(tenantNId).Where(row => row.NId == nId && row.TenantNId == sourceTenant && row.Revision == revision
            && row.PublishedOn != null && (row.Status == "Published" || row.Status == "Superseded" || row.Status == "Disabled")).FirstAsync(cancellationToken);
        return await LoadAsync(row, false, cancellationToken);
    }

    public async Task<DynamicConfigDefinition?> GetPublishedInScopeAsync(DynamicConfigDefinition definition, CancellationToken cancellationToken) =>
        await LoadAsync(await Db.Queryable<DynamicDefinitionRow>().AS(Table("definition"))
            .Where(row => !row.IsDeleted && row.TenantNId == definition.TenantNId && row.NId == definition.NId && row.Status == "Published")
            .FirstAsync(cancellationToken), false, cancellationToken);

    public async Task<DynamicConfigDefinition?> GetLastPublishedAsync(DynamicConfigDefinition definition, CancellationToken cancellationToken) =>
        await LoadAsync(await Db.Queryable<DynamicDefinitionRow>().AS(Table("definition"))
            .Where(row => row.TenantNId == definition.TenantNId && row.NId == definition.NId && row.PublishedOn != null && row.Id != definition.Id)
            .OrderByDescending(row => row.Revision).FirstAsync(cancellationToken), false, cancellationToken);

    public async Task<int> GetNextRevisionAsync(DynamicConfigDefinition definition, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var revision = await Db.Queryable<DynamicDefinitionRow>().AS(Table("definition"))
            .Where(row => row.TenantNId == definition.TenantNId && row.NId == definition.NId).MaxAsync(row => row.Revision);
        if (revision == int.MaxValue) throw Conflict();
        return revision + 1;
    }

    public async Task<(IReadOnlyList<DynamicConfigRecord> Items, long Total)> SearchRecordsAsync(Guid definitionId, DynamicRecordQuery query, bool enabledOnly, CancellationToken cancellationToken)
    {
        var filter = Db.Queryable<DynamicRecordRow>().AS(Table("record")).Where(row => row.DefinitionId == definitionId && !row.IsDeleted);
        if (enabledOnly) filter = filter.Where(row => row.Enabled);
        if (query.NId is not null) filter = filter.Where(row => row.NId == query.NId);
        if (query.Category is not null) filter = filter.Where(row => row.Category == query.Category);
        if (query.Keyword is { Length: > 0 } keyword)
        {
            keyword = keyword.ToUpperInvariant();
            filter = filter.Where(row => row.NId.Contains(keyword) || SqlFunc.Contains(SqlFunc.ToUpper(row.Name!), keyword));
        }
        var total = await filter.CountAsync(cancellationToken);
        var rows = await filter.OrderBy(row => row.Sort).OrderBy(row => row.NId).ToPageListAsync(query.PageIndex, query.PageSize, cancellationToken);
        if (rows.Count == 0) return ([], total);
        var fields = await FieldsAsync(definitionId, cancellationToken);
        var ids = rows.Select(row => row.Id).ToArray();
        var values = await Db.Queryable<DynamicValueRow>().AS(Table("value")).Where(row => row.DefinitionId == definitionId && ids.Contains(row.RecordId) && !row.IsDeleted).ToListAsync(cancellationToken);
        return (RestoreRecords(rows, values, fields), total);
    }

    public async Task CreateAsync(DynamicConfigDefinition definition, DynamicConfigurationChange? source, CancellationToken cancellationToken)
    {
        await Db.Ado.BeginTranAsync();
        try
        {
            if (source is not null)
            {
                // Acquire a row lock without changing the source snapshot; clone checks remain atomic with insertion.
                var changed = await Db.Ado.ExecuteCommandAsync($"UPDATE {Table("definition")} SET id=id WHERE id=@Id AND optimistic_version=@Version AND concurrency_version=@Token AND NOT is_deleted",
                    new SugarParameter("@Id", source.Definition.Id), new SugarParameter("@Version", source.ExpectedOptimisticVersion), new SugarParameter("@Token", source.ExpectedConcurrencyVersion));
                if (changed != 1) throw Conflict();
            }
            await Db.Insertable(Row(definition)).AS(Table("definition")).ExecuteCommandAsync(cancellationToken);
            foreach (var field in definition.Fields) await WriteFieldAsync(Row(field), true);
            foreach (var record in definition.Records)
            {
                await Db.Insertable(Row(record)).AS(Table("record")).ExecuteCommandAsync(cancellationToken);
                foreach (var value in record.Values) await WriteValueAsync(Row(value), true);
            }
            await Db.Ado.CommitTranAsync();
        }
        catch (Exception error) { await Db.Ado.RollbackTranAsync(); ThrowKnown(error); throw; }
    }

    public async Task SaveAsync(IReadOnlyList<DynamicConfigurationChange> changes, CancellationToken cancellationToken)
    {
        await Db.Ado.BeginTranAsync();
        try
        {
            foreach (var change in changes)
            {
                var definition = change.Definition;
                var changed = await Db.Updateable(Row(definition)).AS(Table("definition"))
                    .Where(row => row.Id == definition.Id && !row.IsDeleted && row.TenantNId == definition.TenantNId
                        && row.OptimisticVersion == change.ExpectedOptimisticVersion && row.ConcurrencyVersion == change.ExpectedConcurrencyVersion)
                    .ExecuteCommandAsync(cancellationToken);
                if (changed != 1) throw Conflict();
                if (change.SaveFields) await SaveFieldsAsync(definition, cancellationToken);
                if (change.RecordId is Guid recordId) await SaveRecordAsync(definition.Record(recordId), cancellationToken);
                if (definition.Status == PublicationStatus.Published)
                    await ReferenceDataOutboxWriter.InsertAsync(Db, "dynamic-property",
                        new ReferenceDynamicConfigurationPublishedV1(definition.TenantNId,
                            definition.ScopeType.ToString(), null, definition.Id, definition.NId,
                            definition.Revision), cancellationToken);
            }
            await ReferenceDataCacheGenerationStore.AdvanceAsync(Db,
                changes.Where(change => change.Definition.Status != PublicationStatus.Draft)
                    .Select(change => ReferenceDataCacheKeys.DynamicConfigurations(change.Definition.NId)),
                cancellationToken);
            await Db.Ado.CommitTranAsync();
            var published = changes.FirstOrDefault(change =>
                change.Definition.Status == PublicationStatus.Published);
            if (published is not null)
                Outbox.ReferenceDataMetrics.RecordDynamicPublication(
                    published.Definition.Records.Count);
        }
        catch (Exception error) { await Db.Ado.RollbackTranAsync(); ThrowKnown(error); throw; }
    }

    private async Task SaveFieldsAsync(DynamicConfigDefinition definition, CancellationToken cancellationToken)
    {
        var existing = await Db.Queryable<DynamicFieldRow>().AS(Table("field")).Where(row => row.DefinitionId == definition.Id).ToListAsync(cancellationToken);
        var ids = definition.Fields.Select(field => field.Id).ToHashSet();
        foreach (var removed in existing.Where(row => !ids.Contains(row.Id)))
            await Db.Deleteable<DynamicFieldRow>().AS(Table("field")).Where(row => row.Id == removed.Id).ExecuteCommandAsync(cancellationToken);
        foreach (var field in definition.Fields)
        {
            var old = existing.Find(row => row.Id == field.Id);
            if (old is null || old.ConcurrencyVersion != field.ConcurrencyVersion) await WriteFieldAsync(Row(field), old is null);
        }
    }

    private async Task SaveRecordAsync(DynamicConfigRecord record, CancellationToken cancellationToken)
    {
        var old = await Db.Queryable<DynamicRecordRow>().AS(Table("record")).Where(row => row.Id == record.Id).FirstAsync(cancellationToken);
        if (old is null) await Db.Insertable(Row(record)).AS(Table("record")).ExecuteCommandAsync(cancellationToken);
        else if (old.ConcurrencyVersion != record.ConcurrencyVersion)
            await Db.Updateable(Row(record)).AS(Table("record")).Where(row => row.Id == record.Id).ExecuteCommandAsync(cancellationToken);
        var existing = await Db.Queryable<DynamicValueRow>().AS(Table("value")).Where(row => row.RecordId == record.Id).ToListAsync(cancellationToken);
        var ids = record.Values.Select(value => value.Id).ToHashSet();
        foreach (var removed in existing.Where(row => !ids.Contains(row.Id)))
            await Db.Deleteable<DynamicValueRow>().AS(Table("value")).Where(row => row.Id == removed.Id).ExecuteCommandAsync(cancellationToken);
        foreach (var value in record.Values)
        {
            var before = existing.Find(row => row.Id == value.Id);
            if (before is null || before.ConcurrencyVersion != value.ConcurrencyVersion) await WriteValueAsync(Row(value), before is null);
        }
    }

    private async Task<DynamicConfigDefinition?> LoadAsync(DynamicDefinitionRow? row, bool includeRecords, CancellationToken cancellationToken)
    {
        if (row is null) return null;
        var fields = await FieldsAsync(row.Id, cancellationToken);
        IReadOnlyList<DynamicConfigRecord> records = [];
        if (includeRecords)
        {
            var rows = await Db.Queryable<DynamicRecordRow>().AS(Table("record")).Where(item => item.DefinitionId == row.Id && !item.IsDeleted).ToListAsync(cancellationToken);
            var values = await Db.Queryable<DynamicValueRow>().AS(Table("value")).Where(item => item.DefinitionId == row.Id && !item.IsDeleted).ToListAsync(cancellationToken);
            records = RestoreRecords(rows, values, fields);
        }
        return DynamicConfigDefinition.Restore(new(Stamp(row), row.NId, row.Name, row.Description, Enum.Parse<ReferenceScopeType>(row.ScopeType),
            row.TenantNId, row.Revision, Enum.Parse<PublicationStatus>(row.Status), row.PublishedOn is null ? null : Timestamp(row.PublishedOn.Value), row.PublishedBy), fields, records);
    }

    private async Task<IReadOnlyList<DynamicConfigFieldDefinition>> FieldsAsync(Guid definitionId, CancellationToken cancellationToken)
    {
        var rows = await Db.Queryable<DynamicFieldRow>().AS(Table("field")).Where(row => row.DefinitionId == definitionId && !row.IsDeleted).ToListAsync(cancellationToken);
        return rows.Select(row =>
        {
            var type = Enum.Parse<ReferenceDataType>(row.DataType);
            return DynamicConfigFieldDefinition.Restore(Stamp(row), row.DefinitionId, row.NId,
                new(row.Name, type, row.Required, row.Enabled, row.Sort, row.DefaultValueJson is null ? null : Parse(type, row.DefaultValueJson),
                    row.MinLength, row.MaxLength, row.MinValue, row.MaxValue, row.Scale, row.Pattern, row.DictionaryNId, row.ReferenceTarget, row.Description), row.HasHadValue, row.WasPublished);
        }).OrderBy(field => field.Settings.Sort).ThenBy(field => field.NId, StringComparer.Ordinal).ToArray();
    }

    private DynamicConfigRecord[] RestoreRecords(IReadOnlyList<DynamicRecordRow> rows, IReadOnlyList<DynamicValueRow> values, IReadOnlyList<DynamicConfigFieldDefinition> fields)
    {
        var names = fields.ToDictionary(field => field.Id, field => field.NId);
        var children = values.ToLookup(value => value.RecordId);
        return rows.Select(row => DynamicConfigRecord.Restore(Stamp(row), row.DefinitionId, row.NId, new(row.Name, row.Category, row.Sort, row.Enabled),
            children[row.Id].Select(value => DynamicConfigFieldValue.Restore(Stamp(value), value.DefinitionId, value.RecordId, value.FieldId, names[value.FieldId], Scalar(value))).ToArray())).ToArray();
    }

    private ReferenceScalar Scalar(DynamicValueRow row)
    {
        var type = Enum.Parse<ReferenceDataType>(row.ValueType);
        var json = type switch
        {
            ReferenceDataType.String or ReferenceDataType.Enum => JsonSerializer.Serialize(row.StringValue!),
            ReferenceDataType.Integer => row.IntegerValue!.Value.ToString(CultureInfo.InvariantCulture),
            ReferenceDataType.Decimal => row.DecimalValue!.Value.ToString(CultureInfo.InvariantCulture),
            ReferenceDataType.Boolean => row.BooleanValue!.Value ? "true" : "false",
            ReferenceDataType.Date => JsonSerializer.Serialize(row.DateValue!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ReferenceDataType.DateTime => JsonSerializer.Serialize(Timestamp(row.DateTimeValue!.Value).ToString("O", CultureInfo.InvariantCulture)),
            ReferenceDataType.Json => row.JsonValue!,
            ReferenceDataType.Reference => JsonSerializer.Serialize(row.ReferenceValue!),
            _ => throw new ReferenceDataException("503", 503),
        };
        return Parse(type, json);
    }
    private static ReferenceScalar Parse(ReferenceDataType type, string json)
    {
        using var document = JsonDocument.Parse(json);
        return ReferenceScalar.Parse(type, document.RootElement);
    }
    private DynamicEntityState Stamp(DynamicEntityRow row) => new(row.Id, row.IsFrozen, row.IsLocked, row.IsDeleted,
        Timestamp(row.CreatedOn), Timestamp(row.LastUpdatedOn), row.OptimisticVersion, row.ConcurrencyVersion);
    private DateTimeOffset Timestamp(DateTimeOffset value) => Postgres ? value.ToUniversalTime() : new(value.DateTime, TimeSpan.Zero);
    private static T Stamp<T>(T row, Entity entity) where T : DynamicEntityRow
    {
        row.Id = entity.Id; row.IsFrozen = entity.IsFrozen; row.IsLocked = entity.IsLocked; row.IsDeleted = entity.IsDeleted;
        row.EntityType = entity.EntityType; row.CreatedOn = entity.CreatedOn; row.LastUpdatedOn = entity.LastUpdatedOn;
        row.OptimisticVersion = entity.OptimisticVersion; row.ConcurrencyVersion = entity.ConcurrencyVersion;
        return row;
    }
    private static DynamicDefinitionRow Row(DynamicConfigDefinition definition) => Stamp(new DynamicDefinitionRow
    {
        NId = definition.NId, Name = definition.Name, Description = definition.Description, ScopeType = definition.ScopeType.ToString(),
        TenantNId = definition.TenantNId, Revision = definition.Revision, Status = definition.Status.ToString(), PublishedOn = definition.PublishedOn, PublishedBy = definition.PublishedBy,
    }, definition);
    private static DynamicFieldRow Row(DynamicConfigFieldDefinition field)
    {
        var settings = field.Settings;
        return Stamp(new DynamicFieldRow
        {
            DefinitionId = field.DynamicConfigDefinitionId, NId = field.NId, Name = settings.Name, DataType = settings.DataType.ToString(),
            Required = settings.Required, Enabled = settings.Enabled, Sort = settings.Sort, DefaultValueJson = settings.DefaultValue?.CanonicalValue,
            MinLength = settings.MinLength, MaxLength = settings.MaxLength, MinValue = settings.MinValue, MaxValue = settings.MaxValue,
            Scale = settings.Scale, Pattern = settings.Pattern, DictionaryNId = settings.DictionaryNId, ReferenceTarget = settings.ReferenceTarget,
            Description = settings.Description, HasHadValue = field.HasHadValue, WasPublished = field.WasPublished,
        }, field);
    }
    private static DynamicRecordRow Row(DynamicConfigRecord record) => Stamp(new DynamicRecordRow
    {
        DefinitionId = record.DynamicConfigDefinitionId, NId = record.NId, Name = record.Settings.Name,
        Category = record.Settings.Category, Sort = record.Settings.Sort, Enabled = record.Settings.Enabled,
    }, record);
    private static DynamicValueRow Row(DynamicConfigFieldValue value)
    {
        var row = Stamp(new DynamicValueRow { DefinitionId = value.DynamicConfigDefinitionId, RecordId = value.DynamicConfigRecordId,
            FieldId = value.DynamicConfigFieldDefinitionId, ValueType = value.Value.DataType.ToString() }, value);
        var json = value.Value.JsonValue;
        switch (value.Value.DataType)
        {
            case ReferenceDataType.String: case ReferenceDataType.Enum: row.StringValue = json.GetString(); break;
            case ReferenceDataType.Integer: row.IntegerValue = json.GetInt64(); break;
            case ReferenceDataType.Decimal: row.DecimalValue = json.GetDecimal(); break;
            case ReferenceDataType.Boolean: row.BooleanValue = json.GetBoolean(); break;
            case ReferenceDataType.Date: row.DateValue = DateOnly.ParseExact(json.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture); break;
            case ReferenceDataType.DateTime: row.DateTimeValue = json.GetDateTimeOffset().ToUniversalTime(); break;
            case ReferenceDataType.Json: row.JsonValue = value.Value.CanonicalValue; break;
            case ReferenceDataType.Reference: row.ReferenceValue = json.GetString(); break;
        }
        return row;
    }

    private string JsonParameter(string parameter) => Postgres ? $"CAST({parameter} AS jsonb)" : parameter;
    private string DateParameter(string parameter) => Postgres ? $"CAST({parameter} AS date)" : parameter;
    private string TypedParameter(string parameter, string type) => Postgres ? $"CAST({parameter} AS {type})" : parameter;


    private async Task WriteFieldAsync(DynamicFieldRow row, bool create)
    {
        var sql = create
            ? $"INSERT INTO {Table("field")} (id,is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version,definition_id,n_id,name,data_type,required,enabled,sort,default_value_json,min_length,max_length,min_value,max_value,scale,pattern,dictionary_nid,reference_target,description,has_had_value,was_published) VALUES (@Id,@IsFrozen,@IsLocked,@IsDeleted,@EntityType,@CreatedOn,@LastUpdatedOn,@OptimisticVersion,@ConcurrencyVersion,@DefinitionId,@NId,@Name,@DataType,@Required,@Enabled,@Sort,{JsonParameter("@DefaultValueJson")},{TypedParameter("@MinLength","integer")},{TypedParameter("@MaxLength","integer")},{TypedParameter("@MinValue","numeric")},{TypedParameter("@MaxValue","numeric")},{TypedParameter("@Scale","integer")},@Pattern,@DictionaryNId,@ReferenceTarget,@Description,@HasHadValue,@WasPublished)"
            : $"UPDATE {Table("field")} SET is_frozen=@IsFrozen,is_locked=@IsLocked,is_deleted=@IsDeleted,entity_type=@EntityType,created_on=@CreatedOn,last_updated_on=@LastUpdatedOn,optimistic_version=@OptimisticVersion,concurrency_version=@ConcurrencyVersion,definition_id=@DefinitionId,n_id=@NId,name=@Name,data_type=@DataType,required=@Required,enabled=@Enabled,sort=@Sort,default_value_json={JsonParameter("@DefaultValueJson")},min_length={TypedParameter("@MinLength","integer")},max_length={TypedParameter("@MaxLength","integer")},min_value={TypedParameter("@MinValue","numeric")},max_value={TypedParameter("@MaxValue","numeric")},scale={TypedParameter("@Scale","integer")},pattern=@Pattern,dictionary_nid=@DictionaryNId,reference_target=@ReferenceTarget,description=@Description,has_had_value=@HasHadValue,was_published=@WasPublished WHERE id=@Id";
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
            new SugarParameter("@DefinitionId", row.DefinitionId),
            new SugarParameter("@NId", row.NId),
            new SugarParameter("@Name", row.Name),
            new SugarParameter("@DataType", row.DataType),
            new SugarParameter("@Required", row.Required),
            new SugarParameter("@Enabled", row.Enabled),
            new SugarParameter("@Sort", row.Sort),
            new SugarParameter("@DefaultValueJson", row.DefaultValueJson),
            new SugarParameter("@MinLength", row.MinLength),
            new SugarParameter("@MaxLength", row.MaxLength),
            new SugarParameter("@MinValue", row.MinValue),
            new SugarParameter("@MaxValue", row.MaxValue),
            new SugarParameter("@Scale", row.Scale),
            new SugarParameter("@Pattern", row.Pattern),
            new SugarParameter("@DictionaryNId", row.DictionaryNId),
            new SugarParameter("@ReferenceTarget", row.ReferenceTarget),
            new SugarParameter("@Description", row.Description),
            new SugarParameter("@HasHadValue", row.HasHadValue),
            new SugarParameter("@WasPublished", row.WasPublished));
        if (changed != 1) throw Conflict();
    }

    private async Task WriteValueAsync(DynamicValueRow row, bool create)
    {
        var sql = create
            ? $"INSERT INTO {Table("value")} (id,is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version,definition_id,record_id,field_id,value_type,string_value,integer_value,decimal_value,boolean_value,date_value,date_time_value,json_value,reference_value) VALUES (@Id,@IsFrozen,@IsLocked,@IsDeleted,@EntityType,@CreatedOn,@LastUpdatedOn,@OptimisticVersion,@ConcurrencyVersion,@DefinitionId,@RecordId,@FieldId,@ValueType,@StringValue,{TypedParameter("@IntegerValue","bigint")},{TypedParameter("@DecimalValue","numeric")},{TypedParameter("@BooleanValue","boolean")},{DateParameter("@DateValue")},{TypedParameter("@DateTimeValue","timestamptz")},{JsonParameter("@JsonValue")},@ReferenceValue)"
            : $"UPDATE {Table("value")} SET is_frozen=@IsFrozen,is_locked=@IsLocked,is_deleted=@IsDeleted,entity_type=@EntityType,created_on=@CreatedOn,last_updated_on=@LastUpdatedOn,optimistic_version=@OptimisticVersion,concurrency_version=@ConcurrencyVersion,definition_id=@DefinitionId,record_id=@RecordId,field_id=@FieldId,value_type=@ValueType,string_value=@StringValue,integer_value={TypedParameter("@IntegerValue","bigint")},decimal_value={TypedParameter("@DecimalValue","numeric")},boolean_value={TypedParameter("@BooleanValue","boolean")},date_value={DateParameter("@DateValue")},date_time_value={TypedParameter("@DateTimeValue","timestamptz")},json_value={JsonParameter("@JsonValue")},reference_value=@ReferenceValue WHERE id=@Id";
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
            new SugarParameter("@DefinitionId", row.DefinitionId),
            new SugarParameter("@RecordId", row.RecordId),
            new SugarParameter("@FieldId", row.FieldId),
            new SugarParameter("@ValueType", row.ValueType),
            new SugarParameter("@StringValue", row.StringValue),
            new SugarParameter("@IntegerValue", row.IntegerValue),
            new SugarParameter("@DecimalValue", row.DecimalValue),
            new SugarParameter("@BooleanValue", row.BooleanValue),
            new SugarParameter("@DateValue", row.DateValue?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new SugarParameter("@DateTimeValue", row.DateTimeValue),
            new SugarParameter("@JsonValue", row.JsonValue),
            new SugarParameter("@ReferenceValue", row.ReferenceValue));
        if (changed != 1) throw Conflict();
    }
    private static ReferenceDataException Conflict() => new("REF-CONCURRENCY-CONFLICT", 409);
    private static void ThrowKnown(Exception error)
    {
        for (var current = error; current is not null; current = current.InnerException!)
        {
            if (current is Npgsql.PostgresException { SqlState: "23505" } || current is Microsoft.Data.Sqlite.SqliteException { SqliteExtendedErrorCode: 2067 or 1555 })
                throw new ReferenceDataException("REF-DYNAMIC-CONFIG-DUPLICATE-NID", 409);
            if (current is Npgsql.PostgresException { SqlState: "23503" or "23514" } || current is Microsoft.Data.Sqlite.SqliteException { SqliteExtendedErrorCode: 787 or 275 })
                throw new ReferenceDataException("REF-DYNAMIC-CONFIG-FIELD-INVALID", 422);
        }
        if (error is DbException or SqlSugarException) throw new ReferenceDataException("503", 503);
    }
}

internal sealed class DynamicChildCount
{
    public Guid DefinitionId { get; set; }
    public int Count { get; set; }
}
