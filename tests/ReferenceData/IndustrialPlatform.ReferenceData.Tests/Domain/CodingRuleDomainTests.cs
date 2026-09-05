using IndustrialPlatform.ReferenceData.Domain.CodingRule;
using IndustrialPlatform.ReferenceData.Domain.Common;

namespace IndustrialPlatform.ReferenceData.Tests.Domain;

public sealed class CodingRuleDomainTests
{
    [Fact]
    public void Template_preserves_fixed_and_token_order_and_renders_utc_date_and_sequence()
    {
        var rule = Rule("LOT-{TENANT}-{YYYY}{MM}{DD}-{SEQ:4}", CodingResetPolicy.Daily);
        rule.CheckPublication();

        var rendered = rule.Render(new DateTimeOffset(2026, 9, 5, 23, 59, 58, TimeSpan.FromHours(8)),
            "TENANT-A", null, 7);

        Assert.Equal("LOT-TENANT-A-20260905-0007", rendered.Code);
        Assert.Equal("20260905", rendered.PeriodKey);
        Assert.Equal(4, rendered.SequenceWidth);
        Assert.Equal([
            CodingTemplateSegmentKind.FixedText,
            CodingTemplateSegmentKind.Tenant,
            CodingTemplateSegmentKind.FixedText,
            CodingTemplateSegmentKind.Year,
            CodingTemplateSegmentKind.Month,
            CodingTemplateSegmentKind.Day,
            CodingTemplateSegmentKind.FixedText,
            CodingTemplateSegmentKind.Sequence,
        ], rendered.Segments.Select(segment => segment.Kind).ToArray());
    }

    [Theory]
    [InlineData("LOT", CodingResetPolicy.Never)]
    [InlineData("{UNKNOWN}-{SEQ:4}", CodingResetPolicy.Never)]
    [InlineData("{SEQ:2}-{SEQ:3}", CodingResetPolicy.Never)]
    [InlineData("{SEQ:0}", CodingResetPolicy.Never)]
    [InlineData("{SEQ:13}", CodingResetPolicy.Never)]
    [InlineData("{SEQ:01}", CodingResetPolicy.Never)]
    [InlineData("{{SEQ:4}", CodingResetPolicy.Never)]
    [InlineData("{SEQ:4}}", CodingResetPolicy.Never)]
    [InlineData("{SEQ:4}", CodingResetPolicy.Yearly)]
    [InlineData("{YYYY}-{SEQ:4}", CodingResetPolicy.Monthly)]
    [InlineData("{YYYY}{MM}-{SEQ:4}", CodingResetPolicy.Daily)]
    public void Publication_rejects_invalid_tokens_sequence_shape_and_reset_period(
        string template, CodingResetPolicy policy)
    {
        var error = Assert.Throws<ReferenceDataException>(() => Rule(template, policy).CheckPublication());

        Assert.Equal("REF-CODING-TEMPLATE-INVALID", error.ErrorCode);
        Assert.Equal(422, error.Status);
    }

    [Fact]
    public void Factory_token_is_rejected_until_factory_scope_validation_exists()
    {
        var error = Assert.Throws<ReferenceDataException>(() =>
            Rule("{FACTORY}-{SEQ:4}", CodingResetPolicy.Never).CheckPublication());

        Assert.Equal("REF-SCOPE-FACTORY-NOT-READY", error.ErrorCode);
        Assert.Equal(409, error.Status);
    }

    [Fact]
    public void Render_rejects_missing_context_and_codes_longer_than_128()
    {
        var missing = Assert.Throws<ReferenceDataException>(() =>
            Rule("{TENANT}-{SEQ:1}", CodingResetPolicy.Never).Render(DateTimeOffset.UtcNow, "", null, 1));
        var tooLong = Assert.Throws<ReferenceDataException>(() =>
            Rule(new string('X', 128) + "{SEQ:1}", CodingResetPolicy.Never)
                .Render(DateTimeOffset.UtcNow, "TENANT-A", null, 1));

        Assert.Equal("REF-SCOPE-INVALID", missing.ErrorCode);
        Assert.Equal("REF-CODING-TEMPLATE-INVALID", tooLong.ErrorCode);
    }

    [Fact]
    public void Published_revision_is_immutable_and_clone_has_new_identity_and_independent_versions()
    {
        var published = Rule("LOT-{SEQ:4}", CodingResetPolicy.Never);
        published.Publish("USER-A");

        Assert.Throws<ReferenceDataException>(() =>
            published.Update("Changed", "WorkOrder", "NEW-{SEQ:4}", CodingResetPolicy.Never));
        var clone = published.Clone(2);

        Assert.NotEqual(published.Id, clone.Id);
        Assert.NotEqual(published.ConcurrencyVersion, clone.ConcurrencyVersion);
        Assert.Equal(2, clone.Revision);
        Assert.Equal(1, clone.SourceRevision);
        Assert.Equal(PublicationStatus.Draft, clone.Status);
        Assert.Null(clone.PublishedOn);
    }

    private static CodingRuleDefinition Rule(string template, CodingResetPolicy resetPolicy)
    {
        var rule = new CodingRuleDefinition("LotRule", "Lot rule", ReferenceScopeType.Tenant, "TENANT-A", null);
        rule.Update("Lot rule", "WorkOrder", template, resetPolicy);
        return rule;
    }
}
