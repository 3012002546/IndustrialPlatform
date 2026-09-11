using System.Security.Cryptography;
using IndustrialPlatform.Security;
using IndustrialPlatform.SystemData.Application.Files;
using IndustrialPlatform.SystemData.Application.Pf04;
using IndustrialPlatform.SystemData.Contracts.Files;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.SystemData.Api.Controllers;

/// <summary>Collaboration 使用的固定 SystemData 文件内部边界。</summary>
[ApiController]
[AllowAnonymous]
[Route("~/internal/pf05/systemdata/files")]
public sealed class CollaborationSystemDataFileInternalController : ControllerBase
{
    private const string AttachmentPurpose = "CollaborationMessageAttachment";
    private const string ExportPurpose = "CollaborationComplianceExport";
    private readonly TrustedServiceCallValidator _trustedCalls;
    private readonly ITrustedServiceCallNonceStore _nonceStore;
    private readonly IFileService _files;

    public CollaborationSystemDataFileInternalController(
        TrustedServiceCallValidator trustedCalls,
        ITrustedServiceCallNonceStore nonceStore,
        IFileService files)
    {
        _trustedCalls = trustedCalls;
        _nonceStore = nonceStore;
        _files = files;
    }

    [HttpPost("upload-sessions")]
    public async Task<IActionResult> CreateSession(CreateUploadSessionRequest request, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync("file.upload.create", cancellationToken);
        if (validation.Failure is not null) return validation.Failure;
        if (!string.Equals(request.Purpose, AttachmentPurpose, StringComparison.Ordinal))
            return UnprocessableEntity(ApiResult.Fail<object?>("FILE_BUSINESS_PURPOSE_INVALID", "协作附件上传会话用途无效。"));
        return await Execute(() => _files.CreateSessionAsync(validation.Call!.TenantNId, validation.Call.ActorUserNId, request, cancellationToken));
    }

    [HttpGet("upload-sessions/{sessionNId}")]
    public async Task<IActionResult> GetSession(string sessionNId, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync("file.upload.read", cancellationToken);
        if (validation.Failure is not null) return validation.Failure;
        return await Execute(() => _files.GetSessionAsync(validation.Call!.TenantNId, validation.Call.ActorUserNId, sessionNId, cancellationToken));
    }

    [HttpPut("upload-sessions/{sessionNId}/content-hash")]
    public async Task<IActionResult> SetContentHash(string sessionNId, ContentHashRequest request, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync("file.upload.hash", cancellationToken);
        if (validation.Failure is not null) return validation.Failure;
        return await Execute(() => _files.SetContentHashAsync(validation.Call!.TenantNId, sessionNId, validation.Call.ActorUserNId, request, cancellationToken));
    }

    [HttpPost("upload-sessions/{sessionNId}/resume-proof")]
    public async Task<IActionResult> ResumeProof(string sessionNId, ResumeProofRequest request, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync("file.upload.resume-proof", cancellationToken);
        if (validation.Failure is not null) return validation.Failure;
        return await Execute(() => _files.ResumeProofAsync(validation.Call!.TenantNId, sessionNId, validation.Call.ActorUserNId, request, cancellationToken));
    }

    [HttpPost("upload-sessions/{sessionNId}/takeover")]
    public async Task<IActionResult> Takeover(string sessionNId, TakeoverUploadRequest request, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync("file.upload.takeover", cancellationToken);
        if (validation.Failure is not null) return validation.Failure;
        return await Execute(() => _files.TakeoverAsync(validation.Call!.TenantNId, sessionNId, validation.Call.ActorUserNId, request, cancellationToken));
    }

    [HttpPost("upload-sessions/{sessionNId}/pause")]
    public async Task<IActionResult> Pause(string sessionNId, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync("file.upload.pause", cancellationToken);
        if (validation.Failure is not null) return validation.Failure;
        return await Execute(() => _files.PauseAsync(validation.Call!.TenantNId, sessionNId, validation.Call.ActorUserNId, cancellationToken));
    }

    [HttpPost("upload-sessions/{sessionNId}/resume")]
    public async Task<IActionResult> Resume(string sessionNId, ResumeProofRequest request, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync("file.upload.resume", cancellationToken);
        if (validation.Failure is not null) return validation.Failure;
        return await Execute(() => _files.ResumeAsync(validation.Call!.TenantNId, sessionNId, validation.Call.ActorUserNId, request, cancellationToken));
    }

    [HttpPost("upload-sessions/{sessionNId}/cancel")]
    public async Task<IActionResult> Cancel(string sessionNId, SetUploadStateRequest request, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync("file.upload.cancel", cancellationToken);
        if (validation.Failure is not null) return validation.Failure;
        return await Execute(() => _files.CancelAsync(validation.Call!.TenantNId, sessionNId, validation.Call.ActorUserNId, request, cancellationToken));
    }

    [HttpPatch("uploads/{transportId}")]
    public async Task<IActionResult> Append(string transportId, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync("file.upload.append", cancellationToken);
        if (validation.Failure is not null) return validation.Failure;
        if (!long.TryParse(Request.Headers["X-Upload-Offset"], out var offset)
            || !int.TryParse(Request.Headers["X-Upload-Epoch"], out var epoch))
            return BadRequest(ApiResult.Fail<object?>("FILE_UPLOAD_PROTOCOL_INVALID", "缺少有效 X-Upload-Offset 或 X-Upload-Epoch。"));
        var ticket = Request.Headers["X-Upload-Resume-Ticket"].FirstOrDefault();
        return await Execute(() => _files.AppendAsync(validation.Call!.TenantNId, transportId, validation.Call.ActorUserNId, offset, epoch, Request.Body, ticket, cancellationToken));
    }

    [HttpPost("upload-sessions/{sessionNId}/complete")]
    public async Task<IActionResult> Complete(string sessionNId, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync("file.upload.complete", cancellationToken);
        if (validation.Failure is not null) return validation.Failure;
        return await Execute(() => _files.CompleteAsync(validation.Call!.TenantNId, sessionNId, validation.Call.ActorUserNId, cancellationToken));
    }

    [HttpGet("{fileNId}")]
    public async Task<IActionResult> Get(string fileNId, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync("file.read", cancellationToken);
        if (validation.Failure is not null) return validation.Failure;
        var result = await _files.GetFileAsync(validation.Call!.TenantNId, fileNId, cancellationToken);
        return result is null ? NotFound() : Ok(ApiResult.Ok(result));
    }

    [HttpGet("{fileNId}/reconciliation")]
    public async Task<IActionResult> GetForReconciliation(string fileNId, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync("file.reconcile", cancellationToken);
        if (validation.Failure is not null) return validation.Failure;
        var result = await _files.GetFileForReconciliationAsync(validation.Call!.TenantNId, fileNId, cancellationToken);
        return result is null ? NotFound() : Ok(ApiResult.Ok(result));
    }

    [HttpGet("{fileNId}/content")]
    public async Task<IActionResult> Content(string fileNId, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync("file.download", cancellationToken);
        if (validation.Failure is not null) return validation.Failure;
        try
        {
            var metadata = await _files.GetFileAsync(validation.Call!.TenantNId, fileNId, cancellationToken);
            if (metadata is null) return NotFound();
            var referenceNId = Request.Query["referenceNId"].FirstOrDefault();
            return File(await _files.OpenFileContentAsync(validation.Call.TenantNId, validation.Call.ActorUserNId, fileNId, referenceNId, cancellationToken), metadata.ContentType, metadata.FileName, enableRangeProcessing: true);
        }
        catch (Pf04ServiceException exception)
        {
            return StatusCode(exception.StatusCode, ApiResult.Fail<object?>(exception.Code, exception.Message));
        }
    }

    [HttpPut("references/{referenceNId}")]
    public async Task<IActionResult> BindReference(string referenceNId, FileBindingRequest request, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync("file.reference.bind", cancellationToken);
        if (validation.Failure is not null) return validation.Failure;
        if (!string.Equals(request.RequestNId, validation.Call!.RequestNId, StringComparison.Ordinal))
            return Conflict(ApiResult.Fail<object?>("FILE_BUSINESS_REFERENCE_CONFLICT", "业务文件引用请求与可信调用上下文不一致。"));
        return await Execute(() => _files.BindReferenceAsync(validation.Call.TenantNId, validation.Call.ActorUserNId, referenceNId, request, cancellationToken));
    }

    [HttpPost("references/{referenceNId}/release")]
    public async Task<IActionResult> ReleaseReference(string referenceNId, FileBindingReleaseRequest request, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync("file.reference.release", cancellationToken);
        if (validation.Failure is not null) return validation.Failure;
        if (!string.Equals(request.RequestNId, validation.Call!.RequestNId, StringComparison.Ordinal))
            return Conflict(ApiResult.Fail<object?>("FILE_BUSINESS_REFERENCE_CONFLICT", "业务文件引用请求与可信调用上下文不一致。"));
        return await Execute(() => _files.ReleaseReferenceAsync(validation.Call.TenantNId, validation.Call.ActorUserNId, string.Empty, referenceNId, request, cancellationToken));
    }

    [HttpPut("holds/{caseNId}/files/{fileNId}")]
    public async Task<IActionResult> PutLegalHold(string caseNId, string fileNId, FileHoldRequest request, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync("file.hold.put", cancellationToken);
        if (validation.Failure is not null) return validation.Failure;
        if (!string.Equals(request.RequestNId, validation.Call!.RequestNId, StringComparison.Ordinal))
            return Conflict(ApiResult.Fail<object?>("FILE_LEGAL_HOLD_CONFLICT", "文件 Legal Hold 请求与可信调用上下文不一致。"));
        return await Execute(() => _files.PutLegalHoldAsync(validation.Call.TenantNId, validation.Call.ActorUserNId, caseNId, fileNId, request, cancellationToken));
    }

    [HttpPost("holds/{caseNId}/files/{fileNId}/release")]
    public async Task<IActionResult> ReleaseLegalHold(string caseNId, string fileNId, FileHoldRequest request, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync("file.hold.release", cancellationToken);
        if (validation.Failure is not null) return validation.Failure;
        if (!string.Equals(request.RequestNId, validation.Call!.RequestNId, StringComparison.Ordinal))
            return Conflict(ApiResult.Fail<object?>("FILE_LEGAL_HOLD_CONFLICT", "文件 Legal Hold 请求与可信调用上下文不一致。"));
        return await Execute(() => _files.ReleaseLegalHoldAsync(validation.Call.TenantNId, validation.Call.ActorUserNId, caseNId, fileNId, request, cancellationToken));
    }

    [HttpGet("holds/{caseNId}/files/{fileNId}")]
    public async Task<IActionResult> GetLegalHold(string caseNId, string fileNId, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync("file.hold.read", cancellationToken);
        if (validation.Failure is not null) return validation.Failure;
        return await Execute(() => _files.GetLegalHoldAsync(validation.Call!.TenantNId, validation.Call.ActorUserNId, caseNId, fileNId, cancellationToken));
    }

    [HttpPut("{fileNId}/references/{referenceNId}")]
    public async Task<IActionResult> AddReference(string fileNId, string referenceNId, FileReferenceRequest request, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync("file.reference.add", cancellationToken);
        if (validation.Failure is not null) return validation.Failure;
        return await Execute(() => _files.AddReferenceAsync(validation.Call!.TenantNId, validation.Call.ActorUserNId, fileNId, request with { ReferenceNId = referenceNId }, cancellationToken));
    }

    [HttpDelete("{fileNId}/references/{referenceNId}")]
    public async Task<IActionResult> DeleteReference(string fileNId, string referenceNId, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync("file.reference.delete", cancellationToken);
        if (validation.Failure is not null) return validation.Failure;
        return await Execute(async () =>
        {
            await _files.DeleteReferenceAsync(validation.Call!.TenantNId, validation.Call.ActorUserNId, fileNId, referenceNId, cancellationToken);
            return ApiResult.Ok();
        });
    }

    [HttpPost("exports/{exportNId}")]
    public async Task<IActionResult> CreateExportArtifact(string exportNId, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync("file.export-artifact.create", cancellationToken);
        if (validation.Failure is not null) return validation.Failure;
        var fileName = Request.Headers["X-PF05-File-Name"].FirstOrDefault() ?? "export.bin";
        var contentType = Request.Headers.ContentType.ToString();
        if (string.IsNullOrWhiteSpace(contentType)) contentType = "application/octet-stream";
        return await Execute(() => CreateExportArtifactAsync(validation.Call!, exportNId, fileName, contentType, cancellationToken));
    }

    private async Task<ValidationResult> ValidateAsync(string action, CancellationToken cancellationToken)
    {
        var result = await _trustedCalls.ValidateAsync(Request, "systemdata.pf05", action, _nonceStore, cancellationToken);
        if (result.Status == TrustedServiceCallValidationStatus.Valid && result.Call is not null)
            return new ValidationResult(result.Call, null);
        var status = result.Status switch
        {
            TrustedServiceCallValidationStatus.Replayed => StatusCodes.Status401Unauthorized,
            TrustedServiceCallValidationStatus.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status401Unauthorized,
        };
        var code = result.Status switch
        {
            TrustedServiceCallValidationStatus.Replayed => "PF05_SERVICE_CALL_REPLAYED",
            TrustedServiceCallValidationStatus.Unavailable => "PF05_SERVICE_AUTH_UNAVAILABLE",
            _ => "PF05_SERVICE_CALL_INVALID",
        };
        return new ValidationResult(null, StatusCode(status, ApiResult.Fail<object?>(code, "PF05 service assertion is invalid.")));
    }

    private static async Task<IActionResult> Execute<T>(Func<Task<T>> operation)
    {
        try { return new OkObjectResult(ApiResult.Ok(await operation())); }
        catch (Pf04ServiceException exception) { return new ObjectResult(ApiResult.Fail<object?>(exception.Code, exception.Message)) { StatusCode = exception.StatusCode }; }
    }

    private async Task<ExportArtifactResponse> CreateExportArtifactAsync(
        TrustedCollaborationCall call,
        string exportNId,
        string fileName,
        string contentType,
        CancellationToken cancellationToken)
    {
        await using var buffered = new MemoryStream();
        await Request.Body.CopyToAsync(buffered, cancellationToken);
        var bytes = buffered.ToArray();
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var sessionNId = $"{exportNId}-file";
        UploadSessionV1 session;
        try
        {
            session = await _files.CreateSessionAsync(call.TenantNId, call.ActorUserNId, new CreateUploadSessionRequest
            {
                SessionNId = sessionNId,
                TransportId = $"collab-export-{exportNId}",
                FileName = fileName,
                ContentType = contentType,
                Length = bytes.LongLength,
                Sha256 = sha256,
                Purpose = ExportPurpose,
            }, cancellationToken);
        }
        catch
        {
            session = await _files.GetSessionAsync(call.TenantNId, call.ActorUserNId, sessionNId, cancellationToken);
        }

        if (session.Status == "Completed" && session.FileNId is { Length: > 0 })
        {
            var existing = await _files.GetFileAsync(call.TenantNId, session.FileNId, cancellationToken)
                ?? throw new InvalidOperationException("导出上传会话已完成但文件对象不存在。");
            return ToExportArtifact(existing);
        }

        var ready = session.Status is "Ready" or "Uploading"
            ? session
            : await _files.SetContentHashAsync(call.TenantNId, session.SessionNId, call.ActorUserNId, new ContentHashRequest { Sha256 = sha256 }, cancellationToken);
        await using var payload = new MemoryStream(bytes, writable: false);
        await _files.AppendAsync(call.TenantNId, ready.TransportId, call.ActorUserNId, ready.Offset, ready.WriterEpoch, payload, ready.ResumeTicket, cancellationToken);
        return ToExportArtifact(await _files.CompleteAsync(call.TenantNId, ready.SessionNId, call.ActorUserNId, cancellationToken));
    }

    private static ExportArtifactResponse ToExportArtifact(FileObjectV1 file) =>
        new(file.FileNId, file.FileName, file.ContentType, file.Length, file.ScanStatus);

    private sealed record ValidationResult(TrustedCollaborationCall? Call, IActionResult? Failure);
    private sealed record ExportArtifactResponse(string FileNId, string FileName, string ContentType, long Length, string ScanStatus);
}
