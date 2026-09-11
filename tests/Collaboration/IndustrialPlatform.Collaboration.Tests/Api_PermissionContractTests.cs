using System.Reflection;
using IndustrialPlatform.Collaboration.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Api_PermissionContractTests
{
    [Theory]
    [InlineData(nameof(CollaborationController.MarkRead), "permission:collaboration.messaging.read-cursor.update")]
    [InlineData(nameof(CollaborationController.Hide), "permission:collaboration.messaging.conversation.hide")]
    [InlineData(nameof(CollaborationController.Restore), "permission:collaboration.messaging.conversation.restore")]
    [InlineData(nameof(CollaborationController.SetPresence), "permission:collaboration.presence.write")]
    [InlineData(nameof(CollaborationController.GetPresence), "permission:collaboration.presence.read")]
    [InlineData(nameof(CollaborationController.SetPersonalMessageVisibility), "permission:collaboration.messaging.write")]
    public void MessagingMutation_UsesItsDedicatedPermission(string methodName, string expectedPolicy)
    {
        var method = typeof(CollaborationController).GetMethod(methodName)!;
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal(expectedPolicy, authorize?.Policy);
    }

    [Fact]
    public void ComplianceActions_HaveNoGenericBodyActionRoute()
    {
        var routes = typeof(CollaborationComplianceController).GetMethods()
            .SelectMany(method => method.GetCustomAttributes<HttpPostAttribute>());

        Assert.DoesNotContain(routes, attribute => attribute.Template == "legal-holds/{holdCaseNId}/actions");
    }
}
