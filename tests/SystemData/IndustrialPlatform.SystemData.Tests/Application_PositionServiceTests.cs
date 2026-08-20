using IndustrialPlatform.SystemData.Application.Administration;
using IndustrialPlatform.SystemData.Application.Positions;
using IndustrialPlatform.SystemData.Contracts.Administration;
using IndustrialPlatform.SystemData.Domain.Assignments;
using IndustrialPlatform.SystemData.Domain.Organizations;

namespace IndustrialPlatform.SystemData.Application.Tests;

/// <summary>
/// 岗位管理用例测试(TASK-SD-006):创建/查询/分页/改名/停用。
/// 错误码对齐 §9.9:组织缺失 404 SD_NOT_FOUND、组织内名称冲突/领域不变量 409 SD_CONCURRENCY_CONFLICT、
/// 停用有当前/未来任职 409 SD_POSITION_HAS_ACTIVE_ASSIGNMENTS。
/// </summary>
public sealed class PositionServiceTests
{
    private const string Tenant = "tenant-001";
    private const string Actor = "user-actor";
    private const string TraceId = "trace-001";

    // =====================================================================
    // 创建
    // =====================================================================

    [Fact]
    public async Task CreateAsync_CreatesPosition()
    {
        var fixture = CreateFixture();
        await SeedCompanyAsync(fixture.Orgs);

        var result = await fixture.Service.CreateAsync(Tenant, Actor, TraceId, NewPosition("pos-1", "company-a", "后端工程师", description: "负责后端服务", displayOrder: 1), CancellationToken.None);

        Assert.Equal("pos-1", result.NId);
        Assert.Equal("company-a", result.OrganizationNId);
        Assert.Equal("A 公司", result.OrganizationName);
        Assert.Equal("后端工程师", result.Name);
        Assert.Equal("负责后端服务", result.Description);
        Assert.Equal("Active", result.Status);
        Assert.Equal(1, result.DisplayOrder);
        Assert.Equal(0, result.OptimisticVersion);
        Assert.NotEqual(Guid.Empty, result.ConcurrencyVersion);
    }

    [Fact]
    public async Task CreateAsync_UnknownOrganization_ThrowsNotFound()
    {
        var fixture = CreateFixture();

        var ex = await Assert.ThrowsAsync<AdministrationNotFoundException>(() =>
            fixture.Service.CreateAsync(Tenant, Actor, TraceId, NewPosition("pos-1", "missing", "X"), CancellationToken.None));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("SD_NOT_FOUND", ex.Code);
    }

    [Fact]
    public async Task CreateAsync_CrossTenantOrganization_ThrowsNotFound()
    {
        var fixture = CreateFixture();
        await SeedCompanyAsync(fixture.Orgs);

        var ex = await Assert.ThrowsAsync<AdministrationNotFoundException>(() =>
            fixture.Service.CreateAsync("tenant-002", Actor, TraceId, NewPosition("pos-1", "company-a", "X"), CancellationToken.None));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("SD_NOT_FOUND", ex.Code);
    }

    [Fact]
    public async Task CreateAsync_InactiveOrganization_ThrowsConcurrency()
    {
        var fixture = CreateFixture();
        var company = AdministrativeOrganization.CreateRootCompany(Tenant, "company-a", "A 公司", 1);
        company.Deactivate(0, 0, 0);
        await fixture.Orgs.AddAsync(company, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AdministrationConcurrencyConflictException>(() =>
            fixture.Service.CreateAsync(Tenant, Actor, TraceId, NewPosition("pos-1", "company-a", "X"), CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("SD_CONCURRENCY_CONFLICT", ex.Code);
    }

    [Fact]
    public async Task CreateAsync_NameConflict_ThrowsConcurrency()
    {
        var fixture = CreateFixture();
        await SeedCompanyAsync(fixture.Orgs);
        await fixture.Service.CreateAsync(Tenant, Actor, TraceId, NewPosition("pos-1", "company-a", "后端工程师"), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AdministrationConcurrencyConflictException>(() =>
            fixture.Service.CreateAsync(Tenant, Actor, TraceId, NewPosition("pos-2", "company-a", "后端工程师"), CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("SD_CONCURRENCY_CONFLICT", ex.Code);
    }

    [Fact]
    public async Task CreateAsync_RecordsAuditEntry()
    {
        var fixture = CreateFixture();
        await SeedCompanyAsync(fixture.Orgs);

        await fixture.Service.CreateAsync(Tenant, Actor, TraceId, NewPosition("pos-1", "company-a", "后端工程师"), CancellationToken.None);

        var entry = Assert.Single(fixture.Audit.Entries);
        Assert.Equal("position.create", entry.Action);
        Assert.Equal("Position", entry.ObjectType);
        Assert.Equal("pos-1", entry.ObjectNId);
    }

    // =====================================================================
    // 查询与分页
    // =====================================================================

    [Fact]
    public async Task GetAsync_ReturnsPositionWithOrganizationName()
    {
        var fixture = CreateFixture();
        await SeedCompanyAsync(fixture.Orgs);
        await fixture.Service.CreateAsync(Tenant, Actor, TraceId, NewPosition("pos-1", "company-a", "后端工程师"), CancellationToken.None);

        var result = await fixture.Service.GetAsync(Tenant, "pos-1", CancellationToken.None);

        Assert.Equal("后端工程师", result.Name);
        Assert.Equal("A 公司", result.OrganizationName);
    }

    [Fact]
    public async Task GetAsync_Unknown_ThrowsNotFound()
    {
        var fixture = CreateFixture();

        var ex = await Assert.ThrowsAsync<AdministrationNotFoundException>(() =>
            fixture.Service.GetAsync(Tenant, "missing", CancellationToken.None));

        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task ListAsync_FiltersByOrganizationAndPaginates()
    {
        var fixture = CreateFixture();
        await SeedCompanyAsync(fixture.Orgs, "company-a");
        await SeedCompanyAsync(fixture.Orgs, "company-b");
        await CreatePositionAsync(fixture, "pos-a1", "company-a", "后端工程师");
        await CreatePositionAsync(fixture, "pos-a2", "company-a", "前端工程师");
        await CreatePositionAsync(fixture, "pos-b1", "company-b", "产品经理");

        var page = await fixture.Service.ListAsync(Tenant, "company-a", null, 1, 10, CancellationToken.None);

        Assert.Equal(2, page.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, p => Assert.Equal("company-a", p.OrganizationNId));
        Assert.All(page.Items, p => Assert.Equal("A 公司", p.OrganizationName));
    }

    [Fact]
    public async Task ListAsync_FiltersByStatus()
    {
        var fixture = CreateFixture();
        await SeedCompanyAsync(fixture.Orgs);
        await CreatePositionAsync(fixture, "pos-1", "company-a", "后端工程师");
        await CreatePositionAsync(fixture, "pos-2", "company-a", "前端工程师");
        await fixture.Service.SetStatusAsync(Tenant, Actor, TraceId, "pos-2", new SetPositionStatusRequest { Status = "Inactive" }, CancellationToken.None);

        var page = await fixture.Service.ListAsync(Tenant, null, "Inactive", 1, 10, CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal("pos-2", item.NId);
    }

    // =====================================================================
    // 修改
    // =====================================================================

    [Fact]
    public async Task UpdateAsync_RenamesAndAdvancesVersions()
    {
        var fixture = CreateFixture();
        await SeedCompanyAsync(fixture.Orgs);
        var created = await fixture.Service.CreateAsync(Tenant, Actor, TraceId, NewPosition("pos-1", "company-a", "后端工程师"), CancellationToken.None);

        var updated = await fixture.Service.UpdateAsync(Tenant, Actor, TraceId, "pos-1", new UpdatePositionRequest
        {
            Name = "资深后端工程师",
            Description = "新描述",
            DisplayOrder = 5,
            ExpectedOptimisticVersion = created.OptimisticVersion,
            ExpectedConcurrencyVersion = created.ConcurrencyVersion,
        }, CancellationToken.None);

        Assert.Equal("资深后端工程师", updated.Name);
        Assert.Equal("新描述", updated.Description);
        Assert.Equal(5, updated.DisplayOrder);
        Assert.Equal(3, updated.OptimisticVersion);
        Assert.NotEqual(created.ConcurrencyVersion, updated.ConcurrencyVersion);
    }

    [Fact]
    public async Task UpdateAsync_StaleVersions_ThrowsConcurrency()
    {
        var fixture = CreateFixture();
        await SeedCompanyAsync(fixture.Orgs);
        var created = await fixture.Service.CreateAsync(Tenant, Actor, TraceId, NewPosition("pos-1", "company-a", "后端工程师"), CancellationToken.None);
        await fixture.Service.UpdateAsync(Tenant, Actor, TraceId, "pos-1", new UpdatePositionRequest
        {
            Name = "改名一次",
            DisplayOrder = 1,
            ExpectedOptimisticVersion = created.OptimisticVersion,
            ExpectedConcurrencyVersion = created.ConcurrencyVersion,
        }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AdministrationConcurrencyConflictException>(() =>
            fixture.Service.UpdateAsync(Tenant, Actor, TraceId, "pos-1", new UpdatePositionRequest
            {
                Name = "过期再改",
                DisplayOrder = 2,
                ExpectedOptimisticVersion = created.OptimisticVersion,
                ExpectedConcurrencyVersion = created.ConcurrencyVersion,
            }, CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("SD_CONCURRENCY_CONFLICT", ex.Code);
    }

    // =====================================================================
    // 状态
    // =====================================================================

    [Fact]
    public async Task SetStatusAsync_DeactivateWithActiveAssignments_ThrowsHasActiveAssignments()
    {
        var fixture = CreateFixture();
        await SeedCompanyAsync(fixture.Orgs);
        await CreatePositionAsync(fixture, "pos-1", "company-a", "后端工程师");
        var position = await fixture.Positions.GetAsync(Tenant, "pos-1", CancellationToken.None);
        var assignment = UserAssignment.Create(
            Tenant,
            "assign-1",
            "user-1",
            "张三",
            position!.OrganizationNId,
            position.NId,
            position.Id,
            position.IsDeleted,
            organizationActive: true,
            positionActive: true,
            organizationMatchesPosition: true,
            isPrimary: true,
            DateTimeOffset.UtcNow.AddDays(1),
            effectiveTo: null,
            DateTimeOffset.UtcNow);
        await fixture.Assignments.AddAsync(assignment, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<PositionHasActiveAssignmentsException>(() =>
            fixture.Service.SetStatusAsync(Tenant, Actor, TraceId, "pos-1", new SetPositionStatusRequest { Status = "Inactive" }, CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("SD_POSITION_HAS_ACTIVE_ASSIGNMENTS", ex.Code);
    }

    [Fact]
    public async Task SetStatusAsync_DeactivateClean_Succeeds()
    {
        var fixture = CreateFixture();
        await SeedCompanyAsync(fixture.Orgs);
        await CreatePositionAsync(fixture, "pos-1", "company-a", "后端工程师");

        var result = await fixture.Service.SetStatusAsync(Tenant, Actor, TraceId, "pos-1", new SetPositionStatusRequest { Status = "Inactive" }, CancellationToken.None);

        Assert.Equal("Inactive", result.Status);
    }

    [Fact]
    public async Task SetStatusAsync_Activate_Succeeds()
    {
        var fixture = CreateFixture();
        await SeedCompanyAsync(fixture.Orgs);
        await CreatePositionAsync(fixture, "pos-1", "company-a", "后端工程师");
        await fixture.Service.SetStatusAsync(Tenant, Actor, TraceId, "pos-1", new SetPositionStatusRequest { Status = "Inactive" }, CancellationToken.None);

        var result = await fixture.Service.SetStatusAsync(Tenant, Actor, TraceId, "pos-1", new SetPositionStatusRequest { Status = "Active" }, CancellationToken.None);

        Assert.Equal("Active", result.Status);
    }

    // ===== 辅助 =====

    private static (PositionService Service, FakeAdministrativeOrganizationStore Orgs, FakePositionStore Positions, FakeUserAssignmentStore Assignments, RecordingLocalAuditCommand Audit) CreateFixture()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        var positions = new FakePositionStore();
        var assignments = new FakeUserAssignmentStore();
        var audit = new RecordingLocalAuditCommand();
        var service = new PositionService(positions, orgs, assignments, audit);
        return (service, orgs, positions, assignments, audit);
    }

    private static async Task SeedCompanyAsync(FakeAdministrativeOrganizationStore orgs, string companyNId = "company-a")
    {
        var company = AdministrativeOrganization.CreateRootCompany(Tenant, companyNId, companyNId == "company-a" ? "A 公司" : "B 公司", 1);
        await orgs.AddAsync(company, CancellationToken.None);
    }

    private static CreatePositionRequest NewPosition(string nId, string organizationNId, string name, string? description = null, int? displayOrder = 1) => new()
    {
        NId = nId,
        OrganizationNId = organizationNId,
        Name = name,
        Description = description,
        DisplayOrder = displayOrder,
    };

    private static Task<PositionV1> CreatePositionAsync(
        (PositionService Service, FakeAdministrativeOrganizationStore Orgs, FakePositionStore Positions, FakeUserAssignmentStore Assignments, RecordingLocalAuditCommand Audit) fixture,
        string nId,
        string organizationNId,
        string name) =>
        fixture.Service.CreateAsync(Tenant, Actor, TraceId, NewPosition(nId, organizationNId, name), CancellationToken.None);
}
