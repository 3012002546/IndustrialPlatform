using IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;

namespace IndustrialPlatform.SystemData.Tests;

public sealed class Pf04MigrationTests
{
    [Fact]
    public void MigrationCatalog_ContainsCoreFileNotificationAuditTables()
    {
        var descriptions = SystemDataSchemaMigrations.All
            .Select(step => step.Description)
            .ToArray();

        Assert.Contains("system_file_upload_session", descriptions);
        Assert.Contains("system_file_object", descriptions);
        Assert.Contains("system_file_scan_attempt", descriptions);
        Assert.Contains("system_file_reference_grant", descriptions);
        Assert.Contains("system_notification_announcement", descriptions);
        Assert.Contains("system_notification_message", descriptions);
        Assert.Contains("system_notification_inbox_delivery", descriptions);
        Assert.Contains("system_audit_fact", descriptions);
        Assert.Contains("system_audit_lifecycle", descriptions);
        Assert.Contains("system_audit_ingress_failure", descriptions);
        Assert.Contains("system_audit_outbox", descriptions);
        Assert.Contains("system_audit_lifecycle legal hold", descriptions);
    }
}
