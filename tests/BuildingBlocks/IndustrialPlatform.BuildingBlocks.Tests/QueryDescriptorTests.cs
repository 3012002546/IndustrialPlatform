using IndustrialPlatform.Querying.Descriptors;
using IndustrialPlatform.Querying.Schema;
using IndustrialPlatform.Querying.Validation;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class QueryDescriptorTests
{
    [Fact]
    public void DescriptorUsesImmutablePlatformPagingAndStableValidationLimits()
    {
        var descriptor = new QueryDescriptor(
            [new QueryFilter("name", QueryOperator.Contains, "alice")],
            [new QuerySort("name", QuerySortDirection.Asc)],
            ["name"],
            PageIndex: 1,
            PageSize: 25);

        Assert.Equal(1, descriptor.PageIndex);
        Assert.Equal(25, descriptor.PageSize);
        Assert.Equal(20, QueryLimits.MaxFilterNodes);
        Assert.Equal(100, QueryLimits.MaxTop);
        Assert.Equal(3, QueryLimits.MaxSortFields);
        Assert.Equal("name", descriptor.Filters[0].Field);
    }

    [Fact]
    public void ResourceSchemaRejectsUnknownFieldsWithStableCode()
    {
        var schema = new QueryResourceDefinition(
            "users",
            new Dictionary<string, QueryFieldDefinition>(StringComparer.Ordinal)
            {
                ["name"] = QueryFieldDefinition.Text(),
            });

        var error = Assert.Throws<QueryValidationException>(() =>
            QueryDescriptorValidator.Validate(
                new QueryDescriptor([], [], ["password"], 1, 20),
                schema));

        Assert.Equal("PLATFORM_QUERY_FIELD_NOT_ALLOWED", error.Error.Code);
    }
}
