using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Internal;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Options;
using IndustrialPlatform.SystemData.Contracts.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration;

/// <summary>备份证据用例端口。</summary>
public interface IBackupService
{
    /// <summary>为计划登记备份证据(创建为 Captured)。</summary>
    Task<DatabaseBackupEvidenceV1> CreateAsync(string tenantNId, string actorUserNId, string planNId, DatabaseBackupEvidenceRequestV1 request, CancellationToken cancellationToken);

    /// <summary>验证备份证据(Captured → Verified);已 Verified 幂等返回。</summary>
    Task<DatabaseBackupEvidenceV1> VerifyAsync(string tenantNId, string actorUserNId, string evidenceNId, CancellationToken cancellationToken);

    /// <summary>读取计划最近一条备份证据,供管理端在刷新后恢复门禁状态。</summary>
    Task<DatabaseBackupEvidenceV1?> GetLatestForPlanAsync(string tenantNId, string planNId, CancellationToken cancellationToken);

    /// <summary>计划当前是否有有效备份证据(Verified + 未过保留期 + 校验和/指纹匹配)。</summary>
    Task<bool> IsVerifiedForAsync(string tenantNId, DatabaseProvisionPlan plan, CancellationToken cancellationToken);
}

/// <summary>备份证据用例(05 方案 §8.1 备份证据表、§9.2 备份端点)。只存引用与时间,绝不保存访问 Secret。</summary>
public sealed class DatabaseBackupService : IBackupService
{
    private readonly IDatabaseOrchestrationStore _store;
    private readonly IOptions<DatabaseOrchestrationOptions> _options;

    public DatabaseBackupService(IDatabaseOrchestrationStore store, IOptions<DatabaseOrchestrationOptions> options)
    {
        _store = store;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<DatabaseBackupEvidenceV1> CreateAsync(
        string tenantNId,
        string actorUserNId,
        string planNId,
        DatabaseBackupEvidenceRequestV1 request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var planNIdTrimmed = DatabaseOrchestrationInput.Require(planNId, "计划标识不能为空。");
        // 先确认目标计划存在(缺失 → 404),再校验请求字段(400),避免对不存在计划抛输入校验错误。
        var plan = await _store.GetPlanAsync(tenantNId, planNIdTrimmed, cancellationToken)
            ?? throw new NotFoundException();

        var backupProvider = DatabaseOrchestrationInput.Require(request.BackupProvider, "备份提供程序不能为空。");
        var backupReference = DatabaseOrchestrationInput.Require(request.BackupReference, "备份引用不能为空。");

        var now = DateTimeOffset.UtcNow;
        var retentionUntil = request.RetentionUntil ?? now.AddDays(_options.Value.DefaultBackupRetentionDays);
        var evidence = DatabaseBackupEvidence.Create(
            tenantNId,
            DatabaseOrchestrationInput.NewNId("EVD"),
            plan.PlanNId,
            plan.PlanChecksum,
            plan.TargetStateFingerprint,
            backupProvider,
            backupReference,
            now,
            retentionUntil);
        await WriteGuard.ExecuteAsync(() => _store.AddBackupEvidenceAsync(evidence, cancellationToken));
        return ToEvidenceV1(evidence);
    }

    /// <inheritdoc />
    public async Task<DatabaseBackupEvidenceV1> VerifyAsync(
        string tenantNId,
        string actorUserNId,
        string evidenceNId,
        CancellationToken cancellationToken)
    {
        var evidenceNIdTrimmed = DatabaseOrchestrationInput.Require(evidenceNId, "备份证据标识不能为空。");
        var evidence = await _store.GetBackupEvidenceAsync(tenantNId, evidenceNIdTrimmed, cancellationToken)
            ?? throw new NotFoundException();

        if (evidence.Status == BackupEvidenceStatus.Verified)
        {
            return ToEvidenceV1(evidence);
        }

        var expectedOptimisticVersion = evidence.OptimisticVersion;
        var expectedConcurrencyVersion = evidence.ConcurrencyVersion;
        try
        {
            evidence.Verify(DatabaseOrchestrationInput.Require(actorUserNId, "验证人标识不能为空。"), DateTimeOffset.UtcNow);
        }
        catch (ValidationException ex)
        {
            throw new ValidationFailedException(ex.Message);
        }

        await WriteGuard.ExecuteAsync(
            () => _store.UpdateBackupEvidenceAsync(evidence, expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken));
        return ToEvidenceV1(evidence);
    }

    /// <inheritdoc />
    public async Task<bool> IsVerifiedForAsync(string tenantNId, DatabaseProvisionPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var evidences = await _store.GetBackupEvidenceForPlanAsync(tenantNId, plan.PlanNId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        return evidences.Any(evidence => evidence.IsValidFor(plan.PlanChecksum, plan.TargetStateFingerprint, now));
    }

    /// <inheritdoc />
    public async Task<DatabaseBackupEvidenceV1?> GetLatestForPlanAsync(
        string tenantNId,
        string planNId,
        CancellationToken cancellationToken)
    {
        var plan = await _store.GetPlanAsync(tenantNId, DatabaseOrchestrationInput.Require(planNId, "计划标识不能为空。"), cancellationToken)
            ?? throw new NotFoundException();
        var evidence = (await _store.GetBackupEvidenceForPlanAsync(tenantNId, plan.PlanNId, cancellationToken))
            .OrderByDescending(item => item.CapturedOn)
            .FirstOrDefault();
        return evidence is null ? null : ToEvidenceV1(evidence);
    }

    private static DatabaseBackupEvidenceV1 ToEvidenceV1(DatabaseBackupEvidence evidence) => new()
    {
        TenantNId = evidence.TenantNId,
        EvidenceNId = evidence.EvidenceNId,
        PlanNId = evidence.PlanNId,
        PlanChecksum = evidence.PlanChecksum,
        TargetStateFingerprint = evidence.TargetStateFingerprint,
        BackupProvider = evidence.BackupProvider,
        BackupReference = evidence.BackupReference,
        CapturedOn = evidence.CapturedOn,
        VerifiedOn = evidence.VerifiedOn,
        RetentionUntil = evidence.RetentionUntil,
        VerifiedByUserNId = evidence.VerifiedByUserNId,
        Status = evidence.Status.ToString(),
    };
}
