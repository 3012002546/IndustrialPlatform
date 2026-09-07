using IndustrialPlatform.Security;
using IndustrialPlatform.SystemData.Api.Authorization;
using IndustrialPlatform.SystemData.Application.Files;
using IndustrialPlatform.SystemData.Application.Pf04;
using IndustrialPlatform.SystemData.Contracts.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.SystemData.Api.Controllers;

[ApiController]
[Route("files")]
[Authorize]
public sealed class FilesController : SystemDataControllerBase
{
    private readonly IFileService _service;

    public FilesController(IFileService service, ICurrentUser currentUser) : base(currentUser) => _service = service;

    [HttpPost("upload-sessions")]
    [Authorize(Policy = SystemDataPermissionPolicies.FileUpload)]
    public async Task<ActionResult<UploadSessionV1>> CreateSession(CreateUploadSessionRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        return await Execute(() => _service.CreateSessionAsync(tenant, user, request, cancellationToken));
    }

    /// <summary>标准 tus 创建入口：Upload-Length + Upload-Metadata，传输偏移由 HEAD/PATCH 维护。</summary>
    [HttpPost("uploads")]
    [Authorize(Policy = SystemDataPermissionPolicies.FileUpload)]
    public async Task<IActionResult> CreateTusUpload(CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        if (!long.TryParse(Request.Headers["Upload-Length"], out var length) || length < 0)
            return BadRequestEnvelope("FILE_UPLOAD_PROTOCOL_INVALID", "缺少有效 Upload-Length。");
        var metadata = ParseTusMetadata(Request.Headers["Upload-Metadata"].FirstOrDefault());
        try
        {
            var session = await _service.CreateSessionAsync(tenant, user, new CreateUploadSessionRequest
            {
                FileName = metadata.GetValueOrDefault("filename", "upload.bin"),
                ContentType = metadata.GetValueOrDefault("filetype", "application/octet-stream"),
                Sha256 = metadata.GetValueOrDefault("sha256"),
                Purpose = metadata.GetValueOrDefault("purpose", "default"),
                Length = length,
            }, cancellationToken);
            Response.Headers["Tus-Resumable"] = "1.0.0";
            // Keep the external module prefix (UnifiedHost supplies it through
            // PathBase); the route itself is always /api/v1/files/uploads.
            Response.Headers["Location"] = $"{Request.PathBase}/api/v1/files/uploads/{Uri.EscapeDataString(session.TransportId)}";
            Response.Headers["Upload-Offset"] = "0";
            Response.Headers["Upload-Length"] = length.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return StatusCode(StatusCodes.Status201Created, session);
        }
        catch (Pf04ServiceException exception) { return StatusCodeEnvelope(exception.StatusCode, exception.Code, exception.Message); }
    }

    [HttpOptions("uploads/{transportId}")]
    [Authorize(Policy = SystemDataPermissionPolicies.FileUpload)]
    public IActionResult TusOptions(string transportId)
    {
        Response.Headers["Tus-Resumable"] = "1.0.0";
        Response.Headers["Tus-Version"] = "1.0.0";
        Response.Headers["Tus-Extension"] = "creation,creation-with-upload";
        Response.Headers["Tus-Max-Size"] = (1024L * 1024L * 1024L).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return NoContent();
    }

    [HttpPost("upload-sessions/discover")]
    [Authorize(Policy = SystemDataPermissionPolicies.FileUpload)]
    public async Task<ActionResult<FileUploadDiscoveryV1>> Discover(DiscoverUploadSessionRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        return await Execute(() => _service.DiscoverAsync(tenant, user, request, cancellationToken));
    }

    [HttpGet("upload-sessions/{sessionNId}")]
    [Authorize(Policy = SystemDataPermissionPolicies.FileUpload)]
    public async Task<ActionResult<UploadSessionV1>> GetSession(string sessionNId, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        return await Execute(() => _service.GetSessionAsync(tenant, user, sessionNId, cancellationToken));
    }

    [HttpPut("upload-sessions/{sessionNId}/content-hash")]
    [Authorize(Policy = SystemDataPermissionPolicies.FileUpload)]
    public async Task<ActionResult<UploadSessionV1>> SetContentHash(string sessionNId, ContentHashRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        return await Execute(() => _service.SetContentHashAsync(tenant, sessionNId, user, request, cancellationToken));
    }

    [HttpPost("upload-sessions/{sessionNId}/resume-proof")]
    [Authorize(Policy = SystemDataPermissionPolicies.FileUpload)]
    public async Task<ActionResult<UploadSessionV1>> ResumeProof(string sessionNId, ResumeProofRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        return await Execute(() => _service.ResumeProofAsync(tenant, sessionNId, user, request, cancellationToken));
    }

    [HttpPost("upload-sessions/{sessionNId}/takeover")]
    [Authorize(Policy = SystemDataPermissionPolicies.FileManage)]
    public async Task<ActionResult<UploadSessionV1>> Takeover(string sessionNId, TakeoverUploadRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        return await Execute(() => _service.TakeoverAsync(tenant, sessionNId, user, request, cancellationToken));
    }

    [HttpPost("upload-sessions/{sessionNId}/pause")]
    [Authorize(Policy = SystemDataPermissionPolicies.FileUpload)]
    public async Task<ActionResult<UploadSessionV1>> Pause(string sessionNId, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        return await Execute(() => _service.PauseAsync(tenant, sessionNId, user, cancellationToken));
    }

    [HttpPost("upload-sessions/{sessionNId}/resume")]
    [Authorize(Policy = SystemDataPermissionPolicies.FileUpload)]
    public async Task<ActionResult<UploadSessionV1>> Resume(string sessionNId, ResumeProofRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        return await Execute(() => _service.ResumeAsync(tenant, sessionNId, user, request, cancellationToken));
    }

    [HttpPost("upload-sessions/{sessionNId}/cancel")]
    [Authorize(Policy = SystemDataPermissionPolicies.FileUpload)]
    public async Task<ActionResult<UploadSessionV1>> Cancel(string sessionNId, SetUploadStateRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        return await Execute(() => _service.CancelAsync(tenant, sessionNId, user, request, cancellationToken));
    }

    [HttpPost("upload-sessions/{sessionNId}/complete")]
    [Authorize(Policy = SystemDataPermissionPolicies.FileUpload)]
    public async Task<ActionResult<FileObjectV1>> Complete(string sessionNId, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        return await Execute(() => _service.CompleteAsync(tenant, sessionNId, user, cancellationToken));
    }

    [HttpHead("uploads/{transportId}")]
    [Authorize(Policy = SystemDataPermissionPolicies.FileUpload)]
    public async Task<IActionResult> HeadUpload(string transportId, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        try
        {
            var session = await _service.GetSessionByTransportAsync(tenant, user, transportId, cancellationToken);
            Response.Headers["Tus-Resumable"] = "1.0.0";
            Response.Headers["Upload-Offset"] = session.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["Upload-Length"] = session.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["Upload-Epoch"] = session.WriterEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Ok();
        }
        catch (Pf04ServiceException exception) { return StatusCodeEnvelope(exception.StatusCode, exception.Code, exception.Message); }
    }

    [HttpPatch("uploads/{transportId}")]
    [Authorize(Policy = SystemDataPermissionPolicies.FileUpload)]
    public async Task<ActionResult<UploadSessionV1>> PatchUpload(string transportId, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        if (!long.TryParse(Request.Headers["Upload-Offset"], out var offset) || !int.TryParse(Request.Headers["Upload-Epoch"], out var epoch)) return BadRequestEnvelope("FILE_UPLOAD_PROTOCOL_INVALID", "缺少有效 Upload-Offset 或 Upload-Epoch。");
        var ticket = Request.Headers["X-Upload-Resume-Ticket"].FirstOrDefault();
        try
        {
            var session = await _service.AppendAsync(tenant, transportId, user, offset, epoch, Request.Body, ticket, cancellationToken);
            Response.Headers["Tus-Resumable"] = "1.0.0";
            Response.Headers["Upload-Offset"] = session.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["Upload-Epoch"] = session.WriterEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (session.ResumeTicket is not null) Response.Headers["X-Upload-Resume-Ticket"] = session.ResumeTicket;
            return session;
        }
        catch (Pf04ServiceException exception) { return StatusCodeEnvelope(exception.StatusCode, exception.Code, exception.Message); }
    }

    [HttpGet]
    [Authorize(Policy = SystemDataPermissionPolicies.FileRead)]
    public async Task<ActionResult<FilePageV1>> List([FromQuery] string? search, [FromQuery] string? purpose, [FromQuery] string? ownerUserNId, [FromQuery] string? scanStatus, [FromQuery] bool? restricted, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        return await Execute(() => _service.ListFilesPageAsync(tenant, search, purpose, ownerUserNId, scanStatus, restricted, page, pageSize, cancellationToken));
    }

    [HttpGet("{fileNId}")]
    [Authorize(Policy = SystemDataPermissionPolicies.FileRead)]
    public async Task<ActionResult<FileObjectV1>> Get(string fileNId, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out _)) return UnauthorizedEnvelope();
        var value = await _service.GetFileAsync(tenant, fileNId, cancellationToken);
        return value is null ? NotFound() : value;
    }

    [HttpGet("{fileNId}/content")]
    [Authorize(Policy = SystemDataPermissionPolicies.FileDownload)]
    public async Task<IActionResult> Content(string fileNId, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        try
        {
            var metadata = await _service.GetFileAsync(tenant, fileNId, cancellationToken);
            if (metadata is null) return NotFound();
            return File(await _service.OpenFileContentAsync(tenant, user, fileNId, Request.Query["referenceNId"].FirstOrDefault(), cancellationToken), metadata.ContentType, metadata.FileName, enableRangeProcessing: true);
        }
        catch (Pf04ServiceException exception) { return StatusCodeEnvelope(exception.StatusCode, exception.Code, exception.Message); }
    }

    [HttpPut("{fileNId}/references/{referenceNId}")]
    [Authorize(Policy = SystemDataPermissionPolicies.FileManage)]
    public async Task<ActionResult<FileReferenceRecord>> AddReference(string fileNId, string referenceNId, FileReferenceRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        return await Execute(() => _service.AddReferenceAsync(tenant, user, fileNId, request with { ReferenceNId = referenceNId }, cancellationToken));
    }

    [HttpDelete("{fileNId}/references/{referenceNId}")]
    [Authorize(Policy = SystemDataPermissionPolicies.FileManage)]
    public async Task<IActionResult> DeleteReference(string fileNId, string referenceNId, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        try { await _service.DeleteReferenceAsync(tenant, user, fileNId, referenceNId, cancellationToken); return OkEnvelope(); }
        catch (Pf04ServiceException exception) { return StatusCodeEnvelope(exception.StatusCode, exception.Code, exception.Message); }
    }

    [HttpPost("{fileNId}/deletion-requests")]
    [Authorize(Policy = SystemDataPermissionPolicies.FileDelete)]
    public async Task<ActionResult<FileObjectV1>> RequestDeletion(string fileNId, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        return await Execute(() => _service.RequestDeletionAsync(tenant, user, fileNId, cancellationToken));
    }

    [HttpPut("{fileNId}/restrictions")]
    [Authorize(Policy = SystemDataPermissionPolicies.FileManage)]
    public async Task<ActionResult<FileObjectV1>> SetRestriction(string fileNId, FileRestrictionRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var user)) return UnauthorizedEnvelope();
        return await Execute(() => _service.SetRestrictionAsync(tenant, user, fileNId, request, cancellationToken));
    }

    private static async Task<ActionResult<T>> Execute<T>(Func<Task<T>> operation)
    {
        try { return await operation(); }
        catch (Pf04ServiceException exception) { return StatusCodeEnvelope(exception.StatusCode, exception.Code, exception.Message); }
    }

    private static Dictionary<string, string> ParseTusMetadata(string? value)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value)) return result;
        foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = item.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) continue;
            try { result[parts[0]] = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[1])); }
            catch (FormatException) { }
        }
        return result;
    }
}
