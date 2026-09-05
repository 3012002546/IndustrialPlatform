using System.Globalization;
using System.Text;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.SharedKernel.Entities;

namespace IndustrialPlatform.ReferenceData.Domain.CodingRule;

public enum CodingResetPolicy { Never, Yearly, Monthly, Daily }

public enum CodingTemplateSegmentKind { FixedText, Year, Month, Day, Tenant, Factory, Sequence }

public sealed record CodingTemplateSegment(CodingTemplateSegmentKind Kind, string Text, int? Width = null);

public sealed record CodingRenderResult(
    string Code,
    string PeriodKey,
    int SequenceWidth,
    IReadOnlyList<CodingTemplateSegment> Segments);

public sealed class CodingRuleDefinition : AggregateRoot
{
    private const int MaximumTemplateLength = 1024;
    private const int MaximumCodeLength = 128;

    public string NId { get; private set; }
    public string Name { get; private set; }
    public string TargetEntityNId { get; private set; } = string.Empty;
    public string Template { get; private set; } = string.Empty;
    public CodingResetPolicy ResetPolicy { get; private set; } = CodingResetPolicy.Never;
    public ReferenceScopeType ScopeType { get; private set; }
    public string? TenantNId { get; private set; }
    public int Revision { get; private set; } = 1;
    public PublicationStatus Status { get; private set; } = PublicationStatus.Draft;
    public int? SourceRevision { get; private set; }
    public DateTimeOffset? PublishedOn { get; private set; }
    public string? PublishedBy { get; private set; }

    public CodingRuleDefinition(
        string nId,
        string name,
        ReferenceScopeType scopeType,
        string? tenantNId,
        string? scopeId)
    {
        ReferenceValidation.Scope(scopeType, tenantNId, scopeId);
        NId = ReferenceValidation.NId(nId);
        Name = ReferenceValidation.Name(name);
        ScopeType = scopeType;
        TenantNId = tenantNId;
    }

    public void Update(string name, string targetEntityNId, string template, CodingResetPolicy resetPolicy)
    {
        EnsureDraft();
        var nextName = ReferenceValidation.Name(name);
        var nextTarget = ReferenceValidation.NId(targetEntityNId);
        var nextTemplate = ValidateTemplateText(template);
        if (!Enum.IsDefined(resetPolicy)) throw TemplateInvalid("resetPolicy");
        if (Name == nextName && TargetEntityNId == nextTarget && Template == nextTemplate && ResetPolicy == resetPolicy)
            return;

        Name = nextName;
        TargetEntityNId = nextTarget;
        Template = nextTemplate;
        ResetPolicy = resetPolicy;
        Touch();
    }

    public void CheckVersion(long expectedOptimisticVersion, Guid expectedConcurrencyVersion)
    {
        if (OptimisticVersion != expectedOptimisticVersion || ConcurrencyVersion != expectedConcurrencyVersion)
            throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
    }

    public void CheckPublication()
    {
        EnsureDraft();
        var parsed = CodingTemplate.Parse(Template);
        parsed.ValidateResetPolicy(ResetPolicy);
        if (parsed.RequiresFactory)
            throw new ReferenceDataException("REF-SCOPE-FACTORY-NOT-READY", 409, "template");
        if (parsed.MinimumRenderedLength(ScopeType == ReferenceScopeType.Tenant ? TenantNId!.Length : 1) > MaximumCodeLength)
            throw TemplateInvalid("template");
    }

    public void Publish(string userNId)
    {
        if (string.IsNullOrWhiteSpace(userNId))
            throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "userNId");
        CheckPublication();
        Status = PublicationStatus.Published;
        PublishedOn = DateTimeOffset.UtcNow;
        PublishedBy = userNId;
        Touch();
    }

    public void Supersede()
    {
        EnsureMutable();
        CheckCapacity();
        if (Status != PublicationStatus.Published) throw new ReferenceDataException("REF-INVALID-STATE", 409);
        Status = PublicationStatus.Superseded;
        Touch();
    }

    public void Disable()
    {
        EnsureMutable();
        CheckCapacity();
        if (Status is not (PublicationStatus.Draft or PublicationStatus.Published))
            throw new ReferenceDataException("REF-INVALID-STATE", 409);
        Status = PublicationStatus.Disabled;
        Touch();
    }

    public CodingRuleDefinition Clone(int revision)
    {
        EnsureMutable();
        if (PublishedOn is null || revision <= Revision)
            throw new ReferenceDataException("REF-INVALID-STATE", 409);
        var clone = new CodingRuleDefinition(NId, Name, ScopeType, TenantNId, null)
        {
            Revision = revision,
            SourceRevision = Revision,
        };
        clone.Update(Name, TargetEntityNId, Template, ResetPolicy);
        return clone;
    }

    public new void Freeze() { if (!IsFrozen) CheckCapacity(); base.Freeze(); }
    public new void Unfreeze() { if (IsFrozen) CheckCapacity(); base.Unfreeze(); }
    public new void Lock() { if (!IsLocked) CheckCapacity(); base.Lock(); }
    public new void Unlock() { if (IsLocked) CheckCapacity(); base.Unlock(); }
    public new void MarkDeleted() { if (!IsDeleted) CheckCapacity(); base.MarkDeleted(); }
    public new void Restore() { if (IsDeleted) CheckCapacity(); base.Restore(); }

    public CodingRenderResult Render(DateTimeOffset at, string tenantNId, string? factoryId, long sequence)
    {
        if (sequence < 1) throw TemplateInvalid("sequence");
        if (string.IsNullOrWhiteSpace(tenantNId) || tenantNId.Length > 128 || tenantNId.Any(char.IsControl))
            throw new ReferenceDataException("REF-SCOPE-INVALID");

        var parsed = CodingTemplate.Parse(Template);
        parsed.ValidateResetPolicy(ResetPolicy);
        if (factoryId is not null || parsed.RequiresFactory)
            throw new ReferenceDataException("REF-SCOPE-FACTORY-NOT-READY", 409, "factoryId");
        var utc = at.ToUniversalTime();
        var code = parsed.Render(utc, tenantNId, factoryId, sequence);
        if (code.Length > MaximumCodeLength) throw TemplateInvalid("template");
        return new(code, PeriodKey(utc), parsed.SequenceWidth, parsed.Segments);
    }

    public string ContextValue(string tenantNId, string? factoryId)
    {
        var parsed = CodingTemplate.Parse(Template);
        if (factoryId is not null || parsed.RequiresFactory)
            throw new ReferenceDataException("REF-SCOPE-FACTORY-NOT-READY", 409, "factoryId");
        return parsed.RequiresTenant ? tenantNId : string.Empty;
    }

    public string PeriodKey(DateTimeOffset at)
    {
        var utc = at.ToUniversalTime();
        return ResetPolicy switch
        {
            CodingResetPolicy.Never => "ALL",
            CodingResetPolicy.Yearly => utc.ToString("yyyy", CultureInfo.InvariantCulture),
            CodingResetPolicy.Monthly => utc.ToString("yyyyMM", CultureInfo.InvariantCulture),
            CodingResetPolicy.Daily => utc.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            _ => throw TemplateInvalid("resetPolicy"),
        };
    }

    public static CodingRuleDefinition Restore(CodingRuleState state) => new(
        state.NId,
        state.Name,
        state.ScopeType,
        state.TenantNId,
        null)
    {
        Id = state.Id,
        TargetEntityNId = state.TargetEntityNId,
        Template = state.Template,
        ResetPolicy = state.ResetPolicy,
        Revision = state.Revision,
        Status = state.Status,
        SourceRevision = state.SourceRevision,
        PublishedOn = state.PublishedOn,
        PublishedBy = state.PublishedBy,
        IsFrozen = state.IsFrozen,
        IsLocked = state.IsLocked,
        IsDeleted = state.IsDeleted,
        CreatedOn = state.CreatedOn,
        LastUpdatedOn = state.LastUpdatedOn,
        OptimisticVersion = state.OptimisticVersion,
        ConcurrencyVersion = state.ConcurrencyVersion,
    };

    private static string ValidateTemplateText(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumTemplateLength || value.Any(char.IsControl))
            throw TemplateInvalid("template");
        return value;
    }

    private void EnsureDraft()
    {
        EnsureMutable();
        CheckCapacity();
        if (Status != PublicationStatus.Draft) throw new ReferenceDataException("REF-INVALID-STATE", 409);
    }

    private void EnsureMutable()
    {
        if (IsDeleted || IsFrozen || IsLocked) throw new ReferenceDataException("REF-INVALID-STATE", 409);
    }

    private void CheckCapacity()
    {
        if (OptimisticVersion == long.MaxValue) throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
    }

    internal static ReferenceDataException TemplateInvalid(string field) =>
        new("REF-CODING-TEMPLATE-INVALID", 422, field);
}

public sealed record CodingRuleState(
    Guid Id,
    string NId,
    string Name,
    string TargetEntityNId,
    string Template,
    CodingResetPolicy ResetPolicy,
    ReferenceScopeType ScopeType,
    string? TenantNId,
    int Revision,
    PublicationStatus Status,
    int? SourceRevision,
    DateTimeOffset? PublishedOn,
    string? PublishedBy,
    bool IsFrozen,
    bool IsLocked,
    bool IsDeleted,
    DateTimeOffset CreatedOn,
    DateTimeOffset LastUpdatedOn,
    long OptimisticVersion,
    Guid ConcurrencyVersion);

internal sealed class CodingTemplate
{
    private readonly IReadOnlyList<CodingTemplateSegment> segments;

    private CodingTemplate(IReadOnlyList<CodingTemplateSegment> segments, int sequenceWidth)
    {
        this.segments = Array.AsReadOnly(segments.ToArray());
        SequenceWidth = sequenceWidth;
    }

    public IReadOnlyList<CodingTemplateSegment> Segments => segments;
    public int SequenceWidth { get; }
    public bool RequiresTenant => segments.Any(segment => segment.Kind == CodingTemplateSegmentKind.Tenant);
    public bool RequiresFactory => segments.Any(segment => segment.Kind == CodingTemplateSegmentKind.Factory);

    public static CodingTemplate Parse(string template)
    {
        var segments = new List<CodingTemplateSegment>();
        var sequenceWidth = 0;
        var fixedStart = 0;
        for (var index = 0; index < template.Length; index++)
        {
            if (template[index] == '}') throw CodingRuleDefinition.TemplateInvalid("template");
            if (template[index] != '{') continue;
            if (index > fixedStart)
                segments.Add(new(CodingTemplateSegmentKind.FixedText, template[fixedStart..index]));
            var close = template.IndexOf('}', index + 1);
            if (close < 0 || template.AsSpan(index + 1, close - index - 1).Contains('{'))
                throw CodingRuleDefinition.TemplateInvalid("template");
            var token = template[(index + 1)..close];
            var segment = Token(token);
            if (segment.Kind == CodingTemplateSegmentKind.Sequence)
            {
                if (sequenceWidth != 0) throw CodingRuleDefinition.TemplateInvalid("template");
                sequenceWidth = segment.Width!.Value;
            }
            segments.Add(segment);
            index = close;
            fixedStart = close + 1;
        }
        if (fixedStart < template.Length)
            segments.Add(new(CodingTemplateSegmentKind.FixedText, template[fixedStart..]));
        if (sequenceWidth == 0) throw CodingRuleDefinition.TemplateInvalid("template");
        return new(segments, sequenceWidth);
    }

    public void ValidateResetPolicy(CodingResetPolicy policy)
    {
        bool Has(CodingTemplateSegmentKind kind) => segments.Any(segment => segment.Kind == kind);
        var valid = policy switch
        {
            CodingResetPolicy.Never => true,
            CodingResetPolicy.Yearly => Has(CodingTemplateSegmentKind.Year),
            CodingResetPolicy.Monthly => Has(CodingTemplateSegmentKind.Year) && Has(CodingTemplateSegmentKind.Month),
            CodingResetPolicy.Daily => Has(CodingTemplateSegmentKind.Year) && Has(CodingTemplateSegmentKind.Month)
                && Has(CodingTemplateSegmentKind.Day),
            _ => false,
        };
        if (!valid) throw CodingRuleDefinition.TemplateInvalid("template");
    }

    public int MinimumRenderedLength(int tenantLength) => segments.Sum(segment => segment.Kind switch
    {
        CodingTemplateSegmentKind.FixedText => segment.Text.Length,
        CodingTemplateSegmentKind.Year => 4,
        CodingTemplateSegmentKind.Month or CodingTemplateSegmentKind.Day => 2,
        CodingTemplateSegmentKind.Tenant => tenantLength,
        CodingTemplateSegmentKind.Factory => 1,
        CodingTemplateSegmentKind.Sequence => segment.Width!.Value,
        _ => 0,
    });

    public string Render(DateTimeOffset utc, string tenantNId, string? factoryId, long sequence)
    {
        var builder = new StringBuilder();
        foreach (var segment in segments)
        {
            builder.Append(segment.Kind switch
            {
                CodingTemplateSegmentKind.FixedText => segment.Text,
                CodingTemplateSegmentKind.Year => utc.ToString("yyyy", CultureInfo.InvariantCulture),
                CodingTemplateSegmentKind.Month => utc.ToString("MM", CultureInfo.InvariantCulture),
                CodingTemplateSegmentKind.Day => utc.ToString("dd", CultureInfo.InvariantCulture),
                CodingTemplateSegmentKind.Tenant => tenantNId,
                CodingTemplateSegmentKind.Factory => factoryId!,
                CodingTemplateSegmentKind.Sequence => sequence.ToString($"D{segment.Width}", CultureInfo.InvariantCulture),
                _ => throw CodingRuleDefinition.TemplateInvalid("template"),
            });
        }
        return builder.ToString();
    }

    private static CodingTemplateSegment Token(string token) => token switch
    {
        "YYYY" => new(CodingTemplateSegmentKind.Year, "{YYYY}"),
        "MM" => new(CodingTemplateSegmentKind.Month, "{MM}"),
        "DD" => new(CodingTemplateSegmentKind.Day, "{DD}"),
        "TENANT" => new(CodingTemplateSegmentKind.Tenant, "{TENANT}"),
        "FACTORY" => new(CodingTemplateSegmentKind.Factory, "{FACTORY}"),
        _ when token.StartsWith("SEQ:", StringComparison.Ordinal)
            && int.TryParse(token.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var width)
            && width is >= 1 and <= 12
            && token.AsSpan(4).SequenceEqual(width.ToString(CultureInfo.InvariantCulture))
            => new(CodingTemplateSegmentKind.Sequence, $"{{{token}}}", width),
        _ => throw CodingRuleDefinition.TemplateInvalid("template"),
    };
}
