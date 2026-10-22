using IndustrialPlatform.SystemData.Contracts.Files;
using IndustrialPlatform.SystemData.Application.Files;

namespace IndustrialPlatform.RuntimeSupport.Files;

/// <summary>浏览器仅提交用途；租户、服务和操作者始终由受信适配器注入。</summary>
public sealed record StudioFileUsageV1(string ModuleKey, string? ResourceNId, string Purpose);
public sealed record StudioFileOwnerContextV1(string ProducerServiceKey, string ModuleKey, string? ResourceNId, string Purpose, string ActorUserNId, string TenantNId);
public interface IStudioFileScopeAuthorizer
{
    Task AuthorizeAsync(TrustedServiceContext context, StudioFileUsageV1 usage, CancellationToken cancellationToken);
    Task AuthorizeReadAsync(TrustedServiceContext context, StudioFileUsageV1 usage, CancellationToken cancellationToken) => AuthorizeAsync(context, usage, cancellationToken);
}
/// <summary>允许的Studio文件公开传输子集；所有后续步骤重验已固化scope。</summary>
public interface IStudioFileTransfer
{
    Task<UploadSessionV1> CreateScopedSessionAsync(TrustedServiceContext context, CreateUploadSessionRequest request, StudioFileUsageV1 usage, CancellationToken cancellationToken);
    Task<UploadSessionV1> GetSessionAsync(TrustedServiceContext context, string sessionNId, CancellationToken cancellationToken);
    Task<UploadSessionV1> GetByTransportAsync(TrustedServiceContext context, string transportId, CancellationToken cancellationToken);
    Task<UploadSessionV1> SetContentHashAsync(TrustedServiceContext context, string sessionNId, ContentHashRequest request, CancellationToken cancellationToken);
    Task<UploadSessionV1> ResumeProofAsync(TrustedServiceContext context, string sessionNId, ResumeProofRequest request, CancellationToken cancellationToken);
    Task<UploadSessionV1> ResumeAsync(TrustedServiceContext context, string sessionNId, ResumeProofRequest request, CancellationToken cancellationToken);
    Task<UploadSessionV1> PauseAsync(TrustedServiceContext context, string sessionNId, CancellationToken cancellationToken);
    Task<UploadSessionV1> CancelAsync(TrustedServiceContext context, string sessionNId, SetUploadStateRequest request, CancellationToken cancellationToken);
    Task<UploadSessionV1> AppendAsync(TrustedServiceContext context, string transportId, long offset, int epoch, Stream content, string? ticket, CancellationToken cancellationToken);
    Task<FileObjectV1> CompleteAsync(TrustedServiceContext context, string sessionNId, CancellationToken cancellationToken);
    Task<FileObjectV1> GetFileAsync(TrustedServiceContext context, string fileNId, CancellationToken cancellationToken);
    Task<Stream> OpenContentAsync(TrustedServiceContext context, string fileNId, string? referenceNId, CancellationToken cancellationToken);
    Task<FileReferenceRecord> AddReferenceAsync(TrustedServiceContext context, string fileNId, string referenceNId, FileReferenceRequest request, CancellationToken cancellationToken);
    Task DeleteReferenceAsync(TrustedServiceContext context, string fileNId, string referenceNId, CancellationToken cancellationToken);
}
