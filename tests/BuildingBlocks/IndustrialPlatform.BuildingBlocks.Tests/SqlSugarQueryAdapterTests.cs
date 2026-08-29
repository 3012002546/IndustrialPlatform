using IndustrialPlatform.Infrastructure.Querying;
using IndustrialPlatform.Querying.Descriptors;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class SqlSugarQueryAdapterTests
{
    [Fact]
    public void FieldMapOnlyResolvesRegisteredColumnsAndAddsStableTieBreaker()
    {
        var map = new SqlSugarQueryFieldMap<TestRow>(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "Name",
                ["NId"] = "NId",
            });
        var descriptor = new QueryDescriptor(
            [],
            [new QuerySort("name", QuerySortDirection.Desc)],
            ["name"],
            2,
            25);

        var plan = SqlSugarQueryAdapter.BuildPlan(descriptor, map);

        Assert.Equal(["Name DESC", "NId ASC"], plan.OrderBySql);
        Assert.Equal(25, plan.PageSize);
        Assert.Equal(25, plan.Skip);
        Assert.Throws<KeyNotFoundException>(() =>
            SqlSugarQueryAdapter.BuildPlan(
                descriptor with { OrderBy = [new QuerySort("password", QuerySortDirection.Asc)] },
                map));
    }

    private sealed class TestRow
    {
    }
}
