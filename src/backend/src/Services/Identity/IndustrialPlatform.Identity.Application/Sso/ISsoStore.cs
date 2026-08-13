using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Domain.Sso;
using IndustrialPlatform.Identity.Domain.Users;

namespace IndustrialPlatform.Identity.Application.Sso;

/// <summary>浏览器 SSO 会话创建结果:会话聚合与明文句柄。句柄只在内存流转,数据库只保存 SHA-256 哈希。</summary>
public sealed record SsoBrowserSessionCreated(IdentitySsoBrowserSession Session, string SessionHandle);

/// <summary>外部账号管理查询投影(含平台用户展示字段,不含 external subject 等 IdP 侧敏感标识)。</summary>
public sealed record StoredSsoAccount(
    Guid Id,
    string NId,
    string ProviderNId,
    string UserNId,
    string UserLoginName,
    string UserName,
    string? ExternalName,
    string? ExternalEmail,
    DateTimeOffset? LastLoginOn,
    long OptimisticVersion,
    Guid ConcurrencyVersion);

/// <summary>
/// SSO 持久化端口(§26):Provider/外部账号/Client/端点/浏览器会话的查询与写入。
/// 所有句柄类输入以明文传入,实现内部哈希后比对;写操作按双版本乐观并发,冲突抛并发异常。
/// </summary>
public interface ISsoStore
{
    // ---- Provider ----
    /// <summary>查询租户内全部启用 Provider(公开发现)。</summary>
    Task<IReadOnlyList<IdentitySsoProvider>> ListEnabledProvidersAsync(string tenantNId, CancellationToken cancellationToken);

    /// <summary>查询租户内全部未删除 Provider(管理端),按创建时间倒序。</summary>
    Task<IReadOnlyList<IdentitySsoProvider>> ListProvidersAsync(string tenantNId, CancellationToken cancellationToken);

    /// <summary>按业务标识查询 Provider;不存在或已删除返回 <c>null</c>。</summary>
    Task<IdentitySsoProvider?> FindProviderByNIdAsync(string tenantNId, string providerNId, CancellationToken cancellationToken);

    /// <summary>按 client_id/entity_id 查询 Provider;不存在或已删除返回 <c>null</c>。</summary>
    Task<IdentitySsoProvider?> FindProviderByClientIdAsync(string tenantNId, string clientIdOrEntityId, CancellationToken cancellationToken);

    /// <summary>新增 Provider。</summary>
    Task AddProviderAsync(IdentitySsoProvider provider, CancellationToken cancellationToken);

    /// <summary>按双版本原子更新 Provider;冲突抛并发异常。</summary>
    Task UpdateProviderAsync(IdentitySsoProvider provider, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, CancellationToken cancellationToken);

    // ---- External account ----
    /// <summary>按 IdP 侧主体标识查询外部账号;不存在或已删除返回 <c>null</c>。</summary>
    Task<IdentityExternalAccount?> FindExternalAccountAsync(Guid providerId, string externalSubject, CancellationToken cancellationToken);

    /// <summary>按平台用户主键查询外部账号;不存在或已删除返回 <c>null</c>。</summary>
    Task<IdentityExternalAccount?> FindExternalAccountByUserIdAsync(Guid providerId, Guid userId, CancellationToken cancellationToken);

    /// <summary>按 Provider 分页查询外部账号投影(含用户展示字段)。</summary>
    Task<IReadOnlyList<StoredSsoAccount>> ListExternalAccountsAsync(Guid providerId, string providerNId, CancellationToken cancellationToken);

    /// <summary>新增外部账号映射。</summary>
    Task AddExternalAccountAsync(IdentityExternalAccount account, CancellationToken cancellationToken);

    /// <summary>按双版本原子更新外部账号;冲突抛并发异常。</summary>
    Task UpdateExternalAccountAsync(IdentityExternalAccount account, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, CancellationToken cancellationToken);

    // ---- Client ----
    /// <summary>按业务标识查询 Client(含端点);不存在或已删除返回 <c>null</c>。</summary>
    Task<IdentitySsoClient?> FindClientByNIdAsync(string tenantNId, string clientNId, CancellationToken cancellationToken);

    /// <summary>按 OAuth client_id 查询 Client(含端点);不存在或已删除返回 <c>null</c>。</summary>
    Task<IdentitySsoClient?> FindClientByOAuthClientIdAsync(string tenantNId, string oauthClientId, CancellationToken cancellationToken);

    /// <summary>查询租户内全部未删除 Client(含端点),按创建时间倒序。</summary>
    Task<IReadOnlyList<IdentitySsoClient>> ListClientsAsync(string tenantNId, CancellationToken cancellationToken);

    /// <summary>新增 Client(事务内级联活动端点)。</summary>
    Task AddClientAsync(IdentitySsoClient client, CancellationToken cancellationToken);

    /// <summary>按双版本原子更新 Client 与端点 diff;冲突抛并发异常。</summary>
    Task UpdateClientAsync(IdentitySsoClient client, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, CancellationToken cancellationToken);

    // ---- Browser session ----
    /// <summary>按明文句柄哈希查询浏览器会话;不存在或已删除返回 <c>null</c>。</summary>
    Task<IdentitySsoBrowserSession?> FindBrowserSessionByHandleAsync(string sessionHandle, CancellationToken cancellationToken);

    /// <summary>建立浏览器 SSO 会话:生成随机句柄,内部哈希后持久化,返回明文句柄供 Cookie 使用。</summary>
    Task<SsoBrowserSessionCreated> CreateBrowserSessionAsync(string tenantNId, string providerNId, Guid userId, bool userIsDeleted, int authVersion, CancellationToken cancellationToken);

    /// <summary>按双版本原子更新浏览器会话;冲突抛并发异常。</summary>
    Task UpdateBrowserSessionAsync(IdentitySsoBrowserSession session, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, CancellationToken cancellationToken);

    // ---- JIT(事务内原子)----
    /// <summary>JIT 供给:同一事务内新增本地用户(含活动角色关系)、外部账号映射与 Outbox 事件,避免孤儿用户。</summary>
    Task AddJitUserAsync(
        User user,
        IdentityExternalAccount externalAccount,
        IReadOnlyCollection<OutboxEnvelope> outboxEvents,
        CancellationToken cancellationToken);
}
