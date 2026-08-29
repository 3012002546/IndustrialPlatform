using System.Diagnostics;
using System.Security.Cryptography;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Identity.Application.UserGroups;
using ContractsEvents = IndustrialPlatform.Identity.Contracts.Events;
using IndustrialPlatform.Identity.Contracts.Management;
using Identities = IndustrialPlatform.Identity.Domain.Identities;
using IndustrialPlatform.Identity.Domain.Passwords;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SharedKernel.Events;
using IndustrialPlatform.Querying.Descriptors;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.Identity.Application.Management;

/// <summary>
/// 用户管理用例实现(§16.1/§19.2)。编排管理存储、密码哈希、会话撤销与权限缓存;
/// 写操作按乐观并发更新并写操作审计(best-effort),敏感字段绝不进入审计与日志。
/// </summary>
public sealed partial class UserManagementService : IUserManagementService
{
    /// <summary>内置 ADMIN 保留业务标识(§29A.3 禁删;正式引导由 TASK-ID-019 定义)。</summary>
    private const string ReservedAdminUserNId = "ADMIN";

    /// <summary>内置 bootstrap admin 保留登录名(§29A.4),普通用户不可占用。</summary>
    private const string ReservedAdminLoginName = "admin";

    private readonly IManagementStore _store;
    private readonly IUserGroupStore _groupStore;
    private readonly IPasswordHasher _hasher;
    private readonly ITemporaryPasswordGenerator _temporaryPasswordGenerator;
    private readonly IRefreshSessionStore _refreshStore;
    private readonly IPermissionCache _permissionCache;
    private readonly IOperationAuditSink _auditSink;
    private readonly ILogger<UserManagementService> _logger;

    public UserManagementService(
        IManagementStore store,
        IUserGroupStore groupStore,
        IPasswordHasher hasher,
        ITemporaryPasswordGenerator temporaryPasswordGenerator,
        IRefreshSessionStore refreshStore,
        IPermissionCache permissionCache,
        IOperationAuditSink auditSink,
        ILogger<UserManagementService> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(groupStore);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(temporaryPasswordGenerator);
        _store = store;
        _groupStore = groupStore;
        _hasher = hasher;
        _temporaryPasswordGenerator = temporaryPasswordGenerator;
        _refreshStore = refreshStore;
        _permissionCache = permissionCache;
        _auditSink = auditSink;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<CreateUserResult> CreateAsync(
        string tenantNId,
        string actorUserNId,
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nId = Identities.NId.Create(string.IsNullOrWhiteSpace(request.NId) ? GenerateUserNId() : request.NId!);
        var loginName = request.LoginName?.Trim();
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(loginName))
        {
            throw new ValidationException("登录名不能为空。");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("姓名不能为空。");
        }

        // §29A.4:内置 admin 登录名为系统保留,普通用户不可占用。
        if (string.Equals(loginName, ReservedAdminLoginName, StringComparison.OrdinalIgnoreCase))
        {
            throw new UserLoginNameReservedException();
        }

        if (await _store.UserExistsByNIdAsync(nId.Value, cancellationToken))
        {
            throw new UserNIdConflictException();
        }

        if (await _store.LoginNameExistsAsync(tenantNId, loginName, cancellationToken))
        {
            throw new UserLoginNameConflictException();
        }

        // §29A.4:服务端生成独立随机临时密码(≥20 满足策略),只返回一次;新用户强制首次改密。
        var temporaryPassword = _temporaryPasswordGenerator.Generate(
            BootstrapSeedCatalog.BootstrapPasswordMinLength,
            loginName,
            nId.Value);
        var user = User.Create(
            tenantNId,
            nId.Value,
            loginName,
            name,
            request.Email,
            request.Phone,
            _hasher.Hash(temporaryPassword),
            mustChangePassword: true);

        var roleNIds = (request.RoleNIds ?? []).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.Ordinal).ToArray();
        if (roleNIds.Length > 0)
        {
            var roles = await _store.GetRolesByNIdsAsync(roleNIds, cancellationToken);
            if (roles.Count != roleNIds.Length || roles.Any(r => r.TenantNId != tenantNId))
            {
                throw new BusinessRuleViolationException("存在无效或不可用的角色。");
            }

            foreach (var role in roles)
            {
                user.AssignRole(role);
            }
        }

        // 用户创建事件与用户写入同事务提交到 Outbox(§20 原子提交)。
        await ExecuteWriteAsync(() => _store.AddUserAsync(user, BuildOutboxEnvelopes(user), cancellationToken));
        user.ClearDomainEvents();

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                OperationAction.UserCreate,
                OperationObjectType.User,
                user.NId,
                null,
                BuildUserSummaryText(user),
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        var summary = await GetAsync(tenantNId, user.NId, cancellationToken);
        return new CreateUserResult(summary, temporaryPassword);
    }

    /// <inheritdoc/>
    public async Task<UserSummary> UpdateAsync(
        string tenantNId,
        string actorUserNId,
        string userNId,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await RequireUserAsync(tenantNId, userNId, cancellationToken);
        var before = BuildUserSummaryText(user);

        if (!string.IsNullOrWhiteSpace(request.LoginName))
        {
            var trimmedLoginName = request.LoginName.Trim();
            if (!string.Equals(trimmedLoginName, user.LoginName, StringComparison.Ordinal))
            {
                if (await _store.LoginNameExistsAsync(tenantNId, trimmedLoginName, cancellationToken))
                {
                    throw new UserLoginNameConflictException();
                }

                user.ChangeLoginName(trimmedLoginName);
            }
        }

        user.ChangeProfile(request.Name ?? user.Name, request.Email, request.Phone);

        // 登录名变更产生的安全事件与用户写入同事务提交(§20)。
        var updateEnvelopes = BuildOutboxEnvelopes(user);
        await ExecuteWriteAsync(() => _store.UpdateUserAsync(
            user,
            request.ExpectedOptimisticVersion,
            request.ExpectedConcurrencyVersion,
            updateEnvelopes,
            cancellationToken));
        user.ClearDomainEvents();

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                OperationAction.UserUpdate,
                OperationObjectType.User,
                user.NId,
                before,
                BuildUserSummaryText(user),
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        await TryInvalidatePermissionCacheAsync(user, cancellationToken);

        return await GetAsync(tenantNId, userNId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<UserSummary> SetStatusAsync(
        string tenantNId,
        string actorUserNId,
        string userNId,
        SetUserStatusRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await RequireUserAsync(tenantNId, userNId, cancellationToken);
        var before = BuildUserSummaryText(user);

        if (request.Enabled)
        {
            user.Enable();
        }
        else
        {
            if (string.Equals(user.NId, actorUserNId, StringComparison.Ordinal))
            {
                throw new BusinessRuleViolationException("不能禁用当前登录用户。");
            }

            await EnsureNotLastSystemAdminAsync(user, tenantNId, cancellationToken);
            user.Disable();
        }

        // 状态变更事件与用户写入同事务提交(§20)。
        var statusEnvelopes = BuildOutboxEnvelopes(user);
        await ExecuteWriteAsync(() => _store.UpdateUserAsync(
            user,
            request.ExpectedOptimisticVersion,
            request.ExpectedConcurrencyVersion,
            statusEnvelopes,
            cancellationToken));
        user.ClearDomainEvents();

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                request.Enabled ? OperationAction.UserEnable : OperationAction.UserDisable,
                OperationObjectType.User,
                user.NId,
                before,
                BuildUserSummaryText(user),
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        if (!request.Enabled)
        {
            await TryInvalidatePermissionCacheAsync(user, cancellationToken);
        }

        return await GetAsync(tenantNId, userNId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<UserSummary> AssignRolesAsync(
        string tenantNId,
        string actorUserNId,
        string userNId,
        AssignUserRolesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await RequireUserAsync(tenantNId, userNId, cancellationToken);
        var before = BuildUserSummaryText(user);

        var requestedNIds = (request.RoleNIds ?? []).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.Ordinal).ToArray();
        var roles = requestedNIds.Length == 0 ? [] : await _store.GetRolesByNIdsAsync(requestedNIds, cancellationToken);
        if (roles.Count != requestedNIds.Length || roles.Any(r => r.TenantNId != tenantNId))
        {
            throw new BusinessRuleViolationException("存在无效或不可用的角色。");
        }

        var requestedRoleIds = roles.Select(r => r.Id).ToHashSet();
        var currentActive = user.UserRoles.Where(ur => !ur.IsDeleted && !ur.RoleIsDeleted).ToArray();

        // 差量解除:仅保留在新集合中的活动关系;最后系统管理员保护由领域层校验。
        var removalRoleIds = currentActive.Select(ur => ur.RoleId).Where(id => !requestedRoleIds.Contains(id)).ToArray();
        var removalRoles = removalRoleIds.Length == 0 ? [] : await _store.GetRolesByIdsAsync(removalRoleIds, cancellationToken);

        // 领域变更(可能抛 BusinessException)与持久化写入处于同一映射边界,
        // 保证业务规则违例统一映射为 BusinessRuleViolationException。
        await ExecuteWriteAsync(async () =>
        {
            foreach (var role in removalRoles)
            {
                var holders = await _store.CountActiveRoleHoldersAsync(role.Id, tenantNId, cancellationToken);
                user.RemoveRole(role, holders);
            }

            // 差量分配:已持有的角色跳过(领域重复分配守卫兜底)。
            foreach (var role in roles)
            {
                if (!currentActive.Any(ur => ur.RoleId == role.Id))
                {
                    user.AssignRole(role);
                }
            }

            // 角色变化事件与用户写入同事务提交(§20)。
            var assignEnvelopes = BuildOutboxEnvelopes(user);
            await _store.UpdateUserAsync(
                user,
                request.ExpectedOptimisticVersion,
                request.ExpectedConcurrencyVersion,
                assignEnvelopes,
                cancellationToken);
            user.ClearDomainEvents();
        });

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                OperationAction.UserAssignRoles,
                OperationObjectType.User,
                user.NId,
                before,
                BuildUserSummaryText(user),
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        // 角色关系变化不推进 AuthVersion,必须显式失效权限缓存。
        await TryInvalidatePermissionCacheAsync(user, cancellationToken);

        return await GetAsync(tenantNId, userNId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ResetPasswordResult> ResetPasswordAsync(
        string tenantNId,
        string actorUserNId,
        string userNId,
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await RequireUserAsync(tenantNId, userNId, cancellationToken);

        // §29A.4:重置生成独立随机临时密码,不接受管理员指定;重置后强制首次改密。
        var temporaryPassword = _temporaryPasswordGenerator.Generate(
            BootstrapSeedCatalog.BootstrapPasswordMinLength,
            user.LoginName,
            user.NId);

        var expectedOptimistic = user.OptimisticVersion;
        var expectedConcurrency = user.ConcurrencyVersion;
        user.ChangePasswordHash(_hasher.Hash(temporaryPassword));
        user.RequirePasswordChangeOnNextLogin();

        // 密码变更安全事件与用户写入同事务提交(§20)。
        var resetEnvelopes = BuildOutboxEnvelopes(user);
        await ExecuteWriteAsync(() => _store.UpdateUserAsync(
            user,
            expectedOptimistic,
            expectedConcurrency,
            resetEnvelopes,
            cancellationToken));
        user.ClearDomainEvents();

        // 撤销该用户全部刷新会话,推进 AuthVersion 后旧 Access Token 亦失效。
        await _refreshStore.RevokeAllForUserAsync(user.Id, "admin_reset", cancellationToken);
        await TryInvalidatePermissionCacheAsync(user, cancellationToken);

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                OperationAction.UserResetPassword,
                OperationObjectType.User,
                user.NId,
                null,
                null,
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return new ResetPasswordResult(temporaryPassword);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(
        string tenantNId,
        string actorUserNId,
        string userNId,
        DeleteUserRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await RequireUserAsync(tenantNId, userNId, cancellationToken);
        if (string.Equals(user.NId, actorUserNId, StringComparison.Ordinal))
        {
            throw new BusinessRuleViolationException("不能删除当前登录用户。");
        }

        if (string.Equals(user.NId, ReservedAdminUserNId, StringComparison.Ordinal))
        {
            throw new BusinessRuleViolationException("内置 ADMIN 用户禁止删除。");
        }

        await EnsureNotLastSystemAdminOnDeleteAsync(user, tenantNId, cancellationToken);

        var expectedOptimistic = user.OptimisticVersion;
        var expectedConcurrency = user.ConcurrencyVersion;
        var before = BuildUserSummaryText(user);

        // 领域变更(推进 AuthVersion + 软删直接角色 + 墓碑标记 + 删除事件)与持久化
        // (含组成员关系清理 + Outbox)处于同一映射边界(§29A.3)。
        await ExecuteWriteAsync(async () =>
        {
            user.DeleteForTombstone();
            var deleteEnvelopes = BuildOutboxEnvelopes(user);
            await _store.UpdateUserAsync(user, expectedOptimistic, expectedConcurrency, deleteEnvelopes, cancellationToken);
            user.ClearDomainEvents();
        });

        // 撤销全部刷新会话;推进 AuthVersion 后旧 Access Token 与 SSO 浏览器会话
        // (按 AuthVersion 校验)亦失效,并删除版本化权限缓存。
        await _refreshStore.RevokeAllForUserAsync(user.Id, "user_deleted", cancellationToken);
        await TryInvalidatePermissionCacheAsync(user, cancellationToken);

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                OperationAction.UserDelete,
                OperationObjectType.User,
                user.NId,
                before,
                BuildUserSummaryText(user),
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<UserSummary> RestoreAsync(
        string tenantNId,
        string actorUserNId,
        string userNId,
        RestoreUserRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await _store.GetUserAggregateIncludingDeletedAsync(userNId, cancellationToken);
        if (user is null || !string.Equals(user.TenantNId, tenantNId, StringComparison.Ordinal))
        {
            throw new ResourceNotFoundException();
        }

        if (!user.IsDeleted)
        {
            throw new UserDeletedException();
        }

        var expectedOptimistic = user.OptimisticVersion;
        var expectedConcurrency = user.ConcurrencyVersion;
        var before = BuildUserSummaryText(user);

        // 仅恢复墓碑为 Disabled,不自动恢复授权/凭据/会话;恢复事件与写入同事务提交(§29A.3)。
        await ExecuteWriteAsync(async () =>
        {
            user.RestoreTombstone();
            var restoreEnvelopes = BuildOutboxEnvelopes(user);
            await _store.RestoreUserAsync(user, expectedOptimistic, expectedConcurrency, restoreEnvelopes, cancellationToken);
            user.ClearDomainEvents();
        });

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                OperationAction.UserRestore,
                OperationObjectType.User,
                user.NId,
                before,
                BuildUserSummaryText(user),
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return await GetAsync(tenantNId, userNId, cancellationToken);
    }

    /// <summary>删除前守卫(§29A.3):目标用户的全部有效系统角色(直接 ∪ 组继承)在租户内仅剩其一名持有者时拒绝。</summary>
    private async Task EnsureNotLastSystemAdminOnDeleteAsync(User user, string tenantNId, CancellationToken cancellationToken)
    {
        var directRoleIds = user.UserRoles
            .Where(ur => !ur.IsDeleted && !ur.RoleIsDeleted)
            .Select(ur => ur.RoleId)
            .ToArray();
        var groupRoleIds = await _groupStore.GetRoleIdsForUserAsync(user.Id, tenantNId, cancellationToken);
        var effectiveRoleIds = directRoleIds.Concat(groupRoleIds).Distinct().ToArray();
        if (effectiveRoleIds.Length == 0)
        {
            return;
        }

        var systemRoleIds = (await _store.GetRolesByIdsAsync(effectiveRoleIds, cancellationToken))
            .Where(r => r.IsSystem)
            .Select(r => r.Id)
            .ToArray();
        foreach (var systemRoleId in systemRoleIds)
        {
            var holders = await _store.CountActiveRoleHoldersAsync(systemRoleId, tenantNId, cancellationToken);
            if (holders <= 1)
            {
                throw new LastAdminRequiredException();
            }
        }
    }

    /// <inheritdoc/>
    public async Task<ManagementPage<UserSummary>> ListAsync(string tenantNId, UserListFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var page = await _store.QueryUsersAsync(filter with { TenantNId = tenantNId }, cancellationToken);
        var items = page.Items.Select(ToSummary).ToList();
        return new ManagementPage<UserSummary>(items, page.Total, filter.PageIndex, filter.PageSize);
    }

    /// <inheritdoc/>
    public async Task<ManagementPage<UserQueryResource>> QueryUsersAsync(
        string tenantNId,
        QueryDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var page = _store is IUserQueryStore queryStore
            ? await queryStore.QueryUsersAsync(tenantNId, descriptor, cancellationToken)
            : await _store.QueryUsersAsync(
                UserQueryResource.ToLegacyFilter(tenantNId, descriptor),
                cancellationToken);
        return new ManagementPage<UserQueryResource>(
            page.Items.Select(item => UserQueryResource.From(ToSummary(item))).ToList(),
            page.Total,
            descriptor.PageIndex,
            descriptor.PageSize);
    }

    /// <inheritdoc/>
    public async Task<UserSummary> GetAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
    {
        var stored = await _store.GetUserAsync(userNId, cancellationToken);
        if (stored is null || !string.Equals(stored.TenantNId, tenantNId, StringComparison.Ordinal))
        {
            throw new ResourceNotFoundException();
        }

        return ToSummary(stored);
    }

    private async Task<User> RequireUserAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
    {
        var user = await _store.GetUserAggregateAsync(userNId, cancellationToken);
        if (user is null || !string.Equals(user.TenantNId, tenantNId, StringComparison.Ordinal))
        {
            throw new ResourceNotFoundException();
        }

        return user;
    }

    /// <summary>禁用前守卫:目标用户持有任一系统角色且该角色在租户内仅剩其一名活动持有者时拒绝(§25.1)。</summary>
    private async Task EnsureNotLastSystemAdminAsync(User user, string tenantNId, CancellationToken cancellationToken)
    {
        var activeRoleIds = user.UserRoles
            .Where(ur => !ur.IsDeleted && !ur.RoleIsDeleted)
            .Select(ur => ur.RoleId)
            .ToArray();
        if (activeRoleIds.Length == 0)
        {
            return;
        }

        var roles = await _store.GetRolesByIdsAsync(activeRoleIds, cancellationToken);
        foreach (var role in roles.Where(r => r.IsSystem))
        {
            var holders = await _store.CountActiveRoleHoldersAsync(role.Id, tenantNId, cancellationToken);
            if (holders <= 1)
            {
                throw new BusinessRuleViolationException("不能禁用最后一名系统管理员,除非经过独立恢复流程。");
            }
        }
    }

    private static UserSummary ToSummary(StoredUser u) => new(
        u.NId,
        u.LoginName,
        u.Name,
        u.Email,
        u.Phone,
        u.Status.ToString(),
        u.TenantNId,
        u.CreatedOn,
        u.LastLoginOn,
        u.MustChangePassword,
        u.DirectRoleNIds,
        u.GroupRoleNIds,
        u.EffectiveRoleNIds,
        u.OptimisticVersion,
        u.ConcurrencyVersion,
        u.IsDeleted);

    /// <summary>审计摘要:仅非敏感资料字段;绝不包含密码、Token 或内部哈希。</summary>
    private static string BuildUserSummaryText(User user) =>
        $"loginName={user.LoginName},name={user.Name},email={user.Email ?? "-"},phone={user.Phone ?? "-"},status={user.Status}";

    private async Task TryWriteOperationAuditAsync(OperationAuditEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await _auditSink.WriteAsync(entry, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogOperationAuditWriteFailed(_logger, ex);
        }
    }

    /// <summary>权限缓存失效(尽力而为):角色关系/安全状态已提交,删除旧版本缓存键,TTL 兜底收敛。</summary>
    private async Task TryInvalidatePermissionCacheAsync(User user, CancellationToken cancellationToken)
    {
        try
        {
            await _permissionCache.InvalidateAsync(user.TenantNId, user.NId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPermissionCacheInvalidateFailed(_logger, ex);
        }
    }

    /// <summary>写操作统一异常映射:并发冲突 409、领域业务规则 400。</summary>
    private static async Task ExecuteWriteAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ConcurrencyException)
        {
            throw new ConcurrencyConflictException();
        }
        catch (BusinessException ex)
        {
            throw new BusinessRuleViolationException(ex.Message);
        }
    }

    /// <summary>把聚合上尚未派发的领域事件映射为 Outbox 信封(§20,版本化事件,subjectNId=UserNId)。</summary>
    private static OutboxEnvelope[] BuildOutboxEnvelopes(User user)
    {
        if (user.DomainEvents.Count == 0)
        {
            return [];
        }

        return user.DomainEvents
            .Select(MapToEnvelope)
            .Where(e => e is not null)
            .Select(e => e!)
            .ToArray();
    }

    private static OutboxEnvelope? MapToEnvelope(IDomainEvent domainEvent) => domainEvent switch
    {
        UserCreatedEvent e => CreateEnvelope(new ContractsEvents.UserCreatedEvent(e.TenantNId, e.UserNId, e.AuthVersion)),
        UserStatusChangedEvent e => CreateEnvelope(new ContractsEvents.UserStatusChangedEvent(e.TenantNId, e.UserNId, e.OldStatus.ToString(), e.NewStatus.ToString(), e.AuthVersion)),
        UserSecurityChangedEvent e => CreateEnvelope(new ContractsEvents.UserSecurityChangedEvent(e.TenantNId, e.UserNId, e.Reason.ToString(), e.AuthVersion)),
        UserRolesChangedEvent e => CreateEnvelope(new ContractsEvents.UserRolesChangedEvent(e.TenantNId, e.UserNId, e.RoleNId)),
        UserDeletedEvent e => CreateEnvelope(new ContractsEvents.UserDeletedEvent(e.TenantNId, e.UserNId, e.AuthVersion)),
        UserRestoredEvent e => CreateEnvelope(new ContractsEvents.UserRestoredEvent(e.TenantNId, e.UserNId)),
        _ => null,
    };

    private static OutboxEnvelope CreateEnvelope<TEvent>(TEvent integrationEvent)
        where TEvent : ContractsEvents.IdentityIntegrationEvent
        => new(
            integrationEvent.EventId,
            integrationEvent.EventTypeName,
            integrationEvent.EventVersion,
            ContractsEvents.IntegrationEventJson.Serialize(integrationEvent),
            integrationEvent.CreatedTime);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Error, Message = "操作审计写入失败,已忽略。")]
    private static partial void LogOperationAuditWriteFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning, Message = "权限缓存失效失败,已忽略(TTL 兜底)。")]
    private static partial void LogPermissionCacheInvalidateFailed(ILogger logger, Exception ex);

    private static string? TraceId => Activity.Current?.TraceId.ToString();

    /// <summary>用户业务标识生成(未提供 NId 时):字母开头,符合 NId 格式。</summary>
    private static string GenerateUserNId() => "USR-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
}
