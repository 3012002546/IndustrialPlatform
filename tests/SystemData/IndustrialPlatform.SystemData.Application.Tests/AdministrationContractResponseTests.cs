using System.Reflection;
using IndustrialPlatform.SystemData.Contracts.Administration;

namespace IndustrialPlatform.SystemData.Application.Tests;

/// <summary>
/// 管理响应契约形状测试(TASK-SD-006):响应 DTO 不暴露数据库 Guid 主键,
/// 只暴露稳定 NId 与双并发版本;枚举以枚举名字符串传输(断言字符串属性存在)。
/// </summary>
public sealed class AdministrationContractResponseTests
{
    [Theory]
    [InlineData(typeof(OrganizationDetailV1))]
    [InlineData(typeof(OrganizationNodeV1))]
    [InlineData(typeof(OrganizationMovePreviewV1))]
    [InlineData(typeof(PositionV1))]
    [InlineData(typeof(AssignmentV1))]
    public void ResponseType_DoesNotExposeDatabaseId(Type type)
    {
        var names = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Id", names);
        Assert.DoesNotContain("EntityType", names);
        Assert.DoesNotContain("IsDeleted", names);
        Assert.DoesNotContain("CreatedOn", names);
        Assert.DoesNotContain("LastUpdatedOn", names);
    }

    [Theory]
    [InlineData(typeof(OrganizationDetailV1), "NId")]
    [InlineData(typeof(OrganizationDetailV1), "OptimisticVersion")]
    [InlineData(typeof(OrganizationDetailV1), "ConcurrencyVersion")]
    [InlineData(typeof(OrganizationMovePreviewV1), "OrganizationRevision")]
    [InlineData(typeof(OrganizationMovePreviewV1), "ExpectedConcurrencyVersion")]
    [InlineData(typeof(PositionV1), "ConcurrencyVersion")]
    [InlineData(typeof(AssignmentV1), "ConcurrencyVersion")]
    public void ResponseType_ExposesNIdAndConcurrencyVersions(Type type, string propertyName)
    {
        var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
    }

    [Theory]
    [InlineData(typeof(OrganizationDetailV1), "Type")]
    [InlineData(typeof(OrganizationDetailV1), "Status")]
    [InlineData(typeof(OrganizationNodeV1), "Type")]
    [InlineData(typeof(PositionV1), "Status")]
    [InlineData(typeof(AssignmentV1), "State")]
    public void ResponseType_TransmitsEnumsAsStrings(Type type, string propertyName)
    {
        var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        Assert.Equal(typeof(string), property!.PropertyType);
    }
}
