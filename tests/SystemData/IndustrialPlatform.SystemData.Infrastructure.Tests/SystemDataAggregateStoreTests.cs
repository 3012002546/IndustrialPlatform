using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Application.Assignments;
using IndustrialPlatform.SystemData.Application.Organizations;
using IndustrialPlatform.SystemData.Application.Positions;
using IndustrialPlatform.SystemData.Domain.Assignments;
using IndustrialPlatform.SystemData.Domain.Organizations;
using IndustrialPlatform.SystemData.Domain.Positions;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.SystemData;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using SQLitePCL;

namespace IndustrialPlatform.SystemData.Infrastructure.Tests;

/// <summary>
/// 组织/岗位/任职仓储集成测试(TASK-SD-005,05 方案 §8.1/§8.2)。
/// 基于独立 SQLite 文件库(Foreign Keys=True 开启复合外键)验证 SDM-004-03/005-01/006-01
/// 三张表、三仓储全聚合往返、同级/同组织名称大小写不敏感唯一、依赖计数、子树展开、
/// 双版本并发与按用户 advisory lock(SQLite 替身)。
/// (PostgreSQL 真实验证标记「待验收」。)
/// </summary>
public sealed class SystemDataAggregateStoreTests : IDisposable
{
    static SystemDataAggregateStoreTests()
    {
        Batteries_V2.Init();
    }

    private const string Tenant = "tenant-001";
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly string _dbPath;
    private readonly SqlSugarDbContext _dbContext;
    private readonly AdministrativeOrganizationStore _organizationStore;
    private readonly PositionStore _positionStore;
    private readonly UserAssignmentStore _assignmentStore;
    private readonly UserAssignmentAdvisoryLock _advisoryLock;

    public SystemDataAggregateStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-sysdata-aggregate-test-{Guid.NewGuid():N}.db");
        _dbContext = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            ConnectionString = $"Data Source={_dbPath};Foreign Keys=True",
            DbType = DbType.Sqlite,
        }));

        var runner = new SchemaMigrationRunner(_dbContext, SystemDataSchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance);
        runner.ApplyPendingAsync().GetAwaiter().GetResult();

        _organizationStore = new AdministrativeOrganizationStore(_dbContext);
        _positionStore = new PositionStore(_dbContext);
        _assignmentStore = new UserAssignmentStore(_dbContext);
        _advisoryLock = new UserAssignmentAdvisoryLock(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException)
        {
            // SqlSugarScope 连接池可能短暂占用文件句柄,忽略清理失败。
        }
    }

    private static CancellationToken Ct => CancellationToken.None;

    // =====================================================================
    // 行政组织仓储
    // =====================================================================

    [Fact]
    public async Task Organization_AddThenGet_RoundTrips()
    {
        var company = AdministrativeOrganization.CreateRootCompany(Tenant, "comp-001", "ACME", 0);

        await _organizationStore.AddAsync(company, Ct);
        var loaded = await _organizationStore.GetAsync(Tenant, "comp-001", Ct);

        Assert.NotNull(loaded);
        Assert.Equal("ACME", loaded.Name);
        Assert.Equal(AdministrativeOrganizationType.Company, loaded.Type);
        Assert.Null(loaded.ParentOrganizationNId);
        Assert.Equal(OrganizationStatus.Active, loaded.Status);
        Assert.Equal(1, loaded.OrganizationRevision);
    }

    [Fact]
    public async Task Organization_Get_Missing_ReturnsNull()
    {
        Assert.Null(await _organizationStore.GetAsync(Tenant, "comp-missing", Ct));
    }

    [Fact]
    public async Task Organization_Add_DuplicateNId_ThrowsConcurrencyException()
    {
        await _organizationStore.AddAsync(Company("comp-001", "ACME"), Ct);

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            _organizationStore.AddAsync(Company("comp-001", "BETA"), Ct));
    }

    [Fact]
    public async Task Organization_Update_StaleVersion_ThrowsConcurrencyException()
    {
        var company = Company("comp-001", "ACME");
        await _organizationStore.AddAsync(company, Ct);
        var staleOptimistic = company.OptimisticVersion;
        var staleConcurrency = company.ConcurrencyVersion;

        company.Rename("ACME v2");
        await _organizationStore.UpdateAsync(company, staleOptimistic, staleConcurrency, Ct);

        company.Rename("ACME v3");
        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            _organizationStore.UpdateAsync(company, staleOptimistic, staleConcurrency, Ct));
    }

    [Fact]
    public async Task Organization_NameAvailable_SameParentDuplicate_DetectsCaseInsensitive()
    {
        var company = Company("comp-001", "ACME");
        var dept = Child(company, AdministrativeOrganizationType.Department, "dept-001", "R&D");
        await _organizationStore.AddAsync(company, Ct);
        await _organizationStore.AddAsync(dept, Ct);

        Assert.False(await _organizationStore.NameAvailableAsync(Tenant, company.NId, "r&d", null, Ct));
        Assert.True(await _organizationStore.NameAvailableAsync(Tenant, company.NId, "Finance", null, Ct));
    }

    [Fact]
    public async Task Organization_NameAvailable_ExcludeSelf_RenameAllowed()
    {
        var company = Company("comp-001", "ACME");
        var dept = Child(company, AdministrativeOrganizationType.Department, "dept-001", "R&D");
        await _organizationStore.AddAsync(company, Ct);
        await _organizationStore.AddAsync(dept, Ct);

        Assert.True(await _organizationStore.NameAvailableAsync(Tenant, company.NId, "R&D", dept.NId, Ct));
    }

    [Fact]
    public async Task Organization_NameAvailable_DifferentParent_AllowsDuplicate()
    {
        var companyA = Company("comp-a", "ACME");
        var companyB = Company("comp-b", "BETA");
        await _organizationStore.AddAsync(companyA, Ct);
        await _organizationStore.AddAsync(companyB, Ct);
        var deptA = Child(companyA, AdministrativeOrganizationType.Department, "dept-001", "R&D");
        await _organizationStore.AddAsync(deptA, Ct);

        // companyA 下已有 "R&D",不影响 companyB 下同名可用
        Assert.True(await _organizationStore.NameAvailableAsync(Tenant, companyB.NId, "R&D", null, Ct));
    }

    [Fact]
    public async Task Organization_GetDependencyCounts_CountsChildrenPositionsAndAssignments()
    {
        var company = Company("comp-001", "ACME");
        var dept = Child(company, AdministrativeOrganizationType.Department, "dept-001", "R&D");
        var section = Child(dept, AdministrativeOrganizationType.Section, "sec-001", "QA");
        await _organizationStore.AddAsync(company, Ct);
        await _organizationStore.AddAsync(dept, Ct);
        await _organizationStore.AddAsync(section, Ct);

        var position = Position.Create(
            Tenant, "pos-001", Tenant, dept.NId, dept.Id, false, true, "工程师", null, 0);
        await _positionStore.AddAsync(position, Ct);
        var assignment = UserAssignment.Create(
            Tenant, "assn-001", "user-001", "张三", dept.NId, position.NId, position.Id,
            false, true, true, true, false, Now.AddDays(-1), Now.AddDays(5), Now);
        await _assignmentStore.AddAsync(assignment, Ct);

        // company: 直接子=dept;dept 无岗位/任职
        var companyCounts = await _organizationStore.GetDependencyCountsAsync(Tenant, company.NId, Now, Ct);
        Assert.Equal(1, companyCounts.ActiveChildCount);
        Assert.Equal(0, companyCounts.ActivePositionCount);
        Assert.Equal(0, companyCounts.ActiveOrFutureAssignmentCount);

        // dept: 直接子=section;岗位=1;当前任职=1
        var deptCounts = await _organizationStore.GetDependencyCountsAsync(Tenant, dept.NId, Now, Ct);
        Assert.Equal(1, deptCounts.ActiveChildCount);
        Assert.Equal(1, deptCounts.ActivePositionCount);
        Assert.Equal(1, deptCounts.ActiveOrFutureAssignmentCount);
    }

    [Fact]
    public async Task Organization_GetDependencyCounts_EndedAssignmentNotCounted()
    {
        var company = Company("comp-001", "ACME");
        var dept = Child(company, AdministrativeOrganizationType.Department, "dept-001", "R&D");
        await _organizationStore.AddAsync(company, Ct);
        await _organizationStore.AddAsync(dept, Ct);
        var position = Position.Create(Tenant, "pos-001", Tenant, dept.NId, dept.Id, false, true, "工程师", null, 0);
        await _positionStore.AddAsync(position, Ct);

        // 已结束任职(创建时 now 更早,当前 Now 已过 EffectiveTo)
        var ended = UserAssignment.Create(
            Tenant, "assn-ended", "user-001", "张三", dept.NId, position.NId, position.Id,
            false, true, true, true, false, Now.AddDays(-30), Now.AddDays(-10), Now.AddDays(-40));
        await _assignmentStore.AddAsync(ended, Ct);

        var counts = await _organizationStore.GetDependencyCountsAsync(Tenant, dept.NId, Now, Ct);
        Assert.Equal(0, counts.ActiveOrFutureAssignmentCount);
    }

    [Fact]
    public async Task Organization_GetSubtreeCounts_IncludesDescendants()
    {
        var company = Company("comp-001", "ACME");
        var dept = Child(company, AdministrativeOrganizationType.Department, "dept-001", "R&D");
        var section = Child(dept, AdministrativeOrganizationType.Section, "sec-001", "QA");
        await _organizationStore.AddAsync(company, Ct);
        await _organizationStore.AddAsync(dept, Ct);
        await _organizationStore.AddAsync(section, Ct);

        var position = Position.Create(Tenant, "pos-001", Tenant, section.NId, section.Id, false, true, "工程师", null, 0);
        await _positionStore.AddAsync(position, Ct);
        var assignment = UserAssignment.Create(
            Tenant, "assn-001", "user-001", "张三", section.NId, position.NId, position.Id,
            false, true, true, true, false, Now.AddDays(-1), Now.AddDays(5), Now);
        await _assignmentStore.AddAsync(assignment, Ct);

        var deptSubtree = await _organizationStore.GetSubtreeCountsAsync(Tenant, dept.NId, Now, Ct);
        Assert.Equal(2, deptSubtree.OrganizationCount); // dept + section
        Assert.Equal(1, deptSubtree.PositionCount);
        Assert.Equal(1, deptSubtree.AssignmentCount);

        var companySubtree = await _organizationStore.GetSubtreeCountsAsync(Tenant, company.NId, Now, Ct);
        Assert.Equal(3, companySubtree.OrganizationCount);
        Assert.Equal(1, companySubtree.PositionCount);
        Assert.Equal(1, companySubtree.AssignmentCount);
    }

    [Fact]
    public async Task Organization_GetDescendantNIds_ReturnsSubtreeIncludingRoot()
    {
        var company = Company("comp-001", "ACME");
        var dept = Child(company, AdministrativeOrganizationType.Department, "dept-001", "R&D");
        var section = Child(dept, AdministrativeOrganizationType.Section, "sec-001", "QA");
        await _organizationStore.AddAsync(company, Ct);
        await _organizationStore.AddAsync(dept, Ct);
        await _organizationStore.AddAsync(section, Ct);

        var subtree = await _organizationStore.GetDescendantNIdsAsync(Tenant, dept.NId, Ct);

        Assert.Equal(["dept-001", "sec-001"], subtree.OrderBy(x => x).ToArray());
    }

    // =====================================================================
    // 岗位仓储
    // =====================================================================

    [Fact]
    public async Task Position_AddThenGet_RoundTrips()
    {
        var company = Company("comp-001", "ACME");
        await _organizationStore.AddAsync(company, Ct);
        var position = Position.Create(
            Tenant, "pos-001", Tenant, company.NId, company.Id, false, true, "后端工程师", "服务端", 0);

        await _positionStore.AddAsync(position, Ct);
        var loaded = await _positionStore.GetAsync(Tenant, "pos-001", Ct);

        Assert.NotNull(loaded);
        Assert.Equal("后端工程师", loaded.Name);
        Assert.Equal("服务端", loaded.Description);
        Assert.Equal(company.NId, loaded.OrganizationNId);
        Assert.Equal(PositionStatus.Active, loaded.Status);
    }

    [Fact]
    public async Task Position_NameAvailable_SameOrgDuplicate_Detected()
    {
        var company = Company("comp-001", "ACME");
        await _organizationStore.AddAsync(company, Ct);
        await _positionStore.AddAsync(Position.Create(Tenant, "pos-001", Tenant, company.NId, company.Id, false, true, "工程师", null, 0), Ct);

        Assert.False(await _positionStore.NameAvailableAsync(Tenant, company.NId, "工程师", null, Ct));
        Assert.False(await _positionStore.NameAvailableAsync(Tenant, company.NId, "工程师 ", null, Ct));
    }

    [Fact]
    public async Task Position_NameAvailable_DifferentOrg_AllowsDuplicate()
    {
        var companyA = Company("comp-a", "ACME");
        var companyB = Company("comp-b", "BETA");
        await _organizationStore.AddAsync(companyA, Ct);
        await _organizationStore.AddAsync(companyB, Ct);
        await _positionStore.AddAsync(Position.Create(Tenant, "pos-a", Tenant, companyA.NId, companyA.Id, false, true, "工程师", null, 0), Ct);

        Assert.True(await _positionStore.NameAvailableAsync(Tenant, companyB.NId, "工程师", null, Ct));
    }

    [Fact]
    public async Task Position_Update_StaleVersion_ThrowsConcurrencyException()
    {
        var company = Company("comp-001", "ACME");
        await _organizationStore.AddAsync(company, Ct);
        var position = Position.Create(Tenant, "pos-001", Tenant, company.NId, company.Id, false, true, "工程师", null, 0);
        await _positionStore.AddAsync(position, Ct);
        var staleOptimistic = position.OptimisticVersion;
        var staleConcurrency = position.ConcurrencyVersion;

        position.Rename("高级工程师");
        await _positionStore.UpdateAsync(position, staleOptimistic, staleConcurrency, Ct);

        position.Rename("资深工程师");
        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            _positionStore.UpdateAsync(position, staleOptimistic, staleConcurrency, Ct));
    }

    [Fact]
    public async Task Position_Add_DuplicateNId_ThrowsConcurrencyException()
    {
        var company = Company("comp-001", "ACME");
        await _organizationStore.AddAsync(company, Ct);
        await _positionStore.AddAsync(Position.Create(Tenant, "pos-001", Tenant, company.NId, company.Id, false, true, "工程师", null, 0), Ct);

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            _positionStore.AddAsync(Position.Create(Tenant, "pos-001", Tenant, company.NId, company.Id, false, true, "经理", null, 0), Ct));
    }

    // =====================================================================
    // 用户任职仓储
    // =====================================================================

    [Fact]
    public async Task Assignment_AddThenGet_RoundTrips()
    {
        var position = await CreatePositionAsync("pos-001");
        var assignment = UserAssignment.Create(
            Tenant, "assn-001", "user-001", "张三", position.OrganizationNId, position.NId, position.Id,
            false, true, true, true, false, Now.AddDays(-1), Now.AddDays(5), Now);

        await _assignmentStore.AddAsync(assignment, Ct);
        var loaded = await _assignmentStore.GetAsync(Tenant, "assn-001", Ct);

        Assert.NotNull(loaded);
        Assert.Equal("user-001", loaded.UserNId);
        Assert.Equal(position.NId, loaded.PositionNId);
        Assert.Equal(AssignmentState.Enabled, loaded.State);
        Assert.False(loaded.IsPrimary);
        // SQLite 无原生 DateTimeOffset,经 SqlSugar TEXT 存储后偏移丢失(墙钟保留);PostgreSQL 验证标记「待验收」。
        Assert.Equal(Now.AddDays(-1).DateTime, loaded.EffectiveFrom.DateTime);
        Assert.Equal(Now.AddDays(5).DateTime, loaded.EffectiveTo!.Value.DateTime);
    }

    [Fact]
    public async Task Assignment_GetAssignmentsForUser_ReturnsAllForUser()
    {
        var position = await CreatePositionAsync("pos-001");
        await _assignmentStore.AddAsync(CreateAssignment(position, "assn-001", "user-001", false, Now.AddDays(-1), Now.AddDays(5), Now), Ct);
        await _assignmentStore.AddAsync(CreateAssignment(position, "assn-002", "user-001", true, Now.AddDays(5), Now.AddDays(10), Now), Ct);
        await _assignmentStore.AddAsync(CreateAssignment(position, "assn-003", "user-002", false, Now.AddDays(-1), Now.AddDays(5), Now), Ct);

        var forUser = await _assignmentStore.GetAssignmentsForUserAsync(Tenant, "user-001", Ct);

        Assert.Equal(2, forUser.Count);
        Assert.Contains(forUser, a => a.NId == "assn-001");
        Assert.Contains(forUser, a => a.NId == "assn-002");
    }

    [Fact]
    public async Task Assignment_HasActiveOrFutureByPosition_CurrentTrue_EndedFalse_CancelledFalse()
    {
        var position = await CreatePositionAsync("pos-001");

        var current = CreateAssignment(position, "assn-current", "user-001", false, Now.AddDays(-1), Now.AddDays(5), Now);
        await _assignmentStore.AddAsync(current, Ct);
        var ended = CreateAssignment(position, "assn-ended", "user-002", false, Now.AddDays(-30), Now.AddDays(-10), Now.AddDays(-40));
        await _assignmentStore.AddAsync(ended, Ct);
        var scheduled = CreateAssignment(position, "assn-scheduled", "user-003", false, Now.AddDays(3), Now.AddDays(10), Now);
        scheduled.Cancel(Now, "调整");
        await _assignmentStore.AddAsync(scheduled, Ct);

        Assert.True(await _assignmentStore.HasActiveOrFutureByPositionAsync(Tenant, position.NId, Now, Ct));

        // 仅剩已结束/已取消时返回 false
        var otherPosition = await CreatePositionAsync("pos-002", "comp-002");
        await _assignmentStore.AddAsync(CreateAssignment(otherPosition, "assn-e2", "user-004", false, Now.AddDays(-30), Now.AddDays(-10), Now.AddDays(-40)), Ct);
        await _assignmentStore.AddAsync(CreateAssignment(otherPosition, "assn-c2", "user-005", false, Now.AddDays(3), Now.AddDays(10), Now), Ct);
        var toCancel = (await _assignmentStore.GetAssignmentsForUserAsync(Tenant, "user-005", Ct)).Single();
        var expectedOptimistic = toCancel.OptimisticVersion;
        var expectedConcurrency = toCancel.ConcurrencyVersion;
        toCancel.Cancel(Now, "调整");
        await _assignmentStore.UpdateAsync(toCancel, expectedOptimistic, expectedConcurrency, Ct);

        Assert.False(await _assignmentStore.HasActiveOrFutureByPositionAsync(Tenant, otherPosition.NId, Now, Ct));
    }

    [Fact]
    public async Task Assignment_Update_StaleVersion_ThrowsConcurrencyException()
    {
        var position = await CreatePositionAsync("pos-001");
        var assignment = CreateAssignment(position, "assn-001", "user-001", false, Now.AddDays(3), Now.AddDays(10), Now);
        await _assignmentStore.AddAsync(assignment, Ct);
        var staleOptimistic = assignment.OptimisticVersion;
        var staleConcurrency = assignment.ConcurrencyVersion;

        assignment.UpdateScheduledPeriod(Now.AddDays(4), Now.AddDays(11), Now);
        await _assignmentStore.UpdateAsync(assignment, staleOptimistic, staleConcurrency, Ct);

        assignment.UpdateScheduledPeriod(Now.AddDays(5), Now.AddDays(12), Now);
        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            _assignmentStore.UpdateAsync(assignment, staleOptimistic, staleConcurrency, Ct));
    }

    // =====================================================================
    // 按用户 advisory lock(SQLite 替身)
    // =====================================================================

    [Fact]
    public async Task AdvisoryLock_Sqlite_SerializesSameUser()
    {
        await using var first = await _advisoryLock.AcquireAsync(Tenant, "user-lock-1", Ct);
        var secondEntered = false;

        var second = Task.Run(async () =>
        {
            await using var handle = await _advisoryLock.AcquireAsync(Tenant, "user-lock-1", Ct);
            secondEntered = true;
        });

        await Task.Delay(200);
        Assert.False(secondEntered);

        await first.CommitAsync(Ct);
        await second;
        Assert.True(secondEntered);
    }

    [Fact]
    public async Task AdvisoryLock_Sqlite_DifferentUser_DoesNotSerialize()
    {
        await using var first = await _advisoryLock.AcquireAsync(Tenant, "user-lock-a", Ct);
        var otherEntered = false;

        var other = Task.Run(async () =>
        {
            await using var handle = await _advisoryLock.AcquireAsync(Tenant, "user-lock-b", Ct);
            otherEntered = true;
        });

        await other;
        Assert.True(otherEntered);
    }

    [Fact]
    public async Task AdvisoryLock_Sqlite_CommitAndDispose_AreIdempotent()
    {
        var handle = await _advisoryLock.AcquireAsync(Tenant, "user-lock-c", Ct);
        await handle.CommitAsync(Ct);
        await handle.CommitAsync(Ct);      // 二次提交无害
        await handle.DisposeAsync();       // 已提交后释放无害

        await using var again = await _advisoryLock.AcquireAsync(Tenant, "user-lock-c", Ct);
        await again.CommitAsync(Ct);
    }

    [Fact]
    public async Task AdvisoryLock_Sqlite_DisposeWithoutCommit_Releases()
    {
        var handle = await _advisoryLock.AcquireAsync(Tenant, "user-lock-d", Ct);
        await handle.DisposeAsync();       // 未提交 → 释放(等价回滚)

        await using var again = await _advisoryLock.AcquireAsync(Tenant, "user-lock-d", Ct);
        await again.CommitAsync(Ct);
    }

    // =====================================================================
    // 聚合构建助手
    // =====================================================================

    private static AdministrativeOrganization Company(string nid, string name) =>
        AdministrativeOrganization.CreateRootCompany(Tenant, nid, name, 0);

    private static AdministrativeOrganization Child(
        AdministrativeOrganization parent,
        AdministrativeOrganizationType type,
        string nid,
        string name) =>
        AdministrativeOrganization.CreateChild(
            Tenant, nid, name, type,
            parent.TenantNId, parent.NId, parent.Id, parent.IsDeleted,
            parent.Status == OrganizationStatus.Active, parent.Type, 0);

    private async Task<Position> CreatePositionAsync(string positionNId, string companyNId = "comp-001")
    {
        var company = Company(companyNId, "ACME");
        await _organizationStore.AddAsync(company, Ct);
        var position = Position.Create(
            Tenant, positionNId, Tenant, company.NId, company.Id, false, true, "工程师", null, 0);
        await _positionStore.AddAsync(position, Ct);
        return position;
    }

    private static UserAssignment CreateAssignment(
        Position position,
        string nId,
        string userNId,
        bool isPrimary,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        DateTimeOffset now) =>
        UserAssignment.Create(
            Tenant, nId, userNId, "张三", position.OrganizationNId, position.NId, position.Id,
            position.IsDeleted, organizationActive: true, positionActive: true,
            organizationMatchesPosition: true, isPrimary, effectiveFrom, effectiveTo, now);
}
