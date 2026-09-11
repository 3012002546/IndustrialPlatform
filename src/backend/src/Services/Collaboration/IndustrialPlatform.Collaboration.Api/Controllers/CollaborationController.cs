using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Security;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.Collaboration.Api.Controllers;

[ApiController]
[Route("collaboration")]
[Route("~/collaboration/api/v1")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CollaborationController : ControllerBase
{
    private readonly CollaborationService _service;
    private readonly ICurrentUser _current;

    public CollaborationController(CollaborationService service, ICurrentUser current)
    {
        _service = service;
        _current = current;
    }

    [HttpGet("users")]
    [Authorize(Policy = "permission:collaboration.messaging.conversation.start")]
    public async Task<IActionResult> SearchUsers([FromQuery] string? keyword, [FromQuery] string? cursor, [FromQuery] int limit = 20, CancellationToken cancellationToken = default) =>
        await ExecuteAsync(() => _service.SearchUsersAsync(Tenant, Actor, keyword ?? string.Empty, cursor, limit, cancellationToken));

    [HttpPost("conversations")]
    [Authorize(Policy = "permission:collaboration.messaging.conversation.start")]
    public async Task<IActionResult> CreateConversation(CreateConversationRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _service.CreateConversationAsync(Tenant, Actor, request, cancellationToken));

    [HttpGet("conversations")]
    [Authorize(Policy = "permission:collaboration.messaging.read")]
    public async Task<IActionResult> ListConversations([FromQuery] string? cursor, [FromQuery] int? limit = null, [FromQuery] int? pageSize = null, [FromQuery] string visibility = "Visible", [FromQuery] bool unreadOnly = false, CancellationToken cancellationToken = default) =>
        await ExecuteAsync(() => _service.ListConversationsAsync(Tenant, Actor, cursor, limit ?? pageSize ?? 30, visibility, unreadOnly, cancellationToken));

    [HttpGet("conversations/{conversationNId}")]
    [Authorize(Policy = "permission:collaboration.messaging.read")]
    public async Task<IActionResult> GetConversation(string conversationNId, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _service.GetConversationAsync(Tenant, Actor, conversationNId, cancellationToken));

    [HttpPost("conversations/{conversationNId}/messages")]
    [Authorize(Policy = "permission:collaboration.messaging.write")]
    public async Task<IActionResult> SendMessage(string conversationNId, SendMessageRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _service.SendMessageAsync(Tenant, Actor, conversationNId, request, cancellationToken));

    [HttpGet("conversations/{conversationNId}/messages")]
    [Authorize(Policy = "permission:collaboration.messaging.read")]
    public async Task<IActionResult> GetMessages(
        string conversationNId,
        [FromQuery] string mode = "history",
        [FromQuery] string? cursor = null,
        [FromQuery] long? sequence = null,
        [FromQuery] long? afterSequence = null,
        [FromQuery] long? fromSequence = null,
        [FromQuery] long? toSequence = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] int? limit = null,
        CancellationToken cancellationToken = default) =>
        await ExecuteAsync(() => _service.GetMessagesAsync(Tenant, Actor, conversationNId, mode, cursor, sequence, afterSequence, fromSequence, toSequence, limit ?? pageSize ?? 50, cancellationToken));

    [HttpGet("messages/by-client/{clientMessageNId}")]
    [Authorize(Policy = "permission:collaboration.messaging.read")]
    public async Task<IActionResult> GetMessageByClient(string clientMessageNId, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _service.GetMessageByClientAsync(Tenant, Actor, clientMessageNId, cancellationToken));

    [HttpPut("conversations/{conversationNId}/read-cursor")]
    [Authorize(Policy = "permission:collaboration.messaging.read-cursor.update")]
    public async Task<IActionResult> MarkRead(string conversationNId, ReadCursorRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _service.MarkReadAsync(Tenant, Actor, conversationNId, request, cancellationToken));

    [HttpPost("conversations/{conversationNId}/hide")]
    [Authorize(Policy = "permission:collaboration.messaging.conversation.hide")]
    public async Task<IActionResult> Hide(string conversationNId, HideConversationRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _service.HideAsync(Tenant, Actor, conversationNId, request, cancellationToken));

    [HttpPost("conversations/{conversationNId}/restore")]
    [Authorize(Policy = "permission:collaboration.messaging.conversation.restore")]
    public async Task<IActionResult> Restore(string conversationNId, HideConversationRequest? request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _service.RestoreAsync(Tenant, Actor, conversationNId, request, cancellationToken));

    [HttpPost("conversations/{conversationNId}/messages/{messageNId}/retract")]
    [Authorize(Policy = "permission:collaboration.messaging.retract")]
    public async Task<IActionResult> Retract(string conversationNId, string messageNId, RetractMessageRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _service.RetractAsync(Tenant, Actor, conversationNId, messageNId, request, cancellationToken));

    [HttpPut("conversations/{conversationNId}/messages/{messageNId}/personal-visibility")]
    [Authorize(Policy = "permission:collaboration.messaging.write")]
    public async Task<IActionResult> SetPersonalMessageVisibility(string conversationNId, string messageNId, PersonalMessageVisibilityRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _service.SetPersonalMessageVisibilityAsync(Tenant, Actor, conversationNId, messageNId, request, cancellationToken));

    [HttpPost("messages/{messageNId}/retract")]
    [Authorize(Policy = "permission:collaboration.messaging.retract")]
    public async Task<IActionResult> RetractMessage(string messageNId, RetractMessageRequest request, CancellationToken cancellationToken)
        => await ExecuteAsync(() => _service.RetractAsync(Tenant, Actor, messageNId, request, cancellationToken));

    [HttpPost("conversations/{conversationNId}/attachments/intents")]
    [Authorize(Policy = "permission:collaboration.messaging.attachment.send")]
    public async Task<IActionResult> CreateAttachmentIntent(string conversationNId, AttachmentIntentRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _service.CreateAttachmentIntentAsync(Tenant, Actor, conversationNId, request, cancellationToken), StatusCodes.Status201Created);

    [HttpPost("attachments/intents")]
    [Authorize(Policy = "permission:collaboration.messaging.attachment.send")]
    public async Task<IActionResult> CreateAttachmentIntentFromBody(AttachmentIntentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConversationNId))
            return BadRequest(ApiResult.Fail<object?>("COLLAB_CONVERSATION_REQUIRED", "附件意图必须提供会话标识。"));
        return await ExecuteAsync(() => _service.CreateAttachmentIntentAsync(Tenant, Actor, request.ConversationNId, request, cancellationToken), StatusCodes.Status201Created);
    }

    [HttpGet("attachments/{attachmentNId}/upload-session")]
    [Authorize(Policy = "permission:collaboration.messaging.attachment.send")]
    public async Task<IActionResult> GetAttachmentUploadSession(string attachmentNId, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _service.GetAttachmentUploadAsync(Tenant, Actor, attachmentNId, cancellationToken));

    [HttpPut("attachments/{attachmentNId}/upload-session/content-hash")]
    [Authorize(Policy = "permission:collaboration.messaging.attachment.send")]
    public async Task<IActionResult> SetAttachmentContentHash(string attachmentNId, AttachmentContentHashRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _service.SetAttachmentContentHashAsync(Tenant, Actor, attachmentNId, request.Sha256 ?? string.Empty, cancellationToken));

    [HttpPost("attachments/{attachmentNId}/upload-session/resume-proof")]
    [Authorize(Policy = "permission:collaboration.messaging.attachment.send")]
    public async Task<IActionResult> ResumeAttachmentProof(string attachmentNId, AttachmentResumeProofRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _service.ResumeAttachmentProofAsync(Tenant, Actor, attachmentNId, request.WriterEpoch, request.Proof ?? string.Empty, cancellationToken));

    [HttpPost("attachments/{attachmentNId}/upload-session/takeover")]
    [Authorize(Policy = "permission:collaboration.messaging.attachment.send")]
    public async Task<IActionResult> TakeoverAttachmentUpload(string attachmentNId, AttachmentTakeoverRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _service.TakeoverAttachmentUploadAsync(Tenant, Actor, attachmentNId, request.ExpectedWriterEpoch, request.IdempotencyKey, request.Proof, cancellationToken));

    [HttpPost("attachments/{attachmentNId}/upload-session/pause")]
    [Authorize(Policy = "permission:collaboration.messaging.attachment.send")]
    public async Task<IActionResult> PauseAttachmentUpload(string attachmentNId, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _service.PauseAttachmentUploadAsync(Tenant, Actor, attachmentNId, cancellationToken));

    [HttpPost("attachments/{attachmentNId}/upload-session/resume")]
    [Authorize(Policy = "permission:collaboration.messaging.attachment.send")]
    public async Task<IActionResult> ResumeAttachmentUpload(string attachmentNId, AttachmentResumeProofRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _service.ResumeAttachmentUploadAsync(Tenant, Actor, attachmentNId, request.WriterEpoch, request.Proof ?? string.Empty, cancellationToken));

    [HttpPost("attachments/{attachmentNId}/upload-session/cancel")]
    [Authorize(Policy = "permission:collaboration.messaging.attachment.send")]
    public async Task<IActionResult> CancelAttachmentUpload(string attachmentNId, AttachmentCancelRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _service.CancelAttachmentUploadAsync(Tenant, Actor, attachmentNId, request.Reason, cancellationToken));

    [HttpPost("attachments/{attachmentNId}/upload-session/complete")]
    [Authorize(Policy = "permission:collaboration.messaging.attachment.send")]
    public async Task<IActionResult> CompleteAttachmentUpload(string attachmentNId, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _service.CompleteAttachmentUploadAsync(Tenant, Actor, attachmentNId, cancellationToken));

    [HttpHead("attachments/{attachmentNId}/upload")]
    [Authorize(Policy = "permission:collaboration.messaging.attachment.send")]
    public async Task<IActionResult> HeadAttachmentUpload(string attachmentNId, CancellationToken cancellationToken)
    {
        try
        {
            var session = await _service.GetAttachmentUploadAsync(Tenant, Actor, attachmentNId, cancellationToken);
            Response.Headers["Upload-Offset"] = session.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["Upload-Length"] = session.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["Upload-Writer-Epoch"] = session.WriterEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return NoContent();
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return MapException(exception); }
    }

    [HttpPatch("attachments/{attachmentNId}/upload")]
    [Authorize(Policy = "permission:collaboration.messaging.attachment.send")]
    public async Task<IActionResult> AppendAttachmentUpload(string attachmentNId, [FromHeader(Name = "Upload-Transport-Id")] string? transportId, [FromHeader(Name = "Upload-Offset")] long? expectedOffset, [FromHeader(Name = "Upload-Writer-Epoch")] int? writerEpoch, [FromHeader(Name = "Upload-Resume-Ticket")] string? resumeTicket, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(transportId) || expectedOffset is null || writerEpoch is null)
            return BadRequest(ApiResult.Fail<object?>("COLLAB_UPLOAD_HEADERS_REQUIRED", "上传需要传输标识、偏移量和写入者代次。"));
        return await ExecuteAsync(() => _service.AppendAttachmentUploadAsync(Tenant, Actor, attachmentNId, transportId, expectedOffset.Value, writerEpoch.Value, resumeTicket, Request.Body, cancellationToken));
    }

    [HttpPost("conversations/{conversationNId}/attachments/{attachmentNId}/authorization")]
    [Authorize(Policy = "permission:collaboration.messaging.attachment.send")]
    public async Task<IActionResult> AuthorizeAttachment(string conversationNId, string attachmentNId, AttachmentAuthorizationRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _service.AuthorizeAttachmentAsync(Tenant, Actor, conversationNId, attachmentNId, request, cancellationToken));

    [HttpGet("attachments/{attachmentNId}")]
    [Authorize(Policy = "permission:collaboration.messaging.read")]
    public async Task<IActionResult> GetAttachment(string attachmentNId, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _service.GetAttachmentAsync(Tenant, Actor, attachmentNId, cancellationToken));

    [HttpPost("attachments/{attachmentNId}/authorizations")]
    [Authorize(Policy = "permission:collaboration.messaging.attachment.download")]
    public async Task<IActionResult> AuthorizeAttachmentForMember(string attachmentNId, AttachmentAuthorizationRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _service.AuthorizeAttachmentAsync(Tenant, Actor, attachmentNId, request, cancellationToken));

    [HttpGet("attachments/{attachmentNId}/content")]
    [Authorize(Policy = "permission:collaboration.messaging.attachment.download")]
    public async Task<IActionResult> GetAttachmentContent(string attachmentNId, [FromQuery] string? referenceNId, CancellationToken cancellationToken)
    {
        try
        {
            var content = await _service.OpenAttachmentContentAsync(Tenant, Actor, attachmentNId, referenceNId, cancellationToken);
            Response.Headers.CacheControl = "no-store";
            Response.Headers.Pragma = "no-cache";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(content.Content, content.ContentType, content.FileName, enableRangeProcessing: true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return MapException(exception);
        }
    }

    [HttpPut("presence")]
    [Authorize(Policy = "permission:collaboration.presence.write")]
    public IActionResult SetPresence(PresenceUpdateRequest request) => Execute(() => _service.SetPresence(Tenant, Actor, request, "http"));

    [HttpGet("presence/{userNId}")]
    [Authorize(Policy = "permission:collaboration.presence.read")]
    public IActionResult GetPresence(string userNId) => Execute(() => _service.GetPresence(Tenant, userNId));

    private string Tenant => _current.TenantId ?? throw new UnauthorizedAccessException();
    private string Actor => _current.UserNId ?? throw new UnauthorizedAccessException();

    private IActionResult Execute<T>(Func<T> action)
    {
        try { return Ok(ApiResult.Ok(action())); }
        catch (Exception exception) { return MapException(exception); }
    }

    private async Task<IActionResult> ExecuteAsync<T>(Func<Task<T>> action, int successStatus = StatusCodes.Status200OK)
    {
        try
        {
            var result = await action();
            return successStatus == StatusCodes.Status200OK ? Ok(ApiResult.Ok(result)) : StatusCode(successStatus, ApiResult.Ok(result));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return MapException(exception);
        }
    }

    private static ObjectResult MapException(Exception exception) => exception switch
    {
        CollaborationException collaboration => new ObjectResult(ApiResult.Fail<object?>(collaboration.Code, collaboration.Message)) { StatusCode = collaboration.StatusCode },
        UnauthorizedAccessException => new ObjectResult(ApiResult.Fail<object?>("COLLAB_UNAUTHORIZED", "当前会话未认证。")) { StatusCode = StatusCodes.Status401Unauthorized },
        ArgumentException argument => new ObjectResult(ApiResult.Fail<object?>("COLLAB_VALIDATION_FAILED", argument.Message)) { StatusCode = StatusCodes.Status400BadRequest },
        _ => new ObjectResult(ApiResult.Fail<object?>("COLLAB_SERVICE_UNAVAILABLE", "协作服务暂不可用。")) { StatusCode = StatusCodes.Status503ServiceUnavailable },
    };
}
