using System.Globalization;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Contracts;
using IndustrialPlatform.PlatformStudio.Domain;
using SqlSugar;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;

/// <summary>Studio服务内的资源/台账持久适配。所有对外读取限定可信租户，写入使用CAS和显式本地事务。</summary>
public sealed class StudioResourceStore(StudioDatabase database, TimeProvider clock) : IStudioResourceStore
{
    private ISqlSugarClient Db => database.Client;

    public async Task<T> TransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Db.Ado.Transaction is not null) return await action();
        await Db.Ado.BeginTranAsync();
        try { var result = await action(); cancellationToken.ThrowIfCancellationRequested(); await Db.Ado.CommitTranAsync(); return result; }
        catch { await Db.Ado.RollbackTranAsync(); throw; }
    }

    public async Task<StudioResource?> FindResourceByIdAsync(string tenantNId, Guid id, CancellationToken cancellationToken) =>
        await Db.Queryable<StudioResource>().Where(row => row.TenantNId == tenantNId && row.Id == id).FirstUtcAsync(database.Provider,cancellationToken);

    public async Task<StudioResource?> FindResourceAsync(string tenantNId, string nId, CancellationToken cancellationToken) =>
        await Db.Queryable<StudioResource>().Where(row => row.TenantNId == tenantNId && row.NId == nId).FirstUtcAsync(database.Provider,cancellationToken)!;

    private ISugarQueryable<StudioResource> ResourceQuery(StudioPrincipal principal, string kind, StudioQuery query)
    {
        var roles = principal.Roles.ToArray();
        var tenant = principal.TenantNId;
        var subject = principal.SubjectNId;
        var deleted = query.Status == "Deleted";
        return Db.Queryable<StudioResource>().Where(row => row.TenantNId == tenant && row.Kind == kind && row.IsDeleted == deleted)
            .WhereIF(!string.IsNullOrWhiteSpace(query.Keyword), row => row.Name.Contains(query.Keyword!) || row.NId.Contains(query.Keyword!))
            .WhereIF(query.Status == "Enabled", row => row.IsEnabled)
            .WhereIF(query.Status == "Disabled", row => !row.IsEnabled)
            .Where(row => SqlFunc.Subqueryable<StudioGrant>().Where(grant => grant.TenantNId == tenant && grant.ResourceId == row.Id && grant.Capability == "View" && grant.RevokedOn == null && (grant.SubjectKind == "User" && grant.SubjectNId == subject || grant.SubjectKind == "Role" && roles.Contains(grant.SubjectNId))).Any());
    }

    public async Task<IReadOnlyList<StudioResource>> ListResourcesAsync(StudioPrincipal principal, string kind, StudioQuery query, CancellationToken cancellationToken)
    {
        var data = ResourceQuery(principal, kind, query);
        var order = query.SortOrder == "asc" ? OrderByType.Asc : OrderByType.Desc;
        data = query.SortBy switch { "name" => data.OrderBy(row => row.Name, order), "createdOn" => data.OrderBy(row => row.CreatedOn, order), _ => data.OrderBy(row => row.LastUpdatedOn, order) };
        var rows = await data.OrderBy(row => row.Id).ToPageListAsync(query.Page, query.PageSize, cancellationToken);
        foreach(var row in rows) StudioSqliteUtc.Normalize(row,database.Provider);
        return rows;
    }
    public async Task<long> CountResourcesAsync(StudioPrincipal principal, string kind, StudioQuery query, CancellationToken cancellationToken) => await ResourceQuery(principal, kind, query).CountAsync(cancellationToken);
    public async Task InsertResourceAsync(StudioResource resource, CancellationToken cancellationToken) => await Db.Insertable(resource).ExecuteCommandAsync(cancellationToken);
    public async Task<bool> UpdateResourceAsync(StudioResource resource, long expectedVersion, Guid expectedConcurrencyVersion, CancellationToken cancellationToken) =>
        await Db.Updateable(resource).Where(row => row.Id == resource.Id && row.TenantNId == resource.TenantNId && row.OptimisticVersion == expectedVersion && row.ConcurrencyVersion == expectedConcurrencyVersion).ExecuteCommandAsync(cancellationToken) == 1;

    public async Task<StudioRevision?> FindRevisionAsync(string tenantNId, string nId, CancellationToken cancellationToken) => await Db.Queryable<StudioRevision>().Where(row => row.TenantNId == tenantNId && row.NId == nId).FirstUtcAsync(database.Provider,cancellationToken)!;
    public async Task<StudioRevision?> FindRevisionByIdAsync(string tenantNId, Guid id, CancellationToken cancellationToken) => await Db.Queryable<StudioRevision>().Where(row => row.TenantNId == tenantNId && row.Id == id).FirstUtcAsync(database.Provider,cancellationToken)!;
    public async Task<IReadOnlyList<StudioRevision>> ListRevisionsAsync(string tenantNId, Guid resourceId, CancellationToken cancellationToken) => await Db.Queryable<StudioRevision>().Where(row => row.TenantNId == tenantNId && row.ResourceId == resourceId).OrderBy(row => row.RevisionNo, OrderByType.Desc).ToUtcListAsync(database.Provider,cancellationToken);
    public async Task InsertRevisionAsync(StudioRevision revision, CancellationToken cancellationToken) => await Db.Insertable(revision).ExecuteCommandAsync(cancellationToken);
    public async Task<IReadOnlyList<StudioGrant>> GetGrantsAsync(string tenantNId, Guid resourceId, CancellationToken cancellationToken) => await Db.Queryable<StudioGrant>().Where(row => row.TenantNId == tenantNId && row.ResourceId == resourceId).ToUtcListAsync(database.Provider,cancellationToken);
    public async Task ReplaceGrantsAsync(string tenantNId, Guid resourceId, IReadOnlyList<StudioGrant> grants, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var previous = await GetGrantsAsync(tenantNId, resourceId, cancellationToken);
        foreach (var old in previous)
        {
            var active = grants.Any(grant => grant.SubjectKind == old.SubjectKind && grant.SubjectNId == old.SubjectNId && grant.Capability == old.Capability);
            DateTimeOffset? revoked = active ? null : old.RevokedOn ?? now;
            await Db.Updateable<StudioGrant>().SetColumns(row => new StudioGrant { RevokedOn = revoked }).Where(row => row.Id == old.Id && row.TenantNId == tenantNId).ExecuteCommandAsync(cancellationToken);
        }
        foreach (var grant in grants)
            if (!previous.Any(old => old.SubjectKind == grant.SubjectKind && old.SubjectNId == grant.SubjectNId && old.Capability == grant.Capability)) await Db.Insertable(grant).ExecuteCommandAsync(cancellationToken);
    }

    public async Task<StudioSource?> FindSourceAsync(string tenantNId, string nId, CancellationToken cancellationToken) => await Db.Queryable<StudioSource>().Where(row => row.TenantNId == tenantNId && row.NId == nId && !row.IsDeleted).FirstUtcAsync(database.Provider,cancellationToken)!;
    private ISugarQueryable<StudioSource> SourceQuery(string tenantNId, StudioQuery query) => Db.Queryable<StudioSource>().Where(row => row.TenantNId == tenantNId && !row.IsDeleted)
        .WhereIF(!string.IsNullOrWhiteSpace(query.Keyword), row => row.Name.Contains(query.Keyword!) || row.NId.Contains(query.Keyword!));
    public async Task<IReadOnlyList<StudioSource>> ListSourcesAsync(string tenantNId, StudioQuery query, CancellationToken cancellationToken) => await SourceQuery(tenantNId, query).OrderBy(row => row.Name).OrderBy(row => row.Id).ToPageListAsync(query.Page, query.PageSize, cancellationToken);
    public async Task<long> CountSourcesAsync(string tenantNId, StudioQuery query, CancellationToken cancellationToken) => await SourceQuery(tenantNId, query).CountAsync(cancellationToken);
    public async Task InsertSourceAsync(StudioSource source, CancellationToken cancellationToken) => await Db.Insertable(source).ExecuteCommandAsync(cancellationToken);
    public async Task<bool> UpdateSourceAsync(StudioSource source, long expectedVersion, Guid concurrencyVersion, CancellationToken cancellationToken) =>
        await Db.Updateable(source).Where(row => row.Id == source.Id && row.TenantNId == source.TenantNId && row.OptimisticVersion == expectedVersion && row.ConcurrencyVersion == concurrencyVersion).ExecuteCommandAsync(cancellationToken) == 1;

    public async Task<StudioOperationReceipt?> FindReceiptAsync(string tenantNId, string kind, string operationKey, CancellationToken cancellationToken) => await Db.Queryable<StudioOperationReceipt>().Where(row => row.TenantNId == tenantNId && row.OperationKind == kind && row.OperationKey == operationKey).FirstUtcAsync(database.Provider,cancellationToken)!;
    public async Task InsertReceiptAsync(StudioOperationReceipt receipt, CancellationToken cancellationToken) => await Db.Insertable(receipt).ExecuteCommandAsync(cancellationToken);
    public async Task<bool> ClaimReceiptAsync(Guid id, string tenantNId, CancellationToken cancellationToken) => await Db.Updateable<StudioOperationReceipt>().SetColumns(row => new StudioOperationReceipt { ErrorCode = "DISPATCHED" }).Where(row => row.Id == id && row.TenantNId == tenantNId && row.Status == "Pending" && row.ErrorCode == null).ExecuteCommandAsync(cancellationToken) == 1;
    public async Task UpdateReceiptAsync(StudioOperationReceipt receipt, CancellationToken cancellationToken) => await Db.Updateable(receipt).Where(row => row.Id == receipt.Id && row.TenantNId == receipt.TenantNId && row.ActorUserNId == receipt.ActorUserNId && (row.Status == "Pending" || row.Status == "Unknown" || row.Status == receipt.Status && row.ResponseJson == receipt.ResponseJson)).ExecuteCommandAsync(cancellationToken);

    public async Task AppendAuditAsync(StudioPrincipal principal, string action, string resourceNId, JsonObject summary, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var eventNId = "audit-" + Guid.NewGuid().ToString("N");
        var payload = new JsonObject { ["producerServiceKey"] = "platform-studio", ["auditEventNId"] = eventNId, ["occurredOn"] = now.ToString("O", CultureInfo.InvariantCulture), ["actorUserNId"] = principal.SubjectNId, ["action"] = action, ["objectType"] = "StudioResource", ["objectNId"] = resourceNId, ["payloadJson"] = StudioJson.Canonicalize(summary), ["severity"] = "Info" };
        await Db.Insertable(new StudioAuditOutbox { NId = eventNId, TenantNId = principal.TenantNId, CreatedOn = now, AuditEventNId = eventNId, PayloadJson = payload.ToJsonString(), NextAttemptOn = now }).ExecuteCommandAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<StudioAuditOutbox>> PendingAuditsAsync(int count, CancellationToken cancellationToken) { var now=clock.GetUtcNow();return await Db.Queryable<StudioAuditOutbox>().Where(row => row.Status == "Pending" && row.NextAttemptOn <= now).OrderBy(row => row.CreatedOn).Take(count).ToUtcListAsync(database.Provider,cancellationToken); }
    public async Task MarkAuditSentAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken) => await Db.Updateable<StudioAuditOutbox>().SetColumns(row => new StudioAuditOutbox { Status = "Sent", SentOn = now }).Where(row => row.Id == id && row.Status == "Pending").ExecuteCommandAsync(cancellationToken);
    public async Task MarkAuditFailedAsync(Guid id, int attempts, DateTimeOffset nextAttempt, string code, CancellationToken cancellationToken) => await Db.Updateable<StudioAuditOutbox>().SetColumns(row => new StudioAuditOutbox { AttemptCount = attempts, NextAttemptOn = nextAttempt, LastErrorCode = code }).Where(row => row.Id == id && row.Status == "Pending").ExecuteCommandAsync(cancellationToken);

    public async Task<long> CountLedgerRecordsAsync(string tenantNId, Guid modelId, CancellationToken cancellationToken) => await Db.Queryable<StudioLedgerRecord>().Where(row => row.TenantNId == tenantNId && row.ModelResourceId == modelId).CountAsync(cancellationToken);
    public async Task<StudioLedgerRecord?> FindLedgerRecordAsync(string tenantNId, Guid modelId, string nId, CancellationToken cancellationToken) => await Db.Queryable<StudioLedgerRecord>().Where(row => row.TenantNId == tenantNId && row.ModelResourceId == modelId && row.NId == nId).FirstUtcAsync(database.Provider,cancellationToken)!;
    public async Task<IReadOnlyList<StudioLedgerRecord>> LoadLedgerRecordsAsync(string tenantNId, Guid modelId, CancellationToken cancellationToken) => await Db.Queryable<StudioLedgerRecord>().Where(row => row.TenantNId == tenantNId && row.ModelResourceId == modelId).ToUtcListAsync(database.Provider,cancellationToken);
    public async Task<StudioPage<StudioLedgerRecord>> QueryLedgerAsync(string tenantNId, Guid modelId, LedgerQuery query, IReadOnlyDictionary<string,string> indexColumns, CancellationToken cancellationToken)
    {
        var data = Db.Queryable<StudioLedgerRecord>().Where(row => row.TenantNId == tenantNId && row.ModelResourceId == modelId && row.IsDeleted == query.Deleted);
        var index = database.Table("studio_ledger_value");
        var records = database.Table("studio_ledger_record");
        var ordinal = 0;
        foreach (var filter in query.Filters ?? [])
        {
            var name = "lv" + ordinal++;
            var column = indexColumns[filter.FieldId];
            var value = LedgerQueryValue(filter.Value, column);
            var op = filter.Operator switch { "eq" => "=", "ne" => "<>", "gt" => ">", "ge" => ">=", "lt" => "<", "le" => "<=", _ => throw new StudioException("STUDIO_VALIDATION", "查询运算符未登记。") };
            data = data.Where($"EXISTS (SELECT 1 FROM {index} lv WHERE lv.record_id={records}.id AND lv.tenant_n_id=@{name}tenant AND lv.field_id=@{name}field AND lv.{column} {op} @{name}value)", new[] { new SugarParameter("@"+name+"tenant", tenantNId), new SugarParameter("@"+name+"field",filter.FieldId), new SugarParameter("@"+name+"value",value) });
        }
        if(query.Condition is not null)
        {
            var parameters=new List<SugarParameter>();var predicates=0;
            string Predicate(LedgerCondition condition)
            {
                if(++predicates>100)throw new StudioException("STUDIO_LIMIT","筛选条件超过100项。",429);
                if(condition.Operator is "And" or "Or")return "("+string.Join(condition.Operator=="And"?" AND ":" OR ",condition.Items!.Select(Predicate))+")";
                var name="lc"+predicates;var field=condition.FieldId!;var column=indexColumns[field];
                parameters.Add(new("@"+name+"tenant",tenantNId));parameters.Add(new("@"+name+"field",field));
                var basis=$"SELECT 1 FROM {index} lv WHERE lv.record_id={records}.id AND lv.tenant_n_id=@{name}tenant AND lv.field_id=@{name}field";
                if(condition.Operator=="IsNull"||condition.Value is null&&condition.Operator=="Eq")return $"NOT EXISTS ({basis})";
                if(condition.Value is null&&condition.Operator=="Ne")return $"EXISTS ({basis})";
                var op=condition.Operator switch {"Eq"=>"=","Ne"=>"<>","Gt"=>">","Ge"=>">=","Lt"=>"<","Le"=>"<=","In"=>"IN",_=>throw new StudioException("STUDIO_VALIDATION","过滤比较未登记。")};
                string right;
                if(condition.Operator=="In")
                {
                    var values=condition.Value!.AsArray();var names=new List<string>();
                    for(var i=0;i<values.Count;i++){var key="@"+name+"v"+i;parameters.Add(new(key,LedgerQueryValue(values[i],column)));names.Add(key);}
                    right="("+string.Join(',',names)+")";
                }
                else {right="@"+name+"value";parameters.Add(new(right,LedgerQueryValue(condition.Value,column)));}
                return $"EXISTS ({basis} AND lv.{column} {op} {right})";
            }
            data=data.Where(Predicate(query.Condition),parameters);
        }
        var count = await data.Clone().CountAsync(cancellationToken);
        var direction = query.SortOrder == "asc" ? "ASC" : "DESC";
        if(query.Sorts is {Count:>0} sorts)
        {
            var parts=new List<string>();
            foreach(var sortItem in sorts)
            {
                var field=sortItem.FieldId.Replace("'","''",StringComparison.Ordinal);
                var expression=$"(SELECT lv.{indexColumns[sortItem.FieldId]} FROM {index} lv WHERE lv.record_id={records}.id AND lv.field_id='{field}')";
                parts.Add($"CASE WHEN {expression} IS NULL THEN {(sortItem.Nulls=="First"?"0":"1")} ELSE {(sortItem.Nulls=="First"?"1":"0")} END ASC");
                parts.Add(expression+(sortItem.Direction=="Asc"?" ASC":" DESC"));
            }
            data=data.OrderBy(string.Join(',',parts)+$",{records}.id ASC");
        }
        else if (query.SortFieldId is { } sort)
        {
            var safeField = sort.Replace("'", "''", StringComparison.Ordinal);
            data = data.OrderBy($"(SELECT lv.{indexColumns[sort]} FROM {index} lv WHERE lv.record_id={records}.id AND lv.field_id='{safeField}') {direction}, {records}.id ASC");
        }
        else data = data.OrderBy(row => row.LastUpdatedOn, query.SortOrder == "asc" ? OrderByType.Asc : OrderByType.Desc).OrderBy(row => row.Id);
        var items = await data.ToPageListAsync(query.Page,query.PageSize,cancellationToken);
        foreach(var row in items) StudioSqliteUtc.Normalize(row,database.Provider);
        return new(items,query.Page,query.PageSize,count.ToString(CultureInfo.InvariantCulture));
    }
    private static object? LedgerQueryValue(JsonNode? value, string column) => column switch
    {
        "number_scaled" => StudioJson.ScaledDecimal(StudioJson.Text(value) ?? value?.ToJsonString(), "filters.value"),
        "bool_value" => value?.GetValue<bool>(),
        "time_value" => DateTimeOffset.Parse(StudioJson.Text(value)!, CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal).ToUniversalTime(),
        _ => StudioJson.Text(value),
    };
    public async Task InsertLedgerRecordAsync(StudioLedgerRecord record, CancellationToken cancellationToken) => await Db.Insertable(record).ExecuteCommandAsync(cancellationToken);
    public async Task<bool> UpdateLedgerRecordAsync(StudioLedgerRecord record, long expectedVersion, Guid concurrencyVersion, CancellationToken cancellationToken) => await Db.Updateable(record).Where(row => row.Id == record.Id && row.TenantNId == record.TenantNId && row.OptimisticVersion == expectedVersion && row.ConcurrencyVersion == concurrencyVersion).ExecuteCommandAsync(cancellationToken) == 1;
    public async Task ReplaceLedgerIndexesAsync(Guid recordId, IReadOnlyList<StudioLedgerValue> indexes, CancellationToken cancellationToken)
    {
        await Db.Deleteable<StudioLedgerValue>().Where(row => row.RecordId == recordId).ExecuteCommandAsync(cancellationToken);
        try { if (indexes.Count > 0) await Db.Insertable(indexes.ToList()).ExecuteCommandAsync(cancellationToken); }
        catch(Exception ex) when(StudioStorageErrors.IsUnique(ex)) { throw StudioStorageErrors.Unique(); }
    }
    public async Task<bool> IsResourceReferencedAsync(string tenantNId, string resourceNId, CancellationToken cancellationToken)
    {
        var escaped = resourceNId.Replace("\"", "\\\"", StringComparison.Ordinal);
        return await Db.Queryable<StudioRevision>().Where(row => row.TenantNId == tenantNId && row.DependenciesJson.Contains("\"resourceNId\":\"" + escaped + "\"")).AnyAsync(cancellationToken);
    }
}
