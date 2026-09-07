using System.Text;
using IndustrialPlatform.SystemData.Domain.Auditing;
using IndustrialPlatform.SystemData.Domain.Files;
using IndustrialPlatform.SystemData.Domain.Notifications;

namespace IndustrialPlatform.SystemData.Tests;

public sealed class Pf04RulesTests
{
    [Fact]
    public void SampleFingerprint_UsesDeterministicWindowedSha256Input()
    {
        var content = Encoding.UTF8.GetBytes(new string('x', 200_000));

        var first = FileUploadRules.ComputeSampleFingerprint(content);
        var second = FileUploadRules.ComputeSampleFingerprint(content);

        Assert.Equal(first, second);
        Assert.StartsWith("sample-v1:", first, StringComparison.Ordinal);
        Assert.NotEqual(first, FileUploadRules.ComputeSampleFingerprint(Encoding.UTF8.GetBytes("different")));
    }

    [Fact]
    public void AuditPayload_IsRedactedAndCanonicalized()
    {
        var result = AuditPayloadRules.Sanitize("{\"z\":1,\"token\":\"secret\",\"a\":{\"password\":\"p\"}}", out var hash);

        Assert.Equal("{\"a\":{\"password\":\"[REDACTED]\"},\"token\":\"[REDACTED]\",\"z\":1}", result);
        Assert.Equal(64, hash.Length);
        Assert.DoesNotContain("secret", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditIdempotencyHash_ChangesWhenFactMetadataChanges()
    {
        var first = AuditPayloadRules.ComputeIdempotencyHash("systemdata", "evt-1", DateTimeOffset.UnixEpoch, "u-1", "create", "File", "f-1", "{}", "trace-1", "Info", null, null);
        var retry = AuditPayloadRules.ComputeIdempotencyHash("systemdata", "evt-1", DateTimeOffset.UnixEpoch, "u-1", "create", "File", "f-1", "{}", "trace-1", "Info", null, null);
        var changed = AuditPayloadRules.ComputeIdempotencyHash("systemdata", "evt-1", DateTimeOffset.UnixEpoch, "u-1", "delete", "File", "f-1", "{}", "trace-1", "Info", null, null);

        Assert.Equal(first, retry);
        Assert.NotEqual(first, changed);
    }

    [Fact]
    public void NotificationContent_RejectsScriptAndNormalizesText()
    {
        Assert.Equal("公告", NotificationContentRules.Title(" 公告 "));
        Assert.Throws<ArgumentException>(() => NotificationContentRules.Body("<script>alert(1)</script>"));
    }

    [Fact]
    public void NotificationTargets_RejectEmptyAndOverLimitLists()
    {
        Assert.Throws<ArgumentException>(() => NotificationTargetRules.Normalize([]));
        Assert.Throws<ArgumentException>(() => NotificationTargetRules.Normalize(Enumerable.Range(1, 1001).Select(i => $"u-{i}")));
        Assert.Equal(["u-1", "u-2"], NotificationTargetRules.Normalize([" u-1 ", "u-1", "u-2"]));
    }
}
