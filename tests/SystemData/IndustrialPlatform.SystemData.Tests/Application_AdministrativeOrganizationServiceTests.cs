using IndustrialPlatform.SystemData.Application.Administration;
using IndustrialPlatform.SystemData.Application.Organizations;
using IndustrialPlatform.SystemData.Contracts.Administration;

namespace IndustrialPlatform.SystemData.Application.Tests;

/// <summary>
/// 行政组织管理用例测试(TASK-SD-006):创建/查询/树/改名/停用/移动预览与提交。
/// 错误码对齐 §9.9:类型矩阵 400 SD_ORG_PARENT_TYPE_INVALID、祖先循环 409 SD_ORG_CYCLE、
/// 停用依赖 409 SD_ORG_HAS_ACTIVE_DEPENDENCIES、双版本/revision 冲突 409 SD_CONCURRENCY_CONFLICT、
/// 跨租户统一 404 SD_NOT_FOUND。
/// </summary>
public sealed class AdministrativeOrganizationServiceTests
{
    private const string Tenant = "tenant-001";
    private const string Actor = "user-actor";
    private const string TraceId = "trace-001";

    // =====================================================================
    // 创建
    // =====================================================================

    [Fact]
    public async Task CreateAsync_RootCompany_ReturnsDetailWithVersions()
    {
        var store = new FakeAdministrativeOrganizationStore();
        var service = CreateService(store);

        var result = await service.CreateAsync(Tenant, Actor, TraceId, CreateCompanyRequest("company-a", "A 公司", 1), CancellationToken.None);

        Assert.Equal(Tenant, result.TenantNId);
        Assert.Equal("company-a", result.NId);
        Assert.Equal("A 公司", result.Name);
        Assert.Equal("Company", result.Type);
        Assert.Equal("Active", result.Status);
        Assert.Null(result.ParentOrganizationNId);
        Assert.Equal(1, result.DisplayOrder);
        Assert.Equal(1, result.OrganizationRevision);
        Assert.Equal(0, result.OptimisticVersion);
        Assert.NotEqual(Guid.Empty, result.ConcurrencyVersion);
    }

    [Fact]
    public async Task CreateAsync_Department_SetsParentAndPassesTypeMatrix()
    {
        var store = new FakeAdministrativeOrganizationStore();
        var service = CreateService(store);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateCompanyRequest("company-a", "A 公司"), CancellationToken.None);

        var child = await service.CreateAsync(
            Tenant, Actor, TraceId, CreateChildRequest("dept-a", "研发部", "Department", "company-a"), CancellationToken.None);

        Assert.Equal("dept-a", child.NId);
        Assert.Equal("Department", child.Type);
        Assert.Equal("company-a", child.ParentOrganizationNId);
    }

    [Fact]
    public async Task CreateAsync_CompanyWithParent_ThrowsValidationFailed()
    {
        var store = new FakeAdministrativeOrganizationStore();
        var service = CreateService(store);

        var ex = await Assert.ThrowsAsync<AdministrationValidationFailedException>(() =>
            service.CreateAsync(Tenant, Actor, TraceId, new CreateOrganizationRequest
            {
                NId = "company-b",
                Name = "B 公司",
                Type = "Company",
                ParentOrganizationNId = "company-a",
                DisplayOrder = 1,
            }, CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("SD_VALIDATION_FAILED", ex.Code);
    }

    [Fact]
    public async Task CreateAsync_ChildWithoutParent_ThrowsValidationFailed()
    {
        var store = new FakeAdministrativeOrganizationStore();
        var service = CreateService(store);

        var ex = await Assert.ThrowsAsync<AdministrationValidationFailedException>(() =>
            service.CreateAsync(Tenant, Actor, TraceId, new CreateOrganizationRequest
            {
                NId = "dept-a",
                Name = "研发部",
                Type = "Department",
                ParentOrganizationNId = null,
                DisplayOrder = 1,
            }, CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("SD_VALIDATION_FAILED", ex.Code);
    }

    [Fact]
    public async Task CreateAsync_TeamUnderCompany_ThrowsParentTypeInvalid()
    {
        var store = new FakeAdministrativeOrganizationStore();
        var service = CreateService(store);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateCompanyRequest("company-a", "A 公司"), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<OrganizationParentTypeInvalidException>(() =>
            service.CreateAsync(Tenant, Actor, TraceId, CreateChildRequest("team-a", "平台组", "Team", "company-a"), CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("SD_ORG_PARENT_TYPE_INVALID", ex.Code);
    }

    [Fact]
    public async Task CreateAsync_RecordsAuditEntry()
    {
        var store = new FakeAdministrativeOrganizationStore();
        var audit = new RecordingLocalAuditCommand();
        var service = new AdministrativeOrganizationService(store, audit);

        await service.CreateAsync(Tenant, Actor, TraceId, CreateCompanyRequest("company-a", "A 公司"), CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("organization.create", entry.Action);
        Assert.Equal(Tenant, entry.TenantNId);
        Assert.Equal(Actor, entry.ActorUserNId);
        Assert.Equal("Organization", entry.ObjectType);
        Assert.Equal("company-a", entry.ObjectNId);
        Assert.Equal(TraceId, entry.TraceId);
    }

    // =====================================================================
    // 查询
    // =====================================================================

    [Fact]
    public async Task GetAsync_Unknown_ThrowsNotFound()
    {
        var store = new FakeAdministrativeOrganizationStore();
        var service = CreateService(store);

        var ex = await Assert.ThrowsAsync<AdministrationNotFoundException>(() => service.GetAsync(Tenant, "missing", CancellationToken.None));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("SD_NOT_FOUND", ex.Code);
    }

    [Fact]
    public async Task GetAsync_CrossTenant_ThrowsNotFound()
    {
        var store = new FakeAdministrativeOrganizationStore();
        var service = CreateService(store);
        var created = await service.CreateAsync(Tenant, Actor, TraceId, CreateCompanyRequest("company-a", "A 公司"), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AdministrationNotFoundException>(() =>
            service.GetAsync("tenant-002", created.NId, CancellationToken.None));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("SD_NOT_FOUND", ex.Code);
    }

    [Fact]
    public async Task GetTreeAsync_ReturnsForestOrdered()
    {
        var store = new FakeAdministrativeOrganizationStore();
        var service = CreateService(store);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateCompanyRequest("company-b", "B 公司", 2), CancellationToken.None);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateCompanyRequest("company-a", "A 公司", 1), CancellationToken.None);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateChildRequest("dept-a", "研发部", "Department", "company-a"), CancellationToken.None);

        var tree = await service.GetTreeAsync(Tenant, null, CancellationToken.None);

        Assert.Equal(2, tree.Count);
        Assert.Equal("company-a", tree[0].NId);
        Assert.Equal("company-b", tree[1].NId);
        Assert.Single(tree[0].Children);
        Assert.Equal("dept-a", tree[0].Children[0].NId);
        Assert.Empty(tree[0].Children[0].Children);
    }

    [Fact]
    public async Task GetTreeAsync_StatusFilter_PromotesMatchingChildrenToRoots()
    {
        var store = new FakeAdministrativeOrganizationStore();
        var service = CreateService(store);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateCompanyRequest("company-a", "A 公司"), CancellationToken.None);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateChildRequest("dept-a", "研发部", "Department", "company-a"), CancellationToken.None);
        await service.SetStatusAsync(Tenant, Actor, TraceId, "dept-a", new SetOrganizationStatusRequest { Status = "Inactive" }, CancellationToken.None);

        var tree = await service.GetTreeAsync(Tenant, "Inactive", CancellationToken.None);

        var dept = Assert.Single(tree);
        Assert.Equal("dept-a", dept.NId);
        Assert.Equal("Inactive", dept.Status);
    }

    // =====================================================================
    // 修改
    // =====================================================================

    [Fact]
    public async Task UpdateAsync_RenamesAndAdvancesVersions()
    {
        var store = new FakeAdministrativeOrganizationStore();
        var service = CreateService(store);
        var created = await service.CreateAsync(Tenant, Actor, TraceId, CreateCompanyRequest("company-a", "A 公司", 1), CancellationToken.None);

        var updated = await service.UpdateAsync(Tenant, Actor, TraceId, created.NId, new UpdateOrganizationRequest
        {
            Name = "A 公司(改)",
            DisplayOrder = 3,
            ExpectedOptimisticVersion = created.OptimisticVersion,
            ExpectedConcurrencyVersion = created.ConcurrencyVersion,
        }, CancellationToken.None);

        Assert.Equal("A 公司(改)", updated.Name);
        Assert.Equal(3, updated.DisplayOrder);
        Assert.Equal(2, updated.OptimisticVersion);
        Assert.NotEqual(created.ConcurrencyVersion, updated.ConcurrencyVersion);
    }

    [Fact]
    public async Task UpdateAsync_StaleVersions_ThrowsConcurrency()
    {
        var store = new FakeAdministrativeOrganizationStore();
        var service = CreateService(store);
        var created = await service.CreateAsync(Tenant, Actor, TraceId, CreateCompanyRequest("company-a", "A 公司"), CancellationToken.None);
        await service.UpdateAsync(Tenant, Actor, TraceId, created.NId, new UpdateOrganizationRequest
        {
            Name = "改名一次",
            DisplayOrder = 1,
            ExpectedOptimisticVersion = created.OptimisticVersion,
            ExpectedConcurrencyVersion = created.ConcurrencyVersion,
        }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AdministrationConcurrencyConflictException>(() =>
            service.UpdateAsync(Tenant, Actor, TraceId, created.NId, new UpdateOrganizationRequest
            {
                Name = "过期版本再改",
                DisplayOrder = 2,
                ExpectedOptimisticVersion = created.OptimisticVersion,
                ExpectedConcurrencyVersion = created.ConcurrencyVersion,
            }, CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("SD_CONCURRENCY_CONFLICT", ex.Code);
    }

    // =====================================================================
    // 移动预览与提交
    // =====================================================================

    [Fact]
    public async Task PreviewMoveAsync_ReturnsRevisionAndVersions()
    {
        var store = new FakeAdministrativeOrganizationStore();
        var service = CreateService(store);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateCompanyRequest("company-a", "A 公司"), CancellationToken.None);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateCompanyRequest("company-b", "B 公司"), CancellationToken.None);
        var dept = await service.CreateAsync(Tenant, Actor, TraceId, CreateChildRequest("dept-a", "研发部", "Department", "company-a"), CancellationToken.None);

        var preview = await service.PreviewMoveAsync(Tenant, dept.NId, new MoveOrganizationPreviewRequest
        {
            TargetParentOrganizationNId = "company-b",
        }, CancellationToken.None);

        Assert.Equal("dept-a", preview.NId);
        Assert.Equal(2, preview.OrganizationRevision);
        Assert.Equal(1, preview.SubtreeOrganizationCount);
        Assert.Equal(0, preview.SubtreePositionCount);
        Assert.Equal(0, preview.SubtreeAssignmentCount);
        Assert.Equal(1, preview.AffectedCount);
        Assert.Equal(dept.OptimisticVersion, preview.ExpectedOptimisticVersion);
        Assert.Equal(dept.ConcurrencyVersion, preview.ExpectedConcurrencyVersion);
    }

    [Fact]
    public async Task PreviewMoveAsync_TargetInSubtree_ThrowsCycle()
    {
        var store = new FakeAdministrativeOrganizationStore();
        var service = CreateService(store);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateCompanyRequest("company-a", "A 公司"), CancellationToken.None);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateChildRequest("dept-1", "研发部", "Department", "company-a"), CancellationToken.None);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateChildRequest("dept-2", "前端组", "Department", "dept-1"), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<OrganizationCycleException>(() =>
            service.PreviewMoveAsync(Tenant, "dept-1", new MoveOrganizationPreviewRequest
            {
                TargetParentOrganizationNId = "dept-2",
            }, CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("SD_ORG_CYCLE", ex.Code);
    }

    [Fact]
    public async Task PreviewMoveAsync_TypeMatrixViolation_ThrowsParentTypeInvalid()
    {
        var store = new FakeAdministrativeOrganizationStore();
        var service = CreateService(store);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateCompanyRequest("company-a", "A 公司"), CancellationToken.None);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateChildRequest("dept-a", "研发部", "Department", "company-a"), CancellationToken.None);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateChildRequest("team-a", "平台组", "Team", "dept-a"), CancellationToken.None);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateChildRequest("dept-b", "市场部", "Department", "company-a"), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<OrganizationParentTypeInvalidException>(() =>
            service.PreviewMoveAsync(Tenant, "dept-b", new MoveOrganizationPreviewRequest
            {
                TargetParentOrganizationNId = "team-a",
            }, CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("SD_ORG_PARENT_TYPE_INVALID", ex.Code);
    }

    [Fact]
    public async Task MoveAsync_CommitsMoveAndAdvancesRevision()
    {
        var store = new FakeAdministrativeOrganizationStore();
        var service = CreateService(store);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateCompanyRequest("company-a", "A 公司"), CancellationToken.None);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateCompanyRequest("company-b", "B 公司"), CancellationToken.None);
        var dept = await service.CreateAsync(Tenant, Actor, TraceId, CreateChildRequest("dept-a", "研发部", "Department", "company-a"), CancellationToken.None);
        var preview = await service.PreviewMoveAsync(Tenant, dept.NId, new MoveOrganizationPreviewRequest
        {
            TargetParentOrganizationNId = "company-b",
        }, CancellationToken.None);

        var moved = await service.MoveAsync(Tenant, Actor, TraceId, dept.NId, new MoveOrganizationRequest
        {
            TargetParentOrganizationNId = "company-b",
            PreviewOrganizationRevision = preview.OrganizationRevision,
            ExpectedOptimisticVersion = preview.ExpectedOptimisticVersion,
            ExpectedConcurrencyVersion = preview.ExpectedConcurrencyVersion,
        }, CancellationToken.None);

        Assert.Equal("company-b", moved.ParentOrganizationNId);
        Assert.Equal(preview.OrganizationRevision, moved.OrganizationRevision);
        Assert.Equal(1, moved.OptimisticVersion);
    }

    [Fact]
    public async Task MoveAsync_StalePreviewRevision_ThrowsConcurrency()
    {
        var store = new FakeAdministrativeOrganizationStore();
        var service = CreateService(store);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateCompanyRequest("company-a", "A 公司"), CancellationToken.None);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateCompanyRequest("company-b", "B 公司"), CancellationToken.None);
        var dept = await service.CreateAsync(Tenant, Actor, TraceId, CreateChildRequest("dept-a", "研发部", "Department", "company-a"), CancellationToken.None);
        var preview = await service.PreviewMoveAsync(Tenant, dept.NId, new MoveOrganizationPreviewRequest
        {
            TargetParentOrganizationNId = "company-b",
        }, CancellationToken.None);
        await service.MoveAsync(Tenant, Actor, TraceId, dept.NId, new MoveOrganizationRequest
        {
            TargetParentOrganizationNId = "company-b",
            PreviewOrganizationRevision = preview.OrganizationRevision,
            ExpectedOptimisticVersion = preview.ExpectedOptimisticVersion,
            ExpectedConcurrencyVersion = preview.ExpectedConcurrencyVersion,
        }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AdministrationConcurrencyConflictException>(() =>
            service.MoveAsync(Tenant, Actor, TraceId, dept.NId, new MoveOrganizationRequest
            {
                TargetParentOrganizationNId = "company-b",
                PreviewOrganizationRevision = preview.OrganizationRevision,
                ExpectedOptimisticVersion = preview.ExpectedOptimisticVersion,
                ExpectedConcurrencyVersion = preview.ExpectedConcurrencyVersion,
            }, CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("SD_CONCURRENCY_CONFLICT", ex.Code);
    }

    // =====================================================================
    // 状态
    // =====================================================================

    [Fact]
    public async Task SetStatusAsync_DeactivateWithDependencies_ThrowsHasActiveDependencies()
    {
        var store = new FakeAdministrativeOrganizationStore();
        store.DependencyCounts["company-a"] = new OrganizationDependencyCounts(1, 0, 0);
        var service = CreateService(store);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateCompanyRequest("company-a", "A 公司"), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<OrganizationHasActiveDependenciesException>(() =>
            service.SetStatusAsync(Tenant, Actor, TraceId, "company-a", new SetOrganizationStatusRequest { Status = "Inactive" }, CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("SD_ORG_HAS_ACTIVE_DEPENDENCIES", ex.Code);
    }

    [Fact]
    public async Task SetStatusAsync_DeactivateLeaf_Succeeds()
    {
        var store = new FakeAdministrativeOrganizationStore();
        var service = CreateService(store);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateCompanyRequest("company-a", "A 公司"), CancellationToken.None);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateChildRequest("dept-a", "研发部", "Department", "company-a"), CancellationToken.None);

        var result = await service.SetStatusAsync(Tenant, Actor, TraceId, "dept-a", new SetOrganizationStatusRequest { Status = "Inactive" }, CancellationToken.None);

        Assert.Equal("Inactive", result.Status);
    }

    [Fact]
    public async Task SetStatusAsync_Activate_Succeeds()
    {
        var store = new FakeAdministrativeOrganizationStore();
        var service = CreateService(store);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateCompanyRequest("company-a", "A 公司"), CancellationToken.None);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateChildRequest("dept-a", "研发部", "Department", "company-a"), CancellationToken.None);
        await service.SetStatusAsync(Tenant, Actor, TraceId, "dept-a", new SetOrganizationStatusRequest { Status = "Inactive" }, CancellationToken.None);

        var result = await service.SetStatusAsync(Tenant, Actor, TraceId, "dept-a", new SetOrganizationStatusRequest { Status = "Active" }, CancellationToken.None);

        Assert.Equal("Active", result.Status);
    }

    [Fact]
    public async Task SetStatusAsync_InvalidStatus_ThrowsValidation()
    {
        var store = new FakeAdministrativeOrganizationStore();
        var service = CreateService(store);
        await service.CreateAsync(Tenant, Actor, TraceId, CreateCompanyRequest("company-a", "A 公司"), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AdministrationValidationFailedException>(() =>
            service.SetStatusAsync(Tenant, Actor, TraceId, "company-a", new SetOrganizationStatusRequest { Status = "Banana" }, CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("SD_VALIDATION_FAILED", ex.Code);
    }

    // ===== 辅助 =====

    private static AdministrativeOrganizationService CreateService(FakeAdministrativeOrganizationStore store) =>
        new(store, new RecordingLocalAuditCommand());

    private static CreateOrganizationRequest CreateCompanyRequest(string nId, string name, int? displayOrder = 1) => new()
    {
        NId = nId,
        Name = name,
        Type = "Company",
        DisplayOrder = displayOrder,
    };

    private static CreateOrganizationRequest CreateChildRequest(string nId, string name, string type, string parentNId) => new()
    {
        NId = nId,
        Name = name,
        Type = type,
        ParentOrganizationNId = parentNId,
        DisplayOrder = 1,
    };
}
