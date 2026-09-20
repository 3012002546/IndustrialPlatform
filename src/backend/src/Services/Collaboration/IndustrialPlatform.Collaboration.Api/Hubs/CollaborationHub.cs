using System.Diagnostics;
using System.Security.Claims;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Collaboration.Application.RemoteAssistance;
using IndustrialPlatform.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialPlatform.Collaboration.Api.Hubs;

public interface ICollaborationHubSessionValidator
{
    Task<CollaborationHubSessionValidation> ValidateAsync(HubCallerContext context, CancellationToken cancellationToken);
}

public sealed record CollaborationHubSessionValidation(bool IsCurrent, DateTimeOffset? TokenExpiresOn = null);

public sealed class AllowAllCollaborationHubSessionValidator : ICollaborationHubSessionValidator
{
    public Task<CollaborationHubSessionValidation> ValidateAsync(HubCallerContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new CollaborationHubSessionValidation(true));
}

public sealed class CollaborationHubSessionFilter(
    ICollaborationHubSessionValidator validator,
    MediaContextRegistry? mediaContexts = null) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var validation = await validator.ValidateAsync(invocationContext.Context, invocationContext.Context.ConnectionAborted);
        if (!validation.IsCurrent)
        {
            invocationContext.Context.Abort();
            throw new HubException("unauthorized");
        }

        ApplyValidation(invocationContext.Context, validation);
        return await next(invocationContext);
    }

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        var validation = await validator.ValidateAsync(context.Context, context.Context.ConnectionAborted);
        if (!validation.IsCurrent)
        {
            context.Context.Abort();
            throw new HubException("unauthorized");
        }

        ApplyValidation(context.Context, validation);
        await next(context);
    }

    public Task OnDisconnectedAsync(
        HubLifetimeContext context,
        Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next) => next(context, exception);

    private void ApplyValidation(HubCallerContext context, CollaborationHubSessionValidation validation)
    {
        if (validation.TokenExpiresOn is not { } expiresOn)
            return;

        if (context.User?.Identities.FirstOrDefault(identity => identity.IsAuthenticated) is { } identity)
        {
            var existing = identity.FindFirst("exp");
            if (existing is not null)
                identity.RemoveClaim(existing);
            identity.AddClaim(new Claim("exp", expiresOn.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        mediaContexts?.RefreshConnectionLease(context.ConnectionId, expiresOn);
    }
}

[Authorize(Policy = "permission:collaboration.messaging.read")]
public sealed class CollaborationHub : Hub
{
    private readonly CollaborationService _service;
    private readonly ICurrentUser _current;
    private readonly IAuthorizationService _authorization;
    private readonly RemoteAssistanceService? _media;
    private readonly MediaContextRegistry _mediaContexts;

    public CollaborationHub(
        CollaborationService service,
        ICurrentUser current,
        IAuthorizationService authorization,
        IServiceProvider? services = null)
    {
        _service = service;
        _current = current;
        _authorization = authorization;
        _media = services?.GetService<RemoteAssistanceService>();
        _mediaContexts = services?.GetService<MediaContextRegistry>() ?? new MediaContextRegistry();
    }

    public async Task JoinConversation(string conversationNId)
    {
        var tenant = _current.TenantId ?? throw new HubException("unauthorized");
        var actor = _current.UserNId ?? throw new HubException("unauthorized");
        var conversation = await _service.GetConversationAsync(tenant, actor, conversationNId, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(tenant, conversationNId), Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, PresenceGroup(tenant, actor), Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, PresenceGroup(tenant, conversation.PeerMember.UserNId), Context.ConnectionAborted);
    }

    public Task LeaveConversation(string conversationNId)
    {
        var tenant = _current.TenantId ?? throw new HubException("unauthorized");
        var actor = _current.UserNId ?? throw new HubException("unauthorized");
        return LeaveConversationAsync(tenant, actor, conversationNId);
    }

    private async Task LeaveConversationAsync(string tenant, string actor, string conversationNId)
    {
        var conversation = await _service.GetConversationAsync(tenant, actor, conversationNId, Context.ConnectionAborted);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(tenant, conversationNId), Context.ConnectionAborted);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, PresenceGroup(tenant, actor), Context.ConnectionAborted);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, PresenceGroup(tenant, conversation.PeerMember.UserNId), Context.ConnectionAborted);
    }

    public async Task<MessageDto> SendMessage(string conversationNId, SendMessageRequest request)
    {
        await RequirePermissionAsync("permission:collaboration.messaging.write");
        var tenant = _current.TenantId ?? throw new HubException("unauthorized");
        var actor = _current.UserNId ?? throw new HubException("unauthorized");
        var message = await _service.SendMessageAsync(tenant, actor, conversationNId, request, Context.ConnectionAborted);
        await Clients.Caller.SendAsync("message.ack", new { conversationNId, request.ClientMessageNId, messageNId = message.MessageNId, sequence = message.Sequence }, Context.ConnectionAborted);
        return message;
    }

    public async Task SetTyping(string conversationNId, bool isTyping)
    {
        await RequirePermissionAsync("permission:collaboration.messaging.write");
        var tenant = _current.TenantId ?? throw new HubException("unauthorized");
        var actor = _current.UserNId ?? throw new HubException("unauthorized");
        await _service.GetConversationAsync(tenant, actor, conversationNId, Context.ConnectionAborted);
        await Clients.Group(Group(tenant, conversationNId)).SendAsync("typing", new { conversationNId, userNId = actor, isTyping }, Context.ConnectionAborted);
    }

    public async Task<PresenceDto> SetPresence(string state)
    {
        await RequirePermissionAsync("permission:collaboration.presence.write");
        var tenant = _current.TenantId ?? throw new HubException("unauthorized");
        var actor = _current.UserNId ?? throw new HubException("unauthorized");
        var presence = _service.SetPresence(tenant, actor, new PresenceUpdateRequest { State = state }, Context.ConnectionId);
        await Clients.Group(PresenceGroup(tenant, actor)).SendAsync("presence.changed", presence);
        return presence;
    }

    public override async Task OnConnectedAsync()
    {
        var tenant = _current.TenantId;
        var actor = _current.UserNId;
        if (!string.IsNullOrWhiteSpace(tenant) && !string.IsNullOrWhiteSpace(actor))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(tenant, actor), Context.ConnectionAborted);
            if ((await _authorization.AuthorizeAsync(Context.User ?? new ClaimsPrincipal(), "permission:collaboration.presence.connect")).Succeeded)
                _service.SetPresence(tenant, actor, new PresenceUpdateRequest { State = "Online" }, Context.ConnectionId);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var tenant = _current.TenantId;
        var actor = _current.UserNId;
        if (!string.IsNullOrWhiteSpace(tenant) && !string.IsNullOrWhiteSpace(actor) && _media is not null)
        {
            try
            {
                await _media.EndMediaForConnectionAsync(tenant, actor, Context.ConnectionId, CancellationToken.None);
            }
            catch (Exception cleanupException) when (cleanupException is not OperationCanceledException)
            {
                // The lifecycle worker remains the durable timeout fallback;
                // a disconnect must still release the in-memory context.
            }
        }
        if (!string.IsNullOrWhiteSpace(tenant) && !string.IsNullOrWhiteSpace(actor))
        {
            _service.RemovePresence(tenant, actor, Context.ConnectionId);
            var presence = _service.GetPresence(tenant, actor);
            await Clients.Group(PresenceGroup(tenant, actor)).SendAsync("presence.changed", presence, CancellationToken.None);
        }
        _mediaContexts.RemoveConnection(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<MediaHubResult<ScreenDto>> InviteScreenShare(InviteScreenShareRequest request) =>
        await ExecuteMediaAsync(() => _media!.InviteScreenShareAsync(Tenant, Actor, Session, SecurityVersion, TokenExpiresOn, Context.ConnectionId, request, Context.ConnectionAborted));

    public async Task<MediaHubResult<ScreenDto>> RespondScreenShare(RespondScreenShareRequest request) =>
        await ExecuteMediaAsync(() => _media!.RespondScreenShareAsync(Tenant, Actor, Session, SecurityVersion, TokenExpiresOn, Context.ConnectionId, request, Context.ConnectionAborted));

    public async Task<MediaHubResult<ScreenDto>> EndScreenShare(EndScreenShareRequest request)
    {
        var result = await ExecuteMediaAsync(() => _media!.EndScreenShareAsync(Tenant, Actor, Session, SecurityVersion, request.SessionNId, request.Reason, Context.ConnectionAborted));
        if (result.Ok && result.Data is { } screen)
            await NotifyMediaContextAsync(screen.ConversationNId, Context.ConnectionAborted);
        return result;
    }

    public async Task<MediaHubResult<VoiceDto>> InviteVoiceCall(InviteVoiceCallRequest request) =>
        await ExecuteMediaAsync(() => _media!.InviteVoiceCallAsync(Tenant, Actor, Session, SecurityVersion, TokenExpiresOn, Context.ConnectionId, request, Context.ConnectionAborted));

    public async Task<MediaHubResult<VoiceDto>> RespondVoiceCall(RespondVoiceCallRequest request) =>
        await ExecuteMediaAsync(() => _media!.RespondVoiceCallAsync(Tenant, Actor, Session, SecurityVersion, TokenExpiresOn, Context.ConnectionId, request, Context.ConnectionAborted));

    public async Task<MediaHubResult<VoiceDto>> EndVoiceCall(EndVoiceCallRequest request)
    {
        var result = await ExecuteMediaAsync(() => _media!.EndVoiceCallAsync(Tenant, Actor, Session, SecurityVersion, request.CallNId, request.Reason, Context.ConnectionAborted));
        if (result.Ok && result.Data is { } voice)
            await NotifyMediaContextAsync(voice.ConversationNId, Context.ConnectionAborted);
        return result;
    }

    public async Task<MediaHubResult<ConversationMediaDto>> GetConversationMedia(GetConversationMediaRequest request) =>
        await ExecuteMediaAsync(() => _media!.GetConversationMediaAsync(Tenant, Actor, request, Context.ConnectionAborted));

    public async Task<MediaHubResult<ActiveMediaPageDto>> GetMyActiveMedia(GetMyActiveMediaRequest request) =>
        await ExecuteMediaAsync(() => _media!.GetMyActiveMediaAsync(Tenant, Actor, request, Context.ConnectionAborted));

    public async Task<MediaHubResult<MediaBindingDto>> BindMedia(BindMediaRequest request)
    {
        var result = await ExecuteMediaAsync(() => _media!.BindMediaAsync(Tenant, Actor, Session, SecurityVersion, TokenExpiresOn, Context.ConnectionId, request, Context.ConnectionAborted));
        if (result.Ok && result.Data is { } binding)
            await NotifyPeerMediaContextAsync(request.ConversationNId, binding, Context.ConnectionAborted);
        return result;
    }

    public async Task<MediaHubResult<MediaSignalResultDto>> SignalMedia(SignalMediaRequest request)
    {
        var result = await ExecuteMediaAsync(() => _media!.SignalMediaAsync(Tenant, Actor, Context.ConnectionId, request, Context.ConnectionAborted));
        if (result.Ok && result.Data?.TargetConnectionId is { Length: > 0 } target)
        {
            var payload = new MediaEventEnvelope<SignalMediaRequest>
            {
                EventNId = Guid.NewGuid().ToString("N"),
                OccurredOn = DateTimeOffset.UtcNow,
                Payload = request,
            };
            await Clients.Client(target).SendAsync("media.signal", payload, Context.ConnectionAborted);
        }
        return result;
    }

    public async Task<MediaHubResult<object>> MediaReady(MediaReadyRequest request) =>
        await ExecuteMediaAsync(() => _media!.MediaReadyAsync(Tenant, Actor, Context.ConnectionId, request, Context.ConnectionAborted));

    public async Task<MediaHubResult<KeepAliveMediaDto>> KeepAliveMedia(KeepAliveMediaRequest request) =>
        await ExecuteMediaAsync(() => _media!.KeepAliveMediaAsync(Tenant, Actor, Context.ConnectionId, request, Context.ConnectionAborted));

    public async Task<MediaHubResult<VoiceMutedDto>> SetVoiceMuted(SetVoiceMutedRequest request)
    {
        var result = await ExecuteMediaAsync(() => _media!.SetVoiceMutedAsync(Tenant, Actor, Context.ConnectionId, request, Context.ConnectionAborted));
        if (result.Ok && result.Data?.TargetConnectionId is { Length: > 0 } target)
            await Clients.Client(target).SendAsync("voice-call.muted", result.Data with { TargetConnectionId = null }, Context.ConnectionAborted);
        return result;
    }

    public async Task<MediaHubResult<object>> ReportMediaStopped(ReportMediaStoppedRequest request) =>
        await ExecuteMediaAsync(() => _media!.ReportMediaStoppedAsync(Tenant, Actor, request, Context.ConnectionAborted));

    public async Task<MediaHubResult<EndAllMediaDto>> EndAllMedia(EndAllMediaRequest request) =>
        await EndAllMediaAndNotifyAsync(request);

    private static string Group(string tenant, string conversationNId) => $"collaboration:{tenant}:{conversationNId}";
    private static string UserGroup(string tenant, string userNId) => $"collaboration-user:{tenant}:{userNId}";
    private static string PresenceGroup(string tenant, string userNId) => $"collaboration-presence:{tenant}:{userNId}";

    private async Task NotifyPeerMediaContextAsync(string conversationNId, MediaBindingDto binding, CancellationToken cancellationToken)
    {
        var context = _mediaContexts.Find(Tenant, conversationNId);
        if (context is null) return;
        var target = binding.EndpointRole.Equals("Low", StringComparison.OrdinalIgnoreCase)
            ? context.High?.ConnectionId
            : context.Low?.ConnectionId;
        if (string.IsNullOrWhiteSpace(target)) return;
        var payload = new MediaEventEnvelope<MediaContextChangedDto>
        {
            EventNId = Guid.NewGuid().ToString("N"),
            OccurredOn = DateTimeOffset.UtcNow,
            Payload = new MediaContextChangedDto
            {
                MediaContextNId = context.MediaContextNId,
                ContextRevision = context.Revision,
                ScreenSessionNId = context.ScreenSessionNId,
                VoiceCallNId = context.VoiceCallNId,
            },
        };
        await Clients.Client(target).SendAsync("media.context.changed", payload, cancellationToken);
    }

    private async Task NotifyMediaContextAsync(string conversationNId, CancellationToken cancellationToken)
    {
        var context = _mediaContexts.Find(Tenant, conversationNId);
        if (context is null) return;
        var connections = new[] { context.Low?.ConnectionId, context.High?.ConnectionId }
            .Where(connectionId => !string.IsNullOrWhiteSpace(connectionId))
            .Select(connectionId => connectionId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (connections.Length == 0) return;
        var payload = new MediaEventEnvelope<MediaContextChangedDto>
        {
            EventNId = Guid.NewGuid().ToString("N"),
            OccurredOn = DateTimeOffset.UtcNow,
            Payload = new MediaContextChangedDto
            {
                MediaContextNId = context.MediaContextNId,
                ContextRevision = context.Revision,
                ScreenSessionNId = context.ScreenSessionNId,
                VoiceCallNId = context.VoiceCallNId,
            },
        };
        await Clients.Clients(connections).SendAsync("media.context.changed", payload, cancellationToken);
    }

    private async Task<MediaHubResult<EndAllMediaDto>> EndAllMediaAndNotifyAsync(EndAllMediaRequest request)
    {
        var result = await ExecuteMediaAsync(() => _media!.EndAllMediaAsync(Tenant, Actor, Session, SecurityVersion, request, Context.ConnectionAborted));
        if (result.Ok && result.Data is { } ended)
        {
            var conversationNId = ended.Screen?.ConversationNId ?? ended.Voice?.ConversationNId;
            if (conversationNId is not null)
                await NotifyMediaContextAsync(conversationNId, Context.ConnectionAborted);
        }
        return result;
    }

    private async Task RequirePermissionAsync(string policy)
    {
        if (!(await _authorization.AuthorizeAsync(Context.User ?? new ClaimsPrincipal(), policy)).Succeeded)
            throw new HubException("forbidden");
    }

    private string Tenant => _current.TenantId ?? throw new HubException("unauthorized");
    private string Actor => _current.UserNId ?? throw new HubException("unauthorized");
    private string Session => Context.User?.FindFirst(ClaimConstants.SessionId)?.Value ?? string.Empty;
    private string SecurityVersion => Context.User?.FindFirst(ClaimConstants.AuthVersion)?.Value ?? string.Empty;
    private DateTimeOffset TokenExpiresOn
    {
        get
        {
            var raw = Context.User?.FindFirst("exp")?.Value;
            return long.TryParse(raw, out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : throw new HubException("unauthorized");
        }
    }

    private static async Task<MediaHubResult<T>> ExecuteMediaAsync<T>(Func<Task<T>> action)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        try
        {
            return new MediaHubResult<T> { Ok = true, Data = await action(), TraceId = traceId };
        }
        catch (RemoteAssistanceException exception)
        {
            return new MediaHubResult<T> { Ok = false, Error = new MediaErrorDto { Code = exception.Code, MessageKey = exception.MessageKey }, TraceId = traceId };
        }
        catch (MediaContextException exception)
        {
            return new MediaHubResult<T> { Ok = false, Error = new MediaErrorDto { Code = exception.Code, MessageKey = "collaboration.media.contextError" }, TraceId = traceId };
        }
        catch (CollaborationException exception)
        {
            return new MediaHubResult<T> { Ok = false, Error = new MediaErrorDto { Code = exception.Code.StartsWith("MEDIA_", StringComparison.Ordinal) ? exception.Code : "MEDIA_DEPENDENCY_UNAVAILABLE", MessageKey = "collaboration.media.dependencyUnavailable" }, TraceId = traceId };
        }
    }
}
