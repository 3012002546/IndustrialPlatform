using System.Globalization;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Application.Administration;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Application.IdentityDirectory;
using IndustrialPlatform.SystemData.Application.Organizations;
using IndustrialPlatform.SystemData.Application.Positions;
using IndustrialPlatform.SystemData.Contracts.Administration;
using IndustrialPlatform.SystemData.Domain.Assignments;
using IndustrialPlatform.SystemData.Domain.Organizations;

namespace IndustrialPlatform.SystemData.Application.Assignments;

/// <summary>
/// 用户任职管理用例(TASK-SD-006):时间化任职的创建/改区间/结束/取消/主任职原子切换。
/// 同一 (租户, 用户) 关键区经 <see cref="IUserAssignmentAdvisoryLock"/> 串行,
/// 区间重叠与主任职覆盖经 <see cref="AssignmentScheduleRules"/> 裁决为 §9.9 结构化错误码。
/// 新写入要求用户目录可验证(§11.2 fail-closed),读取沿用显示名快照。
/// </summary>
public sealed class UserAssignmentService : IUserAssignmentService
{
    private const string AssignmentObjectType = "UserAssignment";

    private readonly IUserAssignmentStore _store;
    private readonly IPositionStore _positionStore;
    private readonly IAdministrativeOrganizationStore _organizationStore;
    private readonly IIdentityUserDirectory _userDirectory;
    private readonly IUserAssignmentAdvisoryLock _advisoryLock;
    private readonly ILocalAuditCommand _audit;
    private readonly TimeProvider _timeProvider;

    /// <summary>初始化用户任职管理用例。</summary>
    public UserAssignmentService(
        IUserAssignmentStore store,
        IPositionStore positionStore,
        IAdministrativeOrganizationStore organizationStore,
        IIdentityUserDirectory userDirectory,
        IUserAssignmentAdvisoryLock advisoryLock,
        ILocalAuditCommand audit,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(positionStore);
        ArgumentNullException.ThrowIfNull(organizationStore);
        ArgumentNullException.ThrowIfNull(userDirectory);
        ArgumentNullException.ThrowIfNull(advisoryLock);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
        _positionStore = positionStore;
        _organizationStore = organizationStore;
        _userDirectory = userDirectory;
        _advisoryLock = advisoryLock;
        _audit = audit;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AssignmentV1>> ListForUserAsync(
        string tenantNId,
        string userNId,
        CancellationToken cancellationToken)
    {
        var userNIdValue = AdministrationInput.Require(userNId, "任职用户标识不能为空。");
        var assignments = await _store.GetAssignmentsForUserAsync(tenantNId, userNIdValue, cancellationToken);
        var positionNames = await LoadPositionNamesAsync(tenantNId, assignments, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        return assignments
            .Select(a => ToV1(a, positionNames.TryGetValue(a.PositionNId, out var name) ? name : string.Empty, now))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<AssignmentV1> CreateAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        string userNId,
        CreateAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userNIdValue = AdministrationInput.Require(userNId, "任职用户标识不能为空。");
        var nId = AdministrationInput.Require(request.NId, "任职业务标识不能为空。");
        var positionNId = AdministrationInput.Require(request.PositionNId, "任职岗位标识不能为空。");
        var isPrimary = request.IsPrimary ?? false;
        var now = _timeProvider.GetUtcNow();
        var effectiveFrom = request.EffectiveFrom ?? throw new AdministrationValidationFailedException("任职生效时间不能为空。");
        var effectiveTo = request.EffectiveTo;

        // 新写入 fail-closed:用户目录必须可验证(§11.2)。
        var displayNameSnapshot = await ResolveUserDisplayNameAsync(tenantNId, userNIdValue, cancellationToken);
        var position = await LoadPositionAsync(tenantNId, positionNId, cancellationToken);
        var organization = await LoadOrganizationAsync(tenantNId, position.OrganizationNId, cancellationToken);
        if (organization.Status != OrganizationStatus.Active)
        {
            throw new AdministrationValidationFailedException("任职只能创建在活动组织下。");
        }

        await using var handle = await _advisoryLock.AcquireAsync(tenantNId, userNIdValue, cancellationToken);

        try
        {
            var existing = await _store.GetAssignmentsForUserAsync(tenantNId, userNIdValue, cancellationToken);
            var assignment = UserAssignment.Create(
                tenantNId,
                nId,
                userNIdValue,
                displayNameSnapshot,
                position.OrganizationNId,
                position.NId,
                position.Id,
                position.IsDeleted,
                organizationActive: organization.Status == OrganizationStatus.Active,
                positionActive: position.Status == Domain.Positions.PositionStatus.Active,
                organizationMatchesPosition: true,
                isPrimary,
                effectiveFrom,
                effectiveTo,
                now);
            assignment.ClearDomainEvents();

            var candidate = existing.Where(a => a.Id != assignment.Id).Append(assignment).ToList();
            EnsureScheduleValid(candidate);

            await AdministrationWriteGuard.ExecuteAsync(() => _store.AddAsync(assignment, cancellationToken));
            await handle.CommitAsync(cancellationToken);
            await RecordAuditAsync(tenantNId, actorUserNId, traceId, "assignment.create", assignment.NId,
                $"岗位={positionNId}/主任职={isPrimary}", null, ToSummary(assignment));
            return ToV1(assignment, position.Name, now);
        }
        catch (ValidationException ex)
        {
            throw new AdministrationValidationFailedException(ex.Message);
        }
        catch (BusinessException ex)
        {
            throw new AdministrationConcurrencyConflictException(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<AssignmentV1> UpdateScheduledAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        string assignmentNId,
        UpdateScheduledAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var expectedOptimisticVersion = AdministrationInput.RequireNonNegativeVersion(request.ExpectedOptimisticVersion, "乐观版本");
        var expectedConcurrencyVersion = AdministrationInput.RequireConcurrencyVersion(request.ExpectedConcurrencyVersion, "并发版本");
        var now = _timeProvider.GetUtcNow();
        var effectiveFrom = request.EffectiveFrom ?? throw new AdministrationValidationFailedException("任职生效时间不能为空。");
        var effectiveTo = request.EffectiveTo;

        var assignment = await LoadAssignmentAsync(tenantNId, assignmentNId, cancellationToken);
        await using var handle = await _advisoryLock.AcquireAsync(tenantNId, assignment.UserNId, cancellationToken);

        try
        {
            var fresh = await LoadAssignmentAsync(tenantNId, assignmentNId, cancellationToken);
            var before = ToSummary(fresh);
            fresh.UpdateScheduledPeriod(effectiveFrom, effectiveTo, now);
            fresh.ClearDomainEvents();

            var others = await _store.GetAssignmentsForUserAsync(tenantNId, fresh.UserNId, cancellationToken);
            var candidate = others.Where(a => a.Id != fresh.Id).Append(fresh).ToList();
            EnsureScheduleValid(candidate);

            await AdministrationWriteGuard.ExecuteAsync(() =>
                _store.UpdateAsync(fresh, expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken));
            await handle.CommitAsync(cancellationToken);
            var positionName = await PositionNameAsync(tenantNId, fresh.PositionNId, cancellationToken);
            await RecordAuditAsync(tenantNId, actorUserNId, traceId, "assignment.update-scheduled", fresh.NId,
                "调整未来区间", before, ToSummary(fresh));
            return ToV1(fresh, positionName, now);
        }
        catch (ValidationException ex)
        {
            throw new AdministrationValidationFailedException(ex.Message);
        }
        catch (BusinessException ex)
        {
            throw new AdministrationConcurrencyConflictException(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<AssignmentV1> EndAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        string assignmentNId,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var assignment = await LoadAssignmentAsync(tenantNId, assignmentNId, cancellationToken);
        await using var handle = await _advisoryLock.AcquireAsync(tenantNId, assignment.UserNId, cancellationToken);

        try
        {
            var fresh = await LoadAssignmentAsync(tenantNId, assignmentNId, cancellationToken);
            var expectedOptimisticVersion = fresh.OptimisticVersion;
            var expectedConcurrencyVersion = fresh.ConcurrencyVersion;
            var before = ToSummary(fresh);

            fresh.End(now);
            fresh.ClearDomainEvents();

            var others = await _store.GetAssignmentsForUserAsync(tenantNId, fresh.UserNId, cancellationToken);
            var candidate = others.Where(a => a.Id != fresh.Id).Append(fresh).ToList();
            EnsurePrimaryCoverage(candidate);

            await AdministrationWriteGuard.ExecuteAsync(() =>
                _store.UpdateAsync(fresh, expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken));
            await handle.CommitAsync(cancellationToken);
            var positionName = await PositionNameAsync(tenantNId, fresh.PositionNId, cancellationToken);
            await RecordAuditAsync(tenantNId, actorUserNId, traceId, "assignment.end", fresh.NId,
                "结束当前任职", before, ToSummary(fresh));
            return ToV1(fresh, positionName, now);
        }
        catch (ValidationException ex)
        {
            throw new AdministrationValidationFailedException(ex.Message);
        }
        catch (BusinessException ex)
        {
            throw new AdministrationConcurrencyConflictException(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<AssignmentV1> CancelAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        string assignmentNId,
        CancelAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = _timeProvider.GetUtcNow();
        var assignment = await LoadAssignmentAsync(tenantNId, assignmentNId, cancellationToken);
        await using var handle = await _advisoryLock.AcquireAsync(tenantNId, assignment.UserNId, cancellationToken);

        try
        {
            var fresh = await LoadAssignmentAsync(tenantNId, assignmentNId, cancellationToken);
            var expectedOptimisticVersion = fresh.OptimisticVersion;
            var expectedConcurrencyVersion = fresh.ConcurrencyVersion;
            var before = ToSummary(fresh);

            fresh.Cancel(now, request.Reason);
            fresh.ClearDomainEvents();

            var others = await _store.GetAssignmentsForUserAsync(tenantNId, fresh.UserNId, cancellationToken);
            var candidate = others.Where(a => a.Id != fresh.Id).Append(fresh).ToList();
            EnsurePrimaryCoverage(candidate);

            await AdministrationWriteGuard.ExecuteAsync(() =>
                _store.UpdateAsync(fresh, expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken));
            await handle.CommitAsync(cancellationToken);
            var positionName = await PositionNameAsync(tenantNId, fresh.PositionNId, cancellationToken);
            await RecordAuditAsync(tenantNId, actorUserNId, traceId, "assignment.cancel", fresh.NId,
                request.Reason, before, ToSummary(fresh));
            return ToV1(fresh, positionName, now);
        }
        catch (ValidationException ex)
        {
            throw new AdministrationValidationFailedException(ex.Message);
        }
        catch (BusinessException ex)
        {
            throw new AdministrationConcurrencyConflictException(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AssignmentV1>> SetPrimaryAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        string userNId,
        SetPrimaryAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userNIdValue = AdministrationInput.Require(userNId, "任职用户标识不能为空。");
        var targetAssignmentNId = AdministrationInput.Require(request.TargetAssignmentNId, "目标主任职标识不能为空。");
        var effectiveOn = request.EffectiveOn ?? throw new AdministrationValidationFailedException("主任职切换生效时间不能为空。");
        var expectedRevision = request.ExpectedUserAssignmentRevision;
        var now = _timeProvider.GetUtcNow();

        await using var handle = await _advisoryLock.AcquireAsync(tenantNId, userNIdValue, cancellationToken);

        try
        {
            var assignments = await _store.GetAssignmentsForUserAsync(tenantNId, userNIdValue, cancellationToken);
            var target = assignments.FirstOrDefault(a => a.NId == targetAssignmentNId)
                ?? throw new AdministrationNotFoundException();

            // 当前主任职(区间覆盖 effectiveOn 的 Enabled 主任职;不变量下至多一条)。
            var currentPrimary = assignments
                .FirstOrDefault(a => a.State == AssignmentState.Enabled
                    && a.IsPrimary
                    && a.EffectiveFrom <= effectiveOn
                    && (a.EffectiveTo is null || a.EffectiveTo > effectiveOn));

            if (currentPrimary is not null && currentPrimary.NId != target.NId)
            {
                if (expectedRevision is { } revision && currentPrimary.OptimisticVersion != revision)
                {
                    throw new AdministrationConcurrencyConflictException("主任职已被其他操作修改,请刷新后重试。");
                }

                var beforePrimary = ToSummary(currentPrimary);
                var primaryExpectedOptimisticVersion = currentPrimary.OptimisticVersion;
                var primaryExpectedConcurrencyVersion = currentPrimary.ConcurrencyVersion;
                if (currentPrimary.EffectiveFrom == effectiveOn)
                {
                    // 新主任职与原主任职同刻生效:直接取消原主任职标记,不产生空区间。
                    currentPrimary.MarkPrimary(false, effectiveOn);
                }
                else
                {
                    // 保留历史:原主任职在切换时点结束并取消主任职标记,
                    // 避免切换前区间同时存在两条主任职违反覆盖不变量。
                    currentPrimary.MarkPrimary(false, effectiveOn);
                    currentPrimary.ScheduleEnd(effectiveOn);
                }

                currentPrimary.ClearDomainEvents();
                await AdministrationWriteGuard.ExecuteAsync(() =>
                    _store.UpdateAsync(currentPrimary, primaryExpectedOptimisticVersion, primaryExpectedConcurrencyVersion, cancellationToken));
                await RecordAuditAsync(tenantNId, actorUserNId, traceId, "assignment.primary-split", currentPrimary.NId,
                    $"切换主任职至 {target.NId},生效 {effectiveOn}", beforePrimary, ToSummary(currentPrimary));
            }

            var beforeTarget = ToSummary(target);
            var targetExpectedOptimisticVersion = target.OptimisticVersion;
            var targetExpectedConcurrencyVersion = target.ConcurrencyVersion;
            target.MarkPrimary(true, effectiveOn);
            target.ClearDomainEvents();

            var candidate = await _store.GetAssignmentsForUserAsync(tenantNId, userNIdValue, cancellationToken);
            var updatedSet = new List<UserAssignment>();
            if (currentPrimary is not null && currentPrimary.NId != target.NId)
            {
                updatedSet.Add(currentPrimary);
            }

            updatedSet.Add(target);
            updatedSet.AddRange(candidate.Where(a => a.Id != target.Id
                && (currentPrimary is null || a.Id != currentPrimary.Id)));
            EnsurePrimaryCoverage(updatedSet);

            await AdministrationWriteGuard.ExecuteAsync(() =>
                _store.UpdateAsync(target, targetExpectedOptimisticVersion, targetExpectedConcurrencyVersion, cancellationToken));
            await handle.CommitAsync(cancellationToken);
            await RecordAuditAsync(tenantNId, actorUserNId, traceId, "assignment.primary-switch", target.NId,
                request.Reason ?? $"切换主任职至 {target.NId},生效 {effectiveOn}", beforeTarget, ToSummary(target));

            var refreshed = await _store.GetAssignmentsForUserAsync(tenantNId, userNIdValue, cancellationToken);
            var positionNames = await LoadPositionNamesAsync(tenantNId, refreshed, cancellationToken);
            return refreshed
                .Select(a => ToV1(a, positionNames.TryGetValue(a.PositionNId, out var name) ? name : string.Empty, now))
                .ToList();
        }
        catch (ValidationException ex)
        {
            throw new AdministrationValidationFailedException(ex.Message);
        }
        catch (BusinessException ex)
        {
            throw new AdministrationConcurrencyConflictException(ex.Message);
        }
    }

    // ===== 私有辅助 =====

    /// <summary>新写入用户目录 fail-closed:目录不可用抛 503,用户不存在抛 400。</summary>
    private async Task<string> ResolveUserDisplayNameAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
    {
        var entry = await _userDirectory.GetAsync(tenantNId, userNId, cancellationToken);
        if (entry is null)
        {
            throw new AdministrationValidationFailedException("用户目录中不存在该用户,禁止创建任职。");
        }

        return entry.Name;
    }

    /// <summary>裁决候选任职集合的区间重叠与主任职覆盖;违规抛 §9.9 结构化异常。</summary>
    private static void EnsureScheduleValid(IReadOnlyList<UserAssignment> candidate)
    {
        var overlaps = AssignmentScheduleRules.FindOverlappingIntervals(candidate);
        if (overlaps.Count > 0)
        {
            throw new AssignmentIntervalOverlapException();
        }

        EnsurePrimaryCoverage(candidate);
    }

    /// <summary>裁决主任职覆盖;缺少主任职或主任职重叠分别抛对应 409。</summary>
    private static void EnsurePrimaryCoverage(IReadOnlyList<UserAssignment> candidate)
    {
        var violations = AssignmentScheduleRules.FindPrimaryCoverageViolations(candidate);
        if (violations.Count == 0)
        {
            return;
        }

        var first = violations[0];
        if (first.PrimaryCount == 0)
        {
            throw new AssignmentPrimaryRequiredException();
        }

        throw new AssignmentPrimaryOverlapException();
    }

    private async Task<Domain.Assignments.UserAssignment> LoadAssignmentAsync(string tenantNId, string assignmentNId, CancellationToken cancellationToken)
    {
        var assignment = await _store.GetAsync(tenantNId, assignmentNId, cancellationToken)
            ?? throw new AdministrationNotFoundException();
        return assignment;
    }

    private async Task<Domain.Positions.Position> LoadPositionAsync(string tenantNId, string positionNId, CancellationToken cancellationToken)
    {
        var position = await _positionStore.GetAsync(tenantNId, positionNId, cancellationToken)
            ?? throw new AdministrationNotFoundException();
        return position;
    }

    private async Task<AdministrativeOrganization> LoadOrganizationAsync(string tenantNId, string organizationNId, CancellationToken cancellationToken)
    {
        var organization = await _organizationStore.GetAsync(tenantNId, organizationNId, cancellationToken)
            ?? throw new AdministrationNotFoundException();
        return organization;
    }

    /// <summary>按 NId 去重惰性加载岗位名称(响应丰富化)。</summary>
    private async Task<IReadOnlyDictionary<string, string>> LoadPositionNamesAsync(
        string tenantNId,
        IReadOnlyCollection<Domain.Assignments.UserAssignment> assignments,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var positionNId in assignments.Select(a => a.PositionNId).Distinct(StringComparer.Ordinal))
        {
            var position = await _positionStore.GetAsync(tenantNId, positionNId, cancellationToken);
            if (position is not null)
            {
                result[positionNId] = position.Name;
            }
        }

        return result;
    }

    private async Task<string> PositionNameAsync(string tenantNId, string positionNId, CancellationToken cancellationToken)
    {
        var position = await _positionStore.GetAsync(tenantNId, positionNId, cancellationToken);
        return position?.Name ?? string.Empty;
    }

    private async Task RecordAuditAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        string action,
        string assignmentNId,
        string? reason,
        string? before,
        string? after)
    {
        await _audit.RecordAsync(
            new LocalAuditEntry(tenantNId, actorUserNId, action, AssignmentObjectType, assignmentNId, reason, before, after, traceId),
            CancellationToken.None);
    }

    /// <summary>审计摘要:岗位@用户#区间/主任职/状态。</summary>
    private static string ToSummary(Domain.Assignments.UserAssignment assignment) =>
        $"{assignment.PositionNId}@{assignment.UserNId}#[{assignment.EffectiveFrom.ToString("O", CultureInfo.InvariantCulture)}..{(assignment.EffectiveTo?.ToString("O", CultureInfo.InvariantCulture) ?? "∞")}]/主任职={assignment.IsPrimary}/状态={assignment.State}";

    private static AssignmentV1 ToV1(Domain.Assignments.UserAssignment assignment, string positionName, DateTimeOffset now) => new()
    {
        TenantNId = assignment.TenantNId,
        NId = assignment.NId,
        UserNId = assignment.UserNId,
        UserDisplayNameSnapshot = assignment.UserDisplayNameSnapshot,
        OrganizationNId = assignment.OrganizationNId,
        PositionNId = assignment.PositionNId,
        PositionName = positionName,
        IsPrimary = assignment.IsPrimary,
        EffectiveFrom = assignment.EffectiveFrom,
        EffectiveTo = assignment.EffectiveTo,
        State = assignment.GetProjection(now).ToString(),
        CancelledOn = assignment.CancelledOn,
        CancelReason = assignment.CancelReason,
        OptimisticVersion = assignment.OptimisticVersion,
        ConcurrencyVersion = assignment.ConcurrencyVersion,
    };
}
