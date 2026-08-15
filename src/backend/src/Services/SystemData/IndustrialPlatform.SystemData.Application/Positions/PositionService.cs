using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Application.Administration;
using IndustrialPlatform.SystemData.Application.Assignments;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Application.Organizations;
using IndustrialPlatform.SystemData.Contracts.Administration;
using IndustrialPlatform.SystemData.Domain.Organizations;
using IndustrialPlatform.SystemData.Domain.Positions;

namespace IndustrialPlatform.SystemData.Application.Positions;

/// <summary>
/// 岗位管理用例(TASK-SD-006):创建/查询/改名/停用。
/// 错误映射:组织缺失 404;组织内名称冲突 409(SD_CONCURRENCY_CONFLICT);
/// 停用有当前/未来任职 → SD_POSITION_HAS_ACTIVE_ASSIGNMENTS(409);
/// 领域不变量兜底 400/409。
/// 响应 <see cref="PositionV1"/> 经组织存储丰富化 OrganizationName。
/// </summary>
public sealed class PositionService : IPositionService
{
    private readonly IPositionStore _store;
    private readonly IAdministrativeOrganizationStore _organizationStore;
    private readonly IUserAssignmentStore _assignmentStore;
    private readonly ILocalAuditCommand _audit;

    /// <summary>初始化岗位管理用例。</summary>
    public PositionService(
        IPositionStore store,
        IAdministrativeOrganizationStore organizationStore,
        IUserAssignmentStore assignmentStore,
        ILocalAuditCommand audit)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(organizationStore);
        ArgumentNullException.ThrowIfNull(assignmentStore);
        ArgumentNullException.ThrowIfNull(audit);
        _store = store;
        _organizationStore = organizationStore;
        _assignmentStore = assignmentStore;
        _audit = audit;
    }

    /// <inheritdoc />
    public async Task<PositionV1> GetAsync(string tenantNId, string positionNId, CancellationToken cancellationToken)
    {
        var position = await LoadAsync(tenantNId, positionNId, cancellationToken);
        var organizationName = await OrganizationNameAsync(tenantNId, position.OrganizationNId, cancellationToken);
        return ToV1(position, organizationName);
    }

    /// <inheritdoc />
    public async Task<AdministrationPageResult<PositionV1>> ListAsync(
        string tenantNId,
        string? organizationNId,
        string? status,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var filter = new PositionListFilter(
            tenantNId,
            AdministrationInput.TrimOrNull(organizationNId),
            AdministrationInput.ParseOptionalPositionStatus(status),
            pageIndex,
            pageSize);
        var page = await _store.QueryPositionsAsync(filter, cancellationToken);

        var organizationNames = await LoadOrganizationNamesAsync(tenantNId, page.Items, cancellationToken);
        var items = page.Items
            .Select(p => ToV1(p, organizationNames.TryGetValue(p.OrganizationNId, out var name) ? name : string.Empty))
            .ToList();
        return new AdministrationPageResult<PositionV1>(items, page.Total, pageIndex, pageSize);
    }

    /// <inheritdoc />
    public async Task<PositionV1> CreateAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        CreatePositionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var nId = AdministrationInput.Require(request.NId, "岗位业务标识不能为空。");
        var organizationNId = AdministrationInput.Require(request.OrganizationNId, "岗位所属组织标识不能为空。");
        var name = AdministrationInput.Require(request.Name, "岗位名称不能为空。");
        var description = AdministrationInput.TrimOrNull(request.Description);
        var displayOrder = AdministrationInput.RequireNonNegative(request.DisplayOrder);

        try
        {
            var organization = await LoadOrganizationAsync(tenantNId, organizationNId, cancellationToken);
            await EnsureNameAvailableAsync(tenantNId, organizationNId, name, null, cancellationToken);

            var position = Position.Create(
                tenantNId,
                nId,
                organization.TenantNId,
                organization.NId,
                organization.Id,
                organization.IsDeleted,
                organization.Status == OrganizationStatus.Active,
                name,
                description,
                displayOrder);
            position.ClearDomainEvents();
            await AdministrationWriteGuard.ExecuteAsync(() => _store.AddAsync(position, cancellationToken));
            await RecordAuditAsync(tenantNId, actorUserNId, traceId, "position.create", "Position", position.NId, $"组织={organizationNId}", null, ToSummary(position));
            return ToV1(position, organization.Name);
        }
        catch (ValidationException ex)
        {
            throw new AdministrationValidationFailedException(ex.Message);
        }
        catch (BusinessException ex)
        {
            throw new AdministrationConcurrencyConflictException(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<PositionV1> UpdateAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        string positionNId,
        UpdatePositionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var position = await LoadAsync(tenantNId, positionNId, cancellationToken);
        var expectedOptimisticVersion = AdministrationInput.RequireNonNegativeVersion(request.ExpectedOptimisticVersion, "乐观版本");
        var expectedConcurrencyVersion = AdministrationInput.RequireConcurrencyVersion(request.ExpectedConcurrencyVersion, "并发版本");
        var before = ToSummary(position);

        try
        {
            var name = AdministrationInput.Require(request.Name, "岗位名称不能为空。");
            await EnsureNameAvailableAsync(tenantNId, position.OrganizationNId, name, position.NId, cancellationToken);
            var description = AdministrationInput.TrimOrNull(request.Description);
            var displayOrder = AdministrationInput.RequireNonNegative(request.DisplayOrder);

            position.Rename(name);
            position.ChangeDescription(description);
            position.ChangeDisplayOrder(displayOrder);
            position.ClearDomainEvents();
            await AdministrationWriteGuard.ExecuteAsync(() =>
                _store.UpdateAsync(position, expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken));
        }
        catch (ValidationException ex)
        {
            throw new AdministrationValidationFailedException(ex.Message);
        }
        catch (BusinessException ex)
        {
            throw new AdministrationConcurrencyConflictException(ex.Message);
        }

        var organizationName = await OrganizationNameAsync(tenantNId, position.OrganizationNId, cancellationToken);
        await RecordAuditAsync(tenantNId, actorUserNId, traceId, "position.update", "Position", position.NId, null, before, ToSummary(position));
        return ToV1(position, organizationName);
    }

    /// <inheritdoc />
    public async Task<PositionV1> SetStatusAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        string positionNId,
        SetPositionStatusRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var position = await LoadAsync(tenantNId, positionNId, cancellationToken);
        var expectedOptimisticVersion = position.OptimisticVersion;
        var expectedConcurrencyVersion = position.ConcurrencyVersion;
        var before = ToSummary(position);
        var status = AdministrationInput.ParseEnum<PositionStatus>(request.Status, nameof(PositionStatus));

        try
        {
            if (status == PositionStatus.Inactive)
            {
                var hasActiveOrFutureAssignments = await _assignmentStore.HasActiveOrFutureByPositionAsync(
                    tenantNId, positionNId, DateTimeOffset.UtcNow, cancellationToken);
                if (hasActiveOrFutureAssignments)
                {
                    throw new PositionHasActiveAssignmentsException();
                }

                position.Deactivate(hasActiveOrFutureAssignments);
            }
            else
            {
                var organization = await LoadOrganizationAsync(tenantNId, position.OrganizationNId, cancellationToken);
                position.Activate(organization.Status == OrganizationStatus.Active);
            }

            position.ClearDomainEvents();
            await AdministrationWriteGuard.ExecuteAsync(() =>
                _store.UpdateAsync(position, expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken));
        }
        catch (ValidationException ex)
        {
            throw new AdministrationValidationFailedException(ex.Message);
        }
        catch (BusinessException ex)
        {
            throw new AdministrationConcurrencyConflictException(ex.Message);
        }

        var action = status == PositionStatus.Inactive ? "position.deactivate" : "position.activate";
        var organizationName = await OrganizationNameAsync(tenantNId, position.OrganizationNId, cancellationToken);
        await RecordAuditAsync(tenantNId, actorUserNId, traceId, action, "Position", position.NId, request.Reason, before, ToSummary(position));
        return ToV1(position, organizationName);
    }

    // ===== 私有辅助 =====

    private async Task<Position> LoadAsync(string tenantNId, string positionNId, CancellationToken cancellationToken)
    {
        var position = await _store.GetAsync(tenantNId, positionNId, cancellationToken)
            ?? throw new AdministrationNotFoundException();
        return position;
    }

    private async Task<AdministrativeOrganization> LoadOrganizationAsync(
        string tenantNId,
        string organizationNId,
        CancellationToken cancellationToken)
    {
        var organization = await _organizationStore.GetAsync(tenantNId, organizationNId, cancellationToken)
            ?? throw new AdministrationNotFoundException();
        return organization;
    }

    private async Task EnsureNameAvailableAsync(
        string tenantNId,
        string organizationNId,
        string name,
        string? excludePositionNId,
        CancellationToken cancellationToken)
    {
        var available = await _store.NameAvailableAsync(tenantNId, organizationNId, name, excludePositionNId, cancellationToken);
        if (!available)
        {
            throw new AdministrationConcurrencyConflictException("组织内岗位名称已存在。");
        }
    }

    /// <summary>读取组织名称(响应丰富化);缺失返回空串。</summary>
    private async Task<string> OrganizationNameAsync(string tenantNId, string organizationNId, CancellationToken cancellationToken)
    {
        var organization = await _organizationStore.GetAsync(tenantNId, organizationNId, cancellationToken);
        return organization?.Name ?? string.Empty;
    }

    /// <summary>批量读取岗位所属组织名称(按 NId 去重一次查询)。</summary>
    private async Task<IReadOnlyDictionary<string, string>> LoadOrganizationNamesAsync(
        string tenantNId,
        IReadOnlyList<Position> positions,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var all = await _organizationStore.GetAllAsync(tenantNId, cancellationToken);
        foreach (var organization in all)
        {
            result[organization.NId] = organization.Name;
        }

        return result;
    }

    private async Task RecordAuditAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        string action,
        string objectType,
        string objectNId,
        string? reason,
        string? before,
        string? after)
    {
        await _audit.RecordAsync(
            new LocalAuditEntry(tenantNId, actorUserNId, action, objectType, objectNId, reason, before, after, traceId),
            CancellationToken.None);
    }

    /// <summary>审计摘要:名称@组织#状态/序。</summary>
    private static string ToSummary(Position position) =>
        $"{position.Name}@{position.OrganizationNId}#{position.Status}/序={position.DisplayOrder}";

    private static PositionV1 ToV1(Position position, string organizationName) => new()
    {
        TenantNId = position.TenantNId,
        NId = position.NId,
        OrganizationNId = position.OrganizationNId,
        OrganizationName = organizationName,
        Name = position.Name,
        Description = position.Description ?? string.Empty,
        Status = position.Status.ToString(),
        DisplayOrder = position.DisplayOrder,
        OptimisticVersion = position.OptimisticVersion,
        ConcurrencyVersion = position.ConcurrencyVersion,
    };
}
