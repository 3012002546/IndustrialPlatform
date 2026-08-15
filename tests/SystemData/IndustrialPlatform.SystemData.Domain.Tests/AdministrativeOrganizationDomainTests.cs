using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Domain.Organizations;

namespace IndustrialPlatform.SystemData.Domain.Tests;

/// <summary>
/// 行政组织领域测试(TASK-SD-005,05 方案 §7.2/§12.3):
/// 四类型父子矩阵、多根公司、同租户/跨根移动、祖先循环、并发预览过期、
/// 停用依赖与恢复、名称/顺序修改。
/// </summary>
public sealed class AdministrativeOrganizationDomainTests
{
    private const string Tenant = "tenant-001";

    // ===== 创建与类型矩阵 =====

    [Fact]
    public void CreateRootCompany_IsRootAndActive()
    {
        var company = Company("comp-001", "ACME");

        Assert.Equal(AdministrativeOrganizationType.Company, company.Type);
        Assert.Null(company.ParentOrganizationId);
        Assert.Null(company.ParentOrganizationNId);
        Assert.Equal(OrganizationStatus.Active, company.Status);
        Assert.Equal(1, company.OrganizationRevision);
    }

    [Fact]
    public void MultipleRootCompanies_AreAllowed()
    {
        var first = Company("comp-001", "ACME");
        var second = Company("comp-002", "BETA");

        Assert.Null(first.ParentOrganizationId);
        Assert.Null(second.ParentOrganizationId);
    }

    [Theory]
    [InlineData(AdministrativeOrganizationType.Department, AdministrativeOrganizationType.Company, true)]
    [InlineData(AdministrativeOrganizationType.Section, AdministrativeOrganizationType.Company, false)]
    [InlineData(AdministrativeOrganizationType.Team, AdministrativeOrganizationType.Company, false)]
    [InlineData(AdministrativeOrganizationType.Department, AdministrativeOrganizationType.Section, false)]
    [InlineData(AdministrativeOrganizationType.Section, AdministrativeOrganizationType.Department, true)]
    [InlineData(AdministrativeOrganizationType.Section, AdministrativeOrganizationType.Section, true)]
    [InlineData(AdministrativeOrganizationType.Team, AdministrativeOrganizationType.Department, true)]
    [InlineData(AdministrativeOrganizationType.Team, AdministrativeOrganizationType.Section, true)]
    [InlineData(AdministrativeOrganizationType.Team, AdministrativeOrganizationType.Team, true)]
    [InlineData(AdministrativeOrganizationType.Company, AdministrativeOrganizationType.Company, false)]
    public void ParentMatrix_AllowsExactlyExpectedCombinations(
        AdministrativeOrganizationType childType,
        AdministrativeOrganizationType parentType,
        bool allowed)
    {
        Assert.Equal(allowed, AdministrativeOrganization.CanHaveParent(childType, parentType));
    }

    [Fact]
    public void CreateChild_ValidChild_IsActiveWithParentSnapshot()
    {
        var company = Company("comp-001", "ACME");
        var dept = Child(company, AdministrativeOrganizationType.Department, "dept-001", "R&D");

        Assert.Equal(AdministrativeOrganizationType.Department, dept.Type);
        Assert.Equal(company.Id, dept.ParentOrganizationId);
        Assert.Equal(company.NId, dept.ParentOrganizationNId);
        Assert.False(dept.ParentOrganizationIsDeleted);
        Assert.Equal(OrganizationStatus.Active, dept.Status);
        Assert.Equal(1, dept.OrganizationRevision);
    }

    [Fact]
    public void CreateChild_CompanyAsChild_Throws()
    {
        var company = Company("comp-001", "ACME");

        Assert.Throws<ValidationException>(() =>
            Child(company, AdministrativeOrganizationType.Company, "comp-002", "Nested"));
    }

    [Fact]
    public void CreateChild_InvalidParentType_Throws()
    {
        var company = Company("comp-001", "ACME");

        Assert.Throws<ValidationException>(() =>
            Child(company, AdministrativeOrganizationType.Section, "sec-001", "Finance"));
    }

    [Fact]
    public void CreateChild_CrossTenantParent_Throws()
    {
        var otherTenantCompany = Company("comp-other", "Other");

        Assert.Throws<BusinessException>(() =>
            AdministrativeOrganization.CreateChild(
                Tenant, "dept-001", "R&D", AdministrativeOrganizationType.Department,
                "tenant-other", otherTenantCompany.NId, otherTenantCompany.Id, false, true,
                AdministrativeOrganizationType.Company, 0));
    }

    [Fact]
    public void CreateChild_InactiveParent_Throws()
    {
        var company = Company("comp-001", "ACME");
        company.Deactivate(0, 0, 0);

        Assert.Throws<BusinessException>(() =>
            Child(company, AdministrativeOrganizationType.Department, "dept-001", "R&D"));
    }

    [Fact]
    public void CreateChild_DeletedParent_Throws()
    {
        var company = Company("comp-001", "ACME");

        Assert.Throws<BusinessException>(() =>
            AdministrativeOrganization.CreateChild(
                Tenant, "dept-001", "R&D", AdministrativeOrganizationType.Department,
                Tenant, company.NId, company.Id, parentIsDeleted: true, parentActive: true,
                AdministrativeOrganizationType.Company, 0));
    }

    // ===== 移动 =====

    [Fact]
    public void Move_CrossRootCompany_SameTenant_PreservesNIdAndBumpsRevision()
    {
        var companyA = Company("comp-a", "ACME");
        var companyB = Company("comp-b", "BETA");
        var dept = Child(companyA, AdministrativeOrganizationType.Department, "dept-001", "R&D");
        var preview = dept.PreviewMove(
            companyB.NId, companyB.Id, false, companyB.TenantNId, companyB.Type, true, [dept.NId], 1, 0, 0);

        dept.Move(preview, companyB.NId, companyB.Id, false, companyB.TenantNId, companyB.Type, true, [dept.NId]);

        Assert.Equal(companyB.Id, dept.ParentOrganizationId);
        Assert.Equal(companyB.NId, dept.ParentOrganizationNId);
        Assert.Equal("dept-001", dept.NId);
        Assert.Equal(2, dept.OrganizationRevision);
    }

    [Fact]
    public void PreviewMove_ReturnsAffectedSummary()
    {
        var companyA = Company("comp-a", "ACME");
        var companyB = Company("comp-b", "BETA");
        var dept = Child(companyA, AdministrativeOrganizationType.Department, "dept-001", "R&D");

        var preview = dept.PreviewMove(
            companyB.NId, companyB.Id, false, companyB.TenantNId, companyB.Type, true, [dept.NId], 1, 2, 3);

        Assert.Equal(dept.NId, preview.NId);
        Assert.Equal(2, preview.OrganizationRevision);
        Assert.Equal(1, preview.SubtreeOrganizationCount);
        Assert.Equal(2, preview.SubtreePositionCount);
        Assert.Equal(3, preview.SubtreeAssignmentCount);
        Assert.Equal(6, preview.AffectedCount);
    }

    [Fact]
    public void Move_CrossTenant_Throws()
    {
        var company = Company("comp-a", "ACME");
        var dept = Child(company, AdministrativeOrganizationType.Department, "dept-001", "R&D");
        var preview = dept.PreviewMove(
            "comp-b", Guid.NewGuid(), false, Tenant, AdministrativeOrganizationType.Company, true, [dept.NId], 1, 0, 0);

        Assert.Throws<BusinessException>(() =>
            dept.Move(preview, "comp-b", Guid.NewGuid(), false, "tenant-other",
                AdministrativeOrganizationType.Company, true, [dept.NId]));
    }

    [Fact]
    public void Move_ToSelf_Throws()
    {
        var company = Company("comp-a", "ACME");
        var dept = Child(company, AdministrativeOrganizationType.Department, "dept-001", "R&D");

        Assert.Throws<BusinessException>(() =>
            dept.PreviewMove(
                dept.NId, dept.Id, false, Tenant, dept.Type, true, [dept.NId], 1, 0, 0));
    }

    [Fact]
    public void Move_AncestorCycle_Throws()
    {
        var company = Company("comp-a", "ACME");
        var dept = Child(company, AdministrativeOrganizationType.Department, "dept-001", "R&D");
        var section = Child(dept, AdministrativeOrganizationType.Section, "sec-001", "Quality");
        var nested = Child(section, AdministrativeOrganizationType.Section, "sec-002", "Lab");

        // Section 可作 Section 的父级(类型矩阵合法),但把 section 移到自己的后代 nested 之下 → 祖先循环
        Assert.Throws<BusinessException>(() =>
            section.PreviewMove(
                nested.NId, nested.Id, false, Tenant, nested.Type, true, [section.NId, nested.NId], 2, 0, 0));
    }

    [Fact]
    public void Move_CompanyRoot_Throws()
    {
        var company = Company("comp-a", "ACME");

        Assert.Throws<BusinessException>(() =>
            company.PreviewMove("comp-b", Guid.NewGuid(), false, Tenant,
                AdministrativeOrganizationType.Company, true, [company.NId], 1, 0, 0));
    }

    [Fact]
    public void Move_TypeMatrixViolation_Throws()
    {
        var company = Company("comp-a", "ACME");
        var dept = Child(company, AdministrativeOrganizationType.Department, "dept-001", "R&D");
        var team = Child(dept, AdministrativeOrganizationType.Team, "team-001", "Ops");
        var otherCompany = Company("comp-b", "BETA");

        // Team 不能以 Company 为父级(类型矩阵校验先于祖先检查)
        Assert.Throws<ValidationException>(() =>
            team.PreviewMove(
                otherCompany.NId, otherCompany.Id, false, Tenant, otherCompany.Type, true, [team.NId], 1, 0, 0));
    }

    [Fact]
    public void Move_StalePreview_Throws()
    {
        var companyA = Company("comp-a", "ACME");
        var companyB = Company("comp-b", "BETA");
        var dept = Child(companyA, AdministrativeOrganizationType.Department, "dept-001", "R&D");
        var preview = dept.PreviewMove(
            companyB.NId, companyB.Id, false, companyB.TenantNId, companyB.Type, true, [dept.NId], 1, 0, 0);

        dept.Move(preview, companyB.NId, companyB.Id, false, companyB.TenantNId, companyB.Type, true, [dept.NId]);

        // 同一预览已消费过,修订已推进,再次提交过期
        Assert.Throws<BusinessException>(() =>
            dept.Move(preview, companyB.NId, companyB.Id, false, companyB.TenantNId, companyB.Type, true, [dept.NId]));
    }

    [Fact]
    public void Move_InactiveOrganization_Throws()
    {
        var company = Company("comp-a", "ACME");
        var dept = Child(company, AdministrativeOrganizationType.Department, "dept-001", "R&D");
        dept.Deactivate(0, 0, 0);

        Assert.Throws<BusinessException>(() =>
            dept.PreviewMove(
                company.NId, company.Id, false, Tenant, company.Type, true, [dept.NId], 1, 0, 0));
    }

    // ===== 停用与恢复 =====

    [Fact]
    public void Deactivate_WithActiveChild_Throws()
    {
        var company = Company("comp-a", "ACME");
        var dept = Child(company, AdministrativeOrganizationType.Department, "dept-001", "R&D");

        Assert.Throws<BusinessException>(() => dept.Deactivate(activeChildCount: 1, 0, 0));
    }

    [Fact]
    public void Deactivate_WithActivePosition_Throws()
    {
        var company = Company("comp-a", "ACME");
        var dept = Child(company, AdministrativeOrganizationType.Department, "dept-001", "R&D");

        Assert.Throws<BusinessException>(() => dept.Deactivate(0, activePositionCount: 1, 0));
    }

    [Fact]
    public void Deactivate_WithActiveOrFutureAssignment_Throws()
    {
        var company = Company("comp-a", "ACME");
        var dept = Child(company, AdministrativeOrganizationType.Department, "dept-001", "R&D");

        Assert.Throws<BusinessException>(() => dept.Deactivate(0, 0, activeOrFutureAssignmentCount: 1));
    }

    [Fact]
    public void Deactivate_NoDependencies_Succeeds()
    {
        var company = Company("comp-a", "ACME");
        var dept = Child(company, AdministrativeOrganizationType.Department, "dept-001", "R&D");

        dept.Deactivate(0, 0, 0);

        Assert.Equal(OrganizationStatus.Inactive, dept.Status);
    }

    [Fact]
    public void Deactivate_AlreadyInactive_Throws()
    {
        var company = Company("comp-a", "ACME");
        company.Deactivate(0, 0, 0);

        Assert.Throws<BusinessException>(() => company.Deactivate(0, 0, 0));
    }

    [Fact]
    public void Activate_WithInactiveParent_Throws()
    {
        var company = Company("comp-a", "ACME");
        var parent = Child(company, AdministrativeOrganizationType.Department, "dept-001", "R&D");
        var section = Child(parent, AdministrativeOrganizationType.Section, "sec-001", "Quality");
        parent.Deactivate(0, 0, 0);
        section.Deactivate(0, 0, 0);

        Assert.Throws<BusinessException>(() => section.Activate(parentIsActive: false));
    }

    [Fact]
    public void Activate_RootCompany_WithoutParentCheck_Succeeds()
    {
        var company = Company("comp-a", "ACME");
        company.Deactivate(0, 0, 0);

        company.Activate(parentIsActive: false);

        Assert.Equal(OrganizationStatus.Active, company.Status);
    }

    // ===== 修改 =====

    [Fact]
    public void Rename_UpdatesName()
    {
        var company = Company("comp-a", "ACME");

        company.Rename("ACME v2");

        Assert.Equal("ACME v2", company.Name);
        Assert.True(company.OptimisticVersion >= 1);
    }

    [Fact]
    public void Rename_Empty_Throws()
    {
        var company = Company("comp-a", "ACME");

        Assert.Throws<ValidationException>(() => company.Rename("   "));
    }

    [Fact]
    public void ChangeDisplayOrder_UpdatesOrder()
    {
        var company = Company("comp-a", "ACME");

        company.ChangeDisplayOrder(5);

        Assert.Equal(5, company.DisplayOrder);
    }

    [Fact]
    public void ChangeDisplayOrder_Negative_Throws()
    {
        var company = Company("comp-a", "ACME");

        Assert.Throws<ValidationException>(() => company.ChangeDisplayOrder(-1));
    }

    // ===== 辅助 =====

    private static AdministrativeOrganization Company(string nid, string name) =>
        AdministrativeOrganization.CreateRootCompany(Tenant, nid, name, 0);

    private static AdministrativeOrganization Child(
        AdministrativeOrganization parent,
        AdministrativeOrganizationType type,
        string nid,
        string name) =>
        AdministrativeOrganization.CreateChild(
            Tenant,
            nid,
            name,
            type,
            parent.TenantNId,
            parent.NId,
            parent.Id,
            parent.IsDeleted,
            parent.Status == OrganizationStatus.Active,
            parent.Type,
            0);
}
