using IndustrialPlatform.Identity.Domain.Identities;
using IndustrialPlatform.Identity.Domain.Permissions;

namespace IndustrialPlatform.Identity.Domain.Tests;

/// <summary>
/// 系统权限目录测试:第一批 Permission.NId 数量、内容与格式(§9.2)。
/// </summary>
public sealed class PermissionCatalogTests
{
    private static readonly string[] ExpectedFirstBatchNIds =
    [
        "identity.user.view",
        "identity.user.create",
        "identity.user.update",
        "identity.user.status",
        "identity.user.assign-role",
        "identity.user.delete",
        "identity.user.restore",
        "identity.role.view",
        "identity.role.create",
        "identity.role.update",
        "identity.role.assign-permission",
        "identity.user-group.delete",
        "identity.user-group.restore",
        "identity.user-group.view",
        "identity.user-group.create",
        "identity.user-group.update",
        "identity.user-group.status",
        "identity.user-group.assign-member",
        "identity.user-group.assign-role",
        "identity.user.reset-password",
        "identity.permission.view",
        "identity.audit.login.view",
        "identity.sso.view",
        "identity.sso.manage",
        "identity.sso.test",
        "identity.session.view",
        "identity.session.revoke",
        "platform.home.view",
        "platform.operation.view",
        "platform.pda.view",
        "platform.mobile.view",
        "identity.bootstrap.view",
        "identity.bootstrap.recover",
        "collaboration.messaging.read",
        "collaboration.messaging.conversation.start",
        "collaboration.messaging.write",
        "collaboration.messaging.read-cursor.update",
        "collaboration.messaging.conversation.hide",
        "collaboration.messaging.conversation.restore",
        "collaboration.messaging.retract",
        "collaboration.messaging.attachment.send",
        "collaboration.messaging.attachment.download",
        "collaboration.compliance.read",
        "collaboration.compliance.view",
        "collaboration.compliance.read-original",
        "collaboration.compliance.dispose",
        "collaboration.compliance.export.request",
        "collaboration.compliance.export.download",
        "collaboration.compliance.export.approve",
        "collaboration.compliance.legal-hold.create",
        "collaboration.compliance.legal-hold.review",
        "collaboration.compliance.legal-hold.release",
        "collaboration.compliance.legal-hold.release.approve",
        "collaboration.compliance.retention.manage",
        "collaboration.compliance.retention.update",
        "collaboration.presence.connect",
        "collaboration.presence.read",
        "collaboration.presence.write",
        "systemdata.organization.view",
        "systemdata.organization.create",
        "systemdata.organization.update",
        "systemdata.organization.move",
        "systemdata.organization.status",
        "systemdata.position.view",
        "systemdata.position.create",
        "systemdata.position.update",
        "systemdata.position.status",
        "systemdata.assignment.view",
        "systemdata.assignment.manage",
        "systemdata.resource.view",
        "systemdata.navigation.view",
        "systemdata.navigation.manage",
        "systemdata.navigation.publish",
        "systemdata.navigation.rollback",
        "systemdata.feature.view",
        "systemdata.feature.manage",
        "systemdata.service-catalog.view",
        "systemdata.service-catalog.manage",
        "systemdata.theme-policy.view",
        "systemdata.theme-policy.manage",
        "systemdata.database-orchestration.view",
        "systemdata.database-orchestration.register",
        "systemdata.database-orchestration.plan",
        "systemdata.database-orchestration.apply",
        "systemdata.database-orchestration.approve",
        "systemdata.database-orchestration.backup",
        "systemdata.database-orchestration.cancel",
        "systemdata.service-initialization.view",
        "systemdata.service-initialization.register",
        "systemdata.service-initialization.plan",
        "systemdata.service-initialization.apply",
        "systemdata.service-initialization.approve",
        "systemdata.service-initialization.backup",
        "systemdata.service-initialization.cancel",
        "systemdata.file.upload",
        "systemdata.file.read",
        "systemdata.file.download",
        "systemdata.file.manage",
        "systemdata.file.delete",
        "systemdata.notification.inbox.read",
        "systemdata.notification.announcement.read",
        "systemdata.notification.announcement.manage",
        "systemdata.notification.announcement.publish",
        "systemdata.notification.system.send",
        "systemdata.audit.write",
        "systemdata.audit.read",
        "systemdata.audit.export",
        "systemdata.audit.retention.manage",
        "referencedata.dictionary.view",
        "referencedata.dictionary.create",
        "referencedata.dictionary.update",
        "referencedata.dictionary.publish",
        "referencedata.dictionary.disable",
        "referencedata.parameter.view",
        "referencedata.parameter.create",
        "referencedata.parameter.update",
        "referencedata.parameter.disable",
        "referencedata.parameter.read-secret-reference",
        "referencedata.dynamic-property.view",
        "referencedata.dynamic-property.create",
        "referencedata.dynamic-property.update",
        "referencedata.dynamic-property.publish",
        "referencedata.dynamic-property.disable",
        "referencedata.unit-of-measure.view",
        "referencedata.unit-of-measure.create",
        "referencedata.unit-of-measure.update",
        "referencedata.unit-of-measure.publish",
        "referencedata.unit-of-measure.disable",
        "referencedata.metadata.view",
        "referencedata.metadata.create",
        "referencedata.metadata.update",
        "referencedata.metadata.publish",
        "referencedata.metadata.disable",
        "referencedata.coding-rule.view",
        "referencedata.coding-rule.create",
        "referencedata.coding-rule.update",
        "referencedata.coding-rule.publish",
        "referencedata.coding-rule.disable",
        "referencedata.coding-rule.preview",
        "referencedata.coding-rule.generate",
        "referencedata.state-machine.view",
        "referencedata.state-machine.create",
        "referencedata.state-machine.update",
        "referencedata.state-machine.publish",
        "referencedata.state-machine.disable",
        "referencedata.platform.manage",
    ];

    [Fact]
    public void FirstBatch_HasIdentityAndSystemDataPermissions()
    {
        Assert.Equal(146, PermissionCatalog.FirstBatchNIds.Count);
    }

    [Fact]
    public void FirstBatch_MatchesDocumentedCatalog()
    {
        Assert.Equal(ExpectedFirstBatchNIds, PermissionCatalog.FirstBatchNIds);
    }

    [Fact]
    public void FirstBatch_AllPassNIdValidation()
    {
        foreach (var nId in PermissionCatalog.FirstBatchNIds)
        {
            NId.Create(nId);
        }
    }

    [Fact]
    public void FirstBatch_HasNoDuplicates()
    {
        Assert.Equal(
            PermissionCatalog.FirstBatchNIds.Count,
            PermissionCatalog.FirstBatchNIds.Distinct().Count());
    }
}
