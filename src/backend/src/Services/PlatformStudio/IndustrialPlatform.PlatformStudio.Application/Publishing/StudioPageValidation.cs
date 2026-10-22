using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Application.Publishing;

/// <summary>页面条件和验证器在真实动作输入前重复执行，客户端验证不构成业务授权。</summary>
internal static class StudioPageValidation
{
    public static void ValidateForm(JsonObject document,IReadOnlyDictionary<string,JsonNode?> contexts)
    {
        var form=contexts["Form"] as JsonObject??new();
        foreach(var binding in (document["fields"] as JsonArray??[]).OfType<JsonObject>())
        {
            var target=StudioJson.Text(binding["writeTarget"]);if(target is null)continue;
            var value=form[target];
            var supplied=form.ContainsKey(target);
            if(supplied&&(binding["readOnly"]?.GetValue<bool>()==true||binding["visibleWhen"] is JsonObject visible&&!StudioBinding.Evaluate(visible,contexts)))throw new StudioException("STUDIO_FORBIDDEN","当前字段不允许写入。",403,"context.form."+target);
            if(binding["requiredWhen"] is JsonObject required&&StudioBinding.Evaluate(required,contexts)&&Empty(value))throw Invalid(target,"当前条件下此字段必填。");
            foreach(var validator in (binding["validators"] as JsonArray??[]).OfType<JsonObject>())
            {
                var kind=StudioJson.RequiredString(validator,"kind");var options=validator["options"] as JsonObject??new();var text=StudioJson.Text(value);
                switch(kind)
                {
                    case "Required":if(Empty(value))throw Invalid(target,"必填字段不能为空。");break;
                    case "Length":
                        if(value is null)break;
                        if(text is null||text.Length<(options["min"]?.GetValue<int>()??0)||text.Length>(options["max"]?.GetValue<int>()??65536))throw Invalid(target,"文本长度不满足约束。");break;
                    case "Range":
                        if(value is null)break;
                        if(!decimal.TryParse(text??value.ToJsonString(),NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var number))throw Invalid(target,"范围验证需要数值。");
                        if(options["min"] is {} min&&number<decimal.Parse(StudioJson.Text(min)??min.ToJsonString(),CultureInfo.InvariantCulture)||options["max"] is {} max&&number>decimal.Parse(StudioJson.Text(max)??max.ToJsonString(),CultureInfo.InvariantCulture))throw Invalid(target,"数值不在允许范围。");break;
                    case "Pattern":
                        if(value is null)break;
                        var pattern=StudioJson.RequiredString(options,"pattern",256);
                        try {if(text is null||text.Length>65536||!Regex.IsMatch(text,pattern,RegexOptions.CultureInvariant|RegexOptions.NonBacktracking,TimeSpan.FromMilliseconds(100)))throw Invalid(target,"文本格式不符合约束。");}
                        catch(ArgumentException){throw Invalid(target,"配置的验证表达式不受支持。");}
                        catch(NotSupportedException){throw Invalid(target,"配置的验证表达式不受支持。");}
                        catch(RegexMatchTimeoutException){throw Invalid(target,"文本格式验证超出预算。");}break;
                    default:throw Invalid(target,"未登记的验证器。");
                }
            }
        }
    }
    private static bool Empty(JsonNode? value)=>value is null||StudioJson.Text(value) is {} text&&string.IsNullOrWhiteSpace(text);
    private static StudioException Invalid(string target,string message)=>new("STUDIO_VALIDATION",message,path:"context.form."+target);
}
