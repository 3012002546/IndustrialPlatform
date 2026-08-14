using IndustrialPlatform.SharedKernel.Entities;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;

/// <summary>
/// 不可变计划步骤(DatabaseProvisionPlan 子实体)。包含稳定 StepKind、顺序、
/// 输入/前置/后置条件摘要与风险等级;不保存 Secret、原始连接串或完整 SQL(05 方案 §7.1.3)。
/// </summary>
public sealed class DatabasePlanStep : Entity
{
    /// <summary>StepKind 最大长度。</summary>
    public const int StepKindMaxLength = 64;

    /// <summary>顺序(1 起),计划内唯一。</summary>
    public int Sequence { get; }

    /// <summary>稳定步骤标识,如 <c>identity-check</c>、<c>version-check</c>。</summary>
    public string StepKind { get; }

    /// <summary>输入摘要(不含敏感信息)。</summary>
    public string? InputSummary { get; }

    /// <summary>前置条件摘要。</summary>
    public string? PreconditionSummary { get; }

    /// <summary>后置条件摘要。</summary>
    public string? PostconditionSummary { get; }

    /// <summary>步骤风险等级。</summary>
    public RiskLevel RiskLevel { get; }

    /// <summary>由计划聚合在生成时创建。</summary>
    internal DatabasePlanStep(
        int sequence,
        string stepKind,
        string? inputSummary,
        string? preconditionSummary,
        string? postconditionSummary,
        RiskLevel riskLevel)
    {
        if (sequence < 1)
        {
            throw new ValidationException("计划步骤顺序必须从 1 开始。");
        }

        StepKind = RequireTrimmedNonEmpty(
            stepKind,
            "计划步骤标识不能为空。",
            StepKindMaxLength,
            $"计划步骤标识长度不能超过 {StepKindMaxLength} 个字符。");
        Sequence = sequence;
        InputSummary = TrimOrNull(inputSummary, SummaryMaxLength);
        PreconditionSummary = TrimOrNull(preconditionSummary, SummaryMaxLength);
        PostconditionSummary = TrimOrNull(postconditionSummary, SummaryMaxLength);
        RiskLevel = riskLevel;
    }

    /// <summary>持久化层重建专用构造,不重新校验。</summary>
    internal DatabasePlanStep(
        Guid id,
        int sequence,
        string stepKind,
        string? inputSummary,
        string? preconditionSummary,
        string? postconditionSummary,
        RiskLevel riskLevel,
        bool isFrozen,
        bool isLocked,
        bool isDeleted,
        string entityType,
        DateTimeOffset createdOn,
        DateTimeOffset lastUpdatedOn,
        long optimisticVersion,
        Guid concurrencyVersion)
        : base(id)
    {
        Sequence = sequence;
        StepKind = stepKind;
        InputSummary = inputSummary;
        PreconditionSummary = preconditionSummary;
        PostconditionSummary = postconditionSummary;
        RiskLevel = riskLevel;
        IsFrozen = isFrozen;
        IsLocked = isLocked;
        IsDeleted = isDeleted;
        EntityType = entityType;
        CreatedOn = createdOn;
        LastUpdatedOn = lastUpdatedOn;
        OptimisticVersion = optimisticVersion;
        ConcurrencyVersion = concurrencyVersion;
    }

    /// <summary>生成参与计划校验和计算的规范化文本,供 <see cref="Topology.DatabaseTopologyFingerprint"/> 使用。</summary>
    internal string ToChecksumCanonical() =>
        $"{Sequence}|{StepKind}|{RiskLevel}|{InputSummary ?? string.Empty}|{PreconditionSummary ?? string.Empty}|{PostconditionSummary ?? string.Empty}";

    private static string RequireTrimmedNonEmpty(string? value, string emptyMessage, int maxLength, string tooLongMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(emptyMessage);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ValidationException(tooLongMessage);
        }

        return trimmed;
    }

    private static string? TrimOrNull(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ValidationException($"计划步骤摘要长度不能超过 {maxLength} 个字符。");
        }

        return trimmed;
    }

    private const int SummaryMaxLength = 512;
}
