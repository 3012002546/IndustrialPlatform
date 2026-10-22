using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Contracts;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Api;

/// <summary>公开Condition/Sort合同到固定类型索引查询的无脚本投影。</summary>
internal static class LedgerQueryContract
{
    public static LedgerQuery Parse(JsonObject request)
    {
        var count=0;
        LedgerCondition Visit(JsonObject condition,int depth=0)
        {
            if(depth>32||++count>100)throw Invalid();
            var op=StudioJson.RequiredString(condition,"op");
            if(op is "And" or "Or")
            {
                if(condition["items"] is not JsonArray {Count:>0 and <=20} items||items.Any(i=>i is not JsonObject))throw Invalid();
                return new(op,Items:items.Cast<JsonObject>().Select(c=>Visit(c,depth+1)).ToArray());
            }
            if(op is not ("Eq" or "Ne" or "Gt" or "Ge" or "Lt" or "Le" or "In" or "IsNull"))throw Invalid();
            var left=condition["left"] as JsonObject;var right=condition["right"] as JsonObject;
            if(StudioJson.Text(left?["from"])!="Row"||left?["path"] is not JsonArray { Count:1 } path)throw Invalid();
            if(op!="IsNull"&&(StudioJson.Text(right?["from"])!="Constant"||right?.ContainsKey("value")!=true))throw Invalid();
            return new(op,StudioJson.Text(path[0])??throw Invalid(),right?["value"]?.DeepClone());
        }
        var condition=request["filters"] is JsonObject filter?Visit(filter):null;
        if(request["filters"] is not null&&condition is null)throw Invalid();
        var sorts=new List<LedgerSort>();
        if(request["sort"] is JsonArray items)
        {
            if(items.Count>5||items.Any(i=>i is not JsonObject))throw Invalid();
            foreach(var order in items.Cast<JsonObject>())sorts.Add(new(StudioJson.RequiredString(order,"fieldId"),StudioJson.Text(order["direction"])??"Asc",StudioJson.Text(order["nulls"])??"Last"));
        }
        else if(request["sort"] is not null)throw Invalid();
        return new(request["page"]?.GetValue<int>()??1,request["pageSize"]?.GetValue<int>()??20,Deleted:request["deleted"]?.GetValue<bool>()??false,Condition:condition,Sorts:sorts);
    }
    private static StudioException Invalid()=>new("STUDIO_VALIDATION","台账过滤或排序不在受控合同范围。",path:"filters");
}
