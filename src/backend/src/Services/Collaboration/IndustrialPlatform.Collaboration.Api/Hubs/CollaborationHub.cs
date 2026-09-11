using System.Security.Claims;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace IndustrialPlatform.Collaboration.Api.Hubs;

[Authorize(Policy = "permission:collaboration.messaging.read")]
public sealed class CollaborationHub : Hub
{
    private readonly CollaborationService _service;
    private readonly ICurrentUser _current;
    private readonly IAuthorizationService _authorization;

    public CollaborationHub(CollaborationService service, ICurrentUser current, IAuthorizationService authorization)
    {
        _service = service;
        _current = current;
        _authorization = authorization;
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

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        var tenant = _current.TenantId;
        var actor = _current.UserNId;
        if (!string.IsNullOrWhiteSpace(tenant) && !string.IsNullOrWhiteSpace(actor))
            _service.RemovePresence(tenant, actor, Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    private static string Group(string tenant, string conversationNId) => $"collaboration:{tenant}:{conversationNId}";
    private static string UserGroup(string tenant, string userNId) => $"collaboration-user:{tenant}:{userNId}";
    private static string PresenceGroup(string tenant, string userNId) => $"collaboration-presence:{tenant}:{userNId}";

    private async Task RequirePermissionAsync(string policy)
    {
        if (!(await _authorization.AuthorizeAsync(Context.User ?? new ClaimsPrincipal(), policy)).Succeeded)
            throw new HubException("forbidden");
    }
}
