using System.Net.Http.Json;
using System.Text.Json;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Security;
using IndustrialPlatform.SystemData.Application.Files;
using IndustrialPlatform.SystemData.Contracts.Files;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.Infrastructure;

/// <summary>通过受信服务断言调用 SystemData 文件边界。</summary>
public sealed class HttpSystemDataFilePort : ICollaborationFilePort
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IHttpClientFactory _clients;
    private readonly ITrustedServiceCallSigner _signer;
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpSystemDataFilePort(IHttpClientFactory clients, ITrustedServiceCallSigner signer, IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
    {
        _clients = clients;
        _signer = signer;
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<AttachmentUploadResult> CreateUploadAsync(string tenantNId, string userNId, AttachmentRecord attachment, CancellationToken cancellationToken)
    {
        var result = await SendJsonAsync<UploadSessionV1>(HttpMethod.Post, "upload-sessions", new CreateUploadSessionRequest
        {
            SessionNId = attachment.AttachmentNId,
            TransportId = "collab-" + attachment.AttachmentNId,
            FileName = attachment.FileNameSnapshot,
            ContentType = attachment.ContentTypeSnapshot,
            Length = attachment.SizeSnapshot,
            Purpose = CollaborationServiceConstants.AttachmentPurpose,
        }, tenantNId, userNId, "file.upload.create", attachment.AttachmentNId, cancellationToken);
        return ToUploadResult(result);
    }

    public async Task<AttachmentUploadResult> GetUploadAsync(string tenantNId, string userNId, string sessionNId, CancellationToken cancellationToken) =>
        ToUploadResult(await SendJsonAsync<UploadSessionV1>(HttpMethod.Get, $"upload-sessions/{Escape(sessionNId)}", null, tenantNId, userNId, "file.upload.read", sessionNId, cancellationToken));

    public async Task<AttachmentUploadResult> SetContentHashAsync(string tenantNId, string userNId, string sessionNId, string sha256, CancellationToken cancellationToken) =>
        ToUploadResult(await SendJsonAsync<UploadSessionV1>(HttpMethod.Put, $"upload-sessions/{Escape(sessionNId)}/content-hash", new ContentHashRequest { Sha256 = sha256 }, tenantNId, userNId, "file.upload.hash", sessionNId, cancellationToken));

    public async Task<AttachmentUploadResult> ResumeProofAsync(string tenantNId, string userNId, string sessionNId, int writerEpoch, string proof, CancellationToken cancellationToken) =>
        ToUploadResult(await SendJsonAsync<UploadSessionV1>(HttpMethod.Post, $"upload-sessions/{Escape(sessionNId)}/resume-proof", new ResumeProofRequest { WriterEpoch = writerEpoch, Proof = proof }, tenantNId, userNId, "file.upload.resume-proof", sessionNId, cancellationToken));

    public async Task<AttachmentUploadResult> TakeoverAsync(string tenantNId, string userNId, string sessionNId, int expectedWriterEpoch, string? idempotencyKey, string? proof, CancellationToken cancellationToken) =>
        ToUploadResult(await SendJsonAsync<UploadSessionV1>(HttpMethod.Post, $"upload-sessions/{Escape(sessionNId)}/takeover", new TakeoverUploadRequest { ExpectedWriterEpoch = expectedWriterEpoch, IdempotencyKey = idempotencyKey, Proof = proof }, tenantNId, userNId, "file.upload.takeover", sessionNId, cancellationToken));

    public async Task<AttachmentUploadResult> PauseAsync(string tenantNId, string userNId, string sessionNId, CancellationToken cancellationToken) =>
        ToUploadResult(await SendJsonAsync<UploadSessionV1>(HttpMethod.Post, $"upload-sessions/{Escape(sessionNId)}/pause", null, tenantNId, userNId, "file.upload.pause", sessionNId, cancellationToken));

    public async Task<AttachmentUploadResult> ResumeAsync(string tenantNId, string userNId, string sessionNId, int writerEpoch, string proof, CancellationToken cancellationToken) =>
        ToUploadResult(await SendJsonAsync<UploadSessionV1>(HttpMethod.Post, $"upload-sessions/{Escape(sessionNId)}/resume", new ResumeProofRequest { WriterEpoch = writerEpoch, Proof = proof }, tenantNId, userNId, "file.upload.resume", sessionNId, cancellationToken));

    public async Task<AttachmentUploadResult> CancelAsync(string tenantNId, string userNId, string sessionNId, string? reason, CancellationToken cancellationToken) =>
        ToUploadResult(await SendJsonAsync<UploadSessionV1>(HttpMethod.Post, $"upload-sessions/{Escape(sessionNId)}/cancel", new SetUploadStateRequest { Reason = reason }, tenantNId, userNId, "file.upload.cancel", sessionNId, cancellationToken));

    public async Task<AttachmentUploadResult> AppendAsync(string tenantNId, string userNId, string transportId, long expectedOffset, int writerEpoch, Stream content, string? resumeTicket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        await using var buffered = new MemoryStream();
        await content.CopyToAsync(buffered, cancellationToken);
        var result = await SendRawAsync<UploadSessionV1>(HttpMethod.Patch, $"uploads/{Escape(transportId)}", buffered.ToArray(), tenantNId, userNId, "file.upload.append", transportId, request =>
        {
            request.Headers.TryAddWithoutValidation("X-Upload-Offset", expectedOffset.ToString(System.Globalization.CultureInfo.InvariantCulture));
            request.Headers.TryAddWithoutValidation("X-Upload-Epoch", writerEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(resumeTicket)) request.Headers.TryAddWithoutValidation("X-Upload-Resume-Ticket", resumeTicket);
        }, cancellationToken);
        return ToUploadResult(result);
    }

    public async Task<AttachmentUploadResult> CompleteUploadAsync(string tenantNId, string userNId, string sessionNId, CancellationToken cancellationToken) =>
        ToUploadResult(await SendJsonAsync<FileObjectV1>(HttpMethod.Post, $"upload-sessions/{Escape(sessionNId)}/complete", null, tenantNId, userNId, "file.upload.complete", sessionNId, cancellationToken), sessionNId);

    public async Task<string?> AddReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, CancellationToken cancellationToken, string? purpose = null)
    {
        _ = await SendJsonAsync<FileReferenceRecord>(HttpMethod.Put, $"{Escape(fileNId)}/references/{Escape(referenceNId)}", new FileReferenceRequest { ReferenceNId = referenceNId, Purpose = purpose ?? CollaborationServiceConstants.AttachmentPurpose }, tenantNId, userNId, "file.reference.add", referenceNId, cancellationToken);
        return referenceNId;
    }

    public async Task<string?> BindReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, string conversationNId, string messageNId, string attachmentNId, string uploaderUserNId, string purpose, string requestNId, CancellationToken cancellationToken)
    {
        _ = await SendJsonAsync<FileBindingV1>(HttpMethod.Put, $"references/{Escape(referenceNId)}", new FileBindingRequest
        {
            RequestNId = requestNId,
            FileNId = fileNId,
            ConversationNId = conversationNId,
            MessageNId = messageNId,
            AttachmentNId = attachmentNId,
            UploaderUserNId = uploaderUserNId,
            Purpose = purpose,
        }, tenantNId, userNId, "file.reference.bind", requestNId, cancellationToken);
        return referenceNId;
    }

    public async Task ReleaseReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, long expectedVersion, string requestNId, CancellationToken cancellationToken)
    {
        _ = await SendJsonAsync<FileBindingV1>(HttpMethod.Post, $"references/{Escape(referenceNId)}/release", new FileBindingReleaseRequest { RequestNId = requestNId, ExpectedVersion = expectedVersion }, tenantNId, userNId, "file.reference.release", requestNId, cancellationToken);
    }

    public async Task<ExportArtifactResult> CreateExportArtifactAsync(string tenantNId, string userNId, string exportNId, string fileName, string contentType, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        await using var buffered = new MemoryStream();
        await content.CopyToAsync(buffered, cancellationToken);
        return await SendRawAsync<ExportArtifactResult>(HttpMethod.Post, $"exports/{Escape(exportNId)}", buffered.ToArray(), tenantNId, userNId, "file.export-artifact.create", exportNId, request =>
        {
            request.Headers.TryAddWithoutValidation("X-PF05-File-Name", fileName);
            request.Content!.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        }, cancellationToken);
    }

    public Task<string?> AddExportReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, CancellationToken cancellationToken) =>
        BindExportReferenceAsync(tenantNId, userNId, fileNId, referenceNId, referenceNId, referenceNId, cancellationToken);

    public async Task<string?> BindExportReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, string conversationNId, string exportNId, CancellationToken cancellationToken)
    {
        _ = await SendJsonAsync<FileBindingV1>(HttpMethod.Put, $"references/{Escape(referenceNId)}", new FileBindingRequest
        {
            RequestNId = $"export-reference-{exportNId}",
            FileNId = fileNId,
            ConversationNId = conversationNId,
            MessageNId = exportNId,
            AttachmentNId = exportNId,
            UploaderUserNId = userNId,
            Purpose = CollaborationServiceConstants.ComplianceExportPurpose,
        }, tenantNId, userNId, "file.reference.bind", $"export-reference-{exportNId}", cancellationToken);
        return referenceNId;
    }

    public async Task ReleaseExportReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, CancellationToken cancellationToken)
    {
        try
        {
            _ = await SendJsonAsync<FileBindingV1>(HttpMethod.Post, $"references/{Escape(referenceNId)}/release", new FileBindingReleaseRequest
            {
                RequestNId = $"export-release-{referenceNId}",
                ExpectedVersion = 0,
            }, tenantNId, userNId, "file.reference.release", $"export-release-{referenceNId}", cancellationToken);
        }
        catch (CollaborationException exception) when (exception.StatusCode == StatusCodes.Status404NotFound)
        {
            await DeleteReferenceAsync(tenantNId, userNId, fileNId, referenceNId, "file.export-reference.release", cancellationToken);
        }
    }

    public async Task AddLegalHoldReferenceAsync(string tenantNId, string userNId, string fileNId, string holdCaseNId, CancellationToken cancellationToken)
    {
        var referenceNId = $"HOLD-{holdCaseNId}-{fileNId}";
        await AddReferenceWithPurposeAsync(tenantNId, userNId, fileNId, referenceNId, CollaborationServiceConstants.ComplianceLegalHoldPurpose, "file.legal-hold-reference.add", cancellationToken);
    }

    public async Task AddLegalHoldReferenceAsync(string tenantNId, string userNId, string fileNId, string holdCaseNId, string requestNId, string scopeChecksum, long caseRevision, CancellationToken cancellationToken)
    {
        _ = await SendJsonAsync<FileHoldV1>(HttpMethod.Put, $"holds/{Escape(holdCaseNId)}/files/{Escape(fileNId)}", new FileHoldRequest { RequestNId = requestNId, ScopeChecksum = scopeChecksum, CaseRevision = caseRevision }, tenantNId, userNId, "file.hold.put", requestNId, cancellationToken);
    }

    public async Task ReleaseLegalHoldReferenceAsync(string tenantNId, string userNId, string fileNId, string holdCaseNId, CancellationToken cancellationToken)
    {
        var referenceNId = $"HOLD-{holdCaseNId}-{fileNId}";
        await DeleteReferenceAsync(tenantNId, userNId, fileNId, referenceNId, "file.legal-hold-reference.release", cancellationToken);
    }

    public async Task ReleaseLegalHoldReferenceAsync(string tenantNId, string userNId, string fileNId, string holdCaseNId, string requestNId, string scopeChecksum, long caseRevision, CancellationToken cancellationToken)
    {
        _ = await SendJsonAsync<FileHoldV1>(HttpMethod.Post, $"holds/{Escape(holdCaseNId)}/files/{Escape(fileNId)}/release", new FileHoldRequest { RequestNId = requestNId, ScopeChecksum = scopeChecksum, CaseRevision = caseRevision }, tenantNId, userNId, "file.hold.release", requestNId, cancellationToken);
    }

    public async Task<CollaborationFileState?> GetAsync(string tenantNId, string userNId, string fileNId, CancellationToken cancellationToken)
    {
        var result = await SendJsonOrNullAsync<FileObjectV1>(HttpMethod.Get, Escape(fileNId), null, tenantNId, userNId, "file.read", fileNId, cancellationToken);
        return ToFileState(result);
    }

    public async Task<CollaborationFileState?> GetForReconciliationAsync(string tenantNId, string fileNId, CancellationToken cancellationToken)
    {
        var worker = _configuration["Collaboration:ServiceIdentity:WorkerUserNId"] ?? "collaboration-worker";
        var result = await SendJsonOrNullAsync<FileObjectV1>(HttpMethod.Get, $"{Escape(fileNId)}/reconciliation", null, tenantNId, worker, "file.reconcile", fileNId, cancellationToken);
        return ToFileState(result);
    }

    public async Task<Stream> OpenContentAsync(string tenantNId, string userNId, string fileNId, string referenceNId, CancellationToken cancellationToken)
    {
        var path = $"{Escape(fileNId)}/content?referenceNId={Uri.EscapeDataString(referenceNId)}";
        var response = await SendResponseAsync(HttpMethod.Get, path, ReadOnlyMemory<byte>.Empty, tenantNId, userNId, "file.download", fileNId, null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            response.Dispose();
            throw new CollaborationException(statusCode, "COLLAB_SYSTEMDATA_FILE_UNAVAILABLE", "SystemData 文件内容调用失败。");
        }
        return new ResponseStream(await response.Content.ReadAsStreamAsync(cancellationToken), response);
    }

    private async Task<string?> AddReferenceWithPurposeAsync(string tenantNId, string userNId, string fileNId, string referenceNId, string purpose, string action, CancellationToken cancellationToken)
    {
        _ = await SendJsonAsync<FileReferenceRecord>(HttpMethod.Put, $"{Escape(fileNId)}/references/{Escape(referenceNId)}", new FileReferenceRequest { ReferenceNId = referenceNId, Purpose = purpose }, tenantNId, userNId, action, referenceNId, cancellationToken);
        return referenceNId;
    }

    private async Task DeleteReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, string action, CancellationToken cancellationToken)
    {
        using var response = await SendResponseAsync(HttpMethod.Delete, $"{Escape(fileNId)}/references/{Escape(referenceNId)}", ReadOnlyMemory<byte>.Empty, tenantNId, userNId, action, referenceNId, null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task<T> SendJsonAsync<T>(HttpMethod method, string path, object? body, string tenantNId, string actorUserNId, string action, string requestNId, CancellationToken cancellationToken)
    {
        var bytes = body is null ? Array.Empty<byte>() : JsonSerializer.SerializeToUtf8Bytes(body);
        using var response = await SendResponseAsync(method, path, bytes, tenantNId, actorUserNId, action, requestNId, request => request.Content!.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json"), cancellationToken);
        return await ReadDataAsync<T>(response, cancellationToken);
    }

    private async Task<T?> SendJsonOrNullAsync<T>(HttpMethod method, string path, object? body, string tenantNId, string actorUserNId, string action, string requestNId, CancellationToken cancellationToken)
    {
        var bytes = body is null ? Array.Empty<byte>() : JsonSerializer.SerializeToUtf8Bytes(body);
        using var response = await SendResponseAsync(method, path, bytes, tenantNId, actorUserNId, action, requestNId, request => request.Content!.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json"), cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return default;
        return await ReadDataAsync<T>(response, cancellationToken);
    }

    private async Task<T> SendRawAsync<T>(HttpMethod method, string path, byte[] body, string tenantNId, string actorUserNId, string action, string requestNId, Action<HttpRequestMessage>? configure, CancellationToken cancellationToken)
    {
        using var response = await SendResponseAsync(method, path, body, tenantNId, actorUserNId, action, requestNId, configure, cancellationToken);
        return await ReadDataAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendResponseAsync(HttpMethod method, string path, ReadOnlyMemory<byte> body, string tenantNId, string actorUserNId, string action, string requestNId, Action<HttpRequestMessage>? configure, CancellationToken cancellationToken)
    {
        var baseUrl = _configuration["Collaboration:SystemData:BaseUrl"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme is not ("https" or "http"))
            throw new CollaborationException(503, "COLLAB_SYSTEMDATA_FILE_UNAVAILABLE", "SystemData 服务内部地址未配置。");
        var request = new HttpRequestMessage(method, new Uri(baseUri, "/internal/pf05/systemdata/files/" + path))
        {
            Content = new ByteArrayContent(body.ToArray()),
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        configure?.Invoke(request);
        var assertionPath = request.RequestUri!.PathAndQuery.TrimStart('/');
        var actorContext = CollaborationHttpActorContext.Require(_httpContextAccessor, _configuration, tenantNId, actorUserNId);
        var assertion = _signer.Sign(method, assertionPath, body, CollaborationServiceConstants.InternalSystemDataAudience, tenantNId, actorUserNId, actorContext.SessionNId, actorContext.SecurityVersion, action, requestNId);
        request.Headers.TryAddWithoutValidation(TrustedServiceCallValidator.HeaderName, assertion);
        try
        {
            return await _clients.CreateClient("Collaboration.SystemData").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            request.Dispose();
            throw;
        }
        catch (HttpRequestException)
        {
            request.Dispose();
            throw new CollaborationException(503, "COLLAB_SYSTEMDATA_FILE_UNAVAILABLE", "SystemData 文件服务不可用。");
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
            throw new CollaborationException((int)response.StatusCode, "COLLAB_SYSTEMDATA_FILE_UNAVAILABLE", "SystemData 文件调用失败。");
        var envelope = await response.Content.ReadFromJsonAsync<InternalApiResult<T>>(JsonOptions, cancellationToken);
        if (envelope is null || !envelope.Success || envelope.Data is null)
            throw new CollaborationException(503, "COLLAB_SYSTEMDATA_FILE_UNAVAILABLE", "SystemData 未返回有效文件结果。");
        return envelope.Data;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
            throw new CollaborationException((int)response.StatusCode, "COLLAB_SYSTEMDATA_FILE_UNAVAILABLE", "SystemData 文件调用失败。");
        var envelope = await response.Content.ReadFromJsonAsync<InternalApiResult<ApiResultMarker>>(JsonOptions, cancellationToken);
        if (envelope is null || !envelope.Success)
            throw new CollaborationException(503, "COLLAB_SYSTEMDATA_FILE_UNAVAILABLE", "SystemData 未返回有效文件结果。");
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private static CollaborationFileState? ToFileState(FileObjectV1? file) => file is null
        ? null
        : new CollaborationFileState(file.FileNId, file.FileName, file.ContentType, file.Length, file.ScanStatus, file.Restricted, file.DeletionStatus, file.LastUpdatedOn);

    private static AttachmentUploadResult ToUploadResult(UploadSessionV1 session) => new(session.SessionNId, session.TransportId, session.ExpiresOn, session.FileName, session.ContentType, session.Length, session.Offset, session.WriterEpoch, session.Status, session.ResumeTicket, session.FileNId, session.Sha256);

    private static AttachmentUploadResult ToUploadResult(FileObjectV1 file, string sessionNId) => new(sessionNId, null, file.RetentionUntil ?? DateTimeOffset.UtcNow, file.FileName, file.ContentType, file.Length, file.Length, null, file.ScanStatus, null, file.FileNId, file.Sha256);

    private sealed record InternalApiResult<T>(bool Success, string? Code, string? Message, T? Data);
    private sealed record ApiResultMarker;

    private sealed class ResponseStream : Stream
    {
        private readonly Stream _inner;
        private readonly HttpResponseMessage _response;

        public ResponseStream(Stream inner, HttpResponseMessage response)
        {
            _inner = inner;
            _response = response;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            _response.Dispose();
            await base.DisposeAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _response.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
