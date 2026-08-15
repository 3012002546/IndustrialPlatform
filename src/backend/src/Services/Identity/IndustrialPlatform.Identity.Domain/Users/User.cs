using IndustrialPlatform.Identity.Domain.Identities;
using IndustrialPlatform.Identity.Domain.LoginSecurity;
using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.SharedKernel.Entities;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.Identity.Domain.Users;

/// <summary>
/// 用户聚合根。业务标识 NId 与规范化值创建后不可变;
/// 登录名、密码、状态与登录安全信息通过对应方法变更,变更受删除/锁定/冻结保护。
/// </summary>
public sealed class User : AggregateRoot
{
    /// <summary>登录名最大长度。</summary>
    public const int LoginNameMaxLength = 64;

    /// <summary>姓名最大长度。</summary>
    public const int NameMaxLength = 256;

    /// <summary>邮箱最大长度。</summary>
    public const int EmailMaxLength = 256;

    /// <summary>电话最大长度。</summary>
    public const int PhoneMaxLength = 32;

    /// <summary>密码哈希最大长度。</summary>
    public const int PasswordHashMaxLength = 1024;

    /// <summary>租户编码(不透明字符串,不做 NId 规范化)。</summary>
    public string TenantNId { get; private set; }

    /// <summary>用户业务标识,创建后不可变。</summary>
    public string NId { get; private set; }

    /// <summary>规范化业务标识(大写),创建后不可变。</summary>
    public string NormalizedNId { get; private set; }

    /// <summary>登录名。</summary>
    public string LoginName { get; private set; }

    /// <summary>规范化登录名(大写)。</summary>
    public string NormalizedLoginName { get; private set; }

    /// <summary>姓名。</summary>
    public string Name { get; private set; }

    /// <summary>密码哈希。禁止明文密码存储、日志或进入领域事件。</summary>
    public string PasswordHash { get; private set; }

    /// <summary>邮箱。</summary>
    public string? Email { get; private set; }

    /// <summary>电话。</summary>
    public string? Phone { get; private set; }

    /// <summary>状态。</summary>
    public UserStatus Status { get; private set; }

    /// <summary>当前连续登录失败次数。</summary>
    public int FailedLoginCount { get; private set; }

    /// <summary>临时锁定截止时间;为空表示未锁定。</summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    /// <summary>安全版本,密码/登录名等安全状态变化时递增,使旧会话失效。</summary>
    public int AuthVersion { get; private set; }

    /// <summary>最近一次成功登录时间。</summary>
    public DateTimeOffset? LastLoginOn { get; private set; }

    /// <summary>
    /// 是否强制首次登录改密(§29A.4)。内置 admin 创建后为 <c>false</c>;
    /// 普通新建用户为 <c>true</c>,首次登录只允许改密与注销,改密成功后清除该标记。
    /// </summary>
    public bool MustChangePassword { get; private set; }

    private readonly List<UserRole> _userRoles = [];

    /// <summary>已分配的用户角色关系(含已解除的软删除关系)。</summary>
    public IReadOnlyCollection<UserRole> UserRoles => _userRoles;

    /// <summary>ORM 反序列化专用构造,非空字符串字段初始化后由持久化框架填充。</summary>
    private User()
    {
        TenantNId = string.Empty;
        NId = string.Empty;
        NormalizedNId = string.Empty;
        LoginName = string.Empty;
        NormalizedLoginName = string.Empty;
        Name = string.Empty;
        PasswordHash = string.Empty;
    }

    private User(
        string tenantNId,
        string nId,
        string loginName,
        string name,
        string? email,
        string? phone,
        string passwordHash,
        bool mustChangePassword)
        : this()
    {
        var trimmedTenantNId = RequireTrimmedNonEmpty(tenantNId, "租户编码不能为空。");
        var trimmedLoginName = RequireTrimmedNonEmpty(
            loginName,
            "登录名不能为空。",
            LoginNameMaxLength,
            $"登录名长度不能超过 {LoginNameMaxLength} 个字符。");
        var trimmedName = RequireTrimmedNonEmpty(
            name,
            "姓名不能为空。",
            NameMaxLength,
            $"姓名长度不能超过 {NameMaxLength} 个字符。");
        var trimmedHash = RequireTrimmedNonEmpty(
            passwordHash,
            "密码哈希不能为空。",
            PasswordHashMaxLength,
            $"密码哈希长度不能超过 {PasswordHashMaxLength} 个字符。");

        var nIdValue = Identities.NId.Create(nId);

        TenantNId = trimmedTenantNId;
        NId = nIdValue.Value;
        NormalizedNId = nIdValue.Normalized;
        LoginName = trimmedLoginName;
        NormalizedLoginName = trimmedLoginName.ToUpperInvariant();
        Name = trimmedName;
        PasswordHash = trimmedHash;
        Email = TrimOrNull(email, EmailMaxLength, $"邮箱长度不能超过 {EmailMaxLength} 个字符。");
        Phone = TrimOrNull(phone, PhoneMaxLength, $"电话长度不能超过 {PhoneMaxLength} 个字符。");
        Status = UserStatus.Active;
        FailedLoginCount = 0;
        LockedUntil = null;
        AuthVersion = 0;
        LastLoginOn = null;
        MustChangePassword = mustChangePassword;
    }

    /// <summary>
    /// 持久化层重建专用构造:从已持久化快照恢复聚合全部状态与生命周期字段,
    /// 不发布任何领域事件,不重新校验(持久化数据假定已通过创建/变更校验)。
    /// </summary>
    internal User(
        Guid id,
        string tenantNId,
        string nId,
        string normalizedNId,
        string loginName,
        string normalizedLoginName,
        string name,
        string passwordHash,
        string? email,
        string? phone,
        UserStatus status,
        int failedLoginCount,
        DateTimeOffset? lockedUntil,
        int authVersion,
        DateTimeOffset? lastLoginOn,
        bool mustChangePassword,
        IReadOnlyCollection<UserRole> userRoles,
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
        LoginName = loginName;
        NormalizedLoginName = normalizedLoginName;
        Name = name;
        PasswordHash = passwordHash;
        Email = email;
        Phone = phone;
        Status = status;
        FailedLoginCount = failedLoginCount;
        LockedUntil = lockedUntil;
        AuthVersion = authVersion;
        LastLoginOn = lastLoginOn;
        MustChangePassword = mustChangePassword;
        _userRoles.AddRange(userRoles);
        IsFrozen = isFrozen;
        IsLocked = isLocked;
        IsDeleted = isDeleted;
        EntityType = entityType;
        CreatedOn = createdOn;
        LastUpdatedOn = lastUpdatedOn;
        OptimisticVersion = optimisticVersion;
        ConcurrencyVersion = concurrencyVersion;
    }

    /// <summary>
    /// 创建用户并校验全部字段,同时发布 <see cref="UserCreatedEvent"/>。
    /// 接收的是已哈希密码,明文密码由应用层在哈希前按
    /// <see cref="IndustrialPlatform.Identity.Domain.Passwords.PasswordPolicy"/> 校验。
    /// </summary>
    /// <param name="tenantNId">租户编码。</param>
    /// <param name="nId">用户业务标识,按 NId 规则校验并规范化。</param>
    /// <param name="loginName">登录名。</param>
    /// <param name="name">姓名。</param>
    /// <param name="email">邮箱,可为空。</param>
    /// <param name="phone">电话,可为空。</param>
    /// <param name="passwordHash">密码哈希。</param>
    /// <param name="mustChangePassword">
    /// 是否强制首次登录改密;普通新建用户为 <c>true</c>(§29A.4),
    /// 内置 bootstrap admin 传 <c>false</c>。
    /// </param>
    /// <param name="id">
    /// 可选稳定内部 Id(bootstrap admin 使用代码内稳定常量,§29A.4);为空时随机生成。
    /// </param>
    /// <returns>创建完成、已注册创建事件的用户聚合根。</returns>
    public static User Create(
        string tenantNId,
        string nId,
        string loginName,
        string name,
        string? email,
        string? phone,
        string passwordHash,
        bool mustChangePassword = true,
        Guid? id = null)
    {
        var user = new User(tenantNId, nId, loginName, name, email, phone, passwordHash, mustChangePassword);
        if (id.HasValue && id.Value != Guid.Empty)
        {
            user.Id = id.Value;
        }

        user.AddDomainEvent(new UserCreatedEvent(user.TenantNId, user.NId, user.LoginName, user.AuthVersion));
        return user;
    }

    /// <summary>
    /// 变更资料(姓名、邮箱、电话)。不改变安全版本,不发布领域事件。
    /// </summary>
    /// <param name="name">新姓名。</param>
    /// <param name="email">新邮箱,可为空。</param>
    /// <param name="phone">新电话,可为空。</param>
    public void ChangeProfile(string name, string? email, string? phone)
    {
        EnsureCanModify();

        Name = RequireTrimmedNonEmpty(
            name,
            "姓名不能为空。",
            NameMaxLength,
            $"姓名长度不能超过 {NameMaxLength} 个字符。");
        Email = TrimOrNull(email, EmailMaxLength, $"邮箱长度不能超过 {EmailMaxLength} 个字符。");
        Phone = TrimOrNull(phone, PhoneMaxLength, $"电话长度不能超过 {PhoneMaxLength} 个字符。");

        Touch();
    }

    /// <summary>
    /// 变更登录名。业务标识不变,安全版本递增并发布安全变更事件(使旧会话失效)。
    /// </summary>
    /// <param name="newLoginName">新登录名。</param>
    public void ChangeLoginName(string newLoginName)
    {
        EnsureCanModify();

        var trimmed = RequireTrimmedNonEmpty(
            newLoginName,
            "登录名不能为空。",
            LoginNameMaxLength,
            $"登录名长度不能超过 {LoginNameMaxLength} 个字符。");

        LoginName = trimmed;
        NormalizedLoginName = trimmed.ToUpperInvariant();
        AuthVersion++;
        AddDomainEvent(new UserSecurityChangedEvent(
            TenantNId,
            NId,
            UserSecurityChangeReason.LoginNameChanged,
            AuthVersion));

        Touch();
    }

    /// <summary>
    /// 变更密码哈希。安全版本递增并发布安全变更事件(使旧会话失效)。
    /// </summary>
    /// <param name="newPasswordHash">新密码哈希。</param>
    public void ChangePasswordHash(string newPasswordHash)
    {
        EnsureCanModify();

        PasswordHash = RequireTrimmedNonEmpty(
            newPasswordHash,
            "密码哈希不能为空。",
            PasswordHashMaxLength,
            $"密码哈希长度不能超过 {PasswordHashMaxLength} 个字符。");
        AuthVersion++;
        AddDomainEvent(new UserSecurityChangedEvent(
            TenantNId,
            NId,
            UserSecurityChangeReason.PasswordChanged,
            AuthVersion));

        Touch();
    }

    /// <summary>
    /// 记录一次登录失败。连续失败达到策略阈值时清零计数并临时锁定;
    /// 已处于临时锁定期内时忽略(不计数、不延长)。
    /// </summary>
    /// <param name="now">失败发生的当前时间。</param>
    /// <param name="policy">锁定策略。</param>
    public void RecordLoginFailure(DateTimeOffset now, LoginAttemptPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        EnsureCanModify();

        if (LockedUntil.HasValue && LockedUntil.Value > now)
        {
            return;
        }

        FailedLoginCount++;
        if (FailedLoginCount >= policy.MaxFailures)
        {
            FailedLoginCount = 0;
            LockedUntil = now + policy.LockDuration;
        }

        Touch();
    }

    /// <summary>
    /// 记录一次成功登录:清零失败计数与临时锁定,并记录最近登录时间。
    /// </summary>
    /// <param name="now">成功登录时间。</param>
    public void RecordLoginSuccess(DateTimeOffset now)
    {
        EnsureCanModify();

        FailedLoginCount = 0;
        LockedUntil = null;
        LastLoginOn = now;

        Touch();
    }

    /// <summary>
    /// 禁用用户:状态置为已禁用,安全版本递增并发布状态变更事件。已禁用时幂等。
    /// </summary>
    public void Disable()
    {
        EnsureCanModify();

        if (Status == UserStatus.Disabled)
        {
            return;
        }

        Status = UserStatus.Disabled;
        AuthVersion++;
        AddDomainEvent(new UserStatusChangedEvent(
            TenantNId,
            NId,
            UserStatus.Active,
            UserStatus.Disabled,
            AuthVersion));

        Touch();
    }

    /// <summary>
    /// 启用用户:状态置为正常,并清零失败计数与临时锁定,发布状态变更事件。
    /// 不递增安全版本。已启用时幂等。
    /// </summary>
    public void Enable()
    {
        EnsureCanModify();

        if (Status == UserStatus.Active)
        {
            return;
        }

        Status = UserStatus.Active;
        FailedLoginCount = 0;
        LockedUntil = null;
        AddDomainEvent(new UserStatusChangedEvent(
            TenantNId,
            NId,
            UserStatus.Disabled,
            UserStatus.Active,
            AuthVersion));

        Touch();
    }

    /// <summary>
    /// 校验用户是否允许登录。已删除、已禁用或处于临时锁定期的用户抛出
    /// <see cref="UnauthorizedException"/>。只读方法,不改变任何状态。
    /// </summary>
    /// <param name="now">当前时间。</param>
    public void EnsureLoginAllowed(DateTimeOffset now)
    {
        if (IsDeleted)
        {
            throw new UnauthorizedException("用户已删除,禁止登录。");
        }

        if (Status == UserStatus.Disabled)
        {
            throw new UnauthorizedException("用户已禁用,禁止登录。");
        }

        if (LockedUntil.HasValue && LockedUntil.Value > now)
        {
            throw new UnauthorizedException("登录失败次数过多,账号已临时锁定,请稍后再试。");
        }
    }

    /// <summary>
    /// 递增安全版本,用于使该用户全部既有会话失效(如安全事件发生时的兜底手段)。
    /// </summary>
    public void IncrementAuthVersion()
    {
        EnsureCanModify();

        AuthVersion++;
        Touch();
    }

    /// <summary>
    /// 要求下次登录强制改密(§29A.4):管理员重置密码后调用;首次登录只允许改密与注销。
    /// </summary>
    public void RequirePasswordChangeOnNextLogin()
    {
        EnsureCanModify();

        if (MustChangePassword)
        {
            return;
        }

        MustChangePassword = true;
        Touch();
    }

    /// <summary>
    /// 清除首次改密标记(§29A.4):首次登录改密成功后调用,同时推进凭据版本并撤销其他会话由调用方执行。
    /// </summary>
    public void ClearMustChangePassword()
    {
        EnsureCanModify();

        if (!MustChangePassword)
        {
            return;
        }

        MustChangePassword = false;
        Touch();
    }

    /// <summary>
    /// 安全删除(墓碑,§29A.3):推进安全版本、软删除全部活动角色关系并标记删除,
    /// 发布删除事件。UserNId/NormalizedNId/NormalizedLoginName 永久保留不复用;
    /// 禁止删除自己、内置 ADMIN 与最后一名系统管理员由应用层按权威计数守卫执行。
    /// </summary>
    public void DeleteForTombstone()
    {
        EnsureCanModify();

        AuthVersion++;
        foreach (var relation in _userRoles.Where(r => !r.IsDeleted))
        {
            relation.MarkDeleted();
        }

        AddDomainEvent(new UserDeletedEvent(TenantNId, NId, AuthVersion));
        MarkDeleted();
    }

    /// <summary>
    /// 恢复墓碑(§29A.3):仅清除删除标记并保持禁用,不自动恢复已移除的角色、用户组、
    /// 凭据有效性或会话;发布恢复事件。恢复后管理员必须显式分配授权、重置密码并启用。
    /// </summary>
    public void RestoreTombstone()
    {
        if (!IsDeleted)
        {
            throw new BusinessException("用户未删除，无需恢复。");
        }

        Restore();
        Status = UserStatus.Disabled;
        AddDomainEvent(new UserRestoredEvent(TenantNId, NId));
        Touch();
    }

    /// <summary>
    /// 分配角色:跨租户、已删除角色或重复分配时抛出业务异常,
    /// 否则新增关系并发布 <see cref="UserRolesChangedEvent"/> 作为权限缓存失效信号。
    /// </summary>
    /// <param name="role">待分配的角色聚合根。</param>
    public void AssignRole(Role role)
    {
        ArgumentNullException.ThrowIfNull(role);
        EnsureCanModify();

        if (role.TenantNId != TenantNId)
        {
            throw new BusinessException("不能分配其他租户的角色。");
        }

        if (role.IsDeleted)
        {
            throw new BusinessException("已删除的角色不能分配。");
        }

        if (_userRoles.Any(ur => ur.RoleId == role.Id && !ur.IsDeleted))
        {
            throw new BusinessException("用户已拥有该角色。");
        }

        _userRoles.Add(new UserRole(TenantNId, Id, IsDeleted, role.Id, role.IsDeleted));
        AddDomainEvent(new UserRolesChangedEvent(TenantNId, NId, role.NId));
        Touch();
    }

    /// <summary>
    /// 移除角色:找不到活动关系时幂等返回;系统角色的最后一名活动持有者禁止移除
    /// (活动持有数由应用层快照传入)。实际变更时发布权限缓存失效事件。
    /// </summary>
    /// <param name="role">待移除的角色聚合根。</param>
    /// <param name="activeHolderCountInTenant">该角色在租户内的活动持有者数量(含当前用户)。</param>
    public void RemoveRole(Role role, int activeHolderCountInTenant)
    {
        ArgumentNullException.ThrowIfNull(role);
        EnsureCanModify();

        var relation = _userRoles.FirstOrDefault(ur => ur.RoleId == role.Id && !ur.IsDeleted);
        if (relation is null)
        {
            return;
        }

        if (role.IsSystem && activeHolderCountInTenant <= 1)
        {
            throw new BusinessException("不能移除最后一名系统管理员,除非经过独立恢复流程。");
        }

        relation.MarkDeleted();
        AddDomainEvent(new UserRolesChangedEvent(TenantNId, NId, role.NId));
        Touch();
    }

    private static string RequireTrimmedNonEmpty(string? value, string emptyMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(emptyMessage);
        }

        return value.Trim();
    }

    private static string RequireTrimmedNonEmpty(string? value, string emptyMessage, int maxLength, string tooLongMessage)
    {
        var trimmed = RequireTrimmedNonEmpty(value, emptyMessage);
        if (trimmed.Length > maxLength)
        {
            throw new ValidationException(tooLongMessage);
        }

        return trimmed;
    }

    private static string? TrimOrNull(string? value, int maxLength, string tooLongMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ValidationException(tooLongMessage);
        }

        return trimmed;
    }
}
