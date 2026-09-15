using System.Security.Claims;
using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Api.Hubs;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Collaboration.Application.RemoteAssistance;
using IndustrialPlatform.Collaboration.Domain.RemoteAssistance;
using IndustrialPlatform.Collaboration.EmbeddedHost;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Security_EmbeddedHostHandshakeTests
{
    [Theory]
    [InlineData("?account=xxA&account=xxB", "EMBEDDED_ACCOUNT_AMBIGUOUS")]
    [InlineData("?account=", "EMBEDDED_ACCOUNT_INVALID")]
    public void Account_selector_rejects_ambiguous_or_empty_values(string query, string code)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(query);

        var exception = Assert.Throws<EmbeddedHandshakeException>(() => EmbeddedAccountQuery.Read(context));

        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public void Account_selector_cannot_switch_a_verified_mes_session()
    {
        var identity = new EmbeddedIdentity("T-1", "U-1", "S-1", "7") { AccountNId = "mes-account-a" };

        var exception = Assert.Throws<EmbeddedHandshakeException>(() => EmbeddedAccountQuery.EnsureMatches(identity, "mes-account-b"));

        Assert.Equal("EMBEDDED_ACCOUNT_MISMATCH", exception.Code);
    }

    [Fact]
    public async Task Signed_assertion_is_exchanged_once_across_two_store_instances_and_authenticates_session()
    {
        using var fixture = CreateFixture();
        using var db1 = CreateDb(fixture.DatabasePath);
        using var db2 = CreateDb(fixture.DatabasePath);
        using var store1 = new SqlEmbeddedHandshakeStore(db1);
        using var store2 = new SqlEmbeddedHandshakeStore(db2);
        var service1 = CreateService(fixture, store1);
        var service2 = CreateService(fixture, store2);

        var challengeContext = Context(fixture.ParentOrigin);
        var challenge = await service1.CreateChallengeAsync(fixture.ParentOrigin, challengeContext, CancellationToken.None);
        var storedChallenge = await store1.GetChallengeAsync(challenge.Nonce, CancellationToken.None);
        Assert.NotNull(storedChallenge);
        Assert.Equal(challenge.ParentOrigin, storedChallenge.ParentOrigin);
        Assert.Equal(challenge.ExpiresOn, storedChallenge.ExpiresOn);
        var bindingCookie = CookiePair(challengeContext);
        var assertionContext = Context(fixture.ParentOrigin, $"{bindingCookie}; {fixture.SourceCookie}");
        var assertion = await service1.IssueAssertionAsync(challenge.Nonce, assertionContext, CancellationToken.None);
        Assert.NotNull(await store2.GetChallengeAsync(challenge.Nonce, CancellationToken.None));
        var completeContext = Context(fixture.ParentOrigin, bindingCookie);

        var session = await service2.CompleteAsync(assertion, completeContext, CancellationToken.None);
        Assert.StartsWith("ext_", session.Identity.UserNId, StringComparison.Ordinal);
        Assert.Equal(1, session.Epoch);
        Assert.Equal("T-1", session.Identity.TenantNId);
        var storedSession = await store1.GetSessionAsync(EmbeddedSessionToken.Hash(session.SessionToken), BindingHash(bindingCookie), CancellationToken.None);
        Assert.NotNull(storedSession);

        var authenticatedContext = Context(fixture.ParentOrigin, $"{bindingCookie}; industrial_embedded_session={session.SessionToken}");
        var nextCalled = false;
        await new EmbeddedSessionAuthenticationMiddleware(_ =>
        {
            nextCalled = true;
            Assert.True(authenticatedContext.User.Identity?.IsAuthenticated);
            Assert.Equal(session.Identity.UserNId, authenticatedContext.User.FindFirst("sub")?.Value);
            Assert.Equal("T-1", authenticatedContext.User.FindFirst("tenant_id")?.Value);
            Assert.Equal(session.ExpiresOn.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), authenticatedContext.User.FindFirst("exp")?.Value);
            return Task.CompletedTask;
        }).InvokeAsync(authenticatedContext, store1, fixture.Options, new ConfigurationEmbeddedSubjectIdentityMapper(fixture.Configuration));
        Assert.True(nextCalled);

        await Assert.ThrowsAsync<EmbeddedHandshakeException>(() => service1.CompleteAsync(assertion, Context(fixture.ParentOrigin, bindingCookie), CancellationToken.None));
        Assert.Null(await store1.GetSessionAsync(EmbeddedSessionToken.Hash("not-a-token"), BindingHash(bindingCookie), CancellationToken.None));
    }

    [Fact]
    public async Task Hub_validation_uses_heartbeat_expiry_refreshes_bound_media_and_rejects_revoke()
    {
        using var fixture = CreateFixture(sessionLifetime: TimeSpan.FromSeconds(3));
        using var db = CreateDb(fixture.DatabasePath);
        using var store = new SqlEmbeddedHandshakeStore(db);
        var service = CreateService(fixture, store);
        var initial = await EstablishAsync(service, fixture);
        var http = Context(fixture.ParentOrigin, $"{initial.BindingCookie}; industrial_embedded_session={initial.Session.SessionToken}");
        http.Features.Set<IHttpContextFeature>(new TestHttpContextFeature { HttpContext = http });
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, initial.Session.Identity.UserNId),
            new Claim(ClaimConstants.UserNId, initial.Session.Identity.UserNId),
            new Claim(ClaimConstants.TenantId, initial.Session.Identity.TenantNId),
            new Claim(ClaimConstants.SessionId, initial.Session.Identity.SessionNId),
            new Claim("embedded_session", "true"),
            new Claim("source_n_id", initial.Session.Identity.SourceNId),
            new Claim("external_tenant_n_id", initial.Session.Identity.ExternalTenantNId),
            new Claim("external_subject", initial.Session.Identity.ExternalSubject),
            new Claim("source_session_n_id", initial.Session.Identity.SessionNId),
            new Claim("ver", initial.Session.Identity.SecurityVersion),
        ], "embedded"));
        http.User = principal;
        var caller = new TestHubCallerContext("connection-a", http, principal);
        var actorUserNId = initial.Session.Identity.UserNId;
        var validator = new EmbeddedHubSessionValidator(
            store,
            fixture.Options,
            new ConfigurationEmbeddedSourcePrincipalResolver(fixture.Configuration, fixture.Options));
        var registry = new MediaContextRegistry();

        var beforeHeartbeat = await validator.ValidateAsync(caller, CancellationToken.None);
        Assert.True(beforeHeartbeat.IsCurrent);
        Assert.Equal(initial.Session.ExpiresOn, beforeHeartbeat.TokenExpiresOn);

        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await service.KeepAliveAsync(http, CancellationToken.None);
        var refreshed = await store.GetSessionAsync(
            EmbeddedSessionToken.Hash(initial.Session.SessionToken),
            BindingHash(initial.BindingCookie),
            CancellationToken.None);
        Assert.NotNull(refreshed);
        Assert.True(refreshed!.ExpiresOn > initial.Session.ExpiresOn);
        registry.Bind(
            initial.Session.Identity.TenantNId,
            "conversation-1",
            "peer-user",
            "connection-peer",
            "peer-session",
            7,
            refreshed.ExpiresOn,
            "screen-1",
            null,
            actorUserNId);

        var waitUntilOriginalExpiry = initial.Session.ExpiresOn - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(100);
        if (waitUntilOriginalExpiry > TimeSpan.Zero)
            await Task.Delay(waitUntilOriginalExpiry);
        Assert.True(DateTimeOffset.UtcNow > initial.Session.ExpiresOn);
        Assert.True(DateTimeOffset.UtcNow < refreshed.ExpiresOn);

        var mediaRepository = TestMediaRepository.Create(CreateAcceptedScreen(actorUserNId), out _);
        var collaborationRepository = TestCollaborationRepository.Create(actorUserNId);
        var directory = new TestDirectory();
        var collaboration = new CollaborationService(
            collaborationRepository,
            directory,
            null!,
            null!,
            new TestPresence(),
            null!,
            null!,
            null!,
            new PageCursorCodec("embedded-media-test", TimeSpan.FromMinutes(5)));
        var media = new RemoteAssistanceService(
            collaboration,
            mediaRepository,
            directory,
            new AllowAllMediaPermissions(),
            registry,
            new MediaContextCoordinator(),
            new EmptyIceServers(),
            Options.Create(new RemoteAssistanceOptions { ScreenEnabled = true, VoiceEnabled = true }));
        using var mediaServices = new ServiceCollection()
            .AddSingleton(media)
            .AddSingleton(registry)
            .BuildServiceProvider();
        var hub = new CollaborationHub(
            collaboration,
            new TestCurrentUser(initial.Session.Identity.TenantNId, initial.Session.Identity.UserNId),
            null!,
            mediaServices)
        {
            Context = caller,
            Clients = TestHubCallerClients.Create(),
        };
        var filter = new CollaborationHubSessionFilter(validator, registry);
        var bindRequest = new BindMediaRequest
        {
            ConversationNId = "conversation-1",
            ScreenSessionNId = "screen-1",
        };
        var bindInvocation = new HubInvocationContext(
            caller,
            mediaServices,
            hub,
            typeof(CollaborationHub).GetMethod(nameof(CollaborationHub.BindMedia))!,
            [bindRequest]);
        var bindResult = Assert.IsType<MediaHubResult<MediaBindingDto>>(await filter.InvokeMethodAsync(
            bindInvocation,
            async _ => (object?)await hub.BindMedia(bindRequest)));
        Assert.True(bindResult.Ok, $"bind failed: {bindResult.Error?.Code} {bindResult.Error?.MessageKey}");
        Assert.NotNull(bindResult.Data);
        Assert.Equal(refreshed.ExpiresOn.ToUnixTimeSeconds(), registry.Find(initial.Session.Identity.TenantNId, "conversation-1")!.Low!.TokenExpiresOn.ToUnixTimeSeconds());

        Assert.Equal(
            refreshed.ExpiresOn.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            principal.FindFirst("exp")?.Value);

        var binding = bindResult.Data!;
        var signalRequest = new SignalMediaRequest
        {
            MediaContextNId = binding.MediaContextNId,
            ContextRevision = binding.ContextRevision,
            NegotiationNId = "negotiation-1",
            Sequence = 1,
            Kind = "Offer",
            Description = new MediaDescriptionDto { Type = "offer", Sdp = "v=0" },
        };
        var signalInvocation = new HubInvocationContext(
            caller,
            mediaServices,
            hub,
            typeof(CollaborationHub).GetMethod(nameof(CollaborationHub.SignalMedia))!,
            [signalRequest]);
        var signalResult = Assert.IsType<MediaHubResult<MediaSignalResultDto>>(await filter.InvokeMethodAsync(
            signalInvocation,
            async _ => (object?)await hub.SignalMedia(signalRequest)));
        Assert.True(signalResult.Ok, $"signal failed: {signalResult.Error?.Code} {signalResult.Error?.MessageKey}");

        var readyRequest = new MediaReadyRequest
        {
            MediaContextNId = binding.MediaContextNId,
            Kind = "Screen",
            SessionNId = "screen-1",
        };
        var readyInvocation = new HubInvocationContext(
            caller,
            mediaServices,
            hub,
            typeof(CollaborationHub).GetMethod(nameof(CollaborationHub.MediaReady))!,
            [readyRequest]);
        var readyResult = Assert.IsType<MediaHubResult<object>>(await filter.InvokeMethodAsync(
            readyInvocation,
            async _ => (object?)await hub.MediaReady(readyRequest)));
        Assert.True(readyResult.Ok);

        var keepAliveRequest = new KeepAliveMediaRequest
        {
            MediaContextNId = binding.MediaContextNId,
            ScreenSessionNId = "screen-1",
        };
        var keepAliveInvocation = new HubInvocationContext(
            caller,
            mediaServices,
            hub,
            typeof(CollaborationHub).GetMethod(nameof(CollaborationHub.KeepAliveMedia))!,
            [keepAliveRequest]);
        var keepAliveResult = Assert.IsType<MediaHubResult<KeepAliveMediaDto>>(await filter.InvokeMethodAsync(
            keepAliveInvocation,
            async _ => (object?)await hub.KeepAliveMedia(keepAliveRequest)));
        Assert.True(keepAliveResult.Ok);
        Assert.True(keepAliveResult.Data?.Screen?.Allowed);

        await service.RevokeAsync(http, CancellationToken.None);
        var afterRevoke = await validator.ValidateAsync(caller, CancellationToken.None);
        Assert.False(afterRevoke.IsCurrent);
        var delegateCalledAfterRevoke = false;
        await Assert.ThrowsAsync<HubException>(async () =>
            await filter.InvokeMethodAsync(bindInvocation, async _ =>
            {
                delegateCalledAfterRevoke = true;
                return (object?)await hub.BindMedia(bindRequest);
            }));
        Assert.False(delegateCalledAfterRevoke);
        Assert.Equal(refreshed.ExpiresOn.ToUnixTimeSeconds(), registry.Find(initial.Session.Identity.TenantNId, "conversation-1")!.Low!.TokenExpiresOn.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Session_token_without_matching_browser_binding_stays_anonymous()
    {
        using var fixture = CreateFixture();
        using var db = CreateDb(fixture.DatabasePath);
        using var store = new SqlEmbeddedHandshakeStore(db);
        var service = CreateService(fixture, store);
        var initial = await EstablishAsync(service, fixture);

        var bearerOnlyContext = Context(fixture.ParentOrigin);
        bearerOnlyContext.Request.Headers.Authorization = $"Bearer {initial.Session.SessionToken}";
        await new EmbeddedSessionAuthenticationMiddleware(_ => Task.CompletedTask).InvokeAsync(bearerOnlyContext, store, fixture.Options, new ConfigurationEmbeddedSubjectIdentityMapper(fixture.Configuration));
        Assert.False(bearerOnlyContext.User.Identity?.IsAuthenticated == true);

        var wrongBindingContext = Context(fixture.ParentOrigin, $"industrial_embedded_binding=wrong; industrial_embedded_session={initial.Session.SessionToken}");
        await new EmbeddedSessionAuthenticationMiddleware(_ => Task.CompletedTask).InvokeAsync(wrongBindingContext, store, fixture.Options, new ConfigurationEmbeddedSubjectIdentityMapper(fixture.Configuration));
        Assert.False(wrongBindingContext.User.Identity?.IsAuthenticated == true);
    }

    [Fact]
    public async Task Disabled_subject_is_rejected_by_existing_session_and_renewal()
    {
        using var fixture = CreateFixture();
        using var db = CreateDb(fixture.DatabasePath);
        using var store = new SqlEmbeddedHandshakeStore(db);
        var service = CreateService(fixture, store);
        var initial = await EstablishAsync(service, fixture);
        fixture.Configuration["EmbeddedCollaboration:Sources:mes-example:SubjectMappings:external-subject-1:Status"] = "Inactive";

        var context = Context(fixture.ParentOrigin, $"{initial.BindingCookie}; industrial_embedded_session={initial.Session.SessionToken}");
        await new EmbeddedSessionAuthenticationMiddleware(_ => Task.CompletedTask).InvokeAsync(context, store, fixture.Options, new ConfigurationEmbeddedSubjectIdentityMapper(fixture.Configuration));
        Assert.False(context.User.Identity?.IsAuthenticated == true);

        var renewalChallengeContext = Context(fixture.ParentOrigin, $"{initial.BindingCookie}; {fixture.SourceCookie}");
        var renewalChallenge = await service.CreateChallengeAsync(fixture.ParentOrigin, renewalChallengeContext, CancellationToken.None);
        var renewalAssertion = await service.IssueAssertionAsync(renewalChallenge.Nonce, renewalChallengeContext, CancellationToken.None);
        await Assert.ThrowsAsync<EmbeddedHandshakeException>(() => service.RenewAsync(renewalAssertion, Context(fixture.ParentOrigin, $"{initial.BindingCookie}; industrial_embedded_session={initial.Session.SessionToken}"), CancellationToken.None));
    }

    [Fact]
    public async Task Security_version_change_is_rejected_by_existing_session_and_renewal()
    {
        using var fixture = CreateFixture();
        using var db = CreateDb(fixture.DatabasePath);
        using var store = new SqlEmbeddedHandshakeStore(db);
        var service = CreateService(fixture, store);
        var initial = await EstablishAsync(service, fixture);
        fixture.Configuration["EmbeddedCollaboration:Sources:mes-example:SubjectMappings:external-subject-1:SecurityVersion"] = "8";

        var context = Context(fixture.ParentOrigin, $"{initial.BindingCookie}; industrial_embedded_session={initial.Session.SessionToken}");
        await new EmbeddedSessionAuthenticationMiddleware(_ => Task.CompletedTask).InvokeAsync(context, store, fixture.Options, new ConfigurationEmbeddedSubjectIdentityMapper(fixture.Configuration));
        Assert.False(context.User.Identity?.IsAuthenticated == true);

        var renewalChallengeContext = Context(fixture.ParentOrigin, $"{initial.BindingCookie}; {fixture.SourceCookie}");
        var renewalChallenge = await service.CreateChallengeAsync(fixture.ParentOrigin, renewalChallengeContext, CancellationToken.None);
        var renewalAssertion = await service.IssueAssertionAsync(renewalChallenge.Nonce, renewalChallengeContext, CancellationToken.None);
        await Assert.ThrowsAsync<EmbeddedHandshakeException>(() => service.RenewAsync(renewalAssertion, Context(fixture.ParentOrigin, $"{initial.BindingCookie}; industrial_embedded_session={initial.Session.SessionToken}"), CancellationToken.None));
    }

    [Fact]
    public async Task Inactive_source_session_cannot_issue_new_assertion()
    {
        using var fixture = CreateFixture();
        using var db = CreateDb(fixture.DatabasePath);
        using var store = new SqlEmbeddedHandshakeStore(db);
        var service = CreateService(fixture, store);
        fixture.Configuration["EmbeddedCollaboration:SourceSessions:source-session-1:Status"] = "Inactive";

        var challengeContext = Context(fixture.ParentOrigin);
        var challenge = await service.CreateChallengeAsync(fixture.ParentOrigin, challengeContext, CancellationToken.None);
        await Assert.ThrowsAsync<EmbeddedHandshakeException>(() => service.IssueAssertionAsync(challenge.Nonce, Context(fixture.ParentOrigin, fixture.SourceCookie), CancellationToken.None));
    }

    [Fact]
    public async Task Source_session_mapping_uses_session_value_not_only_configuration_key_and_fails_closed_when_missing()
    {
        using var fixture = CreateFixture();
        var mapper = new ConfigurationEmbeddedSubjectIdentityMapper(fixture.Configuration);

        void AddSourceSession(
            string key,
            string sessionNId,
            string sourceNId = "mes-example",
            string externalTenantNId = "MES-TENANT-1",
            string externalSubject = "external-subject-1",
            string securityVersion = "7",
            string status = "Active",
            bool? enabled = null)
        {
            var prefix = $"EmbeddedCollaboration:SourceSessions:{key}";
            fixture.Configuration[$"{prefix}:SourceNId"] = sourceNId;
            fixture.Configuration[$"{prefix}:ExternalTenantNId"] = externalTenantNId;
            fixture.Configuration[$"{prefix}:ExternalSubject"] = externalSubject;
            fixture.Configuration[$"{prefix}:SessionNId"] = sessionNId;
            fixture.Configuration[$"{prefix}:SecurityVersion"] = securityVersion;
            fixture.Configuration[$"{prefix}:Status"] = status;
            if (enabled is not null)
                fixture.Configuration[$"{prefix}:Enabled"] = enabled.Value ? "true" : "false";
        }

        EmbeddedStoredSession Session(string sessionNId) => new(
            "session-token-hash",
            "browser-binding-hash",
            new EmbeddedIdentity(
                "T-1",
                ConfigurationEmbeddedSubjectIdentityMapper.UserNId("mes-example", "MES-TENANT-1", "external-subject-1"),
                sessionNId,
                "7")
            {
                SourceNId = "mes-example",
                ExternalTenantNId = "MES-TENANT-1",
                ExternalSubject = "external-subject-1",
                DisplayName = "Alice",
            },
            1,
            DateTimeOffset.UtcNow.AddMinutes(1),
            null);

        // 直接调用无撤销副作用的 mapper，各场景使用不同会话，避免复用已撤销 Token 造成假通过。
        // cookie 索引和显式 SessionNId 故意不同。
        AddSourceSession("upstream-cookie-key", "valid-session");
        Assert.True(await mapper.IsCurrentAsync(Session("valid-session"), CancellationToken.None));

        // 配置键等于旧会话 ID，也不能绕过条目内已轮换的 SessionNId。
        AddSourceSession("old-session", "rotated-session");
        Assert.False(await mapper.IsCurrentAsync(Session("old-session"), CancellationToken.None));

        AddSourceSession("missing-session", "");
        Assert.False(await mapper.IsCurrentAsync(Session("missing-session"), CancellationToken.None));

        AddSourceSession("inactive-session", "inactive-session", status: "Inactive");
        Assert.False(await mapper.IsCurrentAsync(Session("inactive-session"), CancellationToken.None));

        // 先证明有效，再清空这条源记录；不能只检查一个从未存在过的会话。
        AddSourceSession("deleted-cookie", "deleted-session");
        Assert.True(await mapper.IsCurrentAsync(Session("deleted-session"), CancellationToken.None));
        foreach (var field in fixture.Configuration.GetSection("EmbeddedCollaboration:SourceSessions:deleted-cookie").GetChildren())
            fixture.Configuration[field.Path] = null;
        Assert.False(await mapper.IsCurrentAsync(Session("deleted-session"), CancellationToken.None));

        AddSourceSession("disabled-session", "disabled-session", enabled: false);
        Assert.False(await mapper.IsCurrentAsync(Session("disabled-session"), CancellationToken.None));

        AddSourceSession("source-mismatch", "source-mismatch", sourceNId: "other-source");
        Assert.False(await mapper.IsCurrentAsync(Session("source-mismatch"), CancellationToken.None));
        AddSourceSession("tenant-mismatch", "tenant-mismatch", externalTenantNId: "OTHER-TENANT");
        Assert.False(await mapper.IsCurrentAsync(Session("tenant-mismatch"), CancellationToken.None));
        AddSourceSession("subject-mismatch", "subject-mismatch", externalSubject: "external-subject-2");
        Assert.False(await mapper.IsCurrentAsync(Session("subject-mismatch"), CancellationToken.None));
        AddSourceSession("version-mismatch", "version-mismatch", securityVersion: "8");
        Assert.False(await mapper.IsCurrentAsync(Session("version-mismatch"), CancellationToken.None));

        // 同 ID 的无关记录排在前面，仍须找到后面的完整合法匹配。
        AddSourceSession("aaa-unrelated", "duplicate-session", sourceNId: "other-source");
        AddSourceSession("zzz-valid", "duplicate-session");
        Assert.True(await mapper.IsCurrentAsync(Session("duplicate-session"), CancellationToken.None));
    }

    [Fact]
    public async Task Controller_maps_persistence_failures_to_stable_503()
    {
        using var fixture = CreateFixture();
        using var issuer = new ConfigurationEmbeddedIdentityAssertionIssuer(fixture.Configuration);
        var service = CreateService(fixture, new ThrowingEmbeddedHandshakeStore());
        var source = new EmbeddedSourcePrincipal("mes-example", "MES-TENANT-1", "external-subject-1", "Alice", "7", "source-session-1");
        var assertion = issuer.Issue(source, "nonce-1", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(60));
        var cookieContext = Context(fixture.ParentOrigin, "industrial_embedded_binding=binding; industrial_embedded_session=session");

        var createController = Controller(service, fixture.Options, Context(fixture.ParentOrigin));
        AssertStoreUnavailable((await createController.CreateChallenge(CancellationToken.None)).Result);

        var assertionController = Controller(service, fixture.Options, Context(fixture.ParentOrigin));
        AssertStoreUnavailable((await assertionController.GetAssertion("nonce-1", CancellationToken.None)).Result);

        var exchangeController = Controller(service, fixture.Options, Context(fixture.ParentOrigin, "industrial_embedded_binding=binding"));
        AssertStoreUnavailable((await exchangeController.Exchange(new EmbeddedHandshakeCompletion(assertion), CancellationToken.None)).Result);

        var renewController = Controller(service, fixture.Options, cookieContext);
        AssertStoreUnavailable((await renewController.Renew(new EmbeddedHandshakeCompletion(assertion), CancellationToken.None)).Result);

        var revokeController = Controller(service, fixture.Options, cookieContext);
        AssertStoreUnavailable(await revokeController.Revoke(CancellationToken.None));

        var currentController = Controller(service, fixture.Options, cookieContext);
        AssertStoreUnavailable((await currentController.Current(CancellationToken.None)).Result);
    }

    [Fact]
    public async Task Middleware_persistence_failure_stays_anonymous_and_continues_without_authentication()
    {
        using var fixture = CreateFixture();
        var context = Context(fixture.ParentOrigin, "industrial_embedded_binding=binding; industrial_embedded_session=session");
        var nextCalled = false;

        await new EmbeddedSessionAuthenticationMiddleware(_ =>
        {
            nextCalled = true;
            Assert.False(context.User.Identity?.IsAuthenticated == true);
            return Task.CompletedTask;
        }).InvokeAsync(context, new ThrowingEmbeddedHandshakeStore(), fixture.Options, new ConfigurationEmbeddedSubjectIdentityMapper(fixture.Configuration));

        Assert.True(nextCalled);
        Assert.False(context.User.Identity?.IsAuthenticated == true);
    }

    [Fact]
    public void Tampered_payload_and_signature_are_rejected_by_real_validator()
    {
        using var fixture = CreateFixture();
        using var issuer = new ConfigurationEmbeddedIdentityAssertionIssuer(fixture.Configuration);
        var source = new EmbeddedSourcePrincipal("mes-example", "MES-TENANT-1", "external-subject-1", "Alice", "7", "source-session-1");
        var token = issuer.Issue(source, "nonce-1", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(60));

        Assert.ThrowsAny<Exception>(() => issuer.Validate(FlipSegment(token, 1), DateTimeOffset.UtcNow));
        Assert.ThrowsAny<Exception>(() => issuer.Validate(FlipSegment(token, 2), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Expired_and_over_sixty_second_assertions_are_rejected()
    {
        using var fixture = CreateFixture();
        using var issuer = new ConfigurationEmbeddedIdentityAssertionIssuer(fixture.Configuration);
        var source = new EmbeddedSourcePrincipal("mes-example", "MES-TENANT-1", "external-subject-1", "Alice", "7", "source-session-1");
        var expired = issuer.Issue(source, "nonce-expired", DateTimeOffset.UtcNow.AddSeconds(-70), TimeSpan.FromSeconds(30));

        Assert.ThrowsAny<Exception>(() => issuer.Validate(expired, DateTimeOffset.UtcNow));
        var exception = Assert.Throws<EmbeddedHandshakeException>(() => issuer.Issue(source, "nonce-too-long", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(61)));
        Assert.Equal("EMBEDDED_IDENTITY_NOT_READY", exception.Code);
    }

    [Fact]
    public async Task Wrong_external_tenant_mapping_is_rejected_during_exchange()
    {
        using var fixture = CreateFixture();
        using var db = CreateDb(fixture.DatabasePath);
        using var store = new SqlEmbeddedHandshakeStore(db);
        var service = CreateService(fixture, store);
        var challengeContext = Context(fixture.ParentOrigin);
        var challenge = await service.CreateChallengeAsync(fixture.ParentOrigin, challengeContext, CancellationToken.None);
        var bindingCookie = CookiePair(challengeContext);
        using var issuer = new ConfigurationEmbeddedIdentityAssertionIssuer(fixture.Configuration);
        var assertion = issuer.Issue(new EmbeddedSourcePrincipal("mes-example", "MES-TENANT-2", "external-subject-1", "Alice", "7", "source-session-1"), challenge.Nonce, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(60));

        var exception = await Assert.ThrowsAsync<EmbeddedHandshakeException>(() => service.CompleteAsync(assertion, Context(fixture.ParentOrigin, bindingCookie), CancellationToken.None));
        Assert.Equal("EMBEDDED_IDENTITY_NOT_MAPPED", exception.Code);
    }

    [Fact]
    public async Task Switching_subject_revokes_old_session_and_filters_late_old_event()
    {
        using var fixture = CreateFixture();
        using var db = CreateDb(fixture.DatabasePath);
        using var store = new SqlEmbeddedHandshakeStore(db);
        var service = CreateService(fixture, store);
        var initial = await EstablishAsync(service, fixture);
        fixture.Configuration["EmbeddedCollaboration:Sources:mes-example:SubjectMappings:external-subject-2:ExternalTenantNId"] = "MES-TENANT-1";
        fixture.Configuration["EmbeddedCollaboration:Sources:mes-example:SubjectMappings:external-subject-2:DisplayName"] = "Bob";
        fixture.Configuration["EmbeddedCollaboration:Sources:mes-example:SubjectMappings:external-subject-2:SecurityVersion"] = "7";
        fixture.Configuration["EmbeddedCollaboration:Sources:mes-example:SubjectMappings:external-subject-2:Status"] = "Active";
        fixture.Configuration["EmbeddedCollaboration:SourceSessions:source-session-1:ExternalSubject"] = "external-subject-2";

        var oldContext = Context(fixture.ParentOrigin, $"{initial.BindingCookie}; industrial_embedded_session={initial.Session.SessionToken}");
        await new EmbeddedSessionAuthenticationMiddleware(_ => Task.CompletedTask).InvokeAsync(oldContext, store, fixture.Options, new ConfigurationEmbeddedSubjectIdentityMapper(fixture.Configuration));
        Assert.False(oldContext.User.Identity?.IsAuthenticated == true);
        Assert.Null(await store.GetSessionAsync(EmbeddedSessionToken.Hash(initial.Session.SessionToken), BindingHash(initial.BindingCookie), CancellationToken.None));

        var newChallengeContext = Context(fixture.ParentOrigin, $"{initial.BindingCookie}; {fixture.SourceCookie}");
        var newChallenge = await service.CreateChallengeAsync(fixture.ParentOrigin, newChallengeContext, CancellationToken.None);
        var newAssertion = await service.IssueAssertionAsync(newChallenge.Nonce, newChallengeContext, CancellationToken.None);
        var replacement = await service.CompleteAsync(newAssertion, Context(fixture.ParentOrigin, initial.BindingCookie), CancellationToken.None);
        var current = await store.GetSessionAsync(EmbeddedSessionToken.Hash(replacement.SessionToken), BindingHash(initial.BindingCookie), CancellationToken.None);

        Assert.NotNull(current);
        Assert.Equal("external-subject-2", current.Identity.ExternalSubject);
        Assert.False(EmbeddedSessionEpochFilter.Accept(current, initial.Session.Epoch, initial.Session.Identity));
        Assert.False(EmbeddedSessionEpochFilter.Accept(current, current.Epoch, initial.Session.Identity));
        Assert.True(EmbeddedSessionEpochFilter.Accept(current, current.Epoch, current.Identity));
    }

    [Fact]
    public async Task Same_assertion_concurrently_exchanges_once_across_two_store_instances()
    {
        using var fixture = CreateFixture();
        using var db1 = CreateDb(fixture.DatabasePath);
        using var db2 = CreateDb(fixture.DatabasePath);
        using var store1 = new SqlEmbeddedHandshakeStore(db1);
        using var store2 = new SqlEmbeddedHandshakeStore(db2);
        var service1 = CreateService(fixture, store1);
        var service2 = CreateService(fixture, store2);

        var challengeContext = Context(fixture.ParentOrigin);
        var challenge = await service1.CreateChallengeAsync(fixture.ParentOrigin, challengeContext, CancellationToken.None);
        var bindingCookie = CookiePair(challengeContext);
        var assertion = await service1.IssueAssertionAsync(challenge.Nonce, Context(fixture.ParentOrigin, $"{bindingCookie}; {fixture.SourceCookie}"), CancellationToken.None);

        var outcomes = await Task.WhenAll(
            TryCompleteAsync(service1, assertion, Context(fixture.ParentOrigin, bindingCookie)),
            TryCompleteAsync(service2, assertion, Context(fixture.ParentOrigin, bindingCookie)));

        Assert.Equal(1, outcomes.Count(success => success));
        Assert.Equal(1, outcomes.Count(success => !success));
    }

    [Fact]
    public async Task Renewal_uses_new_upstream_assertion_rotates_epoch_and_revoke_invalidates_session()
    {
        using var fixture = CreateFixture();
        using var db = CreateDb(fixture.DatabasePath);
        using var store = new SqlEmbeddedHandshakeStore(db);
        var service = CreateService(fixture, store);
        var initial = await EstablishAsync(service, fixture);

        var renewalChallengeContext = Context(fixture.ParentOrigin, $"{initial.BindingCookie}; {fixture.SourceCookie}");
        var renewalChallenge = await service.CreateChallengeAsync(fixture.ParentOrigin, renewalChallengeContext, CancellationToken.None);
        var renewalAssertion = await service.IssueAssertionAsync(renewalChallenge.Nonce, renewalChallengeContext, CancellationToken.None);
        var renewalContext = Context(fixture.ParentOrigin, $"{initial.BindingCookie}; industrial_embedded_session={initial.Session.SessionToken}");
        var renewed = await service.RenewAsync(renewalAssertion, renewalContext, CancellationToken.None);

        Assert.Equal(2, renewed.Epoch);
        Assert.Null(await store.GetSessionAsync(EmbeddedSessionToken.Hash(initial.Session.SessionToken), BindingHash(initial.BindingCookie), CancellationToken.None));
        Assert.NotNull(await store.GetSessionAsync(EmbeddedSessionToken.Hash(renewed.SessionToken), BindingHash(initial.BindingCookie), CancellationToken.None));

        var revokeContext = Context(fixture.ParentOrigin, $"{initial.BindingCookie}; industrial_embedded_session={renewed.SessionToken}");
        await service.RevokeAsync(revokeContext, CancellationToken.None);
        Assert.Null(await store.GetSessionAsync(EmbeddedSessionToken.Hash(renewed.SessionToken), BindingHash(initial.BindingCookie), CancellationToken.None));
    }

    [Fact]
    public async Task Wrong_origin_binding_and_missing_upstream_authentication_fail_closed()
    {
        using var fixture = CreateFixture();
        using var db = CreateDb(fixture.DatabasePath);
        using var store = new SqlEmbeddedHandshakeStore(db);
        var service = CreateService(fixture, store);

        await Assert.ThrowsAsync<EmbeddedHandshakeException>(() => service.CreateChallengeAsync("https://evil.example", Context(fixture.ParentOrigin), CancellationToken.None));
        var challengeContext = Context(fixture.ParentOrigin);
        var challenge = await service.CreateChallengeAsync(fixture.ParentOrigin, challengeContext, CancellationToken.None);
        await Assert.ThrowsAsync<EmbeddedHandshakeException>(() => service.IssueAssertionAsync(challenge.Nonce, Context(fixture.ParentOrigin), CancellationToken.None));
        var assertionContext = Context(fixture.ParentOrigin, $"{CookiePair(challengeContext)}; {fixture.SourceCookie}");
        var assertion = await service.IssueAssertionAsync(challenge.Nonce, assertionContext, CancellationToken.None);
        await Assert.ThrowsAsync<EmbeddedHandshakeException>(() => service.CompleteAsync(assertion, Context(fixture.ParentOrigin, "industrial_embedded_binding=wrong"), CancellationToken.None));
    }

    [Fact]
    public void Wrong_audience_or_issuer_is_rejected_by_real_assertion_validator()
    {
        using var fixture = CreateFixture(issuer: "https://wrong-issuer.example/identity");
        var issuer = new ConfigurationEmbeddedIdentityAssertionIssuer(fixture.Configuration);
        var source = new EmbeddedSourcePrincipal("mes-example", "MES-TENANT-1", "external-subject-1", "Alice", "7", "source-session-1");
        var token = issuer.Issue(source, "nonce-1", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(60));

        var validator = new ConfigurationEmbeddedIdentityAssertionIssuer(CreateConfiguration(fixture.PrivateKeyPem, fixture.PublicKeyPem));

        Assert.ThrowsAny<Exception>(() => validator.Validate(token, DateTimeOffset.UtcNow));
    }

    private static async Task<(EmbeddedHandshakeSession Session, string BindingCookie)> EstablishAsync(EmbeddedHostHandshakeService service, Fixture fixture)
    {
        var challengeContext = Context(fixture.ParentOrigin);
        var challenge = await service.CreateChallengeAsync(fixture.ParentOrigin, challengeContext, CancellationToken.None);
        var bindingCookie = CookiePair(challengeContext);
        var assertion = await service.IssueAssertionAsync(challenge.Nonce, Context(fixture.ParentOrigin, $"{bindingCookie}; {fixture.SourceCookie}"), CancellationToken.None);
        return (await service.CompleteAsync(assertion, Context(fixture.ParentOrigin, bindingCookie), CancellationToken.None), bindingCookie);
    }

    private static async Task<bool> TryCompleteAsync(EmbeddedHostHandshakeService service, string assertion, HttpContext context)
    {
        try
        {
            await service.CompleteAsync(assertion, context, CancellationToken.None);
            return true;
        }
        catch (EmbeddedHandshakeException exception)
        {
            Assert.True(exception.Code is "EMBEDDED_ASSERTION_REPLAYED" or "EMBEDDED_HANDSHAKE_INVALID");
            return false;
        }
    }

    private static EmbeddedHostHandshakeService CreateService(Fixture fixture, IEmbeddedHandshakeStore store) =>
        new(fixture.Options, new ConfigurationEmbeddedSubjectIdentityMapper(fixture.Configuration), store, new ConfigurationEmbeddedSourcePrincipalResolver(fixture.Configuration, fixture.Options), new ConfigurationEmbeddedIdentityAssertionIssuer(fixture.Configuration));

    private static EmbeddedHandshakeController Controller(EmbeddedHostHandshakeService service, EmbeddedHostHandshakeOptions options, HttpContext context) => new(service, options)
    {
        ControllerContext = new ControllerContext { HttpContext = context },
    };

    private static void AssertStoreUnavailable(IActionResult? result)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        Assert.Equal("EMBEDDED_HANDSHAKE_STORE_UNAVAILABLE", objectResult.Value?.GetType().GetProperty("code")?.GetValue(objectResult.Value));
    }

    private static string FlipSegment(string token, int segmentIndex)
    {
        var segments = token.Split('.');
        var bytes = Encoding.ASCII.GetBytes(segments[segmentIndex]);
        bytes[0] = bytes[0] == (byte)'A' ? (byte)'B' : (byte)'A';
        segments[segmentIndex] = Encoding.ASCII.GetString(bytes);
        return string.Join('.', segments);
    }

    private static DefaultHttpContext Context(string origin, string? cookie = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Origin = origin;
        if (!string.IsNullOrWhiteSpace(cookie))
            context.Request.Headers.Cookie = cookie;
        return context;
    }

    private static string CookiePair(HttpContext context) => context.Response.Headers.SetCookie.ToString().Split(';', 2, StringSplitOptions.RemoveEmptyEntries)[0];

    private static string BindingHash(string cookiePair) => EmbeddedSessionBinding.Hash(cookiePair.Split('=', 2)[1]);

    private static SqlSugarDbContext CreateDb(string path) => new(Options.Create(new SqlSugarOptions { DbType = DbType.Sqlite, ConnectionString = $"Data Source={path};Pooling=False", IsAutoCloseConnection = true }));

    private static Fixture CreateFixture(
        string issuer = "https://embedded-host.example/identity",
        TimeSpan? sessionLifetime = null)
    {
        using var rsa = RSA.Create(2048);
        var privatePem = rsa.ExportPkcs8PrivateKeyPem();
        var publicPem = rsa.ExportSubjectPublicKeyInfoPem();
        var configuration = CreateConfiguration(privatePem, publicPem, issuer);
        return new Fixture(configuration, privatePem, publicPem, Path.Combine(Path.GetTempPath(), $"embedded-handshake-{Guid.NewGuid():N}.db"), "https://parent.example", "embedded_host_session=source-session-1", new EmbeddedHostHandshakeOptions(["https://parent.example"]) { SessionLifetime = sessionLifetime ?? TimeSpan.FromSeconds(60) });
    }

    private static IConfiguration CreateConfiguration(string privatePem, string publicPem, string issuer = "https://embedded-host.example/identity") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EmbeddedCollaboration:Sources:mes-example:Issuer"] = issuer,
            ["EmbeddedCollaboration:Sources:mes-example:Audience"] = "collaboration.embedded",
            ["EmbeddedCollaboration:Sources:mes-example:KeyId"] = "embedded-source-v1",
            ["EmbeddedCollaboration:Sources:mes-example:PrivateKey"] = privatePem,
            ["EmbeddedCollaboration:Sources:mes-example:PublicKey"] = publicPem,
            ["EmbeddedCollaboration:Sources:mes-example:ExternalTenantMappings:MES-TENANT-1"] = "T-1",
            ["EmbeddedCollaboration:Sources:mes-example:SubjectMappings:external-subject-1:ExternalTenantNId"] = "MES-TENANT-1",
            ["EmbeddedCollaboration:Sources:mes-example:SubjectMappings:external-subject-1:DisplayName"] = "Alice",
            ["EmbeddedCollaboration:Sources:mes-example:SubjectMappings:external-subject-1:SecurityVersion"] = "7",
            ["EmbeddedCollaboration:Sources:mes-example:SubjectMappings:external-subject-1:Status"] = "Active",
            ["EmbeddedCollaboration:SourceSessions:source-session-1:SourceNId"] = "mes-example",
            ["EmbeddedCollaboration:SourceSessions:source-session-1:ExternalTenantNId"] = "MES-TENANT-1",
            ["EmbeddedCollaboration:SourceSessions:source-session-1:ExternalSubject"] = "external-subject-1",
            ["EmbeddedCollaboration:SourceSessions:source-session-1:DisplayName"] = "Alice",
            ["EmbeddedCollaboration:SourceSessions:source-session-1:SessionNId"] = "source-session-1",
            ["EmbeddedCollaboration:SourceSessions:source-session-1:SecurityVersion"] = "7",
        }).Build();

    private sealed class Fixture : IDisposable
    {
        public Fixture(IConfiguration configuration, string privateKeyPem, string publicKeyPem, string databasePath, string parentOrigin, string sourceCookie, EmbeddedHostHandshakeOptions options)
        {
            Configuration = configuration;
            PrivateKeyPem = privateKeyPem;
            PublicKeyPem = publicKeyPem;
            DatabasePath = databasePath;
            ParentOrigin = parentOrigin;
            SourceCookie = sourceCookie;
            Options = options;
        }

        public IConfiguration Configuration { get; }
        public string PrivateKeyPem { get; }
        public string PublicKeyPem { get; }
        public string DatabasePath { get; }
        public string ParentOrigin { get; }
        public string SourceCookie { get; }
        public EmbeddedHostHandshakeOptions Options { get; }

        public void Dispose()
        {
            for (var attempt = 0; attempt < 20 && File.Exists(DatabasePath); attempt++)
            {
                try
                {
                    File.Delete(DatabasePath);
                }
                catch (IOException) when (attempt < 19)
                {
                    Thread.Sleep(100);
                }
                catch (UnauthorizedAccessException) when (attempt < 19)
                {
                    Thread.Sleep(100);
                }
                catch (IOException)
                {
                    break;
                }
                catch (UnauthorizedAccessException)
                {
                    break;
                }
            }
        }
    }

    private sealed class ThrowingEmbeddedHandshakeStore : IEmbeddedHandshakeStore
    {
        private static EmbeddedPersistenceException Unavailable() => new("test persistence failure");

        public Task<EmbeddedStoredChallenge?> GetChallengeAsync(string nonce, CancellationToken cancellationToken) => Task.FromException<EmbeddedStoredChallenge?>(Unavailable());
        public Task SaveChallengeAsync(EmbeddedStoredChallenge challenge, CancellationToken cancellationToken) => Task.FromException(Unavailable());
        public Task<EmbeddedChallengeConsumeResult> ConsumeChallengeAsync(string nonce, string browserBindingHash, string jti, DateTimeOffset now, DateTimeOffset assertionExpiresOn, CancellationToken cancellationToken) => Task.FromException<EmbeddedChallengeConsumeResult>(Unavailable());
        public Task<EmbeddedStoredSession?> GetSessionAsync(string sessionTokenHash, string browserBindingHash, CancellationToken cancellationToken) => Task.FromException<EmbeddedStoredSession?>(Unavailable());
        public Task<EmbeddedStoredSession?> TouchSessionAsync(string sessionTokenHash, string browserBindingHash, DateTimeOffset now, DateTimeOffset expiresOn, CancellationToken cancellationToken) => Task.FromException<EmbeddedStoredSession?>(Unavailable());
        public Task<EmbeddedStoredSession> CreateSessionAsync(EmbeddedIdentity identity, string browserBindingHash, string sessionTokenHash, DateTimeOffset expiresOn, CancellationToken cancellationToken) => Task.FromException<EmbeddedStoredSession>(Unavailable());
        public Task RevokeSessionAsync(string sessionTokenHash, string browserBindingHash, DateTimeOffset revokedOn, CancellationToken cancellationToken) => Task.FromException(Unavailable());
    }

    private static ScreenShareSessionRecord CreateAcceptedScreen(string actorUserNId) => new()
    {
        Id = Guid.NewGuid(),
        TenantNId = "T-1",
        SessionNId = "screen-1",
        ConversationNId = "conversation-1",
        InitiatorUserNId = "peer-user",
        InviteeUserNId = actorUserNId,
        Direction = "ShareMine",
        SharerUserNId = "peer-user",
        ViewerUserNId = actorUserNId,
        State = "Accepted",
        RequestNId = "request-1",
        RequestHash = "hash-1",
        DeadlineOn = DateTimeOffset.UtcNow.AddMinutes(5),
        AcceptedOn = DateTimeOffset.UtcNow,
        InitiatorConnectionId = "connection-peer",
        InviteeConnectionId = "connection-a",
        InitiatorAliveUntil = DateTimeOffset.UtcNow.AddMinutes(5),
        InviteeAliveUntil = DateTimeOffset.UtcNow.AddMinutes(5),
        CreatedOn = DateTimeOffset.UtcNow,
        LastUpdatedOn = DateTimeOffset.UtcNow,
        Version = 1,
        ConcurrencyVersion = Guid.NewGuid(),
    };

    #pragma warning disable CA1852
    private class TestMediaRepository : DispatchProxy
    {
        public ScreenShareSessionRecord CurrentScreen { get; set; } = null!;

        public static IRemoteAssistanceRepository Create(ScreenShareSessionRecord screen, out TestMediaRepository proxy)
        {
            var repository = DispatchProxy.Create<IRemoteAssistanceRepository, TestMediaRepository>();
            proxy = (TestMediaRepository)(object)repository;
            proxy.CurrentScreen = screen;
            return repository;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            if (targetMethod.Name == nameof(IRemoteAssistanceRepository.GetScreenAsync))
                return TaskResult(targetMethod.ReturnType, CurrentScreen);
            if (targetMethod.Name == nameof(IRemoteAssistanceRepository.UpdateScreenAsync))
            {
                CurrentScreen = Assert.IsType<ScreenShareSessionRecord>(args?[0]);
                return TaskResult(targetMethod.ReturnType, CurrentScreen);
            }
            throw new NotSupportedException(targetMethod.Name);
        }
    }

    private class TestCollaborationRepository : DispatchProxy
    {
        public static ICollaborationRepository Create(string actorUserNId)
        {
            var repository = DispatchProxy.Create<ICollaborationRepository, TestCollaborationRepository>();
            ((TestCollaborationRepository)(object)repository).ActorUserNId = actorUserNId;
            return repository;
        }

        private string ActorUserNId { get; set; } = string.Empty;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            object value = targetMethod.Name switch
            {
                nameof(ICollaborationRepository.GetConversationAsync) => new ConversationRecord("T-1", "conversation-1", ActorUserNId, "peer-user", "Active", 0, null, null, 0, 1, Guid.NewGuid()),
                nameof(ICollaborationRepository.GetMemberAsync) => new ConversationMemberRecord("T-1", "conversation-1", args?[2]?.ToString() ?? string.Empty, args?[2]?.ToString() ?? string.Empty, DateTimeOffset.UtcNow, "Visible", null, 0, 0, null, 0, 1, Guid.NewGuid()),
                nameof(ICollaborationRepository.GetMembersAsync) => new[]
                {
                    new ConversationMemberRecord("T-1", "conversation-1", ActorUserNId, "Alice", DateTimeOffset.UtcNow, "Visible", null, 0, 0, null, 0, 1, Guid.NewGuid()),
                    new ConversationMemberRecord("T-1", "conversation-1", "peer-user", "Peer", DateTimeOffset.UtcNow, "Visible", null, 0, 0, null, 0, 1, Guid.NewGuid()),
                },
                _ => throw new NotSupportedException(targetMethod.Name),
            };
            return TaskResult(targetMethod.ReturnType, value);
        }
    }

    private sealed class TestDirectory : ICollaborationIdentityDirectory
    {
        public Task<DirectoryUser?> GetAsync(string tenantNId, string userNId, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryUser?>(new DirectoryUser(userNId, userNId, "Active", "7"));

        public Task<DirectorySearchPage> SearchAsync(string tenantNId, string actorUserNId, string keyword, string? cursor, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(new DirectorySearchPage([], null));
    }

    private sealed class TestPresence : ICollaborationPresence
    {
        public PresenceDto SetPresence(string tenantNId, string userNId, string state, string? connectionNId = null) => new() { UserNId = userNId, State = state, ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(1) };
        public PresenceDto GetPresence(string tenantNId, string userNId) => new() { UserNId = userNId, State = "Online", ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(1) };
        public void RemovePresence(string tenantNId, string userNId, string connectionNId) { }
    }

    private sealed class AllowAllMediaPermissions : IRemoteAssistancePermissionEvaluator
    {
        public Task<bool> HasPermissionAsync(string permission, string tenantNId, string userNId, string sessionNId, string securityVersion, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class EmptyIceServers : IRemoteAssistanceIceServerProvider
    {
        public Task<IReadOnlyList<IceServerDto>> GetAsync(string tenantNId, string userNId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<IceServerDto>>([]);
    }

    private sealed class TestCurrentUser(string tenantId, string userNId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserNId { get; } = userNId;
        public string? UserName => UserNId;
        public string? TenantId { get; } = tenantId;
        public IReadOnlyCollection<string> Roles => [];
    }

    private static object TaskResult(Type returnType, object? value)
    {
        if (!returnType.IsGenericType || returnType.GetGenericTypeDefinition() != typeof(Task<>))
            throw new NotSupportedException(returnType.Name);
        return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(returnType.GenericTypeArguments[0]).Invoke(null, [value])!;
    }
    #pragma warning restore CA1852

    #pragma warning disable CA1852
    private class TestHubCallerClients : DispatchProxy
    {
        public static IHubCallerClients Create() => DispatchProxy.Create<IHubCallerClients, TestHubCallerClients>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IHubCallerClients.Client)
                ? TestClientProxy.Create()
                : throw new NotSupportedException(targetMethod?.Name);
    }

    private class TestClientProxy : DispatchProxy
    {
        public static ISingleClientProxy Create() => DispatchProxy.Create<ISingleClientProxy, TestClientProxy>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.ReturnType == typeof(Task))
                return Task.CompletedTask;
            throw new NotSupportedException(targetMethod?.Name);
        }
    }
    #pragma warning restore CA1852

    private sealed class TestHubCallerContext(string connectionId, HttpContext httpContext, ClaimsPrincipal user) : HubCallerContext
    {
        public override string ConnectionId => connectionId;
        public override string? UserIdentifier => user.FindFirstValue(ClaimTypes.NameIdentifier);
        public override ClaimsPrincipal? User => user;
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override IFeatureCollection Features => httpContext.Features;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override void Abort() { }
    }

    private sealed class TestHttpContextFeature : IHttpContextFeature
    {
        public HttpContext? HttpContext { get; set; }
    }
}
