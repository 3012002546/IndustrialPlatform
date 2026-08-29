using IndustrialPlatform.Querying.Descriptors;
using IndustrialPlatform.Querying.Schema;
using IndustrialPlatform.Querying.Validation;
using IndustrialPlatform.Web.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class ODataQueryDescriptorParserTests
{
    private static readonly QueryResourceDefinition Users = new(
        "users",
        new Dictionary<string, QueryFieldDefinition>(StringComparer.Ordinal)
        {
            ["NId"] = QueryFieldDefinition.Text(sortable: true),
            ["userName"] = QueryFieldDefinition.Text(),
            ["displayName"] = QueryFieldDefinition.Text(),
            ["status"] = QueryFieldDefinition.Text(),
        },
        tieBreaker: "NId");

    [Fact]
    public void ParsesOnlySupportedOptionsAndAlignsInternalPageIndex()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["$filter"] = "contains(userName,'alice') and status eq 'Active'",
            ["$select"] = "userName,displayName",
            ["$orderby"] = "displayName desc",
            ["$top"] = "20",
            ["$skip"] = "40",
            ["$count"] = "true",
        });

        var descriptor = new ODataQueryDescriptorParser().Parse(query, Users);

        Assert.Equal(3, descriptor.PageIndex);
        Assert.Equal(20, descriptor.PageSize);
        Assert.Equal(2, descriptor.Filters.Count);
        Assert.Equal("displayName", descriptor.OrderBy[0].Field);
        Assert.Equal("NId", descriptor.OrderBy[^1].Field);
        Assert.True(descriptor.IncludeCount);
    }

    [Theory]
    [InlineData("$expand", "roles")]
    [InlineData("$search", "alice")]
    [InlineData("$apply", "groupby((status))")]
    public void RejectsDisabledOptions(string option, string value)
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            [option] = value,
            ["$top"] = "20",
        });

        var error = Assert.Throws<QueryValidationException>(() =>
            new ODataQueryDescriptorParser().Parse(query, Users));

        Assert.Equal("PLATFORM_QUERY_OPTION_NOT_ALLOWED", error.Error.Code);
    }

    [Fact]
    public void RejectsUnalignedPagingAndUnknownFields()
    {
        var unaligned = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["$top"] = "20",
            ["$skip"] = "5",
        });
        var alignmentError = Assert.Throws<QueryValidationException>(() =>
            new ODataQueryDescriptorParser().Parse(unaligned, Users));
        Assert.Equal("PLATFORM_QUERY_PAGING_ALIGNMENT_REQUIRED", alignmentError.Error.Code);

        var unknown = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["$top"] = "20",
            ["$select"] = "password",
        });
        var fieldError = Assert.Throws<QueryValidationException>(() =>
            new ODataQueryDescriptorParser().Parse(unknown, Users));
        Assert.Equal("PLATFORM_QUERY_FIELD_NOT_ALLOWED", fieldError.Error.Code);
    }

    [Fact]
    public void UsesOfficialODataSyntaxForParenthesesAndTypedLiterals()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["$filter"] = "(status eq 'Active' and userName ne 'bob') and NId in ('u-1','u-2')",
            ["$select"] = "userName,displayName",
            ["$top"] = "20",
        });

        var descriptor = new ODataQueryDescriptorParser().Parse(query, Users);

        Assert.Equal(3, descriptor.Filters.Count);
        Assert.Equal(QueryOperator.Ne, descriptor.Filters[1].Operator);
        Assert.Equal(QueryOperator.In, descriptor.Filters[2].Operator);
        Assert.Equal(new object[] { "u-1", "u-2" }, (object[])descriptor.Filters[2].Value!);
    }

    [Theory]
    [InlineData("userName eq 'alice' or status eq 'Active'")]
    [InlineData("length(userName) gt 2")]
    [InlineData("userName add 'x' eq 'alice'")]
    [InlineData("userName/Name eq 'alice'")]
    [InlineData("cast(userName,Edm.String) eq 'alice'")]
    [InlineData("userName any()")]
    [InlineData("userName all()")]
    public void RejectsUnsupportedOfficialODataExpressions(string filter)
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["$filter"] = filter,
            ["$top"] = "20",
        });

        var error = Assert.Throws<QueryValidationException>(() =>
            new ODataQueryDescriptorParser().Parse(query, Users));

        Assert.Equal("PLATFORM_QUERY_INVALID", error.Error.Code);
    }

    [Fact]
    public void EnforcesFilterSortAndTopLimitsWithStableErrors()
    {
        var tooManyFilters = string.Join(" and ", Enumerable.Range(1, 21).Select(i => $"status eq 'S{i}'"));
        var filterError = Assert.Throws<QueryValidationException>(() => new ODataQueryDescriptorParser().Parse(
            new QueryCollection(new Dictionary<string, StringValues>
            {
                ["$filter"] = tooManyFilters,
                ["$top"] = "20",
            }), Users));
        Assert.Equal("PLATFORM_QUERY_LIMIT_EXCEEDED", filterError.Error.Code);

        var functionError = Assert.Throws<QueryValidationException>(() => new ODataQueryDescriptorParser(
            new ODataQueryValidationSettings(MaxFunctionDepth: 0)).Parse(
            new QueryCollection(new Dictionary<string, StringValues>
            {
                ["$filter"] = "contains(userName,'a')",
                ["$top"] = "20",
            }), Users));
        Assert.Equal("PLATFORM_QUERY_LIMIT_EXCEEDED", functionError.Error.Code);

        var sortError = Assert.Throws<QueryValidationException>(() => new ODataQueryDescriptorParser().Parse(
            new QueryCollection(new Dictionary<string, StringValues>
            {
                ["$orderby"] = "NId,userName,displayName,status",
                ["$top"] = "20",
            }), Users));
        Assert.Equal("PLATFORM_QUERY_LIMIT_EXCEEDED", sortError.Error.Code);

        var topError = Assert.Throws<QueryValidationException>(() => new ODataQueryDescriptorParser().Parse(
            new QueryCollection(new Dictionary<string, StringValues>
            {
                ["$top"] = "101",
            }), Users));
        Assert.Equal("PLATFORM_QUERY_LIMIT_EXCEEDED", topError.Error.Code);
    }

}
