using System.Security.Claims;
using IndustrialPlatform.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class CurrentUserTests
{
    [Fact]
    public void AuthenticatedUser_ExposesClaims()
    {
        var userId = Guid.NewGuid();
        var user = CreateCurrentUser(
            new Claim(ClaimConstants.UserId, userId.ToString()),
            new Claim(ClaimConstants.UserName, "admin"),
            new Claim(ClaimConstants.TenantId, "tenant-1"),
            new Claim(ClaimConstants.Role, "admin"),
            new Claim(ClaimConstants.Role, "operator"));

        Assert.True(user.IsAuthenticated);
        Assert.Equal(userId, user.UserId);
        Assert.Equal("admin", user.UserName);
        Assert.Equal("tenant-1", user.TenantId);
        Assert.Equal(2, user.Roles.Count);
        Assert.Contains("admin", user.Roles);
        Assert.Contains("operator", user.Roles);
    }

    [Fact]
    public void AnonymousUser_ReturnsNullsAndEmptyRoles()
    {
        var user = CreateCurrentUser(authenticationType: null, []);

        Assert.False(user.IsAuthenticated);
        Assert.Null(user.UserId);
        Assert.Null(user.UserName);
        Assert.Null(user.TenantId);
        Assert.Empty(user.Roles);
    }

    [Fact]
    public void InvalidUserIdClaim_ReturnsNull()
    {
        var user = CreateCurrentUser(new Claim(ClaimConstants.UserId, "not-a-guid"));

        Assert.Null(user.UserId);
    }

    [Fact]
    public void NoHttpContext_ReturnsNulls()
    {
        var user = new CurrentUser(new HttpContextAccessor());

        Assert.False(user.IsAuthenticated);
        Assert.Null(user.UserId);
        Assert.Empty(user.Roles);
    }

    private static CurrentUser CreateCurrentUser(params Claim[] claims)
        => CreateCurrentUser("Test", claims);

    private static CurrentUser CreateCurrentUser(string? authenticationType, Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, authenticationType);
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        return new CurrentUser(new HttpContextAccessor { HttpContext = context });
    }
}

public sealed class SecurityRegistrationTests
{
    [Fact]
    public void AddCurrentUser_RegistersCurrentUserAndHttpContextAccessor()
    {
        var services = new ServiceCollection();
        services.AddCurrentUser();

        Assert.Contains(services, service => service.ServiceType == typeof(ICurrentUser));
        Assert.Contains(services, service => service.ServiceType == typeof(IHttpContextAccessor));
    }
}
