using IndustrialPlatform.Collaboration.Application;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Security_ComplianceCommandCanonicalizerTests
{
    [Fact]
    public void Every_final_command_field_is_hash_bound()
    {
        var baseline = new ComplianceCommandSnapshot(
            "retention.update", "REQ-1", "TARGET-1", "aabb", "reason", "CASE-1", "keyword", true,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            "message", "MSG-1", ["textContent"],
            365, 730, 1095, true, 24, 48, 3, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

        var variants = new[]
        {
            baseline with { Action = "compliance.dispose" },
            baseline with { RequestNId = "REQ-2" },
            baseline with { TargetNId = "TARGET-2" },
            baseline with { ScopeChecksum = "ccdd" },
            baseline with { Reason = "other" },
            baseline with { CaseReference = "CASE-2" },
            baseline with { Keyword = "other" },
            baseline with { ReadOriginal = false },
            baseline with { SubjectType = "attachment" },
            baseline with { SubjectNId = "MSG-2" },
            baseline with { ExpiresOn = baseline.ExpiresOn.GetValueOrDefault().AddDays(1) },
            baseline with { Fields = ["messageNId"] },
            baseline with { MessageRetentionDays = 366 },
            baseline with { AttachmentRetentionDays = 731 },
            baseline with { AuditRetentionDays = 1096 },
            baseline with { Enabled = false },
            baseline with { ExportRetentionHours = 25 },
            baseline with { ReviewDueHours = 49 },
            baseline with { ExpectedOptimisticVersion = 4 },
            baseline with { ExpectedConcurrencyVersion = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb") },
        };

        var hash = ComplianceCommandCanonicalizer.Hash(baseline);

        Assert.All(variants, variant => Assert.NotEqual(hash, ComplianceCommandCanonicalizer.Hash(variant)));
    }
}
