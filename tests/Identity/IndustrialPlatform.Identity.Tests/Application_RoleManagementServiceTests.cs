using System;
using System.Threading;
using System.Threading.Tasks;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Contracts.Management;
using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.SharedKernel.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IndustrialPlatform.Identity.Application.Tests;

/// <summary>
/// 角色管理用例测试(§16.2/§19.2):创建/修改/权限分配的校验与守卫,
/// 乐观并发映射,租户隔离,操作审计与按持有者的权限缓存失效。
/// </summary>
public sealed class RoleManagementServiceTests
{
    private readonly FakeManagementStore _store = new();
    private readonly FakePermissionCache _cache = new();
    private readonly FakeAuditSink _auditSink = new();

    private RoleManagementService CreateService() => new(
        _store,
        _cache,
        _auditSink,
        NullLogger<RoleManagementService>.Instance);

    private Role SeedRole(string nId = "role.operator", bool isSystem = false)
    {
        var role = ManagementTestData.CreateRole(ManagementTestData.Tenant, nId, isSystem);
        _store.Seed(role);
        return role;
    }

    private Permission SeedPermission(string nId)
    {
        var permission = ManagementTestData.CreatePermission(nId);
        _store.Seed(permission);
        return permission;
    }

    [Fact]
    public async Task CreateAsync_WithPermissions_ReturnsSummaryAndAudits()
    {
        SeedPermission("identity.user.view");
        var service = CreateService();

        var result = await service.CreateAsync(
            ManagementTestData.Tenant,
            ManagementTestData.Actor,
            new CreateRoleRequest("role.operator", "Operator", "Operation role", ["identity.user.view"]),
            CancellationToken.None);

        Assert.Equal("role.operator", result.RoleNId);
        Assert.Equal("Operator", result.Name);
        Assert.False(result.IsSystem);
        Assert.Equal(["identity.user.view"], result.PermissionNIds);

        var audit = Assert.Single(_auditSink.Entries);
        Assert.Equal(OperationAction.RoleCreate, audit.Action);
        Assert.Equal(OperationObjectType.Role, audit.ObjectType);
        Assert.Equal("role.operator", audit.ObjectNId);
        Assert.Null(audit.BeforeSummary);
        Assert.NotNull(audit.AfterSummary);
        Assert.Contains("permissionCount=1", audit.AfterSummary);
        Assert.DoesNotContain("Guid", audit.AfterSummary);
    }

    [Fact]
    public async Task CreateAsync_GeneratedNId_WhenAbsent()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            ManagementTestData.Tenant,
            ManagementTestData.Actor,
            new CreateRoleRequest(null, "Operator", null, null),
            CancellationToken.None);

        Assert.StartsWith("ROLE-", result.RoleNId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_NullName_ThrowsValidation()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(ManagementTestData.Tenant, ManagementTestData.Actor, new CreateRoleRequest(null, null, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_NIdConflict_ThrowsRoleNIdConflict()
    {
        SeedRole();
        var service = CreateService();

        await Assert.ThrowsAsync<RoleNIdConflictException>(() =>
            service.CreateAsync(ManagementTestData.Tenant, ManagementTestData.Actor, new CreateRoleRequest("role.operator", "Other", null, null), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_InvalidPermission_ThrowsBusinessRuleViolation()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            service.CreateAsync(ManagementTestData.Tenant, ManagementTestData.Actor, new CreateRoleRequest("role.operator", "Operator", null, ["perm.missing"]), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_AuditSinkFailure_DoesNotBlockCreate()
    {
        _auditSink.ThrowOnWrite = true;
        var service = CreateService();

        var result = await service.CreateAsync(
            ManagementTestData.Tenant,
            ManagementTestData.Actor,
            new CreateRoleRequest("role.operator", "Operator", null, null),
            CancellationToken.None);

        Assert.Equal("role.operator", result.RoleNId);
    }

    [Fact]
    public async Task UpdateAsync_ChangesProfile_Succeeds()
    {
        var role = SeedRole();
        var service = CreateService();
        var (optimistic, concurrency) = ManagementTestData.Versions(role);

        var result = await service.UpdateAsync(
            ManagementTestData.Tenant,
            ManagementTestData.Actor,
            "role.operator",
            new UpdateRoleRequest("Supervisor", "Supervises line", optimistic, concurrency),
            CancellationToken.None);

        Assert.Equal("Supervisor", result.Name);
        Assert.Equal("Supervises line", result.Description);
        Assert.Equal(OperationAction.RoleUpdate, Assert.Single(_auditSink.Entries).Action);
    }

    [Fact]
    public async Task UpdateAsync_ConcurrencyConflict_ThrowsConcurrencyConflict()
    {
        SeedRole();
        var service = CreateService();

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            service.UpdateAsync(ManagementTestData.Tenant, ManagementTestData.Actor, "role.operator", new UpdateRoleRequest("Supervisor", null, -1, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_CrossTenant_ThrowsResourceNotFound()
    {
        SeedRole();
        var service = CreateService();

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.UpdateAsync("other", ManagementTestData.Actor, "role.operator", new UpdateRoleRequest("Supervisor", null, 0, Guid.Empty), CancellationToken.None));
    }

    [Fact]
    public async Task AssignPermissionsAsync_AssignAndRemove_Succeeds()
    {
        var role = SeedRole();
        var (optimistic, concurrency) = ManagementTestData.Versions(role);
        var permA = SeedPermission("identity.user.view");
        role.AssignPermission(permA);
        SeedPermission("identity.user.create");
        var service = CreateService();

        var result = await service.AssignPermissionsAsync(
            ManagementTestData.Tenant,
            ManagementTestData.Actor,
            "role.operator",
            new AssignRolePermissionsRequest(["identity.user.create"], optimistic, concurrency),
            CancellationToken.None);

        Assert.Equal(["identity.user.create"], result.PermissionNIds);
        Assert.Equal(OperationAction.RoleAssignPermissions, Assert.Single(_auditSink.Entries).Action);
    }

    [Fact]
    public async Task AssignPermissionsAsync_InvalidPermission_ThrowsBusinessRuleViolation()
    {
        var role = SeedRole();
        var service = CreateService();
        var (optimistic, concurrency) = ManagementTestData.Versions(role);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            service.AssignPermissionsAsync(
                ManagementTestData.Tenant,
                ManagementTestData.Actor,
                "role.operator",
                new AssignRolePermissionsRequest(["perm.missing"], optimistic, concurrency),
                CancellationToken.None));
    }

    [Fact]
    public async Task AssignPermissionsAsync_InvalidatesEachHolderCache()
    {
        var role = SeedRole();
        var (optimistic, concurrency) = ManagementTestData.Versions(role);
        var perm = SeedPermission("identity.user.view");
        role.AssignPermission(perm);
        var alice = ManagementTestData.CreateUser(nId: "alice.user");
        alice.AssignRole(role);
        var bob = ManagementTestData.CreateUser(nId: "bob.user", loginName: "bob", name: "Bob");
        bob.AssignRole(role);
        _store.Seed(alice);
        _store.Seed(bob);
        var service = CreateService();

        var result = await service.AssignPermissionsAsync(
            ManagementTestData.Tenant,
            ManagementTestData.Actor,
            "role.operator",
            new AssignRolePermissionsRequest(["identity.user.view"], optimistic, concurrency),
            CancellationToken.None);

        Assert.Equal(["identity.user.view"], result.PermissionNIds);
        Assert.Equal(2, _cache.Invalidated.Count);
        Assert.Contains(("development", "alice.user"), _cache.Invalidated);
        Assert.Contains(("development", "bob.user"), _cache.Invalidated);
    }

    [Fact]
    public async Task AssignPermissionsAsync_CacheInvalidateFailure_DoesNotBlock()
    {
        var role = SeedRole();
        var (optimistic, concurrency) = ManagementTestData.Versions(role);
        var perm = SeedPermission("identity.user.view");
        role.AssignPermission(perm);
        var alice = ManagementTestData.CreateUser(nId: "alice.user");
        alice.AssignRole(role);
        _store.Seed(alice);
        _cache.ThrowOnInvalidate = true;
        var service = CreateService();

        var result = await service.AssignPermissionsAsync(
            ManagementTestData.Tenant,
            ManagementTestData.Actor,
            "role.operator",
            new AssignRolePermissionsRequest(["identity.user.view"], optimistic, concurrency),
            CancellationToken.None);

        Assert.Equal("role.operator", result.RoleNId);
    }

    [Fact]
    public async Task AssignPermissionsAsync_ConcurrencyConflict_Throws()
    {
        SeedRole();
        var service = CreateService();

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            service.AssignPermissionsAsync(
                ManagementTestData.Tenant,
                ManagementTestData.Actor,
                "role.operator",
                new AssignRolePermissionsRequest([], -1, Guid.NewGuid()),
                CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_OverridesTenantAndMapsPage()
    {
        SeedRole("role.a");
        SeedRole("role.b");
        var service = CreateService();

        var page = await service.ListAsync(
            ManagementTestData.Tenant,
            new RoleListFilter("ignored", null, null, 1, 20),
            CancellationToken.None);

        Assert.Equal(2, page.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(1, page.PageIndex);
        Assert.Equal(20, page.PageSize);
    }

    [Fact]
    public async Task GetAsync_CrossTenant_ThrowsResourceNotFound()
    {
        SeedRole();
        var service = CreateService();

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.GetAsync("other", "role.operator", CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_Unknown_ThrowsResourceNotFound()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.GetAsync(ManagementTestData.Tenant, "role.missing", CancellationToken.None));
    }
}
