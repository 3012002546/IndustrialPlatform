using System.Diagnostics;
using System.Security.Cryptography;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Identity.Application.Management;
using ContractsEvents = IndustrialPlatform.Identity.Contracts.Events;
using Identities = IndustrialPlatform.Identity.Domain.Identities;
using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.Identity.Domain.UserGroups;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SharedKernel.Events;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.Identity.Application.UserGroups;

/// <summary>
/// 用户组管理用例实现(§29A.2/§29A.6)。编排组存储、管理存储、会话撤销与权限缓存;
/// 成员/组角色/禁用变化按权威计数守卫最后系统管理员,并在组写入提交后对全部受影响用户
/// 推进授权版本(使旧 Access Token 失效)、撤销刷新会话并失效版本化权限缓存,
/// 集成事件经 Outbox 与组写入同事务提交。
/// </summary>
public sealed partial class UserGroupService : IUserGroupService
{
    private const int AuthVersionBumpRetryLimit = 3;

    private readonly IUserGroupStore _groupStore;
    private readonly IManagementStore _store;
    private readonly IRefreshSessionStore _refreshStore;
    private readonly IPermissionCache _permissionCache;
    private readonly IOperationAuditSink _auditSink;
    private readonly ILogger<UserGroupService> _logger;

    /// <summary>初始化用户组管理用例。</summary>
    public UserGroupService(
        IUserGroupStore groupStore,
        IManagementStore store,
        IRefreshSessionStore refreshStore,
        IPermissionCache permissionCache,
        IOperationAuditSink auditSink,
        ILogger<UserGroupService> logger)
    {
        ArgumentNullException.ThrowIfNull(groupStore);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(refreshStore);
        ArgumentNullException.ThrowIfNull(permissionCache);
        ArgumentNullException.ThrowIfNull(auditSink);
        _groupStore = groupStore;
        _store = store;
        _refreshStore = refreshStore;
        _permissionCache = permissionCache;
        _auditSink = auditSink;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<UserGroupSummary> CreateAsync(
        string tenantNId,
        string actorUserNId,
        CreateUserGroupCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var nId = Identities.NId.Create(string.IsNullOrWhiteSpace(command.NId) ? GenerateGroupNId() : command.NId!);
        var name = command.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("用户组名称不能为空。");
        }

        if (await _groupStore.UserGroupExistsByNIdAsync(nId.Value, cancellationToken))
        {
            throw new GroupNIdConflictException();
        }

        var group = UserGroup.Create(tenantNId, nId.Value, name, command.Description);

        var memberNIds = (command.MemberUserNIds ?? [])
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (memberNIds.Length > 0)
        {
            var members = await _groupStore.GetUsersByNIdsAsync(memberNIds, cancellationToken);
            if (members.Count != memberNIds.Length || members.Any(u => u.TenantNId != tenantNId))
            {
                throw new BusinessRuleViolationException("存在无效或不可用的成员用户。");
            }

            foreach (var member in members)
            {
                group.AssignMember(member);
            }
        }

        var roleNIds = (command.RoleNIds ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (roleNIds.Length > 0)
        {
            var roles = await _store.GetRolesByNIdsAsync(roleNIds, cancellationToken);
            if (roles.Count != roleNIds.Length || roles.Any(r => r.TenantNId != tenantNId))
            {
                throw new BusinessRuleViolationException("存在无效或不可用的角色。");
            }

            foreach (var role in roles)
            {
                group.AssignRole(role);
            }
        }

        // 用户组创建事件与组写入同事务提交(§29A.6)。
        await ExecuteWriteAsync(() => _groupStore.AddAsync(group, BuildOutboxEnvelopes(group), cancellationToken));
        group.ClearDomainEvents();

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                OperationAction.UserGroupCreate,
                OperationObjectType.UserGroup,
                group.NId,
                null,
                BuildGroupSummaryText(group),
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return await GetAsync(tenantNId, group.NId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<UserGroupSummary> UpdateAsync(
        string tenantNId,
        string actorUserNId,
        string groupNId,
        UpdateUserGroupCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var group = await RequireGroupAsync(tenantNId, groupNId, cancellationToken);
        var before = BuildGroupSummaryText(group);

        group.ChangeProfile(command.Name, command.Description);

        // 资料变更不改变授权,Outbox 仍随组写入同事务提交(§29A.6)。
        await ExecuteWriteAsync(() => _groupStore.UpdateAsync(
            group,
            command.ExpectedOptimisticVersion,
            command.ExpectedConcurrencyVersion,
            BuildOutboxEnvelopes(group),
            cancellationToken));
        group.ClearDomainEvents();

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                OperationAction.UserGroupUpdate,
                OperationObjectType.UserGroup,
                group.NId,
                before,
                BuildGroupSummaryText(group),
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return await GetAsync(tenantNId, groupNId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<UserGroupSummary> SetStatusAsync(
        string tenantNId,
        string actorUserNId,
        string groupNId,
        bool enabled,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken)
    {
        var group = await RequireGroupAsync(tenantNId, groupNId, cancellationToken);
        var before = BuildGroupSummaryText(group);

        if (enabled)
        {
            await ExecuteWriteAsync(async () =>
            {
                group.Enable();
                await _groupStore.UpdateAsync(
                    group,
                    expectedOptimisticVersion,
                    expectedConcurrencyVersion,
                    BuildOutboxEnvelopes(group),
                    cancellationToken);
                group.ClearDomainEvents();
            });
        }
        else
        {
            // 禁用守卫:组内每个系统角色都不得使任何成员成为租户最后一名系统管理员。
            var systemRoles = await LoadSystemRolesAsync(group, tenantNId, cancellationToken);
            await ExecuteWriteAsync(async () =>
            {
                foreach (var role in systemRoles)
                {
                    await EnsureRoleRemovalKeepsSystemAdminAsync(group, role, tenantNId, cancellationToken);
                }

                group.Disable();
                await _groupStore.UpdateAsync(
                    group,
                    expectedOptimisticVersion,
                    expectedConcurrencyVersion,
                    BuildOutboxEnvelopes(group),
                    cancellationToken);
                group.ClearDomainEvents();
            });
        }

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                enabled ? OperationAction.UserGroupEnable : OperationAction.UserGroupDisable,
                OperationObjectType.UserGroup,
                group.NId,
                before,
                BuildGroupSummaryText(group),
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        // 禁用组立即停止贡献角色,全部成员授权受影响(§29A.2)。
        if (!enabled)
        {
            var memberNIds = await _groupStore.GetMemberUserNIdsAsync(group.Id, tenantNId, cancellationToken);
            await InvalidateAffectedUsersAsync(memberNIds, tenantNId, "user_group_disabled", cancellationToken);
        }

        return await GetAsync(tenantNId, groupNId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<UserGroupSummary> SetMembersAsync(
        string tenantNId,
        string actorUserNId,
        string groupNId,
        IReadOnlyCollection<string> memberUserNIds,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken)
    {
        var group = await RequireGroupAsync(tenantNId, groupNId, cancellationToken);
        var before = BuildGroupSummaryText(group);

        var requestedNIds = (memberUserNIds ?? [])
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var members = requestedNIds.Length == 0 ? [] : await _groupStore.GetUsersByNIdsAsync(requestedNIds, cancellationToken);
        if (members.Count != requestedNIds.Length || members.Any(u => u.TenantNId != tenantNId))
        {
            throw new BusinessRuleViolationException("存在无效或不可用的成员用户。");
        }

        var currentMembers = await _groupStore.GetMembersAsync(group.Id, tenantNId, cancellationToken);
        var currentIds = currentMembers.Select(u => u.Id).ToHashSet();
        var requestedIds = members.Select(u => u.Id).ToHashSet();

        var removedMembers = currentMembers.Where(u => !requestedIds.Contains(u.Id)).ToArray();
        var addedMembers = members.Where(u => !currentIds.Contains(u.Id)).ToArray();

        // 领域变更(可能抛 BusinessException)与持久化写入处于同一映射边界。
        await ExecuteWriteAsync(async () =>
        {
            foreach (var user in removedMembers)
            {
                await EnsureMemberRemovalKeepsSystemAdminAsync(user, group, tenantNId, cancellationToken);
            }

            foreach (var user in removedMembers)
            {
                group.RemoveMember(user);
            }

            foreach (var user in addedMembers)
            {
                group.AssignMember(user);
            }

            await _groupStore.UpdateAsync(
                group,
                expectedOptimisticVersion,
                expectedConcurrencyVersion,
                BuildOutboxEnvelopes(group),
                cancellationToken);
            group.ClearDomainEvents();
        });

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                OperationAction.UserGroupSetMembers,
                OperationObjectType.UserGroup,
                group.NId,
                before,
                BuildGroupSummaryText(group),
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        // 加入与移除的成员有效角色都发生变化,全部推进授权版本并失效会话/缓存(§29A.2)。
        var affectedNIds = removedMembers.Select(u => u.NId)
            .Concat(addedMembers.Select(u => u.NId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        await InvalidateAffectedUsersAsync(affectedNIds, tenantNId, "user_group_membership_changed", cancellationToken);

        return await GetAsync(tenantNId, groupNId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<UserGroupSummary> SetRolesAsync(
        string tenantNId,
        string actorUserNId,
        string groupNId,
        IReadOnlyCollection<string> roleNIds,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken)
    {
        var group = await RequireGroupAsync(tenantNId, groupNId, cancellationToken);
        var before = BuildGroupSummaryText(group);

        var requestedNIds = (roleNIds ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var roles = requestedNIds.Length == 0 ? [] : await _store.GetRolesByNIdsAsync(requestedNIds, cancellationToken);
        if (roles.Count != requestedNIds.Length || roles.Any(r => r.TenantNId != tenantNId))
        {
            throw new BusinessRuleViolationException("存在无效或不可用的角色。");
        }

        var requestedRoleIds = roles.Select(r => r.Id).ToHashSet();
        var currentActive = group.Roles.Where(r => !r.IsDeleted && !r.RoleIsDeleted).ToArray();
        var removalRoleIds = currentActive.Select(r => r.RoleId).Where(id => !requestedRoleIds.Contains(id)).ToArray();
        var removalRoles = removalRoleIds.Length == 0 ? [] : await _store.GetRolesByIdsAsync(removalRoleIds, cancellationToken);

        await ExecuteWriteAsync(async () =>
        {
            foreach (var role in removalRoles)
            {
                await EnsureRoleRemovalKeepsSystemAdminAsync(group, role, tenantNId, cancellationToken);
            }

            foreach (var role in removalRoles)
            {
                group.RemoveRole(role);
            }

            foreach (var role in roles)
            {
                if (!currentActive.Any(r => r.RoleId == role.Id))
                {
                    group.AssignRole(role);
                }
            }

            await _groupStore.UpdateAsync(
                group,
                expectedOptimisticVersion,
                expectedConcurrencyVersion,
                BuildOutboxEnvelopes(group),
                cancellationToken);
            group.ClearDomainEvents();
        });

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                OperationAction.UserGroupSetRoles,
                OperationObjectType.UserGroup,
                group.NId,
                before,
                BuildGroupSummaryText(group),
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        // 组角色变化影响全部成员的有效角色(§29A.2)。
        var memberNIds = await _groupStore.GetMemberUserNIdsAsync(group.Id, tenantNId, cancellationToken);
        await InvalidateAffectedUsersAsync(memberNIds, tenantNId, "user_group_roles_changed", cancellationToken);

        return await GetAsync(tenantNId, groupNId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<UserGroupSummary> GetAsync(string tenantNId, string groupNId, CancellationToken cancellationToken)
    {
        var group = await RequireGroupAsync(tenantNId, groupNId, cancellationToken);

        var memberNIds = await _groupStore.GetMemberUserNIdsAsync(group.Id, tenantNId, cancellationToken);
        var activeRoleIds = group.Roles.Where(r => !r.IsDeleted && !r.RoleIsDeleted).Select(r => r.RoleId).ToArray();
        var roleNIdByRoleId = activeRoleIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await _store.GetRoleNIdsAsync(activeRoleIds, cancellationToken);
        var roleNIds = activeRoleIds
            .Where(roleNIdByRoleId.ContainsKey)
            .Select(id => roleNIdByRoleId[id])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        return new UserGroupSummary(
            group.NId,
            group.Name,
            group.Description,
            group.Status.ToString(),
            group.TenantNId,
            memberNIds,
            roleNIds,
            group.OptimisticVersion,
            group.ConcurrencyVersion);
    }

    private async Task<UserGroup> RequireGroupAsync(string tenantNId, string groupNId, CancellationToken cancellationToken)
    {
        var group = await _groupStore.GetUserGroupAggregateAsync(groupNId, cancellationToken);
        if (group is null || !string.Equals(group.TenantNId, tenantNId, StringComparison.Ordinal))
        {
            throw new ResourceNotFoundException();
        }

        return group;
    }

    /// <summary>装载用户组当前活动角色中的系统角色聚合。</summary>
    private async Task<IReadOnlyList<Role>> LoadSystemRolesAsync(UserGroup group, string tenantNId, CancellationToken cancellationToken)
    {
        var roleIds = group.Roles.Where(r => !r.IsDeleted && !r.RoleIsDeleted).Select(r => r.RoleId).ToArray();
        if (roleIds.Length == 0)
        {
            return [];
        }

        var roles = await _store.GetRolesByIdsAsync(roleIds, cancellationToken);
        return roles.Where(r => r.IsSystem).ToList();
    }

    /// <summary>
    /// 移除成员守卫(§29A.3):被移除用户若只经本组持有系统角色、且该角色在租户内仅剩其一名
    /// 有效持有者(权威计数含组继承),则拒绝,防止剥夺最后一名系统管理员。
    /// </summary>
    private async Task EnsureMemberRemovalKeepsSystemAdminAsync(
        User user,
        UserGroup group,
        string tenantNId,
        CancellationToken cancellationToken)
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
            // 用户在本组之外仍持有该角色(直接或其他组)→ 移除本组不剥夺其管理员身份。
            if (await _groupStore.UserHoldsRoleOutsideGroupAsync(user.Id, group.Id, systemRoleId, tenantNId, cancellationToken))
            {
                continue;
            }

            var holders = await _store.CountActiveRoleHoldersAsync(systemRoleId, tenantNId, cancellationToken);
            if (holders <= 1)
            {
                throw new BusinessRuleViolationException("不能移除最后一名系统管理员,除非经过独立恢复流程。");
            }
        }
    }

    /// <summary>
    /// 组角色/禁用守卫(§29A.3):移除或停用系统角色时,若存在仅经本组持有该角色的成员、
    /// 且该角色在租户内仅剩其一名有效持有者,则拒绝。
    /// </summary>
    private async Task EnsureRoleRemovalKeepsSystemAdminAsync(
        UserGroup group,
        Role role,
        string tenantNId,
        CancellationToken cancellationToken)
    {
        if (!role.IsSystem)
        {
            return;
        }

        if (!await _groupStore.AnyMemberHoldsRoleOnlyViaGroupAsync(group.Id, role.Id, tenantNId, cancellationToken))
        {
            return;
        }

        var holders = await _store.CountActiveRoleHoldersAsync(role.Id, tenantNId, cancellationToken);
        if (holders <= 1)
        {
            throw new BusinessRuleViolationException("不能移除最后一名系统管理员,除非经过独立恢复流程。");
        }
    }

    /// <summary>
    /// 受影响用户授权失效(§29A.2):逐个推进授权版本(旧 Access Token 的 ver 声明失效)、
    /// 撤销全部刷新会话并删除版本化权限缓存;数据库已提交新版本,缓存失效为尽力而为。
    /// </summary>
    private async Task InvalidateAffectedUsersAsync(
        IReadOnlyCollection<string> userNIds,
        string tenantNId,
        string revokeReason,
        CancellationToken cancellationToken)
    {
        foreach (var userNId in userNIds.Distinct(StringComparer.Ordinal))
        {
            var user = await BumpAuthVersionAsync(userNId, cancellationToken);
            if (user is null)
            {
                continue;
            }

            await TryRevokeSessionsAsync(user.Id, revokeReason, cancellationToken);
            await TryInvalidatePermissionCacheAsync(tenantNId, userNId, cancellationToken);
        }
    }

    /// <summary>推进单个用户授权版本(乐观并发冲突时重新装载重试)。</summary>
    private async Task<User?> BumpAuthVersionAsync(string userNId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var user = await _store.GetUserAggregateAsync(userNId, cancellationToken);
            if (user is null || user.IsDeleted)
            {
                return null;
            }

            var expectedOptimistic = user.OptimisticVersion;
            var expectedConcurrency = user.ConcurrencyVersion;
            user.IncrementAuthVersion();

            try
            {
                await _store.UpdateUserAsync(user, expectedOptimistic, expectedConcurrency, [], cancellationToken);
                return user;
            }
            catch (ConcurrencyException) when (attempt < AuthVersionBumpRetryLimit)
            {
                // 并发登录等同时推进版本,重新装载后重试。
            }
            catch (ConcurrencyException)
            {
                LogAuthVersionBumpSkipped(_logger, userNId);
                return null;
            }
        }
    }

    private async Task TryRevokeSessionsAsync(Guid userId, string reason, CancellationToken cancellationToken)
    {
        try
        {
            await _refreshStore.RevokeAllForUserAsync(userId, reason, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSessionRevokeFailed(_logger, userId, ex);
        }
    }

    private async Task TryInvalidatePermissionCacheAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
    {
        try
        {
            await _permissionCache.InvalidateAsync(tenantNId, userNId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPermissionCacheInvalidateFailed(_logger, userNId, ex);
        }
    }

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

    /// <summary>审计摘要:仅非敏感资料字段与成员/角色数量;绝不包含数据库主键或敏感信息。</summary>
    private static string BuildGroupSummaryText(UserGroup group) =>
        $"name={group.Name},description={group.Description ?? "-"},status={group.Status},memberCount={group.Memberships.Count(m => !m.IsDeleted)},roleCount={group.Roles.Count(r => !r.IsDeleted)}";

    /// <summary>把聚合上尚未派发的领域事件映射为 Outbox 信封(§29A.6,版本化事件,subjectNId=GroupNId)。</summary>
    private static OutboxEnvelope[] BuildOutboxEnvelopes(UserGroup group)
    {
        if (group.DomainEvents.Count == 0)
        {
            return [];
        }

        return group.DomainEvents
            .Select(MapToEnvelope)
            .Where(e => e is not null)
            .Select(e => e!)
            .ToArray();
    }

    private static OutboxEnvelope? MapToEnvelope(IDomainEvent domainEvent) => domainEvent switch
    {
        UserGroupCreatedEvent e => CreateEnvelope(new ContractsEvents.UserGroupCreatedEvent(e.TenantNId, e.GroupNId, e.Name, e.Status.ToString())),
        UserGroupChangedEvent e => CreateEnvelope(new ContractsEvents.UserGroupChangedEvent(e.TenantNId, e.GroupNId, e.Name, e.Status.ToString())),
        UserGroupMembershipChangedEvent e => CreateEnvelope(new ContractsEvents.UserGroupMembershipChangedEvent(e.TenantNId, e.GroupNId, e.UserNId)),
        UserGroupRolesChangedEvent e => CreateEnvelope(new ContractsEvents.UserGroupRolesChangedEvent(e.TenantNId, e.GroupNId)),
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

    [LoggerMessage(EventId = 2201, Level = LogLevel.Error, Message = "操作审计写入失败,已忽略。")]
    private static partial void LogOperationAuditWriteFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 2202, Level = LogLevel.Warning, Message = "会话撤销失败,已忽略。用户: {userId}")]
    private static partial void LogSessionRevokeFailed(ILogger logger, Guid userId, Exception ex);

    [LoggerMessage(EventId = 2203, Level = LogLevel.Warning, Message = "权限缓存失效失败,已忽略(TTL 兜底)。用户: {userNId}")]
    private static partial void LogPermissionCacheInvalidateFailed(ILogger logger, string userNId, Exception ex);

    [LoggerMessage(EventId = 2204, Level = LogLevel.Warning, Message = "授权版本推进并发冲突,已降级放弃。用户: {userNId}")]
    private static partial void LogAuthVersionBumpSkipped(ILogger logger, string userNId);

    private static string? TraceId => Activity.Current?.TraceId.ToString();

    /// <summary>用户组业务标识生成(未提供 NId 时):字母开头,符合 NId 格式。</summary>
    private static string GenerateGroupNId() => "GRP-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
}
