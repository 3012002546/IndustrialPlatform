using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Ledger;
using IndustrialPlatform.PlatformStudio.Contracts;
using IndustrialPlatform.PlatformStudio.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialPlatform.PlatformStudio.Api;

/// <summary>代码登记的独立台账提交动作；真实写入仍走同一个台账应用服务。</summary>
public sealed class StudioLedgerSaveAction(IServiceProvider services,IStudioResourceStore store) : IStudioAction
{
    public string Key=>"studio.ledger.save";
    public string ExecutorVersion=>"1";
    public string PermissionKey=>"studio.ledger.write";
    public bool Mutating=>true;
    public JsonObject InputSchema=>JsonNode.Parse("""{"type":"object","required":["modelNId","revisionNId","values"],"additionalProperties":false,"properties":{"modelNId":{"type":"string","maxLength":128},"revisionNId":{"type":"string","maxLength":128},"recordNId":{"type":"string","maxLength":128,"nullable":true},"nId":{"type":"string","maxLength":128,"nullable":true},"values":{"type":"object","additionalProperties":true},"optimisticVersion":{"type":"string","maxLength":20,"nullable":true},"concurrencyVersion":{"type":"string","maxLength":36,"nullable":true}}}""")!.AsObject();
    public JsonObject OutputSchema=>JsonNode.Parse("""{"type":"object","properties":{"nId":{"type":"string"},"values":{"type":"object","additionalProperties":true}}}""")!.AsObject();
    public async Task<JsonNode?> PreviewAsync(StudioPrincipal principal,JsonObject input,CancellationToken cancellationToken)
    {
        var model=await store.FindResourceAsync(principal.TenantNId,StudioJson.RequiredString(input,"modelNId"),cancellationToken)??throw new StudioException("STUDIO_NOT_FOUND","台账模型不存在。",404);
        await services.GetRequiredService<IStudioAuthorization>().RequireAsync(principal,model,"studio.lowcode.run","Run",cancellationToken);
        var revision=await store.FindRevisionAsync(principal.TenantNId,StudioJson.RequiredString(input,"revisionNId"),cancellationToken);
        if(model.Kind!="LedgerModel"||revision is null||revision.ResourceId!=model.Id||!model.IsEnabled)throw new StudioException("STUDIO_NOT_READY","台账修订不可用。",409);
        var values=StudioDocuments.ValidateValues(JsonNode.Parse(revision.DocumentJson)!.AsObject(),input["values"]!.AsObject(),StudioJson.Text(input["recordNId"]) is null);
        return new JsonObject { ["simulated"]=true,["message"]="模拟：未写入业务记录",["values"]=values };
    }
    public async Task<StudioActionResult> ExecuteAsync(StudioPrincipal principal,string operationKey,JsonObject input,CancellationToken cancellationToken)
    {
        var token=StudioJson.Text(input["concurrencyVersion"]);
        if(token is not null&&!Guid.TryParse(token,out _))throw new StudioException("STUDIO_VALIDATION","并发标识无效。");
        var result=await services.GetRequiredService<StudioLedger>().WriteAsync(principal,StudioJson.RequiredString(input,"modelNId"),StudioJson.Text(input["recordNId"]),new(input["values"]!.AsObject(),StudioJson.RequiredString(input,"revisionNId"),StudioJson.Text(input["optimisticVersion"]),token is null?Guid.Empty:Guid.Parse(token),StudioJson.Text(input["nId"])),operationKey,cancellationToken);
        return new("Succeeded",JsonSerializer.SerializeToNode(result,StudioCommands.JsonOptions),result.NId);
    }
    public async Task<StudioActionResult?> GetByOperationAsync(StudioPrincipal principal,string operationKey,CancellationToken cancellationToken)
    {
        var receipt=await store.FindReceiptAsync(principal.TenantNId,"LedgerWrite",operationKey,cancellationToken);
        if(receipt is null||receipt.ActorUserNId!=principal.SubjectNId)return null;
        var auth=services.GetRequiredService<IStudioAuthorization>();auth.RequirePermission(principal,"studio.ledger.read");
        _=await services.GetRequiredService<IndustrialPlatform.PlatformStudio.Application.Publishing.StudioPublishing>().RequireRuntimeAsync(principal,receipt.ResourceNId,null,cancellationToken);
        return new(receipt.Status,receipt.ResponseJson is null?null:JsonNode.Parse(receipt.ResponseJson),receipt.ExternalReference,receipt.ErrorCode);
    }
}
