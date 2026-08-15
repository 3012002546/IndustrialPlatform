using IndustrialPlatform.SystemData.Application.Administration;
using IndustrialPlatform.SystemData.Application.Assignments;
using IndustrialPlatform.SystemData.Contracts.Administration;
using IndustrialPlatform.SystemData.Domain.Organizations;
using IndustrialPlatform.SystemData.Domain.Positions;

namespace IndustrialPlatform.SystemData.Application.Tests;

/// <summary>
/// 用户任职管理用例测试(TASK-SD-006):创建/改区间/结束/取消/主任职原子切换。
/// 错误码对齐 §9.9:目录不可用 503 SD_IDENTITY_DIRECTORY_UNAVAILABLE、用户不存在 400 SD_VALIDATION_FAILED、
/// 区间重叠 409 SD_ASSIGNMENT_INTERVAL_OVERLAP、缺少主任职 409 SD_ASSIGNMENT_PRIMARY_REQUIRED、
/// 主任职重叠 409 SD_ASSIGNMENT_PRIMARY_OVERLAP、双版本/revision 409 SD_CONCURRENCY_CONFLICT。
/// </summary>
public sealed class UserAssignmentServiceTests
{
    private const string Tenant = "tenant-001";
    private const string Actor = "user-actor";
    private const string TraceId = "trace-001";

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // =====================================================================
    // 创建
    // =====================================================================

    [Fact]
    public async Task CreateAsync_CreatesScheduledAssignment()
    {
        var fixture = CreateFixture();
        fixture.Directory.WithEntry("user-1", "张三");
        await SeedCompanyAndPositionAsync(fixture);

        var result = await fixture.Service.CreateAsync(Tenant, Actor, TraceId, "user-1", new CreateAssignmentRequest
        {
            NId = "assign-1",
            PositionNId = "pos-1",
            IsPrimary = true,
            EffectiveFrom = Now.AddDays(1),
            EffectiveTo = null,
        }, CancellationToken.None);

        Assert.Equal("assign-1", result.NId);
        Assert.Equal("user-1", result.UserNId);
        Assert.Equal("张三", result.UserDisplayNameSnapshot);
        Assert.Equal("company-a", result.OrganizationNId);
        Assert.Equal("pos-1", result.PositionNId);
        Assert.Equal("后端工程师", result.PositionName);
        Assert.True(result.IsPrimary);
        Assert.Equal("Scheduled", result.State);
        Assert.Equal(0, result.OptimisticVersion);
    }

    [Fact]
    public async Task CreateAsync_DirectoryUnavailable_ThrowsIdentityDirectoryUnavailable()
    {
        var fixture = CreateFixture();
        fixture.Directory.Unavailable();
        await SeedCompanyAndPositionAsync(fixture);

        var ex = await Assert.ThrowsAsync<IdentityDirectoryUnavailableException>(() =>
            fixture.Service.CreateAsync(Tenant, Actor, TraceId, "user-1", new CreateAssignmentRequest
            {
                NId = "assign-1",
                PositionNId = "pos-1",
                IsPrimary = true,
                EffectiveFrom = Now.AddDays(1),
            }, CancellationToken.None));

        Assert.Equal(503, ex.StatusCode);
        Assert.Equal("SD_IDENTITY_DIRECTORY_UNAVAILABLE", ex.Code);
    }

    [Fact]
    public async Task CreateAsync_UserMissingInDirectory_ThrowsValidationFailed()
    {
        var fixture = CreateFixture();
        await SeedCompanyAndPositionAsync(fixture);

        var ex = await Assert.ThrowsAsync<AdministrationValidationFailedException>(() =>
            fixture.Service.CreateAsync(Tenant, Actor, TraceId, "user-1", new CreateAssignmentRequest
            {
                NId = "assign-1",
                PositionNId = "pos-1",
                IsPrimary = true,
                EffectiveFrom = Now.AddDays(1),
            }, CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("SD_VALIDATION_FAILED", ex.Code);
    }

    [Fact]
    public async Task CreateAsync_IntervalOverlap_ThrowsIntervalOverlap()
    {
        var fixture = CreateFixture();
        fixture.Directory.WithEntry("user-1", "张三");
        await SeedCompanyAndPositionAsync(fixture);
        await fixture.Service.CreateAsync(Tenant, Actor, TraceId, "user-1", new CreateAssignmentRequest
        {
            NId = "assign-1",
            PositionNId = "pos-1",
            IsPrimary = true,
            EffectiveFrom = Now.AddDays(1),
            EffectiveTo = Now.AddDays(3),
        }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AssignmentIntervalOverlapException>(() =>
            fixture.Service.CreateAsync(Tenant, Actor, TraceId, "user-1", new CreateAssignmentRequest
            {
                NId = "assign-2",
                PositionNId = "pos-1",
                IsPrimary = false,
                EffectiveFrom = Now.AddDays(2),
                EffectiveTo = Now.AddDays(4),
            }, CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("SD_ASSIGNMENT_INTERVAL_OVERLAP", ex.Code);
    }

    [Fact]
    public async Task CreateAsync_NonPrimaryWithoutPrimary_ThrowsPrimaryRequired()
    {
        var fixture = CreateFixture();
        fixture.Directory.WithEntry("user-1", "张三");
        await SeedCompanyAndPositionAsync(fixture);

        var ex = await Assert.ThrowsAsync<AssignmentPrimaryRequiredException>(() =>
            fixture.Service.CreateAsync(Tenant, Actor, TraceId, "user-1", new CreateAssignmentRequest
            {
                NId = "assign-1",
                PositionNId = "pos-1",
                IsPrimary = false,
                EffectiveFrom = Now.AddDays(1),
            }, CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("SD_ASSIGNMENT_PRIMARY_REQUIRED", ex.Code);
    }

    [Fact]
    public async Task CreateAsync_SecondPrimaryOverlap_ThrowsPrimaryOverlap()
    {
        var fixture = CreateFixture();
        fixture.Directory.WithEntry("user-1", "张三");
        await SeedCompanyAndPositionAsync(fixture);
        await SeedCompanyAndPositionAsync(fixture, "company-a", "pos-2");
        await fixture.Service.CreateAsync(Tenant, Actor, TraceId, "user-1", new CreateAssignmentRequest
        {
            NId = "assign-1",
            PositionNId = "pos-1",
            IsPrimary = true,
            EffectiveFrom = Now.AddDays(1),
            EffectiveTo = Now.AddDays(3),
        }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AssignmentPrimaryOverlapException>(() =>
            fixture.Service.CreateAsync(Tenant, Actor, TraceId, "user-1", new CreateAssignmentRequest
            {
                NId = "assign-2",
                PositionNId = "pos-2",
                IsPrimary = true,
                EffectiveFrom = Now.AddDays(2),
                EffectiveTo = Now.AddDays(4),
            }, CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("SD_ASSIGNMENT_PRIMARY_OVERLAP", ex.Code);
    }

    [Fact]
    public async Task CreateAsync_PositionInactive_ThrowsConcurrency()
    {
        var fixture = CreateFixture();
        fixture.Directory.WithEntry("user-1", "张三");
        await SeedCompanyAndPositionAsync(fixture);
        var position = await fixture.Positions.GetAsync(Tenant, "pos-1", CancellationToken.None);
        var expectedOptimisticVersion = position!.OptimisticVersion;
        var expectedConcurrencyVersion = position.ConcurrencyVersion;
        position.Deactivate(hasActiveOrFutureAssignments: false);
        await fixture.Positions.UpdateAsync(position, expectedOptimisticVersion, expectedConcurrencyVersion, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AdministrationConcurrencyConflictException>(() =>
            fixture.Service.CreateAsync(Tenant, Actor, TraceId, "user-1", new CreateAssignmentRequest
            {
                NId = "assign-1",
                PositionNId = "pos-1",
                IsPrimary = true,
                EffectiveFrom = Now.AddDays(1),
            }, CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("SD_CONCURRENCY_CONFLICT", ex.Code);
    }

    [Fact]
    public async Task CreateAsync_RecordsAuditEntry()
    {
        var fixture = CreateFixture();
        fixture.Directory.WithEntry("user-1", "张三");
        await SeedCompanyAndPositionAsync(fixture);

        await fixture.Service.CreateAsync(Tenant, Actor, TraceId, "user-1", new CreateAssignmentRequest
        {
            NId = "assign-1",
            PositionNId = "pos-1",
            IsPrimary = true,
            EffectiveFrom = Now.AddDays(1),
        }, CancellationToken.None);

        var entry = Assert.Single(fixture.Audit.Entries);
        Assert.Equal("assignment.create", entry.Action);
        Assert.Equal("UserAssignment", entry.ObjectType);
        Assert.Equal("assign-1", entry.ObjectNId);
    }

    // =====================================================================
    // 列表
    // =====================================================================

    [Fact]
    public async Task ListForUserAsync_ReturnsTimelineWithDerivedStates()
    {
        var fixture = CreateFixture();
        fixture.Directory.WithEntry("user-1", "张三");
        await SeedCompanyAndPositionAsync(fixture);
        await SeedCompanyAndPositionAsync(fixture, "company-a", "pos-2");
        await fixture.Service.CreateAsync(Tenant, Actor, TraceId, "user-1", new CreateAssignmentRequest
        {
            NId = "assign-1",
            PositionNId = "pos-1",
            IsPrimary = true,
            EffectiveFrom = Now.AddDays(-1),
            EffectiveTo = Now.AddDays(1),
        }, CancellationToken.None);
        await fixture.Service.CreateAsync(Tenant, Actor, TraceId, "user-1", new CreateAssignmentRequest
        {
            NId = "assign-2",
            PositionNId = "pos-2",
            IsPrimary = true,
            EffectiveFrom = Now.AddDays(1),
            EffectiveTo = Now.AddDays(3),
        }, CancellationToken.None);

        var timeline = await fixture.Service.ListForUserAsync(Tenant, "user-1", CancellationToken.None);

        Assert.Equal(2, timeline.Count);
        Assert.Equal("Current", timeline.Single(a => a.NId == "assign-1").State);
        Assert.Equal("Scheduled", timeline.Single(a => a.NId == "assign-2").State);
    }

    // =====================================================================
    // 修改计划区间
    // =====================================================================

    [Fact]
    public async Task UpdateScheduledAsync_UpdatesFuturePeriod()
    {
        var fixture = CreateFixture();
        fixture.Directory.WithEntry("user-1", "张三");
        await SeedCompanyAndPositionAsync(fixture);
        var created = await fixture.Service.CreateAsync(Tenant, Actor, TraceId, "user-1", new CreateAssignmentRequest
        {
            NId = "assign-1",
            PositionNId = "pos-1",
            IsPrimary = true,
            EffectiveFrom = Now.AddDays(1),
            EffectiveTo = Now.AddDays(3),
        }, CancellationToken.None);

        var updated = await fixture.Service.UpdateScheduledAsync(Tenant, Actor, TraceId, "assign-1", new UpdateScheduledAssignmentRequest
        {
            EffectiveFrom = Now.AddDays(2),
            EffectiveTo = Now.AddDays(4),
            ExpectedOptimisticVersion = created.OptimisticVersion,
            ExpectedConcurrencyVersion = created.ConcurrencyVersion,
        }, CancellationToken.None);

        Assert.Equal(Now.AddDays(2), updated.EffectiveFrom);
        Assert.Equal(Now.AddDays(4), updated.EffectiveTo);
        Assert.Equal(1, updated.OptimisticVersion);
    }

    [Fact]
    public async Task UpdateScheduledAsync_StaleVersions_ThrowsConcurrency()
    {
        var fixture = CreateFixture();
        fixture.Directory.WithEntry("user-1", "张三");
        await SeedCompanyAndPositionAsync(fixture);
        var created = await fixture.Service.CreateAsync(Tenant, Actor, TraceId, "user-1", new CreateAssignmentRequest
        {
            NId = "assign-1",
            PositionNId = "pos-1",
            IsPrimary = true,
            EffectiveFrom = Now.AddDays(1),
            EffectiveTo = Now.AddDays(3),
        }, CancellationToken.None);
        await fixture.Service.UpdateScheduledAsync(Tenant, Actor, TraceId, "assign-1", new UpdateScheduledAssignmentRequest
        {
            EffectiveFrom = Now.AddDays(2),
            EffectiveTo = Now.AddDays(4),
            ExpectedOptimisticVersion = created.OptimisticVersion,
            ExpectedConcurrencyVersion = created.ConcurrencyVersion,
        }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AdministrationConcurrencyConflictException>(() =>
            fixture.Service.UpdateScheduledAsync(Tenant, Actor, TraceId, "assign-1", new UpdateScheduledAssignmentRequest
            {
                EffectiveFrom = Now.AddDays(3),
                EffectiveTo = Now.AddDays(5),
                ExpectedOptimisticVersion = created.OptimisticVersion,
                ExpectedConcurrencyVersion = created.ConcurrencyVersion,
            }, CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("SD_CONCURRENCY_CONFLICT", ex.Code);
    }

    // =====================================================================
    // 结束与取消
    // =====================================================================

    [Fact]
    public async Task EndAsync_EndsCurrentAssignment()
    {
        var fixture = CreateFixture();
        fixture.Directory.WithEntry("user-1", "张三");
        await SeedCompanyAndPositionAsync(fixture);
        await fixture.Service.CreateAsync(Tenant, Actor, TraceId, "user-1", new CreateAssignmentRequest
        {
            NId = "assign-1",
            PositionNId = "pos-1",
            IsPrimary = true,
            EffectiveFrom = Now.AddDays(-1),
            EffectiveTo = Now.AddDays(1),
        }, CancellationToken.None);

        var ended = await fixture.Service.EndAsync(Tenant, Actor, TraceId, "assign-1", CancellationToken.None);

        Assert.Equal(Now, ended.EffectiveTo);
        Assert.Equal("Ended", ended.State);
    }

    [Fact]
    public async Task EndAsync_ScheduledAssignment_ThrowsConcurrency()
    {
        var fixture = CreateFixture();
        fixture.Directory.WithEntry("user-1", "张三");
        await SeedCompanyAndPositionAsync(fixture);
        await fixture.Service.CreateAsync(Tenant, Actor, TraceId, "user-1", new CreateAssignmentRequest
        {
            NId = "assign-1",
            PositionNId = "pos-1",
            IsPrimary = true,
            EffectiveFrom = Now.AddDays(1),
            EffectiveTo = Now.AddDays(3),
        }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AdministrationConcurrencyConflictException>(() =>
            fixture.Service.EndAsync(Tenant, Actor, TraceId, "assign-1", CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("SD_CONCURRENCY_CONFLICT", ex.Code);
    }

    [Fact]
    public async Task CancelAsync_CancelsScheduledAssignment()
    {
        var fixture = CreateFixture();
        fixture.Directory.WithEntry("user-1", "张三");
        await SeedCompanyAndPositionAsync(fixture);
        await fixture.Service.CreateAsync(Tenant, Actor, TraceId, "user-1", new CreateAssignmentRequest
        {
            NId = "assign-1",
            PositionNId = "pos-1",
            IsPrimary = true,
            EffectiveFrom = Now.AddDays(1),
            EffectiveTo = Now.AddDays(3),
        }, CancellationToken.None);

        var cancelled = await fixture.Service.CancelAsync(Tenant, Actor, TraceId, "assign-1", new CancelAssignmentRequest
        {
            Reason = "计划调整",
        }, CancellationToken.None);

        Assert.Equal("Cancelled", cancelled.State);
        Assert.Equal(Now, cancelled.CancelledOn);
        Assert.Equal("计划调整", cancelled.CancelReason);
    }

    // =====================================================================
    // 主任职原子切换
    // =====================================================================

    [Fact]
    public async Task SetPrimaryAsync_SwitchesPrimaryAndSplitsHistory()
    {
        var fixture = CreateFixture();
        fixture.Directory.WithEntry("user-1", "张三");
        await SeedCompanyAndPositionAsync(fixture);
        await SeedCompanyAndPositionAsync(fixture, "company-a", "pos-2");
        await fixture.Service.CreateAsync(Tenant, Actor, TraceId, "user-1", new CreateAssignmentRequest
        {
            NId = "assign-1",
            PositionNId = "pos-1",
            IsPrimary = true,
            EffectiveFrom = Now.AddDays(1),
            EffectiveTo = Now.AddDays(5),
        }, CancellationToken.None);
        await fixture.Service.CreateAsync(Tenant, Actor, TraceId, "user-1", new CreateAssignmentRequest
        {
            NId = "assign-2",
            PositionNId = "pos-2",
            IsPrimary = false,
            EffectiveFrom = Now.AddDays(1),
            EffectiveTo = Now.AddDays(5),
        }, CancellationToken.None);

        var timeline = await fixture.Service.SetPrimaryAsync(Tenant, Actor, TraceId, "user-1", new SetPrimaryAssignmentRequest
        {
            TargetAssignmentNId = "assign-2",
            EffectiveOn = Now.AddDays(3),
        }, CancellationToken.None);

        var newPrimary = timeline.Single(a => a.NId == "assign-2");
        Assert.True(newPrimary.IsPrimary);
        var oldPrimary = timeline.Single(a => a.NId == "assign-1");
        Assert.Equal(Now.AddDays(3), oldPrimary.EffectiveTo);
        Assert.Contains(fixture.Audit.Entries, e => e.Action == "assignment.primary-switch");
    }

    [Fact]
    public async Task SetPrimaryAsync_UnknownTarget_ThrowsNotFound()
    {
        var fixture = CreateFixture();
        fixture.Directory.WithEntry("user-1", "张三");
        await SeedCompanyAndPositionAsync(fixture);

        var ex = await Assert.ThrowsAsync<AdministrationNotFoundException>(() =>
            fixture.Service.SetPrimaryAsync(Tenant, Actor, TraceId, "user-1", new SetPrimaryAssignmentRequest
            {
                TargetAssignmentNId = "missing",
                EffectiveOn = Now.AddDays(1),
            }, CancellationToken.None));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("SD_NOT_FOUND", ex.Code);
    }

    [Fact]
    public async Task SetPrimaryAsync_StaleExpectedRevision_ThrowsConcurrency()
    {
        var fixture = CreateFixture();
        fixture.Directory.WithEntry("user-1", "张三");
        await SeedCompanyAndPositionAsync(fixture);
        await SeedCompanyAndPositionAsync(fixture, "company-a", "pos-2");
        await fixture.Service.CreateAsync(Tenant, Actor, TraceId, "user-1", new CreateAssignmentRequest
        {
            NId = "assign-1",
            PositionNId = "pos-1",
            IsPrimary = true,
            EffectiveFrom = Now.AddDays(1),
            EffectiveTo = Now.AddDays(5),
        }, CancellationToken.None);
        await fixture.Service.CreateAsync(Tenant, Actor, TraceId, "user-1", new CreateAssignmentRequest
        {
            NId = "assign-2",
            PositionNId = "pos-2",
            IsPrimary = false,
            EffectiveFrom = Now.AddDays(1),
            EffectiveTo = Now.AddDays(5),
        }, CancellationToken.None);
        await fixture.Service.SetPrimaryAsync(Tenant, Actor, TraceId, "user-1", new SetPrimaryAssignmentRequest
        {
            TargetAssignmentNId = "assign-2",
            EffectiveOn = Now.AddDays(3),
        }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AdministrationConcurrencyConflictException>(() =>
            fixture.Service.SetPrimaryAsync(Tenant, Actor, TraceId, "user-1", new SetPrimaryAssignmentRequest
            {
                TargetAssignmentNId = "assign-1",
                EffectiveOn = Now.AddDays(4),
                ExpectedUserAssignmentRevision = 0,
            }, CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("SD_CONCURRENCY_CONFLICT", ex.Code);
    }

    // ===== 辅助 =====

    private static (
        UserAssignmentService Service,
        FakeUserAssignmentStore Assignments,
        FakePositionStore Positions,
        FakeAdministrativeOrganizationStore Orgs,
        FakeIdentityUserDirectory Directory,
        ManualTimeProvider Time,
        RecordingLocalAuditCommand Audit) CreateFixture()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        var positions = new FakePositionStore();
        var assignments = new FakeUserAssignmentStore();
        var directory = new FakeIdentityUserDirectory();
        var time = new ManualTimeProvider(Now);
        var audit = new RecordingLocalAuditCommand();
        var service = new UserAssignmentService(assignments, positions, orgs, directory, new FakeUserAssignmentAdvisoryLock(), audit, time);
        return (service, assignments, positions, orgs, directory, time, audit);
    }

    private static async Task SeedCompanyAndPositionAsync(
        (UserAssignmentService Service, FakeUserAssignmentStore Assignments, FakePositionStore Positions, FakeAdministrativeOrganizationStore Orgs, FakeIdentityUserDirectory Directory, ManualTimeProvider Time, RecordingLocalAuditCommand Audit) fixture,
        string companyNId = "company-a",
        string positionNId = "pos-1")
    {
        var existing = await fixture.Orgs.GetAsync(Tenant, companyNId, CancellationToken.None);
        var company = existing ?? AdministrativeOrganization.CreateRootCompany(Tenant, companyNId, "A 公司", 1);
        if (existing is null)
        {
            await fixture.Orgs.AddAsync(company, CancellationToken.None);
        }

        var position = Position.Create(Tenant, positionNId, Tenant, companyNId, company.Id, company.IsDeleted, true, "后端工程师", null, 1);
        await fixture.Positions.AddAsync(position, CancellationToken.None);
    }
}
