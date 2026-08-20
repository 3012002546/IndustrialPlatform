using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Application.Administration;
using IndustrialPlatform.SystemData.Application.Assignments;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Application.IdentityDirectory;
using IndustrialPlatform.SystemData.Application.Organizations;
using IndustrialPlatform.SystemData.Application.Positions;
using IndustrialPlatform.SystemData.Contracts.Administration;
using IndustrialPlatform.SystemData.Domain.Assignments;
using IndustrialPlatform.SystemData.Domain.Organizations;
using IndustrialPlatform.SystemData.Domain.Positions;

namespace IndustrialPlatform.SystemData.Application.Tests;

/// <summary>
/// 管理用例测试替身(TASK-SD-006):组织/岗位/任职存储端口、用户目录端口、
/// advisory lock、本地审计命令与手动时钟的进程内实现。与 FakeDatabaseOrchestrationStore
/// 同模式:Add 遇唯一冲突、Update 遇双版本不匹配抛 <see cref="ConcurrencyException"/>,
/// 由应用层 AdministrationWriteGuard 映射为 §9.9 结构化错误码。
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset now)
    {
        _now = now;
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan span) => _now += span;

    public void SetNow(DateTimeOffset now) => _now = now;
}

/// <summary>行政组织存储端口的进程内假实现。</summary>
internal sealed class FakeAdministrativeOrganizationStore : IAdministrativeOrganizationStore
{
    private readonly List<AdministrativeOrganization> _items = [];
    private readonly Dictionary<(string TenantNId, string NId), (long Optimistic, Guid Concurrency)> _versions = [];

    /// <summary>停用门依赖计数覆盖(按组织 NId);未配置默认 (0,0,0)。</summary>
    public Dictionary<string, OrganizationDependencyCounts> DependencyCounts { get; } = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<AdministrativeOrganization?> GetAsync(string tenantNId, string organizationNId, CancellationToken cancellationToken) =>
        Task.FromResult(_items.FirstOrDefault(o => o.TenantNId == tenantNId && o.NId == organizationNId && !o.IsDeleted));

    /// <inheritdoc />
    public Task<bool> NameAvailableAsync(
        string tenantNId,
        string? parentOrganizationNId,
        string name,
        string? excludeOrganizationNId,
        CancellationToken cancellationToken)
    {
        var available = !_items.Any(o => o.TenantNId == tenantNId
            && o.ParentOrganizationNId == parentOrganizationNId
            && string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)
            && o.NId != excludeOrganizationNId
            && !o.IsDeleted);
        return Task.FromResult(available);
    }

    /// <inheritdoc />
    public Task<OrganizationDependencyCounts> GetDependencyCountsAsync(
        string tenantNId,
        string organizationNId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult(DependencyCounts.TryGetValue(organizationNId, out var counts)
            ? counts
            : new OrganizationDependencyCounts(0, 0, 0));

    /// <inheritdoc />
    public Task<OrganizationSubtreeCounts> GetSubtreeCountsAsync(
        string tenantNId,
        string rootOrganizationNId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var descendants = DescendantNIds(rootOrganizationNId);
        return Task.FromResult(new OrganizationSubtreeCounts(descendants.Count, 0, 0));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetDescendantNIdsAsync(
        string tenantNId,
        string rootOrganizationNId,
        CancellationToken cancellationToken)
    {
        var descendants = DescendantNIds(rootOrganizationNId);
        return Task.FromResult<IReadOnlyList<string>>(descendants);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AdministrativeOrganization>> GetAllAsync(string tenantNId, CancellationToken cancellationToken)
    {
        var items = _items.Where(o => o.TenantNId == tenantNId && !o.IsDeleted).ToList();
        return Task.FromResult<IReadOnlyList<AdministrativeOrganization>>(items);
    }

    /// <inheritdoc />
    public Task AddAsync(AdministrativeOrganization organization, CancellationToken cancellationToken)
    {
        var key = (organization.TenantNId, organization.NId);
        if (_items.Any(o => o.TenantNId == organization.TenantNId && o.NId == organization.NId))
        {
            throw new ConcurrencyException("组织业务标识已存在。");
        }

        _items.Add(organization);
        _versions[key] = (organization.OptimisticVersion, organization.ConcurrencyVersion);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateAsync(
        AdministrativeOrganization organization,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken)
    {
        var index = _items.FindIndex(o => o.TenantNId == organization.TenantNId && o.NId == organization.NId && !o.IsDeleted);
        if (index < 0)
        {
            throw new ConcurrencyException("组织不存在。");
        }

        var key = (organization.TenantNId, organization.NId);
        if (!_versions.TryGetValue(key, out var snapshot)
            || snapshot.Optimistic != expectedOptimisticVersion
            || snapshot.Concurrency != expectedConcurrencyVersion)
        {
            throw new ConcurrencyException("双版本不匹配。");
        }

        _items[index] = organization;
        _versions[key] = (organization.OptimisticVersion, organization.ConcurrencyVersion);
        return Task.CompletedTask;
    }

    /// <summary>以父链接展开子树组织 NId(含根),用于循环校验与子树计数。</summary>
    private List<string> DescendantNIds(string rootOrganizationNId)
    {
        var result = new List<string>();
        var queue = new Queue<string>();
        queue.Enqueue(rootOrganizationNId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(current);
            foreach (var child in _items.Where(o => o.ParentOrganizationNId == current))
            {
                queue.Enqueue(child.NId);
            }
        }

        return result;
    }
}

/// <summary>岗位存储端口的进程内假实现(含分页过滤)。</summary>
internal sealed class FakePositionStore : IPositionStore
{
    private readonly List<Position> _items = [];
    private readonly Dictionary<(string TenantNId, string NId), (long Optimistic, Guid Concurrency)> _versions = [];

    /// <inheritdoc />
    public Task<Position?> GetAsync(string tenantNId, string positionNId, CancellationToken cancellationToken) =>
        Task.FromResult(_items.FirstOrDefault(p => p.TenantNId == tenantNId && p.NId == positionNId && !p.IsDeleted));

    /// <inheritdoc />
    public Task<bool> NameAvailableAsync(
        string tenantNId,
        string organizationNId,
        string name,
        string? excludePositionNId,
        CancellationToken cancellationToken)
    {
        var available = !_items.Any(p => p.TenantNId == tenantNId
            && p.OrganizationNId == organizationNId
            && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
            && p.NId != excludePositionNId
            && !p.IsDeleted);
        return Task.FromResult(available);
    }

    /// <inheritdoc />
    public Task<PositionListPage> QueryPositionsAsync(PositionListFilter filter, CancellationToken cancellationToken)
    {
        IEnumerable<Position> query = _items.Where(p => p.TenantNId == filter.TenantNId && !p.IsDeleted);
        if (filter.OrganizationNId is not null)
        {
            query = query.Where(p => p.OrganizationNId == filter.OrganizationNId);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(p => p.Status == status);
        }

        var ordered = query
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
        var total = ordered.Count;
        var page = ordered.Skip((filter.PageIndex - 1) * filter.PageSize).Take(filter.PageSize).ToList();
        return Task.FromResult(new PositionListPage(page, total));
    }

    /// <inheritdoc />
    public Task AddAsync(Position position, CancellationToken cancellationToken)
    {
        var key = (position.TenantNId, position.NId);
        if (_items.Any(p => p.TenantNId == position.TenantNId && p.NId == position.NId))
        {
            throw new ConcurrencyException("岗位业务标识已存在。");
        }

        _items.Add(position);
        _versions[key] = (position.OptimisticVersion, position.ConcurrencyVersion);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateAsync(
        Position position,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken)
    {
        var index = _items.FindIndex(p => p.TenantNId == position.TenantNId && p.NId == position.NId && !p.IsDeleted);
        if (index < 0)
        {
            throw new ConcurrencyException("岗位不存在。");
        }

        var key = (position.TenantNId, position.NId);
        if (!_versions.TryGetValue(key, out var snapshot)
            || snapshot.Optimistic != expectedOptimisticVersion
            || snapshot.Concurrency != expectedConcurrencyVersion)
        {
            throw new ConcurrencyException("双版本不匹配。");
        }

        _items[index] = position;
        _versions[key] = (position.OptimisticVersion, position.ConcurrencyVersion);
        return Task.CompletedTask;
    }
}

/// <summary>用户任职存储端口的进程内假实现。</summary>
internal sealed class FakeUserAssignmentStore : IUserAssignmentStore
{
    private readonly List<UserAssignment> _items = [];
    private readonly Dictionary<(string TenantNId, string NId), (long Optimistic, Guid Concurrency)> _versions = [];

    /// <inheritdoc />
    public Task<UserAssignment?> GetAsync(string tenantNId, string assignmentNId, CancellationToken cancellationToken) =>
        Task.FromResult(_items.FirstOrDefault(a => a.TenantNId == tenantNId && a.NId == assignmentNId && !a.IsDeleted));

    /// <inheritdoc />
    public Task<IReadOnlyList<UserAssignment>> GetAssignmentsForUserAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
    {
        var items = _items.Where(a => a.TenantNId == tenantNId && a.UserNId == userNId && !a.IsDeleted).ToList();
        return Task.FromResult<IReadOnlyList<UserAssignment>>(items);
    }

    /// <inheritdoc />
    public Task<bool> HasActiveOrFutureByPositionAsync(string tenantNId, string positionNId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var result = _items.Any(a => a.TenantNId == tenantNId
            && a.PositionNId == positionNId
            && a.State == AssignmentState.Enabled
            && !a.IsDeleted
            && (a.EffectiveTo is null || a.EffectiveTo > now));
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task AddAsync(UserAssignment assignment, CancellationToken cancellationToken)
    {
        var key = (assignment.TenantNId, assignment.NId);
        if (_items.Any(a => a.TenantNId == assignment.TenantNId && a.NId == assignment.NId))
        {
            throw new ConcurrencyException("任职业务标识已存在。");
        }

        _items.Add(assignment);
        _versions[key] = (assignment.OptimisticVersion, assignment.ConcurrencyVersion);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateAsync(
        UserAssignment assignment,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken)
    {
        var index = _items.FindIndex(a => a.TenantNId == assignment.TenantNId && a.NId == assignment.NId && !a.IsDeleted);
        if (index < 0)
        {
            throw new ConcurrencyException("任职不存在。");
        }

        var key = (assignment.TenantNId, assignment.NId);
        if (!_versions.TryGetValue(key, out var snapshot)
            || snapshot.Optimistic != expectedOptimisticVersion
            || snapshot.Concurrency != expectedConcurrencyVersion)
        {
            throw new ConcurrencyException("双版本不匹配。");
        }

        _items[index] = assignment;
        _versions[key] = (assignment.OptimisticVersion, assignment.ConcurrencyVersion);
        return Task.CompletedTask;
    }
}

/// <summary>身份用户目录端口的进程内假实现(可配置条目与 fail-closed 不可用)。</summary>
internal sealed class FakeIdentityUserDirectory : IIdentityUserDirectory
{
    private readonly Dictionary<string, IdentityUserDirectoryEntryV1> _entries = new(StringComparer.Ordinal);
    private bool _unavailable;

    /// <summary>登记目录条目。</summary>
    public FakeIdentityUserDirectory WithEntry(string userNId, string name)
    {
        _entries[userNId] = new IdentityUserDirectoryEntryV1
        {
            UserNId = userNId,
            LoginName = userNId,
            Name = name,
            Status = "Active",
        };
        return this;
    }

    /// <summary>模拟目录不可用(实现抛 503)。</summary>
    public FakeIdentityUserDirectory Unavailable()
    {
        _unavailable = true;
        return this;
    }

    /// <inheritdoc />
    public Task<IdentityUserDirectoryEntryV1?> GetAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
    {
        if (_unavailable)
        {
            throw new IdentityDirectoryUnavailableException("身份用户目录暂不可用。");
        }

        return Task.FromResult(_entries.TryGetValue(userNId, out var entry) ? entry : null);
    }
}

/// <summary>按用户 advisory lock 端口的进程内假实现(no-op 句柄)。</summary>
internal sealed class FakeUserAssignmentAdvisoryLock : IUserAssignmentAdvisoryLock
{
    /// <inheritdoc />
    public Task<IUserAssignmentLockHandle> AcquireAsync(string tenantNId, string userNId, CancellationToken cancellationToken) =>
        Task.FromResult<IUserAssignmentLockHandle>(new NoopLockHandle());
}

/// <summary>no-op 锁句柄:提交/释放均无害。</summary>
internal sealed class NoopLockHandle : IUserAssignmentLockHandle
{
    /// <inheritdoc />
    public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>本地审计命令的进程内记录实现(供断言动作/对象/角色)。</summary>
internal sealed class RecordingLocalAuditCommand : ILocalAuditCommand
{
    /// <summary>已记录审计条目。</summary>
    public List<LocalAuditEntry> Entries { get; } = [];

    /// <inheritdoc />
    public Task RecordAsync(LocalAuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}
