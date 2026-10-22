using System.Text.Json;
using IndustrialPlatform.RuntimeSupport.Persistence;
using IndustrialPlatform.SystemData.Application.Files;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Application.Operations;
using IndustrialPlatform.SystemData.Contracts.Files;

namespace IndustrialPlatform.RuntimeSupport.Files;

/// <summary>同进程公开File受信门面。scope只在创建事务中由服务端固化，其余操作不能更改归属。</summary>
public sealed class ScopedFileTransfer(IFileService files, IFileStore store, FileAuditOperationalStore persistence,
    IStudioFileScopeAuthorizer authorization, ISystemDataWriteTransaction transaction, ILocalAuditCommand audit) : IStudioFileTransfer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<UploadSessionV1> CreateScopedSessionAsync(TrustedServiceContext context, CreateUploadSessionRequest request, StudioFileUsageV1 usage, CancellationToken cancellationToken)
    {
        Validate(context);
        await authorization.AuthorizeAsync(context, usage, cancellationToken);
        UploadSessionV1? result = null;
        await transaction.ExecuteAsync(async () =>
        {
            result = await files.CreateSessionAsync(context.TenantNId, context.ActorUserNId, request with { SessionNId = null, TransportId = null, Purpose = $"studio.{usage.ModuleKey}.{usage.Purpose}" }, cancellationToken);
            var owner = new StudioFileOwnerContextV1(context.ServiceKey, usage.ModuleKey, usage.ResourceNId, usage.Purpose, context.ActorUserNId, context.TenantNId);
            await persistence.SetOwnerContextAsync(context.TenantNId, result.SessionNId, JsonSerializer.Serialize(owner, Json), cancellationToken);
        }, cancellationToken);
        return result!;
    }
    public async Task<UploadSessionV1> GetSessionAsync(TrustedServiceContext context, string sessionNId, CancellationToken cancellationToken) { await Scope(context, sessionNId, false, cancellationToken); return await files.GetSessionAsync(context.TenantNId, context.ActorUserNId, sessionNId, cancellationToken); }
    public async Task<UploadSessionV1> GetByTransportAsync(TrustedServiceContext context, string transportId, CancellationToken cancellationToken) { await Scope(context, transportId, true, cancellationToken); return await files.GetSessionByTransportAsync(context.TenantNId, context.ActorUserNId, transportId, cancellationToken); }
    public async Task<UploadSessionV1> SetContentHashAsync(TrustedServiceContext context, string sessionNId, ContentHashRequest request, CancellationToken cancellationToken) { await Scope(context, sessionNId, false, cancellationToken); return await files.SetContentHashAsync(context.TenantNId, sessionNId, context.ActorUserNId, request, cancellationToken); }
    public async Task<UploadSessionV1> ResumeProofAsync(TrustedServiceContext context, string sessionNId, ResumeProofRequest request, CancellationToken cancellationToken) { await Scope(context, sessionNId, false, cancellationToken); return await files.ResumeProofAsync(context.TenantNId, sessionNId, context.ActorUserNId, request, cancellationToken); }
    public async Task<UploadSessionV1> ResumeAsync(TrustedServiceContext context, string sessionNId, ResumeProofRequest request, CancellationToken cancellationToken) { await Scope(context, sessionNId, false, cancellationToken); return await files.ResumeAsync(context.TenantNId, sessionNId, context.ActorUserNId, request, cancellationToken); }
    public async Task<UploadSessionV1> PauseAsync(TrustedServiceContext context, string sessionNId, CancellationToken cancellationToken) { await Scope(context, sessionNId, false, cancellationToken); return await files.PauseAsync(context.TenantNId, sessionNId, context.ActorUserNId, cancellationToken); }
    public async Task<UploadSessionV1> CancelAsync(TrustedServiceContext context, string sessionNId, SetUploadStateRequest request, CancellationToken cancellationToken) { await Scope(context, sessionNId, false, cancellationToken); return await files.CancelAsync(context.TenantNId, sessionNId, context.ActorUserNId, request, cancellationToken); }
    public async Task<UploadSessionV1> AppendAsync(TrustedServiceContext context, string transportId, long offset, int epoch, Stream content, string? ticket, CancellationToken cancellationToken) { await Scope(context, transportId, true, cancellationToken); return await files.AppendAsync(context.TenantNId, transportId, context.ActorUserNId, offset, epoch, content, ticket, cancellationToken); }
    public async Task<FileObjectV1> CompleteAsync(TrustedServiceContext context, string sessionNId, CancellationToken cancellationToken) { await Scope(context, sessionNId, false, cancellationToken); return await files.CompleteAsync(context.TenantNId, sessionNId, context.ActorUserNId, cancellationToken); }
    public async Task<FileObjectV1> GetFileAsync(TrustedServiceContext context, string fileNId, CancellationToken cancellationToken)
    {
        var file = await FileScope(context, fileNId, cancellationToken, readOnly: true);
        return await files.GetFileAsync(context.TenantNId, file.FileNId, cancellationToken) ?? throw Error();
    }
    public async Task<Stream> OpenContentAsync(TrustedServiceContext context, string fileNId, string? referenceNId, CancellationToken cancellationToken)
    {
        await FileScope(context, fileNId, cancellationToken, readOnly: true);
        await audit.RecordAsync(new(context.TenantNId, context.ActorUserNId, "file.studio.download", "File", fileNId, null, null, null, ""), cancellationToken);
        return await files.OpenFileContentAsync(context.TenantNId, context.ActorUserNId, fileNId, referenceNId, cancellationToken);
    }
    public async Task<FileReferenceRecord> AddReferenceAsync(TrustedServiceContext context, string fileNId, string referenceNId, FileReferenceRequest request, CancellationToken cancellationToken)
    {
        await FileScope(context, fileNId, cancellationToken);
        // Existing personal authorization is mandatory before creating another reference.
        await using (await files.OpenFileContentAsync(context.TenantNId, context.ActorUserNId, fileNId, null, cancellationToken)) { }
        return await files.AddReferenceAsync(context.TenantNId, context.ActorUserNId, fileNId, request with { ReferenceNId = referenceNId }, cancellationToken);
    }
    public async Task DeleteReferenceAsync(TrustedServiceContext context, string fileNId, string referenceNId, CancellationToken cancellationToken)
    {
        await FileScope(context, fileNId, cancellationToken);
        await files.DeleteReferenceAsync(context.TenantNId, context.ActorUserNId, fileNId, referenceNId, cancellationToken);
    }
    private async Task<FileObjectRecord> FileScope(TrustedServiceContext context, string fileNId, CancellationToken cancellationToken, bool readOnly = false)
    {
        Validate(context);
        var file = await store.GetFileAsync(context.TenantNId, fileNId, cancellationToken) ?? throw Error();
        if (readOnly && file.OwnerUserNId != context.ActorUserNId)
        {
            var reference = await store.GetReferenceForFileAsync(context.TenantNId, fileNId, context.ActorUserNId, cancellationToken);
            if (reference is null || reference.DeletedOn is not null || file.ScanStatus != "Clean" || file.Restricted || file.DeletionStatus != "Active") throw Error();
        }
        await Scope(context, file.UploadSessionNId, false, cancellationToken, readOnly);
        return file;
    }
    private async Task Scope(TrustedServiceContext context, string id, bool transportId, CancellationToken cancellationToken, bool readOnly = false)
    {
        Validate(context);
        var sessionNId = transportId ? await store.GetSessionByTransportAsync(context.TenantNId, id, cancellationToken) : await store.GetSessionAsync(context.TenantNId, id, cancellationToken);
        if (sessionNId?.OwnerContextJson is null || !readOnly && sessionNId.UploaderUserNId != context.ActorUserNId) throw Error();
        StudioFileOwnerContextV1? owner;
        try { owner = JsonSerializer.Deserialize<StudioFileOwnerContextV1>(sessionNId.OwnerContextJson, Json); }
        catch (JsonException) { throw Error(); }
        if (owner is null || owner.TenantNId != context.TenantNId || !readOnly && owner.ActorUserNId != context.ActorUserNId || owner.ProducerServiceKey != context.ServiceKey) throw Error();
        if (readOnly) await authorization.AuthorizeReadAsync(context, new(owner.ModuleKey, owner.ResourceNId, owner.Purpose), cancellationToken);
        else await authorization.AuthorizeAsync(context, new(owner.ModuleKey, owner.ResourceNId, owner.Purpose), cancellationToken);
    }
    private static void Validate(TrustedServiceContext context) { if (context.ServiceKey != "platform-studio" || string.IsNullOrWhiteSpace(context.TenantNId) || string.IsNullOrWhiteSpace(context.ActorUserNId)) throw Error(); }
    private static SystemDataOperationException Error() => new("FILE_ACCESS_DENIED", "当前用户没有该文件的用途授权。", 403);
}
