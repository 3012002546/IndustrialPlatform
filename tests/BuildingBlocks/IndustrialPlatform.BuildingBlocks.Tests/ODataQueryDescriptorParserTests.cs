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
}
