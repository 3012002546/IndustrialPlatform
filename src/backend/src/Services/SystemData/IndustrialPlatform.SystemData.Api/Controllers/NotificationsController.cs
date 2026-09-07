using IndustrialPlatform.Security;
using IndustrialPlatform.SystemData.Api.Authorization;
using IndustrialPlatform.SystemData.Application.Notifications;
using IndustrialPlatform.SystemData.Application.Pf04;
using IndustrialPlatform.SystemData.Contracts.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.SystemData.Api.Controllers;

[ApiController]
[Route("notifications")]
[Authorize]
public sealed class NotificationsController : SystemDataControllerBase
{
    private readonly INotificationService _service;

    public NotificationsController(INotificationService service, ICurrentUser currentUser) : base(currentUser) => _service = service;

    [HttpGet("announcements")]
    [Authorize(Policy = SystemDataPermissionPolicies.NotificationAnnouncementRead)]
    public async Task<ActionResult<IReadOnlyList<NotificationAnnouncementV1>>> ListAnnouncements([FromQuery] string? search, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out _)) return UnauthorizedEnvelope();
        return await Execute(() => _service.ListAnnouncementsAsync(tenant, search, cancellationToken));
    }

    [HttpPost("announcements")]
    [Authorize(Policy = SystemDataPermissionPolicies.NotificationAnnouncementManage)]
    public async Task<ActionResult<NotificationAnnouncementV1>> CreateAnnouncement(CreateAnnouncementRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        return await Execute(() => _service.CreateAnnouncementAsync(tenant, user, request, cancellationToken));
    }

    [HttpPut("announcements/{announcementNId}")]
    [Authorize(Policy = SystemDataPermissionPolicies.NotificationAnnouncementManage)]
    public async Task<ActionResult<NotificationAnnouncementV1>> UpdateAnnouncement(string announcementNId, UpdateAnnouncementRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        return await Execute(() => _service.UpdateAnnouncementAsync(tenant, user, announcementNId, request, cancellationToken));
    }

    [HttpPost("announcements/{announcementNId}/publish")]
    [Authorize(Policy = SystemDataPermissionPolicies.NotificationAnnouncementPublish)]
    public async Task<ActionResult<NotificationAnnouncementV1>> PublishAnnouncement(string announcementNId, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        return await Execute(() => _service.PublishAnnouncementAsync(tenant, user, announcementNId, cancellationToken));
    }

    [HttpPost("announcements/{announcementNId}/revoke")]
    [Authorize(Policy = SystemDataPermissionPolicies.NotificationAnnouncementManage)]
    public async Task<ActionResult<NotificationAnnouncementV1>> RevokeAnnouncement(string announcementNId, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        return await Execute(() => _service.RevokeAnnouncementAsync(tenant, user, announcementNId, cancellationToken));
    }

    [HttpPost("system-messages")]
    [Authorize(Policy = SystemDataPermissionPolicies.NotificationSystemSend)]
    public async Task<ActionResult<NotificationInboxItemV1>> SendSystemMessage(SystemMessageRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        return await Execute(() => _service.SendSystemMessageAsync(tenant, user, request, cancellationToken));
    }

    [HttpGet("inbox")]
    [Authorize(Policy = SystemDataPermissionPolicies.NotificationInboxRead)]
    public async Task<ActionResult<NotificationInboxPageV1>> Inbox([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        return await Execute(() => _service.GetInboxAsync(tenant, user, page, pageSize, cancellationToken));
    }

    [HttpGet("inbox/unread-count")]
    [Authorize(Policy = SystemDataPermissionPolicies.NotificationInboxRead)]
    public async Task<ActionResult<int>> UnreadCount(CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        try { return (await _service.GetInboxAsync(tenant, user, 1, 1, cancellationToken)).UnreadCount; }
        catch (Pf04ServiceException exception) { return StatusCodeEnvelope(exception.StatusCode, exception.Code, exception.Message); }
    }

    [HttpPut("inbox/{notificationNId}/read")]
    [Authorize(Policy = SystemDataPermissionPolicies.NotificationInboxRead)]
    public async Task<IActionResult> MarkRead(string notificationNId, MarkNotificationReadRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        try
        {
            if (!await _service.MarkReadAsync(tenant, user, notificationNId, request.Read ?? true, cancellationToken)) return NotFound();
            return OkEnvelope();
        }
        catch (Pf04ServiceException exception) { return StatusCodeEnvelope(exception.StatusCode, exception.Code, exception.Message); }
    }

    [HttpPost("inbox/read-batch")]
    [Authorize(Policy = SystemDataPermissionPolicies.NotificationInboxRead)]
    public async Task<ActionResult<int>> MarkManyRead(BatchReadNotificationsRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        try { return await _service.MarkManyReadAsync(tenant, user, request.NotificationNIds ?? [], cancellationToken); }
        catch (Pf04ServiceException exception) { return StatusCodeEnvelope(exception.StatusCode, exception.Code, exception.Message); }
    }

    private static async Task<ActionResult<T>> Execute<T>(Func<Task<T>> operation)
    {
        try { return await operation(); }
        catch (Pf04ServiceException exception) { return StatusCodeEnvelope(exception.StatusCode, exception.Code, exception.Message); }
    }
}
