using IndustrialPlatform.SharedKernel.Entities;

namespace IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;

/// <summary>
/// Operation 步骤诊断子实体(05 方案 §8.1 <c>system_data_database_operation_step</c>)。
/// 一个阶段一个步骤,顺序推进;只记录状态与脱敏错误(不含 SQL/迁移原始输出)。
/// </summary>
public sealed class DatabaseOperationStep : Entity
{
    /// <summary>SanitizedErrorCode 最大长度。</summary>
    public const int ErrorCodeMaxLength = 64;

    /// <summary>SanitizedErrorSummary 最大长度。</summary>
    public const int ErrorSummaryMaxLength = 512;

    /// <summary>顺序(1 起),操作内唯一。</summary>
    public int Sequence { get; }

    /// <summary>执行阶段。</summary>
    public OperationPhase Phase { get; }

    /// <summary>本次执行尝试序号。</summary>
    public int Attempt { get; private set; }

    /// <summary>步骤状态。</summary>
    public OperationStepStatus Status { get; private set; }

    /// <summary>开始时间。</summary>
    public DateTimeOffset? StartedOn { get; private set; }

    /// <summary>完成时间(成功/失败/取消)。</summary>
    public DateTimeOffset? CompletedOn { get; private set; }

    /// <summary>脱敏错误码。</summary>
    public string? SanitizedErrorCode { get; private set; }

    /// <summary>脱敏错误摘要。</summary>
    public string? SanitizedErrorSummary { get; private set; }

    /// <summary>由操作聚合在入队时创建。</summary>
    internal DatabaseOperationStep(int sequence, OperationPhase phase, int attempt)
    {
        Sequence = sequence;
        Phase = phase;
        Attempt = attempt;
        Status = OperationStepStatus.Pending;
    }

    /// <summary>持久化层重建专用构造,不重新校验。</summary>
    internal DatabaseOperationStep(
        Guid id,
        int sequence,
        OperationPhase phase,
        int attempt,
        OperationStepStatus status,
        DateTimeOffset? startedOn,
        DateTimeOffset? completedOn,
        string? sanitizedErrorCode,
        string? sanitizedErrorSummary,
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
        Phase = phase;
        Attempt = attempt;
        Status = status;
        StartedOn = startedOn;
        CompletedOn = completedOn;
        SanitizedErrorCode = sanitizedErrorCode;
        SanitizedErrorSummary = sanitizedErrorSummary;
        IsFrozen = isFrozen;
        IsLocked = isLocked;
        IsDeleted = isDeleted;
        EntityType = entityType;
        CreatedOn = createdOn;
        LastUpdatedOn = lastUpdatedOn;
        OptimisticVersion = optimisticVersion;
        ConcurrencyVersion = concurrencyVersion;
    }

    /// <summary>标记开始执行(Pending → Running)。</summary>
    internal void MarkRunning(DateTimeOffset at, int attempt)
    {
        if (Status != OperationStepStatus.Pending)
        {
            return;
        }

        Status = OperationStepStatus.Running;
        Attempt = attempt;
        StartedOn = at;
    }

    /// <summary>标记成功(Running → Succeeded)。</summary>
    internal void MarkSucceeded(DateTimeOffset at)
    {
        if (Status != OperationStepStatus.Running)
        {
            return;
        }

        Status = OperationStepStatus.Succeeded;
        CompletedOn = at;
    }

    /// <summary>标记失败(Running/Pending → Failed)。</summary>
    internal void MarkFailed(DateTimeOffset at, string? errorCode, string? errorSummary)
    {
        if (Status is not (OperationStepStatus.Pending or OperationStepStatus.Running))
        {
            return;
        }

        Status = OperationStepStatus.Failed;
        CompletedOn = at;
        SanitizedErrorCode = errorCode;
        SanitizedErrorSummary = errorSummary;
    }

    /// <summary>标记取消(Pending/Running → Cancelled)。</summary>
    internal void MarkCancelled(DateTimeOffset at)
    {
        if (Status is not (OperationStepStatus.Pending or OperationStepStatus.Running))
        {
            return;
        }

        Status = OperationStepStatus.Cancelled;
        CompletedOn = at;
    }
}
