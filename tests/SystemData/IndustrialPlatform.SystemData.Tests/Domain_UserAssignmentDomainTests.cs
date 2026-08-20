using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Domain.Assignments;

namespace IndustrialPlatform.SystemData.Domain.Tests;

/// <summary>
/// 用户任职领域测试(TASK-SD-005,05 方案 §7.4/§12.3):
/// 创建门、左闭右开投影、区间更新/结束/取消/主任职/未来拆分状态机。
/// </summary>
public sealed class UserAssignmentDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset D(int day) => Now.AddDays(day);

    // ===== 创建门 =====

    [Fact]
    public void Create_ValidCurrentAssignment_Succeeds()
    {
        var assignment = CreateAssignment(effectiveFrom: D(-1), effectiveTo: D(5));

        Assert.Equal(AssignmentState.Enabled, assignment.State);
        Assert.Equal(AssignmentProjection.Current, assignment.GetProjection(Now));
        Assert.False(assignment.IsPrimary);
    }

    [Fact]
    public void Create_EffectiveToBeforeFrom_Throws()
    {
        Assert.Throws<ValidationException>(() =>
            CreateAssignment(effectiveFrom: D(5), effectiveTo: D(2)));
    }

    [Fact]
    public void Create_EffectiveToEqualToFrom_Throws()
    {
        Assert.Throws<ValidationException>(() =>
            CreateAssignment(effectiveFrom: D(2), effectiveTo: D(2)));
    }

    [Fact]
    public void Create_AlreadyEndedAtNow_Throws()
    {
        Assert.Throws<BusinessException>(() =>
            CreateAssignment(effectiveFrom: D(-3), effectiveTo: Now));
    }

    [Fact]
    public void Create_InactiveOrganization_Throws()
    {
        Assert.Throws<BusinessException>(() =>
            UserAssignment.Create(
                Tenant, "assn-001", "user-001", "张三", "org-001", "pos-001",
                Guid.NewGuid(), positionIsDeleted: false,
                organizationActive: false, positionActive: true, organizationMatchesPosition: true,
                false, D(-1), D(5), Now));
    }

    [Fact]
    public void Create_InactivePosition_Throws()
    {
        Assert.Throws<BusinessException>(() =>
            UserAssignment.Create(
                Tenant, "assn-001", "user-001", "张三", "org-001", "pos-001",
                Guid.NewGuid(), positionIsDeleted: false,
                organizationActive: true, positionActive: false, organizationMatchesPosition: true,
                false, D(-1), D(5), Now));
    }

    [Fact]
    public void Create_DeletedPosition_Throws()
    {
        Assert.Throws<BusinessException>(() =>
            UserAssignment.Create(
                Tenant, "assn-001", "user-001", "张三", "org-001", "pos-001",
                Guid.NewGuid(), positionIsDeleted: true,
                organizationActive: true, positionActive: true, organizationMatchesPosition: true,
                false, D(-1), D(5), Now));
    }

    [Fact]
    public void Create_OrganizationPositionMismatch_Throws()
    {
        Assert.Throws<BusinessException>(() =>
            UserAssignment.Create(
                Tenant, "assn-001", "user-001", "张三", "org-002", "pos-001",
                Guid.NewGuid(), positionIsDeleted: false,
                organizationActive: true, positionActive: true, organizationMatchesPosition: false,
                false, D(-1), D(5), Now));
    }

    // ===== 投影 =====

    [Fact]
    public void Projection_Scheduled_WhenNowBeforeStart()
    {
        var assignment = CreateAssignment(effectiveFrom: D(2), effectiveTo: D(5));

        Assert.Equal(AssignmentProjection.Scheduled, assignment.GetProjection(Now));
    }

    [Fact]
    public void Projection_Current_WithinRange()
    {
        var assignment = CreateAssignment(effectiveFrom: D(-1), effectiveTo: D(5));

        Assert.Equal(AssignmentProjection.Current, assignment.GetProjection(Now));
    }

    [Fact]
    public void Projection_Current_OpenEnded()
    {
        var assignment = CreateAssignment(effectiveFrom: D(-1), effectiveTo: null);

        Assert.Equal(AssignmentProjection.Current, assignment.GetProjection(Now));
    }

    [Fact]
    public void Projection_Ended_AtEffectiveTo()
    {
        // 创建时(now=D(-10))区间尚未结束;测试时刻(Now=D0)恰在 EffectiveTo 上
        var assignment = CreateAssignment(effectiveFrom: D(-5), effectiveTo: Now, now: D(-10));

        Assert.Equal(AssignmentProjection.Ended, assignment.GetProjection(Now));
    }

    [Fact]
    public void Projection_Cancelled_TakesPrecedence()
    {
        var assignment = CreateAssignment(effectiveFrom: D(2), effectiveTo: D(5));
        assignment.Cancel(Now, "需求调整");

        Assert.Equal(AssignmentProjection.Cancelled, assignment.GetProjection(Now.AddDays(3)));
    }

    // ===== 区间更新 =====

    [Fact]
    public void UpdateScheduledPeriod_Ok()
    {
        var assignment = CreateAssignment(effectiveFrom: D(2), effectiveTo: D(5));

        assignment.UpdateScheduledPeriod(D(3), D(10), Now);

        Assert.Equal(D(3), assignment.EffectiveFrom);
        Assert.Equal(D(10), assignment.EffectiveTo);
    }

    [Fact]
    public void UpdateScheduledPeriod_OnCurrent_Throws()
    {
        var assignment = CreateAssignment(effectiveFrom: D(-1), effectiveTo: D(5));

        Assert.Throws<BusinessException>(() =>
            assignment.UpdateScheduledPeriod(D(3), D(10), Now));
    }

    [Fact]
    public void UpdateScheduledPeriod_PastStart_Throws()
    {
        var assignment = CreateAssignment(effectiveFrom: D(2), effectiveTo: D(5));

        Assert.Throws<BusinessException>(() =>
            assignment.UpdateScheduledPeriod(D(-1), D(10), Now));
    }

    [Fact]
    public void UpdateScheduledPeriod_InvalidRange_Throws()
    {
        var assignment = CreateAssignment(effectiveFrom: D(2), effectiveTo: D(5));

        Assert.Throws<ValidationException>(() =>
            assignment.UpdateScheduledPeriod(D(10), D(3), Now));
    }

    // ===== 结束 =====

    [Fact]
    public void End_Current_SetsEffectiveToNow()
    {
        var assignment = CreateAssignment(effectiveFrom: D(-1), effectiveTo: D(5));

        assignment.End(Now);

        Assert.Equal(Now, assignment.EffectiveTo);
    }

    [Fact]
    public void End_Scheduled_Throws()
    {
        var assignment = CreateAssignment(effectiveFrom: D(2), effectiveTo: D(5));

        Assert.Throws<BusinessException>(() => assignment.End(Now));
    }

    [Fact]
    public void End_Ended_Throws()
    {
        var assignment = CreateAssignment(effectiveFrom: D(-5), effectiveTo: D(-1), now: D(-10));

        Assert.Throws<BusinessException>(() => assignment.End(Now));
    }

    [Fact]
    public void End_Cancelled_Throws()
    {
        var assignment = CreateAssignment(effectiveFrom: D(2), effectiveTo: D(5));
        assignment.Cancel(Now, "取消");

        Assert.Throws<BusinessException>(() => assignment.End(Now.AddDays(3)));
    }

    // ===== 取消 =====

    [Fact]
    public void Cancel_Scheduled_SetsCancelledAndReason()
    {
        var assignment = CreateAssignment(effectiveFrom: D(2), effectiveTo: D(5));

        assignment.Cancel(Now, " 需求调整 ");

        Assert.Equal(AssignmentState.Cancelled, assignment.State);
        Assert.Equal(Now, assignment.CancelledOn);
        Assert.Equal("需求调整", assignment.CancelReason);
    }

    [Fact]
    public void Cancel_Current_Throws()
    {
        var assignment = CreateAssignment(effectiveFrom: D(-1), effectiveTo: D(5));

        Assert.Throws<BusinessException>(() => assignment.Cancel(Now, "取消"));
    }

    [Fact]
    public void Cancel_AlreadyCancelled_Throws()
    {
        var assignment = CreateAssignment(effectiveFrom: D(2), effectiveTo: D(5));
        assignment.Cancel(Now, "第一次");

        Assert.Throws<BusinessException>(() => assignment.Cancel(Now, "第二次"));
    }

    // ===== 主任职 =====

    [Fact]
    public void MarkPrimary_OnScheduled_Ok()
    {
        var assignment = CreateAssignment(effectiveFrom: D(2), effectiveTo: D(5));

        assignment.MarkPrimary(true, Now);

        Assert.True(assignment.IsPrimary);
    }

    [Fact]
    public void MarkPrimary_OnEnded_Throws()
    {
        var assignment = CreateAssignment(effectiveFrom: D(-5), effectiveTo: D(-1), now: D(-10));

        Assert.Throws<BusinessException>(() => assignment.MarkPrimary(true, Now));
    }

    [Fact]
    public void MarkPrimary_OnCancelled_Throws()
    {
        var assignment = CreateAssignment(effectiveFrom: D(2), effectiveTo: D(5));
        assignment.Cancel(Now, "取消");

        Assert.Throws<BusinessException>(() => assignment.MarkPrimary(true, Now.AddDays(3)));
    }

    // ===== 未来拆分(主任职原子切换历史拆分) =====

    [Fact]
    public void ScheduleEnd_OnOpenEndedCurrent_SplitsFuture()
    {
        var assignment = CreateAssignment(effectiveFrom: D(-1), effectiveTo: null);

        assignment.ScheduleEnd(D(10));

        Assert.Equal(D(10), assignment.EffectiveTo);
        Assert.Equal(AssignmentState.Enabled, assignment.State);
    }

    [Fact]
    public void ScheduleEnd_BeforeOrAtStart_Throws()
    {
        var assignment = CreateAssignment(effectiveFrom: D(-1), effectiveTo: null);

        Assert.Throws<BusinessException>(() => assignment.ScheduleEnd(D(-1)));
    }

    [Fact]
    public void ScheduleEnd_AfterExistingEnd_Throws()
    {
        var assignment = CreateAssignment(effectiveFrom: D(-1), effectiveTo: D(5));

        Assert.Throws<BusinessException>(() => assignment.ScheduleEnd(D(10)));
    }

    [Fact]
    public void ScheduleEnd_OnCancelled_Throws()
    {
        var assignment = CreateAssignment(effectiveFrom: D(2), effectiveTo: D(5));
        assignment.Cancel(Now, "取消");

        Assert.Throws<BusinessException>(() => assignment.ScheduleEnd(D(10)));
    }

    // ===== 辅助 =====

    private const string Tenant = "tenant-001";

    private static UserAssignment CreateAssignment(
        string userNId = "user-001",
        string positionNId = "pos-001",
        string organizationNId = "org-001",
        bool isPrimary = false,
        DateTimeOffset? effectiveFrom = null,
        DateTimeOffset? effectiveTo = null,
        DateTimeOffset? now = null) =>
        UserAssignment.Create(
            Tenant,
            "assn-001",
            userNId,
            "张三",
            organizationNId,
            positionNId,
            Guid.NewGuid(),
            positionIsDeleted: false,
            organizationActive: true,
            positionActive: true,
            organizationMatchesPosition: true,
            isPrimary,
            effectiveFrom ?? D(-1),
            effectiveTo,
            now ?? Now);
}
