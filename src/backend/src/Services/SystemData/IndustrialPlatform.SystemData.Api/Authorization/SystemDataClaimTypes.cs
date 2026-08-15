namespace IndustrialPlatform.SystemData.Api.Authorization;

/// <summary>
/// SystemData 声明类型常量(TASK-SD-006)。JwtBearer 以原始声明名承载
/// (<c>MapInboundClaims=false</c>),与 Identity 签发的令牌声明名对齐。
/// 权限以 <c>permission_nid</c> 声明承载(可多条,或单条以空格分隔),由
/// <see cref="SystemDataPermissionAuthorizationHandler"/> 裁决。
/// </summary>
public static class SystemDataClaimTypes
{
    /// <summary>权限 NId 声明(§9.6,如 <c>systemdata.organization.view</c>)。</summary>
    public const string PermissionNId = "permission_nid";
}
