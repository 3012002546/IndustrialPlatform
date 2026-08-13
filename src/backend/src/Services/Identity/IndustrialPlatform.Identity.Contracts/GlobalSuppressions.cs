using System.Diagnostics.CodeAnalysis;

// IdentityIntegrationEvent.EventVersion 返回常量 Version,但必须保持实例属性:System.Text.Json 只序列化
// 实例属性(线上 eventVersion 字段),标记 static 会从载荷中消失。CA1822 属误报,予以豁免。
[assembly: SuppressMessage(
    "Performance",
    "CA1822:MarkMembersAsStatic",
    Justification = "EventVersion must remain an instance property so System.Text.Json serializes eventVersion (§20).",
    Scope = "member",
    Target = "~P:IndustrialPlatform.Identity.Contracts.Events.IdentityIntegrationEvent.EventVersion")]
