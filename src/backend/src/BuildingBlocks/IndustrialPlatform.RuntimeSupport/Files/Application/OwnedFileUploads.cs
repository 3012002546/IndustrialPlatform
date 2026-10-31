using IndustrialPlatform.SystemData.Application.Files;
using IndustrialPlatform.SystemData.Contracts.Files;
using System.Diagnostics;

namespace IndustrialPlatform.RuntimeSupport.Files;

/// <summary>复用owned受理、断点证明、上传及提交；扫描等待与业务成功由消费方决定。</summary>
public static class OwnedFileUploads
{
    public sealed record Result(OwnedFilePreparedV1 Prepared, FileObjectV1 File, OwnedFileStatusV1 Reference);
    public static async Task<Result> CommitAsync(IOwnedFileService owned, IFileService files, TrustedServiceContext context, OwnedFilePrepareV1 request, Stream content, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanSeek || content.Length != request.ExpectedLength) throw new ArgumentException("Owned upload requires a seekable stream with the declared length.", nameof(content));
        var prepared = await owned.PrepareAsync(context, request, token);
        var session = await files.GetSessionAsync(context.TenantNId, context.ActorUserNId, prepared.UploadSessionNId, token);
        if (session.Status != "Completed")
        {
            session = await files.ResumeProofAsync(context.TenantNId, prepared.UploadSessionNId, context.ActorUserNId, new() { WriterEpoch = session.WriterEpoch, Proof = request.ExpectedSha256 }, token);
            content.Position = session.Offset;
            await files.AppendAsync(context.TenantNId, session.TransportId, context.ActorUserNId, session.Offset, session.WriterEpoch, content, session.ResumeTicket, token);
        }
        var file = await files.CompleteAsync(context.TenantNId, prepared.UploadSessionNId, context.ActorUserNId, token);
        return new(prepared, file, await owned.CommitAsync(context, new(prepared.ReferenceNId, file.FileNId, request.ExpectedSha256), token));
    }

    /// <summary>等待同一上传的扫描/引用确认，不重复上传，也不把Pending当成功。</summary>
    public static async Task<OwnedFileStatusV1> WaitForConfirmationAsync(IOwnedFileService owned, TrustedServiceContext context, Result upload, TimeSpan timeout, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var confirmed = upload.Reference;
        var started = Stopwatch.GetTimestamp();
        while (confirmed.Status != "Active" && Stopwatch.GetElapsedTime(started) < timeout)
        {
            if (confirmed.Status is "Rejected" or "Revoked" or "Deleted" or "Released") break;
            var remaining = timeout - Stopwatch.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero) break;
            await Task.Delay(remaining < TimeSpan.FromMilliseconds(500) ? remaining : TimeSpan.FromMilliseconds(500), token);
            token.ThrowIfCancellationRequested();
            confirmed = await owned.CommitAsync(context, new(upload.Prepared.ReferenceNId, upload.File.FileNId, upload.File.Sha256), token);
        }
        return confirmed;
    }
}
