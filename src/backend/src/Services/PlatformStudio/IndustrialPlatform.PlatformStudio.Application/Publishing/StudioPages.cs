using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Data;
using IndustrialPlatform.PlatformStudio.Contracts;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Application.Publishing;

public sealed record StudioPageInput(string? RevisionNId=null,JsonObject? Parameters=null,JsonObject? Document=null,JsonObject? Context=null);

/// <summary>页面实例、字段与动作共用解释器；运行只消费不可变发布修订。</summary>
public sealed class StudioPages(IStudioResourceStore store,StudioPublishing publishing,DatasetExecution datasets,IStudioAuthorization authorization,IStudioIdentity identity,IEnumerable<IStudioAction> actions,TimeProvider clock)
{
    private readonly IStudioAction[] _actions=actions.ToArray();
    public async Task<JsonObject> ContractAsync(StudioPrincipal principal,string nId,string? revisionNId,CancellationToken ct)
    {
        var (resource,revision)=await publishing.RequireRuntimeAsync(principal,nId,revisionNId,ct);
        if(resource.Kind!="LowCodePage")throw new StudioException("STUDIO_NOT_FOUND","低代码页面不存在。",404);
        var document=StudioPublishing.Parse(revision.DocumentJson);
        return new() { ["resource"]=JsonSerializer.SerializeToNode(await publishing.ToDtoAsync(resource,ct),StudioCommands.JsonOptions),["revisionNId"]=revision.NId,["document"]=document,["ledgerModel"]=await LedgerModelAsync(principal,document,ct),["instances"]=new JsonObject(),["diagnostics"]=new JsonArray() };
    }
    public async Task<JsonObject> RuntimeAsync(StudioPrincipal principal,string nId,StudioPageInput request,CancellationToken ct)
    {
        var (resource,revision)=await publishing.RequireRuntimeAsync(principal,nId,request.RevisionNId,ct);
        if(resource.Kind!="LowCodePage")throw new StudioException("STUDIO_NOT_FOUND","低代码页面不存在。",404);
        var document=StudioPublishing.Parse(revision.DocumentJson);
        if(request.Context?.ContainsKey("row")==true)throw new StudioException("STUDIO_VALIDATION","运行行上下文必须通过rowKey从已授权表格重新解析。",path:"context.row");
        var supplied=request.Context?.ContainsKey("rowKey")==true?new JsonObject { ["rowKey"]=request.Context["rowKey"]?.DeepClone() }:null;
        var context=await ContextAsync(principal,document,request.Parameters??new(),supplied,false,ct);
        return new() { ["resource"]=JsonSerializer.SerializeToNode(await publishing.ToDtoAsync(resource,ct),StudioCommands.JsonOptions),["revisionNId"]=revision.NId,["document"]=document,["instances"]=context.Instances,["fields"]=ResolveFields(document,context.Values),["ledgerModel"]=await LedgerModelAsync(principal,document,ct),["diagnostics"]=new JsonArray() };
    }
    public async Task<JsonObject> PreviewAsync(StudioPrincipal principal,string? nId,StudioPageInput request,CancellationToken ct)
    {
        authorization.RequirePermission(principal,"studio.lowcode.design");
        if(nId is not null)_=await publishing.RequireResourceAsync(principal,nId,"design","Design",ct);
        var document=request.Document??throw new StudioException("STUDIO_VALIDATION","试运行文档缺失。");
        var errors=StudioDocuments.Validate("LowCodePage",document);
        if(errors.FirstOrDefault(e=>e.Severity=="Error") is {} error)throw new StudioException(error.Code,error.Message,path:error.Path);
        var context=await ContextAsync(principal,document,request.Parameters??new(),request.Context,true,ct);
        return new() { ["revisionNId"]="draft",["document"]=document.DeepClone(),["instances"]=context.Instances,["fields"]=ResolveFields(document,context.Values),["preview"]=true,["ledgerModel"]=await LedgerModelAsync(principal,document,ct),["diagnostics"]=new JsonArray() };
    }
    public async Task<JsonObject> PreviewActionAsync(StudioPrincipal principal,JsonObject document,string actionId,JsonObject? supplied,CancellationToken ct)
    {
        authorization.RequirePermission(principal,"studio.lowcode.design");
        var (action,executor)=FindAction(document,actionId);
        authorization.RequirePermission(principal,executor.PermissionKey);
        var contexts=await ContextAsync(principal,document,supplied?["pageParameters"] as JsonObject??new(),supplied,true,ct);
        StudioPageValidation.ValidateForm(document,contexts.Values);
        var input=Input(action,executor,contexts.Values);
        var result=await executor.PreviewAsync(principal,input,ct);
        return new() { ["resolvedInput"]=input,["mockResult"]=result,["simulated"]=executor.Mutating,["diagnostics"]=new JsonArray(),["refreshPlan"]=action["success"]?.DeepClone() };
    }
    public async Task<JsonObject> ExecuteActionAsync(StudioPrincipal principal,string nId,string actionId,StudioPageInput request,string operationKey,CancellationToken ct)
    {
        principal=await identity.RefreshAsync(principal,ct);
        var (resource,revision)=await publishing.RequireRuntimeAsync(principal,nId,request.RevisionNId,ct);
        if(resource.Kind!="LowCodePage")throw new StudioException("STUDIO_NOT_FOUND","页面不存在。",404);
        var document=StudioPublishing.Parse(revision.DocumentJson);var (action,executor)=FindAction(document,actionId);authorization.RequirePermission(principal,executor.PermissionKey);
        var contexts=await ContextAsync(principal,document,request.Context?["pageParameters"] as JsonObject??request.Parameters??new(),request.Context,false,ct);
        StudioPageValidation.ValidateForm(document,contexts.Values);
        var input=Input(action,executor,contexts.Values);
        if(!executor.Mutating&&string.IsNullOrWhiteSpace(operationKey))operationKey="read-"+Guid.NewGuid().ToString("N");
        StudioCommands.ValidateKey(operationKey);
        var hash=StudioJson.Hash(StudioJson.Canonicalize(new JsonObject { ["page"]=nId,["revision"]=revision.NId,["action"]=actionId,["input"]=input.DeepClone() }));
        var receipt=await store.TransactionAsync(async ()=>
        {
            var previous=await store.FindReceiptAsync(principal.TenantNId,"PageAction",operationKey,ct);
            if(previous is not null)
            {
                if(previous.ActorUserNId!=principal.SubjectNId||previous.ResourceNId!=nId||previous.RequestHash!=hash)throw new StudioException("STUDIO_IDEMPOTENCY_CONFLICT","此幂等键已用于不同动作输入。",409);
                return previous;
            }
            var created=new StudioOperationReceipt { NId="receipt-"+Guid.NewGuid().ToString("N"),TenantNId=principal.TenantNId,CreatedOn=clock.GetUtcNow(),OperationKey=operationKey,ActorUserNId=principal.SubjectNId,OperationKind="PageAction",ResourceNId=nId,RequestHash=hash,ExecutorKey=executor.Key,ExecutorVersion=executor.ExecutorVersion };
            await store.InsertReceiptAsync(created,ct);return created;
        },ct);
        if(receipt.Status!="Pending")return await GetOperationAsync(principal,operationKey,ct);
        // 既有Pending/Unknown只可查对账，永不重发。新受理的标识只存在本调用栈。
        if(receipt.CompletedOn is not null)return Operation(receipt,action);
        var known=await executor.GetByOperationAsync(principal,ChildKey(receipt),ct);
        if(known is not null){await CompleteAsync(receipt,known,ct);return Operation(receipt,action);}
        // 将受理标记持久化后再调用，进程中断后同键只能查询，不会重新执行副作用。
        var dispatch=await store.ClaimReceiptAsync(receipt.Id,principal.TenantNId,ct);
        if(!dispatch)throw new StudioException("STUDIO_OPERATION_UNKNOWN","动作已受理，请查询操作状态。",409);
        try
        {
            var result=await executor.ExecuteAsync(principal,ChildKey(receipt),input,ct);
            await CompleteAsync(receipt,result,ct);
        }
        catch(Exception ex) when(ex is not OutOfMemoryException)
        {
            receipt.Status="Unknown";receipt.ErrorCode=ex is StudioException studio?studio.Code:"ACTION_RESULT_UNKNOWN";receipt.CompletedOn=clock.GetUtcNow();await store.UpdateReceiptAsync(receipt,CancellationToken.None);
        }
        return Operation(receipt,action);
    }
    public async Task<JsonObject> GetOperationAsync(StudioPrincipal principal,string operationKey,CancellationToken ct)
    {
        principal=await identity.RefreshAsync(principal,ct);
        var receipt=await store.FindReceiptAsync(principal.TenantNId,"PageAction",operationKey,ct);
        if(receipt is null||receipt.ActorUserNId!=principal.SubjectNId)throw new StudioException("STUDIO_NOT_FOUND","操作不存在。",404);
        _=await publishing.RequireResourceAsync(principal,receipt.ResourceNId,"run","Run",ct);
        var executor=_actions.SingleOrDefault(a=>a.Key==receipt.ExecutorKey&&a.ExecutorVersion==receipt.ExecutorVersion)??throw new StudioException("DEPENDENCY_CHANGED","原执行器合同不可用，不能读取动作结果。",409);
        authorization.RequirePermission(principal,executor.PermissionKey);
        var result=await executor.GetByOperationAsync(principal,ChildKey(receipt),ct);
        if(result is not null)await CompleteAsync(receipt,result,ct);
        else if(receipt.Status is "Succeeded" or "Failed")throw new StudioException("STUDIO_OPERATION_UNKNOWN","执行器未能重新确认结果访问权限。",409);
        receipt=await store.FindReceiptAsync(principal.TenantNId,"PageAction",operationKey,ct)??receipt;
        return Operation(receipt,null);
    }
    private async Task<JsonObject?> LedgerModelAsync(StudioPrincipal principal,JsonObject page,CancellationToken ct)
    {
        if(StudioJson.Text(page["ledgerModelRevisionNId"]) is not {} id)return null;
        var revision=await store.FindRevisionAsync(principal.TenantNId,id,ct)??throw new StudioException("DEPENDENCY_CHANGED","页面模型修订不可用。",409);
        var model=await store.FindResourceByIdAsync(principal.TenantNId,revision.ResourceId,ct)??throw new StudioException("STUDIO_NOT_FOUND","台账模型不存在。",404);
        authorization.RequirePermission(principal,"studio.ledger.read");
        _=await publishing.RequireRuntimeAsync(principal,model.NId,id,ct);
        var document=StudioPublishing.Parse(revision.DocumentJson);
        document["fields"]=new JsonArray((document["fields"] as JsonArray??[]).OfType<JsonObject>().Where(f=>f["retired"]?.GetValue<bool>()!=true).Select(f=>(JsonNode)f.DeepClone()).ToArray());
        return new() { ["nId"]=model.NId,["revisionNId"]=id,["document"]=document };
    }
    private async Task CompleteAsync(StudioOperationReceipt receipt,StudioActionResult result,CancellationToken ct)
    {
        if(result.Status is not ("Succeeded" or "Failed" or "Unknown" or "Pending"))throw new StudioException("STUDIO_VALIDATION","执行器回执状态未登记。");
        await store.TransactionAsync(async ()=>
        {
            var response=result.Result?.ToJsonString();
            if(response is not null&&System.Text.Encoding.UTF8.GetByteCount(response)>4*1024*1024)throw new StudioException("STUDIO_LIMIT","动作响应超过4MiB预算。",429);
            receipt.Status=result.Status;receipt.ResponseJson=response;receipt.ExternalReference=result.ExternalReference;receipt.ErrorCode=result.Status=="Pending"?"DISPATCHED":result.ErrorCode;receipt.CompletedOn=result.Status is "Succeeded" or "Failed"?clock.GetUtcNow():null;
            await store.UpdateReceiptAsync(receipt,ct);return true;
        },ct);
    }
    private static string ChildKey(StudioOperationReceipt receipt)=>"page-"+receipt.NId;
    private static JsonObject Operation(StudioOperationReceipt receipt,JsonObject? action)=>new() { ["operationKey"]=receipt.OperationKey,["status"]=receipt.Status,["result"]=receipt.ResponseJson is null?null:JsonNode.Parse(receipt.ResponseJson),["errorCode"]=receipt.ErrorCode=="DISPATCHED"?null:receipt.ErrorCode,["externalReference"]=receipt.ExternalReference,["refreshInstanceIds"]=receipt.Status=="Succeeded"?action?["success"]?["refreshInstanceIds"]?.DeepClone():new JsonArray() };
    private (JsonObject,IStudioAction) FindAction(JsonObject document,string actionId)
    {
        var action=(document["actions"] as JsonArray??[]).OfType<JsonObject>().SingleOrDefault(a=>StudioJson.Text(a["id"])==actionId)??throw new StudioException("STUDIO_NOT_FOUND","页面动作不存在。",404);
        var key=StudioJson.RequiredString(action,"actionKey");var version=StudioJson.RequiredString(action,"executorVersion");
        return (action,_actions.SingleOrDefault(a=>a.Key==key&&a.ExecutorVersion==version)??throw new StudioException("DEPENDENCY_CHANGED","执行器合同不可用。",409));
    }
    private static JsonObject Input(JsonObject action,IStudioAction executor,Dictionary<string,JsonNode?> contexts)
    {
        if(action["enabledWhen"] is JsonObject condition&&!StudioBinding.Evaluate(condition,contexts))throw new StudioException("STUDIO_FORBIDDEN","当前条件不允许执行动作。",403);
        var input=new JsonObject();
        foreach(var map in (action["inputMap"] as JsonArray??[]).OfType<JsonObject>())
        {
            var target=StudioJson.RequiredString(map,"target");
            if(input.ContainsKey(target))throw new StudioException("STUDIO_VALIDATION","动作目标字段重复。");
            input[target]=StudioBinding.Resolve(map["source"]!.AsObject(),contexts);
        }
        if(System.Text.Encoding.UTF8.GetByteCount(input.ToJsonString())>1024*1024)throw new StudioException("STUDIO_LIMIT","动作输入超过1MiB保护预算。",429);
        ValidateInput(input,executor.InputSchema,"input");return input;
    }
    public static void ValidateInput(JsonNode? input,JsonObject schema,string path)
    {
        if(input is null){if(schema["nullable"]?.GetValue<bool>()==true)return;throw new StudioException("PARAM_REQUIRED","动作输入不能为空。",path:path);}
        var type=StudioJson.Text(schema["type"]);
        if(type=="object")
        {
            if(input is not JsonObject obj)throw new StudioException("TYPE_MISMATCH","动作输入应为对象。",path:path);
            var properties=schema["properties"] as JsonObject??new();
            foreach(var key in (schema["required"] as JsonArray??[]).Select(StudioJson.Text))if(key is null||!obj.ContainsKey(key)||obj[key] is null)throw new StudioException("PARAM_REQUIRED","缺少必填动作输入。",path:path+"."+key);
            foreach(var (key,value) in obj)
            {
                if(properties[key] is JsonObject child)ValidateInput(value,child,path+"."+key);
                else if(schema["additionalProperties"]?.GetValue<bool>()!=true)throw new StudioException("STUDIO_VALIDATION","未声明的动作输入字段。",path:path+"."+key);
            }
        }
        else if(type=="string"&&(StudioJson.Text(input) is not {} text||text.Length>(schema["maxLength"]?.GetValue<int>()??65536)))throw new StudioException("TYPE_MISMATCH","动作输入需要文本。",path:path);
        else if(type=="boolean"&&(input is not JsonValue value||!value.TryGetValue<bool>(out _)))throw new StudioException("TYPE_MISMATCH","动作输入需要布尔值。",path:path);
        else if(type=="array")
        {
            if(input is not JsonArray array||array.Count>(schema["maxItems"]?.GetValue<int>()??1000)||array.Count<(schema["minItems"]?.GetValue<int>()??0)||schema["items"] is not JsonObject itemSchema)throw new StudioException("TYPE_MISMATCH","动作数组结构或预算无效。",path:path);
            foreach(var item in array)ValidateInput(item,itemSchema,path+"[]");
        }
        else if(type=="integer")
        {
            if(input is not JsonValue number||number.GetValueKind()!=JsonValueKind.Number||!long.TryParse(number.ToJsonString(),NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out _))throw new StudioException("TYPE_MISMATCH","动作输入需要精确整数。",path:path);
        }
        else if(type=="number")
        {
            if(input is not JsonValue number||number.GetValueKind()!=JsonValueKind.Number)throw new StudioException("TYPE_MISMATCH","动作输入需要数值。",path:path);
            DatasetValues.CheckScalar("Decimal",JsonValue.Create(number.ToJsonString())!,path,new());
        }
        else if(type is not ("object" or "string" or "boolean"))throw new StudioException("TYPE_MISMATCH","动作Schema含未登记类型。",path:path);
        if(schema["enum"] is JsonArray allowed&&!allowed.Any(value=>JsonNode.DeepEquals(input,value)))throw new StudioException("TYPE_MISMATCH","动作值不在声明枚举范围。",path:path);
    }
    private async Task<(JsonObject Instances,Dictionary<string,JsonNode?> Values)> ContextAsync(StudioPrincipal principal,JsonObject document,JsonObject parameters,JsonObject? supplied,bool preview,CancellationToken ct)
    {
        var definitions=new JsonObject { ["parameters"]=document["parameters"]?.DeepClone()??new JsonArray() };
        var pageParameters=DatasetValues.Parameters(principal,definitions,parameters);
        var form=supplied?["form"] as JsonObject??new();
        var declared=FormFields(document);
        foreach(var key in form.Select(x=>x.Key))if(!declared.Contains(key))throw new StudioException("STUDIO_VALIDATION","表单含未声明字段。",path:"context.form."+key);
        var context=new Dictionary<string,JsonNode?> { ["PageParam"]=pageParameters,["Form"]=form.DeepClone(),["Context"]=new JsonObject { ["tenant"]=principal.TenantNId,["user"]=principal.SubjectNId,["authorizedFactoryIds"]=new JsonArray(principal.FactoryNIds.Select(x=>(JsonNode)JsonValue.Create(x)!).ToArray()) } };
        var instances=new JsonObject();var pending=(document["instances"] as JsonArray??[]).OfType<JsonObject>().ToList();
        if(pending.Count>64)throw new StudioException("STUDIO_LIMIT","页面实例超过64项。",429);
        if(!preview&&supplied?.ContainsKey("row")==true)throw new StudioException("STUDIO_VALIDATION","运行行上下文必须通过rowKey从已授权表格重新解析。",path:"context.row");
        if(supplied?["rowKey"] is not null and not JsonObject)throw new StudioException("STUDIO_VALIDATION","行标识必须为对象。",path:"context.rowKey");
        var rowKey=supplied?["rowKey"] as JsonObject;
        string? rowInstance=null;JsonObject? rowTable=null;
        if(rowKey is not null)
        {
            rowInstance=StudioJson.RequiredString(rowKey,"instanceId");
            if(rowKey["key"] is null)throw new StudioException("PARAM_REQUIRED","行标识缺失。",path:"context.rowKey.key");
            var tables=Tables(document["layout"] as JsonObject).Where(t=>StudioJson.Text(t["props"]?["instanceId"])==rowInstance).Take(2).ToArray();
            if(tables.Length!=1||!pending.Any(i=>StudioJson.Text(i["instanceId"])==rowInstance))throw new StudioException("STUDIO_VALIDATION","行实例没有唯一的表格定义。",path:"context.rowKey.instanceId");
            rowTable=tables[0];
        }
        else if(preview&&supplied?["row"] is JsonObject previewRow)context["Row"]=previewRow.DeepClone();
        bool AwaitingRow(JsonNode? node)=>node switch
        {
            JsonObject obj=>(StudioJson.Text(obj["from"])=="Row"&&!context.ContainsKey("Row"))||(StudioJson.Text(obj["from"])=="Instance"&&context.ContainsKey("AwaitingRow:"+StudioJson.Text(obj["instanceId"])))||obj.Any(pair=>AwaitingRow(pair.Value)),
            JsonArray array=>array.Any(AwaitingRow),_=>false,
        };
        void SelectRow()
        {
            if(rowKey is null||context.ContainsKey("Row")||!context.ContainsKey("Instance:"+rowInstance))return;
            var collectionPath=rowTable!["props"]?["collectionPath"]?.DeepClone()??new JsonArray();
            var collection=StudioBinding.Resolve(new JsonObject { ["from"]="Instance",["instanceId"]=rowInstance,["path"]=collectionPath,["nullPolicy"]="Error" },context);
            if(collection is not JsonArray rows||rows.Any(row=>row is not JsonObject))throw new StudioException("STUDIO_VALIDATION","表格collectionPath必须指向已授权结果中的行对象数组。",path:"context.rowKey.instanceId");
            var rowPath=rowTable["props"]?["rowKeyPath"];
            var keys=new HashSet<string>(StringComparer.Ordinal);JsonObject? selected=null;
            foreach(var row in rows.Cast<JsonObject>())
            {
                var key=StudioBinding.Resolve(new JsonObject { ["from"]="Row",["path"]=rowPath is JsonArray?rowPath.DeepClone():new JsonArray(StudioJson.Text(rowPath)??"nId"),["nullPolicy"]="Error" },new Dictionary<string,JsonNode?> { ["Row"]=row });
                if(!keys.Add(StudioJson.Canonicalize(key)))throw new StudioException("STUDIO_ROW_KEY_AMBIGUOUS","已授权表格结果含重复行标识，无法安全选行。",409,path:"context.rowKey.key");
                if(JsonNode.DeepEquals(key,rowKey["key"]))
                {
                    if(selected is not null)throw new StudioException("STUDIO_ROW_KEY_AMBIGUOUS","已授权表格结果含重复行标识，无法安全选行。",409,path:"context.rowKey.key");
                    selected=row;
                }
            }
            if(selected is null)throw new StudioException("STUDIO_NOT_FOUND","此行当前不可见。",404,path:"context.rowKey.key");
            context["Row"]=selected;
        }
        long totalBytes=0;
        while(pending.Count>0)
        {
            SelectRow();
            var candidates=pending.Where(i=>(i["dependsOn"] as JsonArray??[]).All(d=>instances.ContainsKey(StudioJson.Text(d)!))).ToArray();
            if(candidates.Length==0)throw new StudioException("DEPENDENCY_CHANGED","数据实例依赖缺失或循环。",409);
            bool Blocked(JsonObject instance)=>AwaitingRow(instance["parameters"])||(instance["dependsOn"] as JsonArray??[]).Any(d=>context.ContainsKey("AwaitingRow:"+StudioJson.Text(d)));
            var ready=candidates.FirstOrDefault(i=>!Blocked(i))??candidates[0];
            var id=StudioJson.RequiredString(ready,"instanceId");var mapped=new JsonObject();
            if(Blocked(ready))
            {
                instances[id]=new JsonObject { ["status"]="AwaitingRow",["reasonCode"]="ROW_REQUIRED",["path"]="instances."+id };
                context["AwaitingRow:"+id]=JsonValue.Create(true);pending.Remove(ready);continue;
            }
            foreach(var map in (ready["parameters"] as JsonArray??[]).OfType<JsonObject>())mapped[StudioJson.RequiredString(map,"target")]=StudioBinding.Resolve(map["source"]!.AsObject(),context);
            var result=await datasets.ExecuteAsync(principal,StudioJson.RequiredString(ready,"datasetNId"),new(StudioJson.RequiredString(ready,"revisionNId"),mapped),ct);
            totalBytes+=System.Text.Encoding.UTF8.GetByteCount(result.ToJsonString());
            if(totalBytes>8*1024*1024)throw new StudioException("STUDIO_LIMIT","页面数据实例总结果超过8MiB保护预算。",429);
            instances[id]=result;context["Instance:"+id]=result["data"];pending.Remove(ready);
        }
        SelectRow();
        if(rowKey is not null&&!context.ContainsKey("Row"))throw new StudioException("DEPENDENCY_CHANGED","选行表格正在等待其他行上下文，无法解析行标识。",409,path:"context.rowKey.instanceId");
        return (instances,context);
    }
    private static HashSet<string> FormFields(JsonObject document)
    {
        var fields=(document["fields"] as JsonArray??[]).OfType<JsonObject>().Select(f=>StudioJson.Text(f["writeTarget"])).Where(f=>f is not null).Select(f=>f!).ToHashSet(StringComparer.Ordinal);
        void Walk(JsonObject? node){if(node is null)return;if(StudioJson.Text(node["type"])=="FormField"&&StudioJson.Text(node["props"]?["fieldId"]) is {} id)fields.Add(id);foreach(var child in (node["children"] as JsonArray??[]).OfType<JsonObject>())Walk(child);}
        Walk(document["layout"] as JsonObject);return fields;
    }
    private static IEnumerable<JsonObject> Tables(JsonObject? node)
    {
        if(node is null)yield break;
        if(StudioJson.Text(node["type"])=="Table")yield return node;
        foreach(var child in (node["children"] as JsonArray??[]).OfType<JsonObject>())foreach(var table in Tables(child))yield return table;
    }
    private static JsonArray ResolveFields(JsonObject document,Dictionary<string,JsonNode?> context)
    {
        var output=new JsonArray();
        bool AwaitingRow(JsonNode? node)=>node switch
        {
            JsonObject obj=>(StudioJson.Text(obj["from"])=="Row"&&!context.ContainsKey("Row"))||(StudioJson.Text(obj["from"])=="Instance"&&context.ContainsKey("AwaitingRow:"+StudioJson.Text(obj["instanceId"])))||obj.Any(pair=>AwaitingRow(pair.Value)),
            JsonArray array=>array.Any(AwaitingRow),_=>false,
        };
        foreach(var binding in (document["fields"] as JsonArray??[]).OfType<JsonObject>())
        {
            if(AwaitingRow(binding))
            {
                output.Add(new JsonObject { ["componentId"]=binding["componentId"]?.DeepClone(),["property"]=binding["property"]?.DeepClone(),["visible"]=true,["value"]=null,["readOnly"]=true,["status"]="AwaitingRow",["reasonCode"]="ROW_REQUIRED",["path"]="fields["+output.Count.ToString(CultureInfo.InvariantCulture)+"]" });
                continue;
            }
            var visible=binding["visibleWhen"] is not JsonObject condition||StudioBinding.Evaluate(condition,context);
            output.Add(new JsonObject { ["componentId"]=binding["componentId"]?.DeepClone(),["property"]=binding["property"]?.DeepClone(),["visible"]=visible,["value"]=visible?StudioBinding.Resolve(binding["source"]!.AsObject(),context):null,["readOnly"]=binding["readOnly"]?.DeepClone()??JsonValue.Create(true) });
        }
        return output;
    }
}
