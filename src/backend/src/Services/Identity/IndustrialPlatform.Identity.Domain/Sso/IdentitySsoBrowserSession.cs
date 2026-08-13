using IndustrialPlatform.SharedKernel.Entities;

namespace IndustrialPlatform.Identity.Domain.Sso;

/// <summary>
/// 平台浏览器 SSO 会话(§26.6):本地账号登录与客户 IdP 登录成功后建立,
/// Cookie 只携带随机不透明句柄,数据库/Redis 仅保存句柄哈希。
/// NId 用于审计与会话管理,不得作为 Cookie 内容。
/// </summary>
public sealed class IdentitySsoBrowserSession : AggregateRoot
{
    /// <summary>会话句柄哈希最大长度(SHA-256 十六进制)。</summary>
    public const int SessionHandleHashMaxLength = 128;

    /// <summary>撤销原因最大长度。</summary>
    public const int RevokeReasonMaxLength = 256;

    /// <summary>默认空闲期限。</summary>
    public static readonly TimeSpan DefaultIdleLifetime = TimeSpan.FromHours(8);

    /// <summary>默认绝对期限。</summary>
    public static readonly TimeSpan DefaultAbsoluteLifetime = TimeSpan.FromHours(12);

    /// <summary>租户编码(不透明字符串,不做 NId 规范化)。</summary>
    public string TenantNId { get; private set; }

    /// <summary>业务标识,创建后不可变。</summary>
    public string NId { get; private set; }

    /// <summary>规范化业务标识(大写),创建后不可变。</summary>
    public string NormalizedNId { get; private set; }

    /// <summary>建立会话所用 Provider 业务标识(联邦注销目标),创建后不可变。</summary>
    public string ProviderNId { get; private set; }

    /// <summary>关联平台用户主键(复合外键)。</summary>
    public Guid UserId { get; private set; }

    /// <summary>创建时用户软删除状态快照,由持久化层维护。</summary>
    public bool UserIsDeleted { get; private set; }

    /// <summary>会话句柄哈希(SHA-256),不保存明文句柄。</summary>
    public string SessionHandleHash { get; private set; }

    /// <summary>会话建立时的用户安全版本,校验时与当前 AuthVersion 比对。</summary>
    public int AuthVersion { get; private set; }

    /// <summary>最近活动时间,用于空闲过期滑动。</summary>
    public DateTimeOffset LastActivityOn { get; private set; }

    /// <summary>绝对过期时间。</summary>
    public DateTimeOffset ExpiresOn { get; private set; }

    /// <summary>撤销时间;为空表示会话仍有效。</summary>
    public DateTimeOffset? RevokedOn { get; private set; }

    /// <summary>撤销原因。</summary>
    public SsoBrowserSessionRevokeReason? RevokeReason { get; private set; }

    /// <summary>ORM 反序列化专用构造。</summary>
    private IdentitySsoBrowserSession()
    {
        TenantNId = string.Empty;
        NId = string.Empty;
        NormalizedNId = string.Empty;
        ProviderNId = string.Empty;
        SessionHandleHash = string.Empty;
    }

    private IdentitySsoBrowserSession(
        string tenantNId,
        string nId,
        string providerNId,
        Guid userId,
        bool userIsDeleted,
        string sessionHandleHash,
        int authVersion,
        DateTimeOffset now)
        : this()
    {
        TenantNId = SsoGuard.RequireTrimmedNonEmpty(tenantNId, "租户编码不能为空。");
        var nIdValue = Identities.NId.Create(nId);
        NId = nIdValue.Value;
        NormalizedNId = nIdValue.Normalized;
        ProviderNId = SsoGuard.RequireTrimmedNonEmpty(providerNId, "Provider 业务标识不能为空。");
        UserId = userId;
        UserIsDeleted = userIsDeleted;
        SessionHandleHash = SsoGuard.RequireTrimmedNonEmpty(sessionHandleHash, "会话句柄哈希不能为空。", SessionHandleHashMaxLength, $"会话句柄哈希长度不能超过 {SessionHandleHashMaxLength} 个字符。");
        AuthVersion = authVersion;
        LastActivityOn = now;
        ExpiresOn = now + DefaultAbsoluteLifetime;
        RevokedOn = null;
        RevokeReason = null;
    }

    /// <summary>
    /// 持久化层重建专用构造:恢复全部业务字段与生命周期状态,不重新校验。
    /// </summary>
    internal IdentitySsoBrowserSession(
        Guid id,
        string tenantNId,
        string nId,
        string normalizedNId,
        string providerNId,
        Guid userId,
        bool userIsDeleted,
        string sessionHandleHash,
        int authVersion,
        DateTimeOffset lastActivityOn,
        DateTimeOffset expiresOn,
        DateTimeOffset? revokedOn,
        SsoBrowserSessionRevokeReason? revokeReason,
        bool isFrozen,
        bool isLocked,
        bool isDeleted,
        string entityType,
        DateTimeOffset createdOn,
        DateTimeOffset lastUpdatedOn,
        long optimisticVersion,
        Guid concurrencyVersion)
        : base()
    {
        Id = id;
        TenantNId = tenantNId;
        NId = nId;
        NormalizedNId = normalizedNId;
        ProviderNId = providerNId;
        UserId = userId;
        UserIsDeleted = userIsDeleted;
        SessionHandleHash = sessionHandleHash;
        AuthVersion = authVersion;
        LastActivityOn = lastActivityOn;
        ExpiresOn = expiresOn;
        RevokedOn = revokedOn;
        RevokeReason = revokeReason;
        IsFrozen = isFrozen;
        IsLocked = isLocked;
        IsDeleted = isDeleted;
        EntityType = entityType;
        CreatedOn = createdOn;
        LastUpdatedOn = lastUpdatedOn;
        OptimisticVersion = optimisticVersion;
        ConcurrencyVersion = concurrencyVersion;
    }

    /// <summary>建立浏览器 SSO 会话。</summary>
    public static IdentitySsoBrowserSession Create(
        string tenantNId,
        string nId,
        string providerNId,
        Guid userId,
        bool userIsDeleted,
        string sessionHandleHash,
        int authVersion,
        DateTimeOffset now)
        => new IdentitySsoBrowserSession(tenantNId, nId, providerNId, userId, userIsDeleted, sessionHandleHash, authVersion, now);

    /// <summary>滑动活动时间(空闲续期),并重算绝对过期。</summary>
    public void TouchActivity(DateTimeOffset now)
    {
        EnsureCanModify();

        LastActivityOn = now;
        ExpiresOn = now + DefaultAbsoluteLifetime;
        Touch();
    }

    /// <summary>撤销会话;已撤销时幂等。</summary>
    public void Revoke(SsoBrowserSessionRevokeReason reason, DateTimeOffset now)
    {
        EnsureCanModify();

        if (RevokedOn.HasValue)
        {
            return;
        }

        RevokedOn = now;
        RevokeReason = reason;
        Touch();
    }

    /// <summary>会话是否有效:未删除、未撤销、绝对未过期且距上次活动未超空闲期限。</summary>
    public bool IsValid(DateTimeOffset now)
    {
        if (IsDeleted || RevokedOn.HasValue)
        {
            return false;
        }

        if (now > ExpiresOn)
        {
            return false;
        }

        if (now - LastActivityOn > DefaultIdleLifetime)
        {
            return false;
        }

        return true;
    }
}
