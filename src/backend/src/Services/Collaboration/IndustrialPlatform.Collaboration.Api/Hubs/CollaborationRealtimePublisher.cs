using System.Text.Json;
using IndustrialPlatform.EventBus.Abstractions;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace IndustrialPlatform.Collaboration.Api.Hubs;

/// <summary>Turns durable outbox ids into the safe SignalR message projection.</summary>
public sealed class CollaborationRealtimePublisher : ICollaborationRealtimePublisher
{
    private readonly IHubContext<CollaborationHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEventBus _eventBus;

    public CollaborationRealtimePublisher(IHubContext<CollaborationHub> hub, IServiceScopeFactory scopeFactory, IEventBus eventBus)
    {
        _hub = hub;
        _scopeFactory = scopeFactory;
        _eventBus = eventBus;
    }

    public async Task PublishAsync(CollaborationOutboxRecord message, CancellationToken cancellationToken)
    {
        await _eventBus.PublishAsync(new CollaborationRealtimeIntegrationEvent
        {
            EventId = message.EventId,
            TenantNId = message.TenantNId,
            SourceEventType = message.EventType,
            Payload = message.Payload,
            CreatedTime = message.CreatedOn,
        }, cancellationToken: cancellationToken);
        await PublishLocalAsync(message, cancellationToken);
    }

    /// <summary>Broadcasts an already-consumed event without publishing it again.</summary>
    public async Task PublishLocalAsync(CollaborationOutboxRecord message, CancellationToken cancellationToken)
    {
        if (string.Equals(message.EventType, "collaboration.read-cursor.advanced.v1", StringComparison.Ordinal))
        {
            await SendPayloadAsync("read.cursor.advanced", message, cancellationToken);
            return;
        }
        if (string.Equals(message.EventType, "collaboration.message.personal-hidden.v1", StringComparison.Ordinal))
        {
            using var hiddenPayload = JsonDocument.Parse(message.Payload);
            var hiddenRoot = hiddenPayload.RootElement;
            var userNId = GetString(hiddenRoot, "UserNId", "userNId")
                ?? throw new InvalidOperationException("Collaboration personal visibility payload has no user id.");
            await _hub.Clients.Group($"collaboration-user:{message.TenantNId}:{userNId}")
                .SendAsync("message.personal-hidden", JsonSerializer.Deserialize<JsonElement>(message.Payload), cancellationToken);
            return;
        }
        if (!string.Equals(message.EventType, "collaboration.message.accepted.v1", StringComparison.Ordinal)
            && !string.Equals(message.EventType, "collaboration.message.retracted.v1", StringComparison.Ordinal))
            return;

        using var payload = JsonDocument.Parse(message.Payload);
        var root = payload.RootElement;
        var conversationNId = GetString(root, "AcceptedConversationNId", "ConversationNId", "conversationNId")
            ?? throw new InvalidOperationException("Collaboration outbox payload has no conversation id.");
        var messageNId = GetString(root, "AcceptedMessageNId", "MessageNId", "messageNId")
            ?? throw new InvalidOperationException("Collaboration outbox payload has no message id.");

        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CollaborationService>();
        var projection = await service.GetRealtimeMessageAsync(message.TenantNId, messageNId, cancellationToken);
        if (projection is null) return;
        await SendToConversationUsersAsync(
            service,
            message.TenantNId,
            conversationNId,
            string.Equals(message.EventType, "collaboration.message.retracted.v1", StringComparison.Ordinal)
                ? "message.retracted"
                : "message.accepted",
            projection,
            cancellationToken);
    }

    private async Task SendPayloadAsync(string eventName, CollaborationOutboxRecord message, CancellationToken cancellationToken)
    {
        using var payload = JsonDocument.Parse(message.Payload);
        var root = payload.RootElement;
        var conversationNId = GetString(root, "ConversationNId", "conversationNId")
            ?? throw new InvalidOperationException("Collaboration outbox payload has no conversation id.");
        var value = JsonSerializer.Deserialize<JsonElement>(message.Payload);
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CollaborationService>();
        await SendToConversationUsersAsync(service, message.TenantNId, conversationNId, eventName, value, cancellationToken);
    }

    private async Task SendToConversationUsersAsync(
        CollaborationService service,
        string tenantNId,
        string conversationNId,
        string eventName,
        object payload,
        CancellationToken cancellationToken)
    {
        var users = await service.GetConversationParticipantUserNIdsAsync(tenantNId, conversationNId, cancellationToken);
        var groups = users.Select(userNId => $"collaboration-user:{tenantNId}:{userNId}").ToArray();
        if (groups.Length == 0)
            groups = [$"collaboration:{tenantNId}:{conversationNId}"];
        await _hub.Clients.Groups(groups).SendAsync(eventName, payload, cancellationToken);
    }

    private static string? GetString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        return null;
    }
}
