using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Contracts;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Application.Ledger;

/// <summary>固定存储的台账运行器：真值、类型索引、版本和审计同事务提交。</summary>
public sealed class StudioLedger(IStudioResourceStore store, StudioPublishing publishing, IStudioAuthorization authorization, StudioCommands commands, IStudioFiles files, TimeProvider clock)
{
    private async Task<(StudioResource,StudioRevision,JsonObject)> Model(StudioPrincipal principal,string nId,string? revision,CancellationToken ct)
    {
        var (resource,snapshot)=await publishing.RequireRuntimeAsync(principal,nId,revision,ct);
        if(resource.Kind!="LedgerModel") throw new StudioException("STUDIO_NOT_FOUND","台账模型不存在。",404);
        return (resource,snapshot,StudioPublishing.Parse(snapshot.DocumentJson));
    }
    public async Task<JsonObject> UpgradePreviewAsync(StudioPrincipal principal,string modelNId,VersionCommand target,CancellationToken ct)
    {
        var resource=await publishing.RequireResourceAsync(principal,modelNId,"design","Design",ct);
        if(resource.Kind!="LedgerModel")throw new StudioException("STUDIO_NOT_FOUND","台账模型不存在。",404);
        StudioPublishing.MatchVersion(resource,target);
        var proposed=StudioPublishing.Parse(resource.DraftJson);var count=await store.CountLedgerRecordsAsync(principal.TenantNId,resource.Id,ct);
        var diagnostics=StudioDocuments.Validate("LedgerModel",proposed).ToList();
        if(resource.PublishedRevisionId is {} published)
        {
            var previous=await store.FindRevisionByIdAsync(principal.TenantNId,published,ct)??throw new StudioException("DEPENDENCY_CHANGED","模型发布修订不可用。",409);
            diagnostics.AddRange(StudioDocuments.Compatibility(StudioPublishing.Parse(previous.DocumentJson),proposed,count));
        }
        return new() { ["affectedCount"]=count.ToString(CultureInfo.InvariantCulture),["defaults"]=new JsonObject(),["conflicts"]=System.Text.Json.JsonSerializer.SerializeToNode(diagnostics,StudioCommands.JsonOptions),["compatible"]=!diagnostics.Any(d=>d.Severity=="Error"),["existingValuesChanged"]=false };
    }
    public async Task<StudioPage<LedgerRecordDto>> QueryAsync(StudioPrincipal principal,string modelNId,string? revisionNId,LedgerQuery query,CancellationToken ct)
    {
        authorization.RequirePermission(principal,"studio.ledger.read");
        var (resource,revision,model)=await Model(principal,modelNId,revisionNId,ct);
        if(query.Page<1||query.PageSize is <1 or >200||query.SortOrder is not ("asc" or "desc")||query.Filters?.Count>20) throw new StudioException("STUDIO_VALIDATION","查询分页或过滤预算无效。");
        var fields=Fields(model);
        var columns=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var filter in query.Filters??[])
        {
            if(!fields.TryGetValue(filter.FieldId,out var field)||field["queryable"]?.GetValue<bool>()!=true||field["retired"]?.GetValue<bool>()==true) throw new StudioException("STUDIO_VALIDATION","字段未声明为可查询。",path:"filters."+filter.FieldId);
            StudioDocuments.ValidateValue(field,filter.Value,"filters."+filter.FieldId);
            columns[filter.FieldId]=IndexColumn(field);
        }
        LedgerCondition ValidateCondition(LedgerCondition condition,int depth=0)
        {
            if(depth>32)throw new StudioException("STUDIO_LIMIT","筛选超过保护深度。",429);
            if(condition.Operator is "And" or "Or")
            {
                if(condition.Items is not {Count:>0 and <=20})throw new StudioException("STUDIO_VALIDATION","组合筛选缺少条件。");
                return condition with {Items=condition.Items.Select(c=>ValidateCondition(c,depth+1)).ToArray()};
            }
            if(condition.FieldId is not {} id||!fields.TryGetValue(id,out var field)||field["queryable"]?.GetValue<bool>()!=true||field["retired"]?.GetValue<bool>()==true||condition.Operator is not ("Eq" or "Ne" or "Gt" or "Ge" or "Lt" or "Le" or "In" or "IsNull"))throw new StudioException("STUDIO_VALIDATION","过滤字段或比较未登记。");
            columns[id]=IndexColumn(field);
            if(condition.Operator=="IsNull")return condition;
            if(condition.Operator=="In")
            {
                if(condition.Value is not JsonArray {Count:>0 and <=1000} array)throw new StudioException("STUDIO_VALIDATION","In需要非空受限值集合。");
                foreach(var item in array)StudioDocuments.ValidateValue(field,item,"filters."+id);
                return field["caseInsensitive"]?.GetValue<bool>()==true?condition with {Value=new JsonArray(array.Select(v=>(JsonNode?)JsonValue.Create(StudioJson.Text(v)?.ToUpperInvariant())).ToArray())}:condition;
            }
            if(condition.Value is not null)StudioDocuments.ValidateValue(field,condition.Value,"filters."+id);
            return field["caseInsensitive"]?.GetValue<bool>()==true&&StudioJson.Text(condition.Value) is {} text?condition with {Value=JsonValue.Create(text.ToUpperInvariant())}:condition;
        }
        if(query.Condition is not null)
        {
            if(Encoding.UTF8.GetByteCount(System.Text.Json.JsonSerializer.Serialize(query.Condition,StudioCommands.JsonOptions))>65536)throw new StudioException("STUDIO_LIMIT","筛选超过64KiB。",429);
            query=query with {Condition=ValidateCondition(query.Condition)};
        }
        foreach(var order in query.Sorts??[])
        {
            if(!fields.TryGetValue(order.FieldId,out var field)||field["sortable"]?.GetValue<bool>()!=true||field["retired"]?.GetValue<bool>()==true||order.Direction is not ("Asc" or "Desc")||order.Nulls is not ("First" or "Last"))throw new StudioException("STUDIO_VALIDATION","排序字段或方向未登记。");
            columns[order.FieldId]=IndexColumn(field);
        }
        if(query.SortFieldId is {} sort)
        {
            if(!fields.TryGetValue(sort,out var field)||field["sortable"]?.GetValue<bool>()!=true||field["retired"]?.GetValue<bool>()==true) throw new StudioException("STUDIO_VALIDATION","字段未声明为可排序。",path:"sortFieldId");
            columns[sort]=IndexColumn(field);
        }
        if(query.Filters is not null)query=query with { Filters=query.Filters.Select(f=>fields[f.FieldId]["caseInsensitive"]?.GetValue<bool>()==true&&StudioJson.Text(f.Value) is {} text?f with {Value=JsonValue.Create(text.ToUpperInvariant())}:f).ToArray() };
        var rows=await store.QueryLedgerAsync(principal.TenantNId,resource.Id,query,columns,ct);
        var revisions=new Dictionary<Guid,string> { [revision.Id]=revision.NId };
        var projected=new List<LedgerRecordDto>();
        foreach(var row in rows.Items)
        {
            if(!revisions.TryGetValue(row.SchemaRevisionId,out var savedRevision))
            {
                savedRevision=(await store.FindRevisionByIdAsync(principal.TenantNId,row.SchemaRevisionId,ct))?.NId??throw new StudioException("DEPENDENCY_CHANGED","记录结构修订不存在。",409);
                revisions[row.SchemaRevisionId]=savedRevision;
            }
            projected.Add(Dto(resource.NId,savedRevision,row,model));
        }
        return new(projected,rows.Page,rows.PageSize,rows.TotalCount);
    }
    public async Task<LedgerRecordDto> GetAsync(StudioPrincipal principal,string modelNId,string recordNId,string? revisionNId,CancellationToken ct)
    {
        authorization.RequirePermission(principal,"studio.ledger.read");
        var (resource,revision,model)=await Model(principal,modelNId,revisionNId,ct);
        var row=await store.FindLedgerRecordAsync(principal.TenantNId,resource.Id,recordNId,ct);
        if(row is null||row.IsDeleted) throw new StudioException("STUDIO_NOT_FOUND","台账记录不存在。",404);
        return Dto(modelNId,(row.SchemaRevisionId==revision.Id?revision:await store.FindRevisionByIdAsync(principal.TenantNId,row.SchemaRevisionId,ct)??throw new StudioException("DEPENDENCY_CHANGED","记录结构修订不存在。",409)).NId,row,model);
    }
    public async Task<LedgerRecordDto> WriteAsync(StudioPrincipal principal,string modelNId,string? recordNId,LedgerWrite request,string operationKey,CancellationToken ct)
    {
        authorization.RequirePermission(principal,"studio.ledger.write");
        var (resource,revision,model)=await Model(principal,modelNId,request.RevisionNId,ct);
        // 写入只允许当前发布模型，旧页面必须显式刷新，避免旧结构覆盖新字段。
        if(resource.PublishedRevisionId!=revision.Id) throw new StudioException("MODEL_SCHEMA_INCOMPATIBLE","模型已升级，请刷新页面后写入。",409);
        var input=request.Values??throw new StudioException("STUDIO_VALIDATION","台账值缺失。");
        if(Encoding.UTF8.GetByteCount(input.ToJsonString())>256*1024) throw new StudioException("STUDIO_LIMIT","台账值超过256KiB。",429);
        var values=StudioDocuments.ValidateValues(model,input,recordNId is null);
        if(Encoding.UTF8.GetByteCount(StudioJson.Canonicalize(values))>256*1024)throw new StudioException("STUDIO_LIMIT","台账值含默认值后超过256KiB。",429);
        foreach(var field in Fields(model).Values)
            if(StudioJson.Text(field["type"])=="FileReference"&&values[StudioJson.RequiredString(field,"id")] is JsonObject file)
            {
                if(!files.Enabled) throw new StudioException("STUDIO_NOT_READY","当前部署未安装文件能力。",503);
                await files.ValidateReferenceAsync(principal,StudioJson.RequiredString(file,"fileNId"),StudioJson.RequiredString(file,"referenceNId"),ct);
            }
        return await commands.ExecuteAsync(principal,"LedgerWrite",modelNId,operationKey,new { recordNId, request },async ()=>
        {
            var now=clock.GetUtcNow();
            StudioLedgerRecord row;
            if(recordNId is null)
            {
                var numbering=StudioJson.Text(model["numbering"]?["mode"]);
                if(numbering!="Manual") throw new StudioException("STUDIO_NOT_READY","规则编号提供者未登记，不能自动生成业务编号。",503);
                var nId=StudioPublishing.Required(request.NId,"nId",128);
                if(await store.FindLedgerRecordAsync(principal.TenantNId,resource.Id,nId,ct) is not null) throw new StudioException("STUDIO_VERSION_CONFLICT","台账编号已存在，删除后也不能复用。",409);
                row=new(now) { TenantNId=principal.TenantNId,NId=nId,ModelResourceId=resource.Id,SchemaRevisionId=revision.Id,CreatedBy=principal.SubjectNId,LastUpdatedBy=principal.SubjectNId,ValuesJson=StudioJson.Canonicalize(values) };
                await store.InsertLedgerRecordAsync(row,ct);
            }
            else
            {
                row=await store.FindLedgerRecordAsync(principal.TenantNId,resource.Id,recordNId,ct)??throw new StudioException("STUDIO_NOT_FOUND","台账记录不存在。",404);
                StudioPublishing.GuardWritable(row);
                StudioPublishing.MatchVersion(row,new(request.OptimisticVersion,request.ConcurrencyVersion));
                var oldVersion=row.OptimisticVersion;var oldToken=row.ConcurrencyVersion;
                // retired字段真值保留在存储，不再允许客户端编辑。
                var merged=JsonNode.Parse(row.ValuesJson)!.AsObject();
                foreach(var value in values) merged[value.Key]=value.Value?.DeepClone();
                row.ValuesJson=StudioJson.Canonicalize(merged);
                if(Encoding.UTF8.GetByteCount(row.ValuesJson)>256*1024)throw new StudioException("STUDIO_LIMIT","台账值含保留历史字段后超过256KiB。",429);
                row.SchemaRevisionId=revision.Id;row.LastUpdatedBy=principal.SubjectNId;
                row.MarkChanged(now);
                if(!await store.UpdateLedgerRecordAsync(row,oldVersion,oldToken,ct)) throw new StudioException("STUDIO_VERSION_CONFLICT","台账记录已被修改。",409);
            }
            await store.ReplaceLedgerIndexesAsync(row.Id,Indexes(row,model),ct);
            await store.AppendAuditAsync(principal,"studio.ledger.write",row.NId,new() { ["modelNId"]=modelNId,["revisionNId"]=revision.NId },ct);
            return Dto(modelNId,(row.SchemaRevisionId==revision.Id?revision:await store.FindRevisionByIdAsync(principal.TenantNId,row.SchemaRevisionId,ct)??throw new StudioException("DEPENDENCY_CHANGED","记录结构修订不存在。",409)).NId,row,model);
        },ct);
    }
    public async Task<LedgerRecordDto> SetStateAsync(StudioPrincipal principal,string modelNId,string recordNId,string state,VersionCommand request,string operationKey,CancellationToken ct)
    {
        authorization.RequirePermission(principal,"studio.ledger.write");
        var (resource,revision,model)=await Model(principal,modelNId,null,ct);
        return await commands.ExecuteAsync(principal,"LedgerState",modelNId,operationKey,new { recordNId,state,request },async ()=>
        {
            var row=await store.FindLedgerRecordAsync(principal.TenantNId,resource.Id,recordNId,ct)??throw new StudioException("STUDIO_NOT_FOUND","记录不存在。",404);
            StudioPublishing.MatchVersion(row,request);
            if(row.IsFrozen||row.IsLocked) throw new StudioException("STUDIO_VERSION_CONFLICT","记录已冻结或锁定。",409);
            var old=row.OptimisticVersion;var token=row.ConcurrencyVersion;
            switch(state)
            {
                case "delete": row.MarkDeleted(clock.GetUtcNow()); break;
                case "restore": row.Restore(clock.GetUtcNow()); break;
                default: throw new StudioException("STUDIO_VALIDATION","记录状态命令未登记。");
            }
            if(row.OptimisticVersion!=old)
            {
                row.LastUpdatedBy=principal.SubjectNId;
                if(!await store.UpdateLedgerRecordAsync(row,old,token,ct)) throw new StudioException("STUDIO_VERSION_CONFLICT","记录已被修改。",409);
                await store.ReplaceLedgerIndexesAsync(row.Id,row.IsDeleted?[]:Indexes(row,model),ct);
                await store.AppendAuditAsync(principal,"studio.ledger."+state,row.NId,new() { ["modelNId"]=modelNId },ct);
            }
            return Dto(modelNId,(row.SchemaRevisionId==revision.Id?revision:await store.FindRevisionByIdAsync(principal.TenantNId,row.SchemaRevisionId,ct)??throw new StudioException("DEPENDENCY_CHANGED","记录结构修订不存在。",409)).NId,row,model);
        },ct);
    }
    private static Dictionary<string,JsonObject> Fields(JsonObject model)=>(model["fields"] as JsonArray??[]).OfType<JsonObject>().ToDictionary(f=>StudioJson.RequiredString(f,"id"),StringComparer.Ordinal);
    private static string IndexColumn(JsonObject field)=>StudioJson.Text(field["type"]) switch
    {
        "String" or "Enum"=>"text_value","Decimal" or "Int32"=>"number_scaled","Boolean"=>"bool_value","Date" or "DateTime"=>"time_value",_=>throw new StudioException("STUDIO_VALIDATION","该字段类型不支持索引。")
    };
    private static List<StudioLedgerValue> Indexes(StudioLedgerRecord row,JsonObject model)
    {
        var values=JsonNode.Parse(row.ValuesJson)!.AsObject();var result=new List<StudioLedgerValue>();
        foreach(var (id,field) in Fields(model))
        {
            if(field["retired"]?.GetValue<bool>()==true||values[id] is not {} value||!(field["queryable"]?.GetValue<bool>()==true||field["sortable"]?.GetValue<bool>()==true||field["unique"]?.GetValue<bool>()==true)) continue;
            var index=new StudioLedgerValue { RecordId=row.Id,TenantNId=row.TenantNId,ModelResourceId=row.ModelResourceId,FieldId=id };
            var type=StudioJson.Text(field["type"]);string normalized;
            switch(IndexColumn(field))
            {
                case "text_value":
                    normalized=StudioJson.Text(value)!;
                    if(field["caseInsensitive"]?.GetValue<bool>()==true) normalized=normalized.ToUpperInvariant();
                    if(normalized.Length>512) throw new StudioException("STUDIO_VALIDATION","索引文本超过512字符。",path:"values."+id);
                    index.ValueKind="Text";index.TextValue=normalized;break;
                case "number_scaled":
                    index.ValueKind="Number";index.NumberScaled=StudioJson.ScaledDecimal(StudioJson.Text(value)??value.ToJsonString(),"values."+id);normalized=index.NumberScaled.Value.ToString(CultureInfo.InvariantCulture);break;
                case "bool_value":index.ValueKind="Boolean";index.BoolValue=value.GetValue<bool>();normalized=index.BoolValue.Value?"1":"0";break;
                default:
                    index.ValueKind="Time";index.TimeValue=type=="Date"?DateTimeOffset.ParseExact(StudioJson.Text(value)!,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal):DateTimeOffset.Parse(StudioJson.Text(value)!,CultureInfo.InvariantCulture).ToUniversalTime();
                    normalized=index.TimeValue.Value.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);break;
            }
            if(field["unique"]?.GetValue<bool>()==true) index.UniqueKey=StudioJson.Hash(type+"|"+(type is "String" or "Enum"?normalized.Trim():normalized));
            result.Add(index);
        }
        return result;
    }
    private static LedgerRecordDto Dto(string modelNId,string revisionNId,StudioLedgerRecord row,JsonObject model)
    {
        var stored=JsonNode.Parse(row.ValuesJson)!.AsObject();var values=new JsonObject();
        foreach(var (id,field) in Fields(model)) if(field["retired"]?.GetValue<bool>()!=true) values[id]=stored[id]?.DeepClone();
        return new(row.NId,modelNId,revisionNId,values,row.CreatedOn,row.LastUpdatedOn,row.CreatedBy,row.LastUpdatedBy,row.IsDeleted,row.OptimisticVersion.ToString(CultureInfo.InvariantCulture),row.ConcurrencyVersion);
    }
}
