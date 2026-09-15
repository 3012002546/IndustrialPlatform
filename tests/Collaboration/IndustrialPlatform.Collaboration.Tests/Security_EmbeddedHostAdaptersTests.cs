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
    public async Task Reference_access_adapter_requires_explicit_permissions()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EmbeddedCollaboration:Sources:mes:ExternalTenantMappings:mes-tenant"] = "platform-tenant",
            ["EmbeddedCollaboration:Sources:mes:SubjectMappings:mes-user:ExternalTenantNId"] = "mes-tenant",
            ["EmbeddedCollaboration:Sources:mes:SubjectMappings:mes-user:SecurityVersion"] = "7",
            ["EmbeddedCollaboration:Sources:mes:SubjectMappings:mes-user:Permissions:0"] = "collaboration.messaging.read",
            ["EmbeddedCollaboration:SourceSessions:cookie-key:SourceNId"] = "mes",
            ["EmbeddedCollaboration:SourceSessions:cookie-key:ExternalTenantNId"] = "mes-tenant",
            ["EmbeddedCollaboration:SourceSessions:cookie-key:ExternalSubject"] = "mes-user",
            ["EmbeddedCollaboration:SourceSessions:cookie-key:SessionNId"] = "upstream-session",
            ["EmbeddedCollaboration:SourceSessions:cookie-key:SecurityVersion"] = "7",
            ["EmbeddedCollaboration:SourceSessions:cookie-key:Status"] = "Active",
        }).Build();
        var adapter = new ConfigurationEmbeddedCollaborationAccessAdapter(configuration);
        var identity = new EmbeddedIdentity("platform-tenant", ConfigurationEmbeddedSubjectIdentityMapper.UserNId("mes", "mes-tenant", "mes-user"), "upstream-session", "7")
        {
            SourceNId = "mes",
            ExternalTenantNId = "mes-tenant",
            ExternalSubject = "mes-user",
            DisplayName = "MES user",
        };

        var allowed = await adapter.HasPermissionAsync("collaboration.messaging.read", identity.TenantNId, identity.UserNId, identity.SessionNId, identity.SecurityVersion, CancellationToken.None);
        var denied = await adapter.HasPermissionAsync("remote-assistance.voice.call", identity.TenantNId, identity.UserNId, identity.SessionNId, identity.SecurityVersion, CancellationToken.None);

        Assert.True(allowed);
        Assert.False(denied);
    }

    [Fact]
    public async Task Embedded_permission_evaluator_rejects_invalid_security_version()
    {
        var adapter = new NotConfiguredEmbeddedCollaborationAccessAdapter();
        var evaluator = new EmbeddedPermissionEvaluator(adapter);

        var result = await evaluator.EvaluateAsync("T-1", "U-1", "S-1", 0, "collaboration.messaging.read", CancellationToken.None);

        Assert.False(result.Allowed);
        Assert.Equal(AuthorizationDenialReason.MissingPermission, result.Reason);
    }

    [Fact]
    public async Task Reference_access_adapter_requires_the_configured_source_session_to_match()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EmbeddedCollaboration:Sources:mes:ExternalTenantMappings:mes-tenant"] = "platform-tenant",
            ["EmbeddedCollaboration:Sources:mes:SubjectMappings:mes-user:ExternalTenantNId"] = "mes-tenant",
            ["EmbeddedCollaboration:Sources:mes:SubjectMappings:mes-user:SecurityVersion"] = "7",
            ["EmbeddedCollaboration:Sources:mes:SubjectMappings:mes-user:Permissions:0"] = "collaboration.messaging.read",
            ["EmbeddedCollaboration:SourceSessions:cookie-key:SourceNId"] = "mes",
            ["EmbeddedCollaboration:SourceSessions:cookie-key:ExternalTenantNId"] = "mes-tenant",
            ["EmbeddedCollaboration:SourceSessions:cookie-key:ExternalSubject"] = "mes-user",
            ["EmbeddedCollaboration:SourceSessions:cookie-key:SessionNId"] = "upstream-session",
            ["EmbeddedCollaboration:SourceSessions:cookie-key:SecurityVersion"] = "7",
            ["EmbeddedCollaboration:SourceSessions:cookie-key:Status"] = "Active",
        }).Build();
        var adapter = new ConfigurationEmbeddedCollaborationAccessAdapter(configuration);
        var userNId = ConfigurationEmbeddedSubjectIdentityMapper.UserNId("mes", "mes-tenant", "mes-user");

        Assert.True(await adapter.HasPermissionAsync("collaboration.messaging.read", "platform-tenant", userNId, "upstream-session", "7", CancellationToken.None));
        Assert.False(await adapter.HasPermissionAsync("collaboration.messaging.read", "platform-tenant", userNId, "different-session", "7", CancellationToken.None));
    }
}
