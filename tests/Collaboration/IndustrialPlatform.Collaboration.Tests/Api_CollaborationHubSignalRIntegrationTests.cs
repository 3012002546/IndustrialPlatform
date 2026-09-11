using System.Reflection;
using System.Security.Claims;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using IndustrialPlatform.Collaboration.Api.Hubs;
using IndustrialPlatform.Collaboration.Api.Controllers;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Api_CollaborationHubSignalRIntegrationTests
{
    [Fact]
    public async Task Conversation_names_use_current_directory_instead_of_legacy_member_ids()
    {
        var repository = TestRepository.Create(out _);
        var service = new CollaborationService(repository, new TestDirectory(), new TestFilePort(), new TestAuditPort(),
            new TestPresence(), new TestStepUpVerifier(), new TestStepUpBindingIssuer(), new AllowPermissions(),
            new PageCursorCodec("directory-name-test", TimeSpan.FromMinutes(5)));

        var page = await service.ListConversationsAsync("TENANT-1", "USER-1", null, 20, "Visible", false, CancellationToken.None);
        Assert.Equal("Peer", Assert.Single(page.Items).PeerDisplayName);
        var detail = await service.GetConversationAsync("TENANT-1", "USER-1", "CONVERSATION-1", CancellationToken.None);
        Assert.Equal("Peer", detail.PeerMember.DisplayName);
    }

    [Fact]
    public async Task SignalR_client_can_invoke_send_message_and_receive_ack()
    {
        var repository = TestRepository.Create(out var repositoryProxy);
        var service = new CollaborationService(
            repository,
            new TestDirectory(),
            new TestFilePort(),
            new TestAuditPort(),
            new TestPresence(),
            new TestStepUpVerifier(),
            new TestStepUpBindingIssuer(),
            new AllowPermissions(),
            new PageCursorCodec("signalr-test-key", TimeSpan.FromMinutes(5)));

        using var host = new HostBuilder()
            .ConfigureWebHost(webHost => webHost
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSignalR(options => options.EnableDetailedErrors = true);
                services.AddAuthentication(TestAuthenticationHandler.AuthenticationSchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.AuthenticationSchemeName, _ => { });
                services.AddAuthorization(options =>
                {
                    options.AddPolicy("permission:collaboration.messaging.read", policy => policy.RequireAuthenticatedUser());
                    options.AddPolicy("permission:collaboration.messaging.write", policy => policy.RequireAuthenticatedUser());
                });
                services.AddSingleton<IAuthorizationService, AllowAuthorizationService>();
                services.AddSingleton<ICurrentUser>(new TestCurrentUser("TENANT-1", "USER-1"));
                services.AddSingleton(service);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapHub<CollaborationHub>("/hubs/collaboration-v1"));
            }))
            .Start();
        var server = host.GetTestServer();

        var ack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/collaboration-v1", options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        connection.On<JsonElement>("message.ack", _ => ack.TrySetResult());

        await connection.StartAsync();
        await connection.InvokeAsync("JoinConversation", "CONVERSATION-1");
        var message = await connection.InvokeAsync<MessageDto>(
            "SendMessage",
            "CONVERSATION-1",
            new SendMessageRequest
            {
                ClientMessageNId = "CLIENT-1",
                MessageType = "Text",
                TextContent = "hello",
            });

        Assert.Equal("CLIENT-1", message.ClientMessageNId);
        Assert.Equal("hello", message.TextContent);
        await ack.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, repositoryProxy.AppendedMessage?.Sequence);
    }

    [Fact]
    public async Task Http_read_cursor_binds_a_complete_json_body_and_advances_the_cursor()
    {
        var repository = TestRepository.Create(out var repositoryProxy);
        var service = new CollaborationService(
            repository,
            new TestDirectory(),
            new TestFilePort(),
            new TestAuditPort(),
            new TestPresence(),
            new TestStepUpVerifier(),
            new TestStepUpBindingIssuer(),
            new AllowPermissions(),
            new PageCursorCodec("http-test-key", TimeSpan.FromMinutes(5)));

        using var host = new HostBuilder()
            .ConfigureWebHost(webHost => webHost
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddControllers().AddApplicationPart(typeof(CollaborationController).Assembly);
                services.AddAuthentication(TestAuthenticationHandler.AuthenticationSchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.AuthenticationSchemeName, _ => { });
                services.AddAuthorization(options =>
                {
                    options.AddPolicy("permission:collaboration.messaging.read-cursor.update", policy => policy.RequireAuthenticatedUser());
                });
                services.AddSingleton<IAuthorizationService, AllowAuthorizationService>();
                services.AddSingleton<ICurrentUser>(new TestCurrentUser("TENANT-1", "USER-1"));
                services.AddSingleton(service);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }))
            .Start();

        using var client = host.GetTestServer().CreateClient();
        using var response = await client.PutAsJsonAsync(
            "/collaboration/api/v1/conversations/CONVERSATION-1/read-cursor",
            new { sequence = 1 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(1, repositoryProxy.LastReadSequence);
    }

    private sealed class TestCurrentUser(string tenantId, string userNId) : ICurrentUser
    {
        private readonly string _tenantId = tenantId;
        private readonly string _userNId = userNId;

        public bool IsAuthenticated => true;
        public string? UserNId => _userNId;
        public string? UserName => _userNId;
        public string? TenantId => _tenantId;
        public IReadOnlyCollection<string> Roles => [];
    }

    private sealed class AllowAuthorizationService : IAuthorizationService
    {
        private readonly object _instanceMarker = new();

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
        {
            _ = _instanceMarker;
            return Success();
        }

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, string policyName)
        {
            _ = _instanceMarker;
            return Success();
        }

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
        {
            _ = _instanceMarker;
            return Success();
        }
        private static Task<AuthorizationResult> Success() => Task.FromResult(AuthorizationResult.Success());
    }

    private sealed class AllowPermissions : ICollaborationPermissionEvaluator
    {
        public Task<bool> HasPermissionAsync(string permission, string tenantNId, string actorUserNId, string actorSessionNId, string actorSecurityVersion, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class TestDirectory : ICollaborationIdentityDirectory
    {
        public Task<DirectoryUser?> GetAsync(string tenantNId, string userNId, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryUser?>(new DirectoryUser(userNId, userNId == "USER-2" ? "Peer" : "Sender", "Active", "1"));

        public Task<DirectorySearchPage> SearchAsync(string tenantNId, string actorUserNId, string keyword, string? cursor, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(new DirectorySearchPage([], null));
    }

    private sealed class TestAuditPort : ICollaborationAuditPort
    {
        public Task WriteAsync(string tenantNId, TrustedCollaborationCall? serviceCall, string actorUserNId, string action, string objectType, string objectNId, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestPresence : ICollaborationPresence
    {
        public PresenceDto SetPresence(string tenantNId, string userNId, string state, string? connectionNId = null) => Create(userNId, state);
        public PresenceDto GetPresence(string tenantNId, string userNId) => Create(userNId, "Offline");
        public void RemovePresence(string tenantNId, string userNId, string connectionNId) { }

        private static PresenceDto Create(string userNId, string state) => new()
        {
            UserNId = userNId,
            State = state,
            ObservedOn = DateTimeOffset.UtcNow,
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(1),
        };
    }

    private sealed class TestStepUpVerifier : IComplianceStepUpVerifier
    {
        public Task EnsureValidAsync(string proof, string tenantNId, string actorUserNId, string actorSessionNId, string actorSecurityVersion, string action, string requestNId, string scopeChecksum, string requestHash, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestStepUpBindingIssuer : IStepUpBindingIssuer
    {
        public string Issue(string tenantNId, string actorUserNId, string actorSessionNId, string actorSecurityVersion, string action, string requestNId, string scopeChecksum, string requestHash, DateTimeOffset issuedOn, TimeSpan lifetime) => "test-binding";
    }

    private sealed class TestFilePort : ICollaborationFilePort
    {
        private static Task<T> Unsupported<T>() => Task.FromException<T>(new NotSupportedException());
        private static Task Unsupported() => Task.FromException(new NotSupportedException());

        public Task<AttachmentUploadResult> CreateUploadAsync(string tenantNId, string userNId, AttachmentRecord attachment, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> GetUploadAsync(string tenantNId, string userNId, string sessionNId, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> SetContentHashAsync(string tenantNId, string userNId, string sessionNId, string sha256, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> ResumeProofAsync(string tenantNId, string userNId, string sessionNId, int writerEpoch, string proof, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> TakeoverAsync(string tenantNId, string userNId, string sessionNId, int expectedWriterEpoch, string? idempotencyKey, string? proof, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> PauseAsync(string tenantNId, string userNId, string sessionNId, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> ResumeAsync(string tenantNId, string userNId, string sessionNId, int writerEpoch, string proof, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> CancelAsync(string tenantNId, string userNId, string sessionNId, string? reason, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> AppendAsync(string tenantNId, string userNId, string transportId, long expectedOffset, int writerEpoch, Stream content, string? resumeTicket, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> CompleteUploadAsync(string tenantNId, string userNId, string sessionNId, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<string?> AddReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, CancellationToken cancellationToken, string? purpose = null) => Task.FromResult<string?>(null);
        public Task<CollaborationFileState?> GetAsync(string tenantNId, string userNId, string fileNId, CancellationToken cancellationToken) => Task.FromResult<CollaborationFileState?>(null);
        public Task<Stream> OpenContentAsync(string tenantNId, string userNId, string fileNId, string referenceNId, CancellationToken cancellationToken) => Unsupported<Stream>();
    }

#pragma warning disable CA1852 // DispatchProxy requires an inheritable proxy type.
    private class TestRepository : DispatchProxy
    {
        public MessageRecord? AppendedMessage { get; private set; }
        public long LastReadSequence { get; private set; }

        public static ICollaborationRepository Create(out TestRepository proxy)
        {
            var repository = DispatchProxy.Create<ICollaborationRepository, TestRepository>();
            proxy = (TestRepository)(object)repository;
            return repository;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            var resultType = targetMethod.ReturnType;
            object? value = targetMethod.Name switch
            {
                nameof(ICollaborationRepository.GetConversationAsync) => Conversation,
                nameof(ICollaborationRepository.GetMemberAsync) => Member with { LastReadSequence = LastReadSequence },
                nameof(ICollaborationRepository.GetMembersAsync) => new[] { Member, PeerMember },
                nameof(ICollaborationRepository.ListMembersForConversationsAsync) => new[] { Member, PeerMember with { DisplayNameSnapshot = "USER-2" } },
                nameof(ICollaborationRepository.ListConversationsAsync) => new[] { Conversation },
                nameof(ICollaborationRepository.ListLatestVisibleMessagesForUserAsync) => new Dictionary<string, MessageRecord>(),
                nameof(ICollaborationRepository.FindMessageByClientAsync) => null,
                nameof(ICollaborationRepository.AppendMessageAsync) => Append(args),
                nameof(ICollaborationRepository.UpdateReadCursorAsync) => UpdateReadCursor(args),
                nameof(ICollaborationRepository.ListDispositionsAsync) => Array.Empty<ComplianceDispositionRecord>(),
                nameof(ICollaborationRepository.GetAttachmentAsync) => null,
                _ => throw new NotSupportedException($"Unexpected repository method: {targetMethod.Name}"),
            };
            if (resultType == typeof(Task)) return Task.CompletedTask;
            if (!resultType.IsGenericType || resultType.GetGenericTypeDefinition() != typeof(Task<>))
                throw new NotSupportedException($"Unexpected repository return type: {resultType}");
            return typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType.GenericTypeArguments[0])
                .Invoke(null, [value]);
        }

        private object? UpdateReadCursor(object?[]? args)
        {
            LastReadSequence = Convert.ToInt64(args?[3], System.Globalization.CultureInfo.InvariantCulture);
            return null;
        }

        private MessageRecord Append(object?[]? args)
        {
            var message = Assert.IsType<MessageRecord>(args?[1]);
            AppendedMessage = message with { Sequence = 1 };
            return AppendedMessage;
        }

        private static ConversationRecord Conversation => new(
            "TENANT-1", "CONVERSATION-1", "USER-1", "USER-2", "Active", 1, null, null, 0, 1, Guid.NewGuid());

        private static ConversationMemberRecord Member => new(
            "TENANT-1", "CONVERSATION-1", "USER-1", "Sender", DateTimeOffset.UtcNow, "Visible", null, 0, 0, null, 0, 1, Guid.NewGuid());

        private static ConversationMemberRecord PeerMember => new(
            "TENANT-1", "CONVERSATION-1", "USER-2", "Peer", DateTimeOffset.UtcNow, "Visible", null, 0, 0, null, 0, 1, Guid.NewGuid());
    }
#pragma warning restore CA1852

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string AuthenticationSchemeName = "Test";

        public TestAuthenticationHandler(
            Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
            Microsoft.Extensions.Logging.ILoggerFactory logger,
            System.Text.Encodings.Web.UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimConstants.UserNId, "USER-1"),
                new Claim(ClaimConstants.UserName, "sender"),
                new Claim(ClaimConstants.TenantId, "TENANT-1"),
                new Claim(ClaimConstants.SessionId, "SESSION-1"),
                new Claim(ClaimConstants.AuthVersion, "1"),
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationSchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, AuthenticationSchemeName)));
        }
    }
}
