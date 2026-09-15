using System.Reflection;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Application.RemoteAssistance;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Collaboration.Domain.RemoteAssistance;
using IndustrialPlatform.Security;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Application_RemoteAssistanceBindConcurrencyTests
{
    [Theory]
    [InlineData("ShareMine")]
    [InlineData("RequestPeer")]
    public async Task Screen_invites_reject_a_directory_active_but_offline_peer(string direction)
    {
        var repository = ConversationRepositoryProxy.Create(out _);
        var collaboration = CreateCollaboration(repository, new NoopPresence("Offline"));
        var service = CreateService(collaboration, ScreenRepositoryProxy.Create(CreateScreen(), out _));

        var error = await Assert.ThrowsAsync<RemoteAssistanceException>(() => service.InviteScreenShareAsync(
            "T-1", "U-1", "S-1", "1", DateTimeOffset.UtcNow.AddMinutes(5), "CONN-1",
            new InviteScreenShareRequest { ConversationNId = "CV-1", Direction = direction, RequestNId = "REQ-OFFLINE" },
            CancellationToken.None));

        Assert.Equal("MEDIA_CONFLICT", error.Code);
    }

    [Fact]
    public async Task Voice_invites_reject_a_directory_active_but_offline_peer()
    {
        var repository = ConversationRepositoryProxy.Create(out _);
        var collaboration = CreateCollaboration(repository, new NoopPresence("Offline"));
        var service = CreateService(collaboration, ScreenRepositoryProxy.Create(CreateScreen(), out _));

        var error = await Assert.ThrowsAsync<RemoteAssistanceException>(() => service.InviteVoiceCallAsync(
            "T-1", "U-1", "S-1", "1", DateTimeOffset.UtcNow.AddMinutes(5), "CONN-1",
            new InviteVoiceCallRequest { ConversationNId = "CV-1", RequestNId = "REQ-OFFLINE" },
            CancellationToken.None));

        Assert.Equal("MEDIA_CONFLICT", error.Code);
    }

    [Fact]
    public async Task Disconnect_ends_only_media_bound_to_that_page_connection()
    {
        var screen = CreateScreen() with
        {
            State = ScreenShareState.Sharing.ToString(),
            InviteeConnectionId = "CONN-2",
        };
        var voice = new VoiceCallSessionRecord
        {
            Id = Guid.NewGuid(),
            TenantNId = "T-1",
            CallNId = "VOC-1",
            ConversationNId = "CV-1",
            CallerUserNId = "U-1",
            CalleeUserNId = "U-2",
            State = VoiceCallState.Active.ToString(),
            CallerConnectionId = "CONN-1",
            CalleeConnectionId = "CONN-2",
            DeadlineOn = DateTimeOffset.UtcNow.AddMinutes(5),
            Version = 1,
            ConcurrencyVersion = Guid.NewGuid(),
        };
        var repository = ConnectionMediaRepositoryProxy.Create(screen, voice, out var proxy);
        var service = CreateService(CreateCollaboration(ConversationRepositoryProxy.Create(out _)), repository);

        await service.EndMediaForConnectionAsync("T-1", "U-1", "CONN-1", CancellationToken.None);

        Assert.Equal(ScreenShareState.Ended.ToString(), proxy.Screen.State);
        Assert.Equal(VoiceCallState.Ended.ToString(), proxy.Voice.State);

        var otherPageRepository = ConnectionMediaRepositoryProxy.Create(screen with { State = ScreenShareState.Sharing.ToString() }, voice with { State = VoiceCallState.Active.ToString() }, out var otherProxy);
        var otherPageService = CreateService(CreateCollaboration(ConversationRepositoryProxy.Create(out _)), otherPageRepository);
        await otherPageService.EndMediaForConnectionAsync("T-1", "U-1", "CONN-3", CancellationToken.None);
        Assert.Equal(ScreenShareState.Sharing.ToString(), otherProxy.Screen.State);
        Assert.Equal(VoiceCallState.Active.ToString(), otherProxy.Voice.State);
    }

    [Theory]
    [InlineData("relayonly")]
    [InlineData("RELAYONLY")]
    public async Task Bind_returns_the_normalized_relay_only_policy(string configuredPolicy)
    {
        var screen = CreateScreen();
        var repository = ScreenRepositoryProxy.Create(screen, out _);
        var conversationRepository = ConversationRepositoryProxy.Create(out _);
        var collaboration = CreateCollaboration(conversationRepository);
        var service = new RemoteAssistanceService(
            collaboration,
            repository,
            new Directory(),
            new AllowAllRemotePermissions(),
            new MediaContextRegistry(),
            new MediaContextCoordinator(),
            new EmptyIceServers(),
            Options.Create(new RemoteAssistanceOptions { IcePolicy = configuredPolicy }));

        var binding = await service.BindMediaAsync(
            "T-1", "U-1", "S-1", "1", DateTimeOffset.UtcNow.AddMinutes(5), "CONN-1",
            new BindMediaRequest { ConversationNId = "CV-1", ScreenSessionNId = "SS-1" },
            CancellationToken.None);

        Assert.Equal("RelayOnly", binding.IcePolicy);
    }

    [Fact]
    public async Task Bind_rechecks_the_session_after_waiting_for_the_context_lease()
    {
        var screen = CreateScreen();
        var repository = ScreenRepositoryProxy.Create(screen, out var remoteRepository);
        var conversationRepository = ConversationRepositoryProxy.Create(out _);
        var collaboration = CreateCollaboration(conversationRepository);
        var coordinator = new MediaContextCoordinator();
        var contexts = new MediaContextRegistry();
        using var heldByEnd = await coordinator.AcquireAsync("T-1", "CV-1", CancellationToken.None);
        var service = new RemoteAssistanceService(
            collaboration,
            repository,
            new Directory(),
            new AllowAllRemotePermissions(),
            contexts,
            coordinator,
            new EmptyIceServers(),
            Options.Create(new RemoteAssistanceOptions()));

        var bindTask = service.BindMediaAsync(
            "T-1", "U-1", "S-1", "1", DateTimeOffset.UtcNow.AddMinutes(5), "CONN-1",
            new BindMediaRequest { ConversationNId = "CV-1", ScreenSessionNId = "SS-1" },
            CancellationToken.None);
        var firstReadOrFailure = await Task.WhenAny(remoteRepository.FirstReadCompleted.Task, bindTask);
        if (firstReadOrFailure == bindTask)
            await bindTask;

        remoteRepository.CurrentScreen = screen with { State = ScreenShareState.Ended.ToString(), EndedOn = DateTimeOffset.UtcNow };
        heldByEnd.Dispose();

        var error = await Assert.ThrowsAsync<RemoteAssistanceException>(() => bindTask);
        Assert.Equal("MEDIA_NOT_ACCEPTED", error.Code);
        Assert.Null(contexts.Find("T-1", "CV-1"));
    }

    [Fact]
    public async Task Ending_last_screen_capability_releases_bound_endpoints_for_new_phone()
    {
        var oldScreen = CreateScreen() with { InviteeConnectionId = "CONN-2" };
        var repository = ScreenRepositoryProxy.Create(oldScreen, out var remoteRepository);
        var contexts = new MediaContextRegistry();
        var service = new RemoteAssistanceService(
            CreateCollaboration(ConversationRepositoryProxy.Create(out _)),
            repository,
            new Directory(),
            new AllowAllRemotePermissions(),
            contexts,
            new MediaContextCoordinator(),
            new EmptyIceServers(),
            Options.Create(new RemoteAssistanceOptions { ScreenEnabled = true, VoiceEnabled = true }));

        await service.BindMediaAsync(
            "T-1", "U-1", "S-1", "1", DateTimeOffset.UtcNow.AddMinutes(5), "CONN-1",
            new BindMediaRequest { ConversationNId = "CV-1", ScreenSessionNId = oldScreen.SessionNId },
            CancellationToken.None);
        await service.BindMediaAsync(
            "T-1", "U-2", "S-2", "1", DateTimeOffset.UtcNow.AddMinutes(5), "CONN-2",
            new BindMediaRequest { ConversationNId = "CV-1", ScreenSessionNId = oldScreen.SessionNId },
            CancellationToken.None);

        var boundContext = contexts.Find("T-1", "CV-1");
        Assert.NotNull(boundContext);
        Assert.Equal(oldScreen.SessionNId, boundContext!.Low?.ScreenSessionNId);
        Assert.Equal(oldScreen.SessionNId, boundContext.High?.ScreenSessionNId);

        await service.EndScreenShareAsync(
            "T-1", "U-1", "S-1", "1", oldScreen.SessionNId, "BrowserStopped", CancellationToken.None);

        Assert.Null(contexts.Find("T-1", "CV-1"));

        var newScreen = oldScreen with
        {
            SessionNId = "SS-2",
            RequestNId = "REQ-2",
            RequestHash = "HASH-2",
            InviteeConnectionId = "CONN-3",
            AcceptedOn = DateTimeOffset.UtcNow,
            EndedOn = null,
            EndReason = null,
            Version = 1,
            ConcurrencyVersion = Guid.NewGuid(),
        };
        remoteRepository.CurrentScreen = newScreen;

        var binding = await service.BindMediaAsync(
            "T-1", "U-2", "S-3", "1", DateTimeOffset.UtcNow.AddMinutes(5), "CONN-3",
            new BindMediaRequest { ConversationNId = "CV-1", ScreenSessionNId = newScreen.SessionNId },
            CancellationToken.None);

        Assert.Equal(newScreen.SessionNId, binding.Screen?.SessionNId);
    }

    private static ScreenShareSessionRecord CreateScreen() => new()
    {
        Id = Guid.NewGuid(),
        TenantNId = "T-1",
        SessionNId = "SS-1",
        ConversationNId = "CV-1",
        InitiatorUserNId = "U-1",
        InviteeUserNId = "U-2",
        Direction = "ShareMine",
        SharerUserNId = "U-1",
        ViewerUserNId = "U-2",
        State = ScreenShareState.Accepted.ToString(),
        RequestNId = "REQ-1",
        RequestHash = "HASH-1",
        DeadlineOn = DateTimeOffset.UtcNow.AddMinutes(5),
        AcceptedOn = DateTimeOffset.UtcNow,
        InitiatorConnectionId = "CONN-1",
        CreatedOn = DateTimeOffset.UtcNow,
        LastUpdatedOn = DateTimeOffset.UtcNow,
        Version = 1,
        ConcurrencyVersion = Guid.NewGuid(),
    };

    private static CollaborationService CreateCollaboration(ICollaborationRepository repository) => new(
        repository,
        new Directory(),
        new UnsupportedFilePort(),
        new NoopAuditPort(),
        new NoopPresence(),
        new NoopStepUpVerifier(),
        new NoopStepUpBindingIssuer(),
        new AllowAllCollaborationPermissions(),
        new PageCursorCodec("pf06-bind-test", TimeSpan.FromMinutes(5)));

    private static CollaborationService CreateCollaboration(ICollaborationRepository repository, ICollaborationPresence presence) => new(
        repository,
        new Directory(),
        new UnsupportedFilePort(),
        new NoopAuditPort(),
        presence,
        new NoopStepUpVerifier(),
        new NoopStepUpBindingIssuer(),
        new AllowAllCollaborationPermissions(),
        new PageCursorCodec("pf06-bind-test", TimeSpan.FromMinutes(5)));

    private static RemoteAssistanceService CreateService(CollaborationService collaboration, IRemoteAssistanceRepository repository) => new(
        collaboration,
        repository,
        new Directory(),
        new AllowAllRemotePermissions(),
        new MediaContextRegistry(),
        new MediaContextCoordinator(),
        new EmptyIceServers(),
        Options.Create(new RemoteAssistanceOptions { ScreenEnabled = true, VoiceEnabled = true }));

#pragma warning disable CA1852
    private class ScreenRepositoryProxy : DispatchProxy
    {
        public ScreenShareSessionRecord CurrentScreen { get; set; } = null!;
        public TaskCompletionSource<bool> FirstReadCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reads;

        public static IRemoteAssistanceRepository Create(ScreenShareSessionRecord screen, out ScreenRepositoryProxy proxy)
        {
            var repository = DispatchProxy.Create<IRemoteAssistanceRepository, ScreenRepositoryProxy>();
            proxy = (ScreenRepositoryProxy)(object)repository;
            proxy.CurrentScreen = screen;
            return repository;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            if (targetMethod.Name == nameof(IRemoteAssistanceRepository.GetScreenAsync))
            {
                if (Interlocked.Increment(ref _reads) == 1) FirstReadCompleted.TrySetResult(true);
                return Task.FromResult<ScreenShareSessionRecord?>(CurrentScreen);
            }
            if (targetMethod.Name == nameof(IRemoteAssistanceRepository.UpdateScreenAsync))
                return Task.FromResult<ScreenShareSessionRecord?>(Assert.IsType<ScreenShareSessionRecord>(args?[0]));
            throw new NotSupportedException(targetMethod.Name);
        }
    }

    private class ConnectionMediaRepositoryProxy : DispatchProxy
    {
        public ScreenShareSessionRecord Screen { get; set; } = null!;
        public VoiceCallSessionRecord Voice { get; set; } = null!;

        public static IRemoteAssistanceRepository Create(ScreenShareSessionRecord screen, VoiceCallSessionRecord voice, out ConnectionMediaRepositoryProxy proxy)
        {
            var repository = DispatchProxy.Create<IRemoteAssistanceRepository, ConnectionMediaRepositoryProxy>();
            proxy = (ConnectionMediaRepositoryProxy)(object)repository;
            proxy.Screen = screen;
            proxy.Voice = voice;
            return repository;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            object? value = targetMethod.Name switch
            {
                nameof(IRemoteAssistanceRepository.ListActiveScreensForUserAsync) => new[] { Screen },
                nameof(IRemoteAssistanceRepository.GetScreenAsync) => Screen,
                nameof(IRemoteAssistanceRepository.UpdateScreenAsync) => UpdateScreen(args),
                nameof(IRemoteAssistanceRepository.ListActiveVoicesForUserAsync) => new[] { Voice },
                nameof(IRemoteAssistanceRepository.GetVoiceAsync) => Voice,
                nameof(IRemoteAssistanceRepository.UpdateVoiceAsync) => UpdateVoice(args),
                _ => throw new NotSupportedException(targetMethod.Name),
            };
            return TaskResult(targetMethod.ReturnType, value);
        }

        private ScreenShareSessionRecord UpdateScreen(object?[]? args)
        {
            Screen = Assert.IsType<ScreenShareSessionRecord>(args?[0]);
            return Screen;
        }

        private VoiceCallSessionRecord UpdateVoice(object?[]? args)
        {
            Voice = Assert.IsType<VoiceCallSessionRecord>(args?[0]);
            return Voice;
        }
    }

    private class ConversationRepositoryProxy : DispatchProxy
    {
        public static ICollaborationRepository Create(out ConversationRepositoryProxy proxy)
        {
            var repository = DispatchProxy.Create<ICollaborationRepository, ConversationRepositoryProxy>();
            proxy = (ConversationRepositoryProxy)(object)repository;
            return repository;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            object? value = targetMethod.Name switch
            {
                nameof(ICollaborationRepository.GetConversationAsync) => new ConversationRecord("T-1", "CV-1", "U-1", "U-2", "Active", 0, null, null, 0, 1, Guid.NewGuid()),
                nameof(ICollaborationRepository.GetMemberAsync) => new ConversationMemberRecord("T-1", "CV-1", "U-1", "U-1", DateTimeOffset.UtcNow, "Visible", null, 0, 0, null, 0, 1, Guid.NewGuid()),
                nameof(ICollaborationRepository.GetMembersAsync) => new[]
                {
                    new ConversationMemberRecord("T-1", "CV-1", "U-1", "U-1", DateTimeOffset.UtcNow, "Visible", null, 0, 0, null, 0, 1, Guid.NewGuid()),
                    new ConversationMemberRecord("T-1", "CV-1", "U-2", "U-2", DateTimeOffset.UtcNow, "Visible", null, 0, 0, null, 0, 1, Guid.NewGuid()),
                },
                _ => throw new NotSupportedException(targetMethod.Name),
            };
            return TaskResult(targetMethod.ReturnType, value);
        }
    }
#pragma warning restore CA1852

    private sealed class Directory : ICollaborationIdentityDirectory
    {
        public Task<DirectoryUser?> GetAsync(string tenantNId, string userNId, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryUser?>(new DirectoryUser(userNId, userNId, "Active", "1"));

        public Task<DirectorySearchPage> SearchAsync(string tenantNId, string actorUserNId, string keyword, string? cursor, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(new DirectorySearchPage([], null));
    }

    private sealed class EmptyIceServers : IRemoteAssistanceIceServerProvider
    {
        public Task<IReadOnlyList<IceServerDto>> GetAsync(string tenantNId, string userNId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IceServerDto>>([]);
    }

    private sealed class AllowAllRemotePermissions : IRemoteAssistancePermissionEvaluator
    {
        public Task<bool> HasPermissionAsync(string permission, string tenantNId, string userNId, string sessionNId, string securityVersion, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class AllowAllCollaborationPermissions : ICollaborationPermissionEvaluator
    {
        public Task<bool> HasPermissionAsync(string permission, string tenantNId, string actorUserNId, string actorSessionNId, string actorSecurityVersion, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class UnsupportedFilePort : ICollaborationFilePort
    {
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
        public Task<AttachmentUploadResult> BindReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, string conversationNId, string messageNId, string attachmentNId, string uploaderUserNId, string purpose, string requestNId, CancellationToken cancellationToken)
        {
            _ = this;
            return Unsupported<AttachmentUploadResult>();
        }
        public Task<CollaborationFileState?> GetAsync(string tenantNId, string userNId, string fileNId, CancellationToken cancellationToken) => Task.FromResult<CollaborationFileState?>(null);
        public Task<Stream> OpenContentAsync(string tenantNId, string userNId, string fileNId, string referenceNId, CancellationToken cancellationToken) => Unsupported<Stream>();
        private static Task<T> Unsupported<T>() => Task.FromException<T>(new NotSupportedException());
    }

    private sealed class NoopAuditPort : ICollaborationAuditPort
    {
        public Task WriteAsync(string tenantNId, TrustedCollaborationCall? serviceCall, string actorUserNId, string action, string objectType, string objectNId, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoopPresence : ICollaborationPresence
    {
        private readonly string _state;

        public NoopPresence(string state = "Offline") => _state = state;

        public PresenceDto SetPresence(string tenantNId, string userNId, string state, string? connectionNId = null) => new()
        {
            UserNId = userNId,
            State = state,
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(1),
        };

        public PresenceDto GetPresence(string tenantNId, string userNId) => new()
        {
            UserNId = userNId,
            State = _state,
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(1),
        };
        public void RemovePresence(string tenantNId, string userNId, string connectionNId) { }
    }

    private sealed class NoopStepUpVerifier : IComplianceStepUpVerifier
    {
        public Task EnsureValidAsync(string proof, string tenantNId, string actorUserNId, string actorSessionNId, string actorSecurityVersion, string action, string requestNId, string scopeChecksum, string requestHash, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoopStepUpBindingIssuer : IStepUpBindingIssuer
    {
        public string Issue(string tenantNId, string actorUserNId, string actorSessionNId, string actorSecurityVersion, string action, string requestNId, string scopeChecksum, string requestHash, DateTimeOffset issuedOn, TimeSpan lifetime) => "binding";
    }

    private static object TaskResult(Type returnType, object? value)
    {
        if (!returnType.IsGenericType || returnType.GetGenericTypeDefinition() != typeof(Task<>)) throw new NotSupportedException(returnType.Name);
        return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(returnType.GenericTypeArguments[0]).Invoke(null, [value])!;
    }
}
