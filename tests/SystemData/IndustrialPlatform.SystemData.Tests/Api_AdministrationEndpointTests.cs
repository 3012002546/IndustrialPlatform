using System.Net;
using System.Text;
using System.Text.Json;
using IndustrialPlatform.SystemData.Application.Assignments;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Application.Authorization;
using IndustrialPlatform.SystemData.Application.IdentityDirectory;
using IndustrialPlatform.SystemData.Application.Organizations;
using IndustrialPlatform.SystemData.Application.Positions;
using IndustrialPlatform.SystemData.Domain.Assignments;
using IndustrialPlatform.SystemData.Domain.Organizations;
using IndustrialPlatform.SystemData.Domain.Positions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IndustrialPlatform.SystemData.Api.Tests;

/// <summary>
/// 组织/岗位/任职控制面端点全栈测试(TASK-SD-006,05 方案 §9.3 全部 17 个端点):
/// 真实 Program/控制器/服务,以进程内假存储与用户目录替换端口,测试方案替换 JwtBearer
/// 验证 401(未认证)/403(缺权限)信封,以及成功/400/404/409/503、租户伪造、
/// 双版本冲突、用户目录失败与响应无数据库审计字段。
/// </summary>
public sealed class AdministrationEndpointTests
{
    private const string Tenant = "tenant-001";
    private const string Actor = "user-actor";
    private const string User = "user-1";
    private const string OrgView = "systemdata.organization.view";
    private const string OrgCreate = "systemdata.organization.create";
    private const string OrgUpdate = "systemdata.organization.update";
    private const string OrgMove = "systemdata.organization.move";
    private const string OrgStatus = "systemdata.organization.status";
    private const string PosView = "systemdata.position.view";
    private const string PosCreate = "systemdata.position.create";
    private const string PosUpdate = "systemdata.position.update";
    private const string PosStatus = "systemdata.position.status";
    private const string AssignmentView = "systemdata.assignment.view";
    private const string AssignmentManage = "systemdata.assignment.manage";

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    // =====================================================================
    // 组织(GET tree / GET detail / POST / PUT / move-preview / move / status)
    // =====================================================================

    [Fact]
    public async Task GetTree_Success_ReturnsForestEnvelope()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        await SeedCompanyAsync(orgs, "company-b", "B 公司", displayOrder: 2);
        await SeedCompanyAsync(orgs, "company-a", "A 公司", displayOrder: 1);
        await SeedChildAsync(orgs, "dept-a", "研发部", "Department", "company-a");
        using var factory = CreateFactory(orgs: orgs, user: Admin());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/organizations/tree");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetArrayLength());
        Assert.Equal("company-a", data[0].GetProperty("nId").GetString());
        Assert.Equal(1, data[0].GetProperty("children").GetArrayLength());
        AssertNoDatabaseAuditFields(data[0]);
    }

    [Fact]
    public async Task GetTree_WithoutAuth_Returns401Envelope()
    {
        using var factory = CreateFactory(user: null);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/organizations/tree");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("401", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetTree_AuthenticatedWithoutPermission_Returns403Envelope()
    {
        using var factory = CreateFactory(user: Viewer());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/organizations/tree");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("SD_PERMISSION_DENIED", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetOrganization_Success_ReturnsDetailWithVersions()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        await SeedCompanyAsync(orgs);
        using var factory = CreateFactory(orgs: orgs, user: Admin());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/organizations/company-a");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload.RootElement.GetProperty("data");
        Assert.Equal("A 公司", data.GetProperty("name").GetString());
        Assert.Equal("Company", data.GetProperty("type").GetString());
        Assert.Equal(0, data.GetProperty("optimisticVersion").GetInt32());
        Assert.NotEqual(string.Empty, data.GetProperty("concurrencyVersion").GetString());
        AssertNoDatabaseAuditFields(data);
    }

    [Fact]
    public async Task GetOrganization_Unknown_Returns404Envelope()
    {
        using var factory = CreateFactory(user: Admin());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/organizations/missing");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("SD_NOT_FOUND", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetOrganization_CrossTenant_Returns404Envelope()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        await SeedCompanyAsync(orgs);
        // 请求者属于其他租户 → 租户过滤后 404,绝不泄漏对象存在性。
        using var factory = CreateFactory(orgs: orgs, user: Admin(tenantNId: "tenant-002"));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/organizations/company-a");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("SD_NOT_FOUND", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateOrganization_Success_ReturnsDetailAndAudits()
    {
        var audit = new RecordingLocalAuditCommand();
        using var factory = CreateFactory(audit: audit, user: Admin());
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, HttpMethod.Post, "/api/v1/organizations",
            Json(new { nId = "company-a", name = "A 公司", type = "Company", displayOrder = 1 }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload.RootElement.GetProperty("data");
        Assert.Equal("company-a", data.GetProperty("nId").GetString());
        Assert.Equal("Active", data.GetProperty("status").GetString());
        AssertNoDatabaseAuditFields(data);
        Assert.Contains(audit.Entries, e => e.Action == "organization.create" && e.ObjectNId == "company-a");
    }

    [Fact]
    public async Task CreateOrganization_MissingName_Returns400ValidationEnvelope()
    {
        using var factory = CreateFactory(user: Admin());
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, HttpMethod.Post, "/api/v1/organizations",
            Json(new { nId = "company-a", type = "Company", displayOrder = 1 }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("SD_VALIDATION_FAILED", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateOrganization_WithoutAuth_Returns401Envelope()
    {
        using var factory = CreateFactory(user: null);
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, HttpMethod.Post, "/api/v1/organizations",
            Json(new { nId = "company-a", name = "A 公司", type = "Company", displayOrder = 1 }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("401", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UpdateOrganization_Success_ReturnsRenamedDetail()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        var created = await SeedCompanyViaServiceAsync(orgs);
        using var factory = CreateFactory(orgs: orgs, user: Admin());
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, HttpMethod.Put, "/api/v1/organizations/company-a",
            Json(new
            {
                name = "A 公司(改)",
                displayOrder = 3,
                expectedOptimisticVersion = created.OptimisticVersion,
                expectedConcurrencyVersion = created.ConcurrencyVersion,
            }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("A 公司(改)", payload.RootElement.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public async Task UpdateOrganization_StaleVersions_Returns409ConcurrencyEnvelope()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        var created = await SeedCompanyViaServiceAsync(orgs);
        var staleOptimistic = created.OptimisticVersion;
        var staleConcurrency = created.ConcurrencyVersion;
        await UpdateCompanyAsync(orgs, created); // 推进版本,请求仍用旧版本 → 409
        using var factory = CreateFactory(orgs: orgs, user: Admin());
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, HttpMethod.Put, "/api/v1/organizations/company-a",
            Json(new
            {
                name = "过期版本再改",
                displayOrder = 2,
                expectedOptimisticVersion = staleOptimistic,
                expectedConcurrencyVersion = staleConcurrency,
            }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("SD_CONCURRENCY_CONFLICT", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UpdateOrganization_WithoutUpdatePermission_Returns403Envelope()
    {
        using var factory = CreateFactory(user: Viewer(OrgView, OrgCreate, OrgMove, OrgStatus));
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, HttpMethod.Put, "/api/v1/organizations/company-a",
            Json(new { name = "X", displayOrder = 1, expectedOptimisticVersion = 0, expectedConcurrencyVersion = Guid.Empty }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("SD_PERMISSION_DENIED", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PreviewMove_Success_ReturnsRevisionAndVersions()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        await SeedCompanyAsync(orgs, "company-a", "A 公司");
        await SeedCompanyAsync(orgs, "company-b", "B 公司");
        await SeedChildAsync(orgs, "dept-a", "研发部", "Department", "company-a");
        using var factory = CreateFactory(orgs: orgs, user: Admin());
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, HttpMethod.Post, "/api/v1/organizations/dept-a/move-preview",
            Json(new { targetParentOrganizationNId = "company-b" }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetProperty("organizationRevision").GetInt32());
        Assert.Equal(1, data.GetProperty("subtreeOrganizationCount").GetInt32());
        Assert.Equal(0, data.GetProperty("expectedOptimisticVersion").GetInt32());
        Assert.NotEqual(string.Empty, data.GetProperty("expectedConcurrencyVersion").GetString());
        AssertNoDatabaseAuditFields(data);
    }

    [Fact]
    public async Task PreviewMove_Cycle_Returns409CycleEnvelope()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        await SeedCompanyAsync(orgs);
        await SeedChildAsync(orgs, "dept-1", "研发部", "Department", "company-a");
        await SeedChildAsync(orgs, "dept-2", "前端组", "Department", "dept-1");
        using var factory = CreateFactory(orgs: orgs, user: Admin());
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, HttpMethod.Post, "/api/v1/organizations/dept-1/move-preview",
            Json(new { targetParentOrganizationNId = "dept-2" }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("SD_ORG_CYCLE", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PreviewMove_TypeMatrixViolation_Returns400ParentTypeInvalidEnvelope()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        await SeedCompanyAsync(orgs);
        await SeedChildAsync(orgs, "dept-a", "研发部", "Department", "company-a");
        await SeedChildAsync(orgs, "team-a", "平台组", "Team", "dept-a");
        await SeedChildAsync(orgs, "dept-b", "市场部", "Department", "company-a");
        using var factory = CreateFactory(orgs: orgs, user: Admin());
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, HttpMethod.Post, "/api/v1/organizations/dept-b/move-preview",
            Json(new { targetParentOrganizationNId = "team-a" }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("SD_ORG_PARENT_TYPE_INVALID", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Move_Success_CommitsMove()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        await SeedCompanyAsync(orgs, "company-a", "A 公司");
        await SeedCompanyAsync(orgs, "company-b", "B 公司");
        await SeedChildAsync(orgs, "dept-a", "研发部", "Department", "company-a");
        using var factory = CreateFactory(orgs: orgs, user: Admin());
        using var client = factory.CreateClient();

        using var previewResponse = await SendJsonAsync(client, HttpMethod.Post, "/api/v1/organizations/dept-a/move-preview",
            Json(new { targetParentOrganizationNId = "company-b" }));
        using var previewPayload = JsonDocument.Parse(await previewResponse.Content.ReadAsStreamAsync());
        var preview = previewPayload.RootElement.GetProperty("data");

        using var response = await SendJsonAsync(client, HttpMethod.Post, "/api/v1/organizations/dept-a/move",
            Json(new
            {
                targetParentOrganizationNId = "company-b",
                previewOrganizationRevision = preview.GetProperty("organizationRevision").GetInt64(),
                expectedOptimisticVersion = preview.GetProperty("expectedOptimisticVersion").GetInt64(),
                expectedConcurrencyVersion = preview.GetProperty("expectedConcurrencyVersion").GetString(),
                reason = "组织调整",
            }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("company-b", payload.RootElement.GetProperty("data").GetProperty("parentOrganizationNId").GetString());
    }

    [Fact]
    public async Task Move_StalePreviewRevision_Returns409ConcurrencyEnvelope()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        await SeedCompanyAsync(orgs, "company-a", "A 公司");
        await SeedCompanyAsync(orgs, "company-b", "B 公司");
        await SeedChildAsync(orgs, "dept-a", "研发部", "Department", "company-a");
        var dept = (await orgs.GetAsync(Tenant, "dept-a", CancellationToken.None))!;
        using var factory = CreateFactory(orgs: orgs, user: Admin());
        using var client = factory.CreateClient();

        using var previewResponse = await SendJsonAsync(client, HttpMethod.Post, "/api/v1/organizations/dept-a/move-preview",
            Json(new { targetParentOrganizationNId = "company-b" }));
        using var previewPayload = JsonDocument.Parse(await previewResponse.Content.ReadAsStreamAsync());
        var preview = previewPayload.RootElement.GetProperty("data");
        using var firstMove = await SendJsonAsync(client, HttpMethod.Post, "/api/v1/organizations/dept-a/move",
            Json(new
            {
                targetParentOrganizationNId = "company-b",
                previewOrganizationRevision = preview.GetProperty("organizationRevision").GetInt32(),
                expectedOptimisticVersion = dept.OptimisticVersion,
                expectedConcurrencyVersion = dept.ConcurrencyVersion,
            }));
        Assert.Equal(HttpStatusCode.OK, firstMove.StatusCode);
        using var firstMovePayload = JsonDocument.Parse(await firstMove.Content.ReadAsStreamAsync());
        var moved = firstMovePayload.RootElement.GetProperty("data");

        // 复用已提交的预览修订(版本取提交后最新)→ 修订号过期 → 409。
        using var response = await SendJsonAsync(client, HttpMethod.Post, "/api/v1/organizations/dept-a/move",
            Json(new
            {
                targetParentOrganizationNId = "company-b",
                previewOrganizationRevision = preview.GetProperty("organizationRevision").GetInt32(),
                expectedOptimisticVersion = moved.GetProperty("optimisticVersion").GetInt64(),
                expectedConcurrencyVersion = moved.GetProperty("concurrencyVersion").GetString(),
            }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("SD_CONCURRENCY_CONFLICT", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SetOrganizationStatus_DeactivateWithDependencies_Returns409Envelope()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        orgs.DependencyCounts["company-a"] = new OrganizationDependencyCounts(1, 0, 0);
        await SeedCompanyAsync(orgs);
        using var factory = CreateFactory(orgs: orgs, user: Admin());
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, HttpMethod.Put, "/api/v1/organizations/company-a/status",
            Json(new { status = "Inactive" }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("SD_ORG_HAS_ACTIVE_DEPENDENCIES", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SetOrganizationStatus_InvalidStatus_Returns400ValidationEnvelope()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        await SeedCompanyAsync(orgs);
        using var factory = CreateFactory(orgs: orgs, user: Admin());
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, HttpMethod.Put, "/api/v1/organizations/company-a/status",
            Json(new { status = "Banana" }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("SD_VALIDATION_FAILED", payload.RootElement.GetProperty("code").GetString());
    }

    // =====================================================================
    // 岗位(GET list / POST / PUT / status)
    // =====================================================================

    [Fact]
    public async Task ListPositions_Success_ReturnsPageEnvelope()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        var positions = new FakePositionStore();
        await SeedCompanyAsync(orgs, "company-a", "A 公司");
        await SeedPositionAsync(orgs, positions, "pos-a1", "company-a", "后端工程师");
        await SeedPositionAsync(orgs, positions, "pos-a2", "company-a", "前端工程师");
        using var factory = CreateFactory(orgs: orgs, positions: positions, user: Admin());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/positions?organizationNId=company-a&pageIndex=1&pageSize=10");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetProperty("total").GetInt32());
        Assert.Equal(2, data.GetProperty("items").GetArrayLength());
        AssertNoDatabaseAuditFields(data.GetProperty("items")[0]);
    }

    [Fact]
    public async Task ListPositions_InvalidPagination_Returns400Envelope()
    {
        using var factory = CreateFactory(user: Admin());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/positions?pageIndex=0");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("SD_VALIDATION_FAILED", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreatePosition_Success_ReturnsPositionWithOrganizationName()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        var positions = new FakePositionStore();
        await SeedCompanyAsync(orgs);
        using var factory = CreateFactory(orgs: orgs, positions: positions, user: Admin());
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, HttpMethod.Post, "/api/v1/positions",
            Json(new { nId = "pos-1", organizationNId = "company-a", name = "后端工程师", displayOrder = 1 }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload.RootElement.GetProperty("data");
        Assert.Equal("后端工程师", data.GetProperty("name").GetString());
        Assert.Equal("A 公司", data.GetProperty("organizationName").GetString());
        AssertNoDatabaseAuditFields(data);
    }

    [Fact]
    public async Task CreatePosition_UnknownOrganization_Returns404Envelope()
    {
        using var factory = CreateFactory(user: Admin());
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, HttpMethod.Post, "/api/v1/positions",
            Json(new { nId = "pos-1", organizationNId = "missing", name = "X", displayOrder = 1 }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("SD_NOT_FOUND", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreatePosition_WithoutCreatePermission_Returns403Envelope()
    {
        using var factory = CreateFactory(user: Viewer(PosView));
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, HttpMethod.Post, "/api/v1/positions",
            Json(new { nId = "pos-1", organizationNId = "company-a", name = "X", displayOrder = 1 }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("SD_PERMISSION_DENIED", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UpdatePosition_Success_ReturnsRenamedPosition()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        var positions = new FakePositionStore();
        await SeedCompanyAsync(orgs);
        var position = await SeedPositionViaServiceAsync(orgs, positions, "pos-1", "company-a", "后端工程师");
        using var factory = CreateFactory(orgs: orgs, positions: positions, user: Admin());
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, HttpMethod.Put, "/api/v1/positions/pos-1",
            Json(new
            {
                name = "资深后端工程师",
                description = "新描述",
                displayOrder = 5,
                expectedOptimisticVersion = position.OptimisticVersion,
                expectedConcurrencyVersion = position.ConcurrencyVersion,
            }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("资深后端工程师", payload.RootElement.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public async Task UpdatePosition_StaleVersions_Returns409ConcurrencyEnvelope()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        var positions = new FakePositionStore();
        await SeedCompanyAsync(orgs);
        var position = await SeedPositionViaServiceAsync(orgs, positions, "pos-1", "company-a", "后端工程师");
        var staleOptimistic = position.OptimisticVersion;
        var staleConcurrency = position.ConcurrencyVersion;
        await UpdatePositionAsync(orgs, positions, position); // 推进版本,请求仍用旧版本 → 409
        using var factory = CreateFactory(orgs: orgs, positions: positions, user: Admin());
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, HttpMethod.Put, "/api/v1/positions/pos-1",
            Json(new
            {
                name = "过期再改",
                displayOrder = 2,
                expectedOptimisticVersion = staleOptimistic,
                expectedConcurrencyVersion = staleConcurrency,
            }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("SD_CONCURRENCY_CONFLICT", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SetPositionStatus_WithActiveAssignments_Returns409Envelope()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        var positions = new FakePositionStore();
        var assignments = new FakeUserAssignmentStore();
        await SeedCompanyAsync(orgs);
        var position = await SeedPositionViaServiceAsync(orgs, positions, "pos-1", "company-a", "后端工程师");
        SeedActiveAssignment(assignments, position);
        using var factory = CreateFactory(orgs: orgs, positions: positions, assignments: assignments, user: Admin());
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, HttpMethod.Put, "/api/v1/positions/pos-1/status",
            Json(new { status = "Inactive" }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("SD_POSITION_HAS_ACTIVE_ASSIGNMENTS", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SetPositionStatus_DeactivateClean_Succeeds()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        var positions = new FakePositionStore();
        await SeedCompanyAsync(orgs);
        await SeedPositionViaServiceAsync(orgs, positions, "pos-1", "company-a", "后端工程师");
        using var factory = CreateFactory(orgs: orgs, positions: positions, user: Admin());
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, HttpMethod.Put, "/api/v1/positions/pos-1/status",
            Json(new { status = "Inactive" }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Inactive", payload.RootElement.GetProperty("data").GetProperty("status").GetString());
    }

    // =====================================================================
    // 任职(GET timeline / POST / PUT scheduled / end / cancel / primary)
    // =====================================================================

    [Fact]
    public async Task ListAssignments_Success_ReturnsTimelineEnvelope()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        var positions = new FakePositionStore();
        var assignments = new FakeUserAssignmentStore();
        await SeedCompanyAsync(orgs);
        var position = await SeedPositionViaServiceAsync(orgs, positions, "pos-1", "company-a", "后端工程师");
        SeedActiveAssignment(assignments, position, nId: "assign-1");
        using var factory = CreateFactory(orgs: orgs, positions: positions, assignments: assignments, directory: Directory(), user: Admin());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/users/{User}/assignments");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload.RootElement.GetProperty("data");
        Assert.Single(data.EnumerateArray());
        Assert.Equal("assign-1", data[0].GetProperty("nId").GetString());
        Assert.Equal("Current", data[0].GetProperty("state").GetString());
        AssertNoDatabaseAuditFields(data[0]);
    }

    [Fact]
    public async Task CreateAssignment_Success_ReturnsAssignment()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        var positions = new FakePositionStore();
        await SeedCompanyAsync(orgs);
        await SeedPositionViaServiceAsync(orgs, positions, "pos-1", "company-a", "后端工程师");
        using var factory = CreateFactory(orgs: orgs, positions: positions, directory: Directory(), user: Admin());
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, HttpMethod.Post, $"/api/v1/users/{User}/assignments",
            Json(new { nId = "assign-1", positionNId = "pos-1", isPrimary = true, effectiveFrom = Now().AddDays(1), effectiveTo = Now().AddDays(5) }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload.RootElement.GetProperty("data");
        Assert.Equal("assign-1", data.GetProperty("nId").GetString());
        Assert.Equal("张三", data.GetProperty("userDisplayNameSnapshot").GetString());
        Assert.Equal("Scheduled", data.GetProperty("state").GetString());
        AssertNoDatabaseAuditFields(data);
    }

    [Fact]
    public async Task CreateAssignment_OpenEnded_EffectiveToSerializesAsNull()
    {
        // 无界区间(EffectiveTo 为空)线上为 effectiveTo:null(AddControllers 默认不忽略 null 写出)。
        var orgs = new FakeAdministrativeOrganizationStore();
        var positions = new FakePositionStore();
        await SeedCompanyAsync(orgs);
        await SeedPositionViaServiceAsync(orgs, positions, "pos-1", "company-a", "后端工程师");
        using var factory = CreateFactory(orgs: orgs, positions: positions, directory: Directory(), user: Admin());
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, HttpMethod.Post, $"/api/v1/users/{User}/assignments",
            Json(new { nId = "assign-1", positionNId = "pos-1", isPrimary = true, effectiveFrom = Now().AddDays(1) }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload.RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty("effectiveFrom", out _));
        Assert.True(data.TryGetProperty("effectiveTo", out var effectiveTo));
        Assert.Equal(JsonValueKind.Null, effectiveTo.ValueKind);
    }

    [Fact]
    public async Task CreateAssignment_DirectoryUnavailable_Returns503Envelope()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        var positions = new FakePositionStore();
        await SeedCompanyAsync(orgs);
        await SeedPositionViaServiceAsync(orgs, positions, "pos-1", "company-a", "后端工程师");
        var directory = Directory().Unavailable();
        using var factory = CreateFactory(orgs: orgs, positions: positions, directory: directory, user: Admin());
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, HttpMethod.Post, $"/api/v1/users/{User}/assignments",
            Json(new { nId = "assign-1", positionNId = "pos-1", isPrimary = true, effectiveFrom = Now().AddDays(1) }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("SD_IDENTITY_DIRECTORY_UNAVAILABLE", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateAssignment_UserMissingFromDirectory_Returns400Envelope()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        var positions = new FakePositionStore();
        await SeedCompanyAsync(orgs);
        await SeedPositionViaServiceAsync(orgs, positions, "pos-1", "company-a", "后端工程师");
        // 目录可用但不含该用户 → 禁止创建。
        using var factory = CreateFactory(orgs: orgs, positions: positions, directory: new FakeIdentityUserDirectory(), user: Admin());
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, HttpMethod.Post, $"/api/v1/users/{User}/assignments",
            Json(new { nId = "assign-1", positionNId = "pos-1", isPrimary = true, effectiveFrom = Now().AddDays(1) }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("SD_VALIDATION_FAILED", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateAssignment_IntervalOverlap_Returns409Envelope()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        var positions = new FakePositionStore();
        await SeedCompanyAsync(orgs);
        await SeedPositionViaServiceAsync(orgs, positions, "pos-1", "company-a", "后端工程师");
        using var factory = CreateFactory(orgs: orgs, positions: positions, directory: Directory(), user: Admin());
        using var client = factory.CreateClient();

        await CreateAssignmentAsync(client, "assign-1", "pos-1", isPrimary: true, Now().AddDays(1), Now().AddDays(5));
        using var response = await SendJsonAsync(client, HttpMethod.Post, $"/api/v1/users/{User}/assignments",
            Json(new { nId = "assign-2", positionNId = "pos-1", isPrimary = false, effectiveFrom = Now().AddDays(2), effectiveTo = Now().AddDays(4) }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("SD_ASSIGNMENT_INTERVAL_OVERLAP", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateAssignment_WithoutAuth_Returns401Envelope()
    {
        using var factory = CreateFactory(user: null);
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, HttpMethod.Post, $"/api/v1/users/{User}/assignments",
            Json(new { nId = "assign-1", positionNId = "pos-1", isPrimary = true, effectiveFrom = Now().AddDays(1) }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("401", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ListAssignments_WithoutViewPermission_Returns403Envelope()
    {
        using var factory = CreateFactory(user: Viewer(AssignmentManage));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/users/{User}/assignments");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("SD_PERMISSION_DENIED", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UpdateScheduledAssignment_Success_ReturnsUpdatedAssignment()
    {
        var (orgs, positions, assignments) = SeedAssignmentContext();
        using var factory = CreateFactory(orgs: orgs, positions: positions, assignments: assignments, directory: Directory(), user: Admin());
        using var client = factory.CreateClient();
        var created = await CreateAssignmentAsync(client, "assign-1", "pos-1", isPrimary: true, Now().AddDays(1), Now().AddDays(5));
        var effectiveTo = Now().AddDays(9);

        using var response = await SendJsonAsync(client, HttpMethod.Put, "/api/v1/assignments/assign-1",
            Json(new
            {
                effectiveFrom = Now().AddDays(1),
                effectiveTo,
                expectedOptimisticVersion = created.GetProperty("optimisticVersion").GetInt64(),
                expectedConcurrencyVersion = created.GetProperty("concurrencyVersion").GetString(),
            }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // 响应时间保持时刻不变(JSON 携带 +00:00),以 DateTimeOffset 比较时刻,不受本地时区影响。
        Assert.Equal(effectiveTo, payload.RootElement.GetProperty("data").GetProperty("effectiveTo").GetDateTimeOffset());
    }

    [Fact]
    public async Task UpdateScheduledAssignment_StaleVersions_Returns409Envelope()
    {
        var (orgs, positions, assignments) = SeedAssignmentContext();
        using var factory = CreateFactory(orgs: orgs, positions: positions, assignments: assignments, directory: Directory(), user: Admin());
        using var client = factory.CreateClient();
        var created = await CreateAssignmentAsync(client, "assign-1", "pos-1", isPrimary: true, Now().AddDays(1), Now().AddDays(5));

        using var first = await SendJsonAsync(client, HttpMethod.Put, "/api/v1/assignments/assign-1",
            Json(new
            {
                effectiveFrom = Now().AddDays(1),
                effectiveTo = Now().AddDays(9),
                expectedOptimisticVersion = created.GetProperty("optimisticVersion").GetInt64(),
                expectedConcurrencyVersion = created.GetProperty("concurrencyVersion").GetString(),
            }));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var response = await SendJsonAsync(client, HttpMethod.Put, "/api/v1/assignments/assign-1",
            Json(new
            {
                effectiveFrom = Now().AddDays(2),
                effectiveTo = Now().AddDays(9),
                expectedOptimisticVersion = created.GetProperty("optimisticVersion").GetInt64(),
                expectedConcurrencyVersion = created.GetProperty("concurrencyVersion").GetString(),
            }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("SD_CONCURRENCY_CONFLICT", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task EndAssignment_Success_EndsCurrentInterval()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        var positions = new FakePositionStore();
        var assignments = new FakeUserAssignmentStore();
        await SeedCompanyAsync(orgs);
        var position = await SeedPositionViaServiceAsync(orgs, positions, "pos-1", "company-a", "后端工程师");
        SeedActiveAssignment(assignments, position, nId: "assign-1", isPrimary: true);
        using var factory = CreateFactory(orgs: orgs, positions: positions, assignments: assignments, directory: Directory(), user: Admin());
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/v1/assignments/assign-1/end", null);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Ended", payload.RootElement.GetProperty("data").GetProperty("state").GetString());
    }

    [Fact]
    public async Task CancelAssignment_Success_CancelsScheduledInterval()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        var positions = new FakePositionStore();
        var assignments = new FakeUserAssignmentStore();
        await SeedCompanyAsync(orgs);
        await SeedPositionViaServiceAsync(orgs, positions, "pos-1", "company-a", "后端工程师");
        using var factory = CreateFactory(orgs: orgs, positions: positions, assignments: assignments, directory: Directory(), user: Admin());
        using var client = factory.CreateClient();
        await CreateAssignmentAsync(client, "assign-1", "pos-1", isPrimary: true, Now().AddDays(1), Now().AddDays(5));

        using var response = await SendJsonAsync(client, HttpMethod.Post, "/api/v1/assignments/assign-1/cancel",
            Json(new { reason = "计划调整" }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Cancelled", payload.RootElement.GetProperty("data").GetProperty("state").GetString());
        Assert.Equal("计划调整", payload.RootElement.GetProperty("data").GetProperty("cancelReason").GetString());
    }

    [Fact]
    public async Task SetPrimaryAssignment_Success_SwitchesPrimaryAndReturnsTimeline()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        var positions = new FakePositionStore();
        var assignments = new FakeUserAssignmentStore();
        await SeedCompanyAsync(orgs);
        await SeedPositionViaServiceAsync(orgs, positions, "pos-1", "company-a", "后端工程师");
        await SeedPositionViaServiceAsync(orgs, positions, "pos-2", "company-a", "前端工程师");
        using var factory = CreateFactory(orgs: orgs, positions: positions, assignments: assignments, directory: Directory(), user: Admin());
        using var client = factory.CreateClient();
        // 两条任职共享完全相同的区间边界,切换后不出现仅存非主任职的空窗。
        var start = Now();
        var from = start.AddDays(1);
        var to = start.AddDays(5);
        var effectiveOn = start.AddDays(3);
        await CreateAssignmentAsync(client, "assign-1", "pos-1", isPrimary: true, from, to);
        await CreateAssignmentAsync(client, "assign-2", "pos-2", isPrimary: false, from, to);

        using var response = await SendJsonAsync(client, HttpMethod.Post, $"/api/v1/users/{User}/primary-assignment",
            Json(new { targetAssignmentNId = "assign-2", effectiveOn, reason = "职责交接" }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload.RootElement.GetProperty("data");
        Assert.True(data.EnumerateArray().Single(a => a.GetProperty("nId").GetString() == "assign-2").GetProperty("isPrimary").GetBoolean());
        Assert.Equal(
            effectiveOn,
            data.EnumerateArray().Single(a => a.GetProperty("nId").GetString() == "assign-1").GetProperty("effectiveTo").GetDateTimeOffset());
    }

    [Fact]
    public async Task SetPrimaryAssignment_StaleRevision_Returns409Envelope()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        var positions = new FakePositionStore();
        var assignments = new FakeUserAssignmentStore();
        await SeedCompanyAsync(orgs);
        await SeedPositionViaServiceAsync(orgs, positions, "pos-1", "company-a", "后端工程师");
        await SeedPositionViaServiceAsync(orgs, positions, "pos-2", "company-a", "前端工程师");
        using var factory = CreateFactory(orgs: orgs, positions: positions, assignments: assignments, directory: Directory(), user: Admin());
        using var client = factory.CreateClient();
        // 两条任职共享完全相同的区间边界(对齐成功切换用例)。
        var start = Now();
        var from = start.AddDays(1);
        var to = start.AddDays(5);
        var effectiveOn = start.AddDays(3);
        await CreateAssignmentAsync(client, "assign-1", "pos-1", isPrimary: true, from, to);
        await CreateAssignmentAsync(client, "assign-2", "pos-2", isPrimary: false, from, to);
        await SendJsonAsync(client, HttpMethod.Post, $"/api/v1/users/{User}/primary-assignment",
            Json(new { targetAssignmentNId = "assign-2", effectiveOn }));

        using var response = await SendJsonAsync(client, HttpMethod.Post, $"/api/v1/users/{User}/primary-assignment",
            Json(new { targetAssignmentNId = "assign-1", effectiveOn = Now().AddDays(4), expectedUserAssignmentRevision = 0 }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("SD_CONCURRENCY_CONFLICT", payload.RootElement.GetProperty("code").GetString());
    }

    // =====================================================================
    // 夹具与种子
    // =====================================================================

    private static WebApplicationFactory<Program> CreateFactory(
        FakeAdministrativeOrganizationStore? orgs = null,
        FakePositionStore? positions = null,
        FakeUserAssignmentStore? assignments = null,
        FakeIdentityUserDirectory? directory = null,
        RecordingLocalAuditCommand? audit = null,
        TestUser? user = null)
        => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAdministrativeOrganizationStore>();
                services.AddSingleton<IAdministrativeOrganizationStore>(orgs ?? new FakeAdministrativeOrganizationStore());
                services.RemoveAll<IPositionStore>();
                services.AddSingleton<IPositionStore>(positions ?? new FakePositionStore());
                services.RemoveAll<IUserAssignmentStore>();
                services.AddSingleton<IUserAssignmentStore>(assignments ?? new FakeUserAssignmentStore());
                services.RemoveAll<IIdentityUserDirectory>();
                services.AddSingleton<IIdentityUserDirectory>(directory ?? new FakeIdentityUserDirectory());
                services.RemoveAll<IUserAssignmentAdvisoryLock>();
                services.AddSingleton<IUserAssignmentAdvisoryLock>(new FakeUserAssignmentAdvisoryLock());
                services.RemoveAll<ILocalAuditCommand>();
                services.AddSingleton<ILocalAuditCommand>(audit ?? new RecordingLocalAuditCommand());
                services.RemoveAll<ISystemDataPermissionEvaluator>();
                services.AddSingleton<ISystemDataPermissionEvaluator, DenySystemDataPermissionEvaluator>();

                // 以测试方案替换 JwtBearer:按用例注入身份;401/403 信封形状与生产一致。
                services.AddAuthentication(TestAuthDefaults.Scheme)
                    .AddScheme<TestAuthHandlerOptions, TestAuthHandler>(
                        TestAuthDefaults.Scheme,
                        options => options.PrincipalFactory = _ => user is null ? null : TestAuthHandler.BuildPrincipal(user));
            }));

    private sealed class DenySystemDataPermissionEvaluator : ISystemDataPermissionEvaluator
    {
        public Task<SystemDataPermissionDecision> EvaluateAsync(
            SystemDataPermissionRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SystemDataPermissionDecision(false, SystemDataPermissionDenialReason.MissingPermission));
    }

    private static async Task<HttpResponseMessage> SendJsonAsync(HttpClient client, HttpMethod method, string url, string json)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> CreateAssignmentAsync(
        HttpClient client,
        string nId,
        string positionNId,
        bool isPrimary,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo)
    {
        using var response = await SendJsonAsync(client, HttpMethod.Post, $"/api/v1/users/{User}/assignments",
            Json(new { nId, positionNId, isPrimary, effectiveFrom, effectiveTo }));
        if (response.StatusCode != HttpStatusCode.OK)
        {
            Assert.Fail($"创建任职 {nId} 失败: HTTP {(int)response.StatusCode} {response.StatusCode}, 响应: {await response.Content.ReadAsStringAsync()}");
        }

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return payload.RootElement.GetProperty("data").Clone();
    }

    private static async Task SeedCompanyAsync(FakeAdministrativeOrganizationStore orgs, string nId = "company-a", string name = "A 公司", int displayOrder = 1)
    {
        var company = AdministrativeOrganization.CreateRootCompany(Tenant, nId, name, displayOrder);
        await orgs.AddAsync(company, CancellationToken.None);
    }

    private static async Task SeedChildAsync(FakeAdministrativeOrganizationStore orgs, string nId, string name, string type, string parentNId)
    {
        var parent = (await orgs.GetAsync(Tenant, parentNId, CancellationToken.None))!;
        var child = AdministrativeOrganization.CreateChild(
            Tenant, nId, name, Enum.Parse<AdministrativeOrganizationType>(type),
            parent.TenantNId, parent.NId, parent.Id, parent.IsDeleted, parent.Status == OrganizationStatus.Active,
            parent.Type, displayOrder: 1);
        await orgs.AddAsync(child, CancellationToken.None);
    }

    private static async Task SeedPositionAsync(FakeAdministrativeOrganizationStore orgs, FakePositionStore positions, string nId, string organizationNId, string name)
    {
        var organization = (await orgs.GetAsync(Tenant, organizationNId, CancellationToken.None))!;
        var position = Position.Create(
            Tenant, nId, organization.TenantNId, organization.NId, organization.Id,
            organization.IsDeleted, organizationActive: organization.Status == OrganizationStatus.Active,
            name, description: null, displayOrder: 1);
        await positions.AddAsync(position, CancellationToken.None);
    }

    private static void SeedActiveAssignment(FakeUserAssignmentStore assignments, Position position, string nId = "assign-1", bool isPrimary = false)
    {
        var assignment = UserAssignment.Create(
            Tenant, nId, User, "张三",
            position.OrganizationNId, position.NId, position.Id, position.IsDeleted,
            organizationActive: true, positionActive: true, organizationMatchesPosition: true,
            isPrimary, Now().AddDays(-1), Now().AddDays(5), Now());
        assignments.AddAsync(assignment, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static async Task<AdministrativeOrganization> SeedCompanyViaServiceAsync(FakeAdministrativeOrganizationStore orgs)
    {
        var created = AdministrativeOrganization.CreateRootCompany(Tenant, "company-a", "A 公司", 1);
        created.ClearDomainEvents();
        await orgs.AddAsync(created, CancellationToken.None);
        return created;
    }

    private static async Task UpdateCompanyAsync(FakeAdministrativeOrganizationStore orgs, AdministrativeOrganization company)
    {
        var expectedOptimisticVersion = company.OptimisticVersion;
        var expectedConcurrencyVersion = company.ConcurrencyVersion;
        company.Rename("改名一次");
        company.ChangeDisplayOrder(1);
        company.ClearDomainEvents();
        await orgs.UpdateAsync(company, expectedOptimisticVersion, expectedConcurrencyVersion, CancellationToken.None);
    }

    private static async Task<Position> SeedPositionViaServiceAsync(
        FakeAdministrativeOrganizationStore orgs,
        FakePositionStore positions,
        string nId,
        string organizationNId,
        string name)
    {
        var organization = (await orgs.GetAsync(Tenant, organizationNId, CancellationToken.None))!;
        var position = Position.Create(
            Tenant, nId, organization.TenantNId, organization.NId, organization.Id,
            organization.IsDeleted, organizationActive: organization.Status == OrganizationStatus.Active,
            name, description: null, displayOrder: 1);
        position.ClearDomainEvents();
        await positions.AddAsync(position, CancellationToken.None);
        return position;
    }

    private static async Task UpdatePositionAsync(
        FakeAdministrativeOrganizationStore orgs,
        FakePositionStore positions,
        Position position)
    {
        var expectedOptimisticVersion = position.OptimisticVersion;
        var expectedConcurrencyVersion = position.ConcurrencyVersion;
        position.Rename("改名一次");
        position.ChangeDisplayOrder(1);
        position.ClearDomainEvents();
        await positions.UpdateAsync(position, expectedOptimisticVersion, expectedConcurrencyVersion, CancellationToken.None);
    }

    private static (FakeAdministrativeOrganizationStore Orgs, FakePositionStore Positions, FakeUserAssignmentStore Assignments) SeedAssignmentContext()
    {
        var orgs = new FakeAdministrativeOrganizationStore();
        var positions = new FakePositionStore();
        var assignments = new FakeUserAssignmentStore();
        SeedCompanyAsync(orgs).GetAwaiter().GetResult();
        SeedPositionViaServiceAsync(orgs, positions, "pos-1", "company-a", "后端工程师").GetAwaiter().GetResult();
        return (orgs, positions, assignments);
    }

    private static FakeIdentityUserDirectory Directory() => new FakeIdentityUserDirectory().WithEntry(User, "张三");

    private static DateTimeOffset Now() => DateTimeOffset.UtcNow;

    private static TestUser Admin(string tenantNId = Tenant) => new()
    {
        UserNId = Actor,
        UserName = "api-tester",
        TenantNId = tenantNId,
        PermissionNIds =
        [
            OrgView, OrgCreate, OrgUpdate, OrgMove, OrgStatus,
            PosView, PosCreate, PosUpdate, PosStatus,
            AssignmentView, AssignmentManage,
        ],
    };

    private static TestUser Viewer(params string[] permissionNIds) => new()
    {
        UserNId = "user-viewer",
        UserName = "viewer",
        TenantNId = Tenant,
        PermissionNIds = permissionNIds,
    };

    private static string Json(object value) => JsonSerializer.Serialize(value, WebJson);

    /// <summary>断言负载不含数据库审计字段(Id/EntityType/IsDeleted/CreatedOn/LastUpdatedOn)。</summary>
    private static void AssertNoDatabaseAuditFields(JsonElement data)
    {
        foreach (var property in new[] { "id", "entityType", "isDeleted", "createdOn", "lastUpdatedOn" })
        {
            Assert.False(data.TryGetProperty(property, out _), $"响应不应暴露数据库字段 {property}。");
        }
    }
}
