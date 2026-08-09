namespace IndustrialPlatform.Security;

/// <summary>
/// JWT 声明类型常量(与 API 规范 §32 令牌负载对齐)。
/// </summary>
public static class ClaimConstants
{
    /// <summary>用户标识(JWT 标准 subject)。</summary>
    public const string UserId = "sub";

    /// <summary>用户名。</summary>
    public const string UserName = "user_name";

    /// <summary>租户标识。</summary>
    public const string TenantId = "tenant_id";

    /// <summary>角色(多个角色以多条同类型声明承载)。</summary>
    public const string Role = "role";
}
