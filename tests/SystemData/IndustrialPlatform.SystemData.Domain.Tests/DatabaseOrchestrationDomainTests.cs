using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration.Events;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration.Identities;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SystemData.Domain.Topology;

namespace IndustrialPlatform.SystemData.Domain.Tests;

/// <summary>
/// 数据库编排领域聚合测试(TASK-SD-002,05 方案 §7.1/§8.1):
/// NId 值对象、注册/重注册幂等、不可变计划、审批/备份证据有效性、
/// Operation 状态机全转换与取消安全边界、环境策略与迁移观察。
/// </summary>
public sealed class DatabaseOrchestrationDomainTests
{
    // ===== NId =====

    [Fact]
    public void NId_Create_NormalizesAndComparesCaseInsensitive()
    {
        var lower = NId.Create("plan-001");
        var upper = NId.Create("PLAN-001");

        Assert.Equal(upper, lower);
        Assert.Equal("plan-001", lower.Value);
        Assert.Equal("PLAN-001", lower.Normalized);
    }

    [Fact]
    public void NId_Create_TrimsWhitespace()
    {
        var nid = NId.Create("  op-001  ");

        Assert.Equal("op-001", nid.Value);
    }

    [Fact]
    public void NId_Create_Empty_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() => NId.Create(null));
        Assert.Throws<ValidationException>(() => NId.Create("   "));
    }

    [Fact]
    public void NId_Create_InvalidCharacter_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() => NId.Create("plan 001"));
        Assert.Throws<ValidationException>(() => NId.Create("plan#001"));
        Assert.Throws<ValidationException>(() => NId.Create("-leading-dash"));
    }

    [Fact]
    public void NId_Create_TooLong_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() => NId.Create(new string('a', 129)));
    }

    // ===== 注册清单 =====

    [Fact]
    public void Register_CreatesRegisteredRegistrationAndPublishesEvent()
    {
        var registration = CreateRegistration();

        Assert.Equal(RegistrationStatus.Registered, registration.Status);
        Assert.Equal("industrial_platform_dev", registration.PhysicalDatabaseName);
        Assert.True(registration.IsSharedPhysicalDatabase);
        Assert.Single(registration.DomainEvents.OfType<DatabaseRegistrationChangedEvent>());
        Assert.Equal("systemdata", registration.ServiceKey);
    }

    [Fact]
    public void ReRegister_WithNewManifest_UpdatesFieldsAndPublishesEvent()
    {
        var registration = CreateRegistration();
        registration.ClearDomainEvents();
        var createdOn = registration.LastUpdatedOn;

        registration.ReRegister(
            "PostgreSQL",
            "systemdata_db",
            "industrial_platform_dev",
            true,
            "Shared",
            DatabaseTopologyFingerprint.Sha256Hex("topo-v2"),
            "artifact-1",
            "2.0.0",
            DatabaseTopologyFingerprint.Sha256Hex("artifact-v2"),
            "sig-ref",
            DesiredState.SourceOfTruth,
            false,
            false,
            "2",
            DatabaseTopologyFingerprint.Sha256Hex("manifest-v2"));

        Assert.Equal("2.0.0", registration.MigrationVersion);
        Assert.Equal("2", registration.ManifestVersion);
        Assert.Equal(RegistrationStatus.Registered, registration.Status);
        Assert.True(registration.LastUpdatedOn >= createdOn);
        Assert.Single(registration.DomainEvents.OfType<DatabaseRegistrationChangedEvent>());
    }

    [Fact]
    public void UpdateDesiredState_ChangesStateAndPublishesEvent()
    {
        var registration = CreateRegistration();
        registration.ClearDomainEvents();

        registration.UpdateDesiredState(DesiredState.Retiring);

        Assert.Equal(DesiredState.Retiring, registration.DesiredState);
        Assert.Single(registration.DomainEvents.OfType<DatabaseRegistrationChangedEvent>());
    }

    [Fact]
    public void MarkNotReady_SetsStatusNotReady()
    {
        var registration = CreateRegistration();

        registration.MarkNotReady();

        Assert.Equal(RegistrationStatus.NotReady, registration.Status);
    }

    // ===== 不可变计划 =====

    [Fact]
    public void Create_PlanIsFrozenWithChecksumAndOrderedSteps()
    {
        var plan = CreatePlan();

        Assert.True(plan.IsFrozen);
        Assert.Equal(3, plan.Steps.Count);
        Assert.Equal([1, 2, 3], plan.Steps.Select(step => step.Sequence).ToArray());
        Assert.Equal(64, plan.PlanChecksum.Length);
        Assert.Equal(RiskLevel.Medium, plan.RiskLevel);
        Assert.Equal(DatabasePlanRequiredPolicies.Approval | DatabasePlanRequiredPolicies.Backup, plan.RequiredPolicies);
    }

    [Fact]
    public void Create_EmptySteps_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            DatabaseProvisionPlan.Create(
                "tenant-001",
                "PLAN-001",
                "Development",
                "systemdata",
                "1.0.0",
                "0.9.0",
                DatabaseTopologyFingerprint.Sha256Hex("target"),
                RiskLevel.Low,
                false,
                DatabasePlanRequiredPolicies.None,
                DateTimeOffset.UtcNow.AddMinutes(30),
                "user-001",
                []));
    }

    [Fact]
    public void Create_NonContiguousSteps_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            DatabaseProvisionPlan.Create(
                "tenant-001",
                "PLAN-001",
                "Development",
                "systemdata",
                "1.0.0",
                "0.9.0",
                DatabaseTopologyFingerprint.Sha256Hex("target"),
                RiskLevel.Low,
                false,
                DatabasePlanRequiredPolicies.None,
                DateTimeOffset.UtcNow.AddMinutes(30),
                "user-001",
                [
                    new DatabasePlanStep(1, "validate", null, null, null, RiskLevel.Low),
                    new DatabasePlanStep(3, "inspect", null, null, null, RiskLevel.Low),
                ]));
    }

    [Fact]
    public void Create_DuplicateSequence_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            DatabaseProvisionPlan.Create(
                "tenant-001",
                "PLAN-001",
                "Development",
                "systemdata",
                "1.0.0",
                "0.9.0",
                DatabaseTopologyFingerprint.Sha256Hex("target"),
                RiskLevel.Low,
                false,
                DatabasePlanRequiredPolicies.None,
                DateTimeOffset.UtcNow.AddMinutes(30),
                "user-001",
                [
                    new DatabasePlanStep(1, "validate", null, null, null, RiskLevel.Low),
                    new DatabasePlanStep(1, "inspect", null, null, null, RiskLevel.Low),
                ]));
    }

    [Fact]
    public void IsExpired_AfterDeadline_ReturnsTrue()
    {
        var plan = CreatePlan(expiresOn: DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.True(plan.IsExpired(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsExpired_BeforeDeadline_ReturnsFalse()
    {
        var plan = CreatePlan(expiresOn: DateTimeOffset.UtcNow.AddMinutes(30));

        Assert.False(plan.IsExpired(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MatchesTargetStateFingerprint_ComparesExact()
    {
        var target = DatabaseTopologyFingerprint.Sha256Hex("target");
        var plan = CreatePlan(targetStateFingerprint: target);

        Assert.True(plan.MatchesTargetStateFingerprint(target));
        Assert.False(plan.MatchesTargetStateFingerprint(DatabaseTopologyFingerprint.Sha256Hex("other")));
    }

    [Fact]
    public void MatchesPlanChecksum_ComparesExact()
    {
        var plan = CreatePlan();

        Assert.True(plan.MatchesPlanChecksum(plan.PlanChecksum));
        Assert.False(plan.MatchesPlanChecksum(DatabaseTopologyFingerprint.Sha256Hex("other")));
    }

    // ===== 审批 =====

    [Fact]
    public void Approval_Create_IsApprovedAndValidForMatchingPlan()
    {
        var plan = CreatePlan();
        var approval = CreateApproval(plan, approvedOn: DateTimeOffset.UtcNow.AddMinutes(-1), expiresOn: DateTimeOffset.UtcNow.AddMinutes(30));

        Assert.Equal(ApprovalStatus.Approved, approval.Status);
        Assert.True(approval.IsValidFor(plan.PlanChecksum, plan.TargetStateFingerprint, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Approval_IsValidFor_MismatchedChecksum_ReturnsFalse()
    {
        var plan = CreatePlan();
        var approval = CreateApproval(plan, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(30));

        Assert.False(approval.IsValidFor(DatabaseTopologyFingerprint.Sha256Hex("other"), plan.TargetStateFingerprint, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Approval_IsValidFor_Expired_ReturnsFalse()
    {
        var plan = CreatePlan();
        var approval = CreateApproval(plan, DateTimeOffset.UtcNow.AddMinutes(-30), DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.False(approval.IsValidFor(plan.PlanChecksum, plan.TargetStateFingerprint, DateTimeOffset.UtcNow));
    }

    // ===== 备份证据 =====

    [Fact]
    public void BackupEvidence_Create_IsCaptured()
    {
        var plan = CreatePlan();
        var evidence = CreateEvidence(plan);

        Assert.Equal(BackupEvidenceStatus.Captured, evidence.Status);
        Assert.Null(evidence.VerifiedOn);
    }

    [Fact]
    public void BackupEvidence_Create_RetentionBeforeCapture_ThrowsValidationException()
    {
        var capturedOn = DateTimeOffset.UtcNow;
        Assert.Throws<ValidationException>(() =>
            DatabaseBackupEvidence.Create(
                "tenant-001",
                "EVD-001",
                "PLAN-001",
                DatabaseTopologyFingerprint.Sha256Hex("checksum"),
                DatabaseTopologyFingerprint.Sha256Hex("fingerprint"),
                "aws-s3",
                "snapshot-123",
                capturedOn,
                capturedOn.AddHours(-1)));
    }

    [Fact]
    public void BackupEvidence_Verify_TransitionsToVerifiedAndSetsAudit()
    {
        var plan = CreatePlan();
        var evidence = CreateEvidence(plan);
        var verifiedOn = DateTimeOffset.UtcNow;

        evidence.Verify("user-003", verifiedOn);

        Assert.Equal(BackupEvidenceStatus.Verified, evidence.Status);
        Assert.Equal("user-003", evidence.VerifiedByUserNId);
        Assert.Equal(verifiedOn, evidence.VerifiedOn);
        Assert.True(evidence.IsValidFor(plan.PlanChecksum, plan.TargetStateFingerprint, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void BackupEvidence_Verify_AlreadyVerified_ThrowsValidationException()
    {
        var plan = CreatePlan();
        var evidence = CreateEvidence(plan);
        evidence.Verify("user-003", DateTimeOffset.UtcNow);

        Assert.Throws<ValidationException>(() => evidence.Verify("user-004", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void BackupEvidence_Verify_AfterRetention_ThrowsValidationException()
    {
        var plan = CreatePlan();
        var evidence = CreateEvidence(plan, capturedOn: DateTimeOffset.UtcNow.AddDays(-30), retentionUntil: DateTimeOffset.UtcNow.AddDays(-1));

        Assert.Throws<ValidationException>(() => evidence.Verify("user-003", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void BackupEvidence_IsValidFor_Unverified_ReturnsFalse()
    {
        var plan = CreatePlan();
        var evidence = CreateEvidence(plan);

        Assert.False(evidence.IsValidFor(plan.PlanChecksum, plan.TargetStateFingerprint, DateTimeOffset.UtcNow));
    }

    // ===== Operation 状态机 =====

    [Fact]
    public void Enqueue_CreatesQueuedOperationWithStepsForAllPhases()
    {
        var operation = CreateOperation();

        Assert.Equal(OperationStatus.Queued, operation.Status);
        Assert.Equal(OperationPhase.Validate, operation.Phase);
        Assert.Equal(0, operation.Attempt);
        Assert.Equal(OperationKind.Apply, operation.Kind);
        Assert.Equal(DatabaseProvisionOperation.AllPhases.Length, operation.Steps.Count);
        Assert.Equal(
            DatabaseProvisionOperation.AllPhases.Select((_, index) => index + 1).ToArray(),
            operation.Steps.Select(step => step.Sequence).ToArray());
        Assert.Single(operation.DomainEvents.OfType<DatabaseOperationStatusChangedEvent>());
    }

    [Fact]
    public void Start_QueuedToRunning_AcquiresLease()
    {
        var operation = CreateOperation();
        var now = DateTimeOffset.UtcNow;
        var lease = TimeSpan.FromSeconds(60);

        operation.Start("runner-1", now, lease);

        Assert.Equal(OperationStatus.Running, operation.Status);
        Assert.Equal(OperationPhase.Validate, operation.Phase);
        Assert.Equal(1, operation.Attempt);
        Assert.Equal("runner-1", operation.LeaseOwner);
        Assert.Equal(now.Add(lease), operation.LeaseExpiresOn);
        Assert.Equal(now, operation.HeartbeatOn);
        Assert.Equal(now, operation.StartedOn);
        Assert.True(operation.IsCancellable);
    }

    [Fact]
    public void Start_NotQueued_ThrowsBusinessException()
    {
        var operation = CreateOperation();
        operation.Start("runner-1", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(60));

        Assert.Throws<BusinessException>(() =>
            operation.Start("runner-1", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void AdvanceToNextPhase_MovesThroughPhasesAndMarksSteps()
    {
        var operation = CreateOperation();
        operation.Start("runner-1", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(60));

        operation.AdvanceToNextPhase(DateTimeOffset.UtcNow);

        Assert.Equal(OperationPhase.Inspect, operation.Phase);
        Assert.Equal(OperationStepStatus.Succeeded, operation.Steps.First().Status);
        Assert.Equal(OperationStepStatus.Running, operation.Steps.ElementAt(1).Status);
    }

    [Fact]
    public void AdvanceToNextPhase_NotRunning_ThrowsBusinessException()
    {
        var operation = CreateOperation();

        Assert.Throws<BusinessException>(() => operation.AdvanceToNextPhase(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Complete_MarksSucceededAndReleasesLease()
    {
        var operation = CreateOperation();
        operation.Start("runner-1", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(60));
        var now = DateTimeOffset.UtcNow;

        operation.Complete(now);

        Assert.Equal(OperationStatus.Succeeded, operation.Status);
        Assert.Equal(now, operation.CompletedOn);
        Assert.Null(operation.LeaseOwner);
        Assert.Null(operation.LeaseExpiresOn);
    }

    [Fact]
    public void Complete_NotRunning_ThrowsBusinessException()
    {
        var operation = CreateOperation();

        Assert.Throws<BusinessException>(() => operation.Complete(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Fail_RecordsSanitizedError()
    {
        var operation = CreateOperation();
        var now = DateTimeOffset.UtcNow;

        operation.Fail("SD_DB_EXECUTION_FAILED", "迁移执行失败。", now);

        Assert.Equal(OperationStatus.Failed, operation.Status);
        Assert.Equal("SD_DB_EXECUTION_FAILED", operation.SanitizedErrorCode);
        Assert.Equal("迁移执行失败。", operation.SanitizedErrorSummary);
        Assert.Equal(now, operation.CompletedOn);
    }

    [Fact]
    public void Cancel_QueuedOperation_TransitionsToCancelled()
    {
        var operation = CreateOperation();

        operation.Cancel(DateTimeOffset.UtcNow);

        Assert.Equal(OperationStatus.Cancelled, operation.Status);
        Assert.True(operation.Steps.All(step => step.Status == OperationStepStatus.Cancelled));
    }

    [Fact]
    public void Cancel_RunningAtInspect_Allowed()
    {
        var operation = CreateOperation();
        operation.Start("runner-1", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(60));
        operation.AdvanceToNextPhase(DateTimeOffset.UtcNow);

        Assert.True(operation.IsCancellable);
        operation.Cancel(DateTimeOffset.UtcNow);

        Assert.Equal(OperationStatus.Cancelled, operation.Status);
    }

    [Fact]
    public void Cancel_AfterInspect_ThrowsBusinessException()
    {
        var operation = CreateOperation();
        operation.Start("runner-1", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(60));
        operation.AdvanceToNextPhase(DateTimeOffset.UtcNow);
        operation.AdvanceToNextPhase(DateTimeOffset.UtcNow);

        Assert.False(operation.IsCancellable);
        Assert.Throws<BusinessException>(() => operation.Cancel(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Cancel_AfterComplete_ThrowsBusinessException()
    {
        var operation = CreateOperation();
        operation.Start("runner-1", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(60));
        operation.Complete(DateTimeOffset.UtcNow);

        Assert.Throws<BusinessException>(() => operation.Cancel(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Timeout_TransitionsToTimedOutAndCancelsPendingSteps()
    {
        var operation = CreateOperation();
        var now = DateTimeOffset.UtcNow;

        operation.Timeout(now);

        Assert.Equal(OperationStatus.TimedOut, operation.Status);
        Assert.Equal("SD_DB_OPERATION_TIMED_OUT", operation.SanitizedErrorCode);
        Assert.Equal(now, operation.CompletedOn);
    }

    [Fact]
    public void Timeout_AfterSucceeded_ThrowsBusinessException()
    {
        var operation = CreateOperation();
        operation.Start("runner-1", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(60));
        operation.Complete(DateTimeOffset.UtcNow);

        Assert.Throws<BusinessException>(() => operation.Timeout(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Heartbeat_WithMatchingOwner_RenewsLease()
    {
        var operation = CreateOperation();
        operation.Start("runner-1", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(60));
        var now = DateTimeOffset.UtcNow;

        operation.Heartbeat("runner-1", now, TimeSpan.FromSeconds(120));

        Assert.Equal(now.AddSeconds(120), operation.LeaseExpiresOn);
        Assert.Equal(now, operation.HeartbeatOn);
    }

    [Fact]
    public void Heartbeat_WithMismatchedOwner_ThrowsBusinessException()
    {
        var operation = CreateOperation();
        operation.Start("runner-1", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(60));

        Assert.Throws<BusinessException>(() =>
            operation.Heartbeat("runner-2", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void ReleaseLease_WithMatchingOwner_ClearsLeaseFields()
    {
        var operation = CreateOperation();
        operation.Start("runner-1", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(60));

        operation.ReleaseLease("runner-1");

        Assert.Null(operation.LeaseOwner);
        Assert.Null(operation.LeaseExpiresOn);
    }

    [Fact]
    public void ReleaseLease_WithMismatchedOwner_ThrowsBusinessException()
    {
        var operation = CreateOperation();
        operation.Start("runner-1", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(60));

        Assert.Throws<BusinessException>(() => operation.ReleaseLease("runner-2"));
    }

    [Fact]
    public void HasTimedOut_AfterDeadline_ReturnsTrue()
    {
        var operation = DatabaseProvisionOperation.Enqueue(
            "tenant-001",
            "OP-001",
            OperationKind.Plan,
            "Development",
            "systemdata",
            planNId: null,
            "1.0.0",
            "idem-001",
            DatabaseTopologyFingerprint.Sha256Hex("req"),
            DateTimeOffset.UtcNow.AddMinutes(-1),
            "trace-001",
            "user-001");

        Assert.True(operation.HasTimedOut(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MatchesRequestHash_ComparesExact()
    {
        var hash = DatabaseTopologyFingerprint.Sha256Hex("req");
        var operation = CreateOperation(requestHash: hash);

        Assert.True(operation.MatchesRequestHash(hash));
        Assert.False(operation.MatchesRequestHash(DatabaseTopologyFingerprint.Sha256Hex("other")));
    }

    // ===== 环境策略 =====

    [Fact]
    public void EnvironmentPolicy_Create_StartsRevisionAtOne()
    {
        var policy = DatabaseEnvironmentPolicy.Create(
            "tenant-001", "Production", DatabaseEnvironmentKind.Production, true, true, 1800, 120, 1800, 3);

        Assert.Equal(1, policy.PolicyRevision);
        Assert.True(policy.ApprovalRequired);
        Assert.True(policy.BackupRequired);
    }

    [Fact]
    public void EnvironmentPolicy_UpdatePolicy_IncrementsRevision()
    {
        var policy = DatabaseEnvironmentPolicy.Create(
            "tenant-001", "Production", DatabaseEnvironmentKind.Production, true, true, 1800, 120, 1800, 3);

        policy.UpdatePolicy(DatabaseEnvironmentKind.Production, false, false, 3600, 60, 3600, 0);

        Assert.Equal(2, policy.PolicyRevision);
        Assert.False(policy.ApprovalRequired);
        Assert.Equal(3600, policy.PlanTtlSeconds);
    }

    [Fact]
    public void EnvironmentPolicy_UpdatePolicy_InvalidTimeout_ThrowsValidationException()
    {
        var policy = DatabaseEnvironmentPolicy.Create(
            "tenant-001", "Production", DatabaseEnvironmentKind.Production, true, true, 1800, 120, 1800, 3);

        Assert.Throws<ValidationException>(() =>
            policy.UpdatePolicy(DatabaseEnvironmentKind.Production, false, false, 0, 60, 3600, 0));
    }

    // ===== 迁移观察 =====

    [Fact]
    public void Record_CapturesObservation()
    {
        var observedOn = DateTimeOffset.UtcNow;

        var observation = DatabaseMigrationObservation.Record(
            "tenant-001",
            "Development",
            "systemdata",
            DatabaseTopologyFingerprint.Sha256Hex("identity"),
            "1.0.0",
            DatabaseTopologyFingerprint.Sha256Hex("artifact"),
            observedOn,
            "OP-001",
            VerificationStatus.Verified);

        Assert.Equal("1.0.0", observation.ObservedVersion);
        Assert.Equal("OP-001", observation.OperationNId);
        Assert.Equal(VerificationStatus.Verified, observation.VerificationStatus);
        Assert.Equal(observedOn, observation.ObservedOn);
    }

    // ===== 测试夹具 =====

    private static DatabaseRegistration CreateRegistration() =>
        DatabaseRegistration.Register(
            "tenant-001",
            "Development",
            "systemdata",
            "PostgreSQL",
            "systemdata_db",
            "industrial_platform_dev",
            true,
            "Shared",
            DatabaseTopologyFingerprint.Sha256Hex("topo"),
            "artifact-1",
            "1.0.0",
            DatabaseTopologyFingerprint.Sha256Hex("artifact"),
            "sig-ref",
            "user-001",
            DesiredState.SourceOfTruth,
            false,
            false,
            "1",
            DatabaseTopologyFingerprint.Sha256Hex("manifest"));

    private static DatabaseProvisionPlan CreatePlan(
        string? targetStateFingerprint = null,
        DateTimeOffset? expiresOn = null) =>
        DatabaseProvisionPlan.Create(
            "tenant-001",
            "PLAN-001",
            "Development",
            "systemdata",
            "1.0.0",
            "0.9.0",
            targetStateFingerprint ?? DatabaseTopologyFingerprint.Sha256Hex("target"),
            RiskLevel.Medium,
            false,
            DatabasePlanRequiredPolicies.Approval | DatabasePlanRequiredPolicies.Backup,
            expiresOn ?? DateTimeOffset.UtcNow.AddMinutes(30),
            "user-001",
            [
                new DatabasePlanStep(1, "validate", "input", "pre", "post", RiskLevel.Low),
                new DatabasePlanStep(2, "inspect", "input", "pre", "post", RiskLevel.Low),
                new DatabasePlanStep(3, "migrate", "input", "pre", "post", RiskLevel.High),
            ]);

    private static DatabaseApproval CreateApproval(
        DatabaseProvisionPlan plan,
        DateTimeOffset approvedOn,
        DateTimeOffset expiresOn) =>
        DatabaseApproval.Create(
            "tenant-001",
            "APR-001",
            plan.PlanNId,
            plan.PlanChecksum,
            plan.TargetStateFingerprint,
            "user-002",
            "批准上线",
            approvedOn,
            expiresOn);

    private static DatabaseBackupEvidence CreateEvidence(
        DatabaseProvisionPlan plan,
        DateTimeOffset? capturedOn = null,
        DateTimeOffset? retentionUntil = null) =>
        DatabaseBackupEvidence.Create(
            "tenant-001",
            "EVD-001",
            plan.PlanNId,
            plan.PlanChecksum,
            plan.TargetStateFingerprint,
            "aws-s3",
            "snapshot-123",
            capturedOn ?? DateTimeOffset.UtcNow,
            retentionUntil ?? DateTimeOffset.UtcNow.AddDays(7));

    private static DatabaseProvisionOperation CreateOperation(string? requestHash = null) =>
        DatabaseProvisionOperation.Enqueue(
            "tenant-001",
            "OP-001",
            OperationKind.Apply,
            "Development",
            "systemdata",
            planNId: "PLAN-001",
            requestedVersion: "1.0.0",
            idempotencyKey: "idem-001",
            requestHash: requestHash ?? DatabaseTopologyFingerprint.Sha256Hex("req"),
            timeoutOn: DateTimeOffset.UtcNow.AddMinutes(30),
            traceId: "trace-001",
            createdByUserNId: "user-001");
}
