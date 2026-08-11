namespace IndustrialPlatform.Identity.Application.Authorization;

/// <summary>
/// 授权数据存储端口(§14/§18):按租户与用户业务标识装载权威授权快照。
/// 租户校验由实现保证(用户仓储按 NId 查询不区分租户,必须显式比对)。
/// </summary>
public interface IAuthorizationDataStore
{
    /// <summary>按租户与用户业务标识装载授权快照;不存在或租户不匹配返回 <c>null</c>。</summary>
    Task<AuthorizationSnapshot?> GetSnapshotAsync(
        string tenantNId,
        string userNId,
        CancellationToken cancellationToken);
}
