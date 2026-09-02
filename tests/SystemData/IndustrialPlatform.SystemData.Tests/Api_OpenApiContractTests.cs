using System.Net;
using System.Text.Json;
using IndustrialPlatform.SystemData.Contracts.Administration;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IndustrialPlatform.SystemData.Api.Tests;

/// <summary>
/// OpenAPI 契约测试(TASK-SD-006):以真实 Program 生成 <c>/openapi/v1.json</c>,
/// 冻结组织/岗位/任职全部 17 个端点的路径与 HTTP 方法,以及管理契约 Schema 存在性。
/// 与 AdministrationEndpointTests 的线上信封形状联合构成 OpenAPI/JSON 契约报告来源。
/// </summary>
public sealed class OpenApiContractTests
{
    /// <summary>SD-006 全部 17 个管理端点(路径 → HTTP 方法集合)。</summary>
    private static readonly Dictionary<string, IReadOnlyList<string>> AdminEndpoints =
        new(StringComparer.Ordinal)
        {
            ["/api/v1/organizations/tree"] = ["get"],
            ["/api/v1/organizations/tree/export"] = ["get"],
            ["/api/v1/organizations/{organizationNId}"] = ["get", "put"],
            ["/api/v1/organizations"] = ["post"],
            ["/api/v1/organizations/{organizationNId}/move-preview"] = ["post"],
            ["/api/v1/organizations/{organizationNId}/move"] = ["post"],
            ["/api/v1/organizations/{organizationNId}/status"] = ["put"],
            ["/api/v1/positions"] = ["get", "post"],
            ["/api/v1/positions/export"] = ["get"],
            ["/api/v1/positions/{positionNId}"] = ["put"],
            ["/api/v1/positions/{positionNId}/status"] = ["put"],
            ["/api/v1/users/{userNId}/assignments"] = ["get", "post"],
            ["/api/v1/users/{userNId}/assignments/export"] = ["get"],
            ["/api/v1/assignments/{assignmentNId}"] = ["put"],
            ["/api/v1/assignments/{assignmentNId}/end"] = ["post"],
            ["/api/v1/assignments/{assignmentNId}/cancel"] = ["post"],
            ["/api/v1/users/{userNId}/primary-assignment"] = ["post"],
        };

    /// <summary>管理契约 Schema 名称(OpenAPI components/schemas,按类型名)。</summary>
    private static readonly string[] AdminSchemas =
    [
        nameof(CreateOrganizationRequest),
        nameof(UpdateOrganizationRequest),
        nameof(MoveOrganizationRequest),
        nameof(MoveOrganizationPreviewRequest),
        nameof(SetOrganizationStatusRequest),
        nameof(OrganizationNodeV1),
        nameof(OrganizationDetailV1),
        nameof(OrganizationMovePreviewV1),
        nameof(CreatePositionRequest),
        nameof(UpdatePositionRequest),
        nameof(SetPositionStatusRequest),
        nameof(PositionV1),
        nameof(CreateAssignmentRequest),
        nameof(UpdateScheduledAssignmentRequest),
        nameof(CancelAssignmentRequest),
        nameof(SetPrimaryAssignmentRequest),
        nameof(AssignmentV1),
    ];

    [Fact]
    public async Task OpenApi_IsGeneratedWithExpectedVersion()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("3.1.1", payload.RootElement.GetProperty("openapi").GetString());
        Assert.True(payload.RootElement.TryGetProperty("info", out var info));
        Assert.False(string.IsNullOrWhiteSpace(info.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task OpenApi_DocumentsAllSeventeenAdminEndpoints()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var paths = payload.RootElement.GetProperty("paths");

        Assert.Equal(AdminEndpoints.Count, paths.EnumerateObject().Count(path => IsAdminPath(path.Name)));

        foreach (var (pathTemplate, methods) in AdminEndpoints)
        {
            Assert.True(paths.TryGetProperty(pathTemplate, out var item), $"OpenAPI 缺少路径 {pathTemplate}。");
            foreach (var method in methods)
            {
                Assert.True(
                    item.TryGetProperty(method, out var operation) && operation.ValueKind == JsonValueKind.Object,
                    $"路径 {pathTemplate} 缺少 {method.ToUpperInvariant()} 操作。");
            }
        }
    }

    [Fact]
    public async Task OpenApi_DocumentsAdminContractSchemas()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var schemas = payload.RootElement.GetProperty("components").GetProperty("schemas");

        foreach (var schemaName in AdminSchemas)
        {
            Assert.True(schemas.TryGetProperty(schemaName, out var schema), $"OpenAPI 缺少 Schema {schemaName}。");
            Assert.True(schema.TryGetProperty("type", out _) || schema.TryGetProperty("properties", out _),
                $"{schemaName} 应为对象 Schema。");
        }
    }

    [Fact]
    public async Task OpenApi_ResponseModelsExposeOnlyAdminSchemasNoAuditGuids()
    {
        // 响应模型只引用管理契约 Schema,不暴露数据库审计字段(Id/Guid 主键)。
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var schemas = payload.RootElement.GetProperty("components").GetProperty("schemas");

        foreach (var schemaName in AdminSchemas)
        {
            if (!schemas.TryGetProperty(schemaName, out var schema)
                || !schema.TryGetProperty("properties", out var properties))
            {
                continue;
            }

            foreach (var property in properties.EnumerateObject())
            {
                Assert.False(
                    property.Name is "id" or "persistenceId" or "createdOn" or "updatedOn",
                    $"{schemaName}.{property.Name} 疑似数据库审计字段泄漏到 OpenAPI。");
            }
        }
    }

    private static bool IsAdminPath(string path) =>
        path.StartsWith("/api/v1/organizations", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/positions", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/users/", StringComparison.Ordinal)
        || path.StartsWith("/api/v1/assignments", StringComparison.Ordinal);
}
