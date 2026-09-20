using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Collaboration.EmbeddedHost;
using IndustrialPlatform.Collaboration.EmbeddedHost.Abstractions;
using IndustrialPlatform.Collaboration.EmbeddedHost.Adapters;
using IndustrialPlatform.Collaboration.EmbeddedHost.Authorization;
using IndustrialPlatform.Collaboration.EmbeddedHost.Configuration;
using IndustrialPlatform.Collaboration.EmbeddedHost.Demo;
using IndustrialPlatform.Collaboration.EmbeddedHost.Models;
using IndustrialPlatform.Collaboration.EmbeddedHost.Persistence;
using IndustrialPlatform.Collaboration.EmbeddedHost.Services;
using IndustrialPlatform.Collaboration.EmbeddedHost.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Security_EmbeddedHostAdaptersTests
{
    [Fact]
    public void Standalone_chat_can_advance_its_read_cursor_without_platform_admin_permissions()
    {
        Assert.True(EmbeddedCollaborationPermissionCatalog.IsAllowed(CollaborationPermissions.MessagingReadCursorUpdate));
        Assert.False(EmbeddedCollaborationPermissionCatalog.IsAllowed(CollaborationPermissions.ComplianceRetentionManage));
    }

    [Fact]
    public async Task Standalone_account_is_accepted_only_when_mes_user_list_contains_it()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EmbeddedCollaboration:PlatformTenantNId"] = "standalone",
        }).Build();
        var directory = new MesUserDirectoryAdapter();
        var users = await directory.LoadMesUsersAsync(CancellationToken.None);
        Assert.NotEmpty(users);
        var selected = users[0];
        var resolver = new StandaloneAccountSourcePrincipalResolver(directory, configuration);

        var current = await resolver.ResolveAsync(new DefaultHttpContext(), selected.UserId, CancellationToken.None);

        Assert.Equal(selected.UserId, current?.ExternalSubject);
        Assert.Equal(selected.UserName, current?.DisplayName);
        Assert.Equal("standalone", current?.ExternalTenantNId);
        Assert.Null(await resolver.ResolveAsync(new DefaultHttpContext(), "missing-user", CancellationToken.None));
        Assert.Null(await resolver.ResolveAsync(new DefaultHttpContext(), null, CancellationToken.None));
    }

    [Fact]
    public void Fixed_demo_account_catalog_uses_source_sessions_without_a_second_account_map()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EmbeddedCollaboration:Demo:DefaultAccount"] = "operator-1",
            ["EmbeddedCollaboration:SourceSessions:operator-1:SessionNId"] = "session-1",
            ["EmbeddedCollaboration:SourceSessions:operator-1:ExternalSubject"] = "subject-1",
            ["EmbeddedCollaboration:SourceSessions:operator-2:SessionNId"] = "session-2",
            ["EmbeddedCollaboration:SourceSessions:operator-2:ExternalSubject"] = "subject-2",
            ["EmbeddedCollaboration:SourceSessions:operator-3:SessionNId"] = "session-3",
            ["EmbeddedCollaboration:SourceSessions:operator-3:ExternalSubject"] = "subject-3",
            ["EmbeddedCollaboration:SourceSessions:operator-3:Status"] = "Inactive",
        }).Build();

        var catalog = new FixedDemoAccountCatalog(configuration);

        Assert.Equal("operator-2", catalog.Resolve("operator-2")?.AccountNId);
        Assert.Equal("subject-1", catalog.Resolve(null)?.ExternalSubject);
        Assert.Null(catalog.Resolve("operator-3"));
        Assert.Null(catalog.Resolve("operator-1,operator-2"));
        Assert.Null(catalog.Resolve("operator-1:SessionNId"));
    }

    [Fact]
    public async Task Demo_source_session_must_match_the_selected_account()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EmbeddedCollaboration:SourceSessions:operator-1:SourceNId"] = "mes-example",
            ["EmbeddedCollaboration:SourceSessions:operator-1:ExternalTenantNId"] = "tenant-1",
            ["EmbeddedCollaboration:SourceSessions:operator-1:ExternalSubject"] = "subject-1",
            ["EmbeddedCollaboration:SourceSessions:operator-1:SessionNId"] = "session-1",
            ["EmbeddedCollaboration:SourceSessions:operator-1:SecurityVersion"] = "1",
        }).Build();
        var resolver = new ConfigurationEmbeddedSourcePrincipalResolver(
            configuration, new EmbeddedHostHandshakeOptions([]));
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "embedded_host_session=operator-1";

        Assert.Null(await resolver.ResolveAsync(context, "operator-2", CancellationToken.None));
        var current = await resolver.ResolveAsync(context, "operator-1", CancellationToken.None);
        Assert.Equal("session-1", current?.SessionNId);
        Assert.Equal("operator-1", current?.AccountNId);
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

    [Fact]
    public async Task Mes_user_loading_method_drives_search_get_status_and_paging()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EmbeddedCollaboration:Sources:mes:ExternalTenantMappings:mes-tenant"] = "platform-tenant",
            ["EmbeddedCollaboration:Sources:mes:SubjectMappings:subject-1:ExternalTenantNId"] = "mes-tenant",
            ["EmbeddedCollaboration:Sources:mes:SubjectMappings:subject-1:SecurityVersion"] = "1",
            ["EmbeddedCollaboration:Sources:mes:SubjectMappings:subject-1:DisplayName"] = "Operator One",
            ["EmbeddedCollaboration:Sources:mes:SubjectMappings:subject-2:ExternalTenantNId"] = "mes-tenant",
            ["EmbeddedCollaboration:Sources:mes:SubjectMappings:subject-2:SecurityVersion"] = "2",
            ["EmbeddedCollaboration:Sources:mes:SubjectMappings:subject-2:DisplayName"] = "Operator Two",
            ["EmbeddedCollaboration:Sources:mes:SubjectMappings:subject-2:Status"] = "Inactive",
        }).Build();
        var adapter = new ConfigurationEmbeddedCollaborationAccessAdapter(configuration);

        var first = await adapter.SearchDirectoryAsync("platform-tenant", "someone-else", "Operator", null, 1, CancellationToken.None);
        Assert.Single(first.Items);
        Assert.NotNull(first.NextCursor);
        var second = await adapter.SearchDirectoryAsync("platform-tenant", "someone-else", "Operator", first.NextCursor, 1, CancellationToken.None);
        Assert.Single(second.Items);
        Assert.Null(second.NextCursor);
        Assert.NotEqual(first.Items[0].UserNId, second.Items[0].UserNId);
        var inactiveNId = ConfigurationEmbeddedSubjectIdentityMapper.UserNId("mes", "mes-tenant", "subject-2");
        Assert.Equal("Inactive", (await adapter.GetDirectoryUserAsync("platform-tenant", inactiveNId, CancellationToken.None))?.Status);
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
