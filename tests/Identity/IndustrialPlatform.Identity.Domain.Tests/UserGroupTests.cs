using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Identity.Domain.UserGroups;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.Identity.Domain.Tests;

/// <summary>
/// UserGroup 聚合测试(§29A.2):NId 规范化、资料/状态变更、成员与组角色守卫
/// (跨租户/已删除/重复幂等)、事件发布、禁止嵌套的结构约束与乐观版本推进。
/// </summary>
public sealed class UserGroupTests
{
    private const string TenantNId = "tenant-01";

    private static UserGroup CreateGroup(string nId = "group.ops", string tenantNId = TenantNId) =>
        UserGroup.Create(tenantNId, nId, "运维组", null);

    private static User CreateUser(string nId = "user-001", string tenantNId = TenantNId) =>
        User.Create(tenantNId, nId, nId, "Alice", null, null, "hashed-password");

    private static Role CreateRole(string nId = "role.editor", bool isSystem = false, string tenantNId = TenantNId) =>
        Role.Create(tenantNId, nId, nId, null, isSystem);

    [Fact]
    public void Create_SetsFieldsAndPublishesCreatedEvent()
    {
        var group = UserGroup.Create(TenantNId, "group.ops", "运维组", "描述");

        Assert.Equal(TenantNId, group.TenantNId);
        Assert.Equal("group.ops", group.NId);
        Assert.Equal("GROUP.OPS", group.NormalizedNId);
        Assert.Equal("运维组", group.Name);
        Assert.Equal("描述", group.Description);
        Assert.Equal(UserGroupStatus.Active, group.Status);
        Assert.Empty(group.Memberships);
        Assert.Empty(group.Roles);

        var created = Assert.Single(group.DomainEvents.OfType<UserGroupCreatedEvent>());
        Assert.Equal(TenantNId, created.TenantNId);
        Assert.Equal("group.ops", created.GroupNId);
        Assert.Equal(UserGroupStatus.Active, created.Status);
    }

    [Fact]
    public void Create_EmptyName_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() => UserGroup.Create(TenantNId, "group.ops", "  ", null));
    }

    [Fact]
    public void Create_InvalidNId_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() => UserGroup.Create(TenantNId, "组-非法", "运维组", null));
    }

    [Fact]
    public void Create_NormalizesNIdCaseInsensitively()
    {
        var group = UserGroup.Create(TenantNId, "Group.Ops", "运维组", null);

        Assert.Equal("Group.Ops", group.NId);
        Assert.Equal("GROUP.OPS", group.NormalizedNId);
    }

    [Fact]
    public void ChangeProfile_UpdatesNameAndDescription_PublishesChangedEvent()
    {
        var group = CreateGroup();
        var beforeVersion = group.OptimisticVersion;

        group.ChangeProfile("新名称", "新描述");

        Assert.Equal("新名称", group.Name);
        Assert.Equal("新描述", group.Description);
        Assert.True(group.OptimisticVersion > beforeVersion);
        var changed = Assert.Single(group.DomainEvents.OfType<UserGroupChangedEvent>());
        Assert.Equal("新名称", changed.Name);
        Assert.Equal(UserGroupStatus.Active, changed.Status);
    }

    [Fact]
    public void ChangeProfile_EmptyName_Throws()
    {
        var group = CreateGroup();

        Assert.Throws<ValidationException>(() => group.ChangeProfile(" ", null));
    }

    [Fact]
    public void AssignMember_AddsMembership_PublishesEventWithUserNId()
    {
        var group = CreateGroup();
        var user = CreateUser();

        group.AssignMember(user);

        var relation = Assert.Single(group.Memberships);
        Assert.Equal(TenantNId, relation.TenantNId);
        Assert.Equal(group.Id, relation.UserGroupId);
        Assert.Equal(user.Id, relation.UserId);
        Assert.False(relation.IsDeleted);
        Assert.False(relation.UserIsDeleted);
        Assert.False(relation.UserGroupIsDeleted);

        var membershipEvent = Assert.Single(group.DomainEvents.OfType<UserGroupMembershipChangedEvent>());
        Assert.Equal("user-001", membershipEvent.UserNId);
        Assert.Equal("group.ops", membershipEvent.GroupNId);
    }

    [Fact]
    public void AssignMember_CrossTenant_ThrowsBusinessException()
    {
        var group = CreateGroup();
        var otherTenantUser = CreateUser(nId: "user-other", tenantNId: "tenant-02");

        Assert.Throws<BusinessException>(() => group.AssignMember(otherTenantUser));
        Assert.Empty(group.Memberships);
    }

    [Fact]
    public void AssignMember_DeletedUser_ThrowsBusinessException()
    {
        var group = CreateGroup();
        var user = CreateUser();
        user.MarkDeleted();

        Assert.Throws<BusinessException>(() => group.AssignMember(user));
        Assert.Empty(group.Memberships);
    }

    [Fact]
    public void AssignMember_Duplicate_IsIdempotent()
    {
        var group = CreateGroup();
        var user = CreateUser();

        group.AssignMember(user);
        group.ClearDomainEvents();
        var versionBefore = group.OptimisticVersion;

        group.AssignMember(user);

        Assert.Single(group.Memberships);
        Assert.Equal(versionBefore, group.OptimisticVersion);
        Assert.Empty(group.DomainEvents);
    }

    [Fact]
    public void RemoveMember_NotMember_IsIdempotent()
    {
        var group = CreateGroup();
        group.ClearDomainEvents();
        var user = CreateUser();

        group.RemoveMember(user);

        Assert.Empty(group.Memberships);
        Assert.Empty(group.DomainEvents);
    }

    [Fact]
    public void RemoveMember_RemovesRelationAndPublishesEvent()
    {
        var group = CreateGroup();
        var user = CreateUser();
        group.AssignMember(user);
        group.ClearDomainEvents();

        group.RemoveMember(user);

        var relation = Assert.Single(group.Memberships);
        Assert.True(relation.IsDeleted);
        var membershipEvent = Assert.Single(group.DomainEvents.OfType<UserGroupMembershipChangedEvent>());
        Assert.Equal("user-001", membershipEvent.UserNId);
    }

    [Fact]
    public void AssignRole_AddsGroupRole_PublishesRolesChangedEvent()
    {
        var group = CreateGroup();
        var role = CreateRole();

        group.AssignRole(role);

        var relation = Assert.Single(group.Roles);
        Assert.Equal(group.Id, relation.UserGroupId);
        Assert.Equal(role.Id, relation.RoleId);
        Assert.False(relation.IsDeleted);
        Assert.False(relation.RoleIsDeleted);
        Assert.Single(group.DomainEvents.OfType<UserGroupRolesChangedEvent>());
    }

    [Fact]
    public void AssignRole_CrossTenant_ThrowsBusinessException()
    {
        var group = CreateGroup();
        var otherTenantRole = CreateRole(tenantNId: "tenant-02");

        Assert.Throws<BusinessException>(() => group.AssignRole(otherTenantRole));
        Assert.Empty(group.Roles);
    }

    [Fact]
    public void AssignRole_DeletedRole_ThrowsBusinessException()
    {
        var group = CreateGroup();
        var role = CreateRole();
        role.Delete();

        Assert.Throws<BusinessException>(() => group.AssignRole(role));
        Assert.Empty(group.Roles);
    }

    [Fact]
    public void AssignRole_Duplicate_IsIdempotent()
    {
        var group = CreateGroup();
        var role = CreateRole();

        group.AssignRole(role);
        group.ClearDomainEvents();
        var versionBefore = group.OptimisticVersion;

        group.AssignRole(role);

        Assert.Single(group.Roles);
        Assert.Equal(versionBefore, group.OptimisticVersion);
        Assert.Empty(group.DomainEvents);
    }

    [Fact]
    public void RemoveRole_NotPresent_IsIdempotent()
    {
        var group = CreateGroup();
        group.ClearDomainEvents();
        var role = CreateRole();

        group.RemoveRole(role);

        Assert.Empty(group.Roles);
        Assert.Empty(group.DomainEvents);
    }

    [Fact]
    public void RemoveRole_RemovesRelationAndPublishesEvent()
    {
        var group = CreateGroup();
        var role = CreateRole();
        group.AssignRole(role);
        group.ClearDomainEvents();

        group.RemoveRole(role);

        var relation = Assert.Single(group.Roles);
        Assert.True(relation.IsDeleted);
        Assert.Single(group.DomainEvents.OfType<UserGroupRolesChangedEvent>());
    }

    [Fact]
    public void Disable_Enable_TransitionAndPublishChangedEvents()
    {
        var group = CreateGroup();

        group.Disable();
        Assert.Equal(UserGroupStatus.Disabled, group.Status);
        Assert.Single(group.DomainEvents.OfType<UserGroupChangedEvent>());

        group.ClearDomainEvents();
        var versionBefore = group.OptimisticVersion;
        group.Disable();
        Assert.Equal(versionBefore, group.OptimisticVersion);
        Assert.Empty(group.DomainEvents);

        group.Enable();
        Assert.Equal(UserGroupStatus.Active, group.Status);
        Assert.Single(group.DomainEvents.OfType<UserGroupChangedEvent>());
    }

    [Fact]
    public void Delete_MarksDeletedAndRejectsFurtherChanges()
    {
        var group = CreateGroup();
        var user = CreateUser();

        group.MarkDeleted();
        Assert.True(group.IsDeleted);

        Assert.Throws<BusinessException>(() => group.AssignMember(user));
        Assert.Throws<BusinessException>(() => group.ChangeProfile("x", null));
    }

    /// <summary>
    /// 禁止嵌套(§29A.2):成员关系只承载用户引用(UserId),聚合不存在接收 UserGroup 作为成员的方法,
    /// 数据库层另有 user_id → identity_user 复合外键保证(见 Infrastructure 测试)。
    /// </summary>
    [Fact]
    public void NoNesting_ByDesign_MembershipReferencesUserOnly()
    {
        var group = CreateGroup();
        var user = CreateUser();
        group.AssignMember(user);

        var relation = Assert.Single(group.Memberships);
        // 成员引用是用户主键;不存在 UserGroupId 指向另一用户组的成员语义(成员侧为 UserId)。
        Assert.NotEqual(group.Id, relation.UserId);
        Assert.Equal(user.Id, relation.UserId);
    }

    [Fact]
    public void DeleteForTombstone_SoftDeletesRelationsAndMarksDeleted()
    {
        var group = CreateGroup();
        var user = CreateUser();
        var role = CreateRole();
        group.AssignMember(user);
        group.AssignRole(role);

        group.DeleteForTombstone();

        Assert.True(group.IsDeleted);
        Assert.All(group.Memberships, m => Assert.True(m.IsDeleted));
        Assert.All(group.Roles, r => Assert.True(r.IsDeleted));
    }

    [Fact]
    public void DeleteForTombstone_OnFrozenGroup_Throws()
    {
        var group = CreateGroup();
        group.Freeze();

        Assert.Throws<BusinessException>(() => group.DeleteForTombstone());
        Assert.False(group.IsDeleted);
    }

    [Fact]
    public void RestoreTombstone_ClearsDeletedAndStaysDisabled_RelationsNotRestored()
    {
        var group = CreateGroup();
        var user = CreateUser();
        var role = CreateRole();
        group.AssignMember(user);
        group.AssignRole(role);
        group.DeleteForTombstone();

        group.RestoreTombstone();

        Assert.False(group.IsDeleted);
        Assert.Equal(UserGroupStatus.Disabled, group.Status);
        // 成员/角色关系保持软删,不自动恢复(§29A.3)
        Assert.All(group.Memberships, m => Assert.True(m.IsDeleted));
        Assert.All(group.Roles, r => Assert.True(r.IsDeleted));
    }

    [Fact]
    public void RestoreTombstone_OnActiveGroup_Throws()
    {
        var group = CreateGroup();

        Assert.Throws<BusinessException>(() => group.RestoreTombstone());
        Assert.False(group.IsDeleted);
    }
}
