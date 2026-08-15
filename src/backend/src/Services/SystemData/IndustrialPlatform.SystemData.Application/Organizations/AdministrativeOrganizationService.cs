using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Application.Administration;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Contracts.Administration;
using IndustrialPlatform.SystemData.Domain.Organizations;

namespace IndustrialPlatform.SystemData.Application.Organizations;

/// <summary>
/// 行政组织管理用例(TASK-SD-006):创建/查询/改名/停用/移动预览与提交。
/// 应用层负责把领域工厂/方法的领域异常映射为 §9.9 结构化错误码:
///  - 类型矩阵违规 → SD_ORG_PARENT_TYPE_INVALID(400);
///  - 祖先循环 → SD_ORG_CYCLE(409);
///  - 停用依赖 → SD_ORG_HAS_ACTIVE_DEPENDENCIES(409);
///  - 双版本/revision 冲突 → SD_CONCURRENCY_CONFLICT(409)。
/// 名称唯一冲突按存储唯一索引语义映射为 409;跨租户对象经租户过滤查询返回 404。
/// </summary>
public sealed class AdministrativeOrganizationService : IAdministrativeOrganizationService
{
    private readonly IAdministrativeOrganizationStore _store;
    private readonly ILocalAuditCommand _audit;

    /// <summary>初始化行政组织管理用例。</summary>
    public AdministrativeOrganizationService(
        IAdministrativeOrganizationStore store,
        ILocalAuditCommand audit)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(audit);
        _store = store;
        _audit = audit;
    }

    /// <inheritdoc />
    public async Task<OrganizationDetailV1> GetAsync(
        string tenantNId,
        string organizationNId,
        CancellationToken cancellationToken)
    {
        var organization = await LoadAsync(tenantNId, organizationNId, cancellationToken);
        return ToDetailV1(organization);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrganizationNodeV1>> GetTreeAsync(
        string tenantNId,
        string? status,
        CancellationToken cancellationToken)
    {
        var all = await _store.GetAllAsync(tenantNId, cancellationToken);
        var statusFilter = AdministrationInput.ParseOptionalOrganizationStatus(status);

        var nodes = all.ToDictionary(o => o.NId, ToNode);
        // 状态过滤后的作用域:被剪枝的父节点不参与接线,其匹配子节点提升为森林根。
        var scope = statusFilter is { } activeStatus
            ? nodes.Values.Where(n => n.Status == activeStatus.ToString())
                .ToDictionary(n => n.NId, StringComparer.Ordinal)
            : nodes;

        foreach (var node in scope.Values)
        {
            if (node.ParentOrganizationNId is { } parentNId && scope.TryGetValue(parentNId, out var parent))
            {
                parent.Children.Add(node);
            }
        }

        // 森林根:父为空或父被状态过滤移除(过滤时被剪枝节点成为根)。
        return scope.Values
            .Where(n => n.ParentOrganizationNId is null
                || !scope.ContainsKey(n.ParentOrganizationNId))
            .OrderBy(n => n.DisplayOrder)
            .ThenBy(n => n.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<OrganizationDetailV1> CreateAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        CreateOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var nId = AdministrationInput.Require(request.NId, "组织业务标识不能为空。");
        var name = AdministrationInput.Require(request.Name, "组织名称不能为空。");
        var type = AdministrationInput.ParseOrganizationType(request.Type);
        var displayOrder = AdministrationInput.RequireNonNegative(request.DisplayOrder);

        try
        {
            AdministrativeOrganization organization;
            if (type == AdministrativeOrganizationType.Company)
            {
                if (!string.IsNullOrWhiteSpace(request.ParentOrganizationNId))
                {
                    throw new AdministrationValidationFailedException("公司只能作为根组织创建,不能指定父组织。");
                }

                await EnsureNameAvailableAsync(tenantNId, null, name, null, cancellationToken);
                organization = AdministrativeOrganization.CreateRootCompany(tenantNId, nId, name, displayOrder);
            }
            else
            {
                var parentNId = AdministrationInput.Require(request.ParentOrganizationNId, "非公司组织创建时必须指定父组织。");
                var parent = await LoadAsync(tenantNId, parentNId, cancellationToken);
                if (!AdministrativeOrganization.CanHaveParent(type, parent.Type))
                {
                    throw new OrganizationParentTypeInvalidException(
                        $"组织类型 {type} 不能作为 {parent.Type} 的子组织。");
                }

                if (parent.Status != OrganizationStatus.Active)
                {
                    throw new AdministrationValidationFailedException("停用组织不能新增子组织。");
                }

                await EnsureNameAvailableAsync(tenantNId, parentNId, name, null, cancellationToken);
                organization = AdministrativeOrganization.CreateChild(
                    tenantNId,
                    nId,
                    name,
                    type,
                    parent.TenantNId,
                    parent.NId,
                    parent.Id,
                    parent.IsDeleted,
                    parent.Status == OrganizationStatus.Active,
                    parent.Type,
                    displayOrder);
            }

            organization.ClearDomainEvents();
            await AdministrationWriteGuard.ExecuteAsync(() => _store.AddAsync(organization, cancellationToken));
            await RecordAuditAsync(tenantNId, actorUserNId, traceId, "organization.create", "Organization", organization.NId,
                request.ParentOrganizationNId is { } parentAuditNId ? $"父={parentAuditNId}" : "根公司", null, ToSummary(organization));
            return ToDetailV1(organization);
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
    public async Task<OrganizationDetailV1> UpdateAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        string organizationNId,
        UpdateOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var organization = await LoadAsync(tenantNId, organizationNId, cancellationToken);
        var expectedOptimisticVersion = AdministrationInput.RequireNonNegativeVersion(request.ExpectedOptimisticVersion, "乐观版本");
        var expectedConcurrencyVersion = AdministrationInput.RequireConcurrencyVersion(request.ExpectedConcurrencyVersion, "并发版本");
        var before = ToSummary(organization);

        try
        {
            var name = AdministrationInput.Require(request.Name, "组织名称不能为空。");
            await EnsureNameAvailableAsync(tenantNId, organization.ParentOrganizationNId, name, organization.NId, cancellationToken);
            var displayOrder = AdministrationInput.RequireNonNegative(request.DisplayOrder);

            organization.Rename(name);
            organization.ChangeDisplayOrder(displayOrder);
            organization.ClearDomainEvents();
            await AdministrationWriteGuard.ExecuteAsync(() =>
                _store.UpdateAsync(organization, expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken));
        }
        catch (ValidationException ex)
        {
            throw new AdministrationValidationFailedException(ex.Message);
        }
        catch (BusinessException ex)
        {
            throw new AdministrationConcurrencyConflictException(ex.Message);
        }

        await RecordAuditAsync(tenantNId, actorUserNId, traceId, "organization.update", "Organization", organization.NId, null, before, ToSummary(organization));
        return ToDetailV1(organization);
    }

    /// <inheritdoc />
    public async Task<OrganizationMovePreviewV1> PreviewMoveAsync(
        string tenantNId,
        string organizationNId,
        MoveOrganizationPreviewRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var organization = await LoadAsync(tenantNId, organizationNId, cancellationToken);
        var targetParentNId = AdministrationInput.Require(request.TargetParentOrganizationNId, "移动必须指定目标父组织。");
        var targetParent = await LoadAsync(tenantNId, targetParentNId, cancellationToken);
        if (!AdministrativeOrganization.CanHaveParent(organization.Type, targetParent.Type))
        {
            throw new OrganizationParentTypeInvalidException(
                $"组织类型 {organization.Type} 不能移动到 {targetParent.Type} 父组织之下。");
        }

        var subtreeNIds = await _store.GetDescendantNIdsAsync(tenantNId, organizationNId, cancellationToken);
        var counts = await _store.GetSubtreeCountsAsync(tenantNId, organizationNId, DateTimeOffset.UtcNow, cancellationToken);

        try
        {
            var preview = organization.PreviewMove(
                targetParent.NId,
                targetParent.Id,
                targetParent.IsDeleted,
                targetParent.TenantNId,
                targetParent.Type,
                targetParent.Status == OrganizationStatus.Active,
                subtreeNIds,
                counts.OrganizationCount,
                counts.PositionCount,
                counts.AssignmentCount);
            return new OrganizationMovePreviewV1
            {
                NId = preview.NId,
                OrganizationRevision = preview.OrganizationRevision,
                SubtreeOrganizationCount = preview.SubtreeOrganizationCount,
                SubtreePositionCount = preview.SubtreePositionCount,
                SubtreeAssignmentCount = preview.SubtreeAssignmentCount,
                AffectedCount = preview.AffectedCount,
                PreviewedOn = preview.PreviewedOn,
                ExpectedOptimisticVersion = organization.OptimisticVersion,
                ExpectedConcurrencyVersion = organization.ConcurrencyVersion,
            };
        }
        catch (ValidationException ex)
        {
            throw new AdministrationValidationFailedException(ex.Message);
        }
        catch (BusinessException ex)
        {
            throw MapMoveBusinessException(ex, subtreeNIds, targetParentNId);
        }
    }

    /// <inheritdoc />
    public async Task<OrganizationDetailV1> MoveAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        string organizationNId,
        MoveOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var organization = await LoadAsync(tenantNId, organizationNId, cancellationToken);
        var targetParentNId = AdministrationInput.Require(request.TargetParentOrganizationNId, "移动必须指定目标父组织。");
        var targetParent = await LoadAsync(tenantNId, targetParentNId, cancellationToken);
        if (!AdministrativeOrganization.CanHaveParent(organization.Type, targetParent.Type))
        {
            throw new OrganizationParentTypeInvalidException(
                $"组织类型 {organization.Type} 不能移动到 {targetParent.Type} 父组织之下。");
        }

        var expectedOptimisticVersion = AdministrationInput.RequireNonNegativeVersion(request.ExpectedOptimisticVersion, "乐观版本");
        var expectedConcurrencyVersion = AdministrationInput.RequireConcurrencyVersion(request.ExpectedConcurrencyVersion, "并发版本");
        var previewRevision = AdministrationInput.RequireNonNegativeVersion(request.PreviewOrganizationRevision, "移动预览修订号");
        var subtreeNIds = await _store.GetDescendantNIdsAsync(tenantNId, organizationNId, cancellationToken);
        var before = ToSummary(organization);

        var preview = new OrganizationMovePreview(
            organization.NId,
            previewRevision,
            0, 0, 0, 0,
            DateTimeOffset.UtcNow);

        try
        {
            organization.Move(
                preview,
                targetParent.NId,
                targetParent.Id,
                targetParent.IsDeleted,
                targetParent.TenantNId,
                targetParent.Type,
                targetParent.Status == OrganizationStatus.Active,
                subtreeNIds);
            organization.ClearDomainEvents();
            await AdministrationWriteGuard.ExecuteAsync(() =>
                _store.UpdateAsync(organization, expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken));
        }
        catch (ValidationException ex)
        {
            throw new AdministrationValidationFailedException(ex.Message);
        }
        catch (BusinessException ex)
        {
            throw MapMoveBusinessException(ex, subtreeNIds, targetParentNId);
        }

        await RecordAuditAsync(tenantNId, actorUserNId, traceId, "organization.move", "Organization", organization.NId, request.Reason, before, ToSummary(organization));
        return ToDetailV1(organization);
    }

    /// <inheritdoc />
    public async Task<OrganizationDetailV1> SetStatusAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        string organizationNId,
        SetOrganizationStatusRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var organization = await LoadAsync(tenantNId, organizationNId, cancellationToken);
        var expectedOptimisticVersion = organization.OptimisticVersion;
        var expectedConcurrencyVersion = organization.ConcurrencyVersion;
        var before = ToSummary(organization);
        var status = AdministrationInput.ParseEnum<OrganizationStatus>(request.Status, nameof(OrganizationStatus));

        try
        {
            if (status == OrganizationStatus.Inactive)
            {
                var counts = await _store.GetDependencyCountsAsync(tenantNId, organizationNId, DateTimeOffset.UtcNow, cancellationToken);
                if (counts.ActiveChildCount > 0 || counts.ActivePositionCount > 0 || counts.ActiveOrFutureAssignmentCount > 0)
                {
                    throw new OrganizationHasActiveDependenciesException(
                        counts.ActiveChildCount, counts.ActivePositionCount, counts.ActiveOrFutureAssignmentCount);
                }

                organization.Deactivate(counts.ActiveChildCount, counts.ActivePositionCount, counts.ActiveOrFutureAssignmentCount);
            }
            else
            {
                if (organization.ParentOrganizationId is { } parentId)
                {
                    var parent = await _store.GetAsync(tenantNId, organization.ParentOrganizationNId!, cancellationToken)
                        ?? throw new AdministrationNotFoundException();
                    organization.Activate(parent.Status == OrganizationStatus.Active);
                }
                else
                {
                    organization.Activate(parentIsActive: true);
                }
            }

            organization.ClearDomainEvents();
            await AdministrationWriteGuard.ExecuteAsync(() =>
                _store.UpdateAsync(organization, expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken));
        }
        catch (ValidationException ex)
        {
            throw new AdministrationValidationFailedException(ex.Message);
        }
        catch (BusinessException ex)
        {
            throw new AdministrationConcurrencyConflictException(ex.Message);
        }

        var action = status == OrganizationStatus.Inactive ? "organization.deactivate" : "organization.activate";
        await RecordAuditAsync(tenantNId, actorUserNId, traceId, action, "Organization", organization.NId, request.Reason, before, ToSummary(organization));
        return ToDetailV1(organization);
    }

    // ===== 私有辅助 =====

    /// <summary>按 (租户, NId) 加载组织;跨租户或不存在统一 404。</summary>
    private async Task<AdministrativeOrganization> LoadAsync(string tenantNId, string organizationNId, CancellationToken cancellationToken)
    {
        var organization = await _store.GetAsync(tenantNId, organizationNId, cancellationToken)
            ?? throw new AdministrationNotFoundException();
        return organization;
    }

    private async Task EnsureNameAvailableAsync(
        string tenantNId,
        string? parentOrganizationNId,
        string name,
        string? excludeOrganizationNId,
        CancellationToken cancellationToken)
    {
        var available = await _store.NameAvailableAsync(tenantNId, parentOrganizationNId, name, excludeOrganizationNId, cancellationToken);
        if (!available)
        {
            throw new AdministrationConcurrencyConflictException("同级组织名称已存在。");
        }
    }

    /// <summary>将移动相关的领域异常映射为结构化错误码(类型矩阵 → 400,循环 → 409)。</summary>
    private static AdministrationException MapMoveBusinessException(
        BusinessException ex,
        IReadOnlyCollection<string> subtreeNIds,
        string targetParentNId)
    {
        if (subtreeNIds.Contains(targetParentNId))
        {
            return new OrganizationCycleException();
        }

        return new AdministrationConcurrencyConflictException(ex.Message);
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

    /// <summary>审计摘要:名称@类型#状态/父/顺序。</summary>
    private static string ToSummary(AdministrativeOrganization organization) =>
        $"{organization.Name}@{organization.Type}#{organization.Status}/父={organization.ParentOrganizationNId ?? "-"}/序={organization.DisplayOrder}";

    private static OrganizationNodeV1 ToNode(AdministrativeOrganization organization) => new()
    {
        TenantNId = organization.TenantNId,
        NId = organization.NId,
        Name = organization.Name,
        Type = organization.Type.ToString(),
        Status = organization.Status.ToString(),
        ParentOrganizationNId = organization.ParentOrganizationNId,
        DisplayOrder = organization.DisplayOrder,
    };

    private static OrganizationDetailV1 ToDetailV1(AdministrativeOrganization organization) => new()
    {
        TenantNId = organization.TenantNId,
        NId = organization.NId,
        Name = organization.Name,
        Type = organization.Type.ToString(),
        Status = organization.Status.ToString(),
        ParentOrganizationNId = organization.ParentOrganizationNId,
        DisplayOrder = organization.DisplayOrder,
        OrganizationRevision = organization.OrganizationRevision,
        OptimisticVersion = organization.OptimisticVersion,
        ConcurrencyVersion = organization.ConcurrencyVersion,
    };
}
