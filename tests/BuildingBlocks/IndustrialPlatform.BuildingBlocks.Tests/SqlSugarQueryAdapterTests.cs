using IndustrialPlatform.Infrastructure.Querying;
using IndustrialPlatform.Querying.Descriptors;
using IndustrialPlatform.Querying.Validation;
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

    [Theory]
    [InlineData(3, 1_073_741_824)]
    [InlineData(1_073_741_825, 2)]
    public void RejectsOffsetOverflowAsPlatformValidation(int pageIndex, int pageSize)
    {
        var map = new SqlSugarQueryFieldMap<TestRow>(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["NId"] = "NId" });

        var error = Assert.Throws<QueryValidationException>(() => SqlSugarQueryAdapter.BuildPlan(
            new QueryDescriptor([], [], ["NId"], pageIndex, pageSize),
            map));

        Assert.Equal("PLATFORM_QUERY_LIMIT_EXCEEDED", error.Error.Code);
    }

    [Fact]
    public void EscapesLikeWildcardsAndUsesAnExplicitEscapeCharacter()
    {
        var map = new SqlSugarQueryFieldMap<TestRow>(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["NId"] = "NId" });

        var plan = SqlSugarQueryAdapter.BuildPlan(
            new QueryDescriptor(
                [new QueryFilter("NId", QueryOperator.Contains, "literal%_value")],
                [],
                ["NId"],
                1,
                20),
            map);

        Assert.Equal("NId LIKE @p0 ESCAPE '\\'", Assert.Single(plan.FilterSql));
        Assert.Equal("%literal\\%\\_value%", plan.Parameters["p0"]);
    }

    [Fact]
    public void TranslatesNullEqualityToIsNullAndIsNotNull()
    {
        var map = new SqlSugarQueryFieldMap<TestRow>(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["email"] = "Email",
                ["NId"] = "NId",
            });

        var plan = SqlSugarQueryAdapter.BuildPlan(
            new QueryDescriptor(
                [
                    new QueryFilter("email", QueryOperator.Eq, null),
                    new QueryFilter("email", QueryOperator.Ne, null),
                ],
                [],
                ["email"],
                1,
                20),
            map);

        Assert.Equal(["Email IS NULL", "Email IS NOT NULL"], plan.FilterSql);
        Assert.Empty(plan.Parameters);
    }

    [Fact]
    public void RejectsOrderedComparisonAgainstNull()
    {
        var map = new SqlSugarQueryFieldMap<TestRow>(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["email"] = "Email",
                ["NId"] = "NId",
            });

        var error = Assert.Throws<QueryValidationException>(() => SqlSugarQueryAdapter.BuildPlan(
            new QueryDescriptor(
                [new QueryFilter("email", QueryOperator.Gt, null)],
                [],
                ["email"],
                1,
                20),
            map));

        Assert.Equal("PLATFORM_QUERY_INVALID", error.Error.Code);
    }

    private sealed class TestRow
    {
    }
}
