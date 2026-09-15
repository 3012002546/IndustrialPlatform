using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Collaboration.EmbeddedHost;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Security_EmbeddedHostAdaptersTests
{
    [Fact]
    public void Fixed_demo_account_catalog_maps_xxA_xxB_xxC_and_rejects_unknown_values()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EmbeddedCollaboration:Demo:DefaultAccount"] = "xxA",
            ["EmbeddedCollaboration:Demo:Accounts:xxA:SourceSessionValue"] = "cookie-a",
            ["EmbeddedCollaboration:Demo:Accounts:xxA:ExternalSubject"] = "demo-a",
            ["EmbeddedCollaboration:Demo:Accounts:xxB:SourceSessionValue"] = "cookie-b",
            ["EmbeddedCollaboration:Demo:Accounts:xxB:ExternalSubject"] = "demo-b",
            ["EmbeddedCollaboration:Demo:Accounts:xxC:SourceSessionValue"] = "cookie-c",
            ["EmbeddedCollaboration:Demo:Accounts:xxC:ExternalSubject"] = "demo-c",
        }).Build();

        var catalog = new FixedDemoAccountCatalog(configuration);

        Assert.Equal("cookie-b", catalog.Resolve("xxB")?.SourceSessionValue);
        Assert.Equal("demo-c", catalog.Resolve("xxC")?.ExternalSubject);
        Assert.Equal("demo-a", catalog.Resolve(null)?.ExternalSubject);
        Assert.Null(catalog.Resolve("xxD"));
        Assert.Null(catalog.Resolve("xxA,xxB"));
    }

    [Fact]
    public async Task Unconfigured_current_user_adapter_fails_closed()
    {
        var adapter = new NotConfiguredEmbeddedCurrentUserAdapter();

        var exception = await Assert.ThrowsAsync<EmbeddedHandshakeException>(() =>
            adapter.ResolveAsync(new DefaultHttpContext(), CancellationToken.None));

        Assert.Equal(503, exception.StatusCode);
        Assert.Equal("EMBEDDED_SOURCE_ADAPTER_NOT_CONFIGURED", exception.Code);
    }

    [Fact]
    public async Task Formal_mapper_derives_a_stable_user_key_without_a_second_mes_mapping_seam()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EmbeddedCollaboration:PlatformTenantNId"] = "platform-tenant",
        }).Build();
        var mapper = new ServerDerivedEmbeddedSubjectIdentityMapper(configuration);
        var assertion = new EmbeddedIdentityAssertion("mes", "issuer", "subject-1", "tenant-1", "MES user", "7", "session-1", "nonce", "jti", DateTimeOffset.UtcNow.AddSeconds(30), "account-1");

        var first = await mapper.MapAsync(assertion, CancellationToken.None);
        var second = await mapper.MapAsync(assertion, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Equal("platform-tenant", first!.TenantNId);
        Assert.StartsWith("ext_", first.UserNId, StringComparison.Ordinal);
        Assert.Equal("account-1", first.AccountNId);
    }

    [Fact]
    public async Task Reference_directory_keeps_tenant_filter_and_excludes_current_user()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EmbeddedCollaboration:Sources:mes:ExternalTenantMappings:mes-tenant"] = "platform-tenant",
            ["EmbeddedCollaboration:Sources:mes:SubjectMappings:mes-user:ExternalTenantNId"] = "mes-tenant",
            ["EmbeddedCollaboration:Sources:mes:SubjectMappings:mes-user:SecurityVersion"] = "7",
            ["EmbeddedCollaboration:Sources:mes:SubjectMappings:mes-user:DisplayName"] = "MES user",
        }).Build();
        var adapter = new ConfigurationEmbeddedCollaborationAccessAdapter(configuration);
        var userNId = ConfigurationEmbeddedSubjectIdentityMapper.UserNId("mes", "mes-tenant", "mes-user");

        Assert.NotNull(await adapter.GetDirectoryUserAsync("platform-tenant", userNId, CancellationToken.None));
        Assert.Null(await adapter.GetDirectoryUserAsync("other-tenant", userNId, CancellationToken.None));
        var found = await adapter.SearchDirectoryAsync("platform-tenant", "other-user", "MES", null, 10, CancellationToken.None);
        var own = await adapter.SearchDirectoryAsync("platform-tenant", userNId, "MES", null, 10, CancellationToken.None);
        var otherTenant = await adapter.SearchDirectoryAsync("other-tenant", "other-user", "MES", null, 10, CancellationToken.None);
        Assert.Single(found.Items);
        Assert.Empty(own.Items);
        Assert.Empty(otherTenant.Items);
    }

    [Theory]
    [InlineData("collaboration.messaging.read", "S-1", 1, true)]
    [InlineData("remote-assistance.voice.call", "S-1", 1, true)]
    [InlineData("identity.users.write", "S-1", 1, false)]
    [InlineData("collaboration.messaging.read", "S-1", 0, false)]
    [InlineData("collaboration.messaging.read", null, 1, false)]
    public async Task Embedded_permission_evaluator_preserves_host_permission_boundary(
        string permission, string? sessionNId, int authVersion, bool expected)
    {
        var evaluator = new EmbeddedPermissionEvaluator();

        var result = await evaluator.EvaluateAsync("T-1", "U-1", sessionNId, authVersion, permission, CancellationToken.None);

        Assert.Equal(expected, result.Allowed);
        Assert.Equal(expected ? AuthorizationDenialReason.None : AuthorizationDenialReason.MissingPermission, result.Reason);
    }
}
