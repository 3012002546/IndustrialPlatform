using IndustrialPlatform.SystemData.Domain.Assignments;

namespace IndustrialPlatform.SystemData.Domain.Tests;

/// <summary>
/// 任职调度纯规则测试(TASK-SD-005,05 方案 §7.4/§12.3):
/// 同用户/岗位 Enabled 区间重叠检测与主任职覆盖(左闭右开)。
/// </summary>
public sealed class AssignmentScheduleRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset D(int day) => Now.AddDays(day);

    private const string Tenant = "tenant-001";

    // ===== 区间重叠 =====

    [Fact]
    public void FindOverlapping_SameUserPosition_ReportsOverlap()
    {
        var a = Create("assn-a", "user-001", "pos-001", isPrimary: false, D(0), D(10));
        var b = Create("assn-b", "user-001", "pos-001", isPrimary: false, D(5), D(15));

        var overlaps = AssignmentScheduleRules.FindOverlappingIntervals([a, b]);

        var item = Assert.Single(overlaps);
        Assert.Equal("user-001", item.UserNId);
        Assert.Equal("pos-001", item.PositionNId);
        Assert.Equal(D(0), item.AFrom);
        Assert.Equal(D(5), item.BFrom);
    }

    [Fact]
    public void FindOverlapping_AdjacentNonOverlap_LeftClosedRightOpen()
    {
        var a = Create("assn-a", "user-001", "pos-001", isPrimary: false, D(0), D(5));
        var b = Create("assn-b", "user-001", "pos-001", isPrimary: false, D(5), D(10));

        var overlaps = AssignmentScheduleRules.FindOverlappingIntervals([a, b]);

        Assert.Empty(overlaps);
    }

    [Fact]
    public void FindOverlapping_DifferentPositions_NoOverlap()
    {
        var a = Create("assn-a", "user-001", "pos-001", isPrimary: false, D(0), D(10));
        var b = Create("assn-b", "user-001", "pos-002", isPrimary: false, D(5), D(15));

        var overlaps = AssignmentScheduleRules.FindOverlappingIntervals([a, b]);

        Assert.Empty(overlaps);
    }

    [Fact]
    public void FindOverlapping_OpenEndedInterval_OverlapsEverythingAfter()
    {
        var a = Create("assn-a", "user-001", "pos-001", isPrimary: false, D(0), null);
        var b = Create("assn-b", "user-001", "pos-001", isPrimary: false, D(5), D(15));

        var overlaps = AssignmentScheduleRules.FindOverlappingIntervals([a, b]);

        Assert.Single(overlaps);
    }

    [Fact]
    public void FindOverlapping_CancelledExcluded()
    {
        var a = Create("assn-a", "user-001", "pos-001", isPrimary: false, D(0), D(10));
        var b = Create("assn-b", "user-001", "pos-001", isPrimary: false, D(5), D(15));
        b.Cancel(Now, "取消");

        var overlaps = AssignmentScheduleRules.FindOverlappingIntervals([a, b]);

        Assert.Empty(overlaps);
    }

    // ===== 主任职覆盖 =====

    [Fact]
    public void Coverage_SinglePrimary_NoViolation()
    {
        var a = Create("assn-a", "user-001", "pos-001", isPrimary: true, D(0), D(10));

        var violations = AssignmentScheduleRules.FindPrimaryCoverageViolations([a]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Coverage_CoveredWithoutPrimary_Violation()
    {
        var a = Create("assn-a", "user-001", "pos-001", isPrimary: false, D(0), D(10));

        var violations = AssignmentScheduleRules.FindPrimaryCoverageViolations([a]);

        var item = Assert.Single(violations);
        Assert.Equal(D(0), item.From);
        Assert.Equal(D(10), item.To);
        Assert.Equal(1, item.ActiveCount);
        Assert.Equal(0, item.PrimaryCount);
    }

    [Fact]
    public void Coverage_TwoOverlappingPrimaries_Violation()
    {
        var a = Create("assn-a", "user-001", "pos-001", isPrimary: true, D(0), D(10));
        var b = Create("assn-b", "user-001", "pos-002", isPrimary: true, D(5), D(15));

        var violations = AssignmentScheduleRules.FindPrimaryCoverageViolations([a, b]);

        var item = Assert.Single(violations);
        Assert.Equal(D(5), item.From);
        Assert.Equal(D(10), item.To);
        Assert.Equal(2, item.ActiveCount);
        Assert.Equal(2, item.PrimaryCount);
    }

    [Fact]
    public void Coverage_NonOverlappingPrimaryHandoff_NoViolation()
    {
        var a = Create("assn-a", "user-001", "pos-001", isPrimary: true, D(0), D(5));
        var b = Create("assn-b", "user-001", "pos-002", isPrimary: true, D(5), D(10));

        var violations = AssignmentScheduleRules.FindPrimaryCoverageViolations([a, b]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Coverage_NoAssignments_NoViolation()
    {
        var violations = AssignmentScheduleRules.FindPrimaryCoverageViolations([]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Coverage_OpenEndedTail_ReportsViolationToNull()
    {
        var a = Create("assn-a", "user-001", "pos-001", isPrimary: true, D(0), D(5));
        var b = Create("assn-b", "user-001", "pos-002", isPrimary: false, D(0), null);

        var violations = AssignmentScheduleRules.FindPrimaryCoverageViolations([a, b]);

        // [D0,D5) 主=1 副=1 → 覆盖正确;[D5,∞) 主=0 → 开放段违规
        var item = Assert.Single(violations);
        Assert.Equal(D(5), item.From);
        Assert.Null(item.To);
    }

    [Fact]
    public void Coverage_SeparatesByUser()
    {
        var a = Create("assn-a", "user-001", "pos-001", isPrimary: true, D(0), D(10));
        var b = Create("assn-b", "user-002", "pos-002", isPrimary: false, D(0), D(10));

        var violations = AssignmentScheduleRules.FindPrimaryCoverageViolations([a, b]);

        var item = Assert.Single(violations);
        Assert.Equal("user-002", item.UserNId);
    }

    // ===== 辅助 =====

    private static UserAssignment Create(
        string nId,
        string userNId,
        string positionNId,
        bool isPrimary,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo) =>
        UserAssignment.Create(
            Tenant,
            nId,
            userNId,
            "张三",
            "org-001",
            positionNId,
            Guid.NewGuid(),
            positionIsDeleted: false,
            organizationActive: true,
            positionActive: true,
            organizationMatchesPosition: true,
            isPrimary,
            effectiveFrom,
            effectiveTo,
            Now);
}
