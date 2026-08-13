using System.Diagnostics;
using System.Security.Cryptography;
using IndustrialPlatform.Identity.Application.Authorization;
using ContractsEvents = IndustrialPlatform.Identity.Contracts.Events;
using IndustrialPlatform.Identity.Contracts.Management;
using Identities = IndustrialPlatform.Identity.Domain.Identities;
using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SharedKernel.Events;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.Identity.Application.Management;

/// <summary>
/// 角色管理用例实现(§16.2/§19.2)。编排管理存储与权限缓存失效;
/// 角色权限变化后按持有者逐个失效版本化缓存键,敏感字段绝不进入审计与日志。
/// </summary>
public sealed partial class RoleManagementService : IRoleManagementService
{
    private readonly IManagementStore _store;
    private readonly IPermissionCache _permissionCache;
    private readonly IOperationAuditSink _auditSink;
    private readonly ILogger<RoleManagementService> _logger;

    public RoleManagementService(
        IManagementStore store,
        IPermissionCache permissionCache,
        IOperationAuditSink auditSink,
        ILogger<RoleManagementService> logger)
    {
        _store = store;
        _permissionCache = permissionCache;
        _auditSink = auditSink;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<RoleSummary> CreateAsync(
        string tenantNId,
        string actorUserNId,
        CreateRoleRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nId = Identities.NId.Create(string.IsNullOrWhiteSpace(request.NId) ? GenerateRoleNId() : request.NId!);
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("角色名称不能为空。");
        }

        if (await _store.RoleExistsByNIdAsync(nId.Value, cancellationToken))
        {
            throw new RoleNIdConflictException();
        }

        var role = Role.Create(tenantNId, nId.Value, name, request.Description, isSystem: false);

        var permissionNIds = (request.PermissionNIds ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal).ToArray();
        if (permissionNIds.Length > 0)
        {
            var permissions = await _store.GetPermissionsByNIdsAsync(permissionNIds, cancellationToken);
            if (permissions.Count != permissionNIds.Length)
            {
                throw new BusinessRuleViolationException("存在无效或不可用的权限。");
            }

            foreach (var permission in permissions)
            {
                role.AssignPermission(permission);
            }
        }

        // 角色创建不产生集成事件(§20 五类事件不含角色创建),Outbox 为空。
        await ExecuteWriteAsync(() => _store.AddRoleAsync(role, [], cancellationToken));

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                OperationAction.RoleCreate,
                OperationObjectType.Role,
                role.NId,
                null,
                BuildRoleSummaryText(role),
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return await GetAsync(tenantNId, role.NId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RoleSummary> UpdateAsync(
        string tenantNId,
        string actorUserNId,
        string roleNId,
        UpdateRoleRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var role = await RequireRoleAsync(tenantNId, roleNId, cancellationToken);
        var before = BuildRoleSummaryText(role);

        role.ChangeProfile(request.Name ?? role.Name, request.Description);

        // 角色资料变更不产生集成事件,Outbox 为空。
        await ExecuteWriteAsync(() => _store.UpdateRoleAsync(
            role,
            request.ExpectedOptimisticVersion,
            request.ExpectedConcurrencyVersion,
            [],
            cancellationToken));

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                OperationAction.RoleUpdate,
                OperationObjectType.Role,
                role.NId,
                before,
                BuildRoleSummaryText(role),
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return await GetAsync(tenantNId, roleNId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RoleSummary> AssignPermissionsAsync(
        string tenantNId,
        string actorUserNId,
        string roleNId,
        AssignRolePermissionsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var role = await RequireRoleAsync(tenantNId, roleNId, cancellationToken);
        var before = BuildRoleSummaryText(role);

        var requestedNIds = (request.PermissionNIds ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal).ToArray();
        var permissionsByNId = requestedNIds.Length == 0
            ? new Dictionary<string, Permission>(StringComparer.Ordinal)
            : (await _store.GetPermissionsByNIdsAsync(requestedNIds, cancellationToken)).ToDictionary(p => p.NId, StringComparer.Ordinal);
        if (permissionsByNId.Count != requestedNIds.Length)
        {
            throw new BusinessRuleViolationException("存在无效或不可用的权限。");
        }

        var currentActive = role.Permissions.Where(p => !p.IsDeleted && !p.PermissionIsDeleted).ToArray();
        var requestedIds = permissionsByNId.Values.Select(p => p.Id).ToHashSet();

        // 差量解除:不在新集合中的活动关系。
        var removalIds = currentActive.Select(rp => rp.PermissionId).Where(id => !requestedIds.Contains(id)).ToArray();
        if (removalIds.Length > 0)
        {
            var removalPermissions = await _store.GetPermissionsByIdsAsync(removalIds, cancellationToken);
            foreach (var permission in removalPermissions)
            {
                role.UnassignPermission(permission);
            }
        }

        // 差量分配:已持有的权限跳过(领域重复分配守卫兜底)。
        foreach (var permission in permissionsByNId.Values)
        {
            if (!currentActive.Any(rp => rp.PermissionId == permission.Id))
            {
                role.AssignPermission(permission);
            }
        }

        // 权限变化事件与角色写入同事务提交(§20,subjectNId=RoleNId)。
        var assignEnvelopes = BuildOutboxEnvelopes(role);
        await ExecuteWriteAsync(() => _store.UpdateRoleAsync(
            role,
            request.ExpectedOptimisticVersion,
            request.ExpectedConcurrencyVersion,
            assignEnvelopes,
            cancellationToken));
        role.ClearDomainEvents();

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                OperationAction.RoleAssignPermissions,
                OperationObjectType.Role,
                role.NId,
                before,
                BuildRoleSummaryText(role),
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        await InvalidateHoldersPermissionCacheAsync(role, tenantNId, cancellationToken);

        return await GetAsync(tenantNId, roleNId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ManagementPage<RoleSummary>> ListAsync(string tenantNId, RoleListFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var page = await _store.QueryRolesAsync(filter with { TenantNId = tenantNId }, cancellationToken);
        var items = page.Items.Select(ToSummary).ToList();
        return new ManagementPage<RoleSummary>(items, page.Total, filter.PageIndex, filter.PageSize);
    }

    /// <inheritdoc/>
    public async Task<RoleSummary> GetAsync(string tenantNId, string roleNId, CancellationToken cancellationToken)
    {
        var stored = await _store.GetRoleAsync(roleNId, cancellationToken);
        if (stored is null || !string.Equals(stored.TenantNId, tenantNId, StringComparison.Ordinal))
        {
            throw new ResourceNotFoundException();
        }

        return ToSummary(stored);
    }

    private async Task<Role> RequireRoleAsync(string tenantNId, string roleNId, CancellationToken cancellationToken)
    {
        var role = await _store.GetRoleAggregateAsync(roleNId, cancellationToken);
        if (role is null || !string.Equals(role.TenantNId, tenantNId, StringComparison.Ordinal))
        {
            throw new ResourceNotFoundException();
        }

        return role;
    }

    private static RoleSummary ToSummary(StoredRole r) => new(
        r.NId,
        r.Name,
        r.Description,
        r.IsSystem,
        r.TenantNId,
        r.PermissionNIds,
        r.OptimisticVersion,
        r.ConcurrencyVersion);

    /// <summary>审计摘要:仅非敏感资料字段与权限数量;绝不包含密码、Token、内部哈希或数据库主键。</summary>
    private static string BuildRoleSummaryText(Role role) =>
        $"name={role.Name},description={role.Description ?? "-"},isSystem={role.IsSystem},permissionCount={role.Permissions.Count(p => !p.IsDeleted)}";

    /// <summary>角色权限变化后按持有者逐个失效权限缓存(尽力而为);新分配不推进 AuthVersion。</summary>
    private async Task InvalidateHoldersPermissionCacheAsync(Role role, string tenantNId, CancellationToken cancellationToken)
    {
        var holderNIds = await _store.GetUserNIdsForRoleAsync(role.Id, tenantNId, cancellationToken);
        foreach (var holderNId in holderNIds)
        {
            try
            {
                await _permissionCache.InvalidateAsync(tenantNId, holderNId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogPermissionCacheInvalidateFailed(_logger, ex);
            }
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

    /// <summary>把聚合上尚未派发的领域事件映射为 Outbox 信封(§20,版本化事件,subjectNId=RoleNId)。</summary>
    private static OutboxEnvelope[] BuildOutboxEnvelopes(Role role)
    {
        if (role.DomainEvents.Count == 0)
        {
            return [];
        }

        return role.DomainEvents
            .Select(MapToEnvelope)
            .Where(e => e is not null)
            .Select(e => e!)
            .ToArray();
    }

    private static OutboxEnvelope? MapToEnvelope(IDomainEvent domainEvent) => domainEvent switch
    {
        RolePermissionsChangedEvent e => CreateEnvelope(new ContractsEvents.RolePermissionsChangedEvent(e.TenantNId, e.RoleNId, e.PermissionNId)),
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

    [LoggerMessage(EventId = 2101, Level = LogLevel.Error, Message = "操作审计写入失败,已忽略。")]
    private static partial void LogOperationAuditWriteFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Warning, Message = "权限缓存失效失败,已忽略(TTL 兜底)。")]
    private static partial void LogPermissionCacheInvalidateFailed(ILogger logger, Exception ex);

    private static string? TraceId => Activity.Current?.TraceId.ToString();

    /// <summary>角色业务标识生成(未提供 NId 时):字母开头,符合 NId 格式。</summary>
    private static string GenerateRoleNId() => "ROLE-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
}
