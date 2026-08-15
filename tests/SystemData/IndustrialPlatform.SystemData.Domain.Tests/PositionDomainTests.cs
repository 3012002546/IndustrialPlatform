using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Domain.Organizations;
using IndustrialPlatform.SystemData.Domain.Positions;

namespace IndustrialPlatform.SystemData.Domain.Tests;

/// <summary>
/// 岗位领域测试(TASK-SD-005,05 方案 §7.3/§12.3):
/// 创建门(组织活动/未删除)、组织归属一致、停用依赖与恢复。
/// </summary>
public sealed class PositionDomainTests
{
    private const string Tenant = "tenant-001";

    [Fact]
    public void Create_WithActiveOrganization_Succeeds()
    {
        var position = Position.Create(
            Tenant, "pos-001",
            Tenant, "org-001", Guid.NewGuid(), organizationIsDeleted: false, organizationActive: true,
            "后端工程师", "负责服务端开发", 0);

        Assert.Equal("pos-001", position.NId);
        Assert.Equal("org-001", position.OrganizationNId);
        Assert.Equal(PositionStatus.Active, position.Status);
        Assert.Equal("后端工程师", position.Name);
    }

    [Fact]
    public void Create_WithInactiveOrganization_Throws()
    {
        Assert.Throws<BusinessException>(() =>
            Position.Create(
                Tenant, "pos-001",
                Tenant, "org-001", Guid.NewGuid(), organizationIsDeleted: false, organizationActive: false,
                "后端工程师", null, 0));
    }

    [Fact]
    public void Create_WithDeletedOrganization_Throws()
    {
        Assert.Throws<BusinessException>(() =>
            Position.Create(
                Tenant, "pos-001",
                Tenant, "org-001", Guid.NewGuid(), organizationIsDeleted: true, organizationActive: true,
                "后端工程师", null, 0));
    }

    [Fact]
    public void Create_CrossTenantOrganization_Throws()
    {
        Assert.Throws<BusinessException>(() =>
            Position.Create(
                Tenant, "pos-001",
                "tenant-other", "org-001", Guid.NewGuid(), organizationIsDeleted: false, organizationActive: true,
                "后端工程师", null, 0));
    }

    [Fact]
    public void SameName_DifferentOrganizations_Allowed()
    {
        var a = Position.Create(
            Tenant, "pos-a", Tenant, "org-a", Guid.NewGuid(), false, true, "后端工程师", null, 0);
        var b = Position.Create(
            Tenant, "pos-b", Tenant, "org-b", Guid.NewGuid(), false, true, "后端工程师", null, 0);

        Assert.Equal(a.Name, b.Name);
    }

    [Fact]
    public void Rename_UpdatesName()
    {
        var position = CreateDefault();

        position.Rename("资深后端工程师");

        Assert.Equal("资深后端工程师", position.Name);
    }

    [Fact]
    public void Rename_Empty_Throws()
    {
        var position = CreateDefault();

        Assert.Throws<ValidationException>(() => position.Rename("   "));
    }

    [Fact]
    public void ChangeDescription_Null_Allowed()
    {
        var position = CreateDefault();

        position.ChangeDescription(null);

        Assert.Null(position.Description);
    }

    [Fact]
    public void ChangeDisplayOrder_UpdatesOrder()
    {
        var position = CreateDefault();

        position.ChangeDisplayOrder(9);

        Assert.Equal(9, position.DisplayOrder);
    }

    [Fact]
    public void Deactivate_WithActiveOrFutureAssignments_Throws()
    {
        var position = CreateDefault();

        Assert.Throws<BusinessException>(() => position.Deactivate(hasActiveOrFutureAssignments: true));
    }

    [Fact]
    public void Deactivate_NoAssignments_Succeeds()
    {
        var position = CreateDefault();

        position.Deactivate(hasActiveOrFutureAssignments: false);

        Assert.Equal(PositionStatus.Inactive, position.Status);
    }

    [Fact]
    public void Activate_WithInactiveOrganization_Throws()
    {
        var position = CreateDefault();
        position.Deactivate(false);

        Assert.Throws<BusinessException>(() => position.Activate(organizationActive: false));
    }

    [Fact]
    public void Activate_Ok()
    {
        var position = CreateDefault();
        position.Deactivate(false);

        position.Activate(organizationActive: true);

        Assert.Equal(PositionStatus.Active, position.Status);
    }

    private static Position CreateDefault() =>
        Position.Create(
            Tenant, "pos-001",
            Tenant, "org-001", Guid.NewGuid(), organizationIsDeleted: false, organizationActive: true,
            "后端工程师", "负责服务端开发", 0);
}
