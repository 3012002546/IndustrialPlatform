using IndustrialPlatform.Identity.Application.Management;

namespace IndustrialPlatform.Identity.Application.Tests;

public sealed class UserQueryResourceTests
{
    [Theory]
    [InlineData("effectiveRoleCount")]
    [InlineData("optimisticVersion")]
    [InlineData("isDeleted")]
    public void Fields_without_a_sql_query_map_are_not_filterable_or_sortable(string field)
    {
        var definition = UserQueryResource.Definition.Fields[field];

        Assert.False(definition.Filterable);
        Assert.False(definition.Sortable);
    }

    [Fact]
    public void Executable_query_fields_are_explicitly_registered()
    {
        var executableFields = new[]
        {
            "userNId", "loginName", "name", "email", "phone", "status",
            "createdOn", "lastLoginOn", "mustChangePassword",
        };

        foreach (var field in executableFields)
        {
            var definition = UserQueryResource.Definition.Fields[field];
            Assert.True(definition.Filterable, field);
            Assert.True(definition.Sortable, field);
        }
    }
}
